#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

/// <summary>
/// Fantasy 4x → Gameplay menu: spawn test armies/barbarians and bake NavMesh for open-field marches.
/// </summary>
public static class Fantasy4xTestScenario
{
    const string PlayerMatPath = "Assets/Scripts/Generated/Mat_PlayerArmy.mat";
    const string BarbMatPath = "Assets/Scripts/Generated/Mat_Barbarian.mat";

    [MenuItem("Fantasy 4x/Gameplay/Spawn Test Scenario", false, 100)]
    public static void SpawnTestScenario()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            EditorUtility.DisplayDialog(
                "Spawn Test Scenario",
                "No Terrain found.\n\nRun Fantasy 4x → Terrain → Create Plains Terrain first.",
                "OK");
            return;
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
        Selection.activeGameObject = GameObject.Find("PlayerArmy_1");

        Debug.Log(
            "[Fantasy4x] Test scenario spawned: 3 player armies (blue) + 3 barbarians (red).\n" +
            "Play: LMB-drag select → RMB open field to march → RMB barbarian to chase/fight.\n" +
            "Camera: WASD / edge scroll / mouse wheel.");
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
            "Fallback: Window → AI → Navigation → Bake if agents still cannot path.",
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
        agent.Warp(pos);

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
        agent.isStopped = true;
        agent.Warp(pos);

        var barb = go.AddComponent<BarbarUnit>();
        barb.Health = 80f;
        barb.Attack = 8f;
        barb.Speed = 5f;
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
        agent.radius = 1.5f;
        agent.height = 4f;
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
        var surface = terrain.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = terrain.gameObject.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.All;
        // Terrain walkability comes from the Terrain collider.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.overrideVoxelSize = true;
        // Coarser voxels keep bake time sane on a ~2000² plains map.
        surface.voxelSize = 2f;
        surface.overrideTileSize = true;
        surface.tileSize = 128;
        surface.BuildNavMesh();

        Debug.Log("[Fantasy4x] NavMeshSurface.BuildNavMesh() completed on " + terrain.name);
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
