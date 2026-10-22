@echo off
rem Builds dist\SWYF-Community-Fixer.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No downloads, no extra tools needed. Every lang\*.txt is embedded, so adding a language = adding a file.
setlocal enabledelayedexpansion
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find %CSC%
  echo Install .NET Framework 4.8 ^(built into Windows 10/11^).
  exit /b 1
)
if not exist dist mkdir dist
set RES=
for %%f in (lang\*.txt) do set RES=!RES! /resource:%%f,lang.%%~nxf
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll !RES! /out:dist\SWYF-Community-Fixer.exe src\*.cs
if errorlevel 1 exit /b 1
echo.
echo Built dist\SWYF-Community-Fixer.exe
certutil -hashfile dist\SWYF-Community-Fixer.exe SHA256
