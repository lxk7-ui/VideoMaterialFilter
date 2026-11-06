@echo off
setlocal EnableExtensions
title 素材筛选器
cd /d "%~dp0"

rem 运行环境装在纯英文路径里（识别组件在中文路径下容易出错），不改动电脑上已有的 Python
set "HOME_DIR=%PUBLIC%\SucaiShaixuan"
if "%PUBLIC%"=="" set "HOME_DIR=C:\Users\Public\SucaiShaixuan"
set "PYDIR=%HOME_DIR%\python311"
set "PY=%PYDIR%\python.exe"
set "PYW=%PYDIR%\pythonw.exe"
set "READY=%HOME_DIR%\ready-2.txt"
set "SUCAI_HOME=%HOME_DIR%"
set "HF_ENDPOINT=https://hf-mirror.com"
set "HF_HUB_DISABLE_XET=1"
set "KMP_DUPLICATE_LIB_OK=TRUE"
set "PYTHONUTF8=1"
set "PIPX="
set "REPAIR="

if /i "%~1"=="repair" goto repair
if exist "%READY%" if exist "%PYW%" goto run
goto setup

:repair
del "%READY%" >nul 2>nul
set "PIPX=--force-reinstall"
set "REPAIR=1"

:setup
echo.
echo ==========================================================
echo   正在准备运行环境，只需要一次
echo   全部自动下载安装，大约 10 到 20 分钟，期间别关这个窗口
echo   安装位置：%HOME_DIR%
echo ==========================================================
echo.
if not exist "%HOME_DIR%" mkdir "%HOME_DIR%"
if not exist "%HOME_DIR%" goto fail_dir
copy /y "%~dp0requirements.txt" "%HOME_DIR%\requirements.txt" >nul

rem ---------- 1. Windows 运行库：太旧的话识别组件一加载就崩溃，要 14.40 以上 ----------
call :vcver
if %VCMINOR% GEQ 40 if not defined REPAIR goto vc_ok
echo [1/4] 下载最新的 Windows 运行库，约 25MB……
call :download "https://aka.ms/vs/17/release/vc_redist.x64.exe" "%HOME_DIR%\vc_redist.x64.exe"
if not "%errorlevel%"=="0" goto fail_net
echo        正在安装运行库。如果弹出「是否允许此应用对你的设备进行更改」，请点「是」
"%HOME_DIR%\vc_redist.x64.exe" /install /passive /norestart
call :vcver
if %VCMINOR% LSS 40 goto fail_vc
:vc_ok
echo [1/4] Windows 运行库：正常（14.%VCMINOR%）

rem ---------- 2. 自带的 Python ----------
if exist "%PYW%" goto py_ok
echo [2/4] 下载 Python 3.11，约 25MB……
set "PYEXE=%HOME_DIR%\python-3.11.9-amd64.exe"
call :download "https://mirrors.huaweicloud.com/python/3.11.9/python-3.11.9-amd64.exe" "%PYEXE%"
if not "%errorlevel%"=="0" call :download "https://registry.npmmirror.com/-/binary/python/3.11.9/python-3.11.9-amd64.exe" "%PYEXE%"
if not "%errorlevel%"=="0" call :download "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe" "%PYEXE%"
if not "%errorlevel%"=="0" goto fail_net
echo        正在安装，大约 1 到 2 分钟……
"%PYEXE%" /quiet InstallAllUsers=0 TargetDir="%PYDIR%" Include_launcher=0 InstallLauncherAllUsers=0 PrependPath=0 AssociateFiles=0 Shortcuts=0 Include_doc=0 Include_test=0 Include_dev=0 Include_tcltk=1 Include_pip=1
if not exist "%PYW%" goto fail_py
:py_ok
echo [2/4] Python：正常

rem ---------- 3. 识别组件 ----------
echo [3/4] 下载识别组件，约 300MB，已经装过的会跳过……
set "PIPARGS=install --disable-pip-version-check --no-warn-script-location %PIPX% -r "%HOME_DIR%\requirements.txt""
"%PY%" -m pip %PIPARGS% -i https://pypi.tuna.tsinghua.edu.cn/simple
if "%errorlevel%"=="0" goto pip_ok
echo        换一个下载源再试……
"%PY%" -m pip %PIPARGS% -i https://mirrors.aliyun.com/pypi/simple/
if "%errorlevel%"=="0" goto pip_ok
echo        再换一个下载源……
"%PY%" -m pip %PIPARGS%
if not "%errorlevel%"=="0" goto fail_pip
:pip_ok
echo [3/4] 识别组件：已装好

