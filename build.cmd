@echo off
rem Builds dist\SWYF-Speech-Fixer.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No downloads, no extra tools needed.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find %CSC%
  echo Install .NET Framework 4.8 ^(built into Windows 10/11^).
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /win32manifest:app.manifest /out:dist\SWYF-Speech-Fixer.exe src\Fixer.cs src\CpuFix.cs src\Speedup.cs src\VulkanProbe.cs src\AssemblyInfo.cs
if errorlevel 1 exit /b 1
echo.
echo Built dist\SWYF-Speech-Fixer.exe
certutil -hashfile dist\SWYF-Speech-Fixer.exe SHA256
