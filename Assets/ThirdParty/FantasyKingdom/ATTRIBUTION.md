# Fantasy Kingdom in Unity 6 | URP — attribution & license

**Asset Store ID:** [298128](https://assetstore.unity.com/packages/essentials/tutorial-projects/fantasy-kingdom-in-unity-6-urp-298128)  
**Publisher:** Unity Technologies (Unity Originals / Demo team; environment adapted from Synty POLYGON Fantasy Kingdom)  
**Import path in this repo:** `Assets/FantasyKingdomSample/`  
**Status:** educational **prototype only** — not for commercial release.

## EULA (non-commercial / educational)

This sample uses the Asset Store **Non-standard EULA** for **educational and non-commercial use only**.

By using the Fantasy Kingdom in Unity 6 URP project package you agree to that EULA. See:

- `Assets/ThirdParty/FantasyKingdom/license.txt` (educational notice kept with this repo)
- The package page on the Unity Asset Store (Non-standard EULA)

### Allowed here

- Local prototype / learning / demos on Oliver’s machine while Fantasy 4x is pre-release
- Outer-world map visuals for the kingdom-builder SLG slice (`OuterWorldMap` = open-field marches; inner city comes later)

### Not allowed for shipping

- Commercial products / paid games shipping these Synty-derived environment assets
- Redistributing the demo art as if owned

### Before commercial release

Replace with Synty POLYGON Fantasy Kingdom (paid license), Kenney CC0 (`Assets/SafeFantasyAssets/`), or other commercially licensed art.

## Setup in this project

1. Package lives under `Assets/FantasyKingdomSample/` (Demo scene: `Scenes/Demo.unity`).
2. Menu **Fantasy 4x → World → Setup Fantasy Kingdom Outer Map** (or batch `FantasyKingdomOuterMapSetup.SetupBatch`):
   - Scales demo ×8 for RoK-style open-field marches
   - Strips sample cameras/characters
   - Wires OpenRTS camera + player/barbarian units
   - Bakes NavMesh on `OuterMarchField` only (full-mesh bake OOMs on this sample)
   - Saves **`Assets/Scenes/OuterWorldMap.unity`** as primary Play scene
3. Legacy Kenney meadow `Assets/Scenes/WorldMap.unity` stays in the project but is demoted (not primary).
4. Play steps: `docs/outer-world-map-play.md`

## Related

- OpenRTS gameplay: `Assets/ThirdParty/OpenSourceRTS/`
- Commercial-safe décor fallback: `Assets/SafeFantasyAssets/`
