# MOTTAINAI — ART DIRECTION v3 (replaces ART_DIRECTION.md / v2)

Target: jury demo today. Unity 6 URP, Android mid-range, landscape, ≥55 FPS.
North star: Docs/reference_v3_master.png (cozy toy-bright river, turquoise water, ringed waste, bean crew on yellow raft).
Hard rule: do NOT touch gameplay, physics, networking, or Net HUD data sources. Visual layers only (materials, shaders, meshes, VFX, lighting, UI styling, camera params).

---

## 1. Style pillars (in priority order)
1. **Water is the hero**: turquoise depth gradient, white foam edges, wake behind the raft. It fills 45–60% of every frame.
2. **Readability first**: every waste item = one category color on a thick ring. **Red only for toxic**, purple only for valuable.
3. **Toy-bright, high-key**: midday sun, saturated, soft. No grey, brown-dominant or grim frames.
4. **Chunky low-poly, flat color**: flat-shaded or smooth-low-poly, matte, solid colors. No realistic/photo textures.
5. **Framed river corridor**: banks on both sides, river winds to a vanishing point upper-center, fog hides the end.

---

## 2. Palette tokens

### Fixed gameplay colors (never reuse elsewhere in the water)
| Token | Hex | Use |
|---|---|---|
| `waste.plastic` | `#F2C200` | ring |
| `waste.paper` | `#1E6FD9` | ring |
| `waste.glass` | `#2E9E44` | ring |
| `waste.organic` | `#7A4A21` | ring (add 2 px light outline `#E8C9A0` for contrast on water) |
| `waste.metal` | `#8C959F` | ring (add 2 px white outline) |
| `waste.valuable` | `#9B4DFF` | ring + sparkle; gem highlight `#C79BFF` |
| `waste.toxic` | `#D62828` | ring + pulse + hazard icon. **Only red in the game.** |

### World
| Token | Hex | Use |
|---|---|---|
| `water.deep` | `#178CB8` | river center |
| `water.shallow` | `#3FD3E3` | near banks |
| `water.horizon` | `#8FE6F0` | fresnel/far tint |
| `foam` | `#F4FBFF` | foam, wake, splashes |
| `caustic` | `#CFFBFF` | caustic lines |
| `grass` | `#7CC243` | bank tops |
| `grass.dark` | `#5FA834` | variation, under trees |
| `cliff` | `#D9A464` | bank faces |
| `cliff.shade` | `#B9844A` | lower cliff band |
| `rock` | `#8E959C` / `#6F767D` | rocks (top/side) |
| `pine` | `#2F8F4E` / `#3BA55C` | conifers |
| `tree.round` | `#57B847` | round trees |
| `wood` | `#A0703C` | fence, dock, paddles, net handle |
| `bark` | `#8B5A2B` | logs, trunks |
| `roof` | `#D9653B` | houses (terracotta, NOT red) |
| `wall` | `#F3E6C8` | houses |
| `stone.far` | `#C9C2B5` | far bridge |
| `hills.far` | `#9CCB7A` | background hills |
| `sky.top` | `#6EC6FF` | sky |
| `sky.horizon` | `#CFEFFF` | sky |
| `fog` | `#BFE6F2` | fog |

### Raft, crew, hazard
| Token | Hex |
|---|---|
| `raft.body` | `#FFB020` |
| `raft.band` | `#3A3F47` |
| `raft.floor` | `#2E3338` |
| `hazard.orange` (cones, buoys, life ring, warnings) | `#FF7A1A` + white `#FFFFFF` |
| `crew.blue` / cap | `#2F6BFF` / `#1F4FCC` |
| `crew.yellow` / cap | `#FFD23F` / `#FF6F59` (coral) |
| `crew.green` | `#4CC94C` |
| `eyes` | `#1A1A1A` |

### UI
| Token | Hex |
|---|---|
| `ui.panel` | `#1E2A34` @ 80% alpha |
| `ui.panel.stroke` | `#FFFFFF` @ 15% |
| `ui.text` | `#FFFFFF` |
| `ui.text.outline` | `#0E161C` |
| `ui.primary` / shadow | `#4CC94C` / `#2E8A2E` |
| `ui.secondary` / shadow | `#2F8BFF` / `#1E5FB3` |
| `ui.warn` | `#FF7A1A` |
| `ui.net.badge` (5G SA) | `#00C2A8` |

