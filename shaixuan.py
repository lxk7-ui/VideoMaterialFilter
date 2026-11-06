# -*- coding: utf-8 -*-
"""
素材筛选器
检查文件夹里的视频：画面里有抖音号/抖音水印、画面字幕写了价格、语音里说了价格，
就挪到「已筛掉」文件夹，并在「筛选记录.csv」里写明原因和出现的时间点。
"""
import os
import sys

# 这两行必须在加载识别组件之前：
# 识别组件里有两套一样的运算库，Windows 上同时加载会直接闪退
os.environ.setdefault('KMP_DUPLICATE_LIB_OK', 'TRUE')
# 国内下载语音模型走镜像
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
import subprocess
import unicodedata
import configparser
from datetime import datetime

APP_DIR = os.path.dirname(os.path.abspath(__file__))
# 运行环境和模型放的地方（启动筛选器.bat 会设好，放在纯英文路径里，避免中文路径出错）
HOME_DIR = os.environ.get('SUCAI_HOME') or APP_DIR
CONFIG_PATH = os.path.join(APP_DIR, '设置.ini')
STATE_PATH = os.path.join(APP_DIR, '上次设置.json')
LOG_PATH = os.path.join(APP_DIR, '运行日志.txt')
MODEL_DIR = os.path.join(HOME_DIR, 'models')
VIDEO_EXT = ('.mp4', '.mov', '.webm', '.m4v', '.mkv', '.flv')
REJECT_DIR = '已筛掉'
REPORT_NAME = '筛选记录.csv'
REPORT_HEAD = ['检查时间', '文件', '结果', '原因', '命中内容', '出现位置']

DEFAULT_INI = """; 素材筛选器设置。改完保存，下次点「开始筛选」就生效。
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
; 第一次用会自动下载：base 约 150MB，small 约 500MB
模型 = small
; 语音里说到上面的抖音关键词也筛掉：是 / 否
也查抖音关键词 = 否

[其他]
; 找到一个问题就不再往下查（快很多）：是 / 否
找到就停 = 是
"""


# ---------------------------------------------------------------- 设置
def _split_words(s):
    return [w.strip() for w in re.split(r'[,，、;；]', s or '') if w.strip()]


def _yes(s):
    return str(s).strip() in ('是', '1', 'true', 'True', 'yes', '开')


class Config:
    def __init__(self):
        if not os.path.exists(CONFIG_PATH):
            with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
                f.write(DEFAULT_INI)
        cp = configparser.ConfigParser(interpolation=None)
        cp.optionxform = str
        cp.read_string(DEFAULT_INI)
        for enc in ('utf-8-sig', 'gbk'):  # 用记事本另存为 ANSI 也能读
            try:
                with open(CONFIG_PATH, encoding=enc) as f:
                    cp.read_string(f.read())
                break
            except (UnicodeDecodeError, configparser.Error):
                continue

        self.douyin_words = [norm(w).lower() for w in _split_words(cp.get('画面', '抖音关键词'))]
        self.check_at = _yes(cp.get('画面', '查@用户名'))
        self.interval = max(0.2, float(cp.get('画面', '间隔秒数') or 1))
        self.min_score = min(0.99, max(0.0, float(cp.get('画面', '可信度') or 0.6)))
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
class OcrEngine:
    def __init__(self):
        import onnxruntime  # noqa: F401  必须比 cv2 先加载，否则在部分电脑上会直接崩溃
        try:
            from rapidocr_onnxruntime import RapidOCR
        except ImportError:
            from rapidocr import RapidOCR
        self.engine = RapidOCR()

    def __call__(self, img, min_score):
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
            log('正在下载语音模型 %s（%s），第一次要几分钟……' % (size, ep.split('//')[1]))
            snapshot_download('Systran/faster-whisper-' + size, local_dir=d, endpoint=ep,
                              allow_patterns=MODEL_FILES)
            if os.path.exists(os.path.join(d, 'model.bin')):
                log('语音模型下载好了。')
                return d
        except Exception as e:
            last = e
            log('　　这个地址下载失败：%s' % str(e)[:150])
    raise RuntimeError('语音模型下载不下来，检查网络后再试（%s）' % str(last)[:200])


