@echo off
cd /d "%~dp0"
echo 会重新下载安装识别组件，大约 10 分钟。
echo 开始前请先关掉素材筛选器的窗口，不然有的文件被占着换不掉。
echo 下载好的语音模型会保留，不用重新下载。
echo.
pause
call "%~dp0启动筛选器.bat" repair