---

## 3. Lighting, sky, fog, post
- **Directional light**: color `#FFF2DC`, intensity 1.3, rotation (50, -30, 0). Shadows: **off on Low**, soft on High only (distance 30 m, 1 cascade).
- **Environment lighting**: Gradient. Sky `#9ED8FF`, Equator `#CDEBD8`, Ground `#6E8A5A`. Intensity 1.0.
- **Skybox**: Skybox/Procedural. Sun Disk None, Atmosphere Thickness 0.6, Sky Tint `#8FD3FF`, Ground `#CDEBD8`, Exposure 1.25. (Alternative if it looks muddy: camera Solid Color `#CFEFFF` + large gradient dome mesh `sky.top`→`sky.horizon`.)
- **Fog**: Linear, color `#BFE6F2`, start = 45% of the visible river length, end = 100% (start ≈ 40 m, end ≈ 140 m at current scale). The river end and far banks must dissolve into fog, never a hard edge.
- **Post (URP Volume, global)**:
  - Tonemapping: Neutral (not ACES, which darkens the palette).
  - Color Adjustments: Post Exposure +0.1, Contrast +8, Saturation +15.
  - Vignette: 0.18, smoothness 0.4.
  - Bloom: threshold 1.0, intensity 0.4. **Medium/High tier only.**
  - Nothing else (no DOF, no motion blur, no SSAO).
- **Blob shadows**: unlit quad, radial gradient texture (128 px), color `#0B3A4A` @ 35%, under raft (2.8×1.8 m), crew-free; under large obstacles (1.2× footprint). Sits 0.01 m above water and follows bob.

---

## 4. Water shader (Shader Graph, URP Unlit, opaque)
No depth texture (too expensive on Low). Edge effects come from river-mesh UVs: **UV.x = 0 at left bank → 1 at right bank, UV.y = distance along river.** If the river mesh lacks this, regenerate its UVs (visual-only change).

| Parameter | Value |
|---|---|
| Depth gradient | `d = min(UV.x, 1-UV.x)`; color = lerp(`water.shallow`, `water.deep`, smoothstep(0.0, 0.35, d)) |
| Fresnel tint | lerp toward `water.horizon` by (1 - N·V)^3 × 0.35 |
| Edge foam | band where d < 0.04, color `foam`, broken by scrolling noise (tiling 8, speed 0.15), soft edge 0.02 |
| Flow | all UV scrolling along +UV.y at 0.4 units/s (match current direction) |
| Caustics | one tileable caustic texture, 512 px (bake once from Voronoi with an editor script). Two samples: tiling 6, scroll (0.03, 0.05) and (-0.04, 0.02); multiply the two; color `caustic`, additive intensity 0.25. Low tier: 1 sample. |
| Waves | vertex Y offset: amplitude 0.06 m, wavelength 4 m, speed 1.2, two crossed sine waves (0° and 70°). River mesh ≥ 1 vertex per 1 m along its length, ≤ 5k tris. |
| Sparkle (High only) | caustic peaks > 0.9 → +0.3 white |

**Foam collars** (obstacles, banks contact): ring mesh around the object at the waterline, unlit transparent, `foam` @ 70%, noise-scrolled alpha, radius = footprint × 1.25.

**Wake**: Trail Renderer at raft stern, width 1.2 → 0, time 1.2 s, `foam` alpha 0.8 → 0, soft texture. Plus 2 side spray particle systems (emit on paddle stroke or speed > threshold, 6 particles, lifetime 0.5 s, `foam`).

---

## 5. Camera framing (parameter tweaks only, no logic changes)
- Follow distance 9 m behind, height 6 m, pitch 35°, FOV 50 (landscape).
- Raft centered horizontally, its center at **~32% from the screen bottom**.
- Look-ahead 4 m along raft heading; position damping 0.25 s.
- Visible in every frame: both banks, ≥ 30 m of river ahead, fogged horizon in the top ~20%.

---

