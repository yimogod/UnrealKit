@echo off
setlocal

rem UnrealKit maps Shipping builds to the .NET Release configuration.
set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%..\UnrealKit\Output\Bin\UnrealKit.Desktop\Release\net9.0-windows\UnrealKit.Desktop.exe"

if not exist "%EXE%" (
    echo Shipping GUI executable was not found. Run Build-Shipping.bat first.
    exit /b 1
)

start "UnrealKit Shipping" "%EXE%" %*
exit /b 0
