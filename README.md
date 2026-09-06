# Patreon Beaver Names for Timberborn

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](manifest.json)
[![Target](https://img.shields.io/badge/Timberborn-v1.0%2B-green.svg)](https://store.steampowered.com/app/1062090/Timberborn/)
[![License](https://img.shields.io/badge/license-MIT-lightgrey.svg)](LICENSE)

**Patreon Beaver Names** is a mod for Timberborn that automatically names newborn beavers sequentially after your Patreon supporters. The mod seamlessly synchronizes either with a local `patreons.csv` file or directly with the live **Patreon REST API v2**.

---

## ✨ Features

- **Sequential Name Assignment:** Newly born beavers are assigned names in sequential order from your supporter list.
- **Savegame Persistence:** The current index position in the name list is saved directly into the Timberborn save file and restored on load.
- **Patreon REST API v2 Integration:** Real-time fetching of active campaign members with Bearer token authentication conforming strictly to Patreon's OpenAPI schema.
- **Dynamic Tier Discovery & Filtering:** Auto-detects all campaign tiers (standard Bronze, Silver, Gold, as well as custom tiers) and lets you toggle which tiers to include.
- **Multiple Campaign Auto-Discovery:** Automatically resolves your Campaign ID from your Creator Access Token; prompts with clickable links if you own multiple campaigns.
- **Local CSV Fallback:** Reads names from `patreons.csv` if no Patreon API token is provided or during offline play.
- **Mod Settings UI Support:** Optional integration with `eMka.ModSettings` offering in-game live preview and settings customization.
- **Robust Networking & Error Handling:** Built-in rate limiting handling (HTTP 429 with `Retry-After`), pagination support (`meta.pagination.cursors.next`), socket reuse, and crash reporting.

---

## 🛠️ Installation

1. Download the latest release from the [Releases](https://github.com/Victor7996/Patreon-Beaver-Names/releases) tab.
2. Extract the mod folder into your Timberborn mods directory:
   ```
   Documents\Timberborn\Mods\Patreon-Beaver-Names
   ```
3. *(Optional)* Install **Mod Settings** (`eMka.ModSettings`) for in-game configuration.

---

## ⚙️ Setup & Configuration

### Option A: Live Patreon API (Recommended)
1. Navigate to the **Patreon Developer Portal** (`patreon.com/portal`) &rarr; **My Clients** &rarr; **Create Client**.
2. Copy your **Creator's Access Token**.
3. Open Timberborn, go to **Mod Settings** &rarr; **Patreon Beaver Names**:
   - Paste your token into **Creator's Access Token**.
   - *(Optional)* Leave **Patreon Campaign ID** as `default` for automatic detection.
   - Adjust tier checkboxes as desired.

### Option B: Local CSV List
- Edit the `patreons.csv` file located in the root of the mod directory.
- Add one supporter name per line.

---

## 📜 Changelog

See [CHANGELOG.md](CHANGELOG.md) for full version history.

---

## 👤 Author

- **Victor7996** - [GitHub Profile](https://github.com/Victor7996)
