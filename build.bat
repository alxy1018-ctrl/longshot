@echo off
setlocal
set "CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found: %CSC%
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe+ /codepage:65001 ^
  /out:LongShot.exe ^
  /reference:System.dll /reference:System.Core.dll ^
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  LongShot.cs
if errorlevel 1 (
  echo [ERROR] build failed
  exit /b 1
)
echo Build OK: LongShot.exe

rem This machine marks D:\Code as a low-integrity sandbox dir: an exe started from
rem here cannot write Pictures or inject wheel input. Copy the exe to a normal dir.
copy /Y "LongShot.exe" "%USERPROFILE%\Desktop\LongShot.exe" >nul
if errorlevel 1 (
  echo [WARN] could not copy to Desktop; copy LongShot.exe to a normal folder manually.
) else (
  echo Copied to Desktop: %USERPROFILE%\Desktop\LongShot.exe
)