class AsrEngine:
    def __init__(self, size, log=print):
        import onnxruntime  # noqa: F401  同上
        path = ensure_model(size, log)
        from faster_whisper import WhisperModel
        self.model = WhisperModel(path, device='cpu', compute_type='int8',
                                  cpu_threads=max(1, min(8, os.cpu_count() or 4)))

    def segments(self, path):
        segs, _info = self.model.transcribe(
            path, language='zh', vad_filter=True, beam_size=1, best_of=1,
            condition_on_previous_text=False, initial_prompt='以下是普通话的句子。')
        return segs


# ---------------------------------------------------------------- 检查一个视频
def iter_frames(path, interval):
    """每隔 interval 秒取一帧，返回 (秒, 画面, 缩略图)"""
    import av
    with av.open(path) as box:
        vs = next((s for s in box.streams if s.type == 'video'), None)
        if vs is None:
            return
        vs.thread_type = 'AUTO'
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
            yield t, img, thumb


def check_frames(path, cfg, ocr, stop):
    found = {}  # 原因 -> (命中内容, 秒)
    prev, last_ocr = None, -999.0
    for t, img, thumb in iter_frames(path, cfg.interval):
        if stop.is_set():
            return None
        # 和上一帧几乎一样，且 4 秒内看过，就跳过省时间
        if prev is not None and t - last_ocr < 4:
            if abs(thumb.astype('int16') - prev).mean() < 0.8:
                continue
        prev, last_ocr = thumb.astype('int16'), t
        lines = ocr(img, cfg.min_score)
        if not lines:
            continue
        text = ' '.join(norm(x) for x in lines)
        if '画面有抖音号' not in found:
            hit = find_douyin(text, cfg)
            if hit:
                found['画面有抖音号'] = (hit, t)
        if '画面写了价格' not in found:
            hit = find_price(text, cfg)
            if hit:
                found['画面写了价格'] = (hit, t)
        if found and (cfg.stop_early or len(found) == 2):
            break
    return found


def check_audio(path, cfg, asr, stop):
    found = {}
    for seg in asr.segments(path):
        if stop.is_set():
            return None
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
            break
    return found


def check_video(path, cfg, ocr, asr, stop, log):
    """返回 {原因: (命中内容, 秒)}；空字典=没问题；None=被停止"""
    found = {}
    if ocr is not None:
        r = check_frames(path, cfg, ocr, stop)
        if r is None:
            return None
        found.update(r)
    if asr is not None and not (found and cfg.stop_early):
        try:
            r = check_audio(path, cfg, asr, stop)
            if r is None:
                return None
            found.update(r)
        except Exception as e:  # 没有声音轨道之类
            log('　　语音没法识别，跳过语音检查：%s' % str(e)[:60])
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


def is_stable(path, wait=1.5):
    """文件大小不再变化（没在写入）"""
    try:
        a = os.path.getsize(path)
        time.sleep(wait)
        return a > 0 and a == os.path.getsize(path)
    except OSError:
        return False


def load_done(folder):
    done = set()
    p = os.path.join(folder, REPORT_NAME)
    if not os.path.exists(p):
        return done
    try:
        with open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row.get('结果') in ('保留', '筛掉', '出错'):
                    done.add(row.get('文件', ''))
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


