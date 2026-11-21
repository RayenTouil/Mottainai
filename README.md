# 🌍 Mottainai

> **Run, collect, protect yourself… and save the planet!**

*Mottainai* (もったいない) is a Japanese word for the regret felt when something is wasted, something like *"What a waste!"*. It's the spirit of the game: nothing gets thrown away, everything gets collected and sorted.

**Mottainai** is a 3D endless runner for kids aged 7 to 12. The player runs across a three-lane road through four worlds and picks up the waste scattered along the way. They have to dodge obstacles and gear up before touching hazardous waste. Every run ends at the **Eco Sorting Center**, where the player sorts what they collected and sees the environmental impact of their run.

Built for the **Green Hackathon 2026**, part of the *Swedish-Tunisian Games and Innovation Days* (theme: waste management and recycling).

---

## 🎮 Gameplay

- **Controls:** swipe left or right to change lanes, swipe up to jump and down to slide. On desktop, use the arrow keys or WASD.
- **Waste instead of coins:** each item belongs to one of four categories.

| Category | Examples | Points |
|---|---|---|
| ♻️ Recyclable | Plastic bottles, cans, cardboard, paper | +10 |
| 🍌 Organic | Peels, fruit and vegetable scraps | +15 |
| 🔋 Hazardous | Batteries, e-waste, broken glass | +30 with protection |
| ☣️ Highly toxic | Chemical drums | Big bonus, **shield required** |

- **Health:** you have 2 hearts and each collision costs one. Picking up highly toxic waste without a shield is an instant game over.
- **Eco Shield:** a 15-second power-up that lets you collect dangerous waste safely.
- **Eco Tips:** short facts about the waste you just ran into, shown on the loading screen, between maps and at game over.
- **Eco Sorting Center:** at the end of each run, the player drops every item into the right bin (plastic, paper, glass, metal, organic, hazardous). They then get a report: items collected per category, sorting accuracy, distance and score.

## 🗺️ Worlds

| World | Typical waste | Obstacles |
|---|---|---|
| 🏜️ Desert | Plastic bags, bottles, batteries | Cacti, rocks, sandstorms |
| 🏙️ City | Cardboard, cans, e-waste | Vehicles, barriers, construction sites |
| 🌳 Forest | Packaging, batteries, chemical drums | Fallen trees, animals crossing |
| ❄️ Snow | Packaging, cans, batteries | Ice blocks, avalanches, black ice |

## 🧠 What kids learn

- How to tell recyclable, organic and hazardous waste apart.
- Never to touch a battery, a chemical or broken glass with bare hands, and to get an adult.
- Simple things to do at home: composting scraps and dropping batteries at a collection point.

## 🛠️ Tech stack

<!-- TODO: fill in with your actual stack (e.g. Unity 6 + C#, or Godot 4, or Three.js) -->
- **Engine:** `[engine + version]`
- **Language:** `[language]`
- **Target platforms:** Android / Web (mobile-first, keyboard support for desktop testing)

### Architecture

The game is split into independent managers, and all waste, score and spawn data lives in configuration files:

```
PlayerController · LaneManager · ObstacleSpawner · WasteSpawner
WasteClassification · CollisionManager · HealthManager · ShieldManager
ScoreManager · MapManager · DifficultyManager · EcoTipsManager
GameStateManager · WasteStatsManager · UIManager
```

The collection system and the sorting center read the same classification data, so the end-of-run statistics always match what was collected.

## 🚀 Getting started

```bash
git clone https://github.com/<user>/mottainai.git
cd mottainai
```

<!-- TODO: add the run/build steps for your engine -->
Then open the project in `[engine]` and run the main scene `[scene name]`.

## ✅ Hackathon scope (48 h)

- [ ] One fully playable world (Desert): 3 lanes, jump, slide, swipe
- [ ] 4 waste categories, 2 hearts, Eco Shield
- [ ] Eco Sorting Center + end-of-run report
- [ ] Eco Tips linked to collected waste
- [ ] Main menu with the 3 other worlds locked

## 🔭 Roadmap

- The other 3 worlds: City, Forest and Snow
- Online leaderboard and challenges between friends and schools
- Teacher dashboard: which kinds of waste kids confuse most
- AI-driven adaptive difficulty

## 👥 Team

| Name | Role |
|---|---|
| Rayen TOUIL | `[role]` |
| Yassine AFFES | `[role]` |
| Med Amine CHAKROUN | `[role]` |
| Wajdi ZENTANI | `[role]` |

Engineering students at **SUP'COM** (École Supérieure des Communications de Tunis).

---

<sub>Made with 💚 during the Green Hackathon 2026, Pôle Technologique El Ghazala, Tunisia.</sub>
