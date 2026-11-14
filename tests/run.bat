@echo off
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework C# compiler not found.
    exit /b 1
)
if not exist "dist" mkdir "dist"
"%CSC%" /nologo /target:exe /main:PicGrab.BehaviorTests /codepage:65001 /out:dist\BehaviorTests.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll Program.cs PhotoGallery.cs PhotoPreview.cs tests\BehaviorTests.cs
if errorlevel 1 exit /b 1
dist\BehaviorTests.exe
exit /b %errorlevel%