def run_folder(folder, opts, engines, stop, log, on_file=None, on_done=None):
    """opts: frame/audio/watch/recursive；engines: dict 放 ocr/asr（懒加载，重复使用）"""
    cfg = Config()
    if opts['frame'] and engines.get('ocr') is None:
        log('正在加载文字识别……')
        engines['ocr'] = OcrEngine()
    if opts['audio'] and engines.get('asr') is None:
        log('正在加载语音识别（模型 %s）……' % cfg.model)
        engines['asr'] = AsrEngine(cfg.model, log)
    ocr = engines.get('ocr') if opts['frame'] else None
    asr = engines.get('asr') if opts['audio'] else None

    done = load_done(folder)
    pending_rows = []
    kept = rejected = 0
    tries = {}
    announced_wait = False

    while not stop.is_set():
        todo = [v for v in list_videos(folder, opts['recursive']) if v not in done]
        if not todo:
            if not opts['watch']:
                break
            if not announced_wait:
                log('都查完了，正在等新视频……（点「停止」结束）')
                announced_wait = True
            stop.wait(3)
            continue
        announced_wait = False

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
            log('检查：%s' % rel)
            if on_file:
                on_file(rel)
            t0 = time.time()
            now = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            try:
                found = check_video(path, cfg, ocr, asr, stop, log)
            except Exception as e:
                sys.stderr.write('检查 %s 出错：\n%s\n' % (rel, traceback.format_exc()))
                log('　　出错了，跳过：%s' % str(e)[:80])
                pending_rows.append([now, rel, '出错', str(e)[:200], '', ''])
                done.add(rel)
                if on_done:
                    on_done(rel)
                continue
            if found is None:
                break  # 被停止，这个视频不记录
            if found:
                try:
                    move_to_reject(folder, rel)
                except OSError as e:
                    log('　　要筛掉，但挪不动（是不是正在播放？），请手动处理：%s' % e)
                    pending_rows.append([now, rel, '出错', '要筛掉但挪不动：' + '、'.join(found), '', ''])
                    done.add(rel)
                    if on_done:
                        on_done(rel)
                    continue
                rejected += 1
                for reason, (hit, sec) in found.items():
                    log('　　筛掉：%s「%s」，在 %s' % (reason, hit, fmt_t(sec)))
                    pending_rows.append([now, rel, '筛掉', reason, hit, fmt_t(sec)])
            else:
                kept += 1
                log('　　保留（用时 %d 秒）' % (time.time() - t0))
                pending_rows.append([now, rel, '保留', '', '', ''])
            done.add(rel)
            if write_report(folder, pending_rows, log):
                pending_rows = []
            if on_done:
                on_done(rel)

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
# 会记下是哪个视频出的问题，然后自动接着查下一个。
def emit(kind, text=''):
    try:
        sys.stdout.write('%s\t%s\n' % (kind, str(text).replace('\r', '').replace('\n', '\x1f')))
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
            pass
    OcrEngine = AsrEngine = _Fake

    def fake_check(path, cfg, ocr, asr, stop, log):
        name = os.path.basename(path)
        if 'crash' in name:
            import ctypes
            ctypes.string_at(0)  # 模拟识别组件崩溃
        time.sleep(0.2)
        return {'画面写了价格': ('9.9元', 3)} if 'price' in name else {}
    check_video = fake_check


def worker_main(arg):
    import faulthandler
    faulthandler.enable()  # 真崩溃时把位置写进运行日志
    if os.environ.get('SUCAI_FAKE_ENGINES') == '1':
        _use_fake_engines()
    opts = json.loads(arg)
    stop = threading.Event()
    threading.Thread(target=_watch_stop, args=(stop, opts.get('stop_file'), opts.get('parent_pid')),
                     daemon=True).start()

    def log(m):
        emit('LOG', m)
    try:
        run_folder(opts['folder'], opts, {}, stop, log,
                   on_file=lambda r: emit('FILE', r), on_done=lambda r: emit('DONE', r))
    except Exception as e:
        sys.stderr.write(traceback.format_exc())
        log('出错停止了：%s' % str(e)[:200])
        if isinstance(e, ImportError):
            log('识别组件没装好。请关掉这个窗口，双击「修复运行环境.bat」。')
        emit('FAIL', str(e)[:300])
        return 2
    return 0


def prepare_main():
    """第一次安装时运行：把识别组件和模型都准备好，顺便检查能不能正常工作"""
    import faulthandler
    faulthandler.enable()
    print('正在检查识别组件……')
    cfg = Config()
    import numpy as np
    print('1/3 加载视频读取组件……')
    import av  # noqa: F401
    print('2/3 加载文字识别并试运行……')
    ocr = OcrEngine()
    img = np.full((120, 360, 3), 255, dtype=np.uint8)
    ocr(img, cfg.min_score)
    print('3/3 加载语音识别（模型 %s）并试运行……' % cfg.model)
    asr = AsrEngine(cfg.model, print)
    segs, _ = asr.model.transcribe(np.zeros(16000, dtype=np.float32), language='zh')
    list(segs)
    print('识别组件全部正常。')
    return 0


def check_main(name):
    """安装时逐项检查组件，每项单独运行。崩溃时启动脚本根据是哪一项来修"""
    import faulthandler
    faulthandler.enable()
    cfg = Config()
    if name == 'onnx':
        import glob
        import importlib.util
        import onnxruntime as ort
        print('　　运算组件版本 %s' % ort.__version__)
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
    return 0


