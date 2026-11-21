@echo off
setlocal
call "%~dp0_unity_path.bat" || exit /b 1
echo [Mottainai] Building Android APK with %UNITY% (close the Unity editor first)...
"%UNITY%" -batchmode -quit -projectPath "%PROJ%" -executeMethod Mottainai.EditorTools.BuildTools.BuildAndroid -logFile "%~dp0Builds\build_android.log" %*
set ERR=%ERRORLEVEL%
if %ERR% neq 0 ( echo [Mottainai] FAILED - see Builds\build_android.log & exit /b %ERR% )
echo [Mottainai] OK: Mottainai\Builds\Android\Mottainai.apk
echo Install: adb install -r Mottainai\Builds\Android\Mottainai.apk
