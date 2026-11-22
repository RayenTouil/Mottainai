# MOTTAINAI — pitch script (Green Hackathon 2026)

Target: 4:10 + up to 20 s of demo overrun (4:30 max). Pace: about 140 words per minute.
Spoken words outside the demo: 399 (2:51) + live demo 1:00 = **3:51**.

Before the talk: install `Mottainai/Assets/_Mottainai/Resources/Fonts/Fredoka-SemiBold.ttf` on the presenting PC (titles use it; without it PowerPoint substitutes a default font). Body text uses Calibri (Nunito Sans is not in the repo).

| Slide | Title | Time | Speaker | Words |
|---|---|---|---|---|
| 1 | Hook: the sea is choking | 0:25 | Yassine AFFES | 45 |
| 2 | The real problem | 0:15 | Yassine AFFES | 35 |
| 3 | MOTTAINAI | 0:15 | Wajdi ZENTANI | 30 |
| 4 | Game mechanics | 0:25 | Wajdi ZENTANI | 58 |
| 5 | Rules and objective | 0:15 | Wajdi ZENTANI | 33 |
| 6 | LIVE DEMO | 1:00 ±0:20 | Rayen TOUIL | demo |
| 6b | Demo backup (hidden) | — | Rayen TOUIL | 21 |
| 7 | Online architecture | 0:20 | Rayen TOUIL | 45 |
| 8 | Microphones / voice | 0:15 | Rayen TOUIL | 30 |
| 9 | Data | 0:15 | Med Amine CHAKROUN | 32 |
| 10 | 5G SA: why latency matters | 0:25 | Med Amine CHAKROUN | 57 |
| 11 | Impact and next steps | 0:10 | Med Amine CHAKROUN | 20 |
| 12 | Closing | 0:10 | Med Amine CHAKROUN | 14 |

Cues: [CLICK] = next slide · [START DEMO] = switch the projector to the phone · [HAND OVER TO …] = next speaker steps in.

## 1. Hook: the sea is choking — 0:25 — Yassine AFFES

Every year, five hundred seventy thousand tonnes of plastic flow into the Mediterranean, our sea. That's thirty-three thousand eight hundred bottles every minute, according to WWF. We are four students from SUP'COM, and we asked one question: what if kids could fix it, by playing? [CLICK]

## 2. The real problem — 0:15 — Yassine AFFES

Plastic doesn't start in the sea. It starts in our streets and rivers: the OECD counts one hundred nine million tonnes sitting in rivers. And kids don't learn sorting from lectures. They learn by doing. [CLICK] [HAND OVER TO WAJDI]

## 3. MOTTAINAI — 0:15 — Wajdi ZENTANI

So we built MOTTAINAI, a Japanese word for 'what a waste!'. It's a cooperative 3D mobile game: three players share one raft to clean a polluted river. Paddle, collect, recycle. [CLICK]

## 4. Game mechanics — 0:25 — Wajdi ZENTANI

Each player controls one part of the raft. The left paddler swipes down to push forward and turn right; the right paddler does the opposite. The collector taps waste to throw the net. When both paddlers stroke within a quarter second, you get a sync boost: thirty percent more thrust. Missing a friend? A bot takes the seat. [CLICK]

## 5. Rules and objective — 0:15 — Wajdi ZENTANI

You have two minutes. One hit on a rock or a shark, and it's game over. Grab the shield for six seconds of safety. Every bin color scores, but toxic waste costs you. [CLICK] [HAND OVER TO RAYEN]

## 6. LIVE DEMO — 1:00 ±0:20 — Rayen TOUIL

Let's play it live. [START DEMO] Two phones join the Quick Play room. I'm the left paddler, Wajdi is the collector, and a bot takes the right paddle. Start! Watch the sync: when we stroke together, the raft shoots forward. The net catches plastic, glass, metal... careful, a shark fin! Every action goes to our server and back sixty times a second. [~40 s of play: steer into a rock to end the run] Oops, game over! Here are the results: score, waste per bin, accuracy, distance and kilograms of river cleaned. [CLICK]

- DEMO STEPS: (0) Before the talk: Wi-Fi OFF, mobile data ON on both phones, app opened, name entered, content pack already downloaded, Phone A mirrored to the projector.
- (1) Phone A: QUICK PLAY → LEFT PADDLER. Phone B: QUICK PLAY → COLLECTOR. The bot takes RIGHT PADDLER.
- (2) START → 3 s countdown → play about 40 s (3-finger tap once to show the Net HUD: network type + RTT).
- (3) Steer into a rock to end the run → GAME OVER → results screen.
- FALLBACK: if the network fails, show the backup screenshots on slide 6b (hidden: type 7 + Enter in slideshow mode).

