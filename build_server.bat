@echo off
setlocal
call "%~dp0_unity_path.bat" || exit /b 1
echo [Mottainai] Building Windows server with %UNITY% (close the Unity editor first)...
"%UNITY%" -batchmode -quit -projectPath "%PROJ%" -executeMethod Mottainai.EditorTools.BuildTools.BuildServerWindows -logFile "%~dp0Builds\build_server.log" %*
set ERR=%ERRORLEVEL%
if %ERR% neq 0 ( echo [Mottainai] FAILED - see Builds\build_server.log & exit /b %ERR% )
echo [Mottainai] OK: Mottainai\Builds\Server\Windows\MottainaiServer.exe  -  start it with run_server.bat
