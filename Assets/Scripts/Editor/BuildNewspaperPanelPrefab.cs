using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

public static class BuildNewspaperPanelPrefab
{
    private const string DefaultPrefabPath = "Assets/Prefabs/UI/NewspaperPanel.prefab";

    [MenuItem("Tools/AI/Build NewspaperPanel (3 Papers)")]
    public static void Build()
    {
        string path = FindPrefabPath();
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("NewspaperPanel Prefab Not Found",
                "Could not find NewspaperPanel.prefab via search or default path. Please create the prefab first.",
                "OK");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Load Failed", "Failed to load prefab contents.", "OK");
            return;
        }

        try
        {
            if (root.transform.childCount > 0)
            {
                bool rebuild = EditorUtility.DisplayDialog(
                    "Rebuild NewspaperPanel",
                    "Prefab already has children. Clear and rebuild the UI hierarchy?",
                    "Rebuild",
                    "Cancel");

                if (!rebuild)
                {
                    return;
                }

                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                }
            }

            BuildHierarchy(root);

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static string FindPrefabPath()
    {
        string[] guids = AssetDatabase.FindAssets("NewspaperPanel t:Prefab");
        if (guids != null && guids.Length > 0)
        {
            string guidPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (!string.IsNullOrEmpty(guidPath))
            {
                return guidPath;
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
        return prefab != null ? DefaultPrefabPath : string.Empty;
    }

    private static void BuildHierarchy(GameObject root)
    {
        RectTransform rootRect = EnsureRectTransform(root);
        SetStretch(rootRect);

        GameObject header = CreateUIObject("Header", root.transform, addImage: true);
        RectTransform headerRect = header.GetComponent<RectTransform>();
        SetTopFixed(headerRect, 80f, 0f);

        GameObject paperTabs = CreateUIObject("PaperTabs", root.transform, addImage: true);
        RectTransform tabsRect = paperTabs.GetComponent<RectTransform>();
        SetTopFixed(tabsRect, 50f, -80f);

        GameObject paperPages = CreateUIObject("PaperPages", root.transform, addImage: true);
        RectTransform pagesRect = paperPages.GetComponent<RectTransform>();
        SetStretchWithTopOffset(pagesRect, 130f);

        BuildHeader(header.transform);
        BuildTabs(paperTabs.transform, root);

        GameObject paper1 = BuildPaperPage(paperPages.transform, "Paper1", "A");
        GameObject paper2 = BuildPaperPage(paperPages.transform, "Paper2", "B");
        GameObject paper3 = BuildPaperPage(paperPages.transform, "Paper3", "C");

        paper1.SetActive(true);
        paper2.SetActive(false);
        paper3.SetActive(false);

        AttachSwitcher(root, paper1, paper2, paper3);
    }

    private static void BuildHeader(Transform header)
    {
        GameObject title = CreateTMP("TitleTMP", header, "基金会晨报", 30, TextAlignmentOptions.Left);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        SetLeftCenter(titleRect, 20f, 200f, 40f);

        GameObject day = CreateTMP("DayTMP", header, "Day 1", 24, TextAlignmentOptions.Right);
        RectTransform dayRect = day.GetComponent<RectTransform>();
        SetRightCenter(dayRect, 20f, 120f, 40f);

        GameObject closeBtn = CreateButton("CloseBtn", header, "X");
        RectTransform closeRect = closeBtn.GetComponent<RectTransform>();
        SetRightCenter(closeRect, 20f, 60f, 40f);
    }

    private static void BuildTabs(Transform tabs, GameObject root)
    {
        GameObject tab1 = CreateButton("Tab_Paper1", tabs, "A报");
        RectTransform t1 = tab1.GetComponent<RectTransform>();
        SetLeftCenter(t1, 20f, 120f, 36f);

        GameObject tab2 = CreateButton("Tab_Paper2", tabs, "B报");
        RectTransform t2 = tab2.GetComponent<RectTransform>();
        SetLeftCenter(t2, 160f, 120f, 36f);

        GameObject tab3 = CreateButton("Tab_Paper3", tabs, "C报");
        RectTransform t3 = tab3.GetComponent<RectTransform>();
        SetLeftCenter(t3, 300f, 120f, 36f);

        NewspaperPanelSwitcher switcher = root.GetComponent<NewspaperPanelSwitcher>();
        if (switcher == null)
        {
            switcher = root.AddComponent<NewspaperPanelSwitcher>();
        }

        BindTab(tab1.GetComponent<Button>(), switcher, 0);
        BindTab(tab2.GetComponent<Button>(), switcher, 1);
        BindTab(tab3.GetComponent<Button>(), switcher, 2);
    }

    private static GameObject BuildPaperPage(Transform pages, string name, string tag)
    {
        GameObject page = CreateUIObject(name, pages, addImage: true);
        RectTransform pageRect = page.GetComponent<RectTransform>();
        SetStretch(pageRect);

        GameObject slotHeadline = CreateUIObject("Slot_Headline", page.transform, addImage: true);
        RectTransform slotHeadlineRect = slotHeadline.GetComponent<RectTransform>();
        SetTopBlock(slotHeadlineRect, 0f, 160f);
        CreateTMP("HeadlineTitleTMP", slotHeadline.transform, "占位 头条标题" + tag, 28, TextAlignmentOptions.Left);
        CreateTMP("HeadlineDeckTMP", slotHeadline.transform, "占位 导语" + tag, 20, TextAlignmentOptions.Left);

        GameObject slotA = CreateUIObject("Slot_BlockA", page.transform, addImage: true);
        RectTransform slotARect = slotA.GetComponent<RectTransform>();
        SetTopBlock(slotARect, -170f, 140f);
        CreateTMP("BlockATitleTMP", slotA.transform, "占位 A-标题" + tag, 24, TextAlignmentOptions.Left);
        CreateTMP("BlockABodyTMP", slotA.transform, "占位 A-正文" + tag, 18, TextAlignmentOptions.Left);

        GameObject slotB = CreateUIObject("Slot_BlockB", page.transform, addImage: true);
        RectTransform slotBRect = slotB.GetComponent<RectTransform>();
        SetTopBlock(slotBRect, -320f, 140f);
        CreateTMP("BlockBTitleTMP", slotB.transform, "占位 B-标题" + tag, 24, TextAlignmentOptions.Left);
        CreateTMP("BlockBBodyTMP", slotB.transform, "占位 B-正文" + tag, 18, TextAlignmentOptions.Left);

        GameObject slotFooter = CreateUIObject("Slot_Footer", page.transform, addImage: true);
        RectTransform slotFooterRect = slotFooter.GetComponent<RectTransform>();
        SetBottomBlock(slotFooterRect, 0f, 80f);
        CreateTMP("FooterTMP", slotFooter.transform, "占位 页脚/印章" + tag, 18, TextAlignmentOptions.Center);

        return page;
    }

    private static void AttachSwitcher(GameObject root, GameObject paper1, GameObject paper2, GameObject paper3)
    {
        NewspaperPanelSwitcher switcher = root.GetComponent<NewspaperPanelSwitcher>();
        if (switcher == null)
        {
            switcher = root.AddComponent<NewspaperPanelSwitcher>();
        }

        SerializedObject so = new SerializedObject(switcher);
        SerializedProperty pagesProp = so.FindProperty("Pages");
        pagesProp.arraySize = 3;
        pagesProp.GetArrayElementAtIndex(0).objectReferenceValue = paper1;
        pagesProp.GetArrayElementAtIndex(1).objectReferenceValue = paper2;
        pagesProp.GetArrayElementAtIndex(2).objectReferenceValue = paper3;

        SerializedProperty defaultIndexProp = so.FindProperty("DefaultIndex");
        defaultIndexProp.intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BindTab(Button button, NewspaperPanelSwitcher switcher, int index)
    {
        if (button == null || switcher == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        if (index == 0)
        {
            UnityEventTools.AddPersistentListener(button.onClick, switcher.ShowPaper0);
        }
        else if (index == 1)
        {
            UnityEventTools.AddPersistentListener(button.onClick, switcher.ShowPaper1);
        }
        else if (index == 2)
        {
            UnityEventTools.AddPersistentListener(button.onClick, switcher.ShowPaper2);
        }
    }

    private static GameObject CreateUIObject(string name, Transform parent, bool addImage)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        if (addImage)
        {
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.12f, 0.08f);
        }
        return go;
    }

    private static GameObject CreateTMP(string name, Transform parent, string text, float size, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        RectTransform rect = go.GetComponent<RectTransform>();
        SetStretch(rect);
        return go;
    }

    private static GameObject CreateButton(string name, Transform parent, string text)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
        Button btn = go.AddComponent<Button>();

        GameObject label = new GameObject("Text", typeof(RectTransform));
        label.transform.SetParent(go.transform, false);
        TextMeshProUGUI tmp = label.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 20;
        tmp.alignment = TextAlignmentOptions.Center;
        RectTransform labelRect = label.GetComponent<RectTransform>();
        SetStretch(labelRect);

        return go;
    }

    private static RectTransform EnsureRectTransform(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = go.AddComponent<RectTransform>();
        }
        return rect;
    }

    private static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetStretchWithTopOffset(RectTransform rect, float topOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(0f, -topOffset);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetTopFixed(RectTransform rect, float height, float topOffset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, topOffset);
    }

    private static void SetLeftCenter(RectTransform rect, float left, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(left, 0f);
    }

    private static void SetRightCenter(RectTransform rect, float right, float width, float height)
    {
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(-right, 0f);
    }

    private static void SetTopBlock(RectTransform rect, float topOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, topOffset);
    }

    private static void SetBottomBlock(RectTransform rect, float bottomOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, bottomOffset);
    }
}