rem ---------- 4. 逐项试运行，哪一项崩了就针对性地修 ----------
echo [4/4] 逐项试运行识别组件……
set "FIXED="
:checks
call :check onnx 运算组件
if not "%errorlevel%"=="0" goto fix_onnx
call :check cv2 图像组件
if not "%errorlevel%"=="0" goto fail_check
call :check av 视频读取组件
if not "%errorlevel%"=="0" goto fail_check
call :check ocr 文字识别
if not "%errorlevel%"=="0" goto fix_onnx
echo        语音识别第一次要下载约 500MB 的模型，请耐心等……
call :check asr 语音识别
if not "%errorlevel%"=="0" goto fail_check
echo ok> "%READY%"
echo.
echo 全部准备好了！以后直接双击「启动筛选器.bat」就能用。
timeout /t 3 >nul

:run
start "" "%PYW%" "%~dp0shaixuan.py"
exit /b 0

rem ---------- 运算组件崩溃：换成兼容性更好的旧版本再试一次 ----------
:fix_onnx
if defined FIXED goto fail_check
set "FIXED=1"
echo.
echo        「%WHAT%」没通过，换一个兼容性更好的运算组件版本再试……
"%PY%" -m pip install --disable-pip-version-check --no-warn-script-location "onnxruntime==1.17.3" -i https://pypi.tuna.tsinghua.edu.cn/simple
if not "%errorlevel%"=="0" "%PY%" -m pip install --disable-pip-version-check --no-warn-script-location "onnxruntime==1.17.3" -i https://mirrors.aliyun.com/pypi/simple/
goto checks

rem ---------- 单项检查：%1 检查项  %2 名称 ----------
:check
set "WHAT=%~2"
echo        检查%~2……
"%PY%" "%~dp0shaixuan.py" --check %1
set "CODE=%errorlevel%"
if "%CODE%"=="0" echo        %~2：正常
exit /b %CODE%

rem ---------- 读系统运行库版本（14.xx 的 xx） ----------
:vcver
set "VCMINOR=0"
if not exist "%SystemRoot%\System32\msvcp140.dll" exit /b 0
for /f "delims=" %%v in ('powershell -NoProfile -Command "(Get-Item $env:SystemRoot\System32\msvcp140.dll).VersionInfo.FileMinorPart"') do set "VCMINOR=%%v"
echo %VCMINOR%| findstr /r "^[0-9][0-9]*$" >nul || set "VCMINOR=0"
exit /b 0

rem ---------- 下载：先用 curl，不行再用 PowerShell ----------
:download
echo        %~1
curl.exe -L --fail --retry 2 --connect-timeout 20 -o "%~2" "%~1"
if "%errorlevel%"=="0" exit /b 0
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol='Tls12'; Invoke-WebRequest -UseBasicParsing -Uri '%~1' -OutFile '%~2'"
if "%errorlevel%"=="0" exit /b 0
exit /b 1

:fail_dir
echo.
echo 没法创建文件夹 %HOME_DIR%，把这个窗口截图发给 Claude。
pause
exit /b 1

:fail_net
echo.
echo 下载失败。检查一下网络，公司网络可能拦了下载，然后重新双击「启动筛选器.bat」，会接着装。
pause
exit /b 1

:fail_vc
echo.
echo Windows 运行库还是旧版本（14.%VCMINOR%），一般是因为要重启电脑才能换上新版本。
echo 请重启电脑，然后再双击「启动筛选器.bat」，会接着装。
pause
exit /b 1

:fail_py
echo.
echo Python 没装上。
echo 如果电脑上以前装过 Python 3.11.9，先到「设置 - 应用」里把它卸载，再重新双击「启动筛选器.bat」。
echo 还不行的话，把这个窗口截图发给 Claude。
pause
exit /b 1

:fail_pip
echo.
echo 识别组件下载安装失败。检查网络后重新双击「启动筛选器.bat」，会接着装。
echo 还不行的话，把这个窗口往上翻，红字部分截图发给 Claude。
pause
exit /b 1

:fail_check
echo.
echo 「%WHAT%」试运行没通过，代码 %CODE%。
if "%CODE%"=="-1073741819" echo 这是程序崩溃。可以先重启电脑，再双击「启动筛选器.bat」试一次。
echo 还不行的话，把这个窗口截图发给 Claude，最好往上翻到「检查%WHAT%」那一行开始截。
pause
exit /b 1
