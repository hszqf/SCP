// Runtime bootstrap for SimpleWorldMapPanel
// Auto-creates the map UI at runtime if not present in scene
// Author: Canvas (Auto-setup for WebGL builds)
// Version: 1.0

using UnityEngine;
using UnityEngine.UI;
using UI.Map;

/// <summary>
/// Automatically instantiates SimpleWorldMapPanel at runtime if missing.
/// Uses RuntimeInitializeOnLoadMethod to run before scene loads.
/// </summary>
public static class SimpleWorldMapBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        Debug.Log("[MapBootstrap] SimpleWorldMapBootstrap starting...");

        // Check if SimpleWorldMapPanel already exists
        var existing = Object.FindAnyObjectByType<SimpleWorldMapPanel>();
        if (existing != null)
        {
            Debug.Log("[MapBootstrap] SimpleWorldMapPanel already exists, skipping creation");
            return;
        }

        // Create a MonoBehaviour to run the coroutine
        var bootstrapObj = new GameObject("SimpleWorldMapBootstrap_Runner");
        var runner = bootstrapObj.AddComponent<BootstrapRunner>();
        Object.DontDestroyOnLoad(bootstrapObj);
    }

    private class BootstrapRunner : MonoBehaviour
    {
        private void Start()
        {
            StartCoroutine(InitializeWhenReady());
        }

        private System.Collections.IEnumerator InitializeWhenReady()
        {
            Debug.Log("[MapUI] MapBootstrap waiting for GameController initialization...");

            // Wait until GameController is ready with nodes
            yield return new WaitUntil(() => 
                GameController.I != null && 
                GameController.I.State != null && 
                GameController.I.State.Nodes != null && 
                GameController.I.State.Nodes.Count > 0
            );

            int nodeCount = GameController.I.State.Nodes.Count;
            Debug.Log($"[MapUI] MapBootstrap nodesReady count={nodeCount}");

            // Find Canvas to attach to
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[MapUI] MapBootstrap ERROR: Cannot find Canvas to attach SimpleWorldMapPanel");
                yield break;
            }

            Debug.Log("[MapUI] MapBootstrap creating SimpleWorldMapPanel programmatically...");

            // Disable old NewMapRuntime system if present
            var oldMapBootstrap = GameObject.Find("MapBootstrap");
            if (oldMapBootstrap != null)
            {
                var newMapRuntime = oldMapBootstrap.GetComponent<UI.Map.NewMapRuntime>();
                if (newMapRuntime != null)
                {
                    Debug.Log("[MapUI] MapBootstrap disabling old NewMapRuntime system");
                    oldMapBootstrap.SetActive(false);
                }
            }

            // Create SimpleWorldMapPanel
            var panelObj = CreateSimpleWorldMapPanel(canvas.transform);
            if (panelObj != null)
            {
                Debug.Log("[MapUI] MapBootstrap ✅ SimpleWorldMapPanel created successfully");
                
                // Trigger initial spawn and refresh
                var mapPanel = panelObj.GetComponent<SimpleWorldMapPanel>();
                if (mapPanel != null)
                {
                    // The Start() method will be called by Unity, but we can log the node spawning
                    Debug.Log($"[MapUI] MapBootstrap will spawn markers for {nodeCount} nodes");
                }
            }
            else
            {
                Debug.LogError("[MapUI] MapBootstrap ❌ Failed to create SimpleWorldMapPanel");
            }

            // Destroy the runner after initialization
            Destroy(gameObject);
        }
    }

    private static GameObject CreateSimpleWorldMapPanel(Transform canvasTransform)
    {
        // Create root panel
        GameObject panelObj = new GameObject("SimpleWorldMapPanel");
        panelObj.transform.SetParent(canvasTransform, false);

        RectTransform panelRT = panelObj.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // Add background image
        Image bgImage = panelObj.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.15f, 1f);
        bgImage.raycastTarget = false; // Don't block clicks

        // Create map container
        GameObject containerObj = new GameObject("MapContainer");
        containerObj.transform.SetParent(panelObj.transform, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 0.5f);
        containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.pivot = new Vector2(0.5f, 0.5f);
        containerRT.sizeDelta = new Vector2(1200, 800);
        containerRT.anchoredPosition = Vector2.zero;

        // Add SimpleWorldMapPanel component
        SimpleWorldMapPanel mapPanel = panelObj.AddComponent<SimpleWorldMapPanel>();
        
        // Use reflection to set private fields since they're SerializeField
        var type = typeof(SimpleWorldMapPanel);
        var mapContainerField = type.GetField("mapContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var backgroundImageField = type.GetField("backgroundImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nodeMarkerPrefabField = type.GetField("nodeMarkerPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var hqMarkerPrefabField = type.GetField("hqMarkerPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (mapContainerField != null)
            mapContainerField.SetValue(mapPanel, containerRT);
        if (backgroundImageField != null)
            backgroundImageField.SetValue(mapPanel, bgImage);

        // Create prefabs programmatically and assign them
        GameObject nodeMarkerPrefab = CreateNodeMarkerPrefab();
        GameObject hqMarkerPrefab = CreateHQMarkerPrefab();

        if (nodeMarkerPrefabField != null)
            nodeMarkerPrefabField.SetValue(mapPanel, nodeMarkerPrefab);
        if (hqMarkerPrefabField != null)
            hqMarkerPrefabField.SetValue(mapPanel, hqMarkerPrefab);

        Debug.Log("[MapBootstrap] SimpleWorldMapPanel component configured");

        return panelObj;
    }

    private static GameObject CreateNodeMarkerPrefab()
    {
        GameObject prefab = new GameObject("NodeMarkerPrefab");
        
        RectTransform rootRT = prefab.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(120, 150);

        // Add NodeMarkerView component
        NodeMarkerView markerView = prefab.AddComponent<NodeMarkerView>();
        
        // Add Button component
        Button button = prefab.AddComponent<Button>();

        // Create visual elements
        // 1. Dot (circle)
        GameObject dotObj = new GameObject("Dot");
        dotObj.transform.SetParent(prefab.transform, false);
        Image dotImage = dotObj.AddComponent<Image>();
        dotImage.color = Color.white;
        RectTransform dotRT = dotObj.GetComponent<RectTransform>();
        dotRT.sizeDelta = new Vector2(60, 60);
        dotRT.anchoredPosition = new Vector2(0, 40);

        button.targetGraphic = dotImage;

        // 2. Name text
        GameObject nameTextObj = new GameObject("NameText");
        nameTextObj.transform.SetParent(prefab.transform, false);
        Text nameText = nameTextObj.AddComponent<Text>();
        nameText.text = "Node";
        nameText.fontSize = 16;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.color = Color.white;
        nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        RectTransform nameTextRT = nameTextObj.GetComponent<RectTransform>();
        nameTextRT.sizeDelta = new Vector2(100, 30);
        nameTextRT.anchoredPosition = new Vector2(0, 5);

        // 3. Task bar container
        GameObject taskBarObj = new GameObject("TaskBar");
        taskBarObj.transform.SetParent(prefab.transform, false);
        RectTransform taskBarRT = taskBarObj.AddComponent<RectTransform>();
        taskBarRT.sizeDelta = new Vector2(250, 60);
        taskBarRT.anchoredPosition = new Vector2(0, -45);
        taskBarObj.SetActive(false); // Hidden by default

        // Task bar background
        Image taskBarBg = taskBarObj.AddComponent<Image>();
        taskBarBg.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

        // Avatars container
        GameObject avatarsContainer = new GameObject("AvatarsContainer");
        avatarsContainer.transform.SetParent(taskBarObj.transform, false);
        RectTransform avatarsRT = avatarsContainer.AddComponent<RectTransform>();
        avatarsRT.anchorMin = new Vector2(0, 0);
        avatarsRT.anchorMax = new Vector2(0.5f, 1);
        avatarsRT.offsetMin = Vector2.zero;
        avatarsRT.offsetMax = Vector2.zero;
        
        HorizontalLayoutGroup avatarsLayout = avatarsContainer.AddComponent<HorizontalLayoutGroup>();
        avatarsLayout.spacing = 5;
        avatarsLayout.padding = new RectOffset(5, 5, 5, 5);
        avatarsLayout.childAlignment = TextAnchor.MiddleLeft;

        // Avatar template
        GameObject avatarTemplateObj = new GameObject("AvatarTemplate");
        avatarTemplateObj.transform.SetParent(avatarsContainer.transform, false);
        Image avatarTemplate = avatarTemplateObj.AddComponent<Image>();
        avatarTemplate.color = new Color(0.8f, 0.8f, 0.8f);
        RectTransform avatarTemplateRT = avatarTemplateObj.GetComponent<RectTransform>();
        avatarTemplateRT.sizeDelta = new Vector2(30, 30);
        avatarTemplateObj.SetActive(false); // Template is inactive

        // Stats text
        GameObject statsTextObj = new GameObject("StatsText");
        statsTextObj.transform.SetParent(taskBarObj.transform, false);
        Text statsText = statsTextObj.AddComponent<Text>();
        statsText.text = "HP 100 | SAN 100";
        statsText.fontSize = 10;
        statsText.alignment = TextAnchor.MiddleLeft;
        statsText.color = Color.white;
        statsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        RectTransform statsTextRT = statsTextObj.GetComponent<RectTransform>();
        statsTextRT.anchorMin = new Vector2(0.5f, 0.5f);
        statsTextRT.anchorMax = new Vector2(0.5f, 0.5f);
        statsTextRT.sizeDelta = new Vector2(110, 20);
        statsTextRT.anchoredPosition = new Vector2(55, 12);

        // Progress bar background
        GameObject progressBgObj = new GameObject("ProgressBg");
        progressBgObj.transform.SetParent(taskBarObj.transform, false);
        Image progressBg = progressBgObj.AddComponent<Image>();
        progressBg.color = new Color(0.3f, 0.3f, 0.35f);
        RectTransform progressBgRT = progressBgObj.GetComponent<RectTransform>();
        progressBgRT.anchorMin = new Vector2(0.5f, 0);
        progressBgRT.anchorMax = new Vector2(1, 0.4f);
        progressBgRT.offsetMin = Vector2.zero;
        progressBgRT.offsetMax = new Vector2(-5, 0);

        // Progress bar fill
        GameObject progressFillObj = new GameObject("ProgressFill");
        progressFillObj.transform.SetParent(progressBgObj.transform, false);
        Image progressFill = progressFillObj.AddComponent<Image>();
        progressFill.color = new Color(0.3f, 0.7f, 1f);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = 0.5f;
        RectTransform progressFillRT = progressFillObj.GetComponent<RectTransform>();
        progressFillRT.anchorMin = Vector2.zero;
        progressFillRT.anchorMax = Vector2.one;
        progressFillRT.offsetMin = Vector2.zero;
        progressFillRT.offsetMax = Vector2.zero;

        // 4. Event badge
        GameObject eventBadgeObj = new GameObject("EventBadge");
        eventBadgeObj.transform.SetParent(prefab.transform, false);
        Image eventBadgeImage = eventBadgeObj.AddComponent<Image>();
        eventBadgeImage.color = Color.red;
        RectTransform eventBadgeRT = eventBadgeObj.GetComponent<RectTransform>();
        eventBadgeRT.sizeDelta = new Vector2(25, 25);
        eventBadgeRT.anchoredPosition = new Vector2(30, 60);
        eventBadgeObj.SetActive(false);

        // Event badge text
        GameObject eventBadgeTextObj = new GameObject("EventBadgeText");
        eventBadgeTextObj.transform.SetParent(eventBadgeObj.transform, false);
        Text eventBadgeText = eventBadgeTextObj.AddComponent<Text>();
        eventBadgeText.text = "1";
        eventBadgeText.fontSize = 14;
        eventBadgeText.alignment = TextAnchor.MiddleCenter;
        eventBadgeText.color = Color.white;
        eventBadgeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        eventBadgeText.fontStyle = FontStyle.Bold;
        RectTransform eventBadgeTextRT = eventBadgeTextObj.GetComponent<RectTransform>();
        eventBadgeTextRT.anchorMin = Vector2.zero;
        eventBadgeTextRT.anchorMax = Vector2.one;
        eventBadgeTextRT.offsetMin = Vector2.zero;
        eventBadgeTextRT.offsetMax = Vector2.zero;

        // 5. Unknown icon
        GameObject unknownIconObj = new GameObject("UnknownIcon");
        unknownIconObj.transform.SetParent(prefab.transform, false);
        Text unknownIconText = unknownIconObj.AddComponent<Text>();
        unknownIconText.text = "?";
        unknownIconText.fontSize = 24;
        unknownIconText.alignment = TextAnchor.MiddleCenter;
        unknownIconText.color = Color.yellow;
        unknownIconText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        unknownIconText.fontStyle = FontStyle.Bold;
        RectTransform unknownIconRT = unknownIconObj.GetComponent<RectTransform>();
        unknownIconRT.sizeDelta = new Vector2(30, 30);
        unknownIconRT.anchoredPosition = new Vector2(-30, 60);
        unknownIconObj.SetActive(false);

        // Wire up NodeMarkerView using reflection
        var type = typeof(NodeMarkerView);
        SetPrivateField(type, markerView, "dot", dotImage);
        SetPrivateField(type, markerView, "nameText", nameText);
        SetPrivateField(type, markerView, "taskBar", taskBarRT);
        SetPrivateField(type, markerView, "avatarsContainer", avatarsContainer.transform);
        SetPrivateField(type, markerView, "avatarTemplate", avatarTemplate);
        SetPrivateField(type, markerView, "statsText", statsText);
        SetPrivateField(type, markerView, "progressBg", progressBg);
        SetPrivateField(type, markerView, "progressFill", progressFill);
        SetPrivateField(type, markerView, "eventBadge", eventBadgeObj);
        SetPrivateField(type, markerView, "eventBadgeText", eventBadgeText);
        SetPrivateField(type, markerView, "unknownIcon", unknownIconObj);

        Debug.Log("[MapBootstrap] NodeMarker prefab created");
        return prefab;
    }

    private static GameObject CreateHQMarkerPrefab()
    {
        GameObject prefab = new GameObject("HQMarkerPrefab");

        RectTransform rootRT = prefab.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(80, 80);

        // Circle
        GameObject circleObj = new GameObject("Circle");
        circleObj.transform.SetParent(prefab.transform, false);
        Image circleImage = circleObj.AddComponent<Image>();
        circleImage.color = new Color(0.3f, 0.7f, 1f);
        RectTransform circleRT = circleObj.GetComponent<RectTransform>();
        circleRT.sizeDelta = new Vector2(80, 80);
        circleRT.anchoredPosition = Vector2.zero;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(prefab.transform, false);
        Text text = textObj.AddComponent<Text>();
        text.text = "HQ";
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontStyle = FontStyle.Bold;
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        Debug.Log("[MapBootstrap] HQMarker prefab created");
        return prefab;
    }

    private static void SetPrivateField(System.Type type, object instance, string fieldName, object value)
    {
        var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(instance, value);
        }
        else
        {
            Debug.LogWarning($"[MapBootstrap] Field '{fieldName}' not found on type '{type.Name}'");
        }
    }
}
