<h1 align="center">素材筛选器</h1>

<p align="center">
  自动检查下载好的视频素材：画面文字识别 · 语音识别 · 筛掉带抖音号、水印和报价的视频 · 解压即用
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D6" alt="Windows 10 | 11 x64">
  <img src="https://img.shields.io/badge/C%23-WPF-239120" alt="C# WPF">
  <img src="https://img.shields.io/badge/Python-3.11-3776AB" alt="Python 3.11">
  <img src="https://img.shields.io/badge/RapidOCR-ONNX%20Runtime-EE4C2C" alt="RapidOCR">
  <img src="https://img.shields.io/badge/faster--whisper-small-4B8BBE" alt="faster-whisper">
</p>

收集来的视频素材里，有些画面上带着别人的抖音号、平台水印，或者字幕、口播里报了价格，不能直接用。
素材筛选器逐个检查文件夹里的视频，把这几种挪到「已筛掉」文件夹，没问题的留在原地；
每个视频查了什么、为什么筛掉、在第几秒出现，都记在「筛选记录.csv」里。筛掉的只是挪走，不会删除。

Windows 桌面程序，Python 运行环境、识别组件和语音模型都随包附带，不用安装、不用联网，完全在本机识别。

当前版本 **v2.3**，各版本的变化见 [更新记录](CHANGELOG.md)。

## 它筛什么

| 检查 | 筛掉的情况 | 怎么查 |
|---|---|---|
| 画面：抖音号 | 画面里有「抖音号」、抖音水印、「@用户名」（邮箱不算） | 每隔 1 秒看一帧画面，认画面上的字 |
| 画面：价格 | 字幕里写了价格：¥19.9、9.9元、到手价、券后、只要39.9 | 同上 |
| 语音：价格 | 口播里说了价格：「只要九块九」「十九块钱两条」「一百多块钱」 | 语音识别后按价格说法匹配 |

关键词、看画面的间隔、文字识别的可信度、语音模型都可以在「筛选规则」里改。
「元旦」「到手3天」「直降50%」「拍下2件」这类不是价格的说法不会被误筛。

## 功能

- **一边下载一边筛**：勾上「自动检查新下载的视频」，文件夹里新出现的视频自动接着查，还在下载的等它下完再查；查完一段时间没有新视频就自动停止。
- **筛选记忆（历史通过）**：查过并且通过的视频按内容记住，挪了文件夹、改了名字、重新下载了同一个文件，都直接跳过不再查。
- **检查记录**：按「全部 / 筛掉 / 保留 / 历史通过 / 出错」查看；可以勾选多选、全选，批量播放、打开所在位置、放回原处、重新检查。
- **筛错了能放回**：「放回原处」把视频从「已筛掉」挪回原来的文件夹，以后不会再查它。
- **模型下载**：随包带 small 语音模型，还可以下载 tiny / base / medium，带进度、断点续传、下完校验，线路自动选最快的。
- **快**：画面和语音同时查，按 CPU 核数自动分配，只解码需要的画面，几秒内没变的字不重复认。12 代 i5 笔记本上 3 个 30 秒的 1080P 视频约 27 秒查完。
- **稳**：某个视频检查时识别组件崩了，软件不会关，把这个视频标成「出错」后接着查；「组件自检」逐项试运行；「筛选记录.csv」用 Excel 改过也能读。
- **换版本不丢东西**：新版第一次打开时自动把旧版本的筛选记忆、筛选规则和上次选的文件夹带过来。

## 下载

