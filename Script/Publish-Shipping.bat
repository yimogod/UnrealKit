@echo off
setlocal

rem ============================================================
rem  UnrealKit ? Self-contained Publish (Shipping)
rem  Produces a standalone folder under Script\Publish\UnrealKit
rem  that includes the CLI, desktop app, and required runtimes.
rem ============================================================

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "SOLUTION=%REPO_ROOT%\UnrealKit\UnrealKit.sln"
set "PUBLISH_DIR=%SCRIPT_DIR%Publish\UnrealKit"

echo --- Restoring solution ---
dotnet restore "%SOLUTION%" --runtime win-x64
if errorlevel 1 (
    echo SOLUTION RESTORE FAILED.
    exit /b 1
)

echo --- Publishing CLI (self-contained win-x64) ---
dotnet publish "%REPO_ROOT%\UnrealKit\UnrealKit.Cli\UnrealKit.Cli.csproj" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    --output "%PUBLISH_DIR%"
if errorlevel 1 (
    echo CLI PUBLISH FAILED.
    exit /b 1
)

echo --- Publishing Desktop (self-contained win-x64) ---
dotnet publish "%REPO_ROOT%\UnrealKit\UnrealKit.Desktop\UnrealKit.Desktop.csproj" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    --output "%PUBLISH_DIR%"
if errorlevel 1 (
    echo DESKTOP PUBLISH FAILED.
    exit /b 1
)

echo --- Copying documentation ---
if exist "%REPO_ROOT%\README.md"    copy /y "%REPO_ROOT%\README.md"    "%PUBLISH_DIR%\" >nul
if exist "%REPO_ROOT%\CHANGELOG.md" copy /y "%REPO_ROOT%\CHANGELOG.md" "%PUBLISH_DIR%\" >nul
if exist "%REPO_ROOT%\LICENSE"      copy /y "%REPO_ROOT%\LICENSE"      "%PUBLISH_DIR%\" >nul

echo.
echo ==========================================
echo  Publish complete.
echo  Output: %PUBLISH_DIR%
echo ==========================================
exit /b 0
