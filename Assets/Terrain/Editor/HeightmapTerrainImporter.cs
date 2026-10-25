#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a seamless Unity Terrain from a grayscale PNG heightmap.
/// Menu: Fantasy 4x → Terrain → …
/// Designed for RoK-style top-down strategy (large XY footprint, modest height).
/// </summary>
[InitializeOnLoad]
public static class HeightmapTerrainImporter
{
    const string HeightmapFolder = "Assets/Terrain/Heightmaps/GameDevGary";
    const string TerrainDataFolder = "Assets/Terrain/Generated";
    const string DefaultHeightmap = "Heightmap_04_Plains.png";
    const string PlainsTerrainName = "WorldTerrain_Plains";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string PlainsHeightsSessionKey = "Fantasy4x.PlainsHeights.v3";

    // RoK-scale prototype sizes — see Fantasy4xMapDesign (1200 km via 4000 u × 300 m/u).
    static float DefaultTerrainWidth => Fantasy4xMapDesign.PrototypeTerrainWidthUnity;
    static float DefaultTerrainLength => Fantasy4xMapDesign.PrototypeTerrainLengthUnity;
    static float DefaultTerrainHeightPlains => Fantasy4xMapDesign.PrototypePlainsMaxHeightUnity;
    const float DefaultTerrainHeightRelief = 180f;

    static HeightmapTerrainImporter()
    {
        EditorApplication.delayCall += AutoReapplyPlainsHeightsIfNeeded;
    }

    static void AutoReapplyPlainsHeightsIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(PlainsHeightsSessionKey, false)) return;

        var terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.name != PlainsTerrainName || terrain.terrainData == null)
            return;

        try
        {
            ApplyHeightmapToTerrain(
                terrain,
                $"{HeightmapFolder}/{DefaultHeightmap}",
                DefaultTerrainHeightPlains,
                preserveLayers: true);
            Fantasy4xTestScenario.EnsurePlainsTerrainVisualsPublic(terrain);
            Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange();
            SessionState.SetBool(PlainsHeightsSessionKey, true);
            Debug.Log("[Fantasy4x] Auto-applied Plains heightmap heights (v3) + test scenario resync.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Fantasy4x] Auto plains heights skipped: " + ex.Message);
        }
    }

    [MenuItem("Fantasy 4x/Terrain/Create Plains Terrain (recommended)", false, 10)]
    public static void CreatePlainsTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/{DefaultHeightmap}", DefaultTerrainHeightPlains, PlainsTerrainName);
    }

    /// <summary>
    /// Headless entry: Unity -batchmode -nographics -executeMethod HeightmapTerrainImporter.CreatePlainsTerrainBatch -quit
    /// </summary>
    public static void CreatePlainsTerrainBatch()
    {
        try
        {
            CreateTerrainFromAsset(
                $"{HeightmapFolder}/{DefaultHeightmap}",
                DefaultTerrainHeightPlains,
                PlainsTerrainName,
                batchMode: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fantasy4x] CreatePlainsTerrainBatch OK");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] CreatePlainsTerrainBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    /// <summary>
    /// Re-apply GameDevGary Plains heightmap onto existing WorldTerrain_Plains (keeps grass layers).
    /// Headless: -executeMethod HeightmapTerrainImporter.ApplyPlainsHeightsBatch
    /// </summary>
    [MenuItem("Fantasy 4x/Terrain/Re-apply Plains Heightmap", false, 15)]
    public static void ApplyPlainsHeightsMenu()
    {
        try
        {
            ApplyPlainsHeightsInternal(showDialog: true);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Plains Heightmap", "Failed:\n" + ex.Message, "OK");
            throw;
        }
    }

    public static void ApplyPlainsHeightsBatch()
    {
        try
        {
            ApplyPlainsHeightsInternal(showDialog: false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fantasy4x] ApplyPlainsHeightsBatch OK");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] ApplyPlainsHeightsBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    static void ApplyPlainsHeightsInternal(bool showDialog)
    {
        const string scenePath = "Assets/Scenes/WorldMap.unity";
        if (File.Exists(scenePath))
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var terrainGo = GameObject.Find(PlainsTerrainName);
        var terrain = terrainGo != null ? terrainGo.GetComponent<Terrain>() : Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            throw new System.InvalidOperationException(
                "WorldTerrain_Plains not found. Run Create Plains Terrain first.");

        var stats = ApplyHeightmapToTerrain(
            terrain,
            $"{HeightmapFolder}/{DefaultHeightmap}",
            DefaultTerrainHeightPlains,
            preserveLayers: true);

        Fantasy4xTestScenario.EnsurePlainsTerrainVisualsPublic(terrain);
        Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange();
        SessionState.SetBool(PlainsHeightsSessionKey, true);

        var msg =
            $"[Fantasy4x] Plains heightmap applied (RoK-scale).\n" +
            $"Source: {DefaultHeightmap}\n" +
            $"Terrain size: {DefaultTerrainWidth}×{DefaultTerrainHeightPlains}×{DefaultTerrainLength} " +
            $"(≈ {Fantasy4xMapDesign.PrototypeKingdomWidthKm:F0} km @ {Fantasy4xMapDesign.MetersPerUnityUnit} m/u)\n" +
            $"Resolution: {stats.resolution}\n" +
            $"Raw sample min/max: {stats.rawMin:F3} .. {stats.rawMax:F3} (stretched to 0..1)\n" +
            $"Shard players (design only): {Fantasy4xMapDesign.TargetPlayersPerKingdomShardMin}–{Fantasy4xMapDesign.TargetPlayersPerKingdomShardMax}";
        Debug.Log(msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Plains Heightmap", msg, "OK");
    }

    [MenuItem("Fantasy 4x/Terrain/Create Meadows Terrain", false, 11)]
    public static void CreateMeadowsTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/Heightmap_03_Meadows.png", DefaultTerrainHeightPlains, "WorldTerrain_Meadows");
    }

    [MenuItem("Fantasy 4x/Terrain/Create Mountains Terrain (barrier)", false, 12)]
    public static void CreateMountainsTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/Heightmap_01_Mountains.png", DefaultTerrainHeightRelief, "WorldTerrain_Mountains");
    }

    [MenuItem("Fantasy 4x/Terrain/Create Hills Terrain (barrier)", false, 13)]
    public static void CreateHillsTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/Heightmap_02_Hills.png", 120f, "WorldTerrain_Hills");
    }

    [MenuItem("Fantasy 4x/Terrain/Create Canyon Terrain (barrier)", false, 14)]
    public static void CreateCanyonTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/Heightmap_06_Canyon.png", DefaultTerrainHeightRelief, "WorldTerrain_Canyon");
    }

    [MenuItem("Fantasy 4x/Terrain/Create From Selected Texture…", false, 30)]
    public static void CreateFromSelection()
    {
        var tex = Selection.activeObject as Texture2D;
        if (tex == null)
        {
            EditorUtility.DisplayDialog(
                "Heightmap Terrain",
                "Select a heightmap Texture2D under Assets/Terrain/Heightmaps/ first.",
                "OK");
            return;
        }

        var path = AssetDatabase.GetAssetPath(tex);
        var height = tex.name.Contains("Plains") || tex.name.Contains("Meadows")
            ? DefaultTerrainHeightPlains
            : DefaultTerrainHeightRelief;
        CreateTerrainFromAsset(path, height, $"WorldTerrain_{Sanitize(tex.name)}");
    }

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
            $"Path: {RiverMaskPath} ({mask.width}×{mask.height})\n" +
            "Later: sample this texture in world XZ → treat bright pixels as water / non-walkable.\n" +
            "Do NOT invent pathfinding here — hook into your future movement / NavMesh bake.\n" +
            "Attribution: Blindman67 — https://opengameart.org/content/512-by-512-terrain-elevation-map-with-layers");
        Selection.activeObject = mask;
        EditorGUIUtility.PingObject(mask);
    }

    static void CreateTerrainFromAsset(string texturePath, float maxHeight, string terrainName, bool batchMode = false)
    {
        EnsureReadable(texturePath);
        AssetDatabase.ImportAsset(texturePath);
        AssetDatabase.Refresh();

        EnsureFolder(TerrainDataFolder);

        var dataPath = $"{TerrainDataFolder}/{terrainName}_Data.asset";
        var existingData = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        var preservedLayers = existingData != null ? existingData.terrainLayers : null;

        HeightSampleStats stats;
        TerrainData terrainData;
        if (existingData != null)
        {
            terrainData = existingData;
            stats = ApplyHeightmapToTerrainData(terrainData, texturePath, maxHeight);
            if (preservedLayers != null && preservedLayers.Length > 0)
                terrainData.terrainLayers = preservedLayers;
            EditorUtility.SetDirty(terrainData);
        }
        else
        {
            terrainData = new TerrainData();
            stats = ApplyHeightmapToTerrainData(terrainData, texturePath, maxHeight);
            AssetDatabase.CreateAsset(terrainData, dataPath);
        }

        AssetDatabase.SaveAssets();

        var scene = EnsureWorldMapScene();
        var existing = GameObject.Find(terrainName);
        Terrain terrainComp;
        if (existing != null)
        {
            terrainComp = existing.GetComponent<Terrain>();
            if (terrainComp == null)
                terrainComp = existing.AddComponent<Terrain>();
            terrainComp.terrainData = terrainData;
            var col = existing.GetComponent<TerrainCollider>();
            if (col == null)
                col = existing.AddComponent<TerrainCollider>();
            col.terrainData = terrainData;
            existing.transform.position = Vector3.zero;
        }
        else
        {
            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = terrainName;
            terrainGo.transform.position = Vector3.zero;
            terrainComp = terrainGo.GetComponent<Terrain>();
        }

        if (terrainName == PlainsTerrainName)
            Fantasy4xTestScenario.EnsurePlainsTerrainVisualsPublic(terrainComp);

        EnsureStrategyCamera(terrainData.size);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool(PlainsHeightsSessionKey, true);

        Selection.activeGameObject = terrainComp.gameObject;
        EditorGUIUtility.PingObject(terrainComp.gameObject);

        Debug.Log(
            $"[Fantasy4x] Created/updated seamless Terrain '{terrainName}' from {texturePath}\n" +
            $"Size: {DefaultTerrainWidth}×{maxHeight}×{DefaultTerrainLength}, heightmapRes={stats.resolution}\n" +
            $"Raw sample min/max: {stats.rawMin:F3} .. {stats.rawMax:F3}\n" +
            $"TerrainData: {dataPath}\n" +
            "No grid/tile — single continuous height field.");
    }

    struct HeightSampleStats
    {
        public int resolution;
        public float rawMin;
        public float rawMax;
    }

    static HeightSampleStats ApplyHeightmapToTerrain(
        Terrain terrain,
        string texturePath,
        float maxHeight,
        bool preserveLayers)
    {
        EnsureReadable(texturePath);
        AssetDatabase.ImportAsset(texturePath);

        var layers = preserveLayers ? terrain.terrainData.terrainLayers : null;
        var stats = ApplyHeightmapToTerrainData(terrain.terrainData, texturePath, maxHeight);
        if (layers != null && layers.Length > 0)
            terrain.terrainData.terrainLayers = layers;

        EditorUtility.SetDirty(terrain.terrainData);
        terrain.Flush();
        AssetDatabase.SaveAssets();
        return stats;
    }

    static HeightSampleStats ApplyHeightmapToTerrainData(
        TerrainData terrainData,
        string texturePath,
        float maxHeight)
    {
        var resolution = 1025;
        var heights = SampleHeightsFromPath(texturePath, resolution, out var rawMin, out var rawMax);

        // Changing resolution rebuilds alphamaps — caller may re-apply grass.
        terrainData.heightmapResolution = resolution;
        terrainData.size = new Vector3(DefaultTerrainWidth, maxHeight, DefaultTerrainLength);
        terrainData.SetHeights(0, 0, heights);

        return new HeightSampleStats
        {
            resolution = resolution,
            rawMin = rawMin,
            rawMax = rawMax
        };
    }

    /// <summary>
    /// Decode PNG via LoadImage (reliable for 16-bit grayscale sources) and build Terrain heights.
    /// Samples color.grayscale, then stretches min..max → 0..1 so relief uses full size.y.
    /// </summary>
    static float[,] SampleHeightsFromPath(
        string texturePath,
        int resolution,
        out float rawMin,
        out float rawMax)
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

        try
        {
            return SampleHeights(decode, resolution, out rawMin, out rawMax);
        }
        finally
        {
            Object.DestroyImmediate(decode);
        }
    }

    static float[,] SampleHeights(Texture2D tex, int resolution, out float rawMin, out float rawMax)
    {
        var raw = new float[resolution, resolution];
        rawMin = 1f;
        rawMax = 0f;

        for (var y = 0; y < resolution; y++)
        {
            var v = y / (float)(resolution - 1);
            for (var x = 0; x < resolution; x++)
            {
                var u = x / (float)(resolution - 1);
                var c = tex.GetPixelBilinear(u, v);
                var h = Mathf.Clamp01(c.grayscale);
                raw[y, x] = h;
                if (h < rawMin) rawMin = h;
                if (h > rawMax) rawMax = h;
            }
        }

        var heights = new float[resolution, resolution];
        var range = rawMax - rawMin;
        if (range < 1e-5f)
        {
            // Flat source — keep mid plateau rather than NaNs.
            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
                heights[y, x] = 0.15f;
            Debug.LogWarning("[Fantasy4x] Heightmap samples were flat; using plateau 0.15.");
        }
        else
        {
            // Stretch to full 0..1 so Terrain size.y (e.g. 80) shows real hills.
            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
                heights[y, x] = (raw[y, x] - rawMin) / range;
        }

        return heights;
    }

    static void EnsureReadable(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        var dirty = false;
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            dirty = true;
        }

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

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            dirty = true;
        }

        if (importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            dirty = true;
        }

        // Prefer single-channel import for grayscale heightmaps when available.
        if (importer.textureType != TextureImporterType.SingleChannel)
        {
            importer.textureType = TextureImporterType.SingleChannel;
            importer.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            dirty = true;
        }

        if (importer.maxTextureSize < 2048)
        {
            importer.maxTextureSize = 2048;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    static Scene EnsureWorldMapScene()
    {
        const string scenePath = "Assets/Scenes/WorldMap.unity";
        EnsureFolder("Assets/Scenes");

        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), scenePath)))
        {
            var open = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return open;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        AddSceneToBuildSettings(scenePath);
        return scene;
    }

    static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
        {
            if (s.path == scenePath) return;
        }

        var list = new EditorBuildSettingsScene[scenes.Length + 1];
        for (var i = 0; i < scenes.Length; i++)
            list[i] = scenes[i];
        list[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = list;
    }

    static void EnsureStrategyCamera(Vector3 terrainSize)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        var center = new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);
        cam.transform.position = center + new Vector3(0f, terrainSize.x * 0.55f, -terrainSize.z * 0.15f);
        cam.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
        cam.orthographic = false;
        cam.fieldOfView = 40f;
        cam.farClipPlane = Mathf.Max(5000f, terrainSize.x * 3f);
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

    static string Sanitize(string name)
    {
        return name.Replace("Heightmap_", "").Replace(" ", "_");
    }
}
#endif