## 6. Raft and crew
**Raft** (≤ 1.5k tris): oval inflatable ring 2.6 × 1.6 m, tube radius 0.35 m (12 × 24 segments), `raft.body`; one horizontal band at mid-tube `raft.band`; floor `raft.floor`; orange life ring `hazard.orange` + white stripes on the front; 4 rope loops optional. Matte (smoothness 0.2).

**Crew** (≤ 900 tris each, 3 total): original toy "bean" figures, **not a copy of any existing game character**. One-piece capsule body+head 0.9 m, stubby arms/legs (capsules), two black dot eyes (`eyes`) with a tiny white highlight, no mouth/nose. Matte, smoothness 0.15.
- Paddler A: `crew.blue`, baseball cap `#1F4FCC`, wooden paddle `wood`.
- Paddler B: `crew.yellow`, helmet/cap `#FF6F59` (coral, NOT red), wooden paddle.
- Collector: `crew.green`, net (handle `wood`, rim `#DDE3E8`, mesh white @ 50%), stands center-back.
- Procedural idle (visual script only): bob 0.03 m @ 1.5 Hz (random phase), squash Y 0.95 on paddle stroke, collector net swings ±10°.

---

## 7. Banks and environment kit
- **Bank profile**: grass top `grass` (vertex variation `grass.dark`), vertical/sloped cliff face `cliff` with `cliff.shade` lower band, foam line at water contact.
- **Props** (all ≤ 5k tris, GPU instancing on, shared materials):
  - Pines (2 sizes) and round trees, clustered 3–5 every 8–12 m per bank; scale variation 0.8–1.3, random yaw.
  - Rocks on banks (2 shapes), bushes, wooden fence runs, 1 wooden dock.
  - 2–3 cottages (`wall` + `roof`) set back on the banks; 1 stone bridge + waterfall far away as an end-of-river landmark (fog-tinted).
  - Background: low hill cards `hills.far`, fogged.
- No props in the river except gameplay objects (avoid confusing silhouettes).

---

## 8. Waste and obstacle look table

**Ring spec (all waste)**: flat ring mesh (32 segments), inner radius 0.55 / outer 0.75 × (object radius × 1.6), unlit, category color @ 90%, plus soft outer glow quad (additive, @ 35%). Sits 0.02 m above water and follows the bob. Pulse scale 1 → 1.08 @ 1.2 Hz.
**Shared waste motion**: bob Y ±0.08 m @ 0.8 Hz (random phase), yaw drift 10°/s.
**Pickup FX**: scale pop 1 → 1.2 → 0 in 0.2 s; splash burst 12 `foam` + 6 category-color particles; ripple ring 0 → 2 m in 0.5 s; floating "+1" in Fredoka Bold.

| Item | Silhouette (≤ 3k tris, most ≤ 300) | Extra anim | Ring |
|---|---|---|---|
| Plastic | chunky bottle, clear-blue body `#BFE3FF`, white cap | none | `#F2C200` |
| Paper | milk carton / box, white + blue panel | slight tilt ±5° | `#1E6FD9` |
| Glass | green bottle lying on its side `#3FBF5F` | none | `#2E9E44` |
| Organic | apple core / banana peel, `#E8C9A0` flesh + `#7A4A21` stem | none | `#7A4A21` + light outline |
| Metal | tin can, `#B8C0C8` with darker rims | slow roll ±10° | `#8C959F` + white outline |
| Valuable | faceted gem (~40 tris) `#9B4DFF`, highlight `#C79BFF` | spin 60°/s + sparkle particles (3/s) | `#9B4DFF` |
| Toxic | barrel `#D62828` with black hazard symbol, dark lid | faster pulse (2 Hz) + small green-grey fume particles | `#D62828` |

| Obstacle | Silhouette | Anim | Marker |
|---|---|---|---|
| Rock | rounded faceted boulder, `rock` 2-tone | none | foam collar |
| Buoy | cone buoy `hazard.orange` + 2 white stripes | bob + tilt ±6° | foam collar |
| Iceberg | faceted, top `#E8F8FF`, sides `#9FDDF2` | rotate 3°/s | foam collar |
| Log | cylinder, `bark`, end rings `cliff` | roll ±8°, drift | foam collar |
| Shark | grey `#6F7D8C`, belly `#EEF2F5`, mouth interior `#B5546A` (dark pink, not red) | fin cuts water by default; breach arc (visual only) when gameplay triggers it | foam spray on breach |

