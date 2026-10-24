# Core gameplay (open-field marches)

RoK / Fate War style: select armies, march on continuous NavMesh terrain, fight idle barbarians.

## Scripts

| File | Role |
|------|------|
| `ArmyUnit.cs` | Player army: Health / Attack / Speed / `isSelected` + NavMeshAgent |
| `ArmyMarchController.cs` | LMB box-select, RMB march / attack |
| `RTSCamera.cs` | WASD + edge scroll + clamped zoom |
| `BarbarUnit.cs` | Neutral idle barbarians + tick combat |
| `Editor/Fantasy4xTestScenario.cs` | **Spawn Test Scenario** + NavMesh bake |

## How Oliver tests

1. Open project in **Unity 2022.3+** (AI Navigation package resolves on first open).
2. **Fantasy 4x → Terrain → Create Plains Terrain (recommended)**
3. **Fantasy 4x → Gameplay → Spawn Test Scenario**  
   (bakes NavMesh via `NavMeshSurface`, places 3 blue armies + 3 red barbarians, wires camera + march controller)
4. Optional: **Fantasy 4x → Gameplay → Bake NavMesh (Terrain)** if you recreated terrain
5. Press **Play**
   - **LMB drag** — box-select blue armies  
   - **RMB** on open field — all selected march (repath mid-march works)  
   - **RMB** on red barbarian — chase + fight (1 dmg tick / second from Attack stats)  
   - **WASD** / screen edge / mouse wheel — camera (never under terrain)

## Package

`com.unity.ai.navigation` (NavMeshSurface) is listed in `Packages/manifest.json`.
Fallback bake: **Window → AI → Navigation → Bake**.
