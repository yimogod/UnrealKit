@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "SOLUTION=%REPO_ROOT%\UnrealKit\UnrealKit.sln"

dotnet build "%SOLUTION%" --configuration Debug
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Debug build completed.
exit /b 0