## 6b. Demo backup (hidden) — — — Rayen TOUIL (hidden, Plan B only)

Plan B only. Here is the lobby where each phone picks a role, the river in play, and the results screen. [CLICK]

## 7. Online architecture — 0:20 — Rayen TOUIL

How does it work? Phones connect over secure WebSockets to Cloudflare's edge in Marseille. The game room runs on Cloudflare too, at sixty ticks per second. The server decides everything; phones predict their own raft and smooth the rest, and lag compensation keeps catches fair. [CLICK]

## 8. Microphones / voice — 0:15 — Rayen TOUIL

Voice is next. Today, players talk out loud in the same room; that's part of the fun. Next, we add in-game team voice chat so friends can play from anywhere. [CLICK] [HAND OVER TO MED AMINE]

## 9. Data — 0:15 — Med Amine CHAKROUN

Every run produces data: waste per bin, accuracy, distance, kilos cleaned, and network quality: latency, jitter, bandwidth. Network results land in a CSV on our server. Here is one real test match. [CLICK]

## 10. 5G SA: why latency matters — 0:25 — Med Amine CHAKROUN

Why does latency matter? Paddlers must stroke within a quarter second of each other. We measured it: a phone on Wi-Fi to our Paris room, seventy-one milliseconds; a Marseille room, forty-three. The app already detects 5G SA and requests a low-latency slice. Next step: the same test on 5G SA. Real-time co-op needs edge plus 5G SA. [CLICK]

## 11. Impact and next steps — 0:10 — Med Amine CHAKROUN

Next: classroom sessions with schools and local NGOs, an eco-fact for every catch, and a battle mode with several rafts. [CLICK]

## 12. Closing — 0:10 — Med Amine CHAKROUN

MOTTAINAI: every paddle counts, every piece matters. Thank you, we're ready for your questions.

## Plan B (demo or network fails)

- **One phone fails:** keep going with one phone. The server fills the two other seats with bots (1 human + 2 bots works).
- **Mobile network fails:** switch the phones to a Wi-Fi hotspot and retry once (the server is on the Internet, any network works).
- **Still no game after 15 s:** in slideshow mode type `7` + Enter to open the hidden slide 6b and say:
  "The network is not on our side today, so here is the same match: the lobby where each phone picks a role, the river in play with the score and the timer, and the results screen with waste per bin, accuracy and kilograms cleaned." Then type `8` + Enter and continue with the architecture slide.
- **Projector mirroring fails:** hold the phone up to the jury and narrate; skip the Net HUD.

## Likely jury questions

1. **Why a game?** Kids learn sorting by doing, not by listening. Every waste uses its real bin color, and the raft only moves when the team cooperates.
2. **What is the real impact?** Today: a playable prototype that teaches the 7 bin colors through play. Next: a classroom pilot with a short sorting quiz before and after, to measure what kids learned.
3. **Why 5G SA and not Wi-Fi?** Paddlers must sync within 0.25 s. On Wi-Fi we measured 71 ms average with spikes (p95 88 ms, jitter 16 ms). 5G SA and a latency slice aim for stable low latency outdoors and in schools without good Wi-Fi. The app already detects 5G SA and requests the slice; the 5G SA measurement is our next test.
4. **Data privacy for kids?** No account, no email, no location: a display name and a random token only. The server stores network test numbers (device model, network type, latency, bandwidth), not personal data. A school pilot would add parental consent.
5. **Business model?** Free for schools. Ideas for next: sponsorship by municipalities, NGOs and recycling companies, and cosmetic rewards. Not built yet.
6. **Next steps?** Measure on 5G SA, add eco-facts per catch and team voice chat, run a classroom pilot, then Battle mode (several rafts, team vs team).

## Sources

- WWF (2019), *Stop the Flood of Plastic: How Mediterranean countries can save their sea*: 0.57 Mt of plastic enter the Mediterranean each year, ≈ 33,800 bottles per minute.
- OECD (2022), *Global Plastics Outlook*: 109 Mt of plastic accumulated in rivers, 30 Mt in seas and oceans.
- Game numbers: `server/src/tuning.json` (2:00, 1 hit, sync 0.25 s / ×1.3, shield 6 s, combo 3 in 3 s ×1.5, scores).
- Network numbers: `/api/telemetry.csv` (Infinix phone on Wi-Fi → room CDG: 70.8 ms avg, p95 87.6, jitter 16.3) and `server/tools/fakeclient.ts` on 22 Nov 2026 (room MRS: 43.4 ms avg, p95 47.3, 60 snapshots/s, 226 kbps).
- Chart: the same fakeclient match (waste counts 30/11/19/10/9/0/10).
