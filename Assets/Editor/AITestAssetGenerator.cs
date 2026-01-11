// Assets/Editor/AITestAssetGenerator.cs
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class AITestAssetGenerator
{
    private const string PrefabPath = "Assets/Prefabs/AI_Test.prefab";
    private const string SceneAPath  = "Assets/Scenes/AI_Test_A.unity";
    private const string SceneBPath  = "Assets/Scenes/AI_Test_B.unity";

    [MenuItem("Tools/AIBridge/Generate AI_Test Assets")]
    public static void Generate()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Scenes");

        // 若存在则询问是否覆盖
        if (!ConfirmOverwriteIfExists(PrefabPath, "Prefab") ||
            !ConfirmOverwriteIfExists(SceneAPath, "SceneA") ||
            !ConfirmOverwriteIfExists(SceneBPath, "SceneB"))
        {
            Debug.Log("[AI_Test] Cancelled by user.");
            return;
        }

        GeneratePrefab();
        GenerateScene(SceneAPath, includeBOnly: false);
        GenerateScene(SceneBPath, includeBOnly: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AI_Test] Generated:\n" +
                  $"- {PrefabPath}\n" +
                  $"- {SceneAPath}\n" +
                  $"- {SceneBPath}");
    }

    private static void GeneratePrefab()
    {
        // 清理旧资源
        if (File.Exists(PrefabPath))
            AssetDatabase.DeleteAsset(PrefabPath);

        // Root
        var root = new GameObject("AI_Test", typeof(RectTransform));
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(600, 400);

        // Child Button
        var btnGO = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(root.transform, worldPositionStays: false);

        var btnRt = btnGO.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(200, 80);
        btnRt.anchoredPosition = Vector2.zero;

        // Image / Button 默认值
        var img = btnGO.GetComponent<Image>();
        img.raycastTarget = true;

        var btn = btnGO.GetComponent<Button>();
        btn.interactable = true;

        // 保存 prefab
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

        // 清理临时对象
        Object.DestroyImmediate(root);
    }

    private static void GenerateScene(string scenePath, bool includeBOnly)
    {
        // 清理旧 scene 资源
        if (File.Exists(scenePath))
            AssetDatabase.DeleteAsset(scenePath);

        // 新建空场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Canvas 根节点（名字必须是 "Canvas"）
        var canvasGO = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Panel（路径必须是 Scene:/Canvas/Panel）
        var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        panelGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);

        var panelRt = panelGO.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(800, 500);
        panelRt.anchoredPosition = Vector2.zero;

        var panelCg = panelGO.GetComponent<CanvasGroup>();
        panelCg.alpha = 1f;
        panelCg.interactable = true;
        panelCg.blocksRaycasts = true;

        // EventSystem（避免 UI 警告；如果已有则不创建）
        EnsureEventSystem();

        // SceneB 额外对象
        if (includeBOnly)
        {
            var bOnly = new GameObject("BOnly");
            bOnly.transform.position = Vector3.zero;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null)
            return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        es.transform.position = Vector3.zero;
    }

    private static void EnsureFolder(string assetFolder)
    {
        // assetFolder: "Assets/Prefabs" / "Assets/Scenes"
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        var parent = Path.GetDirectoryName(assetFolder)?.Replace("\\", "/");
        var leaf = Path.GetFileName(assetFolder);

        if (string.IsNullOrEmpty(parent) || parent == "Assets")
        {
            AssetDatabase.CreateFolder("Assets", leaf);
        }
        else
        {
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    private static bool ConfirmOverwriteIfExists(string assetPath, string label)
    {
        // 注意：这里用 File.Exists 检查相对路径，大多数 Unity 项目工作目录为项目根，通常有效。
        // 如果你更严格，可以改为 Path.Combine(projectRoot, assetPath) 再检查。
        if (!File.Exists(assetPath))
            return true;

        return EditorUtility.DisplayDialog(
            $"Overwrite {label}?",
            $"{assetPath} already exists.\n\nDo you want to overwrite it?",
            "Overwrite",
            "Cancel"
        );
    }
}
#endif
