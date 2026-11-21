@echo off
rem Install Builds\Mottainai.apk on every connected Android device and launch it.
setlocal enabledelayedexpansion
call "%~dp0_unity_path.bat" || exit /b 1
for %%d in ("%UNITY%") do set "UDIR=%%~dpd"
set "ADB=%UDIR%Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
if defined ADB_EXE set "ADB=%ADB_EXE%"
if not exist "%ADB%" ( where adb >nul 2>&1 && set "ADB=adb" || ( echo [Mottainai] adb not found: "%ADB%" & exit /b 1 ) )
set "APK=%~dp0Builds\Mottainai.apk"
if not exist "%APK%" ( echo [Mottainai] %APK% missing - run build_android.bat & exit /b 1 )
set N=0
for /f "skip=1 tokens=1,2" %%a in ('"%ADB%" devices') do (
  if "%%b"=="device" (
    set /a N+=1
    echo [Mottainai] %%a: installing...
    "%ADB%" -s %%a install -r "%APK%"
    "%ADB%" -s %%a shell monkey -p com.supcom.mottainai -c android.intent.category.LAUNCHER 1 >nul 2>&1
    echo [Mottainai] %%a: launched com.supcom.mottainai
  ) else if not "%%b"=="" echo [Mottainai] %%a: %%b ^(accept the USB debugging prompt on the phone^)
)
if !N!==0 ( echo [Mottainai] No device found. Plug the phone, enable USB debugging, accept the prompt. & exit /b 1 )
echo [Mottainai] Done: !N! device^(s^)
