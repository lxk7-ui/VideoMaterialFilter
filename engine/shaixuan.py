# -*- coding: utf-8 -*-
"""
素材筛选器 识别引擎
检查文件夹里的视频：画面里有抖音号/抖音水印、画面字幕写了价格、语音里说了价格，
就挪到「已筛掉」文件夹，并在「筛选记录.csv」里写明原因和出现的时间点。

界面在 素材筛选器.exe 里，这个文件只负责识别。窗口程序这样启动它：
    python shaixuan.py --worker -      任务参数放在环境变量 SUCAI_JOB 里（JSON）
识别进程把进度一行一行写到标准输出，每行是「类型<Tab>内容」：
    LOG 文字 / QUEUE {"todo":n} / FILE 相对路径 / PROG {"stage":"画面","pct":37}
    RESULT {...} / DONE 相对路径 / IDLE / FAIL 原因
"""
import os
import sys

# 这两行必须在加载识别组件之前：
# 识别组件里有两套一样的运算库，Windows 上同时加载会直接闪退
os.environ.setdefault('KMP_DUPLICATE_LIB_OK', 'TRUE')
# 换别的语音模型时才会联网下载，国内走镜像
os.environ.setdefault('HF_ENDPOINT', 'https://hf-mirror.com')
os.environ.setdefault('HF_HUB_DISABLE_SYMLINKS_WARNING', '1')
# 新版下载组件默认走另一套下载服务器，会绕开国内镜像导致下不动，关掉
os.environ.setdefault('HF_HUB_DISABLE_XET', '1')

import re
import csv
import json
import time
import queue
import shutil
import threading
import traceback
import unicodedata
import configparser
from datetime import datetime

APP_DIR = os.path.dirname(os.path.abspath(__file__))
# 运行环境和语音模型所在的文件夹（窗口程序会设好）
HOME_DIR = os.environ.get('SUCAI_HOME') or APP_DIR
# 设置和运行日志放在软件根目录，和 素材筛选器.exe 在一起
DATA_DIR = os.environ.get('SUCAI_DATA') or APP_DIR
CONFIG_PATH = os.path.join(DATA_DIR, '设置.ini')
LOG_PATH = os.path.join(DATA_DIR, '运行日志.txt')
MODEL_DIR = os.path.join(HOME_DIR, 'models')
VIDEO_EXT = ('.mp4', '.mov', '.webm', '.m4v', '.mkv', '.flv')
REJECT_DIR = '已筛掉'
REPORT_NAME = '筛选记录.csv'
REPORT_HEAD = ['检查时间', '文件', '结果', '原因', '命中内容', '出现位置']

DEFAULT_INI = """; 素材筛选器设置。一般在软件里点「筛选规则」改就行，也可以直接改这个文件。
; 多个词用英文逗号隔开。

[画面]
; 画面里出现这些字就筛掉（抖音水印、抖音号）
抖音关键词 = 抖音号,抖音ID,抖音,douyin
; 画面里出现「@用户名」也算抖音号：是 / 否
查@用户名 = 是
; 每隔几秒看一帧画面。越小越仔细，也越慢
间隔秒数 = 1
; 文字识别的可信度门槛，0~1。越高越不容易误判，但也可能漏掉
可信度 = 0.6

[价格]
; 画面字幕和语音里出现这些词，就算说了价格
; 「¥19.9」「9.9元」「九块九」「十九块钱」这类说法程序会自动识别，不用写在这里
价格关键词 = 价格,价钱,到手价,券后,原价,现价,售价,特价,秒杀价,活动价,优惠价,多少钱,几块钱

[语音]
; 语音识别模型：tiny / base / small / medium。越大越准，也越慢
; 软件里已经带了 small；换成别的，第一次用会联网下载
模型 = small
; 语音里说到上面的抖音关键词也筛掉：是 / 否
也查抖音关键词 = 否

[其他]
; 找到一个问题就不再往下查（快很多）：是 / 否
找到就停 = 是
"""


