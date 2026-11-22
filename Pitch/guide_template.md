# MOTTAINAI — final jury presentation guide

Deck: `Pitch/MOTTAINAI_Final.pptx` (backup: `Pitch/MOTTAINAI_Final.pdf`). Built by `Pitch/build_final.js`. Sections B, C and E below are generated from the deck itself, so they always match it.

Inputs merged:
- **Presentation A:** `Pitch/MOTTAINAI_Pitch.pptx` (22 Nov 2026, English, 12 slides + 1 hidden, built from the code).
- **Presentation B:** `Downloads/Green Hackathon - THE PITCH - Mottainai (1).pptx` (2 Nov 2026, French, 10 slides, hackathon application form). The other copy without "(1)" is an older draft with "[Nom de l'équipe]" placeholders; the "(1)" version is the newer one (team name filled in).
- **The code and logs** in this repo (source of truth for every number).

---

## A. Comparative analysis

**Presentation A: strengths**
- One clear story (sea → rivers → kids → game → demo → tech → 5G → impact), verified stats with sources (WWF 2019, OECD 2022).
- Every game number comes from `server/src/tuning.json`; latency numbers are real measurements.
- Real architecture (WebSocket → Cloudflare → Durable Object), a timed word-for-word script, a demo plan and Plan B.
- Consistent visual identity, real screenshots.

**Presentation A: weaknesses**
- 12 slides for ~4 minutes: some slides are thin. The voice slide spends 15 s on a feature that does not exist; the data slide shows a bot-only match.
- Engineering appears only as keywords. The challenges and the decisions behind them are not told.
- Impact is a list of next steps, with no plan to measure learning.
- No team name, no story of how the idea evolved, and a 1:00 demo (the new brief asks for 30–45 s).

**Presentation B: strengths**
- Strong education angle: target ages 7–12 ("the age when good habits form"), a safety rule (never touch hazardous waste bare-handed), habits at home.
- Impact framed for the child: an eco-tip at the right moment, an end-of-run report ("he sees that his actions count").
- Data idea worth keeping: log every catch to find which waste kids confuse most.
- Connectivity ideas: online leaderboard, challenges between friends and schools; AI that adapts difficulty.
- Team identity: team name **5le9a Deyra**, school, four telecom engineering students. A 30-second elevator pitch.

**Presentation B: weaknesses**
- It is a questionnaire (checkboxes, long paragraphs), in French, with almost no visuals (only the hackathon poster).
- It describes the **application concept, not the prototype**: a solo 3-lane endless runner across 4 worlds.
- No architecture, no numbers, multiplayer unchecked.

**Redundant in both:** title/elevator pitch, the end-of-run report, eco-tips, impact statements. Merged into one idea slide, one loop slide and one impact slide.

**Contradictions, resolved with the code as evidence**

| Topic | B (application, 2 Nov) | A / code (prototype, 22 Nov) | Final deck |
|---|---|---|---|
| Genre | Solo 3-lane endless runner | 3-player co-op raft on a river | Co-op raft; the evolution is told on slide 3 |
| Worlds | Desert, city, forest, snow | One river course (~650 m) | One river now; new rivers and worlds are **Later** |
| Waste | 4 families (+10/+15/+30/very toxic) | 7 bin colors (+10…+100, toxic −30 at the end) | 7 bin colors |
| Lives | 2 hearts | 1 hit = game over | 1 hit |
| Shield | Eco-Shield 15 s | Shield 6 s (+ magnet, turbo) | 6 s |
| Multiplayer | Not planned | Real-time, 3 seats, bots fill | Implemented |
| Sorting center at run end | Core feature | Not in code | **Next** |
| Eco-tips per catch | Core feature | Not in code (README claims it) | **Next** |
| Analytics "which waste kids confuse" | Planned | Only network telemetry is stored; gameplay results are shown, not stored | Measurement **plan** |
| Network | Online leaderboard | WebSocket (README still says UDP 7777 / NGO: outdated) | WebSocket on Cloudflare |

**Missing in both, added to the final deck**
- The engineering challenges and the decisions they forced (slide 8).
- A plan to measure impact without inventing results (slide 9).
- A clear "implemented vs Next vs Later" split (slides 9 and 11 of A were mixed).
- Measured performance with sources (slide 8), security and privacy answers (section G), a backup video (hidden slide).

**Claims to verify before the jury (flagged, not invented)**
1. The reason for the pivot from runner to co-op ("cleaning a river is teamwork"): confirm the team agrees with this wording.
2. The team name on screen: "5le9a Deyra" (as written in B). Check spelling and how to say it.
3. "60 fps" is one test phone (merge check, 60.1 fps average, worst frame 33.2 ms), not every phone.
4. "43 ms" was measured from a PC test client to a Marseille room; "71 ms" is a phone on Wi-Fi to the Paris room. There is **no 5G SA measurement yet**.
5. The banner and slide 1 art are concept art, not in-game captures (labelled on slide 3).
6. Tunisia-specific plastic figures were **not used**: the WWF sources disagree with each other.
7. The speaker split and who holds Phone B in the demo.

