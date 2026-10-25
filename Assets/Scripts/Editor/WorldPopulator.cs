#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Procedural fantasy decor for the 4x4 kingdom Terrain grid.
/// Menu: Fantasy 4x → World → Populate Forests Rivers Rocks
/// ~2000 Terrain-tree instances, Blindman67 rivers, water layer, ford gaps, NavMesh rebake.
/// </summary>
public static class WorldPopulator
{
    const string EnvRoot = "Assets/FantasyEnvironment";
    const string PrefabFolder = EnvRoot + "/Prefabs";
    const string ModelFolder = EnvRoot + "/Models";
    const string MatFolder = EnvRoot + "/Materials";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string WaterLayerPath = "Assets/Terrain/Generated/Layer_RiverWater.terrainlayer";
    const string WaterTexPath = "Assets/Terrain/Generated/Tex_RiverWater.png";
    const string WorldMapScenePath = "Assets/Scenes/WorldMap.unity";
    const string SessionKey = "Fantasy4x.WorldPopulated.v1";

    const int TargetDecorCount = 2000;
    const float ForestHeightMax = 0.42f;
    const float RockSlopeMin = 0.35f;
    const float ClearingNoiseScale = 0.00035f;
    const float ForestNoiseScale = 0.0011f;
    const float RiverCarveDepth = 0.12f;

    [MenuItem("Fantasy 4x/World/Populate Forests Rivers Rocks", false, 20)]
    public static void PopulateMenu()
    {
        try
        {
            PopulateAll(showDialog: true);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("World Populator", "Failed:\n" + ex.Message, "OK");
            throw;
        }
    }

