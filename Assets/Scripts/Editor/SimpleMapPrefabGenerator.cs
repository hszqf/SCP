// Prefab generator for Simple World Map UI
// This Editor script helps generate the required prefabs programmatically
// Author: Canvas
// Version: 1.0

#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using UI.Map;
using System.IO;

namespace Editor
{
    public partial class SimpleMapPrefabGenerator : EditorWindow
    {
        [MenuItem("Tools/SCP/Generate Simple Map Prefabs")]
        public static void ShowWindow()
        {
            GetWindow<SimpleMapPrefabGenerator>("Map Prefab Generator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Simple World Map Prefab Generator", EditorStyles.boldLabel);
            GUILayout.Space(10);

            if (GUILayout.Button("Generate All Prefabs", GUILayout.Height(40)))
            {
                GenerateAllPrefabs();
            }

            GUILayout.Space(10);
            GUILayout.Label("Individual Prefabs:", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Generate NodeMarker Prefab"))
            {
                GenerateNodeMarkerPrefab();
            }

            if (GUILayout.Button("2. Generate HQMarker Prefab"))
            {
                GenerateHQMarkerPrefab();
            }

            if (GUILayout.Button("3. Generate TaskBar Prefab"))
            {
                GenerateTaskBarPrefab();
            }

            if (GUILayout.Button("4. Generate AgentAvatar Prefab"))
            {
                GenerateAgentAvatarPrefab();
            }

            if (GUILayout.Button("5. Generate AnomalyPin Prefab"))
            {
                GenerateAnomalyPinPrefab();
            }

            if (GUILayout.Button("6. Generate SimpleWorldMapPanel Prefab"))
            {
                GenerateSimpleWorldMapPanelPrefab();
            }
        }

        private static void GenerateAllPrefabs()
        {
            Debug.Log("[PrefabGen] Generating all prefabs...");
            
            GenerateAgentAvatarPrefab();
            GenerateTaskBarPrefab();
            GenerateAnomalyPinPrefab();
            GenerateHQMarkerPrefab();
            GenerateNodeMarkerPrefab();
            GenerateSimpleWorldMapPanelPrefab();

            Debug.Log("[PrefabGen] All prefabs generated successfully!");
            EditorUtility.DisplayDialog("Success", "All prefabs generated successfully!\n\nCheck Assets/Prefabs/UI/Map/", "OK");
        }

        private static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private static void GenerateAgentAvatarPrefab()
        {
            Debug.Log("[PrefabGen] Generating AgentAvatar prefab...");

            GameObject root = new GameObject("AgentAvatar");
            
            // Avatar image
            GameObject avatarObj = new GameObject("Avatar");
            avatarObj.transform.SetParent(root.transform);
            Image avatarImage = avatarObj.AddComponent<Image>();
            avatarImage.color = new Color(0.8f, 0.8f, 0.8f);
            RectTransform avatarRT = avatarObj.GetComponent<RectTransform>();
            avatarRT.sizeDelta = new Vector2(30, 30);
            avatarRT.anchoredPosition = Vector2.zero;

            // HP text
            GameObject hpObj = new GameObject("HP");
            hpObj.transform.SetParent(root.transform);
            TextMeshProUGUI hpText = hpObj.AddComponent<TextMeshProUGUI>();
            hpText.text = "HP 10";
            hpText.fontSize = 10;
            hpText.alignment = TextAlignmentOptions.Center;
            RectTransform hpRT = hpObj.GetComponent<RectTransform>();
            hpRT.sizeDelta = new Vector2(40, 15);
            hpRT.anchoredPosition = new Vector2(0, -20);

            // SAN text
            GameObject sanObj = new GameObject("SAN");
            sanObj.transform.SetParent(root.transform);
            TextMeshProUGUI sanText = sanObj.AddComponent<TextMeshProUGUI>();
            sanText.text = "SAN 10";
            sanText.fontSize = 10;
            sanText.alignment = TextAlignmentOptions.Center;
            RectTransform sanRT = sanObj.GetComponent<RectTransform>();
            sanRT.sizeDelta = new Vector2(40, 15);
            sanRT.anchoredPosition = new Vector2(0, -35);

            SavePrefab(root, "Assets/Prefabs/UI/Map/AgentAvatar.prefab");
        }

        private static void GenerateTaskBarPrefab()
        {
            Debug.Log("[PrefabGen] Generating TaskBar prefab...");

            GameObject root = new GameObject("TaskBar");
            TaskBarView taskBarView = root.AddComponent<TaskBarView>();
            
            // Background
            Image bgImage = root.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            RectTransform rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(250, 50);

            // Agent avatars container
            GameObject avatarsContainer = new GameObject("AgentAvatarsContainer");
            avatarsContainer.transform.SetParent(root.transform);
            HorizontalLayoutGroup hlg = avatarsContainer.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 5;
            hlg.padding = new RectOffset(5, 5, 5, 5);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            RectTransform avatarsRT = avatarsContainer.GetComponent<RectTransform>();
            avatarsRT.anchorMin = new Vector2(0, 0);
            avatarsRT.anchorMax = new Vector2(0.6f, 1);
            avatarsRT.offsetMin = Vector2.zero;
            avatarsRT.offsetMax = Vector2.zero;

            // Progress bar
            GameObject progressBarObj = new GameObject("ProgressBar");
            progressBarObj.transform.SetParent(root.transform);
            Slider progressBar = progressBarObj.AddComponent<Slider>();
            Image progressBgImage = progressBarObj.AddComponent<Image>();
            progressBgImage.color = new Color(0.3f, 0.3f, 0.35f);
            RectTransform progressRT = progressBarObj.GetComponent<RectTransform>();
            progressRT.anchorMin = new Vector2(0.65f, 0.3f);
            progressRT.anchorMax = new Vector2(0.95f, 0.7f);
            progressRT.offsetMin = Vector2.zero;
            progressRT.offsetMax = Vector2.zero;

            // Progress fill
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(progressBarObj.transform);
            RectTransform fillAreaRT = fillArea.GetComponent<RectTransform>();
            fillAreaRT.anchorMin = Vector2.zero;
            fillAreaRT.anchorMax = Vector2.one;
            fillAreaRT.offsetMin = Vector2.zero;
            fillAreaRT.offsetMax = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.7f, 1f);
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            progressBar.fillRect = fillRT;
            progressBar.minValue = 0;
            progressBar.maxValue = 1;

            // Status text
            GameObject statusTextObj = new GameObject("StatusText");
            statusTextObj.transform.SetParent(root.transform);
            TextMeshProUGUI statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
            statusText.text = "调查 0%";
            statusText.fontSize = 12;
            statusText.alignment = TextAlignmentOptions.TopRight;
            RectTransform statusRT = statusTextObj.GetComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0.65f, 0);
            statusRT.anchorMax = new Vector2(1, 0.25f);
            statusRT.offsetMin = Vector2.zero;
            statusRT.offsetMax = Vector2.zero;