**What was kept, merged, dropped**
- Kept from A: hook stat, rivers stat, roles + sync boost, rules, demo slide, architecture, measured latency, closing line.
- Kept from B: team name, ages 7–12, the safety reflex, eco-tips and sorting center (as Next), leaderboards / school challenges, worlds and adaptive difficulty (as Later), the "which waste kids confuse" idea (as a measurement plan).
- Merged: A's rules + data slides → one gameplay-loop slide; A's 5G slide → measurements on the engineering slide + roadmap; A's impact + next steps → one impact-and-roadmap slide.
- Dropped: A's voice slide (feature not built: now one roadmap line), A's bot-match chart, B's checkbox pages, B's specialties list.

---

## B. Final presentation structure

{{B}}

---

## C. Complete slide content
{{C}}

---

## D. Technical diagrams

All four can be rebuilt in PowerPoint with rounded rectangles and arrows (diagram 1 is already built natively on slide 7). On GitHub the Mermaid blocks render as pictures.

### 1. Multiplayer architecture

```mermaid
flowchart LR
  subgraph PH["Android phones · Unity C# (URP)"]
    A["Phone A · left paddler<br/>gestures, rendering, own-raft prediction"]
    B["Phone B · collector"]
  end
  subgraph CF["Cloudflare · edge in Marseille (MRS)"]
    W["Worker<br/>routing · login token · TLS"]
    R["Game room · Durable Object<br/>authoritative sim 60 Hz · bots · lag compensation · results"]
    P["Static assets<br/>content pack 36 MB · sha256"]
    T["Telemetry Durable Object<br/>SQLite → /api/telemetry.csv"]
  end
  A <-- "wss · binary protocol<br/>events up / full snapshots 60 Hz down" --> W
  B <-- "wss" --> W
  W <--> R
  A -- "HTTPS (first launch)" --> P
  A -- "HTTPS (Network Lab)" --> T
```

- **Server responsibilities:** course generation from a seed, raft physics, obstacles and shark, waste, power-ups, collisions, scoring, combo, timer, bots, results.
- **Synchronization:** the server is authoritative. Phones send events only (stroke, back-paddle, net throw, ping every 500 ms, stats every 2 s). The server sends full snapshots at 60 Hz (30 Hz if a phone reports a backlog), so a late packet is simply replaced by the next one.
- **Latency hiding:** own-raft prediction with acknowledged strokes; adaptive interpolation delay = max(2 snapshot intervals, jitter p95); net hits rewound by RTT/2 (cap 150 ms); TCP_NODELAY on the client; drift-free server tick (send jitter p95 14.7 → 0.7 ms).
- **Limits:** 3 seats per room; each room is its own Durable Object, so rooms scale out independently. Free plan: 100,000 requests/day; a room stops 30 s after the last human leaves. Not load-tested beyond a few rooms.
- **Security:** TLS everywhere, random tokens, no accounts or personal data, cooldowns enforced by the server.

### 2. Gameplay loop

```mermaid
flowchart LR
  S["Countdown 3 s"] --> P["Paddle in sync<br/>(both within 0.25 s → +30 %)"]
  P --> C["Catch waste<br/>tap → net (range 7 m)"]
  C --> K["Score<br/>bin points · combo ×1.5"]
  K --> P
  P --> O{"Obstacle hit?"}
  O -- "shield active" --> P
  O -- "no shield" --> G["Game over"]
  K --> TU{"2:00 elapsed?"}
  TU -- yes --> E["Time up"]
  G --> RS["Results: waste per bin · toxic penalty · accuracy · distance · kg cleaned"]
  E --> RS
  RS --> RM["Rematch"]
```

### 3. Player journey

```mermaid
flowchart LR
  T["Title<br/>enter name"] --> L["PLAY: login token<br/>+ content pack (MB/s shown, cached)"]
  L --> Q["Quick Play lobby<br/>pick a role · bots fill"]
  T --> F["FRIENDS: create / join a room code"] --> Q
  Q --> M["2:00 match"] --> R["Results"] --> Q
  R --> T
```

### 4. Roadmap

```mermaid
timeline
  title MOTTAINAI roadmap
  Now (22 Nov 2026) : Co-op raft, 3 roles + bots : Server-authoritative 60 Hz on Cloudflare : Network Lab + telemetry CSV
  Next (weeks) : 5G SA test + latency slice : Eco-tip after each catch : Sorting center at run end : Team voice chat : Classroom pilot (pre/post quiz)
  Later : New rivers and worlds (desert, city, forest, snow) : School leaderboards and challenges : Battle mode, team vs team : Adaptive difficulty
```

---

## E. Presentation script

Natural, short sentences; [CLICK] = next slide, [HAND OVER TO …] = next speaker.
{{E}}

---

## F. Live demo plan (45 s)

**Before the talk (T−30 min)**
1. Both phones charged, Do Not Disturb ON, screen timeout 10 min, Wi-Fi OFF and mobile data ON (or one shared hotspot if 5G is weak in the room).
2. Open the app on both phones: PLAY → content pack already downloaded → Quick Play lobby. Phone A = LEFT PADDLER, Phone B = COLLECTOR, the bot takes RIGHT PADDLER. Leave both on the lobby screen.
3. Mirror Phone A to the projector (USB + scrcpy, an HDMI adapter, or a document camera). Test the image once.
4. Put the backup video in `Pitch/media/demo_backup.mp4` (trim one of the screen recordings to 40 s) and insert it on hidden slide 7 (Insert → Video).

