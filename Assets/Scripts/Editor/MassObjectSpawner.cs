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
/// Mass-places Kenney CC0 trees/rocks/bushes via the Terrain Tree System (≥3000),
/// sparse ruins as GameObjects, and glossy URP water quads in riverbeds.
/// Menu: Fantasy 4x → World → Mass Spawn Safe Fantasy (CC0)
/// </summary>
public static class MassObjectSpawner
{
    const string SafeRoot = "Assets/SafeFantasyAssets";
    const string PrefabFolder = SafeRoot + "/Prefabs";
    const string MatFolder = SafeRoot + "/Materials";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string WorldMapScenePath = "Assets/Scenes/WorldMap.unity";
    const string ForestGoRootName = "FantasyForestDecor";
    const string WaterRootName = "RiverWater3D";
    const string RuinsRootName = "SafeFantasyRuins";
    const string SessionKey = "Fantasy4x.MassObjectSpawner.v1";

    const int TargetTerrainTreeCount = 3200;
    const int MaxRuinCount = 48;
    const float ForestHeightMax = 0.45f;
    const float RockSlopeCos = 0.78f; // ~39° from vertical normal.y
    const float RiverAvoid = 0.22f;

    [MenuItem("Fantasy 4x/World/Mass Spawn Safe Fantasy (CC0)", false, 12)]
    public static void SpawnMenu()
    {
        try
        {
            SpawnAll(showDialog: true);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Mass Object Spawner", "Failed:\n" + ex.Message, "OK");
            throw;
        }
    }

    /// <summary>Headless: Unity -batchmode -executeMethod MassObjectSpawner.SpawnBatch -quit</summary>
    public static void SpawnBatch()
    {
        try
        {
            if (File.Exists(WorldMapScenePath))
                EditorSceneManager.OpenScene(WorldMapScenePath, OpenSceneMode.Single);
            SpawnAll(showDialog: false);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] MassObjectSpawner.SpawnBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    [InitializeOnLoadMethod]
    static void AutoSpawnOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            if (!Directory.Exists(Path.GetFullPath(SafeRoot + "/Models/Trees"))) return;
            if (Terrain.activeTerrains == null || Terrain.activeTerrains.Length < 16) return;

            var trees = 0;
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.terrainData != null)
                    trees += t.terrainData.treeInstanceCount;

