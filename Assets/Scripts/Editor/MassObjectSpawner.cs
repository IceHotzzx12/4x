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
/// Mass-places Kenney CC0 trees/rocks/bushes as visible GameObjects (≥3000).
/// URP cannot draw custom Lit meshes as Terrain trees (Soft Occlusion required) —
/// so we use real MeshRenderer instances for a dense, commercial-safe look.
/// Sparse ruins + glossy URP water in riverbeds.
/// Menu: Fantasy 4x → World → Mass Spawn Safe Fantasy (CC0)
/// </summary>
public static class MassObjectSpawner
{
    const string SafeRoot = "Assets/SafeFantasyAssets";
    const string PrefabFolder = SafeRoot + "/Prefabs";
    const string MatFolder = SafeRoot + "/Materials";
    const string RiverMaskPath = "Assets/Terrain/RiverMask/Blindman67/River.png";
    const string WorldMapScenePath = "Assets/Scenes/WorldMap.unity";
    const string ForestGoRootName = "SafeFantasyForest";
    const string LegacyForestName = "FantasyForestDecor";
    const string WaterRootName = "RiverWater3D";
    const string RuinsRootName = "SafeFantasyRuins";
    const string SessionKey = "Fantasy4x.MassObjectSpawner.v2";

    const int TargetForestCount = 3200;
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

            var forest = GameObject.Find(ForestGoRootName);
            if (forest != null && forest.transform.childCount >= TargetForestCount)
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
        var forestCount = PlaceVisibleForestGOs(terrains, catalog, river);
        PlaceRuins(terrains, catalog, river);
        PlaceUrpWater(terrains, river);
        RebakeNavMeshes(terrains);

