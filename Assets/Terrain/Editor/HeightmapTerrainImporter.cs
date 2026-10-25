#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fantasy 4x â†’ Terrain: RoK-scale kingdom as a seamless 4Ã—4 Unity Terrain grid
/// (visual neighbors only â€” not a gameplay hex grid).
/// </summary>
[InitializeOnLoad]
public static class HeightmapTerrainImporter
{
    const string HeightmapFolder = "Assets/Terrain/Heightmaps/GameDevGary";
    const string TerrainDataFolder = "Assets/Terrain/Generated";
    const string DefaultHeightmap = "Heightmap_04_Plains.png";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string GridSessionKey = "Fantasy4x.KingdomGrid.v1";

    static int Tiles => Fantasy4xMapDesign.TerrainGridTilesPerAxis;
    static float TileSize => Fantasy4xMapDesign.TerrainTileSizeUnity;
    static float MaxHeight => Fantasy4xMapDesign.PrototypeMaxHeightUnity;

    static HeightmapTerrainImporter()
    {
        EditorApplication.delayCall += AutoCreateKingdomGridIfNeeded;
    }

    static void AutoCreateKingdomGridIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(GridSessionKey, false)) return;

        var root = GameObject.Find(Fantasy4xMapDesign.KingdomGridRootName);
        if (root != null && root.transform.childCount >= Tiles * Tiles)
        {
            SessionState.SetBool(GridSessionKey, true);
            return;
        }

        try
        {
            CreateKingdomTerrainGrid(batchMode: true, showDialog: false);
            Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange();
            SessionState.SetBool(GridSessionKey, true);
            Debug.Log("[Fantasy4x] Auto-created KingdomTerrainGrid 4Ã—4 + test scenario.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Fantasy4x] Auto kingdom grid skipped: " + ex.Message);
        }
    }

    [MenuItem("Fantasy 4x/Terrain/Create Kingdom Terrain Grid 4x4 (RoK)", false, 5)]
    public static void CreateKingdomTerrainGridMenu()
    {
        try
        {
            CreateKingdomTerrainGrid(batchMode: false, showDialog: true);
            Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange();
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Kingdom Grid", "Failed:\n" + ex.Message, "OK");
            throw;
        }
    }

    /// <summary>
    /// Headless: -executeMethod HeightmapTerrainImporter.CreateKingdomTerrainGridBatch
    /// </summary>
    public static void CreateKingdomTerrainGridBatch()
    {
        try
        {
            CreateKingdomTerrainGrid(batchMode: true, showDialog: false);
            Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SessionState.SetBool(GridSessionKey, true);
            Debug.Log("[Fantasy4x] CreateKingdomTerrainGridBatch OK");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] CreateKingdomTerrainGridBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    // Back-compat aliases (old menus / CI).
    [MenuItem("Fantasy 4x/Terrain/Create Plains Terrain (recommended)", false, 10)]
    public static void CreatePlainsTerrain() => CreateKingdomTerrainGridMenu();

    public static void CreatePlainsTerrainBatch() => CreateKingdomTerrainGridBatch();

    [MenuItem("Fantasy 4x/Terrain/Re-apply Plains Heightmap", false, 15)]
    public static void ApplyPlainsHeightsMenu() => CreateKingdomTerrainGridMenu();

    public static void ApplyPlainsHeightsBatch() => CreateKingdomTerrainGridBatch();

    [MenuItem("Fantasy 4x/Terrain/Log River Mask Usage", false, 50)]
    public static void LogRiverMaskUsage()
    {
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(RiverMaskPath);
        if (mask == null)
        {
            Debug.LogWarning($"[Fantasy4x] River mask not found at {RiverMaskPath}");
            return;
        }

        Debug.Log(
            "[Fantasy4x] River mask ready (CC-BY 3.0 Blindman67).\n" +
            $"Path: {RiverMaskPath}\n" +
            "Later: water / non-walkable â€” not pathfinding here.\n" +
            Fantasy4xMapDesign.RokTierPassNote);
        Selection.activeObject = mask;
    }

    static void CreateKingdomTerrainGrid(bool batchMode, bool showDialog)
    {
        var texturePath = $"{HeightmapFolder}/{DefaultHeightmap}";
        EnsureReadable(texturePath);
        AssetDatabase.ImportAsset(texturePath);

        EnsureFolder(TerrainDataFolder);
        var scene = EnsureWorldMapScene();
        ClearLegacySingleTerrains();

        var decode = LoadHeightmapTexture(texturePath);
        float rawMin, rawMax;
        try
        {
            MeasureGrayscaleRange(decode, out rawMin, out rawMax);
        }
        catch
        {
            Object.DestroyImmediate(decode);
            throw;
        }

        var existingRoot = GameObject.Find(Fantasy4xMapDesign.KingdomGridRootName);
        if (existingRoot != null)
            Object.DestroyImmediate(existingRoot);

        var root = new GameObject(Fantasy4xMapDesign.KingdomGridRootName);
        var terrains = new Terrain[Tiles, Tiles];
        const int resolution = 513;

        try
        {
            for (var tz = 0; tz < Tiles; tz++)
            {
                for (var tx = 0; tx < Tiles; tx++)
                {
                    var name = $"WorldTerrain_{tx}_{tz}";
                    var dataPath = $"{TerrainDataFolder}/{name}_Data.asset";

                    var heights = SampleTileHeights(
                        decode, resolution, tx, tz, Tiles, rawMin, rawMax);

                    TerrainData data;
                    var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                    if (existing != null)
                    {
                        data = existing;
                    }
                    else
                    {
                        data = new TerrainData();
                        AssetDatabase.CreateAsset(data, dataPath);
                    }

                    data.heightmapResolution = resolution;
                    data.size = new Vector3(TileSize, MaxHeight, TileSize);
                    data.SetHeights(0, 0, heights);
                    EditorUtility.SetDirty(data);

                    var go = Terrain.CreateTerrainGameObject(data);
                    go.name = name;
                    go.transform.SetParent(root.transform, true);
                    go.transform.position = new Vector3(tx * TileSize, 0f, tz * TileSize);

                    var terrain = go.GetComponent<Terrain>();
                    terrains[tx, tz] = terrain;
                    Fantasy4xTestScenario.EnsurePlainsTerrainVisualsPublic(terrain);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(decode);
        }

        AssetDatabase.SaveAssets();
        WireTerrainNeighbors(terrains);

        EnsureStrategyCamera();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var msg =
            $"[Fantasy4x] Kingdom grid {Tiles}Ã—{Tiles} @ {TileSize} â†’ " +
            $"{Fantasy4xMapDesign.PrototypeMapWidthUnity}Â² u, maxH={MaxHeight}\n" +
            $"Heightmap {DefaultHeightmap} raw {rawMin:F3}..{rawMax:F3} (stretched)\n" +
            $"â‰ˆ {Fantasy4xMapDesign.PrototypeKingdomWidthKm:F0} km @ {Fantasy4xMapDesign.MetersPerUnityUnit} m/u; " +
            $"players target ~{Fantasy4xMapDesign.TargetPlayersPerKingdomShard}";
        Debug.Log(msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Kingdom Terrain Grid", msg, "OK");
    }

    static void ClearLegacySingleTerrains()
    {
        // Remove old single-tile Plains (and any orphan Terrain roots).
        var legacy = GameObject.Find("WorldTerrain_Plains");
        if (legacy != null)
            Object.DestroyImmediate(legacy);

        var plainsData = $"{TerrainDataFolder}/WorldTerrain_Plains_Data.asset";
        if (File.Exists(plainsData))
            AssetDatabase.DeleteAsset(plainsData);

        // Unique-path leftovers from older imports.
        foreach (var guid in AssetDatabase.FindAssets("WorldTerrain_Plains_Data t:TerrainData", new[] { TerrainDataFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.DeleteAsset(path);
        }
    }

    static void WireTerrainNeighbors(Terrain[,] terrains)
    {
        for (var tz = 0; tz < Tiles; tz++)
        {
            for (var tx = 0; tx < Tiles; tx++)
            {
                var left = tx > 0 ? terrains[tx - 1, tz] : null;
                var right = tx < Tiles - 1 ? terrains[tx + 1, tz] : null;
                var bottom = tz > 0 ? terrains[tx, tz - 1] : null;
                var top = tz < Tiles - 1 ? terrains[tx, tz + 1] : null;
                // Unity: SetNeighbors(left, top, right, bottom)
                terrains[tx, tz].SetNeighbors(left, top, right, bottom);
            }
        }

        Debug.Log("[Fantasy4x] Terrain.SetNeighbors wired for seamless LOD edges.");
    }

    static Texture2D LoadHeightmapTexture(string texturePath)
    {
        var fullPath = Path.GetFullPath(texturePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Heightmap PNG missing", fullPath);

        var fileBytes = File.ReadAllBytes(fullPath);
        var decode = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: true);
        if (!decode.LoadImage(fileBytes, markNonReadable: false))
        {
            Object.DestroyImmediate(decode);
            throw new System.InvalidOperationException("PNG LoadImage failed: " + texturePath);
        }

        return decode;
    }

    static void MeasureGrayscaleRange(Texture2D tex, out float rawMin, out float rawMax)
    {
        rawMin = 1f;
        rawMax = 0f;
        const int samples = 64;
        for (var y = 0; y < samples; y++)
        {
            var v = y / (float)(samples - 1);
            for (var x = 0; x < samples; x++)
            {
                var u = x / (float)(samples - 1);
                var g = Mathf.Clamp01(tex.GetPixelBilinear(u, v).grayscale);
                if (g < rawMin) rawMin = g;
                if (g > rawMax) rawMax = g;
            }
        }
    }

    /// <summary>
    /// Sample one grid tile from the full heightmap (continuous UV coverage).
    /// Shared edges use identical UVs so SetNeighbors morphing stays clean.
    /// </summary>
    static float[,] SampleTileHeights(
        Texture2D tex,
        int resolution,
        int tileX,
        int tileZ,
        int tilesPerAxis,
        float rawMin,
        float rawMax)
    {
        var heights = new float[resolution, resolution];
        var range = Mathf.Max(1e-5f, rawMax - rawMin);
        var u0 = tileX / (float)tilesPerAxis;
        var u1 = (tileX + 1) / (float)tilesPerAxis;
        var v0 = tileZ / (float)tilesPerAxis;
        var v1 = (tileZ + 1) / (float)tilesPerAxis;

        for (var y = 0; y < resolution; y++)
        {
            var v = Mathf.Lerp(v0, v1, y / (float)(resolution - 1));
            for (var x = 0; x < resolution; x++)
            {
                var u = Mathf.Lerp(u0, u1, x / (float)(resolution - 1));
                var g = Mathf.Clamp01(tex.GetPixelBilinear(u, v).grayscale);
                heights[y, x] = (g - rawMin) / range;
            }
        }

        return heights;
    }

    static void EnsureReadable(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        var dirty = false;
        if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            dirty = true;
        }
        if (importer.npotScale != TextureImporterNPOTScale.None)
        {
            importer.npotScale = TextureImporterNPOTScale.None;
            dirty = true;
        }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
        if (importer.sRGBTexture) { importer.sRGBTexture = false; dirty = true; }
        if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; dirty = true; }
        if (dirty)
            importer.SaveAndReimport();
    }

    static Scene EnsureWorldMapScene()
    {
        const string scenePath = "Assets/Scenes/WorldMap.unity";
        EnsureFolder("Assets/Scenes");
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), scenePath)))
            return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        return scene;
    }

    static void EnsureStrategyCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        var map = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var center = new Vector3(map * 0.5f, 0f, map * 0.5f);
        cam.transform.position = center + new Vector3(0f, 600f, -400f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, map * 3f);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
