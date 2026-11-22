@echo off
rem Deploy the Cloudflare Worker and write its URL into the Unity client config.
setlocal
set ENVARG=
if not "%~1"=="" set ENVARG=--env %~1
cd /d "%~dp0server" || exit /b 1
call npm i || exit /b 1
call node tools\genpack.mjs || exit /b 1
call npx wrangler deploy %ENVARG% > "%TEMP%\mottainai_deploy.log" 2>&1
set ERR=%ERRORLEVEL%
type "%TEMP%\mottainai_deploy.log"
if not "%ERR%"=="0" exit /b %ERR%
powershell -NoProfile -Command "$u = (Select-String -Path $env:TEMP\mottainai_deploy.log -Pattern 'https://[a-z0-9.-]+\.workers\.dev' | Select-Object -First 1).Matches.Value; if (-not $u) { Write-Error 'URL not found'; exit 1 }; Set-Content -Encoding ascii -Path '%~dp0Mottainai\Assets\_Mottainai\Resources\server_config.json' -Value ('{\"baseUrl\":\"' + $u + '\"}'); Write-Host ('DEPLOYED URL: ' + $u)"
endlocal
