@echo off
rem ------------------------------------------------------------------
rem  Build script. ASCII ONLY - cmd.exe parses this file byte by byte
rem  using the current console codepage, so any non-ASCII character
rem  here breaks parsing on machines with a different codepage.
rem
rem  Uses the C# compiler that ships inside Windows itself, so no SDK,
rem  no Visual Studio and no downloads are required.
rem  Output: bin\PocketRoguesEditor.exe - a single portable file.
rem ------------------------------------------------------------------
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [X] C# compiler not found. Is .NET Framework 4.x installed?
    pause
    exit /b 3
)

if not exist "%~dp0bin" mkdir "%~dp0bin"

echo Building with %CSC%

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
    /out:"%~dp0bin\PocketRoguesEditor.exe" ^
    /win32manifest:"%~dp0src\app.manifest" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /resource:"%~dp0data\catalog.tsv",PocketRoguesEditor.catalog.tsv ^
    "%~dp0src\*.cs"

set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" (
    echo [OK] bin\PocketRoguesEditor.exe
) else (
    echo [X] Build failed, exit code %RC%
)
endlocal & exit /b %RC%
