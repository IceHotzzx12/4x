# Terrain import (seamless world map)

Continuous heightmap landscape for Fantasy 4x — **no grid, tilemap, or chunk seams**.

Unity Editor was not available in the CI/agent environment, so TerrainData / scene Terrain objects are created via the menu script below when you open the project locally.

## Quick start (Oliver)

1. Open this repo as a Unity project (**2022.3 LTS** recommended; first open may upgrade `ProjectSettings`).
2. Wait for assets to import (`Assets/Terrain/Heightmaps/…`).
3. Menu: **Fantasy 4x → Terrain → Create Plains Terrain (recommended)**.
4. This creates:
   - `Assets/Terrain/Generated/WorldTerrain_Plains_Data.asset`
   - Scene `Assets/Scenes/WorldMap.unity` with a single Terrain + strategy camera
5. For marches/combat test: **Fantasy 4x → Gameplay → Spawn Test Scenario** (see `Assets/Scripts/README.md`).
6. Hit Play. Terrain scale defaults: **2000 × 60 × 2000** (width × height × length).

### Other heightmaps

| Menu item | Asset | Use |
|-----------|--------|-----|
| Create Plains Terrain | `Heightmap_04_Plains.png` | Open-field marches (default) |
| Create Meadows Terrain | `Heightmap_03_Meadows.png` | Alternate gentle landscape |
| Create Mountains / Hills / Canyon | barrier maps | Natural obstacles later |

Or select any heightmap texture → **Fantasy 4x → Terrain → Create From Selected Texture…**

## River mask (later obstacles)

- Path: `Assets/Terrain/RiverMask/Blindman67/River.png` (512², **CC-BY 3.0** — credit Blindman67)
- Menu: **Fantasy 4x → Terrain → Log River Mask Usage** (pings the asset + documents the hook)
- Apply later as a world-XZ sample mask for water / non-walkable cells when movement/NavMesh exists. No pathfinding was added in this PR.

## Licenses

See [`ATTRIBUTION.md`](ATTRIBUTION.md).

- Heightmaps: **CC0** (GameDevGary)
- River pack: **CC-BY 3.0** (Blindman67) — attribution required

## Folder layout

```
Assets/Terrain/
  Heightmaps/GameDevGary/   # 2K PNG heightmaps + LICENSE
  RiverMask/Blindman67/     # River.png + elevation + LICENSE
  Editor/                   # HeightmapTerrainImporter.cs
  Generated/                # created by menu (gitignored optional)
  ATTRIBUTION.md
  README.md
```
