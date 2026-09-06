# Patreon Beaver Names for Timberborn

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](manifest.json)
[![Target](https://img.shields.io/badge/Timberborn-v1.0%2B-green.svg)](https://store.steampowered.com/app/1062090/Timberborn/)
[![License](https://img.shields.io/badge/license-MIT-lightgrey.svg)](LICENSE)
[![Ko-Fi](https://img.shields.io/badge/Ko--fi-F16061?style=flat&logo=ko-fi&logoColor=white)](https://ko-fi.com/Victor7996)
[![GitHub](https://img.shields.io/badge/GitHub-Victor7996%2FPatreon--Beaver--Names-181717?style=flat&logo=github)](https://github.com/Victor7996/Patreon-Beaver-Names)

**Patreon Beaver Names** is a Timberborn mod that automatically names every newborn beaver after one of your Patreon supporters — in order, cycling through the full list. Connect directly to the **Patreon REST API v2** for live sync, or drop names into a local `patreons.csv` for a simple offline setup. The current position in the list is saved with your game, so no name is ever skipped or repeated across sessions.

---

## What it does

Each time a beaver is born, the mod picks the next name from your supporter list and assigns it. When the list runs out it loops back to the start. The index is stored in the save file, so it survives reloads and game restarts.

- **Live API sync** — fetches active members directly from Patreon using your Creator Access Token. Supports automatic campaign discovery, dynamic tier filtering (Bronze, Silver, Gold, and any custom tiers), and full pagination for large campaigns.
- **Local CSV fallback** — no token? Just edit `patreons.csv` with one name per line. Works fully offline.
- **Save-safe** — only beaver name assignments change. No jobs, no districts, no game state beyond the name index. Removing the mod leaves no trace.
- **Robust networking** — handles HTTP 429 rate limits with `Retry-After` backoff, traverses all API pages via `meta.pagination.cursors.next`, and reuses connections to avoid socket exhaustion.

---

## Optional mod: Mod Settings (`eMka.ModSettings`)

Without **Mod Settings** installed, the mod works fully — names load from `patreons.csv` and the API is configured by editing static properties in code or via the CSV file.

With **Mod Settings** installed you get:

- An in-game settings panel under **Mods → Patreon Beaver Names**
- Live fields for your **Creator Access Token** and **Campaign ID**
- Per-tier checkboxes (Bronze / Silver / Gold / Custom) to control which supporters are included
- A real-time preview of the loaded name list, updated automatically after each API fetch
- Automatic campaign discovery — paste your token and the mod finds your campaign ID for you

Install Mod Settings from the Timberborn mod browser or Steam Workshop.

---

## 🛠️ Installation

1. Download the latest release from the [Releases](https://github.com/Victor7996/Patreon-Beaver-Names/releases) tab.
2. Extract the mod folder into your Timberborn mods directory:
   ```
   Documents\Timberborn\Mods\Patreon-Beaver-Names
   ```
3. *(Optional but recommended)* Install **Mod Settings** (`eMka.ModSettings`) for in-game configuration.

---

## ⚙️ Setup & Configuration

### Option A: Live Patreon API (Recommended)
1. Go to the **Patreon Developer Portal** (`patreon.com/portal`) → **My Clients** → **Create Client**.
2. Copy your **Creator's Access Token**.
3. Open Timberborn → **Mod Settings** → **Patreon Beaver Names**:
   - Paste your token into **Creator's Access Token**.
   - Leave **Patreon Campaign ID** as `default` for automatic detection.
   - Toggle tier checkboxes as desired.

### Option B: Local CSV List
- Edit `patreons.csv` in the mod root directory.
- Add one supporter name per line.

---

## 📜 Changelog

See [CHANGELOG.md](CHANGELOG.md) for full version history.

---

## 👤 Author

**Victor7996** — [GitHub](https://github.com/Victor7996)

---

## ☕ Support

If you enjoy this mod and want to support its development:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Victor7996)