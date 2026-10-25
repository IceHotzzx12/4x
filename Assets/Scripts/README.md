# Core gameplay (open-field marches)

RoK / Fate War style: select armies, march on continuous NavMesh terrain, fight idle barbarians.

Map scale: `Fantasy4xMapDesign.cs` — **4×4** Terrain tiles → **8000²** kingdom, max height 150, ~3000 players planning.

## Active stack (OpenRTS — MIT)

| File | Role |
|------|------|
| `ThirdParty/OpenSourceRTS/Runtime/OpenRtsUnit.cs` | Selectable unit + NavMesh march + combat |
| `ThirdParty/OpenSourceRTS/Runtime/OpenRtsSelection.cs` | LMB box-select, RMB march / attack |
| `ThirdParty/OpenSourceRTS/Runtime/OpenRtsCamera.cs` | WASD + edge scroll + zoom-scaled pan |
| `Editor/Fantasy4xTestScenario.cs` | **Spawn Test Scenario** + NavMesh bake |

Upstream: MinaPecheux UnityTutorials-RTS (MIT). See `ThirdParty/OpenSourceRTS/ATTRIBUTION.md`.

Works on **Unity 2022.3 LTS** and **Unity 6+** (Oliver: either is fine). Project stays on 2022.3.50f1 for now; continuous Terrain, not hex.

## Legacy (unused)

`ArmyUnit`, `ArmyMarchController`, `RTSCamera`, `BarbarUnit` — superseded by OpenRTS. Spawn Test removes them from the scene.

## How Oliver tests

1. Open `Assets/Scenes/WorldMap.unity`
2. Press **Play**

Controls:

- **LMB drag** — box-select blue armies
- **RMB** on open field — selected march
- **RMB** on red barbarian — chase + fight
- **WASD** / screen edge / mouse wheel — camera

If you recreate from scratch:

1. Open in **Unity 2022.3+** or **Unity 6+**
2. **Fantasy 4x → Terrain → Create Kingdom Terrain Grid** (or equivalent heightmap menu)
3. **Fantasy 4x → Gameplay → Spawn Test Scenario**
4. Optional: **Fantasy 4x → Gameplay → Bake NavMesh (Terrain)**

## Package

`com.unity.ai.navigation` (NavMeshSurface) is listed in `Packages/manifest.json`.