---

## 9. UI style
- **Font**: Fredoka (SemiBold for body, Bold for numbers), Google Fonts, OFL. TMP SDF, outline 0.15 `ui.text.outline`. Fallback if unavailable: the existing TMP bold font + same outline.
- **Panels**: rounded rect 9-slice radius 24 px, `ui.panel`, 2 px inner stroke `ui.panel.stroke`.
- **Buttons**: chunky pill, radius 28 px, face `ui.primary` with 6 px bottom shadow band `#2E8A2E` (secondary: `ui.secondary`/`#1E5FB3`), label white Fredoka Bold 40 px. Press state: face moves down 4 px, shadow shrinks to 2 px. Minimum 120 × 120 px touch target.
- **Icons**: flat filled, white with dark outline; stopwatch, recycle, checkered flag.
- **HUD layout** (landscape, safe-area margin 32 px; keeps the dev positions to minimize churn). **Max 5 widgets visible at once.**
  - Top-left: score panel: leaf/recycle icon + score (64 px) + role icon/label (24 px) + distance bar (`131 / 650 m`, green fill `ui.primary`).
  - Top-center: **Net pill** (jury showcase): `5G SA` badge (`ui.net.badge`), `RTT 52 ms` color-coded (< 30 ms `#4CC94C`, < 60 ms `#FFD23F`, else `ui.warn`), `MRS → CDG` colo in 24 px. Tap → full debug panel (existing data) as an overlay; tap again to close. Default: closed.
  - Top-right: timer panel (stopwatch icon + `1:40`, 56 px) + menu button. Last 15 s: digits `ui.warn` + pulse (never red). Power-up chip with a radial timer just under it.
  - Bottom-center: collected-category pills (only those > 0), 48 px, category colors.
  - Bottom-right: existing NET action button, restyled §9 (green ring, `READY` label), 70% opacity when idle.
- No minimap, no new objectives (would need new gameplay data).

---

## 10. REMOVE / FIX from the rejected dev pass (from team screenshots)
| # | Dev pass problem | v3 fix |
|---|---|---|
| R1 | **Giant "SYNC xN!" text center-screen on every tap**; it covers the river and is annoying | Remove the center text entirely. Keep the sync logic untouched. Show a small chip (`x4`, Fredoka Bold 32 px, white on `ui.panel`) 1.5 m above the raft for 0.6 s, **only at x3, x5, x10** and above; otherwise a tiny paddle-splash particle is the only feedback |
| R2 | **Net debug panel covers ~25% of the screen** (bottom-left, 12 lines: snapshots, msgs, TCP_NODELAY…) | Compact **Net pill top-center**: `5G SA` badge · `RTT 52 ms` (color-coded) · `MRS → CDG`. Full debug panel hidden by default, opens on tap of the pill (same data, same toggle) |
| R3 | Redundant/cluttered UI: yellow "NET" circle + "YOU: COLLECTOR" pill top-center, floating "SHIELD 2.0s"/"MAGNET 3.9s" text, 7 zero-pills bottom | Role → small icon + label inside the score panel. Power-up timers → one small icon chip with a radial timer under the timer panel. Category pills: show only categories already collected (> 0), purple for valuable |
| R4 | **Raft is a dark brown box/trapezoid; crew are tiny yellow cylinders** (unreadable, not toy-like) | Yellow inflatable raft + 3 bean crew (§6), scaled so the crew are ≥ 8% of screen height |
| R5 | **Shield = huge opaque dome** hiding the crew | Thin fresnel bubble: rim `#7FE9FF` @ 60%, center @ 8%, radius = raft + 0.3 m |
| R6 | **Magnet = pink/magenta ring** (clashes with valuable purple) | Cyan `#00C2A8` dashed rotating ring + small sparks pulled toward the raft |
| R7 | **Huge opaque white foam blob under/behind the raft** eating the bottom of the screen | V-wake trail only (§4): width ≤ 1.2 m, alpha ≤ 0.8, fades in 1.2 s; no foam disc under the raft |
| R8 | **Water: busy, high-frequency, visibly tiled ripples; flat cyan, no depth** | §4 shader: depth gradient, low-frequency caustics (tiling 6, ≤ 0.25 intensity), foam only at banks |
| R9 | **Straight concrete canal + paved paths + lamp posts + benches**: reads as a city water park, not a river | Keep the river path (gameplay), but replace the banks: irregular grass/cliff edges (§7), rocks breaking the bank line every 5–10 m, remove lamp posts and benches, keep the recycling bins (on-theme) |
| R10 | **Flat endless green plain to the horizon**, sparse lollipop trees | Bank height 1.5–3 m with cliff faces, dense tree clusters, hills + fog at the horizon |
| R11 | **Washed-out, hazy, low-contrast sky and light** | §3 lighting + post (Neutral tonemap, sat +15, contrast +8), fog only in the far 60% |
| R12 | **Camera too low (~20° pitch)**: shows a lot of empty canal/horizon; waste tiny and far | §5: pitch 35°, height 6 m, distance 9 m; the horizon goes up to the top ~20% |
| R13 | **Waste tiny with thin rings**, categories unreadable | §8 rings, waste scaled ×1.5 (visual mesh only, colliders unchanged) |
| R14 | Grey rock has a pinkish tint | `rock` tokens, 2-tone, foam collar |
| R15 | Finish arch is beige and flat | Keep it as a landmark; recolor `wood` + green leaf banner, fogged when far |

