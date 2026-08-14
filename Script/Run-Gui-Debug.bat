@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%..\UnrealKit\Output\Bin\UnrealKit.Desktop\Debug\net9.0-windows\UnrealKit.Desktop.exe"

if not exist "%EXE%" (
    echo Debug GUI executable was not found. Run Build-Debug.bat first.
    exit /b 1
)

start "UnrealKit Debug" "%EXE%" %*
exit /b 0
