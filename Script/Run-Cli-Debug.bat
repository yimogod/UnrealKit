@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%..\UnrealKit\UnrealKit.Cli\bin\Debug\net9.0\UnrealKit.Cli.exe"

if not exist "%EXE%" (
    echo Debug CLI executable was not found. Run Build-Debug.bat first.
    exit /b 1
)

"%EXE%" %*
exit /b %errorlevel%
