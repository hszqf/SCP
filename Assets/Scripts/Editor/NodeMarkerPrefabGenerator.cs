// NodeMarkerPrefabGenerator - Editor script to generate NodeMarkerView.prefab
// Author: Canvas
// Version: 1.0

#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.IO;

namespace UI.Map.Editor
{
    public class NodeMarkerPrefabGenerator
    {
        private const string PrefabPath = "Assets/Prefabs/UI/Map/NodeMarkerView.prefab";

        [MenuItem("Tools/SCP/Generate NodeMarkerView Prefab")]
        public static void GeneratePrefab()
        {
            Debug.Log("[NodeMarkerPrefabGenerator] Starting prefab generation...");

            // Ensure directory exists
            string dir = Path.GetDirectoryName(PrefabPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[NodeMarkerPrefabGenerator] Created directory: {dir}");
            }

            // Create root GameObject
            GameObject root = new GameObject("NodeMarkerView");
            
            // Add RectTransform
            RectTransform rootRect = root.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(150, 150);
            
            // Add Image (transparent, for raycast)
            Image rootImage = root.AddComponent<Image>();
            rootImage.color = new Color(0, 0, 0, 0);
            rootImage.raycastTarget = true;
            
            // Add Button
            Button button = root.AddComponent<Button>();
            
            // Add NodeMarkerView script
            var markerView = root.AddComponent<UI.Map.NodeMarkerView>();

            // Create Dot
            GameObject dot = CreateDot(rootRect);
            
            // Create Name
            GameObject name = CreateName(rootRect);
            
            // Create TaskBar
            GameObject taskBar = CreateTaskBar(rootRect);
            
            // Create EventBadge
            GameObject eventBadge = CreateEventBadge(rootRect);
            
            // Create UnknownIcon
            GameObject unknownIcon = CreateUnknownIcon(rootRect);

            // Wire up references using reflection (since fields are private)
            SerializedObject so = new SerializedObject(markerView);
            so.FindProperty("dot").objectReferenceValue = dot.GetComponent<Image>();
            so.FindProperty("nameText").objectReferenceValue = name.GetComponent<Text>();
            so.FindProperty("taskBar").objectReferenceValue = taskBar.GetComponent<RectTransform>();
            so.FindProperty("avatarsContainer").objectReferenceValue = taskBar.transform.Find("Avatars");
            so.FindProperty("avatarTemplate").objectReferenceValue = taskBar.transform.Find("Avatars/AvatarTemplate").GetComponent<Image>();
            so.FindProperty("statsText").objectReferenceValue = taskBar.transform.Find("Stats").GetComponent<Text>();
            so.FindProperty("progressBg").objectReferenceValue = taskBar.transform.Find("ProgressBg").GetComponent<Image>();
            so.FindProperty("progressFill").objectReferenceValue = taskBar.transform.Find("ProgressBg/ProgressFill").GetComponent<Image>();
            so.FindProperty("eventBadge").objectReferenceValue = eventBadge;
            so.FindProperty("eventBadgeText").objectReferenceValue = eventBadge.transform.Find("Text").GetComponent<Text>();
            so.FindProperty("unknownIcon").objectReferenceValue = unknownIcon;
            so.ApplyModifiedProperties();

            // Save as prefab
            bool success;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
            
            if (success)
            {
                Debug.Log($"[NodeMarkerPrefabGenerator] Successfully created prefab at: {PrefabPath}");
            }
            else
            {
                Debug.LogError($"[NodeMarkerPrefabGenerator] Failed to create prefab at: {PrefabPath}");
            }

            // Clean up temporary GameObject
            Object.DestroyImmediate(root);
            
            AssetDatabase.Refresh();
        }

        private static GameObject CreateDot(Transform parent)
        {
            GameObject dot = new GameObject("Dot");
            dot.transform.SetParent(parent, false);
            
            RectTransform rect = dot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(40, 40);
            rect.anchoredPosition = Vector2.zero;
            
            Image image = dot.AddComponent<Image>();
            image.color = new Color(0.3f, 0.7f, 1f, 1f);
            image.raycastTarget = false;
            
            return dot;
        }

        private static GameObject CreateName(Transform parent)
        {
            GameObject name = new GameObject("Name");
            name.transform.SetParent(parent, false);
            
            RectTransform rect = name.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = new Vector2(0, -35);
            
            Text text = name.AddComponent<Text>();
            text.text = "Node";
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.raycastTarget = false;
            
            return name;
        }

