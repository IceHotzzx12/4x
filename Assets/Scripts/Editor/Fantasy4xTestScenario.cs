#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Fantasy 4x -> Gameplay menu: spawn OpenRTS test armies/barbarians and bake NavMesh.
/// </summary>
public static class Fantasy4xTestScenario
{
    const string PlayerMatPath = "Assets/Scripts/Generated/Mat_PlayerArmy.mat";
    const string BarbMatPath = "Assets/Scripts/Generated/Mat_Barbarian.mat";
    const string WorldMapScenePath = "Assets/Scenes/WorldMap.unity";

    [MenuItem("Fantasy 4x/Gameplay/Spawn Test Scenario", false, 100)]
    public static void SpawnTestScenario()
    {
        if (!TrySpawnTestScenario(showDialogs: true))
            return;

        Selection.activeGameObject = GameObject.Find("PlayerArmy_1");
        Debug.Log(
            "[Fantasy4x] OpenRTS test: 3 blue + 3 red, ~500u apart on kingdom map center.\n" +
            "Play: LMB-drag select -> RMB march / attack. Camera pan speeds up when zoomed out.");
    }

    /// <summary>
    /// Headless entry: Unity -batchmode -nographics -executeMethod Fantasy4xTestScenario.SpawnTestScenarioBatch -quit
    /// Opens WorldMap, spawns OpenRts units, bakes NavMesh, saves scene.
    /// </summary>
    public static void SpawnTestScenarioBatch()
    {
        try
        {
            if (!System.IO.File.Exists(WorldMapScenePath))
            {
                throw new System.InvalidOperationException(
                    "WorldMap scene missing. Run Fantasy 4x → Terrain → Create Kingdom Terrain Grid first.");
            }

            EditorSceneManager.OpenScene(WorldMapScenePath, OpenSceneMode.Single);

            if (!TrySpawnTestScenario(showDialogs: false))
                throw new System.InvalidOperationException("SpawnTestScenario failed (no Terrain?).");

            var tri = NavMesh.CalculateTriangulation();
            var triCount = tri.indices != null ? tri.indices.Length / 3 : 0;

            var playerOk = 0;
            var enemyOk = 0;
            var unitTotal = 0;
            foreach (var u in Object.FindObjectsOfType<OpenRtsUnit>())
            {
                unitTotal++;
                var onMesh = NavMesh.SamplePosition(u.transform.position, out _, 80f, NavMesh.AllAreas);
                if (!onMesh) continue;
                if (u.isPlayerOwned) playerOk++;
                else enemyOk++;
            }

            var hasSel = Object.FindObjectOfType<OpenRtsSelection>() != null;
            var hasCam = Object.FindObjectOfType<OpenRtsCamera>() != null;
            var tileCount = Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0;

            Debug.Log(
                $"[Fantasy4x] tiles={tileCount}, NavMesh triangles={triCount}, units={unitTotal}, " +
                $"playerOk={playerOk}, enemyOk={enemyOk}, OpenRtsSelection={hasSel}, OpenRtsCamera={hasCam}");

            if (tileCount < 16 || triCount < 100 || playerOk < 3 || enemyOk < 3 || !hasSel || !hasCam)
                throw new System.InvalidOperationException(
                    $"Playable setup incomplete: tiles={tileCount}, triangles={triCount}, playerOk={playerOk}, enemyOk={enemyOk}");

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), WorldMapScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fantasy4x] SpawnTestScenarioBatch OK");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] SpawnTestScenarioBatch FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    static bool TrySpawnTestScenario(bool showDialogs)
    {
        var mapW = Fantasy4xMapDesign.PrototypeMapWidthUnity;
        var mapCenter = new Vector3(mapW * 0.5f, 0f, mapW * 0.5f);
        var terrain = OpenRtsCamera.FindTerrainAt(mapCenter) ?? Terrain.activeTerrain;
        if (terrain == null || Terrain.activeTerrains == null || Terrain.activeTerrains.Length == 0)
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog(
                    "Spawn Test Scenario",
                    "No Terrain found.\n\nRun Fantasy 4x → Terrain → Create Kingdom Terrain Grid 4x4 first.",
                    "OK");
            }
            else
            {
                Debug.LogError("[Fantasy4x] No Terrain found. Create Kingdom Terrain Grid first.");
            }