---

## 11. Acceptance tests
Capture on the phone (`adb exec-out screencap -p`) at the same moment: 20 s into a match, raft mid-river. Compare side by side with Docs/reference_v3_master.png.

| # | Test | Pass criterion |
|---|---|---|
| A1 | Water dominance | Water covers 45–60% of the frame; center hue reads turquoise (sample ≈ `#178CB8`–`#3FD3E3` ±15%) |
| A2 | Edge gradient + foam | Lighter water and a visible white foam line along both banks |
| A3 | Framing | Raft centered at ~32% from the bottom; both banks visible; fogged horizon in the top ~20% |
| A4 | Waste readability | Screenshot of all 7 categories in a row: a teammate who hasn't seen the build names every category correctly at arm's length |
| A5 | Red exclusivity | No red (`#C0–FF` R, G and B < `#50`) on screen except toxic items |
| A6 | Wake | White V-wake visible behind the moving raft |
| A7 | Crew | 3 distinct bean figures, blue / yellow-coral cap / green with net, readable at default zoom |
| A8 | HUD | Timer, score and Net HUD (5G SA, RTT, colo) legible; Net HUD in top-right, uncovered |
| A9 | Performance | ≥ 55 FPS average over a full 2-min match on the demo phone, Low tier; no frame > 50 ms |
| A10 | Budgets | Gameplay objects ≤ 3k tris, scenery ≤ 5k, textures ≤ 1024 px; no pink/missing shaders |
| A11 | Non-regression | Match starts, multiplayer connects, pickups score, timer ends the match, same as the stable base |

---

## 12. Execution priority (~4 h, biggest jury impact first)
| Phase | Scope | Time | Gain |
|---|---|---|---|
| P0 | R1 (kill SYNC spam), R2/R3 (Net pill + HUD declutter), R5/R6/R7 (shield, magnet, foam blob), R12 (camera) | 25 min | Clears the screen; the jury's #1 annoyance gone |
| P1 | Lighting, sky, fog, post volume + water shader + palette on existing materials | 60 min | Whole frame transforms |
| P2 | Waste rings, bob, pickup FX, valuable → purple, obstacle recolor + foam collars | 50 min | Readability, the core of the loop |
| P3 | Raft + crew reskin (bean shape, caps, net), wake trail, blob shadows | 50 min | Hero object in the center of every shot |
| P4 | Bank kit dressing: cliffs, trees, rocks, fences, cottages, far landmark | 40 min | Fart Together-style coziness |
| P5 | UI skin: font, panels, buttons (layout already done in P0) | 20 min | Polish the jury sees immediately |
| P6 | Perf pass + acceptance screenshots | 20 min | Safety |

**Cut order if behind schedule**: far landmark (bridge, waterfall) → crew procedural idle → caustics 2nd sample → bloom → cottages → shark breach visual.