        private static GameObject CreateTaskBar(Transform parent)
        {
            GameObject taskBar = new GameObject("TaskBar");
            taskBar.transform.SetParent(parent, false);
            
            RectTransform rect = taskBar.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.sizeDelta = new Vector2(140, 60);
            rect.anchoredPosition = new Vector2(0, -75);
            
            // Create Avatars container
            GameObject avatars = new GameObject("Avatars");
            avatars.transform.SetParent(taskBar.transform, false);
            
            RectTransform avatarsRect = avatars.AddComponent<RectTransform>();
            avatarsRect.anchorMin = new Vector2(0, 1);
            avatarsRect.anchorMax = new Vector2(1, 1);
            avatarsRect.sizeDelta = new Vector2(0, 20);
            avatarsRect.anchoredPosition = new Vector2(0, -10);
            
            HorizontalLayoutGroup layout = avatars.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 5f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            
            // Create AvatarTemplate
            GameObject avatarTemplate = new GameObject("AvatarTemplate");
            avatarTemplate.transform.SetParent(avatars.transform, false);
            
            RectTransform avatarRect = avatarTemplate.AddComponent<RectTransform>();
            avatarRect.sizeDelta = new Vector2(20, 20);
            
            Image avatarImage = avatarTemplate.AddComponent<Image>();
            avatarImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            avatarImage.raycastTarget = false;
            
            avatarTemplate.SetActive(false); // Template should be inactive
            
            // Create Stats text
            GameObject stats = new GameObject("Stats");
            stats.transform.SetParent(taskBar.transform, false);
            
            RectTransform statsRect = stats.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0, 0.5f);
            statsRect.anchorMax = new Vector2(1, 0.5f);
            statsRect.sizeDelta = new Vector2(0, 15);
            statsRect.anchoredPosition = new Vector2(0, 0);
            
            Text statsText = stats.AddComponent<Text>();
            statsText.text = "HP - | SAN -";
            statsText.color = Color.white;
            statsText.alignment = TextAnchor.MiddleCenter;
            statsText.fontSize = 10;
            statsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statsText.raycastTarget = false;
            
            // Create ProgressBg
            GameObject progressBg = new GameObject("ProgressBg");
            progressBg.transform.SetParent(taskBar.transform, false);
            
            RectTransform progressBgRect = progressBg.AddComponent<RectTransform>();
            progressBgRect.anchorMin = new Vector2(0, 0);
            progressBgRect.anchorMax = new Vector2(1, 0);
            progressBgRect.sizeDelta = new Vector2(0, 8);
            progressBgRect.anchoredPosition = new Vector2(0, 8);
            
            Image progressBgImage = progressBg.AddComponent<Image>();
            progressBgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            progressBgImage.raycastTarget = false;
            
            // Create ProgressFill
            GameObject progressFill = new GameObject("ProgressFill");
            progressFill.transform.SetParent(progressBg.transform, false);
            
            RectTransform progressFillRect = progressFill.AddComponent<RectTransform>();
            progressFillRect.anchorMin = new Vector2(0, 0);
            progressFillRect.anchorMax = new Vector2(0, 1);
            progressFillRect.pivot = new Vector2(0, 0.5f);
            progressFillRect.sizeDelta = new Vector2(100, 0);
            progressFillRect.anchoredPosition = Vector2.zero;
            
            Image progressFillImage = progressFill.AddComponent<Image>();
            progressFillImage.color = new Color(0.3f, 1f, 0.3f, 1f);
            progressFillImage.type = Image.Type.Filled;
            progressFillImage.fillMethod = Image.FillMethod.Horizontal;
            progressFillImage.fillAmount = 0.5f;
            progressFillImage.raycastTarget = false;
            
            return taskBar;
        }

        private static GameObject CreateEventBadge(Transform parent)
        {
            GameObject eventBadge = new GameObject("EventBadge");
            eventBadge.transform.SetParent(parent, false);
            
            RectTransform rect = eventBadge.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.sizeDelta = new Vector2(20, 20);
            rect.anchoredPosition = new Vector2(5, 5);
            
            Image image = eventBadge.AddComponent<Image>();
            image.color = new Color(1f, 0.5f, 0f, 1f);
            image.raycastTarget = false;
            
            // Create Text child
            GameObject text = new GameObject("Text");
            text.transform.SetParent(eventBadge.transform, false);
            
            RectTransform textRect = text.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;
            
            Text textComponent = text.AddComponent<Text>();
            textComponent.text = "!";
            textComponent.color = Color.white;
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.fontSize = 14;
            textComponent.fontStyle = FontStyle.Bold;
            textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComponent.raycastTarget = false;
            
            eventBadge.SetActive(false); // Hidden by default
            
            return eventBadge;
        }

        private static GameObject CreateUnknownIcon(Transform parent)
        {
            GameObject unknownIcon = new GameObject("UnknownIcon");
            unknownIcon.transform.SetParent(parent, false);
            
            RectTransform rect = unknownIcon.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(30, 30);
            rect.anchoredPosition = new Vector2(0, 15);
            
            Text text = unknownIcon.AddComponent<Text>();
            text.text = "?";
            text.color = Color.yellow;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.raycastTarget = false;
            
            unknownIcon.SetActive(false); // Hidden by default
            
            return unknownIcon;
        }
    }
}
#endif
