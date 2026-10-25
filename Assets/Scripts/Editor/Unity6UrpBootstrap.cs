using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Batchmode-safe Unity 6 URP setup: high-res shadows, Graphics/Quality assignment,
/// and conversion of Built-in materials (terrain decor + river water) to URP Lit.
/// </summary>
public static class Unity6UrpBootstrap
{
    const string SettingsFolder = "Assets/Settings";
    const string RendererPath = "Assets/Settings/URP_High_Renderer.asset";
    const string PipelinePath = "Assets/Settings/URP_High_Pipeline.asset";

    [MenuItem("Fantasy4x/Unity6/Setup URP High Graphics")]
    public static void RunFromMenu() => Run();

    /// <summary>Entry for: Unity.exe -batchmode -executeMethod Unity6UrpBootstrap.Run</summary>
    public static void Run()
    {
        var exitCode = 0;
        try
        {
            EnsureFolder("Assets");
            EnsureFolder(SettingsFolder);

            var renderer = EnsureRenderer();
            var pipeline = EnsurePipeline(renderer);
            ApplyHighShadowSettings(pipeline);
            AssignGraphicsAndQuality(pipeline);
            ConvertProjectMaterials();
            EnsureTerrainUsesUrp();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fantasy4x] Unity6 URP bootstrap complete: " + PipelinePath);
        }
        catch (System.Exception ex)
        {
            exitCode = 1;
            Debug.LogError("[Fantasy4x] Unity6 URP bootstrap failed: " + ex);
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode != 0)
            throw new System.InvalidOperationException("Unity6 URP bootstrap failed - see Console.");
    }

    static UniversalRendererData EnsureRenderer()
    {
        var existing = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (existing != null)
            return existing;

        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(renderer, RendererPath);
        return renderer;
    }

    static UniversalRenderPipelineAsset EnsurePipeline(UniversalRendererData renderer)
    {
        var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (existing != null)
        {
            BindRenderer(existing, renderer);
            return existing;
        }

        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        AssetDatabase.CreateAsset(pipeline, PipelinePath);
        BindRenderer(pipeline, renderer);
        EditorUtility.SetDirty(pipeline);
        return pipeline;
    }

    static void BindRenderer(UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer)
    {
        var so = new SerializedObject(pipeline);
        var list = so.FindProperty("m_RendererDataList");
        if (list != null)
        {
            if (list.arraySize < 1)
                list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            var idx = so.FindProperty("m_DefaultRendererIndex");
            if (idx != null)
                idx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void ApplyHighShadowSettings(UniversalRenderPipelineAsset pipeline)
    {
        var so = new SerializedObject(pipeline);

        SetBool(so, "m_MainLightShadowsSupported", true);
        SetBool(so, "m_AdditionalLightShadowsSupported", true);
        SetInt(so, "m_MainLightShadowmapResolution", 4096);
        SetInt(so, "m_AdditionalLightsShadowmapResolution", 4096);
        SetInt(so, "m_ShadowCascadeCount", 4);
        SetFloat(so, "m_ShadowDistance", 500f);
        SetBool(so, "m_SoftShadowsSupported", true);
        SetInt(so, "m_AdditionalLightsRenderingMode", 1);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
        Debug.Log("[Fantasy4x] URP high-res shadows: main/additional 4096, 4 cascades, distance 500.");
    }

    static void AssignGraphicsAndQuality(UniversalRenderPipelineAsset pipeline)
    {
        GraphicsSettings.defaultRenderPipeline = pipeline;

        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
            QualitySettings.renderPipeline = pipeline;
        }

        var ultra = System.Array.IndexOf(QualitySettings.names, "Ultra");
        if (ultra < 0) ultra = QualitySettings.names.Length - 1;
        QualitySettings.SetQualityLevel(ultra, true);
        QualitySettings.renderPipeline = pipeline;

        EditorUtility.SetDirty(pipeline);
        Debug.Log($"[Fantasy4x] Graphics/Quality -> URP_High_Pipeline (level {QualitySettings.names[QualitySettings.GetQualityLevel()]}).");
    }

    static void ConvertProjectMaterials()
    {
        var guids = AssetDatabase.FindAssets("t:Material", new[]
        {
            "Assets/FantasyEnvironment",
            "Assets/Scripts/Generated",
            "Assets/Terrain"
        });

        var lit = FindUrpLit();
        if (lit == null)
        {
            Debug.LogError("[Fantasy4x] URP Lit shader not found - is com.unity.render-pipelines.universal installed?");
            return;
        }

        var converted = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            var name = Path.GetFileNameWithoutExtension(path);
            var isWater = name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0
                          || name.IndexOf("River", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isWater)
                ConvertToUrpTransparentLit(mat, lit);
            else
                ConvertToUrpLit(mat, lit);

            EditorUtility.SetDirty(mat);
            converted++;
        }

        Debug.Log($"[Fantasy4x] Converted {converted} materials to URP Lit (water -> transparent).");
    }

    static void ConvertToUrpLit(Material mat, Shader lit)
    {
        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
        var smooth = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
            : (mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.5f);
        Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

        mat.shader = lit;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
        if (mainTex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (bump != null && mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", bump);

        SetOpaqueSurface(mat);
    }

    static void ConvertToUrpTransparentLit(Material mat, Shader lit)
    {
        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : new Color(0.12f, 0.42f, 0.78f, 0.65f);
        if (color.a >= 0.99f) color.a = 0.65f;
        var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0.15f;
        var smooth = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.92f;

        mat.shader = lit;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);

        SetTransparentSurface(mat);
    }

    public static Material EnsureUrpWaterMaterial(string assetPath, Color color)
    {
        EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
        var lit = FindUrpLit();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            mat = new Material(lit != null ? lit : Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, assetPath);
        }

        if (lit != null)
            mat.shader = lit;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);
        SetTransparentSurface(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Shader FindUrpLit()
    {
        return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("URP/Lit");
    }

    static void SetOpaqueSurface(Material mat)
    {
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Geometry;
    }

    static void SetTransparentSurface(Material mat)
    {
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    static void EnsureTerrainUsesUrp()
    {
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var terrainLit = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        foreach (var t in terrains)
        {
            if (t == null) continue;
            if (terrainLit != null)
            {
                var mat = new Material(terrainLit);
                mat.name = t.name + "_TerrainLit";
                t.materialTemplate = mat;
                EditorUtility.SetDirty(t);
            }
        }

        Debug.Log($"[Fantasy4x] Terrain URP refresh: {terrains.Length} scene terrains.");
    }

    static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            return;
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

    static void SetBool(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p != null && p.propertyType == SerializedPropertyType.Boolean)
            p.boolValue = value;
    }

    static void SetInt(SerializedObject so, string name, int value)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Integer)
            p.intValue = value;
        else if (p.propertyType == SerializedPropertyType.Boolean)
            p.boolValue = value != 0;
        else if (p.propertyType == SerializedPropertyType.Enum)
            p.enumValueIndex = value;
    }

    static void SetFloat(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p != null && p.propertyType == SerializedPropertyType.Float)
            p.floatValue = value;
    }
}
