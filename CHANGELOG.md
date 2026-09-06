# Changelog

All notable changes to the **Patreon Beaver Names** mod will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-09-06

### Added
- **Patreon REST API v2 Support:** Full support for retrieving active campaign members directly from Patreon's official API endpoint.
- **Patreon API Pagination:** Complete pagination support parsing `meta.pagination.cursors.next` to support large creator campaigns across multiple pages.
- **Rate Limit Resilience:** Added automatic HTTP 429 ("Too Many Requests") backoff respecting the `Retry-After` header.
- **Dynamic Tier Discovery:** Automatically discovers all campaign tiers (standard Bronze/Silver/Gold and custom tiers) from the Patreon API.
- **Automatic Campaign Discovery:** Automatically discovers and selects the user's campaign using the Creator's Access Token.
- **Multiple Campaign Selection:** Prompts with clear guidance and links when a creator manages multiple Patreon campaigns.
- **Local CSV Fallback:** Offline mode reading supporter names from `patreons.csv`.
- **Mod Settings UI Integration:** Optional deep integration with `eMka.ModSettings` with live preview and configuration controls.
- **Automated Crash Reporting & Logging:** Centralized session logging in `logs/` directory and non-blocking crash reporting.
- **Reentrancy Protection:** `[ThreadStatic]` logging guard in `ModLogger` preventing recursion/stack overflow during error reporting.
- **Savegame Persistence:** Stores and restores the name index inside Timberborn's save file.
- **Mock REST API Server:** Node.js mock API in `mock-api/` for offline testing and local validation.
