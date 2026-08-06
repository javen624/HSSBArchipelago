# Hardspace: Shipbreaker — Multiworld Setup

## Requirements

- [Archipelago](https://github.com/ArchipelagoMW/Archipelago) 0.5.0+
- Hardspace: Shipbreaker + BepInEx 5.x (Unity 2020.3.35f1)

## Install the apworld

1. Use [`dist/HardspaceShipbreaker.apworld`](../../../dist/HardspaceShipbreaker.apworld) — drop on the Launcher or copy into `custom_worlds/`.
2. Restart the Launcher / generator. **Regenerate** after upgrading from 0.5.x.

## Generate

YAML templates: `docs/phase1/HardspaceShipbreaker.yaml` (standard), `_sparse.yaml`, `_deathlink.yaml`.

```bash
python Generate.py --player_files_path path/to/docs/phase1
python MultiServer.py
```

## Client

Build `client/HardspaceShipbreaker.Archipelago` and install by putting DLLs into BepinEx/plugins folder

| Key | Action |
|-----|--------|
| F6 | Progress HUD (status / checked / offline queue) |
| F7 | Connect dialog |
| F8 | Debug location check (debug)|
| F9 | Goal + release/collect |
| F10 | +1 cert rank + full bay refresh (debug) |
| F11 | +1 Progressive Cert Cap (debug) |

Live deposits, Hab shop-sanity, currency, upgrades, PCR cert ceiling, debt goal, and Death Link (when enabled) are handled automatically. Ship families unlock through Career certification as you find Progressive Certification Rank items. Offline checks queue locally (and in AP DataStorage) and flush on reconnect; AutoReconnect retries after unexpected drops.
