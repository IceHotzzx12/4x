#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Fantasy 4x -> Gameplay menu: spawn test armies/barbarians and bake NavMesh for open-field marches.
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
            "[Fantasy4x] Test scenario spawned: 3 player armies (blue) + 3 barbarians (red).\n" +
            "Play: LMB-drag select -> RMB open field to march -> RMB barbarian to chase/fight.\n" +
            "Camera: WASD / edge scroll / mouse wheel.");
    }

    /// <summary>
    /// Headless entry: Unity -batchmode -nographics -executeMethod Fantasy4xTestScenario.SpawnTestScenarioBatch -quit
    /// Opens WorldMap, spawns armies/barbarians, bakes NavMesh, saves scene.
    /// </summary>
    public static void SpawnTestScenarioBatch()
    {
        try
        {
            if (!System.IO.File.Exists(WorldMapScenePath))
            {
                throw new System.InvalidOperationException(
                    "WorldMap scene missing. Run Fantasy 4x -> Terrain -> Create Plains Terrain first.");
            }

            EditorSceneManager.OpenScene(WorldMapScenePath, OpenSceneMode.Single);

            if (!TrySpawnTestScenario(showDialogs: false))
                throw new System.InvalidOperationException("SpawnTestScenario failed (no Terrain?).");

            var tri = NavMesh.CalculateTriangulation();
            var triCount = tri.indices != null ? tri.indices.Length / 3 : 0;

            // isOnNavMesh is unreliable in edit/batchmode; SamplePosition reflects bake coverage.
            var armiesOk = 0;
            var armyTotal = 0;
            foreach (var a in Object.FindObjectsOfType<ArmyUnit>())
            {
                armyTotal++;
                if (NavMesh.SamplePosition(a.transform.position, out _, 5f, NavMesh.AllAreas))
                    armiesOk++;
            }

            var barbsOk = 0;
            var barbTotal = 0;
            foreach (var b in Object.FindObjectsOfType<BarbarUnit>())
            {
                barbTotal++;
                if (NavMesh.SamplePosition(b.transform.position, out _, 5f, NavMesh.AllAreas))
                    barbsOk++;
            }

            var hasMarch = Object.FindObjectOfType<ArmyMarchController>() != null;
            var hasCam = Object.FindObjectOfType<RTSCamera>() != null;

            Debug.Log(
                $"[Fantasy4x] NavMesh triangles={triCount}, army samples ok={armiesOk}/{armyTotal}, " +
                $"barbarian samples ok={barbsOk}/{barbTotal}, ArmyMarchController={hasMarch}, RTSCamera={hasCam}");

            if (triCount < 100 || armiesOk < 3 || barbsOk < 3 || !hasMarch || !hasCam)
                throw new System.InvalidOperationException(
                    $"Playable setup incomplete: triangles={triCount}, armiesOk={armiesOk}, barbsOk={barbsOk}, march={hasMarch}, cam={hasCam}");

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
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog(
                    "Spawn Test Scenario",
                    "No Terrain found.\n\nRun Fantasy 4x -> Terrain -> Create Plains Terrain first.",
                    "OK");
            }
            else
            {
                Debug.LogError("[Fantasy4x] No Terrain found. Create Plains Terrain first.");
            }

            return false;
        }

        EnsureGameplayControllers(terrain);
        EnsureMaterials();
        ClearExistingTestUnits();

        // Bake walkable surface before warping agents onto it.
        BakeNavMeshForTerrain(terrain);

        var size = terrain.terrainData.size;
        var origin = terrain.transform.position;
        var center = origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);

        var playerOffsets = new[]
        {
            new Vector3(-40f, 0f, -30f),
            new Vector3(0f, 0f, -45f),
            new Vector3(40f, 0f, -30f)
        };
        var barbOffsets = new[]
        {
            new Vector3(-35f, 0f, 55f),
            new Vector3(0f, 0f, 70f),
            new Vector3(35f, 0f, 55f)
        };

        var playerMat = AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath);
        var barbMat = AssetDatabase.LoadAssetAtPath<Material>(BarbMatPath);

        for (var i = 0; i < 3; i++)
            SpawnPlayerArmy($"PlayerArmy_{i + 1}", SampleTerrainPoint(terrain, center + playerOffsets[i]), playerMat);

        for (var i = 0; i < 3; i++)
            SpawnBarbarian($"Barbarian_{i + 1}", SampleTerrainPoint(terrain, center + barbOffsets[i]), barbMat);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return true;
    }

    [MenuItem("Fantasy 4x/Gameplay/Bake NavMesh (Terrain)", false, 110)]
    public static void BakeNavMeshMenu()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Bake NavMesh", "No Terrain in the scene.", "OK");
            return;
        }

        BakeNavMeshForTerrain(terrain);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Bake NavMesh",
            "NavMesh baked via NavMeshSurface on the active Terrain.\n\n" +
            "Fallback: Window -> AI -> Navigation -> Bake if agents still cannot path.",
            "OK");
    }

    static void EnsureGameplayControllers(Terrain terrain)
    {
        var march = Object.FindObjectOfType<ArmyMarchController>();
        if (march == null)
        {
            var go = new GameObject("ArmyMarchController");
            march = go.AddComponent<ArmyMarchController>();
        }

        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        var rts = cam.GetComponent<RTSCamera>();
        if (rts == null)
            rts = cam.gameObject.AddComponent<RTSCamera>();
        rts.terrain = terrain;

        var size = terrain.terrainData.size;
        var center = terrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        var ground = terrain.SampleHeight(center) + terrain.transform.position.y;
        cam.transform.position = new Vector3(center.x, ground + 180f, center.z - 120f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, size.x * 3f);

        march.rtsCamera = cam;
    }

    static void ClearExistingTestUnits()
    {
        foreach (var a in Object.FindObjectsOfType<ArmyUnit>())
            Object.DestroyImmediate(a.gameObject);
        foreach (var b in Object.FindObjectsOfType<BarbarUnit>())
            Object.DestroyImmediate(b.gameObject);
    }

    static Vector3 SampleTerrainPoint(Terrain terrain, Vector3 approx)
    {
        var y = terrain.SampleHeight(approx) + terrain.transform.position.y + 0.05f;
        return new Vector3(approx.x, y, approx.z);
    }

    static void SpawnPlayerArmy(string name, Vector3 pos, Material mat)
    {
        var go = CreateCapsuleUnit(name, pos, mat);
        var agent = go.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, 8f);
        PlaceAgentOnNavMesh(agent, pos);

        var army = go.AddComponent<ArmyUnit>();
        army.Health = 100f;
        army.Attack = 12f;
        army.Speed = 8f;
    }

    static void SpawnBarbarian(string name, Vector3 pos, Material mat)
    {
        var go = CreateCapsuleUnit(name, pos, mat);
        var agent = go.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, 5f);
        PlaceAgentOnNavMesh(agent, pos);
        if (agent.isOnNavMesh)
            agent.isStopped = true;

        var barb = go.AddComponent<BarbarUnit>();
        barb.Health = 80f;
        barb.Attack = 8f;
        barb.Speed = 5f;
    }

    static void PlaceAgentOnNavMesh(NavMeshAgent agent, Vector3 desired)
    {
        // Edit/batchmode: set transform explicitly — Warp alone may not move the GameObject.
        if (NavMesh.SamplePosition(desired, out var hit, 200f, NavMesh.AllAreas))
        {
            agent.transform.position = hit.position;
            agent.Warp(hit.position);
            return;
        }

        agent.transform.position = desired;
        agent.Warp(desired);
        Debug.LogWarning("[Fantasy4x] NavMesh.SamplePosition failed near " + desired);
    }

    static GameObject CreateCapsuleUnit(string name, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(4f, 4f, 4f);

        // CapsuleCollider from CreatePrimitive — keep as trigger for combat.
        var col = go.GetComponent<Collider>();
        col.isTrigger = true;

        // Required so OnTriggerEnter fires while NavMeshAgent moves the transform.
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
        // Keep agent dimensions aligned with default NavMesh surface bake (Humanoid).
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.baseOffset = 1f; // capsule visual is ~4 units tall (scale 4)
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

    static void BakeNavMeshForTerrain(Terrain terrain)
    {
        var col = terrain.GetComponent<TerrainCollider>();
        if (col == null)
            col = terrain.gameObject.AddComponent<TerrainCollider>();
        col.enabled = true;
        col.terrainData = terrain.terrainData;

        // Force heightmap into physics before collect.
        terrain.Flush();
        Physics.SyncTransforms();

        var size = terrain.terrainData.size;
        var origin = terrain.transform.position;
        var centerWorld = origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        var hCenter = terrain.SampleHeight(centerWorld) + origin.y;
        var hCorner = terrain.SampleHeight(origin) + origin.y;
        Debug.Log($"[Fantasy4x] Terrain heights: center={hCenter:F2}, corner0={hCorner:F2}, size={size}");

        var surface = terrain.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = terrain.gameObject.AddComponent<NavMeshSurface>();

        // Explicit volume covering the whole Terrain.
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        surface.size = new Vector3(size.x + 10f, Mathf.Max(size.y * 2f, 120f), size.z + 10f);
        // PhysicsColliders is the Terrain-supported path for heightfields.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.defaultArea = 0;
        surface.minRegionArea = 1f;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 1f;
        surface.overrideTileSize = true;
        surface.tileSize = 64;
        surface.BuildNavMesh();

        var tri = NavMesh.CalculateTriangulation();
        var triCount = tri.indices != null ? tri.indices.Length / 3 : 0;
        var b = new Bounds();
        if (tri.vertices != null && tri.vertices.Length > 0)
        {
            b = new Bounds(tri.vertices[0], Vector3.zero);
            for (var i = 1; i < tri.vertices.Length; i++)
                b.Encapsulate(tri.vertices[i]);
        }

        Debug.Log(
            $"[Fantasy4x] NavMeshSurface.BuildNavMesh() on {terrain.name}: " +
            $"triangles={triCount}, bounds={b.min} .. {b.max}, terrainSize={size}");

        // If bake ignored heightfield (flat strip far from terrain surface), fall back to a denser pass.
        if (triCount < 1000 || Mathf.Abs(b.center.y - hCenter) > 20f)
        {
            Debug.LogWarning(
                "[Fantasy4x] NavMesh bake looks wrong vs terrain height — retrying with voxelSize=0.5");
            surface.voxelSize = 0.5f;
            surface.BuildNavMesh();
            tri = NavMesh.CalculateTriangulation();
            triCount = tri.indices != null ? tri.indices.Length / 3 : 0;
            if (tri.vertices != null && tri.vertices.Length > 0)
            {
                b = new Bounds(tri.vertices[0], Vector3.zero);
                for (var i = 1; i < tri.vertices.Length; i++)
                    b.Encapsulate(tri.vertices[i]);
            }

            Debug.Log(
                $"[Fantasy4x] NavMesh retry: triangles={triCount}, bounds={b.min} .. {b.max}");
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