            // Wire up TaskBarView
            SerializedObject so = new SerializedObject(taskBarView);
            so.FindProperty("agentAvatarsContainer").objectReferenceValue = avatarsContainer.transform;
            so.FindProperty("progressBar").objectReferenceValue = progressBar;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.ApplyModifiedProperties();

            SavePrefab(root, "Assets/Prefabs/UI/Map/TaskBar.prefab");
        }

        private static void GenerateAnomalyPinPrefab()
        {
            Debug.Log("[PrefabGen] Generating AnomalyPin prefab...");

            GameObject root = new GameObject("AnomalyPin");
            AnomalyPinView pinView = root.AddComponent<AnomalyPinView>();
            Button button = root.AddComponent<Button>();

            // Icon background
            GameObject iconImageObj = new GameObject("IconImage");
            iconImageObj.transform.SetParent(root.transform);
            Image iconImage = iconImageObj.AddComponent<Image>();
            iconImage.color = new Color(1f, 1f, 0f, 0.8f);
            RectTransform iconImageRT = iconImageObj.GetComponent<RectTransform>();
            iconImageRT.sizeDelta = new Vector2(40, 40);
            iconImageRT.anchoredPosition = Vector2.zero;

            // Icon text
            GameObject iconTextObj = new GameObject("IconText");
            iconTextObj.transform.SetParent(root.transform);
            TextMeshProUGUI iconText = iconTextObj.AddComponent<TextMeshProUGUI>();
            iconText.text = "?";
            iconText.fontSize = 30;
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.color = Color.black;
            RectTransform iconTextRT = iconTextObj.GetComponent<RectTransform>();
            iconTextRT.anchorMin = Vector2.zero;
            iconTextRT.anchorMax = Vector2.one;
            iconTextRT.offsetMin = Vector2.zero;
            iconTextRT.offsetMax = Vector2.zero;

            button.targetGraphic = iconImage;

            // Wire up AnomalyPinView
            SerializedObject so = new SerializedObject(pinView);
            so.FindProperty("iconText").objectReferenceValue = iconText;
            so.FindProperty("iconImage").objectReferenceValue = iconImage;
            so.ApplyModifiedProperties();

            SavePrefab(root, "Assets/Prefabs/UI/Map/AnomalyPin.prefab");
        }

