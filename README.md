<div align="center">

<img src="Docs/media/mottainai_banner.gif" alt="MOTTAINAI — Paddle, Collect, Recycle" width="100%">

<sub><i>Concept art — target art direction</i></sub>

# MOTTAINAI

### *もったいない — "What a waste!"*

**A cooperative 3D mobile game where three players share one raft to clean a polluted river, in real time over 5G.**

`Unity 6 LTS` · `C#` · `URP` · `Netcode for GameObjects` · `Dedicated Server` · `Android` · `5G SA`

*Built for the **Green Hackathon 2026**, Swedish-Tunisian Games and Innovation Days*

</div>

---

## 🌊 The game

Three kids. One inflatable raft. A river full of waste, and **2 minutes** to clean it.

MOTTAINAI is a co-op arcade game for kids aged 7 to 12. Each player controls **one part of the raft**, so nobody can win alone:

| Role | What you do | Gesture |
|---|---|---|
| 🚣 **Left paddler** | Paddles on the left side: pushes forward and turns right | Swipe **down** to paddle, swipe **up** to brake |
| 🚣 **Right paddler** | Paddles on the right side: pushes forward and turns left | Swipe **down** to paddle, swipe **up** to brake |
| 🥅 **Collector** | Throws the net to catch floating waste | **Tap** a waste item, or swipe toward it |

> ⚡ **SYNC BOOST:** when both paddlers stroke within 0.25 s of each other, the raft goes straight with **+30 % thrust**. Coordination is the whole game.

Missing players? **Bots** take the empty roles instantly, so the game works with 1, 2 or 3 phones.

## 🎯 Rules

- ⏱️ **2:00** per run. Catch as much waste as you can.
- 🪨 Dodge rocks, icebergs, wrecks, floating logs and **sharks** (the fin appears 1.5 s before one enters your lane). **One hit = game over.**
- 🫧 **Shield** pickup: immune to obstacles for 6 s.
- 🔥 **Combo:** 3 clean catches within 3 s give ×1.5 on the next catch.

| Waste | Bin color | Points |
|---|---|---|
| 🧴 Plastic | 🟡 Yellow | +10 |
| 📦 Paper / Cardboard | 🔵 Blue | +10 |
| 🫙 Glass | 🟢 Green | +15 |
| 🍌 Organic | 🟤 Brown | +5 |
| 🥫 Metal | ⚪ Grey | +20 |
| 💍 Valuable (rare) | ⭐ Gold | +100 |
| ☢️ Toxic / Hazardous | 🔴 Red | −30 at the end |

At the end of each run, the **results screen** shows your score, waste per category, accuracy, distance and how much river you cleaned. Each caught item also teaches a short **eco-fact**.

## 🧠 Why it matters

- **Learn sorting by playing:** every waste type uses its real recycling bin color.
- **Cooperation over competition:** the raft only moves well when the team is in sync.
- **Visible impact:** the river gets clearer as you clean it.

## 📡 The tech differentiator: real-time multiplayer over 5G

MOTTAINAI is built to show what **low-latency mobile networks** make possible.

- **Server-authoritative dedicated server** running at a **60 Hz tick**. Inputs are sent every tick, and catches are validated with **lag compensation**.
- **Full world snapshots at 60 Hz** (raft, 150–300 floating entities, shark, water flow) in a custom compact serializer.
- **Adaptive interpolation** and **client-side prediction**, so your own strokes feel instant.
- **Content pack download at login,** with a live MB/s progress bar.
- **5G SA detection** on Android, plus a request for a **low-latency network slice** on Android 13+ when the network supports it.
- **Net HUD** (3-finger tap): network type, RTT average / p95, jitter, packet loss, tick rate, bandwidth and FPS.
- **Network Lab:** ping burst and throughput tests exported to CSV, to compare **Wi-Fi vs 4G vs 5G NSA vs 5G SA** and **local edge vs cloud**.

