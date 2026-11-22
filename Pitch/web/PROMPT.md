# Refined brief — MOTTAINAI web pitch (Green Hackathon 2026)

## Goal
A single offline HTML page (`Pitch/web/index.html`) that the team presents full-screen to the Green Hackathon jury. It should feel like a live keynote, not a document: a START screen, a presenter HUD, cinematic animated transitions, and a recorded demo video as the finale.

## Presenters (only two)
- **Yassine AFFES:** the problem, the reveal of the idea, impact and roadmap, closing line.
- **Wajdi ZENTANI:** the game, learn × play balance, innovations, technology, demo video narration.
The HUD shows who is speaking. The script marks every handover.

## Duration
≤ 5:00 in total, including about 0:45 of demo video. The script is written at 140 words/minute.

## Story (6 acts)
1. **Problem, dramatized:** a live counter of plastic bottles entering the Mediterranean since START. A wall of stats that hits one by one, while the water on screen gets dirtier. The problem must feel urgent. Drama comes from the staging, never from fake numbers: every figure is verified and sourced on screen.
2. **Blind spot:** kids 7–12 form habits; sorting is taught, rarely practised; 3.4 billion people play games.
3. **Reveal:** "もったいない — What a waste!" turns into MOTTAINAI. The screen is *cleaned* (polluted water → clear turquoise), so the transition tells the story.
4. **The game + learn × play balance:** 3 roles, 1 raft, sync boost, bots. An animated balance scale: education (real bin colors, toxic penalty as a safety reflex, kg cleaned) versus entertainment (co-op, combos, power-ups, sharks, 2-minute runs).
5. **What makes it different:** cooperation as the controller; real-time multiplayer on phones with a 60 Hz edge server; built for 5G (SA detection + latency slice request); a network lab inside the game. Then the proof: an animated architecture and measured numbers.
6. **Impact & roadmap → DEMO VIDEO (last part) → closing:** classroom pilot with a measurable before/after; then the recorded gameplay video with timed captions; then the closing card with the final bottle counter and the tagline.

## Visual theme (Green Hackathon)
- The hackathon identity: "GREEN HACKATHON — Swedish-Tunisian Games and Innovation Days", slogan « Rien ne se jette, tout s'invente », 21–22 Nov 2026, Pôle Technologique El Ghazala. No sponsor logos.
- A living background: an animated river whose pollution level follows the story (murky water and floating trash → clear water, bubbles, leaves, light rays).
- Transitions: a "tide" wipe (three layered waves) between slides, tinted murky in the problem act and turquoise/green after the reveal; a radial "cleanup" wave at the reveal.
- The progress bar is a river: a raft travels along it and the river behind it turns green.
- Palette: deep teal #073B43, turquoise #19B8B0, river blue #63CDE0, fresh green #72C78A, light aqua #E9F7F2, sun #FFC857 and coral #FF8360 as accents, hackathon green #5CF2A6.
- Fonts: Fredoka (titles) + Nunito Sans (body), bundled with the page (OFL).
- Smooth motion: staggered rise-in, count-up numbers, packets flowing in the diagram. `prefers-reduced-motion` is respected.

## Truth rules
- Statistics: only verified sources (WWF 2019, OECD 2022, Newzoo 2024), cited on the slide.
- Game facts come from the code (`server/src/tuning.json`); measurements are dated (22 Nov 2026).
- Not built yet = labelled Next/Later (eco-tips, sorting center, 5G SA measurement, classroom pilot, leaderboards, battle mode).

## Presenter features
START screen (Enter), → / Space / PageDown / click = next build step or slide, ← = back, F = fullscreen, P = presenter window (notes, next slide, timer), N = notes overlay, H = hide HUD, B = black screen, ? = help, Home/End. On the video slide: K = play/pause.

## Demo video
`Pitch/web/media/demo.mp4` (your phone screen recording, about 45 s). Caption times are in the `CUES` list at the top of the script. If the file is missing, an animated screenshot slideshow plays instead.
