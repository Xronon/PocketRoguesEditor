@echo off
rem ------------------------------------------------------------------
rem  Builds the self-test console app (engine and registry checks).
rem  ASCII ONLY - see build.cmd for the reason.
rem ------------------------------------------------------------------
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [X] C# compiler not found. Is .NET Framework 4.x installed?
    exit /b 3
)

if not exist "%~dp0bin-test" mkdir "%~dp0bin-test"

"%CSC%" /nologo /target:exe /platform:anycpu /codepage:65001 ^
    /main:PocketRoguesEditor.SelfTest ^
    /out:"%~dp0bin-test\SelfTest.exe" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /resource:"%~dp0data\catalog.tsv",PocketRoguesEditor.catalog.tsv ^
    "%~dp0src\*.cs" "%~dp0tests\*.cs"

endlocal & exit /b %ERRORLEVEL%