        try { Fantasy4xTestScenario.ResyncTestScenarioAfterTerrainChange(); }
        catch (System.Exception ex) { Debug.LogWarning("[Fantasy4x] Resync: " + ex.Message); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), WorldMapScenePath);
        AssetDatabase.SaveAssets();
        SessionState.SetBool(SessionKey, true);

        var ruins = GameObject.Find(RuinsRootName);
        var ruinCount = ruins != null ? ruins.transform.childCount : 0;
        var water = GameObject.Find(WaterRootName);
        var waterCount = water != null ? water.transform.childCount : 0;

        var msg =
            $"[Fantasy4x] Safe CC0 VISIBLE forest OK: forestGOs={forestCount}, ruins={ruinCount}, waterCells={waterCount}, tiles={terrains.Count}.\n" +
            "Look for SafeFantasyForest in Hierarchy. License: Kenney CC0.";
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
        foreach (var name in new[]
                 {
                     ForestGoRootName, LegacyForestName, "RiverWaterMeshes", WaterRootName,
                     RuinsRootName, "SafeFantasyDecor"
                 })
        {
            var go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // Clear broken Terrain trees (URP Lit Kenney meshes do not draw as Soft Occlusion trees).
        foreach (var t in CollectTerrains())
        {
            if (t == null || t.terrainData == null) continue;
            t.terrainData.treeInstances = System.Array.Empty<TreeInstance>();
            t.terrainData.treePrototypes = System.Array.Empty<TreePrototype>();
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

    /// <summary>
    /// Visible MeshRenderer forests. Terrain trees stay unused — URP rejects non-Soft-Occlusion prototypes.
    /// </summary>
    static int PlaceVisibleForestGOs(List<Terrain> terrains, Catalog catalog, Texture2D river)
    {
        var root = new GameObject(ForestGoRootName);
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
        var rng = new System.Random(2026);
        var placed = 0;
        var perTile = Mathf.Max(180, TargetForestCount / Mathf.Max(1, terrains.Count));

        // Extra-dense clumps around spawn so Game view immediately shows forest.
        var clumpCenters = new[]
        {
            mapCenter + new Vector3(-280f, 0f, 180f),
            mapCenter + new Vector3(320f, 0f, 220f),
            mapCenter + new Vector3(-180f, 0f, 480f),
            mapCenter + new Vector3(220f, 0f, -360f),
            mapCenter + new Vector3(-620f, 0f, -80f),
            mapCenter + new Vector3(580f, 0f, 360f),
            mapCenter + new Vector3(40f, 0f, 980f),
            mapCenter + new Vector3(-820f, 0f, 520f),
            mapCenter + new Vector3(760f, 0f, -420f),
            mapCenter + new Vector3(-120f, 0f, -720f)
        };
        foreach (var c in clumpCenters)
        {
            for (var i = 0; i < 70; i++)
            {
                var ox = (float)(rng.NextDouble() * 2.0 - 1.0) * 140f;
                var oz = (float)(rng.NextDouble() * 2.0 - 1.0) * 140f;
                var wx = c.x + ox;
                var wz = c.z + oz;
                if (SampleRiver(river, wx / mapW, wz / mapW) > RiverAvoid) continue;
                var terrain = FindTerrain(terrains, wx, wz);
                if (terrain == null) continue;
                var prefab = catalog.Trees[rng.Next(catalog.Trees.Count)];
                if (catalog.Bushes.Count > 0 && (float)rng.NextDouble() < 0.25f)
                    prefab = catalog.Bushes[rng.Next(catalog.Bushes.Count)];
                placed += PlaceOne(root, prefab, terrain, wx, wz, rng, 1.0f, 1.8f) ? 1 : 0;
            }
        }

        foreach (var terrain in terrains)
        {
            var td = terrain.terrainData;
            var pos = terrain.transform.position;
            var size = td.size;
            var attempts = perTile * 10;
            var tilePlaced = 0;
            for (var a = 0; a < attempts && tilePlaced < perTile; a++)
            {
                var nx = (float)rng.NextDouble();
                var nz = (float)rng.NextDouble();
                var wx = pos.x + nx * size.x;
                var wz = pos.z + nz * size.z;
                if (SampleRiver(river, wx / mapW, wz / mapW) > RiverAvoid) continue;

                var h01 = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.01f, size.y);
                var nrm = td.GetInterpolatedNormal(nx, nz);
                var steep = nrm.y < RockSlopeCos;
                var valley = h01 < ForestHeightMax;

                GameObject prefab;
                float sMin, sMax;
                if (steep && catalog.Rocks.Count > 0)
                {
                    if ((float)rng.NextDouble() > 0.5f) continue;
                    prefab = catalog.Rocks[rng.Next(catalog.Rocks.Count)];
                    sMin = 1.4f;
                    sMax = 2.8f;
                }
                else if (valley && catalog.Trees.Count > 0)
                {
                    var dens = Mathf.PerlinNoise(wx * 0.0012f, wz * 0.0012f);
                    if (dens < 0.25f) continue;
                    if (catalog.Bushes.Count > 0 && (float)rng.NextDouble() > 0.7f)
                    {
                        prefab = catalog.Bushes[rng.Next(catalog.Bushes.Count)];
                        sMin = 0.9f;
                        sMax = 1.5f;
                    }
                    else
                    {
                        prefab = catalog.Trees[rng.Next(catalog.Trees.Count)];
                        sMin = 1.1f;
                        sMax = 2.0f;
                    }
                }
                else if (catalog.Bushes.Count > 0 && (float)rng.NextDouble() < 0.1f)
                {
                    prefab = catalog.Bushes[rng.Next(catalog.Bushes.Count)];
                    sMin = 0.8f;
                    sMax = 1.3f;
                }
                else continue;

                if (!PlaceOne(root, prefab, terrain, wx, wz, rng, sMin, sMax)) continue;
                tilePlaced++;
                placed++;
            }
        }

        Debug.Log($"[Fantasy4x] Visible forest GOs: {placed} under {ForestGoRootName}");
        return placed;
    }

    static bool PlaceOne(
        GameObject root, GameObject prefab, Terrain terrain, float wx, float wz,
        System.Random rng, float sMin, float sMax)
    {
        if (prefab == null || terrain == null) return false;
        var y = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(root.transform, true);
        go.transform.position = new Vector3(wx, y, wz);
        go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        var s = Mathf.Lerp(sMin, sMax, (float)rng.NextDouble());
        go.transform.localScale = Vector3.one * s;
        return true;
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
