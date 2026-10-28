@echo off
rem Builds screenworm.scr (screensaver, GUI subsystem) and screenworm.exe (same program, console subsystem
rem for --stats/--screenshot/--install) with the C# compiler that ships inside .NET Framework 4.x.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo csc.exe not found - .NET Framework 4.x is required & exit /b 1 )
if not exist bin mkdir bin
set ICON=
if exist screenworm.ico set ICON=/win32icon:screenworm.ico
set COMMON=/nologo /optimize+ /warn:1 /platform:x64 %ICON% /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll *.cs
"%CSC%" /target:winexe /out:bin\screenworm.exe %COMMON% || exit /b 1
"%CSC%" /target:exe /out:bin\screenworm-cli.exe %COMMON% || exit /b 1
copy /y bin\screenworm.exe bin\screenworm.scr >nul
echo built bin\screenworm.exe (GUI), bin\screenworm-cli.exe (console) and bin\screenworm.scr
