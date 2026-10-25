using UnityEngine;

/// <summary>
/// Design targets for Fantasy 4x map / shard scale.
/// Reference: Rise of Kingdoms (primary) + Fate War (open-field SLG feel).
/// Planning constants only — no networking is implemented here.
/// </summary>
public static class Fantasy4xMapDesign
{
    public const float RokKingdomWidthKm = 1200f;
    public const float RokFogBlockKm = 3f;

    /// <summary>Oliver persistent-world target: ~3000 players per kingdom shard.</summary>
    public const int TargetPlayersPerKingdomShard = 3000;

    public const string RokTierPassNote =
        "Background only: T1 outer -> T2 ring -> T3 center; mountains with passes between tiers.";

    public const int TerrainGridTilesPerAxis = 4;
    public const float TerrainTileSizeUnity = 2000f;
    public const float PrototypeMapWidthUnity = TerrainGridTilesPerAxis * TerrainTileSizeUnity;
    public const float PrototypeMapLengthUnity = PrototypeMapWidthUnity;
    public const float PrototypeMaxHeightUnity = 150f;
    public const float MetersPerUnityUnit = 150f;

    public static float PrototypeKingdomWidthKm =>
        PrototypeMapWidthUnity * MetersPerUnityUnit / 1000f;

    public const string KingdomGridRootName = "KingdomTerrainGrid";

}
