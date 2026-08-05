@echo off
setlocal

rem UnrealKit maps Shipping builds to the .NET Release configuration.
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "SOLUTION=%REPO_ROOT%\UnrealKit\UnrealKit.sln"

dotnet build "%SOLUTION%" --configuration Release
if errorlevel 1 (
    echo Shipping build failed.
    exit /b 1
)

echo Shipping build completed.
exit /b 0
