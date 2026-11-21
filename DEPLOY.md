# MOTTAINAI — Deploy (LAN / demo laptop)

Internet play through playit.gg tunnels (team PC + phones on 4G/5G): see `PLAYIT.md`.

## 1. Build (Unity editor closed)
| What | Windows | Linux/macOS |
|---|---|---|
| Server | `build_server.bat` | `./build_server.sh` |
| Android APK | `build_android.bat` (add `-dev` for a development build) | `./build_android.sh` |

Outputs: `Mottainai/Builds/Server/Windows/MottainaiServer.exe`, `Builds/Mottainai.apk`. Logs in `Builds/*.log`.
Install on a phone: `adb install -r Builds/Mottainai.apk`.

## 2. Run the server
```
run_server.bat               # small status window with IPs, rooms, tick rate
run_server.bat -headless     # console only (-batchmode -nographics)
```
Full command line: `MottainaiServer.exe -server -port 7777 -http 8080 -tick 60 -log [-batchmode -nographics]`.
On start it prints the local ports, "Waiting for playit tunnels" and the LAN IPv4 addresses (window + `ServerData/server_status.txt` next to the exe), then a `[STATUS]` line every 5 s on stdout. On LAN, type a LAN IP in both host fields of the phone login screen.
Session/telemetry/lab logs: `Mottainai/Builds/Server/Windows/ServerData/logs/session_*.csv`, `lab_results.csv`.
Tuning override: edit `ServerData/tuning.json` (served to clients in the Content Pack), restart.

## 3. Ports
| Port | Proto | Use |
|---|---|---|
| 7777 | UDP | game (Netcode for GameObjects / Unity Transport, 60 Hz snapshots) |
| 8080 | TCP | HTTP: login, room list, Content Pack (~36 MB), telemetry, Network Lab download |

## 4. Firewall (Windows, admin PowerShell / cmd)
```
netsh advfirewall firewall add rule name="Mottainai UDP 7777" dir=in action=allow protocol=UDP localport=7777
netsh advfirewall firewall add rule name="Mottainai TCP 8080" dir=in action=allow protocol=TCP localport=8080
```
Also accept the Windows "allow access" popup on first launch (tick Private **and** Public if the 5G CPE network is classified Public).
If the log says `[HTTP] ... localhost fallback`: LAN phones cannot log in (the local playit agent still can); run once as admin `netsh http add urlacl url=http://+:8080/ user=Everyone`.
Linux: `sudo ufw allow 7777/udp && sudo ufw allow 8080/tcp`. (Network Lab echo uses the game port 7777.)

## 5. Reaching the laptop from phones on 5G SA
- The laptop must be on the **same 5G SA network** as the phones, e.g. plugged (Ethernet/Wi-Fi) into the hackathon 5G CPE/router whose WAN is the 5G SA slice, or on the operator's edge/private-5G DNN with a routable IP.
- Phones on the public carrier APN usually sit behind **carrier CGNAT**: they can reach out, but nothing can reach *in*. A laptop that is itself behind CGNAT (tethered to a phone, consumer 5G) is **not reachable** from other phones. Use the CPE LAN/edge network, a private APN, or an operator-provided public/edge IP.
- Quick check from a phone browser: `http://<laptop-ip>:8080/ping` must return `{"ok":true}`.
- Wi-Fi fallback for rehearsals: phones + laptop on the same Wi-Fi; no change needed.

## 6. Demo flow
1. Start server, note IP. 2. On each phone: name + game host/port + API host/port → PLAY (Content Pack downloads with live MB/s; FORCE RE-DOWNLOAD toggle to show it again).
3. One phone: CREATE ROOM → code; others JOIN code (or tap it in the list) → pick roles → START. Missing roles = bots; a phone that drops is replaced by a bot within ~1 s and gets its seat back on reconnect.
4. Net HUD is on by default (3-finger tap toggles). NETWORK LAB on login/lobby/results: run once per network (Wi-Fi / 4G / 5G NSA / 5G SA) → CSV on phone + server.

Editor-only dev: login screen toggle "HOST SERVER IN EDITOR" runs server+client in one process (keyboard: Q/E or W/S paddle, Space throw, H HUD).
Smoke test without phones: `MottainaiServer.exe -client -autoplay -connect 127.0.0.1 [-gameport 7777 -apiport 8080] [-lab] -batchmode -nographics -logFile -`.
