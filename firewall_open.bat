@echo off
rem Opens Mottainai server ports for ALL network profiles (Private, Public, Domain). Run as administrator.
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo [Mottainai] Needs admin. Right-click firewall_open.bat ^> Run as administrator.
  exit /b 1
)
netsh advfirewall firewall delete rule name="Mottainai HTTP TCP 8080" >nul 2>&1
netsh advfirewall firewall delete rule name="Mottainai Game UDP 7777" >nul 2>&1
netsh advfirewall firewall delete rule name="Mottainai Lab UDP 7778" >nul 2>&1
netsh advfirewall firewall add rule name="Mottainai HTTP TCP 8080" dir=in action=allow protocol=TCP localport=8080 profile=any
netsh advfirewall firewall add rule name="Mottainai Game UDP 7777" dir=in action=allow protocol=UDP localport=7777 profile=any
netsh advfirewall firewall add rule name="Mottainai Lab UDP 7778" dir=in action=allow protocol=UDP localport=7778 profile=any
echo [Mottainai] Firewall open: TCP 8080, UDP 7777, UDP 7778 (all profiles).