            return false;
        }

        EnsureGameplayControllers(mapCenter);
        foreach (var t in Terrain.activeTerrains)
            EnsurePlainsTerrainVisuals(t);
        EnsureMaterials();
        ClearExistingTestUnits();

        // Bake walkable surface on every tile before warping agents.
        BakeNavMeshForAllTerrains();

        // ~500 units apart across the kingdom map center (RoK open-field feel).
        var playerOffsets = new[]
        {
            new Vector3(-500f, 0f, -250f),
            new Vector3(0f, 0f, -500f),
            new Vector3(500f, 0f, -250f)
        };
        var barbOffsets = new[]
        {
            new Vector3(-500f, 0f, 500f),
            new Vector3(0f, 0f, 750f),
            new Vector3(500f, 0f, 500f)
        };
        var center = mapCenter;

        var playerMat = AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath);
        var barbMat = AssetDatabase.LoadAssetAtPath<Material>(BarbMatPath);

        for (var i = 0; i < 3; i++)
            SpawnPlayerArmy($"PlayerArmy_{i + 1}", SampleTerrainPoint(center + playerOffsets[i]), playerMat);

        for (var i = 0; i < 3; i++)
            SpawnBarbarian($"Barbarian_{i + 1}", SampleTerrainPoint(center + barbOffsets[i]), barbMat);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return true;
    }

    [MenuItem("Fantasy 4x/Terrain/Apply Plains Ground Visuals", false, 20)]
    public static void ApplyPlainsGroundVisualsMenu()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            EditorUtility.DisplayDialog(
                "Plains Visuals",
                "No Terrain in the scene.\n\nOpen WorldMap or run Create Plains Terrain first.",
                "OK");
            return;
        }

        EnsurePlainsTerrainVisuals(terrain);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Plains Visuals",
            "Grass TerrainLayer applied. The map should now be clearly visible in Game view.",
            "OK");
    }

    /// <summary>Public entry for HeightmapTerrainImporter after Create/Re-apply.</summary>
    public static void EnsurePlainsTerrainVisualsPublic(Terrain terrain)
    {
        if (terrain != null)
            EnsurePlainsTerrainVisuals(terrain);
    }

    /// <summary>Re-bake NavMesh and re-place test armies after Terrain size/heights change.</summary>
    public static void ResyncTestScenarioAfterTerrainChange()
    {
        if (!TrySpawnTestScenario(showDialogs: false))
            throw new System.InvalidOperationException("ResyncTestScenario failed (no Terrain?).");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Fantasy4x] Test scenario re-synced after Terrain change.");
    }

    /// <summary>
    /// Terrain without TerrainLayers often looks empty/black in Game view.
    /// Adds a simple tiled grass diffuse so the plains read as a map.
    /// </summary>
    static void EnsurePlainsTerrainVisuals(Terrain terrain)
    {
        const string texPath = "Assets/Terrain/Generated/Tex_PlainsGrass.png";
        const string layerPath = "Assets/Terrain/Generated/Layer_PlainsGrass.terrainlayer";

        EnsureFolder("Assets/Terrain/Generated");

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var n = Mathf.PerlinNoise(x * 0.17f, y * 0.17f);
                    var c = Color.Lerp(
                        new Color(0.32f, 0.52f, 0.20f),
                        new Color(0.48f, 0.66f, 0.28f),
                        n);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = new TerrainLayer
            {
                diffuseTexture = diffuse,
                tileSize = new Vector2(48f, 48f)
            };
            AssetDatabase.CreateAsset(layer, layerPath);
        }
        else
        {
            layer.diffuseTexture = diffuse;
            layer.tileSize = new Vector2(48f, 48f);
            EditorUtility.SetDirty(layer);
        }

        var td = terrain.terrainData;
        td.terrainLayers = new[] { layer };

        var w = td.alphamapWidth;
        var h = td.alphamapHeight;
        var alphas = new float[h, w, 1];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            alphas[y, x, 0] = 1f;
        td.SetAlphamaps(0, 0, alphas);

        EditorUtility.SetDirty(td);
        terrain.Flush();
        AssetDatabase.SaveAssets();
        Debug.Log("[Fantasy4x] Plains TerrainLayer applied (grass diffuse).");
    }

    [MenuItem("Fantasy 4x/Gameplay/Bake NavMesh (Terrain)", false, 110)]
    public static void BakeNavMeshMenu()
    {
        if (Terrain.activeTerrains == null || Terrain.activeTerrains.Length == 0)
        {
            EditorUtility.DisplayDialog("Bake NavMesh", "No Terrain in the scene.", "OK");
            return;
        }

        BakeNavMeshForAllTerrains();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Bake NavMesh",
            $"NavMesh baked on {Terrain.activeTerrains.Length} Terrain tiles via NavMeshSurface.",
            "OK");
    }

    static void EnsureGameplayControllers(Vector3 mapCenter)
    {
        // Strip legacy capsule RTS stack so only OpenRTS drives input.
        foreach (var legacy in Object.FindObjectsOfType<ArmyMarchController>())
            Object.DestroyImmediate(legacy.gameObject);
        foreach (var cam in Object.FindObjectsOfType<Camera>())
        {
            var old = cam.GetComponent<RTSCamera>();
            if (old != null)
                Object.DestroyImmediate(old);
        }

        var camObj = Camera.main;
        if (camObj == null)
        {
            var camGo = new GameObject("Main Camera");
            camObj = camGo.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        var rts = camObj.GetComponent<OpenRtsCamera>();
        if (rts == null)
            rts = camObj.gameObject.AddComponent<OpenRtsCamera>();
        rts.maxAltitude = 2800f;
        rts.panReferenceAltitude = 500f;
        rts.zoomPanMultiplier = 10f;

        var ground = SampleTerrainPoint(mapCenter).y;
        camObj.transform.position = new Vector3(mapCenter.x, ground + 450f, mapCenter.z - 320f);
        camObj.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        camObj.farClipPlane = Mathf.Max(camObj.farClipPlane, Fantasy4xMapDesign.PrototypeMapWidthUnity * 3f);
        camObj.clearFlags = CameraClearFlags.SolidColor;
        camObj.backgroundColor = new Color(0.45f, 0.62f, 0.85f);

        var selection = Object.FindObjectOfType<OpenRtsSelection>();
        if (selection == null)
        {
            var go = new GameObject("OpenRtsSelection");
            selection = go.AddComponent<OpenRtsSelection>();
        }

        selection.rtsCamera = camObj;
    }

    static void ClearExistingTestUnits()
    {
        foreach (var u in Object.FindObjectsOfType<OpenRtsUnit>())
            Object.DestroyImmediate(u.gameObject);
        foreach (var a in Object.FindObjectsOfType<ArmyUnit>())
            Object.DestroyImmediate(a.gameObject);
        foreach (var b in Object.FindObjectsOfType<BarbarUnit>())
            Object.DestroyImmediate(b.gameObject);
    }

    static Vector3 SampleTerrainPoint(Vector3 approx)
    {
        var t = OpenRtsCamera.FindTerrainAt(approx) ?? Terrain.activeTerrain;
        if (t == null)
            return approx;
        var y = t.SampleHeight(approx) + t.transform.position.y + 0.05f;
        return new Vector3(approx.x, y, approx.z);
    }

    static void SpawnPlayerArmy(string name, Vector3 pos, Material mat)
    {
        var go = CreateCapsuleUnit(name, pos, mat);
        var agent = go.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, 8f);
        PlaceAgentOnNavMesh(agent, pos);

        var unit = go.AddComponent<OpenRtsUnit>();
        unit.isPlayerOwned = true;
        unit.health = 100f;
        unit.attack = 12f;
        unit.speed = 8f;
    }

    static void SpawnBarbarian(string name, Vector3 pos, Material mat)
    {
        var go = CreateCapsuleUnit(name, pos, mat);
        var agent = go.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, 5f);
        PlaceAgentOnNavMesh(agent, pos);
        if (agent.isOnNavMesh)
            agent.isStopped = true;

        var unit = go.AddComponent<OpenRtsUnit>();
        unit.isPlayerOwned = false;
        unit.health = 80f;
        unit.attack = 8f;
        unit.speed = 5f;
    }

    static void PlaceAgentOnNavMesh(NavMeshAgent agent, Vector3 desired)
    {
        // Disable first — enabling off-mesh logs "Failed to create agent...".
        agent.enabled = false;
        agent.transform.position = desired;

        if (NavMesh.SamplePosition(desired, out var hit, 500f, NavMesh.AllAreas))
        {
            agent.transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
            return;
        }

        for (var r = 25f; r <= 400f; r += 25f)
        {
            for (var a = 0; a < 8; a++)
            {
                var ang = a * Mathf.PI * 0.25f;
                var probe = desired + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (!NavMesh.SamplePosition(probe, out hit, 30f, NavMesh.AllAreas))
                    continue;
                agent.transform.position = hit.position;
                agent.enabled = true;
                agent.Warp(hit.position);
                return;
            }
        }

        Debug.LogWarning("[Fantasy4x] NavMesh.SamplePosition failed near " + desired);
        agent.enabled = true;
        agent.Warp(desired);
    }

    static GameObject CreateCapsuleUnit(string name, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(4f, 4f, 4f);

        var col = go.GetComponent<Collider>();
        col.isTrigger = true;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (mat != null)
            go.GetComponent<Renderer>().sharedMaterial = mat;

        return go;
    }

    static void ConfigureAgent(NavMeshAgent agent, float speed)
    {
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.baseOffset = 1f;
        agent.speed = speed;
        agent.acceleration = 40f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 1.25f;
        agent.autoBraking = true;
    }

    static void EnsureMaterials()
    {
        EnsureFolder("Assets/Scripts/Generated");

        if (AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath) == null)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var mat = new Material(shader);
            mat.color = new Color(0.2f, 0.45f, 1f);
            AssetDatabase.CreateAsset(mat, PlayerMatPath);
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(BarbMatPath) == null)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var mat = new Material(shader);
            mat.color = new Color(0.85f, 0.15f, 0.12f);
            AssetDatabase.CreateAsset(mat, BarbMatPath);
        }

        AssetDatabase.SaveAssets();
    }

    static void BakeNavMeshForAllTerrains()
    {
        Physics.SyncTransforms();
        var terrains = Terrain.activeTerrains;
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            BakeNavMeshForTerrain(terrain);
        }

        var tri = NavMesh.CalculateTriangulation();
        var totalTris = tri.indices != null ? tri.indices.Length / 3 : 0;
        Debug.Log($"[Fantasy4x] NavMesh bake complete on {terrains.Length} tiles, triangles={totalTris}");
    }

    static void BakeNavMeshForTerrain(Terrain terrain)
    {
        var col = terrain.GetComponent<TerrainCollider>();
        if (col == null)
            col = terrain.gameObject.AddComponent<TerrainCollider>();
        col.enabled = true;
        col.terrainData = terrain.terrainData;
        terrain.Flush();

        var size = terrain.terrainData.size;
        var surface = terrain.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = terrain.gameObject.AddComponent<NavMeshSurface>();

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
