# Outer world map — Play steps

Primary scene: **`Assets/Scenes/OuterWorldMap.unity`**

This is the **outer** open-field kingdom map (RoK-style marches). The **inner city** builder comes later.

## Open & Play

1. Unity Hub → open `C:\Users\miske\dev\4x` (Unity **6000.0.32f1**, URP).
2. Open `Assets/Scenes/OuterWorldMap.unity` (first enabled Build Settings scene).
3. Press **Play**.
4. OpenRTS controls:
   - **Left-click** — select player army (blue capsules)
   - **Right-click** ground — march (NavMesh on `OuterMarchField`)
   - **WASD / edge pan**, **mouse wheel** zoom
5. Red capsules are barbarians (not player-owned).

## What the setup did

- Source: `Assets/FantasyKingdomSample/Scenes/Demo.unity` (Asset Store **298128**, educational EULA)
- Environment scaled ×8 under `FantasyKingdomScaledRoot`
- Sample cameras/characters stripped; **OpenRTS** camera + selection wired
- NavMesh baked on march plane only (avoids OOM baking every kingdom mesh)
- Legacy Kenney meadow `Assets/Scenes/WorldMap.unity` kept but demoted

## Re-run setup

Menu: **Fantasy 4x → World → Setup Fantasy Kingdom Outer Map**

License: `Assets/ThirdParty/FantasyKingdom/ATTRIBUTION.md`
