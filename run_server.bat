@echo off
rem Usage: run_server.bat [-headless] [-port 7777] [-http 8080] [-tick 60]
setlocal
set "EXE=%~dp0Mottainai\Builds\Server\Windows\MottainaiServer.exe"
if not exist "%EXE%" ( echo [Mottainai] Server not built. Run build_server.bat first. & exit /b 1 )
set "MODE=-screen-fullscreen 0 -screen-width 1100 -screen-height 640"
if /i "%~1"=="-headless" ( set "MODE=-batchmode -nographics" & shift )
echo [Mottainai] Starting server: UDP 7777 game + TCP 8080 HTTP on 0.0.0.0 - expose both with playit (PLAYIT.md)
echo [Mottainai] Addresses are printed in the server window and in Mottainai\Builds\Server\Windows\ServerData\server_status.txt
"%EXE%" -server %MODE% -port 7777 -http 8080 -tick 60 -log -logFile - %1 %2 %3 %4 %5 %6
