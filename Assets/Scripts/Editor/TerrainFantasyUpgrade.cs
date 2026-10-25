#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Fantasy kingdom graphics: AmbientCG 2K layers, slope/river splat, 3D water, visible forests.
/// Menu: Fantasy 4x → World → Upgrade Fantasy Graphics (2K + Trees + Water)
/// </summary>
public static class TerrainFantasyUpgrade
{
    const string TexRoot = "Assets/Terrain/Textures2K";
    const string LayerRoot = "Assets/Terrain/Generated/Layers2K";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string WorldMapScenePath = "Assets/Scenes/WorldMap.unity";
    const string PrefabFolder = "Assets/FantasyEnvironment/Prefabs";
    const string ForestRootName = "FantasyForestDecor";
    const string WaterRootName = "RiverWater3D";
    const int AlphamapRes = 2048;
    const int TargetTrees = 2500;
    const float SlopeRockDegrees = 30f;

    [MenuItem("Fantasy 4x/World/Upgrade Fantasy Graphics (2K + Trees + Water)", false, 15)]
    public static void UpgradeMenu()
    {
        try
        {
            UpgradeAll(showDialog: true);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Fantasy Graphics", "Failed:\n" + ex.Message, "OK");
            throw;
        }
    }

    /// <summary>Headless: -executeMethod TerrainFantasyUpgrade.UpgradeBatch</summary>
    public static void UpgradeBatch()
    {
        try
        {
            if (File.Exists(WorldMapScenePath))
                EditorSceneManager.OpenScene(WorldMapScenePath, OpenSceneMode.Single);
            UpgradeAll(showDialog: false);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] UpgradeBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    [InitializeOnLoadMethod]
    static void AutoUpgradeOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool("Fantasy4x.GraphicsUpgrade.v2", false)) return;
            if (Terrain.activeTerrains == null || Terrain.activeTerrains.Length < 16) return;
            try
            {
                UpgradeAll(showDialog: false);
                SessionState.SetBool("Fantasy4x.GraphicsUpgrade.v2", true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Fantasy4x] Auto graphics upgrade skipped: " + ex.Message);
            }
        };
    }

    public static void UpgradeAll(bool showDialog)
    {
        EnsureFolder(LayerRoot);
        var layers = EnsureTerrainLayers();
        var terrains = CollectTerrains();
        if (terrains.Count == 0)
            throw new System.InvalidOperationException("No Terrains — create Kingdom Grid first.");

        var river = LoadRiverMask();
        foreach (var t in terrains)
        {
            ApplyLayersAndResolution(t, layers);
            AutoSplat(t, river, layers.Length);
            ConfigureTreeDraw(t);
        }

        CarveRiverBeds(terrains, river);
        PlaceRealWater(terrains, river);
        PlaceVisibleForests(terrains, river);
        RebakeNavMeshes(terrains);

        try { Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange(); }
        catch (System.Exception ex) { Debug.LogWarning("[Fantasy4x] Resync: " + ex.Message); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        SessionState.SetBool("Fantasy4x.GraphicsUpgrade.v2", true);

        var goTrees = 0;
        var root = GameObject.Find(ForestRootName);
        if (root != null) goTrees = root.transform.childCount;
        var terrainTrees = 0;
        foreach (var t in terrains)
            terrainTrees += t.terrainData.treeInstanceCount;

        var msg =
            $"[Fantasy4x] Fantasy upgrade OK: splat {AlphamapRes}, terrainTrees={terrainTrees}, forestGOs={goTrees}, tiles={terrains.Count}.\n" +
            "Expect: green valleys, rock cliffs, sand riverbeds, glossy water, dense tree clumps near spawn.";
        Debug.Log(msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Fantasy Graphics", msg, "OK");
    }

    struct LayerSet
    {
        public TerrainLayer Grass, Forest, Rock, Sand;
        public int Length => 4;
    }

    static List<Terrain> CollectTerrains()
    {
        var list = new List<Terrain>();
        var root = GameObject.Find(Fantasy4xMapDesign.KingdomGridRootName);
        if (root != null)
            foreach (var t in root.GetComponentsInChildren<Terrain>())
                if (t != null && t.terrainData != null) list.Add(t);
        if (list.Count == 0 && Terrain.activeTerrains != null)
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.terrainData != null) list.Add(t);
        return list;
    }

    static LayerSet EnsureTerrainLayers()
    {
        return new LayerSet
        {
            Grass = MakeLayer("Layer_Grass2K", $"{TexRoot}/Grass/Grass_Color.jpg", $"{TexRoot}/Grass/Grass_Normal.jpg", new Vector2(48f, 48f), 0.05f, 0.35f),
            Forest = MakeLayer("Layer_Forest2K", $"{TexRoot}/Forest/Forest_Color.jpg", $"{TexRoot}/Forest/Forest_Normal.jpg", new Vector2(40f, 40f), 0.05f, 0.4f),
            Rock = MakeLayer("Layer_Rock2K", $"{TexRoot}/Rock/Rock_Color.jpg", $"{TexRoot}/Rock/Rock_Normal.jpg", new Vector2(55f, 55f), 0.1f, 0.55f),
            Sand = MakeLayer("Layer_Sand2K", $"{TexRoot}/Sand/Sand_Color.jpg", $"{TexRoot}/Sand/Sand_Normal.jpg", new Vector2(28f, 28f), 0.05f, 0.45f)
        };
    }

    static TerrainLayer MakeLayer(string name, string colorPath, string normalPath, Vector2 tile, float metallic, float smooth)
    {
        EnsureReadable(colorPath, sRGB: true, normal: false);
        EnsureReadable(normalPath, sRGB: false, normal: true);
        var path = $"{LayerRoot}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (color == null)
            throw new FileNotFoundException("Missing 2K texture", colorPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = color;
        layer.normalMapTexture = normal;
        layer.tileSize = tile;
        layer.metallic = metallic;
        layer.smoothness = smooth;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void EnsureReadable(string assetPath, bool sRGB, bool normal)
    {
        if (!File.Exists(assetPath) && !File.Exists(Path.GetFullPath(assetPath)))
            return;
        AssetDatabase.ImportAsset(assetPath);
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        var dirty = false;
        if (imp.maxTextureSize < 2048) { imp.maxTextureSize = 2048; dirty = true; }
        if (imp.sRGBTexture != sRGB) { imp.sRGBTexture = sRGB; dirty = true; }
        if (normal && imp.textureType != TextureImporterType.NormalMap)
        {
            imp.textureType = TextureImporterType.NormalMap;
            dirty = true;
        }
        if (!normal && imp.textureType != TextureImporterType.Default)
        {
            imp.textureType = TextureImporterType.Default;
            dirty = true;
        }
        if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) imp.SaveAndReimport();
    }

    static void ApplyLayersAndResolution(Terrain terrain, LayerSet layers)
    {
        var td = terrain.terrainData;
        if (td.alphamapResolution < AlphamapRes)
            td.alphamapResolution = AlphamapRes;
        td.terrainLayers = new[] { layers.Grass, layers.Forest, layers.Rock, layers.Sand };
        EditorUtility.SetDirty(td);
    }

    static void ConfigureTreeDraw(Terrain terrain)
    {
        terrain.treeDistance = 8000f;
        terrain.treeBillboardDistance = 2500f;
        terrain.treeCrossFadeLength = 50f;
        terrain.treeMaximumFullLODCount = 200;
        terrain.drawTreesAndFoliage = true;
    }

    static Texture2D LoadRiverMask()
    {
        EnsureReadable(RiverMaskPath, sRGB: false, normal: false);
        var imp = AssetImporter.GetAtPath(RiverMaskPath) as TextureImporter;
        if (imp != null && !imp.isReadable)
        {
            imp.isReadable = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RiverMaskPath);
        if (tex != null) return tex;
        var bytes = File.ReadAllBytes(Path.GetFullPath(RiverMaskPath));
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(bytes);
        return t;
    }

    static float SampleRiver(Texture2D river, float u, float v)
    {
        var c = river.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return Mathf.Clamp01(Mathf.Max(c.b, c.g * 0.25f) - c.r * 0.1f);
    }

    static void AutoSplat(Terrain terrain, Texture2D river, int layerCount)
    {
        var td = terrain.terrainData;
        var aw = td.alphamapWidth;
        var ah = td.alphamapHeight;
        var alphas = new float[ah, aw, layerCount];
        var pos = terrain.transform.position;
        var size = td.size;
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var rockCos = Mathf.Cos(SlopeRockDegrees * Mathf.Deg2Rad);

        for (var y = 0; y < ah; y++)
        {
            var nz = y / (float)(ah - 1);
            var wz = pos.z + nz * size.z;
            var v = wz / mapW;
            for (var x = 0; x < aw; x++)
            {
                var nx = x / (float)(aw - 1);
                var wx = pos.x + nx * size.x;
                var u = wx / mapW;
                var h01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, size.y);
                var nrm = td.GetInterpolatedNormal(nx, nz);
                var steep = nrm.y < rockCos;
                var riverW = SampleRiver(river, u, v);

                float grass = 0f, forest = 0f, rock = 0f, sand = 0f;
                if (riverW > 0.15f)
                {
                    sand = Mathf.SmoothStep(0.15f, 0.7f, riverW);
                    grass = (1f - sand) * 0.35f;
                    forest = (1f - sand) * 0.15f;
                }
                else if (steep)
                {
                    rock = Mathf.InverseLerp(rockCos, 0.35f, nrm.y);
                    rock = Mathf.Clamp01(1f - (nrm.y - 0.2f) / (rockCos - 0.2f));
                    rock = Mathf.Clamp01(rock);
                    if (nrm.y < rockCos) rock = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(rockCos, 0.2f, nrm.y));
                    grass = 1f - rock;
                }
                else if (h01 < 0.38f)
                {
                    var moss = Mathf.PerlinNoise(wx * 0.001f, wz * 0.001f);
                    forest = Mathf.SmoothStep(0.35f, 0.75f, moss) * 0.65f;
                    grass = 1f - forest;
                }
                else
                {
                    grass = 0.75f;
                    forest = 0.15f;
                    rock = 0.1f * Mathf.InverseLerp(0.38f, 0.7f, h01);
                }

                var sum = grass + forest + rock + sand;
                if (sum < 1e-5f) { grass = 1f; sum = 1f; }
                alphas[y, x, 0] = grass / sum;
                alphas[y, x, 1] = forest / sum;
                alphas[y, x, 2] = rock / sum;
                alphas[y, x, 3] = sand / sum;
            }
        }

        td.SetAlphamaps(0, 0, alphas);
        EditorUtility.SetDirty(td);
        terrain.Flush();
    }

    static void CarveRiverBeds(List<Terrain> terrains, Texture2D river)
    {
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
                    if (r < 0.1f) continue;
                    heights[y, x] = Mathf.Max(0f, heights[y, x] - 0.1f * Mathf.SmoothStep(0.1f, 1f, r));
                }
            }
            td.SetHeights(0, 0, heights);

            // Holes only on deep water; keep spawn clearings solid.
            var hw = td.holesResolution;
            var holes = td.GetHoles(0, 0, hw, hw);
            var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
            for (var y = 0; y < hw; y++)
            {
                var wz = pos.z + (y / (float)(hw - 1)) * size.z;
                var v = wz / mapW;
                for (var x = 0; x < hw; x++)
                {
                    var wx = pos.x + (x / (float)(hw - 1)) * size.x;
                    var u = wx / mapW;
                    var dx = wx - mapCenter.x;
                    var dz = wz - mapCenter.z;
                    if (dx * dx + dz * dz < 320f * 320f) { holes[y, x] = true; continue; }
                    var r = SampleRiver(river, u, v);
                    if (r < 0.6f) { holes[y, x] = true; continue; }
                    var ford = (Mathf.FloorToInt(wx / 160f) + Mathf.FloorToInt(wz / 160f)) % 4 == 0;
                    holes[y, x] = ford;
                }
            }
            td.SetHoles(0, 0, holes);
            EditorUtility.SetDirty(td);
            terrain.Flush();
        }
    }

    static Material EnsureWaterMaterial()
    {
        EnsureFolder("Assets/FantasyEnvironment/Materials");
        var path = "Assets/FantasyEnvironment/Materials/Mat_RiverWater3D.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = new Color(0.12f, 0.42f, 0.78f, 0.65f);
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.SetFloat("_Glossiness", 0.92f);
        mat.SetFloat("_Metallic", 0.15f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void PlaceRealWater(List<Terrain> terrains, Texture2D river)
    {
        var old = GameObject.Find(WaterRootName);
        if (old != null) Object.DestroyImmediate(old);
        old = GameObject.Find("RiverWaterMeshes");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(WaterRootName);
        var mat = EnsureWaterMaterial();
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var step = 32f;
        var rng = new System.Random(11);
        var count = 0;
        for (var z = 0f; z < mapW; z += step)
        {
            for (var x = 0f; x < mapW; x += step)
            {
                var u = x / mapW;
                var v = z / mapW;
                var r = SampleRiver(river, u, v);
                if (r < 0.28f) continue;
                var terrain = FindTerrain(terrains, x, z);
                if (terrain == null) continue;
                var bed = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                // Water surface sits above carved bed.
                var y = bed + Mathf.Lerp(0.8f, 2.2f, r);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "WaterCell";
                quad.transform.SetParent(root.transform, true);
                quad.transform.position = new Vector3(x, y, z);
                quad.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 20f, 0f);
                var s = Mathf.Lerp(38f, 70f, r);
                quad.transform.localScale = new Vector3(s, s, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                count++;
            }
        }
        Debug.Log($"[Fantasy4x] Placed {count} 3D water cells.");
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

    static GameObject LoadPrefab(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
    }

    static void PlaceVisibleForests(List<Terrain> terrains, Texture2D river)
    {
        var old = GameObject.Find(ForestRootName);
        if (old != null) Object.DestroyImmediate(old);

        var pine = LoadPrefab("Tree_Pine");
        var oak = LoadPrefab("Tree_Oak");
        var ancient = LoadPrefab("Tree_Ancient");
        var bush = LoadPrefab("Bush_Large");
        var rock = LoadPrefab("Rock_Large");
        if (pine == null || oak == null)
            throw new System.InvalidOperationException("Kenney tree prefabs missing under FantasyEnvironment/Prefabs.");

        // Also register Terrain tree prototypes + instances (mobile LOD path).
        var protos = new List<TreePrototype>
        {
            new TreePrototype { prefab = pine },
            new TreePrototype { prefab = oak },
            new TreePrototype { prefab = ancient != null ? ancient : pine },
            new TreePrototype { prefab = bush != null ? bush : pine },
            new TreePrototype { prefab = rock != null ? rock : pine }
        };

        var root = new GameObject(ForestRootName);
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
        var rng = new System.Random(99);
        var goCount = 0;
        var perTile = Mathf.Max(80, TargetTrees / Mathf.Max(1, terrains.Count));

        // Dense visible clumps near spawn (GameObjects — always render).
        var clumpCenters = new[]
        {
            mapCenter + new Vector3(-350f, 0f, 200f),
            mapCenter + new Vector3(380f, 0f, 180f),
            mapCenter + new Vector3(-200f, 0f, 550f),
            mapCenter + new Vector3(250f, 0f, -400f),
            mapCenter + new Vector3(-700f, 0f, -100f),
            mapCenter + new Vector3(650f, 0f, 400f),
            mapCenter + new Vector3(0f, 0f, 1100f),
            mapCenter + new Vector3(-900f, 0f, 600f)
        };
        foreach (var c in clumpCenters)
        {
            for (var i = 0; i < 45; i++)
            {
                var ox = (float)(rng.NextDouble() * 2 - 1) * 160f;
                var oz = (float)(rng.NextDouble() * 2 - 1) * 160f;
                var wp = c + new Vector3(ox, 0f, oz);
                var u = wp.x / mapW;
                var v = wp.z / mapW;
                if (SampleRiver(river, u, v) > 0.25f) continue;
                var terrain = FindTerrain(terrains, wp.x, wp.z);
                if (terrain == null) continue;
                var y = terrain.SampleHeight(wp) + terrain.transform.position.y;
                var prefab = rng.NextDouble() < 0.55 ? pine : (rng.NextDouble() < 0.7 ? oak : (ancient ?? pine));
                if (rng.NextDouble() < 0.12 && bush != null) prefab = bush;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.SetParent(root.transform, true);
                inst.transform.position = new Vector3(wp.x, y, wp.z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                var sc = 10f + (float)rng.NextDouble() * 10f;
                inst.transform.localScale = Vector3.one * sc;
                goCount++;
            }
        }

        foreach (var terrain in terrains)
        {
            var td = terrain.terrainData;
            td.treePrototypes = protos.ToArray();
            td.RefreshPrototypes();
            var instances = new List<TreeInstance>(perTile);
            var pos = terrain.transform.position;
            var size = td.size;
            var attempts = perTile * 10;
            var placed = 0;
            for (var a = 0; a < attempts && placed < perTile; a++)
            {
                var nx = (float)rng.NextDouble();
                var nz = (float)rng.NextDouble();
                var wx = pos.x + nx * size.x;
                var wz = pos.z + nz * size.z;
                var u = wx / mapW;
                var v = wz / mapW;
                if (SampleRiver(river, u, v) > 0.22f) continue;
                var h01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, size.y);
                var nrm = td.GetInterpolatedNormal(nx, nz);
                var steep = nrm.y < Mathf.Cos(SlopeRockDegrees * Mathf.Deg2Rad);
                var forest = Mathf.PerlinNoise(wx * 0.0012f, wz * 0.0012f);
                int proto;
                float scale;
                if (steep && h01 > 0.28f)
                {
                    if (rng.NextDouble() > 0.4) continue;
                    proto = 4; // rock
                    scale = 0.8f + (float)rng.NextDouble() * 0.6f;
                }
                else if (h01 < 0.45f && forest > 0.32f)
                {
                    proto = rng.NextDouble() < 0.75 ? rng.Next(0, 3) : 3;
                    scale = 1.1f + (float)rng.NextDouble() * 0.7f;
                }
                else if (forest > 0.72f)
                {
                    proto = rng.Next(0, 3);
                    scale = 1f + (float)rng.NextDouble() * 0.5f;
                }
                else continue;

                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),
                    widthScale = scale,
                    heightScale = scale,
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = proto
                });
                placed++;
            }
            td.SetTreeInstances(instances.ToArray(), true);
            EditorUtility.SetDirty(td);
            ConfigureTreeDraw(terrain);
            terrain.Flush();
        }

        Debug.Log($"[Fantasy4x] Forest GameObjects near spawn: {goCount}");
    }

    static void RebakeNavMeshes(List<Terrain> terrains)
    {
        Physics.SyncTransforms();
        foreach (var terrain in terrains)
        {
            var surface = terrain.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = terrain.gameObject.AddComponent<NavMeshSurface>();
            var size = terrain.terrainData.size;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
            surface.size = new Vector3(size.x + 10f, Mathf.Max(size.y * 2f, 200f), size.z + 10f);
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 4f;
            surface.overrideTileSize = true;
            surface.tileSize = 64;
            surface.BuildNavMesh();
        }
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
