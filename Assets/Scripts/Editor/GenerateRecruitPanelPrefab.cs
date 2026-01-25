using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GenerateRecruitPanelPrefab
{
    private const string PrefabPath = "Assets/Prefabs/UI/RecruitPanel.prefab";

    [MenuItem("Tools/SCP/Generate RecruitPanel Prefab")]
    public static void Generate()
    {
        var root = BuildRecruitPanelHierarchy();
        try
        {
            var prefab = SavePrefab(root, PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[UI] Failed to save RecruitPanel prefab.");
                return;
            }

            Debug.Log($"[UI] Generated RecruitPanel prefab at {PrefabPath}");
            WireUIPanelRoot(prefab);
        }
        finally
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static GameObject BuildRecruitPanelHierarchy()
    {
        var fontAsset = FindDefaultFontAsset();

        var root = new GameObject("RecruitPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(RecruitPanel));
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        rootRT.pivot = new Vector2(0.5f, 0.5f);

        var canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        var mask = CreateUIObject("Mask", root.transform, typeof(Image));
        var maskRT = mask.GetComponent<RectTransform>();
        StretchRectTransform(maskRT);
        var maskImage = mask.GetComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.6f);
        maskImage.raycastTarget = true;

        var window = CreateUIObject("Window", root.transform, typeof(Image), typeof(VerticalLayoutGroup));
        var windowRT = window.GetComponent<RectTransform>();
        windowRT.anchorMin = new Vector2(0.5f, 0.5f);
        windowRT.anchorMax = new Vector2(0.5f, 0.5f);
        windowRT.pivot = new Vector2(0.5f, 0.5f);
        windowRT.sizeDelta = new Vector2(560f, 360f);
        windowRT.anchoredPosition = Vector2.zero;
        var windowImage = window.GetComponent<Image>();
        windowImage.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        windowImage.raycastTarget = true;

        var windowLayout = window.GetComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(24, 24, 20, 20);
        windowLayout.spacing = 12f;
        windowLayout.childControlHeight = true;
        windowLayout.childControlWidth = true;
        windowLayout.childForceExpandHeight = false;
        windowLayout.childForceExpandWidth = true;

        var header = CreateUIObject("Header", window.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.childAlignment = TextAnchor.MiddleCenter;
        headerLayout.childForceExpandHeight = true;
        headerLayout.childForceExpandWidth = true;
        var headerLayoutElement = header.GetComponent<LayoutElement>();
        headerLayoutElement.preferredHeight = 60f;

        var title = CreateText("Title", header.transform, fontAsset, 36f, TextAlignmentOptions.Center);

        var body = CreateUIObject("Body", window.transform, typeof(VerticalLayoutGroup), typeof(LayoutElement));
        var bodyLayout = body.GetComponent<VerticalLayoutGroup>();
        bodyLayout.padding = new RectOffset(8, 8, 8, 8);
        bodyLayout.spacing = 10f;
        bodyLayout.childControlHeight = true;
        bodyLayout.childControlWidth = true;
        bodyLayout.childForceExpandHeight = false;
        bodyLayout.childForceExpandWidth = true;
        var bodyLayoutElement = body.GetComponent<LayoutElement>();
        bodyLayoutElement.flexibleHeight = 1f;

        var rowMoney = CreateRow(body.transform, "Row_Money", "Money", fontAsset, out var moneyValue);
        var rowCost = CreateRow(body.transform, "Row_Cost", "Hire Cost", fontAsset, out var costValue);

        var rowHint = CreateUIObject("Row_Hint", body.transform, typeof(LayoutElement));
        var rowHintLayout = rowHint.GetComponent<LayoutElement>();
        rowHintLayout.preferredHeight = 32f;
        var hint = CreateText("Hint", rowHint.transform, fontAsset, 22f, TextAlignmentOptions.Center);
        hint.text = "Hire adds 1 Agent.";

        var footer = CreateUIObject("Footer", window.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 16f;
        footerLayout.childForceExpandHeight = true;
        footerLayout.childForceExpandWidth = true;
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        var footerLayoutElement = footer.GetComponent<LayoutElement>();
        footerLayoutElement.preferredHeight = 72f;

        var cancelButton = CreateButton("Btn_Close", footer.transform, fontAsset, "Close", out var cancelLabel);
        var confirmButton = CreateButton("Btn_Hire", footer.transform, fontAsset, "Hire", out var confirmLabel);

        BindSerializedFields(root, title, moneyValue, costValue, hint, confirmButton, confirmLabel, cancelButton, cancelLabel);

        return root;
    }

    private static void BindSerializedFields(GameObject root, TMP_Text titleText, TMP_Text moneyText, TMP_Text costText, TMP_Text statusText,
        Button confirmButton, TMP_Text confirmLabel, Button cancelButton, TMP_Text cancelLabel)
    {
        var recruitPanel = root.GetComponent<RecruitPanel>();
        if (recruitPanel == null) return;

        var serialized = new SerializedObject(recruitPanel);
        SetProperty(serialized, "titleText", titleText);
        SetProperty(serialized, "moneyText", moneyText);
        SetProperty(serialized, "costText", costText);
        SetProperty(serialized, "statusText", statusText);
        SetProperty(serialized, "confirmButton", confirmButton);
        SetProperty(serialized, "confirmLabel", confirmLabel);
        SetProperty(serialized, "cancelButton", cancelButton);
        SetProperty(serialized, "cancelLabel", cancelLabel);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetProperty(SerializedObject serialized, string propertyName, UnityEngine.Object value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"[UI] RecruitPanel missing serialized field '{propertyName}'.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static GameObject SavePrefab(GameObject root, string path)
    {
        EnsureFolderExists("Assets/Prefabs");
        EnsureFolderExists("Assets/Prefabs/UI");

        return PrefabUtility.SaveAsPrefabAsset(root, path);
    }

    private static void WireUIPanelRoot(GameObject prefab)
    {
        var panelRoot = UnityEngine.Object.FindObjectOfType<UIPanelRoot>();
        if (panelRoot == null)
        {
            Debug.LogWarning("[UI] Unable to wire UIPanelRoot: no UIPanelRoot found in the active scene.");
            return;
        }

        var serialized = new SerializedObject(panelRoot);
        var field = serialized.FindProperty("recruitPanelPrefab");
        if (field == null)
        {
            Debug.LogWarning("[UI] Unable to wire UIPanelRoot: field 'recruitPanelPrefab' not found.");
            return;
        }

        field.objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[UI] Wired UIPanelRoot.recruitPanelPrefab = RecruitPanel (sceneOnly=true)");
    }

    private static TMP_FontAsset FindDefaultFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

        var samplePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ConfirmDialog.prefab");
        if (samplePrefab != null)
        {
            var tmp = samplePrefab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null && tmp.font != null) return tmp.font;
        }

        return null;
    }

    private static GameObject CreateUIObject(string name, Transform parent, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.localScale = Vector3.one;
        return go;
    }

    private static void StretchRectTransform(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset fontAsset, float size, TextAlignmentOptions alignment)
    {
        var go = CreateUIObject(name, parent, typeof(TextMeshProUGUI));
        var rect = go.GetComponent<RectTransform>();
        StretchRectTransform(rect);

        var text = go.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) text.font = fontAsset;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = name;
        return text;
    }

    private static GameObject CreateRow(Transform parent, string name, string label, TMP_FontAsset fontAsset, out TMP_Text valueText)
    {
        var row = CreateUIObject(name, parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;
        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = 30f;

        var labelText = CreateText("Label", row.transform, fontAsset, 24f, TextAlignmentOptions.Left);
        labelText.text = label;

        valueText = CreateText("Value", row.transform, fontAsset, 24f, TextAlignmentOptions.Right);
        valueText.text = "0";

        return row;
    }

    private static Button CreateButton(string name, Transform parent, TMP_FontAsset fontAsset, string label, out TMP_Text labelText)
    {
        var buttonGO = CreateUIObject(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
        var layout = buttonGO.GetComponent<LayoutElement>();
        layout.preferredWidth = 200f;
        layout.flexibleWidth = 1f;

        var image = buttonGO.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        labelText = CreateText("Label", buttonGO.transform, fontAsset, 26f, TextAlignmentOptions.Center);
        labelText.text = label;
        labelText.raycastTarget = false;

        return buttonGO.GetComponent<Button>();
    }

    private static void EnsureFolderExists(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        var parent = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
