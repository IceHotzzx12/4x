#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires Unity Fantasy Kingdom sample (Asset Store 298128, educational) as the
/// outer-world playable map with OpenRTS marches. Run after the package is in My Assets.
/// </summary>
public static class FantasyKingdomOuterMapSetup
{
    public const string OuterMapScenePath = "Assets/Scenes/OuterWorldMap.unity";
    public const string LegacyWorldMapPath = "Assets/Scenes/WorldMap.unity";
    public const string AttributionPath = "Assets/ThirdParty/FantasyKingdom/ATTRIBUTION.md";

    /// <summary>
    /// Scales the dense demo kingdom so ~500u OpenRTS marches feel RoK-like
    /// (sample town is otherwise city-block scale).
    /// </summary>
    public const float EnvironmentScale = 8f;

    static readonly string[] DemoSceneCandidates =
    {
        "Assets/Scenes/Demo.unity",
        "Assets/FantasyKingdom/Scenes/Demo.unity",
        "Assets/Fantasy Kingdom/Scenes/Demo.unity",
        "Assets/ThirdParty/FantasyKingdom/Scenes/Demo.unity"
    };

    static readonly string[] SampleCameraNameHints =
    {
        "Main Camera", "PlayerCamera", "Cinemachine", "FreeLook", "FPS", "FirstPerson",
        "DemoCamera", "Orbit", "FlyCamera", "SimpleCamera"
    };

    static readonly string[] SampleCharacterNameHints =
    {
        "Player", "Character", "Hero", "NPC", "Wanderer", "FirstPerson", "FPSController",
        "Capsule", "Graphy", "StatsManager", "EventSystem"
    };

    [MenuItem("Fantasy 4x/World/Setup Fantasy Kingdom Outer Map", false, 50)]
    public static void SetupMenu()
    {
        if (!Setup(showDialogs: true))
            return;
        EditorUtility.DisplayDialog(
            "Fantasy Kingdom Outer Map",
            "Outer map ready.\n\n" +
            $"Scene: {OuterMapScenePath}\n" +
            "Play: OpenRTS select/march. Old WorldMap kept but not primary.",
            "OK");
    }

    /// <summary>
    /// Batch: Unity -batchmode -nographics -executeMethod FantasyKingdomOuterMapSetup.SetupBatch -quit
    /// </summary>
    public static void SetupBatch()
    {
        try
        {
            if (!Setup(showDialogs: false))
                throw new System.InvalidOperationException(
                    "Fantasy Kingdom Demo scene not found. Add Asset Store 298128 to My Assets and import first.");
            Debug.Log("[Fantasy4x] FantasyKingdomOuterMapSetup.SetupBatch OK → " + OuterMapScenePath);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Fantasy4x] FantasyKingdomOuterMapSetup FAILED: " + ex);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    public static bool Setup(bool showDialogs)
    {
        EnsureAttributionStub();

        var demoPath = FindDemoScenePath();
        if (string.IsNullOrEmpty(demoPath))
        {
            var msg =
                "Fantasy Kingdom Demo.unity not found.\n\n" +
                "1) Asset Store → Fantasy Kingdom in Unity 6 | URP (298128)\n" +
                "2) Click **Add to My Assets** (FREE — do not buy Synty)\n" +
                "3) Package Manager → My Assets → Download + Import\n" +
                "4) Re-run this menu.";
            if (showDialogs)
                EditorUtility.DisplayDialog("Fantasy Kingdom missing", msg, "OK");
            else
                Debug.LogError("[Fantasy4x] " + msg.Replace("\n", " "));
            return false;
        }

        Debug.Log("[Fantasy4x] Opening Fantasy Kingdom demo: " + demoPath);
        var scene = EditorSceneManager.OpenScene(demoPath, OpenSceneMode.Single);

        var root = EnsureScaleRoot();
        root.transform.localScale = Vector3.one * EnvironmentScale;

        StripSampleCamerasAndCharacters();
        WireOpenRts(root.transform.position);
        BakeNavMeshForKingdom(root);
        SpawnOpenRtsTestArmies(root.transform.position);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, OuterMapScenePath);
        AssetDatabase.Refresh();

        SetPrimaryPlayScene(OuterMapScenePath);
        EditorSceneManager.OpenScene(OuterMapScenePath, OpenSceneMode.Single);

        Debug.Log(
            $"[Fantasy4x] Outer map saved: {OuterMapScenePath} " +
            $"(scale={EnvironmentScale}, legacy meadow={LegacyWorldMapPath} demoted).");
        return true;
    }