到 [Releases](https://github.com/lxk7-ui/VideoMaterialFilter/releases) 下载 `VideoMaterialFilter-v2.3-win64.zip`（约 600MB，其中语音模型 460MB），完整解压后双击「素材筛选器」文件夹里的 `素材筛选器.exe`。

- **系统要求**：64 位 Windows 10 / 11。
- 整个文件夹可以放在任何位置（路径里有中文也没关系），也可以拷到别的电脑。请整体解压 / 拷贝，不要只复制 exe。
- 想在桌面放个图标：双击「创建桌面快捷方式.vbs」。

> [!NOTE]
> 程序没有代码签名，第一次运行时 Windows 可能提示“已保护你的电脑”，点 **更多信息 → 仍要运行**。

## 使用

1. 点「视频文件夹」后面的「选择」，选放视频的文件夹；
2. 「检查」里勾上要查的内容（画面：抖音号、价格；语音：价格）；
3. 需要的话勾「自动检查新下载的视频」「查完自动停止」「包含子文件夹」；
4. 点左下角 **开始筛选**。第一次要加载识别组件，大约 5 秒。

识别不是百分之百准：字太小、字体花哨、背景音乐很吵的时候可能漏掉，偶尔也会误判。建议隔一阵翻一下「已筛掉」文件夹，筛错的用「放回原处」放回去。

完整说明见 [使用说明](package/使用说明.txt)（随发布包一起分发的那一份）。

## 文档

| 文档 | 内容 |
|---|---|
| [使用说明](package/使用说明.txt) | 安装与升级、使用步骤、检查记录、筛选记忆、模型下载、筛选规则、速度、出问题怎么办 |
| [更新记录](CHANGELOG.md) | v1.0 至今每个版本的时间和更新内容 |
| [默认筛选规则](engine/设置.ini) | 关键词、间隔、可信度、模型等的默认值和说明 |

## 它是怎么搭起来的

```
素材筛选器.exe（C# / WPF）  ──启动、发指令、读结果──▶  runtime\python\python.exe  runtime\app\shaixuan.py
       界面、检查记录、筛选记忆、                         识别引擎：PyAV 解码 → RapidOCR 认画面上的字
       规则、模型下载、版本迁移                                    faster-whisper 识别语音 → 按规则判断
```

界面程序不依赖任何 NuGet 包，只用 Windows 自带的 .NET Framework；识别引擎是一个 Python 脚本，
跑在随包附带的独立 Python 3.11 里（不动电脑上已经装的 Python）。两边通过子进程的标准输入输出通信，
所以识别组件崩溃时界面不会跟着崩。

## 从源码构建

需要 Windows 10/11 x64、[.NET SDK](https://dotnet.microsoft.com/download)（用它自带的 Roslyn 编译器）、.NET Framework 4.7.1 引用程序集，以及打包运行环境用的 [uv](https://docs.astral.sh/uv/)。

```powershell
git clone https://github.com/lxk7-ui/VideoMaterialFilter.git
cd VideoMaterialFilter
pwsh build.ps1                         # 只编译界面程序 -> bin\素材筛选器.exe
```

打包完整的便携版（第一次要准备 Python 和语音模型）：

```powershell
uv python install 3.11.15                                              # runtime.ps1 从 %APPDATA%\uv\python 取这个 Python
pwsh tools\dl-model.ps1 -Out ..\_build\models\faster-whisper-small     # 语音模型 small（约 460MB）
pwsh tools\dl-wheels.ps1 -Out ..\_build\wheels                         # 可选：先把大的依赖包下到本地
pwsh package.ps1 -Version v2.3
```

`package.ps1` 依次调用 `build.ps1` 和 `runtime.ps1`，在源码文件夹旁边的 `视频素材工具_v2\素材筛选器\` 里组装出便携文件夹，并打成 `素材筛选器_<版本>_便携版.zip`（输出位置可用 `-OutRoot` 改）。
`runtime.ps1` 做的事：复制一份独立的 Python、按 [requirements-lock.txt](requirements-lock.txt) 装好依赖、带上 VC++ 运行库和语音模型、删掉用不到的部分、预编译全部 `.py`。

## 项目结构

```
src/            界面程序源码（C#，WPF 界面全部用代码搭建）
  MainWindow*.cs      主窗口：文件夹、选项、进度、检查记录
  Engine.cs           启动识别引擎、收发指令和结果
  Records.cs / Memory.cs   筛选记录.csv 的读写、筛选记忆
  Rules*.cs / Models.cs / ModelWindow.cs   筛选规则、语音模型的下载与校验
  Migrate.cs          换新版本时把旧版本的记忆和规则带过来
engine/         识别引擎 shaixuan.py 和默认筛选规则 设置.ini
package/        随发布包分发的文件：使用说明、默认规则、桌面快捷方式脚本
tools/          准备打包用的语音模型和依赖包的下载脚本
build.ps1       编译界面程序
runtime.ps1     组装 runtime\（Python、依赖、VC++ 运行库、模型、引擎）
package.ps1     打包便携版
make-icon.ps1   绘制程序图标
```

## 第三方组件

随发布包分发，各自的协议文件在 `runtime\python\Lib\site-packages\` 对应的 `.dist-info` 文件夹里。

| 组件 | 协议 | 用途 |
|---|---|---|
| [RapidOCR](https://github.com/RapidAI/RapidOCR)（rapidocr-onnxruntime 1.4.4） | Apache-2.0 | 认画面上的字 |
| [faster-whisper](https://github.com/SYSTRAN/faster-whisper) 1.2.1 / [CTranslate2](https://github.com/OpenNMT/CTranslate2) | MIT | 语音识别 |
| [Systran/faster-whisper-small](https://huggingface.co/Systran/faster-whisper-small)（OpenAI Whisper small 的转换版） | MIT | 语音模型 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | MIT | 模型推理 |
| [PyAV](https://github.com/PyAV-Org/PyAV)（内含 FFmpeg 库） | BSD-3-Clause（随附的 FFmpeg 库遵循 FFmpeg 自己的协议） | 视频、音频解码 |
| [OpenCV](https://github.com/opencv/opencv-python) | Apache-2.0 | 图像处理 |
| NumPy、Pillow、Shapely、pyclipper 等 | BSD / HPND / MIT | 数值与图像处理 |
| [CPython 3.11](https://www.python.org)（[python-build-standalone](https://github.com/astral-sh/python-build-standalone)） | PSF | 运行环境 |
| Microsoft Visual C++ 运行库 | Microsoft 可再发行组件 | ONNX Runtime、CTranslate2 需要 |

完整的依赖和版本见 [requirements-lock.txt](requirements-lock.txt)。

## 使用规范

本工具用于整理**自有或已获授权**的视频素材：把带有他人账号、平台水印或价格信息、不适合再次使用的视频挑出来。它只移动文件、不修改视频内容，不去除任何水印。请只处理你有权使用的素材，尊重著作权与各平台规则。

## 许可

随附的第三方组件遵循各自的协议，见上表。
