using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class Unity6UrpVerify
{
    public static void Run()
    {
        var code = 0;
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;

            var pipe = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP_High_Pipeline.asset");
            if (pipe == null)
            {
                Debug.LogError("[Fantasy4x] Missing URP_High_Pipeline");
                code = 1;
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = pipe;
                for (var i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipe;
                }
                var ultra = System.Array.IndexOf(QualitySettings.names, "Ultra");
                if (ultra < 0) ultra = QualitySettings.names.Length - 1;
                QualitySettings.SetQualityLevel(ultra, true);
                QualitySettings.renderPipeline = pipe;

                Debug.Log("[Fantasy4x] Verify OK: " + pipe.name + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + " assigned=" + (GraphicsSettings.defaultRenderPipeline == pipe));
                if (GraphicsSettings.defaultRenderPipeline != pipe) code = 1;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            code = 1;
            Debug.LogError("[Fantasy4x] Verify failed: " + ex);
        }
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }
}