    static void EnsureAttributionStub()
    {
        if (File.Exists(AttributionPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(AttributionPath) ?? "Assets/ThirdParty/FantasyKingdom");
        File.WriteAllText(
            AttributionPath,
            "# Fantasy Kingdom (298128) — educational / non-commercial EULA. See package license.txt after import.\n");
        AssetDatabase.Refresh();
    }

    static string FindDemoScenePath()
    {
        foreach (var p in DemoSceneCandidates)
        {
            if (File.Exists(p))
                return p;
        }

        // Prefer Demo.unity that sits near Fantasy Kingdom license.txt
        var licenseGuids = AssetDatabase.FindAssets("license t:TextAsset");
        foreach (var guid in licenseGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("license.txt", System.StringComparison.OrdinalIgnoreCase))
                continue;
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir))
                continue;
            var demo = dir + "/Scenes/Demo.unity";
            if (File.Exists(demo))
                return demo;
            // license often at Assets/license.txt → Assets/Scenes/Demo.unity
            if (dir == "Assets" && File.Exists("Assets/Scenes/Demo.unity"))
                return "Assets/Scenes/Demo.unity";
        }

        var demos = AssetDatabase.FindAssets("Demo t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith("/Demo.unity", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (demos.Count == 1)
            return demos[0];
        foreach (var d in demos)
        {
            if (d.IndexOf("Fantasy", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Kingdom", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return d;
        }

        return null;
    }

    static GameObject EnsureScaleRoot()
    {
        var existing = GameObject.Find("FantasyKingdomScaledRoot");
        if (existing != null)
            return existing;

        var root = new GameObject("FantasyKingdomScaledRoot");
        var move = new List<Transform>();
        foreach (var t in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (t == root) continue;
            // Keep lighting/global volumes as siblings; parent environment under scale root.
            var n = t.name;
            if (n == "Directional Light" || n.Contains("Global Volume") || n.Contains("Probe") ||
                n.Contains("URP") || n.Contains("Render") || n == "EventSystem")
                continue;
            move.Add(t.transform);
        }

        foreach (var t in move)
            t.SetParent(root.transform, true);

        return root;
    }

    static void StripSampleCamerasAndCharacters()
    {
        foreach (var cam in Object.FindObjectsOfType<Camera>(true))
        {
            var go = cam.gameObject;
            var name = go.name;
            var isSample = SampleCameraNameHints.Any(h =>
                name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0);
            // Remove extra cameras; we recreate Main Camera for OpenRTS.
            if (isSample || cam.CompareTag("MainCamera") || Camera.main == cam)
            {
                Object.DestroyImmediate(go);
                continue;
            }

            // Disable leftover cinematic cams
            go.SetActive(false);
        }

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            StripNamedHierarchy(root.transform);

        // Remove Graphy / sample HUD if present
        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var tn = mb.GetType().Name;
            if (tn.IndexOf("Graphy", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                tn.IndexOf("FPSCounter", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Object.DestroyImmediate(mb.gameObject);
            }
        }
    }

    static void StripNamedHierarchy(Transform t)
    {
        for (var i = t.childCount - 1; i >= 0; i--)
            StripNamedHierarchy(t.GetChild(i));

        var n = t.name;
        if (SampleCharacterNameHints.Any(h => n.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            // Keep static environment props named "PlayerHouse" etc. only if they have MeshRenderer and no CharacterController
            if (t.GetComponent<CharacterController>() != null ||
                t.GetComponent<AudioListener>() != null ||
                t.name.Equals("Player", System.StringComparison.OrdinalIgnoreCase) ||
                t.name.IndexOf("FPS", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name.IndexOf("Graphy", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }
    }

    static void WireOpenRts(Vector3 focus)
    {
        foreach (var legacy in Object.FindObjectsOfType<ArmyMarchController>())
            Object.DestroyImmediate(legacy.gameObject);

        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.tag = "MainCamera";
        camGo.AddComponent<AudioListener>();
        var rts = camGo.AddComponent<OpenRtsCamera>();
        rts.maxAltitude = 2800f;
        rts.panReferenceAltitude = 500f;
        rts.zoomPanMultiplier = 10f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, Fantasy4xMapDesign.PrototypeMapWidthUnity);
        cam.clearFlags = CameraClearFlags.Skybox;

        camGo.transform.position = focus + new Vector3(0f, 120f * EnvironmentScale, -80f * EnvironmentScale);
        camGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

        var selection = Object.FindObjectOfType<OpenRtsSelection>();
        if (selection == null)
        {
            var go = new GameObject("OpenRtsSelection");
            selection = go.AddComponent<OpenRtsSelection>();
        }

        selection.rtsCamera = cam;
    }

    static void BakeNavMeshForKingdom(GameObject root)
    {
        var surface = root.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        var tri = NavMesh.CalculateTriangulation();
        var n = tri.indices != null ? tri.indices.Length / 3 : 0;
        Debug.Log($"[Fantasy4x] Fantasy Kingdom NavMesh triangles={n}");
        if (n < 50)
        {
            // Dense kingdom may lack mesh colliders — fall back to render meshes.
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();
            tri = NavMesh.CalculateTriangulation();
            n = tri.indices != null ? tri.indices.Length / 3 : 0;
            Debug.Log($"[Fantasy4x] NavMesh fallback RenderMeshes triangles={n}");
        }
    }

    static void SpawnOpenRtsTestArmies(Vector3 center)
    {
        foreach (var u in Object.FindObjectsOfType<OpenRtsUnit>())
            Object.DestroyImmediate(u.gameObject);

        EnsureUnitMaterials(out var playerMat, out var barbMat);

        // March spacing after scale — RoK-style separation on the outer map.
        var spacing = 60f * EnvironmentScale;
        var playerOffsets = new[]
        {
            new Vector3(-spacing, 0f, -spacing * 0.5f),
            new Vector3(0f, 0f, -spacing),
            new Vector3(spacing, 0f, -spacing * 0.5f)
        };
        var barbOffsets = new[]
        {
            new Vector3(-spacing, 0f, spacing),
            new Vector3(0f, 0f, spacing * 1.4f),
            new Vector3(spacing, 0f, spacing)
        };

        for (var i = 0; i < 3; i++)
            SpawnUnit($"PlayerArmy_{i + 1}", SampleNav(center + playerOffsets[i]), playerMat, true, 8f);
        for (var i = 0; i < 3; i++)
            SpawnUnit($"Barbarian_{i + 1}", SampleNav(center + barbOffsets[i]), barbMat, false, 5f);
    }

    static Vector3 SampleNav(Vector3 desired)
    {
        if (NavMesh.SamplePosition(desired, out var hit, 200f * EnvironmentScale, NavMesh.AllAreas))
            return hit.position;
        if (NavMesh.SamplePosition(desired, out hit, 800f * EnvironmentScale, NavMesh.AllAreas))
            return hit.position;
        return desired;
    }

    static void SpawnUnit(string name, Vector3 pos, Material mat, bool playerOwned, float speed)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * (2.5f * Mathf.Max(1f, EnvironmentScale * 0.15f));
        var rend = go.GetComponent<Renderer>();
        if (rend != null && mat != null)
            rend.sharedMaterial = mat;

        var agent = go.AddComponent<NavMeshAgent>();
        agent.speed = speed * Mathf.Max(1f, EnvironmentScale * 0.25f);
        agent.angularSpeed = 360f;
        agent.acceleration = 40f;
        agent.stoppingDistance = 1.5f;
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.enabled = false;
        agent.transform.position = pos;
        if (NavMesh.SamplePosition(pos, out var hit, 200f * EnvironmentScale, NavMesh.AllAreas))
        {
            agent.transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
        }
        else
        {
            agent.enabled = true;
        }

        if (!playerOwned && agent.isOnNavMesh)
            agent.isStopped = true;

        var unit = go.AddComponent<OpenRtsUnit>();
        unit.isPlayerOwned = playerOwned;
        unit.health = playerOwned ? 100f : 80f;
        unit.attack = playerOwned ? 12f : 8f;
        unit.speed = agent.speed;
    }

    static void EnsureUnitMaterials(out Material playerMat, out Material barbMat)
    {
        const string playerPath = "Assets/Scripts/Generated/Mat_PlayerArmy.mat";
        const string barbPath = "Assets/Scripts/Generated/Mat_Barbarian.mat";
        Directory.CreateDirectory("Assets/Scripts/Generated");
        playerMat = AssetDatabase.LoadAssetAtPath<Material>(playerPath);
        if (playerMat == null)
        {
            playerMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            playerMat.color = new Color(0.2f, 0.45f, 0.95f);
            AssetDatabase.CreateAsset(playerMat, playerPath);
        }

        barbMat = AssetDatabase.LoadAssetAtPath<Material>(barbPath);
        if (barbMat == null)
        {
            barbMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            barbMat.color = new Color(0.9f, 0.25f, 0.2f);
            AssetDatabase.CreateAsset(barbMat, barbPath);
        }
    }

    static void SetPrimaryPlayScene(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(path, true)
        };
        if (File.Exists(LegacyWorldMapPath) && path != LegacyWorldMapPath)
            scenes.Add(new EditorBuildSettingsScene(LegacyWorldMapPath, false));

        EditorBuildSettings.scenes = scenes.ToArray();
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Debug.Log("[Fantasy4x] EditorBuildSettings primary scene → " + path);
    }
}
#endif