            if (trees >= TargetTerrainTreeCount)
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            try
            {
                SpawnAll(showDialog: false);
                SessionState.SetBool(SessionKey, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Fantasy4x] Auto MassObjectSpawner skipped: " + ex.Message);
            }
        };
    }

    public static void SpawnAll(bool showDialog)
    {
        EnsureFolders();
        var catalog = BuildPrefabs();
        if (catalog.Trees.Count == 0)
            throw new System.InvalidOperationException(
                "No SafeFantasyAssets tree prefabs. Import Kenney FBX under Assets/SafeFantasyAssets/Models/Trees.");

        var terrains = CollectTerrains();
        if (terrains.Count == 0)
            throw new System.InvalidOperationException("No kingdom Terrains. Create Kingdom Terrain Grid first.");

        ClearLegacyDecor();

        var river = LoadRiverMask();
        ScatterTerrainTrees(terrains, catalog, river);
        PlaceRuins(terrains, catalog, river);
        PlaceUrpWater(terrains, river);
        RebakeNavMeshes(terrains);

        try { Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange(); }
        catch (System.Exception ex) { Debug.LogWarning("[Fantasy4x] Resync: " + ex.Message); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        SessionState.SetBool(SessionKey, true);

        var terrainTrees = 0;
        foreach (var t in terrains)
            terrainTrees += t.terrainData.treeInstanceCount;
        var ruins = GameObject.Find(RuinsRootName);
        var ruinCount = ruins != null ? ruins.transform.childCount : 0;
        var water = GameObject.Find(WaterRootName);
        var waterCount = water != null ? water.transform.childCount : 0;

        var msg =
            $"[Fantasy4x] Safe CC0 mass spawn OK: terrainTrees={terrainTrees}, ruins={ruinCount}, waterCells={waterCount}, tiles={terrains.Count}.\n" +
            "License: Kenney Nature / Fantasy Town / Castle kits — CC0 (see Assets/SafeFantasyAssets/ATTRIBUTION.md).";
        Debug.Log(msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Mass Object Spawner", msg, "OK");
    }

    struct Catalog
    {
        public List<GameObject> Trees;
        public List<GameObject> Rocks;
        public List<GameObject> Bushes;
        public List<GameObject> Ruins;
    }

    static void ClearLegacyDecor()
    {
        // Remove old GameObject forests / water from previous meadow look.
        foreach (var name in new[] { ForestGoRootName, "RiverWaterMeshes", WaterRootName, RuinsRootName, "SafeFantasyDecor" })
        {
            var go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // Clear previous Terrain tree instances (keep prototypes until we replace).
        foreach (var t in CollectTerrains())
        {
            if (t == null || t.terrainData == null) continue;
            t.terrainData.treeInstances = System.Array.Empty<TreeInstance>();
            EditorUtility.SetDirty(t.terrainData);
            t.Flush();
        }
    }

    static Catalog BuildPrefabs()
    {
        var treeMat = EnsureLitMat("Mat_SafeTree", new Color(0.22f, 0.48f, 0.20f), 0f, 0.35f);
        var pineMat = EnsureLitMat("Mat_SafePine", new Color(0.14f, 0.36f, 0.18f), 0f, 0.3f);
        var oakMat = EnsureLitMat("Mat_SafeOak", new Color(0.28f, 0.52f, 0.22f), 0f, 0.38f);
        var rockMat = EnsureLitMat("Mat_SafeRock", new Color(0.45f, 0.44f, 0.42f), 0.05f, 0.45f);
        var bushMat = EnsureLitMat("Mat_SafeBush", new Color(0.25f, 0.55f, 0.22f), 0f, 0.4f);
        var ruinMat = EnsureLitMat("Mat_SafeRuin", new Color(0.55f, 0.52f, 0.48f), 0.08f, 0.4f);

        var cat = new Catalog
        {
            Trees = new List<GameObject>(),
            Rocks = new List<GameObject>(),
            Bushes = new List<GameObject>(),
            Ruins = new List<GameObject>()
        };

        foreach (var path in Directory.GetFiles(Path.GetFullPath(SafeRoot + "/Models/Trees"), "*.fbx"))
        {
            var asset = "Assets/SafeFantasyAssets/Models/Trees/" + Path.GetFileName(path);
            ConfigureFbx(asset);
            var name = Path.GetFileNameWithoutExtension(path);
            var mat = name.Contains("pine") ? pineMat : name.Contains("oak") ? oakMat : treeMat;
            var prefab = EnsurePrefab(asset, "Tree_" + name, mat, scale: 1f);
            if (prefab != null) cat.Trees.Add(prefab);
        }

        foreach (var path in Directory.GetFiles(Path.GetFullPath(SafeRoot + "/Models/Rocks"), "*.fbx"))
        {
            var asset = "Assets/SafeFantasyAssets/Models/Rocks/" + Path.GetFileName(path);
            ConfigureFbx(asset);
            var name = Path.GetFileNameWithoutExtension(path);
            var prefab = EnsurePrefab(asset, "Rock_" + name, rockMat, scale: 1.2f);
            if (prefab != null) cat.Rocks.Add(prefab);
        }

        foreach (var path in Directory.GetFiles(Path.GetFullPath(SafeRoot + "/Models/Bushes"), "*.fbx"))
        {
            var asset = "Assets/SafeFantasyAssets/Models/Bushes/" + Path.GetFileName(path);
            ConfigureFbx(asset);
            var name = Path.GetFileNameWithoutExtension(path);
            var prefab = EnsurePrefab(asset, "Bush_" + name, bushMat, scale: 1f);
            if (prefab != null) cat.Bushes.Add(prefab);
        }

        foreach (var path in Directory.GetFiles(Path.GetFullPath(SafeRoot + "/Models/Ruins"), "*.fbx"))
        {
            var asset = "Assets/SafeFantasyAssets/Models/Ruins/" + Path.GetFileName(path);
            ConfigureFbx(asset);
            var name = Path.GetFileNameWithoutExtension(path);
            var prefab = EnsurePrefab(asset, "Ruin_" + name, ruinMat, scale: 2.5f);
            if (prefab != null) cat.Ruins.Add(prefab);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return cat;
    }

    static void ConfigureFbx(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath);
        var imp = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (imp == null) return;
        var dirty = false;
        if (imp.materialImportMode != ModelImporterMaterialImportMode.None)
        {
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            dirty = true;
        }
        if (Mathf.Abs(imp.globalScale - 1f) > 0.001f)
        {
            imp.globalScale = 1f;
            dirty = true;
        }
        if (imp.isReadable)
        {
            imp.isReadable = false;
            dirty = true;
        }
        if (dirty) imp.SaveAndReimport();
    }

    static GameObject EnsurePrefab(string fbxPath, string prefabName, Material mat, float scale)
    {
        var prefabPath = $"{PrefabFolder}/{prefabName}.prefab";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) return null;

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            ApplyMaterialRecursive(existing, mat);
            existing.transform.localScale = Vector3.one * scale;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var instance = Object.Instantiate(model);
        instance.name = prefabName;
        instance.transform.localScale = Vector3.one * scale;
        ApplyMaterialRecursive(instance, mat);
        // Terrain trees need a LOD-friendly root; strip colliders for tree prototypes.
        foreach (var col in instance.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(col);

        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    static void ApplyMaterialRecursive(GameObject go, Material mat)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (var i = 0; i < mats.Length; i++)
                mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    static Material EnsureLitMat(string name, Color color, float metallic, float smooth)
    {
        EnsureFolder(MatFolder);
        var path = $"{MatFolder}/{name}.mat";
        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("URP/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(lit != null ? lit : Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        if (lit != null) mat.shader = lit;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void ScatterTerrainTrees(List<Terrain> terrains, Catalog catalog, Texture2D river)
    {
        var prototypes = new List<TreePrototype>();
        foreach (var t in catalog.Trees)
            prototypes.Add(new TreePrototype { prefab = t });
        foreach (var b in catalog.Bushes)
            prototypes.Add(new TreePrototype { prefab = b });
        foreach (var r in catalog.Rocks)
            prototypes.Add(new TreePrototype { prefab = r });

        var treeProtoCount = catalog.Trees.Count;
        var bushProtoStart = treeProtoCount;
        var bushProtoCount = catalog.Bushes.Count;
        var rockProtoStart = bushProtoStart + bushProtoCount;
        var rockProtoCount = catalog.Rocks.Count;

        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var perTile = Mathf.Max(120, TargetTerrainTreeCount / Mathf.Max(1, terrains.Count));
        var rng = new System.Random(2026);
        var total = 0;

        foreach (var terrain in terrains)
        {
            var td = terrain.terrainData;
            td.treePrototypes = prototypes.ToArray();
            var instances = new List<TreeInstance>(perTile + 64);
            var pos = terrain.transform.position;
            var size = td.size;
            var attempts = perTile * 8;
            var placed = 0;

            for (var a = 0; a < attempts && placed < perTile; a++)
            {
                var nx = (float)rng.NextDouble();
                var nz = (float)rng.NextDouble();
                var wx = pos.x + nx * size.x;
                var wz = pos.z + nz * size.z;
                var u = wx / mapW;
                var v = wz / mapW;
                var riverW = SampleRiver(river, u, v);
                if (riverW > RiverAvoid) continue;

                var h01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, size.y);
                var nrm = td.GetInterpolatedNormal(nx, nz);
                var steep = nrm.y < RockSlopeCos;
                var valley = h01 < ForestHeightMax;

                int proto;
                float widthScale;
                float heightScale;

                if (steep && rockProtoCount > 0)
                {
                    // Cliffs / steep slopes → rocks
                    if ((float)rng.NextDouble() > 0.55f) continue;
                    proto = rockProtoStart + rng.Next(rockProtoCount);
                    widthScale = Mathf.Lerp(1.2f, 2.4f, (float)rng.NextDouble());
                    heightScale = widthScale;
                }
                else if (valley && treeProtoCount > 0)
                {
                    // Dense forests in valleys
                    var dens = Mathf.PerlinNoise(wx * 0.0012f, wz * 0.0012f);
                    if (dens < 0.28f) continue;
                    if ((float)rng.NextDouble() > 0.72f && bushProtoCount > 0)
                    {
                        proto = bushProtoStart + rng.Next(bushProtoCount);
                        widthScale = Mathf.Lerp(0.8f, 1.4f, (float)rng.NextDouble());
                    }
                    else
                    {
                        proto = rng.Next(treeProtoCount);
                        widthScale = Mathf.Lerp(0.85f, 1.55f, (float)rng.NextDouble());
                    }
                    heightScale = widthScale * Mathf.Lerp(0.9f, 1.15f, (float)rng.NextDouble());
                }
                else if (bushProtoCount > 0 && (float)rng.NextDouble() < 0.12f)
                {
                    proto = bushProtoStart + rng.Next(bushProtoCount);
                    widthScale = Mathf.Lerp(0.7f, 1.2f, (float)rng.NextDouble());
                    heightScale = widthScale;
                }
                else continue;

                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),
                    prototypeIndex = proto,
                    widthScale = widthScale,
                    heightScale = heightScale,
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white
                });
                placed++;
            }

            td.SetTreeInstances(instances.ToArray(), snapToHeightmap: true);
            terrain.treeDistance = 8000f;
            terrain.treeBillboardDistance = 2200f;
            terrain.treeCrossFadeLength = 40f;
            terrain.treeMaximumFullLODCount = 150;
            terrain.drawTreesAndFoliage = true;
            EditorUtility.SetDirty(td);
            terrain.Flush();
            total += instances.Count;
        }

        Debug.Log($"[Fantasy4x] Terrain trees placed: {total} (target ≥{TargetTerrainTreeCount}).");
    }

    static void PlaceRuins(List<Terrain> terrains, Catalog catalog, Texture2D river)
    {
        if (catalog.Ruins.Count == 0) return;

        var root = new GameObject(RuinsRootName);
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var rng = new System.Random(77);
        var placed = 0;
        var attempts = MaxRuinCount * 20;

        for (var a = 0; a < attempts && placed < MaxRuinCount; a++)
        {
            var wx = (float)rng.NextDouble() * mapW;
            var wz = (float)rng.NextDouble() * mapW;
            var u = wx / mapW;
            var v = wz / mapW;
            if (SampleRiver(river, u, v) > 0.18f) continue;

            var terrain = FindTerrain(terrains, wx, wz);
            if (terrain == null) continue;
            var td = terrain.terrainData;
            var local = new Vector3(wx, 0f, wz) - terrain.transform.position;
            var nx = Mathf.Clamp01(local.x / td.size.x);
            var nz = Mathf.Clamp01(local.z / td.size.z);
            var h01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, td.size.y);
            // Ruins on mid slopes / ridges, not deep valley forest cores.
            if (h01 < 0.18f || h01 > 0.72f) continue;
            var dens = Mathf.PerlinNoise(wx * 0.0004f + 3.1f, wz * 0.0004f);
            if (dens < 0.55f) continue;

            var prefab = catalog.Ruins[rng.Next(catalog.Ruins.Count)];
            var y = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(root.transform, true);
            go.transform.position = new Vector3(wx, y, wz);
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var s = Mathf.Lerp(1.8f, 3.2f, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * s;
            placed++;
        }

        Debug.Log($"[Fantasy4x] Ruins placed: {placed}");
    }

    static void PlaceUrpWater(List<Terrain> terrains, Texture2D river)
    {
        var old = GameObject.Find(WaterRootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(WaterRootName);
        var mat = Unity6UrpBootstrap.EnsureUrpWaterMaterial(
            $"{MatFolder}/Mat_SafeRiverWater.mat",
            new Color(0.12f, 0.42f, 0.78f, 0.62f));

        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var step = 28f;
        var rng = new System.Random(11);
        var count = 0;
        for (var z = 0f; z < mapW; z += step)
        {
            for (var x = 0f; x < mapW; x += step)
            {
                var r = SampleRiver(river, x / mapW, z / mapW);
                if (r < 0.26f) continue;
                var terrain = FindTerrain(terrains, x, z);
                if (terrain == null) continue;
                var bed = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                var y = bed + Mathf.Lerp(0.6f, 2.0f, r);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "WaterCell";
                quad.transform.SetParent(root.transform, true);
                quad.transform.position = new Vector3(x, y, z);
                quad.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 25f, 0f);
                var s = Mathf.Lerp(34f, 64f, r);
                quad.transform.localScale = new Vector3(s, s, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                count++;
            }
        }
        Debug.Log($"[Fantasy4x] URP water cells: {count}");
    }

    static void RebakeNavMeshes(List<Terrain> terrains)
    {
        Physics.SyncTransforms();
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            var col = terrain.GetComponent<TerrainCollider>();
            if (col == null) col = terrain.gameObject.AddComponent<TerrainCollider>();
            col.enabled = true;
            col.terrainData = terrain.terrainData;
            terrain.Flush();

            var size = terrain.terrainData.size;
            var surface = terrain.GetComponent<NavMeshSurface>();
            if (surface == null) surface = terrain.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
            surface.size = new Vector3(size.x + 10f, Mathf.Max(size.y * 2f, 200f), size.z + 10f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.minRegionArea = 4f;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 4f;
            surface.overrideTileSize = true;
            surface.tileSize = 64;
            surface.BuildNavMesh();
        }
        var tri = NavMesh.CalculateTriangulation();
        var n = tri.indices != null ? tri.indices.Length / 3 : 0;
        Debug.Log($"[Fantasy4x] NavMesh rebake after mass spawn, triangles={n}");
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

    static Texture2D LoadRiverMask()
    {
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

    static void EnsureFolders()
    {
        EnsureFolder(SafeRoot);
        EnsureFolder(SafeRoot + "/Models");
        EnsureFolder(SafeRoot + "/Models/Trees");
        EnsureFolder(SafeRoot + "/Models/Rocks");
        EnsureFolder(SafeRoot + "/Models/Bushes");
        EnsureFolder(SafeRoot + "/Models/Ruins");
        EnsureFolder(PrefabFolder);
        EnsureFolder(MatFolder);
        EnsureFolder(SafeRoot + "/Licenses");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