# ---------------------------------------------------------------- 设置
def _split_words(s):
    return [w.strip() for w in re.split(r'[,，、;；\n]', s or '') if w.strip()]


def _yes(s):
    return str(s).strip() in ('是', '1', 'true', 'True', 'yes', '开')


def _num(s, default):
    try:
        return float(str(s).strip())
    except (TypeError, ValueError):
        return default


class Config:
    def __init__(self):
        if not os.path.exists(CONFIG_PATH):
            try:
                with open(CONFIG_PATH, 'w', encoding='utf-8-sig') as f:
                    f.write(DEFAULT_INI)
            except OSError:
                pass
        cp = configparser.ConfigParser(interpolation=None)
        cp.optionxform = str
        cp.read_string(DEFAULT_INI)
        for enc in ('utf-8-sig', 'gbk'):  # 用记事本另存为 ANSI 也能读
            try:
                with open(CONFIG_PATH, encoding=enc) as f:
                    cp.read_string(f.read())
                break
            except (OSError, UnicodeDecodeError, configparser.Error):
                continue

        self.douyin_words = [norm(w).lower() for w in _split_words(cp.get('画面', '抖音关键词'))]
        self.check_at = _yes(cp.get('画面', '查@用户名'))
        self.interval = max(0.2, _num(cp.get('画面', '间隔秒数'), 1.0))
        self.min_score = min(0.99, max(0.0, _num(cp.get('画面', '可信度'), 0.6)))
        self.price_words = [norm(w) for w in _split_words(cp.get('价格', '价格关键词'))]
        self.model = (cp.get('语音', '模型') or 'small').strip()
        self.audio_douyin = _yes(cp.get('语音', '也查抖音关键词'))
        self.stop_early = _yes(cp.get('其他', '找到就停'))


# ---------------------------------------------------------------- 文字判断
# 繁体常见字转简体（语音识别偶尔会出繁体）
T2S = str.maketrans('價塊錢圓現優殺動幾號後億萬兩', '价块钱圆现优杀动几号后亿万两')


def norm(t):
    t = unicodedata.normalize('NFKC', t or '').translate(T2S)  # 全角数字、￥ 转成普通写法
    return re.sub(r'\s+', '', t)


_C = '[零一二两三四五六七八九十百千万]'
_NOT_PRICE = '(?![旦素宵件头状])'  # 元旦、元素、一块头……不是价格
PRICE_PATTERNS = [re.compile(p) for p in [
    r'¥\s?\d',                                        # ¥19.9
    r'\d+(?:\.\d+)?\s?(?:元|圆)' + _NOT_PRICE,         # 9.9元
    r'\d+(?:\.\d+)?\s?块钱',                           # 19块钱
    r'\d+\.\d+\s?块',                                  # 9.9块
    r'\d{2,}\s?块' + _NOT_PRICE,                       # 19块
    r'(?:\d|%s)\s?块\s?(?:\d|%s)(?!\s?块)' % (_C, _C),  # 九块九、19块9（排除「一块一块」）
    r'%s{2,}\s?(?:元|块钱|块)' % _C + _NOT_PRICE,      # 十九块、二十元
    r'%s\s?(?:元|块钱)' % _C + _NOT_PRICE,             # 九块钱、五元
]]
AT_PATTERN = re.compile(r'@[\w\u4e00-\u9fff]{2,}')


def find_douyin(text, cfg):
    low = text.lower()
    for w in cfg.douyin_words:
        if w and w in low:
            return w
    if cfg.check_at:
        m = AT_PATTERN.search(text)
        if m:
            return m.group()
    return None


def find_price(text, cfg):
    for w in cfg.price_words:
        if w and w in text:
            return w
    for p in PRICE_PATTERNS:
        m = p.search(text)
        if m:
            return m.group()
    return None