    /// <summary>Headless: -executeMethod WorldPopulator.PopulateBatch</summary>
    public static void PopulateBatch()
    {
        try
        {
            if (File.Exists(WorldMapScenePath))
                EditorSceneManager.OpenScene(WorldMapScenePath, OpenSceneMode.Single);
            PopulateAll(showDialog: false);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] WorldPopulator.PopulateBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    [InitializeOnLoadMethod]
    static void AutoPopulateOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            if (Terrain.activeTerrains == null || Terrain.activeTerrains.Length < 16) return;

            var trees = 0;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t != null && t.terrainData != null)
                    trees += t.terrainData.treeInstanceCount;
            }

            if (trees >= 500)
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            try
            {
                PopulateAll(showDialog: false);
                SessionState.SetBool(SessionKey, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Fantasy4x] Auto WorldPopulator skipped: " + ex.Message);
            }
        };
    }

    public static void PopulateAll(bool showDialog)
    {
        EnsureFolders();
        EnsureEnvironmentPrefabs();
        var prefabs = LoadDecorPrefabs();
        if (prefabs.Trees.Count == 0)
            throw new System.InvalidOperationException(
                "No tree prefabs. Import Kenney FBX under FantasyEnvironment/Models first.");

        var terrains = CollectGridTerrains();
        if (terrains.Count == 0)
            throw new System.InvalidOperationException(
                "No kingdom Terrains. Create Kingdom Terrain Grid first.");

        var river = LoadRiverMask();
        CarveRiversAndPaintWater(terrains, river);
        ScatterTerrainTrees(terrains, prefabs, river);
        PlaceWaterMeshes(terrains, river);
        RebakeNavMeshes(terrains);
        try { Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange(); }
        catch (System.Exception ex) { Debug.LogWarning("[Fantasy4x] Resync after populate: " + ex.Message); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        SessionState.SetBool(SessionKey, true);

        var totalTrees = 0;
        foreach (var t in terrains)
            totalTrees += t.terrainData.treeInstanceCount;

        var msg =
            $"[Fantasy4x] World populated: {totalTrees} Terrain trees/rocks/bushes across {terrains.Count} tiles.\n" +
            "Rivers carved from Blindman67 mask (CC-BY) with water layer + ford gaps.\n" +
            "Open WorldMap and Press Play.";
        Debug.Log(msg);
        if (showDialog)
            EditorUtility.DisplayDialog("World Populator", msg, "OK");
    }

    struct DecorPrefabs
    {
        public List<GameObject> Trees;
        public List<GameObject> Rocks;
        public List<GameObject> Bushes;
    }

    static List<Terrain> CollectGridTerrains()
    {
        var list = new List<Terrain>();
        var root = GameObject.Find(Fantasy4xMapDesign.KingdomGridRootName);
        if (root != null)
        {
            foreach (var t in root.GetComponentsInChildren<Terrain>())
                if (t != null && t.terrainData != null)
                    list.Add(t);
        }

        if (list.Count == 0 && Terrain.activeTerrains != null)
        {
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.terrainData != null)
                    list.Add(t);
        }

        return list;
    }

    static void EnsureFolders()
    {
        EnsureFolder(EnvRoot);
        EnsureFolder(PrefabFolder);
        EnsureFolder(ModelFolder);
        EnsureFolder(MatFolder);
        EnsureFolder("Assets/Terrain/Generated");
    }

    public static void EnsureEnvironmentPrefabs()
    {
        AssetDatabase.Refresh();
        var pineMat = EnsureColorMat("Mat_Pine", new Color(0.18f, 0.42f, 0.22f));
        var oakMat = EnsureColorMat("Mat_Oak", new Color(0.28f, 0.48f, 0.20f));
        var detailMat = EnsureColorMat("Mat_TreeDetail", new Color(0.22f, 0.55f, 0.28f));
        var rockMat = EnsureColorMat("Mat_Rock", new Color(0.45f, 0.43f, 0.40f));
        var bushMat = EnsureColorMat("Mat_Bush", new Color(0.25f, 0.50f, 0.18f));

        CreatePrefabFromModel("tree_pineTallA", "Tree_Pine", pineMat, 14f);
        CreatePrefabFromModel("tree_oak", "Tree_Oak", oakMat, 12f);
        CreatePrefabFromModel("tree_detailed", "Tree_Ancient", detailMat, 16f);
        CreatePrefabFromModel("rock_largeA", "Rock_Large", rockMat, 6f);
        CreatePrefabFromModel("rock_tallB", "Rock_Tall", rockMat, 7f);
        CreatePrefabFromModel("plant_bushDetailed", "Bush_Detail", bushMat, 4f);
        CreatePrefabFromModel("plant_bushLarge", "Bush_Large", bushMat, 5f);
        AssetDatabase.SaveAssets();
    }

    static Material EnsureColorMat(string name, Color color)
    {
        var path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            mat = new Material(shader) { color = color };
            if (shader != null && shader.name.Contains("Standard"))
                mat.SetFloat("_Glossiness", name.Contains("Water") ? 0.9f : 0.15f);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.color = color;
            EditorUtility.SetDirty(mat);
        }

        return mat;
    }

    static void CreatePrefabFromModel(string modelName, string prefabName, Material mat, float uniformScale)
    {
        var prefabPath = $"{PrefabFolder}/{prefabName}.prefab";
        var modelPath = $"{ModelFolder}/{modelName}.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogWarning("[Fantasy4x] Missing model " + modelPath + " — primitive fallback.");
            CreateFallbackPrefab(prefabName, prefabPath, mat, uniformScale,
                modelName.StartsWith("rock"), modelName.StartsWith("plant"));
            return;
        }

        var instance = Object.Instantiate(model);
        instance.name = prefabName;
        instance.transform.localScale = Vector3.one * uniformScale;
        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
                mats[i] = mat;
            r.sharedMaterials = mats;
        }

        foreach (var c in instance.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
    }

    static void CreateFallbackPrefab(
        string prefabName, string prefabPath, Material mat, float scale, bool rock, bool bush)
    {
        GameObject go;
        if (rock)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(scale, scale * 0.7f, scale * 0.85f);
        }
        else if (bush)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.localScale = new Vector3(scale, scale * 0.55f, scale);
        }
        else
        {
            go = new GameObject(prefabName);
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(go.transform, false);
            trunk.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f) * scale;
            trunk.transform.localPosition = new Vector3(0f, 0.45f * scale, 0f);
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.transform.SetParent(go.transform, false);
            crown.transform.localScale = new Vector3(0.7f, 0.85f, 0.7f) * scale;
            crown.transform.localPosition = new Vector3(0f, scale * 1.05f, 0f);
        }

        go.name = prefabName;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterial = mat;
        foreach (var c in go.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
    }

    static DecorPrefabs LoadDecorPrefabs()
    {
        var d = new DecorPrefabs
        {
            Trees = new List<GameObject>(),
            Rocks = new List<GameObject>(),
            Bushes = new List<GameObject>()
        };
        TryAdd(d.Trees, "Tree_Pine");
        TryAdd(d.Trees, "Tree_Oak");
        TryAdd(d.Trees, "Tree_Ancient");
        TryAdd(d.Rocks, "Rock_Large");
        TryAdd(d.Rocks, "Rock_Tall");
        TryAdd(d.Bushes, "Bush_Detail");
        TryAdd(d.Bushes, "Bush_Large");
        return d;
    }

    static void TryAdd(List<GameObject> list, string name)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
        if (go != null)
            list.Add(go);
    }

    static Texture2D LoadRiverMask()
    {
        EnsureReadableTexture(RiverMaskPath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RiverMaskPath);
        if (tex != null)
            return tex;

        var full = Path.GetFullPath(RiverMaskPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("River mask missing", RiverMaskPath);
        var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        raw.LoadImage(File.ReadAllBytes(full));
        return raw;
    }

    static float SampleRiver(Texture2D river, float u, float v)
    {
        var c = river.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return Mathf.Clamp01(Mathf.Max(c.b, c.g * 0.25f) - c.r * 0.1f);
    }

    static void CarveRiversAndPaintWater(List<Terrain> terrains, Texture2D river)
    {
        var waterLayer = EnsureWaterTerrainLayer();
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;

        foreach (var terrain in terrains)
        {
            var td = terrain.terrainData;
            var res = td.heightmapResolution;
            var heights = td.GetHeights(0, 0, res, res);
            var pos = terrain.transform.position;
            var size = td.size;

            for (var y = 0; y < res; y++)
            {
                var wz = pos.z + (y / (float)(res - 1)) * size.z;
                var v = wz / mapW;
                for (var x = 0; x < res; x++)
                {
                    var wx = pos.x + (x / (float)(res - 1)) * size.x;
                    var u = wx / mapW;
                    var r = SampleRiver(river, u, v);
                    if (r < 0.08f)
                        continue;
                    var carve = RiverCarveDepth * Mathf.SmoothStep(0.08f, 1f, r);
                    heights[y, x] = Mathf.Max(0f, heights[y, x] - carve);
                }
            }

            td.SetHeights(0, 0, heights);

            EnsureTerrainHasWaterLayer(terrain, waterLayer);
            var aw = td.alphamapWidth;
            var ah = td.alphamapHeight;
            var alphas = td.GetAlphamaps(0, 0, aw, ah);
            var layers = alphas.GetLength(2);
            for (var y = 0; y < ah; y++)
            {
                var wz = pos.z + (y / (float)(ah - 1)) * size.z;
                var v = wz / mapW;
                for (var x = 0; x < aw; x++)
                {
                    var wx = pos.x + (x / (float)(aw - 1)) * size.x;
                    var u = wx / mapW;
                    var r = SampleRiver(river, u, v);
                    var water = Mathf.SmoothStep(0.12f, 0.55f, r);
                    if (layers >= 2)
                    {
                        alphas[y, x, 0] = 1f - water;
                        alphas[y, x, 1] = water;
                    }
                }
            }

            td.SetAlphamaps(0, 0, alphas);
            ApplyRiverHolesWithFords(terrain, river, mapW);
            EditorUtility.SetDirty(td);
            terrain.Flush();
        }
    }

    static void ApplyRiverHolesWithFords(Terrain terrain, Texture2D river, float mapW)
    {
        var td = terrain.terrainData;
        var hw = td.holesResolution;
        var holes = td.GetHoles(0, 0, hw, hw);
        var pos = terrain.transform.position;
        var size = td.size;
        var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
        var clearCenters = new[]
        {
            mapCenter,
            mapCenter + new Vector3(-500f, 0f, -250f),
            mapCenter + new Vector3(500f, 0f, -250f),
            mapCenter + new Vector3(0f, 0f, 750f),
            mapCenter + new Vector3(0f, 0f, -500f),
            mapCenter + new Vector3(-500f, 0f, 500f),
            mapCenter + new Vector3(500f, 0f, 500f)
        };
        const float clearR2 = 280f * 280f;

        for (var y = 0; y < hw; y++)
        {
            var wz = pos.z + (y / (float)(hw - 1)) * size.z;
            var v = wz / mapW;
            for (var x = 0; x < hw; x++)
            {
                var wx = pos.x + (x / (float)(hw - 1)) * size.x;
                var u = wx / mapW;

                var inClear = false;
                for (var i = 0; i < clearCenters.Length; i++)
                {
                    var dx = wx - clearCenters[i].x;
                    var dz = wz - clearCenters[i].z;
                    if (dx * dx + dz * dz < clearR2) { inClear = true; break; }
                }
                if (inClear)
                {
                    holes[y, x] = true;
                    continue;
                }

                var r = SampleRiver(river, u, v);
                if (r < 0.55f)
                {
                    holes[y, x] = true;
                    continue;
                }

                var ford = (Mathf.FloorToInt(wx / 160f) + Mathf.FloorToInt(wz / 160f)) % 4 == 0;
                holes[y, x] = ford;
            }
        }

        td.SetHoles(0, 0, holes);
    }

    static TerrainLayer EnsureWaterTerrainLayer()
    {
        if (!File.Exists(WaterTexPath))
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var n = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                var c = Color.Lerp(
                    new Color(0.12f, 0.35f, 0.65f, 1f),
                    new Color(0.25f, 0.55f, 0.85f, 1f),
                    n);
                tex.SetPixel(x, y, c);
            }

            tex.Apply();
            File.WriteAllBytes(WaterTexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WaterTexPath);
            var imp = AssetImporter.GetAtPath(WaterTexPath) as TextureImporter;
            if (imp != null)
            {
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.sRGBTexture = true;
                imp.SaveAndReimport();
            }
        }

        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterTexPath);
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(WaterLayerPath);
        if (layer == null)
        {
            layer = new TerrainLayer
            {
                diffuseTexture = diffuse,
                tileSize = new Vector2(64f, 64f),
                metallic = 0.35f,
                smoothness = 0.85f
            };
            AssetDatabase.CreateAsset(layer, WaterLayerPath);
        }
        else
        {
            layer.diffuseTexture = diffuse;
            layer.metallic = 0.35f;
            layer.smoothness = 0.85f;
            EditorUtility.SetDirty(layer);
        }

        return layer;
    }

    static void EnsureTerrainHasWaterLayer(Terrain terrain, TerrainLayer water)
    {
        var td = terrain.terrainData;
        var layers = td.terrainLayers;
        if (layers != null && layers.Length >= 2 && layers[1] == water)
            return;

        TerrainLayer grass = null;
        if (layers != null && layers.Length > 0)
            grass = layers[0];
        if (grass == null)
            grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/Terrain/Generated/Layer_PlainsGrass.terrainlayer");

        td.terrainLayers = new[] { grass, water };
    }

    static void ScatterTerrainTrees(List<Terrain> terrains, DecorPrefabs prefabs, Texture2D river)
    {
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
        var clearCenters = new[]
        {
            mapCenter,
            mapCenter + new Vector3(-500f, 0f, -250f),
            mapCenter + new Vector3(500f, 0f, -250f),
            mapCenter + new Vector3(0f, 0f, 750f),
            mapCenter + new Vector3(-1200f, 0f, 800f),
            mapCenter + new Vector3(1400f, 0f, -900f),
            mapCenter + new Vector3(-1600f, 0f, -1400f),
            mapCenter + new Vector3(1800f, 0f, 1600f)
        };

        var perTile = Mathf.Max(40, TargetDecorCount / Mathf.Max(1, terrains.Count));
        var rng = new System.Random(42);

        foreach (var terrain in terrains)
        {
            var td = terrain.terrainData;
            AssignTreePrototypes(td, prefabs);

            var instances = new List<TreeInstance>(perTile);
            var attempts = perTile * 8;
            var placed = 0;
            var pos = terrain.transform.position;
            var size = td.size;

            for (var a = 0; a < attempts && placed < perTile; a++)
            {
                var nx = (float)rng.NextDouble();
                var nz = (float)rng.NextDouble();
                var wx = pos.x + nx * size.x;
                var wz = pos.z + nz * size.z;
                var u = wx / mapW;
                var v = wz / mapW;

                if (SampleRiver(river, u, v) > 0.2f)
                    continue;

                var world = new Vector3(wx, 0f, wz);
                if (InClearing(world, clearCenters, 220f))
                    continue;

                var height01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, size.y);
                var normal = td.GetInterpolatedNormal(nx, nz);
                var slope = 1f - Mathf.Clamp01(normal.y);

                var clearing = Mathf.PerlinNoise(wx * ClearingNoiseScale, wz * ClearingNoiseScale);
                if (clearing > 0.62f)
                    continue;

                var forest = Mathf.PerlinNoise(wx * ForestNoiseScale + 20f, wz * ForestNoiseScale + 7f);
                TreeInstance inst;

                if (height01 < ForestHeightMax && slope < 0.28f && forest > 0.38f)
                {
                    var proto = rng.NextDouble() < 0.72
                        ? rng.Next(prefabs.Trees.Count)
                        : prefabs.Trees.Count + rng.Next(prefabs.Bushes.Count);
                    var wScale = 0.85f + (float)rng.NextDouble() * 0.45f;
                    var hScale = 0.9f + (float)rng.NextDouble() * 0.5f;
                    inst = MakeTree(nx, nz, proto, wScale, hScale, (float)rng.NextDouble() * 360f);
                }
                else if (slope >= RockSlopeMin && height01 > 0.25f && rng.NextDouble() < 0.35)
                {
                    var proto = prefabs.Trees.Count + prefabs.Bushes.Count + rng.Next(prefabs.Rocks.Count);
                    var wScale = 0.7f + (float)rng.NextDouble() * 0.6f;
                    inst = MakeTree(nx, nz, proto, wScale, wScale, (float)rng.NextDouble() * 360f);
                }
                else if (forest > 0.7f && slope < 0.4f && rng.NextDouble() < 0.12)
                {
                    var proto = rng.Next(prefabs.Trees.Count);
                    var wScale = 0.75f + (float)rng.NextDouble() * 0.4f;
                    inst = MakeTree(nx, nz, proto, wScale, wScale, (float)rng.NextDouble() * 360f);
                }
                else
                {
                    continue;
                }

                instances.Add(inst);
                placed++;
            }

            td.SetTreeInstances(instances.ToArray(), true);
            EditorUtility.SetDirty(td);
            terrain.Flush();
        }
    }

    static bool InClearing(Vector3 world, Vector3[] centers, float radius)
    {
        var r2 = radius * radius;
        for (var i = 0; i < centers.Length; i++)
        {
            var d = world - centers[i];
            d.y = 0f;
            if (d.sqrMagnitude < r2)
                return true;
        }

        return false;
    }

    static TreeInstance MakeTree(float nx, float nz, int prototype, float w, float h, float rotY)
    {
        return new TreeInstance
        {
            position = new Vector3(nx, 0f, nz),
            widthScale = w,
            heightScale = h,
            rotation = rotY * Mathf.Deg2Rad,
            color = Color.white,
            lightmapColor = Color.white,
            prototypeIndex = prototype
        };
    }

    static void AssignTreePrototypes(TerrainData td, DecorPrefabs prefabs)
    {
        var list = new List<TreePrototype>();
        foreach (var t in prefabs.Trees)
            list.Add(new TreePrototype { prefab = t, bendFactor = 0f });
        foreach (var b in prefabs.Bushes)
            list.Add(new TreePrototype { prefab = b, bendFactor = 0f });
        foreach (var r in prefabs.Rocks)
            list.Add(new TreePrototype { prefab = r, bendFactor = 0f });
        td.treePrototypes = list.ToArray();
        td.RefreshPrototypes();
    }

    static void PlaceWaterMeshes(List<Terrain> terrains, Texture2D river)
    {
        const string rootName = "RiverWaterMeshes";
        var existing = GameObject.Find(rootName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        var root = new GameObject(rootName);
        var mat = EnsureColorMat("Mat_RiverWater", new Color(0.15f, 0.45f, 0.85f, 0.75f));
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = new Color(0.15f, 0.45f, 0.85f, 0.72f);
        mat.SetFloat("_Glossiness", 0.9f);
        EditorUtility.SetDirty(mat);

        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var step = 48f;
        var rng = new System.Random(7);
        for (var z = 0f; z < mapW; z += step)
        {
            for (var x = 0f; x < mapW; x += step)
            {
                var u = x / mapW;
                var v = z / mapW;
                if (SampleRiver(river, u, v) < 0.35f)
                    continue;

                var terrain = FindTerrain(terrains, x, z);
                if (terrain == null)
                    continue;
                var y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + 0.35f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "WaterPatch";
                quad.transform.SetParent(root.transform, true);
                quad.transform.position = new Vector3(x, y, z);
                quad.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 30f, 0f);
                var s = 55f + (float)rng.NextDouble() * 25f;
                quad.transform.localScale = new Vector3(s, s, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
            }
        }
    }

    static Terrain FindTerrain(List<Terrain> terrains, float x, float z)
    {
        foreach (var t in terrains)
        {
            var p = t.transform.position;
            var s = t.terrainData.size;
            if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z)
                return t;
        }

        return terrains.Count > 0 ? terrains[0] : null;
    }

    static void RebakeNavMeshes(List<Terrain> terrains)
    {
        Physics.SyncTransforms();
        foreach (var terrain in terrains)
        {
            if (terrain == null)
                continue;
            var surface = terrain.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = terrain.gameObject.AddComponent<NavMeshSurface>();
            var size = terrain.terrainData.size;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
            surface.size = new Vector3(size.x + 10f, Mathf.Max(size.y * 2f, 200f), size.z + 10f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 4f;
            surface.overrideTileSize = true;
            surface.tileSize = 64;
            surface.BuildNavMesh();
        }

        Debug.Log("[Fantasy4x] NavMesh rebaked after rivers/holes.");
    }

    static void EnsureReadableTexture(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;
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

        if (importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
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