```
 Phones (Android)  ──UDP 7777 (game, 60 Hz)──▶  Dedicated server
                   ──TCP 8080 (login, content, telemetry)──▶  (laptop / edge)
```

## 🛠️ Tech stack

| Layer | Choice |
|---|---|
| Engine | Unity 6 LTS (6000.3), URP mobile renderer |
| Language | C# |
| Networking | Netcode for GameObjects 2.x + Unity Transport (UDP) |
| Server | Headless dedicated server (Windows / Linux), built-in HTTP listener |
| Input | New Input System (EnhancedTouch) |
| UI | TextMeshPro |
| Target | Android (min API 26, IL2CPP, ARM64), landscape |

## 🚀 Getting started

### Requirements
- Unity Hub + **Unity 6 LTS** with **Android Build Support** (OpenJDK + Android SDK & NDK Tools) and **Dedicated Server** modules.
- Git.

### Setup
```bash
git clone https://github.com/RayenTouil/Mottainai.git
```
1. Open the project in Unity Hub.
2. Run **Mottainai → Setup Project** from the menu (it generates the scenes, prefabs and player settings).

### Run the server
```bash
build_server.bat
run_server.bat        # -port 7777 -http 8080 -tick 60 -log
```
On startup, the server prints its IP addresses. Open **UDP 7777** and **TCP 8080** in the firewall, and set the Wi-Fi network profile to *Private*.

### Build the Android client
```bash
build_android.bat     # → Builds/Mottainai.apk
```
Install the APK, enter your name and the server IP, then press **PLAY**.

> 💡 **Lowest latency:** put the server and the phones on the **same local network** (Wi-Fi, hotspot or a 5G edge network). See `DEPLOY.md`.

## 📁 Project structure

```
Assets/_Mottainai/
├── Scripts/
│   ├── Core/          # game modes, config, events
│   ├── Gameplay/      # raft physics, spawner, scoring, waste, obstacles
│   ├── Net/           # snapshots, serialization, lag compensation
│   ├── Server/        # dedicated server, HTTP listener, bots
│   ├── Client/        # interpolation, prediction, Net HUD
│   ├── Input/         # swipe & tap gestures
│   ├── UI/            # menus, HUD, results
│   ├── Platform/Android/  # 5G detection, network slicing
│   └── EditorTools/   # Setup Project, build tools
├── Shaders/           # stylized water
└── Resources/         # models, textures, tuning
```

## 🗺️ Roadmap

- [x] Co-op raft, 3 roles + bots
- [x] Dedicated server at 60 Hz, rooms and lobby
- [x] Net HUD and Network Lab
- [ ] Stylized water, environment kit and art polish
- [ ] Waste collection album and cosmetic rewards
- [ ] **Battle mode:** several rafts and teams, 5 min, raft-vs-raft collisions

## 👥 Team

| Member | Role |
|---|---|
| **Rayen TOUIL** | `[role]` |
| **Yassine AFFES** | `[role]` |
| **Med Amine CHAKROUN** | `[role]` |
| **Wajdi ZENTANI** | `[role]` |

Engineering students at **SUP'COM** (École Supérieure des Communications de Tunis).

## 🙏 Credits

- 3D models: [Quaternius](https://quaternius.com) (CC0), [Kenney](https://kenney.nl) (CC0), and models from [Poly Pizza](https://poly.pizza) (see `CREDITS.md`; CC-BY authors are credited there).
- Sounds and icons: [Kenney](https://kenney.nl) (CC0).
- Fonts: [Fredoka](https://fonts.google.com/specimen/Fredoka), [Nunito Sans](https://fonts.google.com/specimen/Nunito+Sans) (OFL).

---

<div align="center">

**Every paddle counts. Every piece matters.** 💚

<sub>Made during the Green Hackathon 2026, Pôle Technologique El Ghazala, Tunisia</sub>

</div>
