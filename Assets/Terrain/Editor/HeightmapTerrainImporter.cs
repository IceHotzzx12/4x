#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a seamless Unity Terrain from a 16-bit grayscale PNG heightmap.
/// Menu: Fantasy 4x → Terrain → …
/// Designed for RoK-style top-down strategy (large XY footprint, modest height).
/// </summary>
public static class HeightmapTerrainImporter
{
    const string HeightmapFolder = "Assets/Terrain/Heightmaps/GameDevGary";
    const string TerrainDataFolder = "Assets/Terrain/Generated";
    const string DefaultHeightmap = "Heightmap_04_Plains.png";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";

    // Strategy top-down defaults: wide map, gentle relief for open marches.
    const float DefaultTerrainWidth = 2000f;
    const float DefaultTerrainLength = 2000f;
    const float DefaultTerrainHeightPlains = 60f;
    const float DefaultTerrainHeightRelief = 180f;

    [MenuItem("Fantasy 4x/Terrain/Create Plains Terrain (recommended)", false, 10)]
    public static void CreatePlainsTerrain()
    {
        CreateTerrainFromAsset($"{HeightmapFolder}/{DefaultHeightmap}", DefaultTerrainHeightPlains, "WorldTerrain_Plains");
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

    static void CreateTerrainFromAsset(string texturePath, float maxHeight, string terrainName)
    {
        EnsureReadable(texturePath);
        AssetDatabase.Refresh();

        var heightmap = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (heightmap == null)
        {
            EditorUtility.DisplayDialog("Heightmap Terrain", $"Could not load texture:\n{texturePath}", "OK");
            return;
        }

        if (!heightmap.isReadable)
        {
            EditorUtility.DisplayDialog(
                "Heightmap Terrain",
                $"{heightmap.name} is not readable.\n" +
                "In the Inspector enable Read/Write, then re-run this menu item.\n" +
                "(Importer .meta files in this PR already set that for GameDevGary maps.)",
                "OK");
            return;
        }

        EnsureFolder(TerrainDataFolder);

        var resolution = PickHeightmapResolution(Mathf.Max(heightmap.width, heightmap.height));
        var heights = SampleHeights(heightmap, resolution);

        var terrainData = new TerrainData();
        terrainData.heightmapResolution = resolution;
        terrainData.size = new Vector3(DefaultTerrainWidth, maxHeight, DefaultTerrainLength);
        terrainData.SetHeights(0, 0, heights);

        var dataPath = $"{TerrainDataFolder}/{terrainName}_Data.asset";
        dataPath = AssetDatabase.GenerateUniqueAssetPath(dataPath);
        AssetDatabase.CreateAsset(terrainData, dataPath);

        var scene = EnsureWorldMapScene();
        var existing = GameObject.Find(terrainName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
        terrainGo.name = terrainName;
        terrainGo.transform.position = Vector3.zero;

        // Top-down strategy camera if none present.
        EnsureStrategyCamera(terrainData.size);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = terrainGo;
        EditorGUIUtility.PingObject(terrainGo);

        Debug.Log(
            $"[Fantasy4x] Created seamless Terrain '{terrainName}' from {texturePath}\n" +
            $"Size: {DefaultTerrainWidth}×{maxHeight}×{DefaultTerrainLength}, heightmapRes={resolution}\n" +
            $"TerrainData: {dataPath}\n" +
            "No grid/tile — single continuous height field.");
    }

    static float[,] SampleHeights(Texture2D tex, int resolution)
    {
        var heights = new float[resolution, resolution];
        // Unity Terrain height samples: [y, x]; texture UV sample across full map.
        for (var y = 0; y < resolution; y++)
        {
            var v = y / (float)(resolution - 1);
            for (var x = 0; x < resolution; x++)
            {
                var u = x / (float)(resolution - 1);
                // Bilinear via GetPixelBilinear; grayscale in R (16-bit PNG imports as R).
                var c = tex.GetPixelBilinear(u, v);
                heights[y, x] = Mathf.Clamp01(c.r);
            }
        }

        return heights;
    }

    static int PickHeightmapResolution(int sourceMax)
    {
        // Power-of-two + 1 required by TerrainData. Cap for mobile.
        if (sourceMax >= 2048) return 1025;
        if (sourceMax >= 1024) return 1025;
        if (sourceMax >= 512) return 513;
        return 257;
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

        // Orthographic-ish top-down for RoK feel; slight pitch so height reads.
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