class Supervisor:
    """在窗口这边管理识别进程：启动、停止、崩溃后自动续查"""
    MAX_CRASH = 5

    def __init__(self, opts, log):
        self.opts = opts
        self.log = log
        self.q = queue.Queue()
        self.proc = None
        self.logf = None
        self.current = None
        self.stopping = False
        self.crashes = 0
        self.running = False

    def start(self):
        self.stopping = False
        self.crashes = 0
        self._spawn()

    def _spawn(self):
        exe = sys.executable
        d = os.path.dirname(exe)
        if os.path.basename(exe).lower() == 'pythonw.exe' and os.path.exists(os.path.join(d, 'python.exe')):
            exe = os.path.join(d, 'python.exe')  # 识别进程要能输出文字
        env = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUTF8='1')
        try:
            self.logf = open(LOG_PATH, 'a', encoding='utf-8')
            self.logf.write('\n==== %s 开始筛选 ====\n' % datetime.now().strftime('%Y-%m-%d %H:%M:%S'))
            self.logf.flush()
        except OSError:
            self.logf = subprocess.DEVNULL
        flags = 0x08000000 if os.name == 'nt' else 0  # 不弹黑窗口
        self.current = None
        self.spawns = getattr(self, 'spawns', 0) + 1
        self.stop_file = os.path.join(HOME_DIR, 'stop-%d-%d.flag' % (os.getpid(), self.spawns))
        try:
            os.remove(self.stop_file)
        except OSError:
            pass
        opts = dict(self.opts, stop_file=self.stop_file, parent_pid=os.getpid())
        self.started = time.time()
        self.last_msg = time.time()
        self.warned = False
        self.proc = subprocess.Popen(
            [exe, '-u', os.path.abspath(__file__), '--worker', json.dumps(opts, ensure_ascii=True)],
            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=self.logf,
            env=env, cwd=APP_DIR, creationflags=flags)
        self.running = True
        threading.Thread(target=self._reader, args=(self.proc,), daemon=True).start()

    def _reader(self, proc):
        for raw in proc.stdout:
            line = raw.decode('utf-8', 'replace').rstrip('\r\n')
            kind, _, text = line.partition('\t')
            self.q.put((kind, text.replace('\x1f', '\n')))
        self.q.put(('EXIT', proc.wait()))

    def stop(self):
        if not self.running:
            return
        self.stopping = True
        try:
            with open(self.stop_file, 'w') as f:
                f.write('stop')
        except OSError:
            self.kill()

    def kill(self):
        if self.proc is not None and self.proc.poll() is None:
            try:
                self.proc.kill()
            except Exception:
                pass

    def poll(self):
        """窗口定时调用；返回 True 表示识别进程彻底结束了"""
        finished = False
        while True:
            try:
                kind, text = self.q.get_nowait()
            except queue.Empty:
                break
            self.last_msg = time.time()
            if kind == 'LOG':
                self.loading = text.startswith('正在加载')
                self.log(text)
            elif kind == 'FILE':
                self.current = text
            elif kind == 'DONE':
                self.current = None
                self.crashes = 0
            elif kind == 'EXIT':
                finished = self._on_exit(text)
        if (self.running and getattr(self, 'loading', False) and not self.warned and not self.stopping
                and time.time() - self.last_msg > 180):
            self.warned = True
            self.log('已经 3 分钟没有动静了。点「停止」再点「开始筛选」试一次；还卡住就把「运行日志」发给 Claude。')
        return finished

    def _on_exit(self, code):
        try:
            os.remove(self.stop_file)
        except OSError:
            pass
        if self.logf not in (None, subprocess.DEVNULL):
            try:
                self.logf.close()
            except Exception:
                pass
        self.running = False
        if self.stopping or code in (0, 2):
            if self.stopping:
                self.log('已停止。')
            return True
        code_txt = '0x%08X' % (code & 0xFFFFFFFF) if isinstance(code, int) else str(code)
        if self.current:
            rel = self.current
            self.crashes += 1
            self.log('　　检查「%s」时识别组件崩溃了（代码 %s），这个视频先跳过，标成「出错」。' % (rel, code_txt))
            now = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
            write_report(self.opts['folder'], [[now, rel, '出错', '检查时识别组件崩溃（代码 %s），请手动看一下' % code_txt, '', '']], self.log)
            if self.crashes <= self.MAX_CRASH:
                self.log('自动重新启动识别，接着查后面的视频……')
                self._spawn()
                return False
            self.log('连续崩溃太多次，先停下了。把「运行日志.txt」发给 Claude。')
            return True
        self.log('识别组件启动失败（代码 %s）。' % code_txt)
        self.log('可以双击「修复运行环境.bat」重装组件；还不行就把「运行日志.txt」发给 Claude。')
        tail = read_log_tail(12)
        if tail:
            self.log('运行日志最后几行：\n' + tail)
        return True


