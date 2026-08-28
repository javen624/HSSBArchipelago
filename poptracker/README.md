# Hardspace Shipbreaker — PopTracker

The loadable pack lives in **[`HSSB/`](HSSB/)**.

## Install

1. Install [PopTracker](https://github.com/black-sliver/PopTracker/releases).
2. Copy the `HSSB` folder into PopTracker’s `packs/` directory  
   (or run `tools\Pack-Poptracker.ps1` and drop the zip into packs / load it).
3. Open **Hardspace Shipbreaker - Archipelago**.
4. Click **AP** and connect to your multiworld.

## Regenerate

```powershell
python tools/generate_poptracker.py
```

Writes into `poptracker/HSSB/` (items, locations, maps, layouts, generated Lua).

Hand-maintained under `HSSB/`: `manifest.json`, `settings.json`, `scripts/init.lua`, `scripts/autotracking.lua`, `scripts/logic.lua`, `README.md`.

## Zip release

```powershell
tools\Pack-Poptracker.ps1
```

Output: `dist/HardspaceShipbreaker-PopTracker.zip`

## Version

Pack **0.2.0** — APWorld / client 0.6.x (`BASE_ID` 2026080100).
