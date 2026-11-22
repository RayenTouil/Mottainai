# MOTTAINAI — ART DIRECTION (single source for visuals)

> Sources of truth, read first:
> - `Docs/MOTTAINAI_Design_Guide.md` — palette, typography, UI rules, assets, roadmap.
> - `Docs/reference_gameplay.jpg` — target in-game look. Open and study it.
> - `art_inbox/LIST.md` + `art_inbox/*.glb|*.fbx` — imported models (§4).
> Goal: look as close as possible to the reference image, following the guide.

## 0. RULES
- Act as senior technical artist + environment artist + mobile UI dev for Unity 6 LTS (URP, C#). Autonomous, no explanations, minimal comments.
- **GATE:** start only after phase I1 of `CLAUDE.md` passed (full match over 5G SA). Never break a working build: commit + push after each sub-task, rebuild + install on the phone at the end of each phase, update `HANDOFF.md`.
- **Do NOT change:** gameplay rules, tuning, raft physics, colliders, networking/protocol, server code, controls.
- **Performance:** ≥ 55 FPS High, ≥ 60 FPS Low on the test phone (landscape). `Quality` setting (High/Low) disables expensive effects (caustics, bloom, post, extra particles).
- Reuse existing systems (VisualFactory, water shader, HUD, menus, `Resources/Models`). Improve, do not rebuild.
- Missing model → clean stylized fallback (rounded low-poly, correct colors). Never leave grey primitives.
- End of each phase output only: changed file paths + visual tests with pass conditions + FPS High/Low.

## 1. TARGET LOOK (reference image)
**Style:** Eco Adventure, cozy low-poly 3D. Bright, sunny, saturated but harmonious, soft light, rounded shapes, clean surfaces, no photorealism. Premium stylized mobile game.

**Camera:** third-person behind/above raft (~30–35° pitch), raft in lower-center third, river leads the eye to the horizon opening onto the sea.

**Raft & crew:** brown inflatable ring raft (thick rounded tube, slightly glossy). **3 toy-like characters** (one per role: LEFT = blue cap, RIGHT = green cap, COLLECTOR = yellow cap; pink reserved for future modes): cylindrical bodies, round heads, orange life vests, no detailed faces/anatomy. Wooden paddles with dark blades. Strong white foam wake, bow wave, paddle splashes.

**Water:** crystal turquoise/cyan (`#72D6E7` → `#21B8C7` → `#167E9B`), clear, light caustics, lighter see-through shallows near banks, soft wavelets, white foam lines on rocks/banks, sun glints. Calm and alive — never flat, never rough.

**Floating waste:** bottles, cans, cardboard box with recycling symbol, tire, rusty barrels — each bobs with a small ripple ring.

**Obstacles:** grassy rocks, floating logs, red-and-white buoys further ahead.

**Banks — clean urban park:** grass, warm stone/earth edges, reeds and tall grass at waterline, flowers; 2–4 round cartoon tree variants + pines; wooden fences, benches, lamp posts; blue/green recycling bins; poles with green recycling banners; wooden recycling sign; stone arch bridge with wooden railings and banners.

**Background:** green hills/mountains, sea horizon with red/white lighthouse on a rocky island, a sailboat, seagulls, blue sky with soft clouds, sun rays from top-right.

## 2. DESIGN TOKENS (`DesignTokens` ScriptableObject)
```
COLOR_PRIMARY      = #62C96B   (Eco Green — PLAY, validated, progress)
COLOR_WATER        = #21B8C7   (River Cyan)
COLOR_WATER_SHADOW = #167E9B
COLOR_ACCENT_LIME  = #A8DF70
COLOR_REWARD       = #FFC857   (Sun Yellow)
COLOR_ALERT        = #FF8360   (Coral)
COLOR_TEXT_DARK    = #244A53   (Deep Teal; panels 88–95% opacity)
COLOR_PANEL_LIGHT  = #F5FAF7   (Cloud)
RADIUS             = 10 / 16 / 24
BUTTON_PRESS_SCALE = 0.96
TRANSITION         = 150–250 ms
```
- Gradients: Play `#8BE66F → #42BF62`, Boost `#FFC857 → #FF9E45`. Balance 60% environment / 30% UI + main objects / 10% accents.
- Fonts: Fredoka (titles, logo, buttons) + Nunito Sans (HUD, numbers, text) from Google Fonts → TMP font assets. Only these 2 (fallback: keep current font if download fails, log it).
- **Waste category colors stay the bin-standard ones (`CLAUDE.md` §4)**, shown as outline/glow + "+X" popup color, even when the model has its own colors (e.g. red soda can = METAL → grey outline; red must stay reserved for TOXIC). Guide palette is for UI, environment, card frames.

## 3. WASTE & OBSTACLE MAPPING (visual only, no new gameplay types)
| Visual item | Game category | Look | Animation |
|---|---|---|---|
| Plastic bottle | Plastic | clear/cyan, colored cap | slow rotation + bob |
| Plastic bag | Plastic | white translucent stylized | floats, folds slightly |
| Tire | Plastic | very dark grey, thick | rocking |
| Soda can / metal scrap | Metal | red/blue aluminium / grey bars | small roll |
| Cardboard box | Paper/Cardboard | kraft brown, flaps, recycling symbol | light sway |
| Glass jar/bottle | Glass | green/blue + highlight | slow rotation |
| Apple core / banana peel | Organic | simple rounded shapes | bob |
| Barrel / battery | Toxic | rusty orange-brown + hazard symbol floating above | heavy slow bob |
| Chest / ring | Valuable | gold/violet, discreet sparkle | sparkle, no particle rain |
- Waste vs obstacles must differ by silhouette, animation and feedback.
- Buoys = visual variant of the existing Rock obstacle (same collider). Log = existing Floating Log. Bridge = scenery, no collision.

## 4. IMPORTED MODELS (`art_inbox/`)
- Follow `art_inbox/LIST.md` rules: match by name prefix, process only files not in `art_inbox/processed.json`, copy to `Mottainai/Assets/_Mottainai/Resources/Models/<Category>/`, GLB via `com.unity.cloud.gltfast`, FBX native.
- Normalize scale/pivot/rotation to existing collider sizes. Budgets: gameplay ≤ 3k tris, scenery ≤ 5k tris (decimate/reject above), textures ≤ 1024 px, URP Simple Lit, GPU instancing, static batching for scenery. Log rejects in `DECISIONS.md`.
- Restyle imported models toward the target look (palette tint, soft gradient/flat shading) so mixed sources feel like one game.
- Credit every author in `CREDITS.md` + Settings > Credits.
- Current mapping: plastic bottle, glass bottle, metal bars (waste); iceberg, rock (obstacles); palm trees ×2, pine, elm, nature pack (scenery). Everything else → stylized fallback until a file is added.

## 5. PHASES (in order; ★ = needed for tomorrow's demo, ☆ = only if time remains)
**★ Phase 1 — Lighting, sky, post:** warm directional sun top-right; gradient ambient (sky blue / grass green / warm ground); stylized skybox (blue gradient, soft cartoon clouds, sun disc + rays billboard); light pale-blue distance fog; Global Volume High only (low Bloom, +saturation, slight warm tint, Neutral tonemapping, light vignette); blob shadows (no realtime shadows on Low).

**★ Phase 2 — Water:** upgrade shader: turquoise depth gradient + shallow transparency; animated caustics (High); 2 flow-aligned scrolling normals + 3 low waves; fresnel sky tint, sun glints; stylized foam at banks/rocks/logs/raft. Raft: bow wave, big white wake trail, contact ripples, paddle splashes synced to STROKE events, double splash on SYNC. Floating objects: bob, slight roll, drift, instanced ripple ring. Last part of course: banks open to sea horizon + lighthouse island (deeper blue preset). ☆ Restoration layer: dirty patches near waste clusters fade to turquoise as waste is collected.

**★ Phase 3 — Raft, characters, paddles:** brown inflatable ring, gentle cosmetic rocking; 3 toy characters (caps per role §1, dot eyes max, vest straps, cap brim); procedural animations: paddle synced to stroke events, idle bob, cheer on catch, flinch on hit, collector throw; wooden paddles with water trail.

**★ Phase 4 — Environment kit (pooled bank chunks):** imported trees first (vary scale/rotation, never same tree twice in a row), stylized fallbacks for the rest: stone/earth edge, grass, reeds, flowers, stones; fences, benches, lamp posts, blue/green recycling bins, banner poles, wooden recycling signs; layered hills/mountains with atmospheric fade; obstacles with foam. ☆ Stone arch bridge every ~250 m, seagulls, ducks/butterflies (sparingly), sailboat.

**★ Phase 5 — Waste prop kit:** §3 table, same proportions, readable at small size.

**★ Phase 6 — UI:**
- Main menu: live 3D river background (raft + crew, slow camera drift); MOTTAINAI logo (Fredoka Bold, leaf + recycling symbol); tagline "Paddle • Collect • Recycle"; compact name field; dominant PLAY (green gradient) → existing Quick Play flow; secondary NETWORK LAB + SETTINGS; footer "Small actions. Big impact."; move Force re-download + debug URL into Settings.
- HUD (safe area, never covers the lane ahead): top-left score panel + thin distance progress bar; top-center role badge (L/R/Net, color + letter); top-right timer pill + round menu button (**online game: opens settings overlay, does NOT pause the match**); bottom/sides SYNC ring (fills, glows when ready) + waste counters. Keep the Net HUD (5G SA badge, Edge/Room colo, RTT) restyled in the guide style — it is the jury showcase.
- Collect feedback: pop 0.15–0.25 s → "+X" near item → particles in category color → short soft sound per family → score bump.
- Onboarding: animated hand; "**SWIPE DOWN** to paddle — **SYNC WITH YOUR TEAM** for a **BOOST**"; halo on the other paddler; fades after a few successful actions.
- Results: "RIVER CLEANED!" / "GOOD RUN!"; final score, distance, waste per category (real data only); restored-river thumbnail; PLAY AGAIN / MENU.
- Buttons: Primary green, Secondary cyan, Utility deep teal, Reward yellow, Alert coral; states idle/pressed/disabled/loading/focus. Icons: one rounded thick filled style (Kenney Game Icons, recolored).

**☆ Phase 7 — Collection screen (post-demo):** item cards (3D render-texture thumbnail, name, family, discovered/silhouette, set progress); rewards (badges, raft palettes, cap colors, titles). Results "New item discovered!" only once this exists.

**★ Phase 8 — Polish & acceptance:** main action understood < 1 s; items recognizable; text readable on phone; palette respected; HUD never hides waste/obstacles; feedback visible not aggressive; low-poly everywhere; ≥ 55 FPS High, ≥ 60 FPS Low. Take a gameplay screenshot from the default camera, compare with `Docs/reference_gameplay.jpg`, list the 5 biggest differences, fix the cheap ones.

Start with Phase 1.
