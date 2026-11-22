# MOTTAINAI — ART DIRECTION IMPLEMENTATION PROMPT

> Before starting, read these 2 files (they are the source of truth):
> - `docs/MOTTAINAI_Design_Guide.md` — palette, typography, UI rules, assets, roadmap.
> - `docs/reference_gameplay.jpg` — the target in-game look. Open and study it.
>
> Goal: make the game look as close as possible to the reference image, following the design guide.

---

## 0. RULES

- Act as a senior technical artist, environment artist and mobile UI developer for Unity 6 LTS (URP, C#).
- Work autonomously. No explanations, minimal comments. Commit + push after each sub-task.
- **Do NOT change:** gameplay rules, tuning values, raft physics, networking, multiplayer sync, controls.
- **Performance:** ≥ 55 FPS on mid-range Android in landscape. A `Quality` setting (High/Low) disables the expensive effects.
- Reuse the existing systems (VisualFactory, water shader, HUD, menus, imported models in `Resources/Models`). Improve them; do not rebuild from scratch.
- If a model is missing, build a clean stylized fallback (rounded low-poly shapes with the correct colors). Never leave grey primitives.
- At the end of each phase, output only: changed file paths + visual tests with pass conditions + FPS on High and Low.

---

## 1. THE REFERENCE IMAGE — WHAT IT SHOWS (reproduce this)

**Style:** Eco Adventure, Cozy Low-Poly 3D. Bright, sunny, saturated but harmonious, soft lighting, rounded shapes, clean surfaces, no photorealism. Like a premium stylized mobile game.

**Camera:** third-person behind and slightly above the raft (~30–35° pitch). The raft is in the lower-center third. The river leads the eye toward the horizon, where it opens onto the sea.

**Raft and crew:**
- A **brown inflatable ring raft** (thick rounded tube, dark brown, slightly glossy).
- **3 to 4 toy-like characters**: simple cylindrical bodies, small round heads, **orange life vests**, colored caps (blue, green, yellow, pink), no detailed faces or anatomy.
- **Wooden paddles** with dark blades, held on each side.
- A **strong white foam wake** behind the raft, a bow wave at the front, and splashes at the paddles.

**Water:**
- Crystal **turquoise/cyan** water (`#72D6E7` → `#21B8C7` → `#167E9B`), very clear, with visible light caustic patterns.
- Lighter, see-through shallow water near the banks.
- Soft wavelets, white foam lines along the rocks and banks, sun glints.
- Calm, flowing and alive. Never a flat plane, never a rough ocean.

**Floating waste (readable, "collectible" look):** plastic bottles (clear/blue with a cap), red soda cans, a cardboard box with a recycling symbol, a black tire, rusty barrels/cans, a log. Each one bobs with a small ripple ring around it.

**Obstacles:** grassy rocks in the river, floating logs, red-and-white buoys/cones further ahead.

**Banks — urban park, clean and lively:**
- Green grass, warm stone and earth edges, reeds and tall grass at the waterline, small flowers.
- 2–4 varieties of round cartoon trees plus a few pine trees.
- Wooden fences, wooden benches, lamp posts.
- **Colored recycling bins** (blue, green) on the banks.
- Poles with **green recycling banners**, and a **wooden sign with a recycling symbol**.
- A **stone arch bridge** with wooden railings and recycling banners across the river.

**Background:** green hills and mountains, a **sea horizon with a lighthouse** (red/white) on a small rocky island, a sailboat, **seagulls** flying, a blue sky with soft white clouds, and sun rays from the top-right.

---

## 2. DESIGN TOKENS (from the guide — put them in a `DesignTokens` ScriptableObject)

```
COLOR_PRIMARY      = #62C96B   (Eco Green — PLAY, validated, progress)
COLOR_WATER        = #21B8C7   (River Cyan)
COLOR_WATER_SHADOW = #167E9B
COLOR_ACCENT_LIME  = #A8DF70
COLOR_REWARD       = #FFC857   (Sun Yellow)
COLOR_ALERT        = #FF8360   (Coral)
COLOR_TEXT_DARK    = #244A53   (Deep Teal; panels at 88–95% opacity)
COLOR_PANEL_LIGHT  = #F5FAF7   (Cloud)
RADIUS             = 10 / 16 / 24
BUTTON_PRESS_SCALE = 0.96
TRANSITION         = 150–250 ms
```

- **Gradients:** Play `#8BE66F → #42BF62`, Boost `#FFC857 → #FF9E45`.
- **Color balance:** 60% soft environment, 30% UI and main objects, 10% accents.
- **Fonts:** Fredoka (titles, logo, buttons) + Nunito Sans (HUD, numbers, text). Download the TTFs from Google Fonts and create TMP font assets. Use only these 2 families.
- **Waste category colors:** keep the existing bin-standard colors already defined in the project for gameplay readability (they teach sorting). Use the guide's palette for the UI, the environment and the collection card frames.

---

## 3. PHASES (execute in order)

### Phase 1 — Lighting, sky and post-processing (biggest visual gain)
- A warm directional sun from the top-right, matching the reference. Soft gradient ambient (sky blue / grass green / warm ground).
- A stylized skybox: bright blue gradient, soft volumetric-looking cartoon clouds, a sun disc with soft rays (billboard).
- Light distance fog in a pale blue tone for depth.
- **Global Volume (High only):** Bloom (low), Color Adjustments (+saturation, slight warm tint), Neutral tonemapping, light vignette.
- Blob shadows under the raft and objects (no realtime shadows on Low).

### Phase 2 — Water (match the reference)
- Upgrade the water shader:
  - turquoise depth gradient and transparency in the shallows;
  - **animated caustics** (High);
  - 2 flow-aligned scrolling normal maps plus 3 low-amplitude waves;
  - fresnel sky tint and sun glints;
  - stylized white foam at intersections with banks, rocks, logs and the raft.
- **Raft:** bow wave, a **big white wake trail** behind it (as in the image), contact ripples, paddle splashes synced to strokes, a double splash on SYNC.
- **Floating objects:** bobbing, slight roll, drift with the current, a small ripple ring under each one (instanced).
- **River → sea:** in the last part of the course, the banks open onto a sea horizon with the lighthouse island. The same shader uses a deeper blue preset there.
- **Restoration layer:** dirty green-brown patches near waste clusters fade to clear turquoise as waste is collected.

### Phase 3 — Raft, characters and paddles (preserve the toy style)
- **Raft:** a brown inflatable ring (torus-like, rounded, slightly glossy). Optionally add a small recycling logo that never covers the characters. Gentle rocking from the waves (cosmetic only).
- **Characters:** keep the minimalist cylindrical toy design: rounded cylinder body, round head, orange life vest, colored cap per player (blue / green / yellow / pink), simple dot eyes at most. Add subtle details only (vest straps, cap brim). **No realistic humans, no detailed anatomy.**
- **Animations** (procedural if no clips): paddling synced to stroke events, idle breathing/bob, a small cheer on catch, a flinch on near-miss or hit, and the collector's throw.
- **Paddles:** wooden shaft, dark blade, a water trail when the blade moves.

### Phase 4 — Environment kit (urban park riverbanks)
Build pooled bank chunks matching the image. Use imported CC0 models first, otherwise stylized fallbacks:
- **Trees:** 2–4 round cartoon variants + pine. Vary the scale and rotation; never repeat the same tree in a row.
- **Banks:** stone/earth edge, grass, reeds and tall grass at the waterline, flowers, small stones.
- **Park props:** wooden fences, benches, lamp posts, blue/green recycling bins, poles with green recycling banners, wooden recycling signs.
- **Landmarks:** one stone arch bridge over the river every ~250 m (passable, no collision with the raft), a lighthouse island near the sea section.
- **Life:** seagulls flying (simple flapping billboard or low-poly), a few ducks or butterflies, used sparingly.
- **Background:** layered hills and mountains with atmospheric fade.
- **River obstacles:** grassy rocks, floating logs, red-and-white buoys. Each has foam around it.

### Phase 5 — Waste prop kit (collectible look)
Same proportions, saturated but controlled colors, clean surfaces, readable at small size:

| Item | Look | Animation |
|---|---|---|
| Plastic bottle | clear/cyan with colored cap | slow rotation + bob |
| Soda can | red/blue aluminium | small roll |
| Cardboard box | kraft brown, visible flaps, recycling symbol | light sway |
| Glass jar/bottle | green/blue with a simple highlight | slow rotation |
| Plastic bag | white translucent stylized | floats, folds slightly |
| Tire | very dark grey, thick | rocking |
| Barrel (toxic) | rusty orange-brown + hazard symbol floating above | heavy slow bob |
| Rare item | gold/violet with a discreet sparkle | sparkle, no particle rain |

Collectible waste and obstacles must differ by silhouette, animation and feedback.

### Phase 6 — UI: main menu, HUD, results (from the guide)

**Main menu:**
- A live 3D background: the river scene with the raft and the crew, a slow camera drift.
- The **MOTTAINAI** logo (Fredoka Bold, custom treatment with a leaf + recycling symbol).
- Tagline **"Paddle • Collect • Recycle"**.
- A compact player name field (light panel, accent border on focus).
- A dominant **PLAY** button (green gradient, scale-up).
- Secondary buttons **NETWORK LAB** and **SETTINGS**.
- Footer **"Small actions. Big impact."**
- Move **Force re-download** into Settings.

**HUD (landscape, safe area):**
- top-left: score panel (dark translucent, leaf/trophy icon) + a thin progress bar (distance / total);
- top-center: a compact role badge (L/R/Net, color + letter);
- top-right: a timer pill + a round pause button;
- bottom/sides: the boost/SYNC ring (fills, glows when ready) and the waste counters.
- The HUD never covers the lane ahead.

**Collect feedback:** pop 0.15–0.25 s → "+X" near the item → particles in the category color → a short soft sound per family → score bump.

**Onboarding:** an animated hand showing the gesture. Text: "**SWIPE DOWN** to paddle — **SYNC WITH YOUR TEAM** for a **BOOST**". The right paddler is highlighted with a soft halo. The hint fades after a few successful actions.

**Results screen:**
- "RIVER CLEANED!" / "GOOD RUN!";
- final score, distance, waste per category (real data only);
- "New item discovered!";
- a thumbnail of the restored river;
- PLAY AGAIN / MENU.

**Buttons:** Primary green, Secondary cyan, Utility deep teal, Reward yellow, Alert coral. States: idle, pressed, disabled, loading, focus.

**Icons:** one rounded, thick, filled style everywhere (pause, timer, recycle, settings, trophy, leaf). Use Kenney Game Icons as a base, recolored.

### Phase 7 — Collection screen
- Cards per item: 3D thumbnail (render texture), name, family, discovered / to discover (silhouette), set progress, rarity only if it is real in the game.
- Rewards: badges, raft color palettes, cap colors, player titles, set completion.

### Phase 8 — Polish and acceptance
Check every screen against the guide's acceptance checklist:
- main action understood in < 1 s;
- items recognizable;
- text readable on a phone;
- palette respected;
- HUD never hides waste or obstacles;
- feedback visible but not aggressive;
- low-poly style everywhere, no photorealism;
- ≥ 55 FPS on High, ≥ 60 FPS on Low.

Then take a gameplay screenshot from the default camera. Compare it with `docs/reference_gameplay.jpg` and list the 5 biggest remaining differences. Fix them if they are cheap.

---

Start with Phase 1.