def read_log_tail(n):
    try:
        with open(LOG_PATH, encoding='utf-8', errors='replace') as f:
            lines = [x.rstrip() for x in f.readlines() if x.strip()]
        return '\n'.join(lines[-n:])
    except OSError:
        return ''


# ---------------------------------------------------------------- 界面
def trim_log(limit=2 * 1024 * 1024):
    """运行日志超过 2MB 就只留后面一半"""
    try:
        if os.path.getsize(LOG_PATH) > limit:
            with open(LOG_PATH, 'rb') as f:
                f.seek(-limit // 2, os.SEEK_END)
                tail = f.read()
            with open(LOG_PATH, 'wb') as f:
                f.write(tail.split(b'\n', 1)[-1])
    except OSError:
        pass


_instance_lock = None


def already_running():
    """占一个本机端口当作「已经开着」的标记，防止开两个窗口同时挪同一批文件"""
    global _instance_lock
    import socket
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        s.bind(('127.0.0.1', 47391))
        s.listen(1)
    except OSError:
        s.close()
        return True
    _instance_lock = s
    return False


def main():
    import tkinter as tk
    from tkinter import ttk, filedialog, messagebox
    from tkinter.scrolledtext import ScrolledText

    trim_log()
    if sys.stderr is None:  # 双击启动时没有黑窗口，错误写进运行日志
        try:
            sys.stderr = open(LOG_PATH, 'a', encoding='utf-8', buffering=1)
        except OSError:
            pass
    if already_running():
        try:
            import ctypes
            ctypes.windll.user32.MessageBoxW(0, '素材筛选器已经开着了，在任务栏里找一下它的窗口。', '素材筛选器', 0x40)
        except Exception:
            pass
        return

    try:
        with open(STATE_PATH, encoding='utf-8') as f:
            state = json.load(f)
    except Exception:
        state = {}

    root = tk.Tk()
    root.title('素材筛选器')
    root.geometry('640x540')
    root.minsize(520, 420)
    root.option_add('*Font', ('Microsoft YaHei UI', 10))

    msgs = queue.Queue()
    sup = {'s': None}

    def log(msg):
        stamp = datetime.now().strftime('%H:%M:%S ')
        msgs.put(stamp + msg.replace('\n', '\n' + ' ' * 9))

    def on_tk_error(exc, val, tb):
        text = ''.join(traceback.format_exception(exc, val, tb))
        try:
            sys.stderr.write(text)
        except Exception:
            pass
        log('出错了（详细信息在运行日志里）：%s' % val)
    root.report_callback_exception = on_tk_error

    frm = ttk.Frame(root, padding=12)
    frm.pack(fill='both', expand=True)

    ttk.Label(frm, text='要检查的视频文件夹').pack(anchor='w')
    row = ttk.Frame(frm)
    row.pack(fill='x', pady=(2, 8))
    folder_var = tk.StringVar(value=state.get('folder', ''))
    ttk.Entry(row, textvariable=folder_var).pack(side='left', fill='x', expand=True)

    def choose():
        d = filedialog.askdirectory(initialdir=folder_var.get() or os.path.expanduser('~'))
        if d:
            folder_var.set(os.path.normpath(d))
    ttk.Button(row, text='选择', command=choose).pack(side='left', padx=(6, 0))

    v_frame = tk.BooleanVar(value=state.get('frame', True))
    v_audio = tk.BooleanVar(value=state.get('audio', True))
    v_watch = tk.BooleanVar(value=state.get('watch', True))
    v_rec = tk.BooleanVar(value=state.get('recursive', False))
    ttk.Checkbutton(frm, text='查画面：抖音号、抖音水印、字幕写的价格', variable=v_frame).pack(anchor='w')
    ttk.Checkbutton(frm, text='查语音：说了价格', variable=v_audio).pack(anchor='w')
    ttk.Checkbutton(frm, text='一直开着，新下载的视频自动检查', variable=v_watch).pack(anchor='w')
    ttk.Checkbutton(frm, text='也查子文件夹里的视频', variable=v_rec).pack(anchor='w')

    btns = ttk.Frame(frm)
    btns.pack(fill='x', pady=8)
    b_start = ttk.Button(btns, text='开始筛选')
    b_stop = ttk.Button(btns, text='停止', state='disabled')
    b_start.pack(side='left')
    b_stop.pack(side='left', padx=6)

    def open_path(p):
        if p and os.path.exists(p) and hasattr(os, 'startfile'):
            os.startfile(p)

    def open_ini():
        Config()  # 没有就先生成
        open_path(CONFIG_PATH)
    ttk.Button(btns, text='打开文件夹', command=lambda: open_path(folder_var.get())).pack(side='right')
    ttk.Button(btns, text='改筛选规则', command=open_ini).pack(side='right', padx=6)
    ttk.Button(btns, text='运行日志', command=lambda: open_path(LOG_PATH)).pack(side='right')

    box = ScrolledText(frm, height=14, wrap='word', state='disabled')
    box.pack(fill='both', expand=True)

    def set_idle(idle):
        b_start.configure(state='normal' if idle else 'disabled')
        b_stop.configure(state='disabled' if idle else 'normal')

    def poll():
        s = sup['s']
        if s is not None:
            try:
                if s.poll():
                    sup['s'] = None
                    set_idle(True)
            except Exception:
                log('出错了：' + traceback.format_exc()[-600:])
                sup['s'] = None
                set_idle(True)
        try:
            while True:
                m = msgs.get_nowait()
                box.configure(state='normal')
                box.insert('end', m + '\n')
                box.see('end')
                box.configure(state='disabled')
        except queue.Empty:
            pass
        root.after(200, poll)

    def start():
        folder = folder_var.get().strip()
        if not folder or not os.path.isdir(folder):
            messagebox.showwarning('素材筛选器', '先选一个存视频的文件夹')
            return
        if not v_frame.get() and not v_audio.get():
            messagebox.showwarning('素材筛选器', '「查画面」和「查语音」至少勾一个')
            return
        opts = {'folder': folder, 'frame': v_frame.get(), 'audio': v_audio.get(),
                'watch': v_watch.get(), 'recursive': v_rec.get()}
        try:
            with open(STATE_PATH, 'w', encoding='utf-8') as f:
                json.dump(opts, f, ensure_ascii=False)
        except OSError:
            pass
        set_idle(False)
        s = Supervisor(opts, log)
        try:
            s.start()
        except Exception as e:
            log('启动识别失败：%s' % e)
            set_idle(True)
            return
        sup['s'] = s

    def do_stop():
        s = sup['s']
        if s is None:
            return
        log('正在停止，当前这个视频查到一半会直接放弃……')
        s.stop()

        def force():
            if sup['s'] is s and s.running:
                s.kill()
        root.after(5000, force)

    def on_close():
        s = sup['s']
        if s is not None:
            s.kill()
        root.destroy()

    b_start.configure(command=start)
    b_stop.configure(command=do_stop)
    root.protocol('WM_DELETE_WINDOW', on_close)
    log('选好文件夹，点「开始筛选」。有问题的视频会挪到「%s」，原因写在「%s」里。' % (REJECT_DIR, REPORT_NAME))
    root.after(200, poll)
    root.mainloop()


def show_fatal(text):
    """窗口都打不开时，至少弹个提示框，并写进运行日志"""
    try:
        with open(LOG_PATH, 'a', encoding='utf-8') as f:
            f.write('\n==== %s 窗口打不开 ====\n%s\n' % (datetime.now().strftime('%Y-%m-%d %H:%M:%S'), text))
    except OSError:
        pass
    msg = '素材筛选器打不开，详细信息已写进「运行日志.txt」，发给 Claude 就行。\n\n' + text[-800:]
    try:
        import ctypes
        ctypes.windll.user32.MessageBoxW(0, msg, '素材筛选器', 0x10)
    except Exception:
        print(msg)


if __name__ == '__main__':
    if len(sys.argv) > 2 and sys.argv[1] == '--worker':
        sys.exit(worker_main(sys.argv[2]))
    if len(sys.argv) > 2 and sys.argv[1] == '--check':
        try:
            sys.exit(check_main(sys.argv[2]))
        except Exception:
            traceback.print_exc()
            sys.exit(2)
    if len(sys.argv) > 1 and sys.argv[1] == '--prepare':
        try:
            sys.exit(prepare_main())
        except Exception:
            traceback.print_exc()
            sys.exit(2)
    try:
        main()
    except Exception:
        show_fatal(traceback.format_exc())