**Sequence**

| Time | Action | Narration |
|---|---|---|
| 0:00 | Slide 6 shown → switch the projector to the phone, press START | "Let's prove it live. Two phones, one bot." |
| 0:05 | Three synced strokes, point at the SYNC ring | "When we stroke together, the raft surges." |
| 0:12 | Collector taps 2–3 waste items | "Wajdi taps a bottle, the net flies, and it scores." |
| 0:25 | Point at the Net pill (network, RTT) | "The server runs all of this sixty times a second." |
| 0:30 | Steer into a rock | "Rock ahead… game over." |
| 0:35 | Results screen | "Waste per bin, accuracy, and kilos of river cleaned." |
| 0:45 | Switch back to the deck, [CLICK] | — |

**Backup strategy (in this order)**
1. A phone drops: keep playing; the server gives its seat to a bot.
2. Mobile data fails: switch both phones to the hotspot and press START again (once).
3. Still nothing after 10 s: type `7` + Enter in slideshow mode (hidden slide) and play the backup video, then type `8` + Enter.
4. No video either: narrate the three screenshots on the hidden slide.

---

## G. Jury preparation

1. **Why a game?** Kids 7–12 learn by doing. Every waste uses its real bin color, and the raft only moves when the team cooperates.
2. **Do you have proof of impact?** Not yet, and we don't claim it. The prototype works; the next step is a classroom pilot with a sorting quiz before and after playing, plus sorting accuracy per bin from the game.
3. **Why did you leave the solo runner from your application?** Cleaning a river is teamwork, and cooperation is the lesson. It also made real-time networking our engineering challenge. The runner's best ideas (sorting center, eco-tips, worlds) are on the roadmap.
4. **Why TCP/WebSocket and not UDP?** Cloudflare offers custom UDP only on Enterprise. We send full snapshots (no deltas) at 60 Hz, so a late packet is replaced by the next one; we disable Nagle (TCP_NODELAY) and fixed server tick drift (send jitter p95 0.7 ms). Measured: 60 snapshots/s, 226 kbps per phone.
5. **How do you keep a 0.25 s sync window fair with latency?** Each phone predicts its own raft immediately; other objects use an adaptive interpolation buffer; the server rewinds net throws by half the round trip (max 150 ms). The server alone decides hits and scores.
6. **How many players can it handle?** 3 seats per room; every room is a separate Durable Object, so rooms scale independently. Limits today: the free plan (100,000 requests/day). We have not load-tested many rooms yet.
7. **What about cheating and security?** Phones never send positions or scores, only events; the server enforces cooldowns and validates hits. Everything runs over TLS with random tokens.
8. **Privacy for kids?** No account, no email, no location: a display name and a random token. The server stores network test numbers only (device model, network type, latency, bandwidth). A school pilot would add parental consent.
9. **Why 5G Standalone?** On Wi-Fi we measured 71 ms average with spikes (p95 88 ms, jitter 16 ms). 5G SA and a low-latency slice aim at stable latency anywhere. The app already detects 5G SA/NSA and requests the slice on Android 13+; the 5G SA measurement is our next test.
10. **Business model?** Free for schools. Ideas to validate: sponsorship by municipalities, NGOs and recycling companies; cosmetic rewards. Nothing signed.
11. **What would you build next with one more week?** The 5G SA test, eco-tips after each catch, and the sorting center at the end of a run.

---

## H. Quality check (self-evaluation)

| Criterion | Score /5 | Note |
|---|---|---|
| Storytelling | 4 | Six acts, one idea per slide; the runner → raft evolution adds credibility |
| Clarity of the problem | 4 | Two verified stats + the "taught, not practised" gap |
| Strength of the solution | 4 | Co-op mechanic tied to the lesson; bots remove the 3-player barrier |
| Game identity | 4 | Real phone screenshot, bin colors as a visual motif |
| Gameplay explanation | 5 | Roles slide + loop slide + live demo |
| Technical credibility | 5 | Real architecture, challenges with decisions, dated measurements |
| Visual consistency | 4 | One palette, two fonts, same card/circle motif; PowerPoint rendering not checked on this PC |
| Environmental impact | 3 | Honest but unmeasured: the pilot plan is the answer |
| Innovation | 4 | Real-time co-op learning game on edge servers, 5G SA ready |
| Demonstration | 4 | 45 s scripted, three-level backup |
| Time management | 5 | 4:10 planned for 10 slides, about 4:00 of speech: margin for a demo overrun |
| Professionalism | 4 | Sources on every stat; open items flagged above |

**Before presenting:** install `Mottainai/Assets/_Mottainai/Resources/Fonts/Fredoka-SemiBold.ttf` on the presenting PC (titles; otherwise PowerPoint substitutes a default font). Body text is Calibri. Copy the PDF to a USB stick as the last-resort backup.
