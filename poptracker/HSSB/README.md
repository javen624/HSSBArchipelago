# Hardspace Shipbreaker — PopTracker pack 0.2.0

List/tab progress tracker with **Archipelago autotracking**.

## Install

1. Copy this `HSSB` folder into PopTracker `packs/`, or load the release zip.
2. Open the pack and click **AP** (same host/port/slot as the game client).

`game_name` is **Hardspace Shipbreaker**.

## Features

- Item grid (progression / useful) + filler strip + seed options
- Region tabs with **nested group tabs** and compact multi-column check maps
- Slot data: density, hab shop-sanity, death link, goal
- `Archipelago.CheckedLocations` applied on connect (mid-seed / reconnect)
- Broadcast view: item grid only

## Regenerate (from repo root)

```powershell
python tools/generate_poptracker.py
tools\Pack-Poptracker.ps1
```
