# PLAYIT — server on the team PC, phones over 4G/5G (playit.gg free tunnels)
1. Start the server: `run_server.bat` (game UDP 7777 + API TCP 8080 on 0.0.0.0; Windows sleep is blocked while it runs).
2. Install the playit agent (https://playit.gg/download), run it, open its claim link, log in (free account).
3. playit dashboard > Tunnels > Add: **Custom UDP**, local address 127.0.0.1, local port **7777** = GAME address.
4. Add: **TCP**, local address 127.0.0.1, local port **8080** = API address.
5. Each tunnel lists its public address `name.ply.gg:PORT` (dashboard + agent window). Public ports are NOT 7777/8080.
6. Phones (Wi-Fi OFF): GAME host + port = UDP tunnel, API host + port = TCP tunnel, then PLAY (pasting `name.ply.gg:PORT` in a host field fills its port).
7. Check from a phone browser: `http://<API host>:<API port>/ping` returns `{"ok":true,...}`. Network Lab pings go through the same UDP tunnel.