        private static void GenerateHQMarkerPrefab()
        {
            Debug.Log("[PrefabGen] Generating HQMarker prefab...");

            GameObject root = new GameObject("HQMarker");

            // Circle
            GameObject circleObj = new GameObject("Circle");
            circleObj.transform.SetParent(root.transform);
            Image circleImage = circleObj.AddComponent<Image>();
            circleImage.color = new Color(0.3f, 0.7f, 1f);
            RectTransform circleRT = circleObj.GetComponent<RectTransform>();
            circleRT.sizeDelta = new Vector2(80, 80);
            circleRT.anchoredPosition = Vector2.zero;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform);
            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = "HQ";
            text.fontSize = 24;
            text.alignment = TextAlignmentOptions.Center;
            text.fontWeight = FontWeight.Bold;
            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            SavePrefab(root, "Assets/Prefabs/UI/Map/HQMarker.prefab");
        }

        private static void GenerateNodeMarkerPrefab()
        {
            Debug.Log("[PrefabGen] Generating NodeMarker prefab...");

            GameObject root = new GameObject("NodeMarker");
            NodeMarkerView markerView = root.AddComponent<NodeMarkerView>();
            Button button = root.AddComponent<Button>();

            RectTransform rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(120, 150);

            // Circle
            GameObject circleObj = new GameObject("Circle");
            circleObj.transform.SetParent(root.transform);
            Image circleImage = circleObj.AddComponent<Image>();
            circleImage.color = Color.white;
            RectTransform circleRT = circleObj.GetComponent<RectTransform>();
            circleRT.sizeDelta = new Vector2(60, 60);
            circleRT.anchoredPosition = new Vector2(0, 40);

            // Name text
            GameObject nameTextObj = new GameObject("NameText");
            nameTextObj.transform.SetParent(root.transform);
            TextMeshProUGUI nameText = nameTextObj.AddComponent<TextMeshProUGUI>();
            nameText.text = "N1";
            nameText.fontSize = 16;
            nameText.alignment = TextAlignmentOptions.Center;
            RectTransform nameTextRT = nameTextObj.GetComponent<RectTransform>();
            nameTextRT.sizeDelta = new Vector2(100, 30);
            nameTextRT.anchoredPosition = new Vector2(0, 5);

            // Task bar container
            GameObject taskBarContainer = new GameObject("TaskBarContainer");
            taskBarContainer.transform.SetParent(root.transform);
            RectTransform taskBarRT = taskBarContainer.GetComponent<RectTransform>();
            taskBarRT.sizeDelta = new Vector2(250, 50);
            taskBarRT.anchoredPosition = new Vector2(0, -40);
            taskBarContainer.SetActive(false);

            // Attention badge
            GameObject badgeObj = new GameObject("AttentionBadge");
            badgeObj.transform.SetParent(root.transform);
            Image badgeImage = badgeObj.AddComponent<Image>();
            badgeImage.color = Color.red;
            RectTransform badgeRT = badgeObj.GetComponent<RectTransform>();
            badgeRT.sizeDelta = new Vector2(20, 20);
            badgeRT.anchoredPosition = new Vector2(30, 60);
            badgeObj.SetActive(false);

            // Anomaly pins container
            GameObject pinsContainer = new GameObject("AnomalyPinsContainer");
            pinsContainer.transform.SetParent(root.transform);
            RectTransform pinsRT = pinsContainer.GetComponent<RectTransform>();
            pinsRT.anchoredPosition = Vector2.zero;
            pinsRT.sizeDelta = new Vector2(200, 200);

            button.targetGraphic = circleImage;

            // Wire up NodeMarkerView
            SerializedObject so = new SerializedObject(markerView);
            so.FindProperty("nodeNameText").objectReferenceValue = nameText;
            so.FindProperty("nodeCircle").objectReferenceValue = circleImage;
            so.FindProperty("taskBarContainer").objectReferenceValue = taskBarContainer;
            so.FindProperty("attentionBadge").objectReferenceValue = badgeObj;
            so.FindProperty("anomalyPinsContainer").objectReferenceValue = pinsContainer.transform;
            so.ApplyModifiedProperties();

            SavePrefab(root, "Assets/Prefabs/UI/Map/NodeMarker.prefab");
        }

        private static void GenerateSimpleWorldMapPanelPrefab()
        {
            Debug.Log("[PrefabGen] Generating SimpleWorldMapPanel prefab...");

            GameObject root = new GameObject("SimpleWorldMapPanel");
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            SimpleWorldMapPanel mapPanel = root.AddComponent<SimpleWorldMapPanel>();

            // Background
            Image bgImage = root.AddComponent<Image>();
            bgImage.color = new Color(0.1f, 0.1f, 0.15f, 1f);

            // Map container
            GameObject container = new GameObject("MapContainer");
            container.transform.SetParent(root.transform);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0.5f, 0.5f);
            containerRT.sizeDelta = new Vector2(1200, 800);
            containerRT.anchoredPosition = Vector2.zero;

            // Wire up SimpleWorldMapPanel
            SerializedObject so = new SerializedObject(mapPanel);
            so.FindProperty("mapContainer").objectReferenceValue = containerRT;
            so.FindProperty("backgroundImage").objectReferenceValue = bgImage;
            so.ApplyModifiedProperties();

            SavePrefab(root, "Assets/Prefabs/UI/Map/SimpleWorldMapPanel.prefab");

            Debug.Log("[PrefabGen] Remember to assign NodeMarker and HQMarker prefabs to SimpleWorldMapPanel!");
        }

        private static void SavePrefab(GameObject obj, string path)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            
            PrefabUtility.SaveAsPrefabAsset(obj, path);
            DestroyImmediate(obj);
            
            Debug.Log($"[PrefabGen] Saved prefab: {path}");
            AssetDatabase.Refresh();
        }
    }
}
#endif