def fmt_t(sec):
    sec = int(sec or 0)
    return '%d:%02d' % (sec // 60, sec % 60)


# ---------------------------------------------------------------- 识别引擎
def cpu_plan():
    """怎么分 CPU：(文字识别开几份, 每份几个线程, 语音识别几个线程)
    识别组件默认把所有核都用上，在现在常见的大小核 CPU 上线程之间互相等，反而慢一倍多。
    实测每份 4 个线程最快；核多的电脑就多开几份，同时认不同的帧。"""
    n = os.cpu_count() or 4
    workers = 1 if n <= 8 else (2 if n <= 12 else 3)
    threads = 2 if n <= 4 else 4
    env = os.environ.get  # 开发时用来对比不同分法
    return (int(env('SUCAI_OCR_WORKERS') or workers), int(env('SUCAI_OCR_THREADS') or threads),
            int(env('SUCAI_ASR_THREADS') or threads))


class OcrEngine:
    def __init__(self, threads=4):
        import onnxruntime  # noqa: F401  必须比 cv2 先加载，否则在部分电脑上会直接崩溃
        try:
            from rapidocr_onnxruntime import RapidOCR
        except ImportError:
            from rapidocr import RapidOCR
        try:
            self.engine = RapidOCR(intra_op_num_threads=threads)
        except TypeError:
            self.engine = RapidOCR()
        # 视频里的字基本都是正着的，不用先判断文字方向，每帧能省不少时间
        self.kw = {'use_cls': False}

    def __call__(self, img, min_score):
        try:
            out = self.engine(img, **self.kw)
        except TypeError:  # 老版本不认这个参数
            self.kw = {}
            out = self.engine(img)
        res = out[0] if isinstance(out, tuple) else out
        if res is None:
            return []
        if hasattr(res, 'txts'):  # 新版返回对象
            txts = list(res.txts or [])
            scores = list(res.scores or [1.0] * len(txts))
            return [t for t, s in zip(txts, scores) if float(s) >= min_score]
        lines = []
        for item in res:  # 旧版返回 [框, 文字, 分数]
            try:
                if float(item[2]) >= min_score:
                    lines.append(str(item[1]))
            except (IndexError, TypeError, ValueError):
                continue
        return lines


class OcrPool:
    """几份文字识别，同时认不同的帧"""

    def __init__(self, workers, threads):
        self.engines = [OcrEngine(threads) for _ in range(max(1, workers))]


MODEL_FILES = ['config.json', 'model.bin', 'tokenizer.json', 'vocabulary.*', 'preprocessor_config.json']


def ensure_model(size, log):
    """语音模型在本地就直接用，不在就下载。返回模型文件夹"""
    if os.path.isdir(size):  # 设置里直接写了模型文件夹
        return size
    d = os.path.join(MODEL_DIR, 'faster-whisper-' + size)
    if os.path.exists(os.path.join(d, 'model.bin')) and os.path.exists(os.path.join(d, 'config.json')):
        return d
    from huggingface_hub import snapshot_download
    last = None
    for ep in ('https://hf-mirror.com', 'https://huggingface.co'):
        try:
            log('软件里没带语音模型 %s，正在联网下载（%s），要几分钟……' % (size, ep.split('//')[1]))
            snapshot_download('Systran/faster-whisper-' + size, local_dir=d, endpoint=ep,
                              allow_patterns=MODEL_FILES)
            if os.path.exists(os.path.join(d, 'model.bin')):
                log('语音模型下载好了。')
                return d
        except Exception as e:
            last = e
            log('　　这个地址下载失败：%s' % str(e)[:150])
    raise RuntimeError('语音模型 %s 下载不下来。检查网络，或者在「筛选规则」里把模型改回 small（%s）'
                       % (size, str(last)[:200]))


class AsrEngine:
    def __init__(self, size, log=print, threads=4):
        import onnxruntime  # noqa: F401  同上
        path = ensure_model(size, log)
        from faster_whisper import WhisperModel
        self.model = WhisperModel(path, device='cpu', compute_type='int8', cpu_threads=max(1, threads))

    def transcribe(self, path):
        # temperature 只给 0：听不清的段落不再换参数重听好几遍，省时间
        return self.model.transcribe(
            path, language='zh', vad_filter=True, beam_size=1, best_of=1, temperature=0.0,
            condition_on_previous_text=False, initial_prompt='以下是普通话的句子。')


# ---------------------------------------------------------------- 检查一个视频
class Progress:
    """把「看到第几秒了」换算成百分比报给窗口，最多半秒报一次"""

    def __init__(self, report):
        self.report = report
        self.last = 0.0

    def __call__(self, stage, pct, force=False):
        now = time.time()
        if not force and now - self.last < 0.5:
            return
        self.last = now
        if self.report:
            self.report(stage, max(0, min(100, int(pct))))


class StageProgress:
    """画面和语音同时查：进度条跟着画面走（一般画面慢），画面看完了还在听语音，就显示语音"""

    def __init__(self, prog, frames_on):
        self.prog = prog
        self.frames_done = not frames_on

    def frames(self, stage, pct, force=False):
        if pct >= 100:
            self.frames_done = True
        self.prog('画面', pct, force)

    def audio(self, stage, pct, force=False):
        if self.frames_done:
            self.prog('语音', pct, force)


class Halt:
    """该停下了：用户点了停止，或者「找到就停」时另一边已经找到问题"""

    def __init__(self, stop):
        self.stop = stop
        self.found = threading.Event()

    def is_set(self):
        return self.stop.is_set() or self.found.is_set()


def video_duration(box, vs):
    try:
        if box.duration:
            return float(box.duration) / 1000000.0
        if vs.duration and vs.time_base:
            return float(vs.duration * vs.time_base)
    except Exception:
        pass
    return 0.0


def iter_frames(path, interval):
    """每隔 interval 秒取一帧，返回 (秒, 总时长, 画面, 缩略图)"""
    import av
    with av.open(path) as box:
        vs = next((s for s in box.streams if s.type == 'video'), None)
        if vs is None:
            return
        vs.thread_type = 'AUTO'
        if os.environ.get('SUCAI_DECODE_ALL') != '1':
            # 只解码会被别的帧参考的那些帧，一秒取一帧完全够用，解码快不少
            vs.codec_context.skip_frame = 'NONREF'
        total = video_duration(box, vs)
        next_t = 0.0
        for frame in box.decode(vs):
            t = frame.time
            if t is None and frame.pts is not None:
                t = float(frame.pts * vs.time_base)
            if t is None or t + 1e-6 < next_t:
                continue
            next_t = t + interval
            w, h = frame.width, frame.height
            k = min(1.0, 1280.0 / max(w, h))
            img = frame.reformat(width=max(2, int(w * k) // 2 * 2), height=max(2, int(h * k) // 2 * 2),
                                 format='bgr24').to_ndarray()
            thumb = frame.reformat(width=96, height=96, format='gray').to_ndarray()
            yield t, total, img, thumb


def check_frames(path, cfg, pool, halt, prog):
    """几份文字识别同时认不同的帧。返回 {原因: (命中内容, 秒)}"""
    found = {}  # 原因 -> (命中内容, 秒)
    lock = threading.Lock()
    done = threading.Event()  # 画面这边已经不用再看了
    errors = []
    jobs = queue.Queue(maxsize=len(pool.engines) * 2)

    def work(ocr):
        while True:
            job = jobs.get()
            if job is None:
                return
            if halt.is_set() or done.is_set():
                continue
            t, img = job
            try:
                lines = ocr(img, cfg.min_score)
            except Exception as e:
                errors.append(e)
                continue
            if not lines:
                continue
            text = ' '.join(norm(x) for x in lines)
            with lock:
                for reason, finder in (('画面有抖音号', find_douyin), ('画面写了价格', find_price)):
                    hit = finder(text, cfg)
                    if hit and (reason not in found or t < found[reason][1]):
                        found[reason] = (hit, t)
                if found and (cfg.stop_early or len(found) == 2):
                    done.set()
                    if cfg.stop_early:
                        halt.found.set()

    workers = [threading.Thread(target=work, args=(o,), daemon=True) for o in pool.engines]
    for w in workers:
        w.start()
    frames = iter_frames(path, cfg.interval)
    prev, last_ocr = None, -999.0
    try:
        for t, total, img, thumb in frames:
            if halt.is_set() or done.is_set():
                break
            if total:
                prog('画面', t * 100.0 / total)
            # 和上一帧几乎一样，且 4 秒内看过，就跳过省时间
            cur = thumb.astype('int16')
            if prev is not None and t - last_ocr < 4 and abs(cur - prev).mean() < 0.8:
                continue
            prev, last_ocr = cur, t
            jobs.put((t, img))
    finally:
        frames.close()
        for _ in workers:
            jobs.put(None)
        for w in workers:
            w.join()
    if errors and not found:
        raise errors[0]
    prog('画面', 100, True)
    return found


def check_audio(path, cfg, asr, halt, prog):
    found = {}
    segs, info = asr.transcribe(path)
    total = float(getattr(info, 'duration', 0) or 0)
    prog('语音', 0, True)
    for seg in segs:
        if halt.is_set():
            break
        if total:
            prog('语音', seg.end * 100.0 / total)
        raw = (seg.text or '').strip()
        text = norm(raw)
        if '语音说了价格' not in found:
            hit = find_price(text, cfg)
            if hit:
                found['语音说了价格'] = ('%s（原话：%s）' % (hit, raw[:40]), seg.start)
        if cfg.audio_douyin and '语音提到抖音' not in found:
            hit = find_douyin(text, cfg)
            if hit:
                found['语音提到抖音'] = ('%s（原话：%s）' % (hit, raw[:40]), seg.start)
        if found and cfg.stop_early:
            halt.found.set()
            break
    prog('语音', 100, True)
    return found


def check_video(path, cfg, pool, asr, stop, log, prog):
    """返回 {原因: (命中内容, 秒)}；空字典=没问题；None=被停止。
    看画面和听语音同时进行，「找到就停」时一边找到问题，另一边也马上停下"""
    halt = Halt(stop)
    stages = StageProgress(prog, pool is not None)
    audio, audio_err = {}, []

    def run_audio():
        try:
            audio.update(check_audio(path, cfg, asr, halt, stages.audio))
        except Exception as e:  # 没有声音轨道之类
            audio_err.append(e)

    th = None
    if asr is not None:
        th = threading.Thread(target=run_audio, daemon=True)
        th.start()
    try:
        frames = check_frames(path, cfg, pool, halt, stages.frames) if pool is not None else {}
    finally:
        if th is not None:
            th.join()
    if stop.is_set():
        return None
    if audio_err:
        log('　　语音没法识别，跳过语音检查：%s' % str(audio_err[0])[:60])
    found = dict(frames)
    found.update(audio)
    return found


# ---------------------------------------------------------------- 文件夹处理
def list_videos(folder, recursive):
    out = []
    if recursive:
        for root, dirs, files in os.walk(folder):
            dirs[:] = [d for d in dirs if d != REJECT_DIR and not d.startswith('.')]
            for f in files:
                if f.lower().endswith(VIDEO_EXT):
                    out.append(os.path.relpath(os.path.join(root, f), folder))
    else:
        for f in os.listdir(folder):
            if f.lower().endswith(VIDEO_EXT) and os.path.isfile(os.path.join(folder, f)):
                out.append(f)
    return sorted(out)


def is_stable(path):
    """文件已经写完：不是空的，而且最近几秒没再变（刚下好的多等一下）"""
    try:
        st = os.stat(path)
        if st.st_size <= 0:
            return False
        if time.time() - st.st_mtime > 5:
            return True
        time.sleep(1.5)
        return st.st_size == os.path.getsize(path)
    except OSError:
        return False


def load_done(folder):
    """记录里有的视频都算查过了；最后一条是「重查」（在软件里点了重新检查）的除外"""
    done = set()
    p = os.path.join(folder, REPORT_NAME)
    if not os.path.exists(p):
        return done
    try:
        with open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                res, rel = row.get('结果'), row.get('文件', '')
                if res in ('保留', '筛掉', '出错', '放回'):
                    done.add(rel)
                elif res == '重查':
                    done.discard(rel)
    except Exception:
        pass
    return done


def write_report(folder, rows, log):
    p = os.path.join(folder, REPORT_NAME)
    new = not os.path.exists(p)
    try:
        with open(p, 'a', encoding='utf-8-sig' if new else 'utf-8', newline='') as f:
            w = csv.writer(f)
            if new:
                w.writerow(REPORT_HEAD)
            w.writerows(rows)
        return True
    except PermissionError:
        log('　　「%s」被 Excel 打开着，写不进记录。关掉它，下一个视频会一起补上。' % REPORT_NAME)
        return False


def move_to_reject(folder, rel):
    src = os.path.join(folder, rel)
    dst_dir = os.path.join(os.path.dirname(src), REJECT_DIR)
    os.makedirs(dst_dir, exist_ok=True)
    name = os.path.basename(src)
    base, ext = os.path.splitext(name)
    dst = os.path.join(dst_dir, name)
    i = 1
    while os.path.exists(dst):
        dst = os.path.join(dst_dir, '%s (%d)%s' % (base, i, ext))
        i += 1
    shutil.move(src, dst)
    return dst


def run_folder(folder, opts, engines, stop, log, emit_ev):
    """opts: frame/audio/watch/recursive；engines: dict 放 ocr/asr（懒加载，重复使用）"""
    cfg = Config()
    workers, threads, asr_threads = cpu_plan()
    if opts['frame'] and engines.get('ocr') is None:
        log('正在加载文字识别……')
        engines['ocr'] = OcrPool(workers, threads)
    if opts['audio'] and engines.get('asr') is None:
        log('正在加载语音识别（模型 %s）……' % cfg.model)
        engines['asr'] = AsrEngine(cfg.model, log, asr_threads)
    ocr = engines.get('ocr') if opts['frame'] else None
    asr = engines.get('asr') if opts['audio'] else None
    emit_ev('READY', '')

    done = load_done(folder)
    pending_rows = []
    kept = rejected = 0
    tries = {}
    announced_wait = False
    last_todo = -1

    while not stop.is_set():
        todo = [v for v in list_videos(folder, opts['recursive']) if v not in done]
        if not todo:
            if not opts['watch']:
                break
            if not announced_wait:
                log('都查完了，正在等新视频……（点「停止」结束）')
                emit_ev('IDLE', '')
                announced_wait = True
                last_todo = -1
            stop.wait(3)
            continue
        if len(todo) != last_todo:
            log('有 %d 个视频要查。' % len(todo))
            last_todo = len(todo)
        announced_wait = False
        emit_ev('QUEUE', json.dumps({'todo': len(todo)}))

        for rel in todo:
            if stop.is_set():
                break
            path = os.path.join(folder, rel)
            if not is_stable(path):
                tries[rel] = tries.get(rel, 0) + 1
                if tries[rel] > 40:  # 一直在变或是空文件，不再等
                    log('跳过（文件一直在变化或是空的）：%s' % rel)
                    done.add(rel)
                continue  # 还在下载，下一轮再查
            tries[rel] = 0
            log('检查：%s' % rel)
            emit_ev('FILE', rel)
            t0 = time.time()
            now = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            prog = Progress(lambda stage, pct: emit_ev('PROG', json.dumps({'stage': stage, 'pct': pct},
                                                                          ensure_ascii=False)))
            result = {'rel': rel, 'time': now, 'items': [], 'moved': ''}
            try:
                found = check_video(path, cfg, ocr, asr, stop, log, prog)
            except Exception as e:
                sys.stderr.write('检查 %s 出错：\n%s\n' % (rel, traceback.format_exc()))
                log('　　出错了，跳过：%s' % str(e)[:80])
                pending_rows.append([now, rel, '出错', str(e)[:200], '', ''])
                done.add(rel)
                result.update(result='出错', items=[{'reason': str(e)[:200], 'hit': '', 'at': ''}])
                emit_ev('RESULT', json.dumps(result, ensure_ascii=False))
                emit_ev('DONE', rel)
                continue
            if found is None:
                break  # 被停止，这个视频不记录
            result['secs'] = round(time.time() - t0, 1)
            if found:
                try:
                    result['moved'] = move_to_reject(folder, rel)
                except OSError as e:
                    log('　　要筛掉，但挪不动（是不是正在播放？），请手动处理：%s' % e)
                    why = '要筛掉但挪不动：' + '、'.join(found)
                    pending_rows.append([now, rel, '出错', why, '', ''])
                    done.add(rel)
                    result.update(result='出错', items=[{'reason': why, 'hit': '', 'at': ''}])
                    emit_ev('RESULT', json.dumps(result, ensure_ascii=False))
                    emit_ev('DONE', rel)
                    continue
                rejected += 1
                result['result'] = '筛掉'
                for reason, (hit, sec) in found.items():
                    log('　　筛掉：%s「%s」，在 %s' % (reason, hit, fmt_t(sec)))
                    pending_rows.append([now, rel, '筛掉', reason, hit, fmt_t(sec)])
                    result['items'].append({'reason': reason, 'hit': hit, 'at': fmt_t(sec)})
            else:
                kept += 1
                log('　　保留（用时 %d 秒）' % (time.time() - t0))
                pending_rows.append([now, rel, '保留', '', '', ''])
                result['result'] = '保留'
            done.add(rel)
            if write_report(folder, pending_rows, log):
                pending_rows = []
            emit_ev('RESULT', json.dumps(result, ensure_ascii=False))
            emit_ev('DONE', rel)

        if not opts['watch']:
            # 单次模式：还在下载的文件等一下再查
            left = [v for v in list_videos(folder, opts['recursive']) if v not in done]
            if not left:
                break
            stop.wait(2)

    if pending_rows:
        write_report(folder, pending_rows, log)
    log('结束：本轮保留 %d 个，筛掉 %d 个。筛掉的在「%s」文件夹里。' % (kept, rejected, REJECT_DIR))


# ---------------------------------------------------------------- 识别进程
# 认字、听语音放在单独的进程里跑：识别组件就算崩了，窗口也不会跟着闪退，
# 窗口会记下是哪个视频出的问题，然后自动重开识别进程接着查下一个。
_out_lock = threading.Lock()


def emit(kind, text=''):
    try:
        line = '%s\t%s\n' % (kind, str(text).replace('\r', '').replace('\n', '\x1f'))
        with _out_lock:
            sys.stdout.write(line)
            sys.stdout.flush()
    except Exception:
        pass


def _parent_alive(pid):
    """窗口进程还在不在（窗口被强行关掉时，识别进程也要跟着退出）"""
    if not pid:
        return True
    if os.name != 'nt':
        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False
    import ctypes
    k = ctypes.WinDLL('kernel32', use_last_error=True)
    h = k.OpenProcess(0x00100000, False, pid)  # 只要「等待」权限
    if not h:
        return ctypes.get_last_error() != 87  # 87 = 没有这个进程
    try:
        return k.WaitForSingleObject(h, 0) == 0x102  # 0x102 = 还在运行
    finally:
        k.CloseHandle(h)


def _watch_stop(stop, stop_file, parent_pid):
    # 注意：不能用一个线程一直等着读窗口发来的文字。
    # 在 Windows 上，那样会让别的线程加载识别组件时永远卡住（卡在「正在加载文字识别」）。
    # 所以改成：窗口要停止时建一个标记文件，这里每半秒看一眼。
    while not stop.is_set():
        if (stop_file and os.path.exists(stop_file)) or not _parent_alive(parent_pid):
            stop.set()
            break
        time.sleep(0.5)


def _use_fake_engines():
    """只在开发测试时用（设了环境变量才生效）：不加载真的识别组件"""
    global OcrEngine, AsrEngine, check_video

    class _Fake:
        def __init__(self, *a, **k):
            time.sleep(0.5)
    OcrEngine = AsrEngine = _Fake

    def fake_check(path, cfg, ocr, asr, stop, log, prog):
        name = os.path.basename(path)
        if 'crash' in name:
            import ctypes
            ctypes.string_at(0)  # 模拟识别组件崩溃
        steps = 10 if 'slow' in name else 4
        for i in range(steps):
            if stop.is_set():
                return None
            time.sleep(0.15)
            prog('画面' if i < steps // 2 else '语音', (i + 1) * 100.0 / steps, True)
        if 'price' in name:
            return {'画面写了价格': ('9.9元', 3)}
        if 'douyin' in name:
            return {'画面有抖音号': ('抖音号', 1), '语音说了价格': ('九块九（原话：只要九块九）', 7)}
        return {}
    check_video = fake_check


def worker_main(arg):
    import faulthandler
    faulthandler.enable()  # 真崩溃时把位置写进运行日志
    if os.environ.get('SUCAI_FAKE_ENGINES') == '1':
        _use_fake_engines()
    opts = json.loads(os.environ.get('SUCAI_JOB', '{}') if arg == '-' else arg)
    stop = threading.Event()
    threading.Thread(target=_watch_stop, args=(stop, opts.get('stop_file'), opts.get('parent_pid')),
                     daemon=True).start()

    def log(m):
        emit('LOG', m)
    try:
        run_folder(opts['folder'], opts, {}, stop, log, emit)
    except Exception as e:
        sys.stderr.write(traceback.format_exc())
        log('出错停止了：%s' % str(e)[:200])
        if isinstance(e, ImportError):
            log('识别组件不全。把软件重新解压一份再试。')
        emit('FAIL', str(e)[:300])
        return 2
    return 0


def check_main(name):
    """逐项检查组件，每项单独运行。软件里「检查组件」和打包时的自检会用到"""
    import faulthandler
    faulthandler.enable()
    cfg = Config()
    if name == 'onnx':
        import glob
        import importlib.util
        import onnxruntime as ort
        print('运算组件版本 %s' % ort.__version__)
        spec = importlib.util.find_spec('rapidocr_onnxruntime')  # 只找位置，不加载
        models = sorted(glob.glob(os.path.join(os.path.dirname(spec.origin), 'models', '*.onnx'))) if spec else []
        if models:
            ort.InferenceSession(models[0], providers=['CPUExecutionProvider'])
    elif name == 'cv2':
        import numpy as np
        import cv2
        cv2.cvtColor(np.zeros((8, 8, 3), np.uint8), cv2.COLOR_BGR2GRAY)
    elif name == 'av':
        import av  # noqa: F401
    elif name == 'ocr':
        import numpy as np
        ocr = OcrEngine()
        ocr(np.full((120, 360, 3), 255, dtype=np.uint8), cfg.min_score)
    elif name == 'asr':
        import numpy as np
        asr = AsrEngine(cfg.model, print)
        segs, _ = asr.model.transcribe(np.zeros(16000, dtype=np.float32), language='zh')
        list(segs)
    else:
        print('不认识的检查项：%s' % name)
        return 2
    print('%s 正常' % name)
    return 0


def scan_main(path):
    """开发测试用：直接查一个视频，把结果打印出来（不挪文件）"""
    cfg = Config()
    stop = threading.Event()
    workers, threads, asr_threads = cpu_plan()
    pool = OcrPool(workers, threads)
    asr = AsrEngine(cfg.model, print, asr_threads)
    t0 = time.time()
    found = check_video(path, cfg, pool, asr, stop, print, Progress(None))
    print(json.dumps({k: [v[0], fmt_t(v[1])] for k, v in found.items()}, ensure_ascii=False))
    print('用时 %.1f 秒' % (time.time() - t0))
    return 0


if __name__ == '__main__':
    if len(sys.argv) > 2 and sys.argv[1] == '--worker':
        sys.exit(worker_main(sys.argv[2]))
    if len(sys.argv) > 2 and sys.argv[1] in ('--check', '--scan'):
        try:
            sys.exit((check_main if sys.argv[1] == '--check' else scan_main)(sys.argv[2]))
        except Exception:
            traceback.print_exc()
            sys.exit(2)
    print('这是素材筛选器的识别引擎，请双击「素材筛选器.exe」打开软件。')
    sys.exit(1)
