@echo off
setlocal

rem UnrealKit maps Shipping builds to the .NET Release configuration.
set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%..\UnrealKit\UnrealKit.Cli\bin\Release\net9.0\UnrealKit.Cli.exe"

if not exist "%EXE%" (
    echo Shipping CLI executable was not found. Run Build-Shipping.bat first.
    exit /b 1
)

"%EXE%" %*
exit /b %errorlevel%
