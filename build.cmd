@echo off
rem ============================================================
rem UpdateToggle - Fallback build script using csc.exe
rem Requires: Windows with .NET Framework 4.x (built-in)
rem ============================================================

setlocal
set DIR=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo Error: csc.exe not found at %CSC%
    echo Please install .NET Framework 4.8 Developer Pack or use Visual Studio.
    exit /b 1
)

echo Building UpdateToggle.exe...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /out:"%DIR%UpdateToggle.exe" ^
    /win32manifest:"%DIR%app.manifest" ^
    /reference:System.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.ServiceProcess.dll ^
    "%DIR%Program.cs"

if %errorlevel% equ 0 (
    echo Build successful: %DIR%UpdateToggle.exe
) else (
    echo Build failed with error code %errorlevel%
    exit /b %errorlevel%
)

endlocal
