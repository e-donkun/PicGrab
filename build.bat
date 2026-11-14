@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework C# compiler not found.
    exit /b 1
)
if not exist "dist" mkdir "dist"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32manifest:app.manifest /out:dist\PicGrab.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll Program.cs PhotoGallery.cs PhotoPreview.cs
if errorlevel 1 exit /b 1
echo Built: %CD%\dist\PicGrab.exe
