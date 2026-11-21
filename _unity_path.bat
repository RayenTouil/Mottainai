@echo off
rem Resolves UNITY (editor exe matching ProjectVersion.txt). Override with UNITY_EXE.
set "PROJ=%~dp0Mottainai"
for /f "tokens=2" %%v in ('findstr /b /c:"m_EditorVersion:" "%PROJ%\ProjectSettings\ProjectVersion.txt"') do set "UVER=%%v"
set "UNITY=%ProgramFiles%\Unity\Hub\Editor\%UVER%\Editor\Unity.exe"
if defined UNITY_EXE set "UNITY=%UNITY_EXE%"
if not exist "%UNITY%" (
  echo [Mottainai] Unity %UVER% not found at "%UNITY%". Set UNITY_EXE to Unity.exe.
  exit /b 1
)
if not exist "%~dp0Builds" mkdir "%~dp0Builds"
exit /b 0
