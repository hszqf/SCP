using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class FixAgentPrefabs
{
    [MenuItem("Tools/AI/Patch Agent Prefabs")]
    public static void Run()
    {
        PatchItem("Assets/Prefabs/UI/AgentPickerItem.prefab");
        PatchPicker("Assets/Prefabs/UI/AgentPicker.prefab");
        AssetDatabase.Refresh();
        Debug.Log("Agent Prefabs Patched!");
    }

    static void PatchItem(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError($"File not found: {path}");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var root = scope.prefabContentsRoot;
            ClearChildren(root.transform);

            // Root Setup
            var rect = GetOrAdd<RectTransform>(root);
            rect.sizeDelta = new Vector2(800, 80);
            GetOrAdd<Image>(root).color = new Color(0.15f, 0.15f, 0.15f); // Dark Grey
            GetOrAdd<Button>(root);

            // 1. Name
            var nameObj = CreateChild(root.transform, "NameText");
            var nameTxt = GetOrAdd<TextMeshProUGUI>(nameObj);
            nameTxt.text = "Agent Name";
            nameTxt.fontSize = 28;
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var nameRt = nameObj.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 0); nameRt.anchorMax = new Vector2(0.4f, 1);
            nameRt.offsetMin = new Vector2(20, 0); nameRt.offsetMax = new Vector2(0, 0);

            // 2. Attr
            var attrObj = CreateChild(root.transform, "AttrText");
            var attrTxt = GetOrAdd<TextMeshProUGUI>(attrObj);
            attrTxt.text = "P:5 O:5 R:5";
            attrTxt.fontSize = 20;
            attrTxt.alignment = TextAlignmentOptions.Midline;
            attrTxt.color = new Color(0.8f, 0.8f, 0.8f);
            var attrRt = attrObj.GetComponent<RectTransform>();
            attrRt.anchorMin = new Vector2(0.4f, 0); attrRt.anchorMax = new Vector2(0.7f, 1);
            attrRt.offsetMin = Vector2.zero; attrRt.offsetMax = Vector2.zero;

            // 3. Busy Tag
            var busyObj = CreateChild(root.transform, "BusyTagText");
            var busyTxt = GetOrAdd<TextMeshProUGUI>(busyObj);
            busyTxt.text = "BUSY";
            busyTxt.fontSize = 20;
            busyTxt.color = Color.red;
            busyTxt.alignment = TextAlignmentOptions.MidlineRight;
            var busyRt = busyObj.GetComponent<RectTransform>();
            busyRt.anchorMin = new Vector2(0.7f, 0); busyRt.anchorMax = new Vector2(0.9f, 1);
            busyRt.offsetMin = Vector2.zero; busyRt.offsetMax = Vector2.zero;

            // 4. Selected Mark (Check Icon)
            var markObj = CreateChild(root.transform, "SelectedMark");
            var markImg = GetOrAdd<Image>(markObj);
            markImg.color = Color.green;
            markObj.SetActive(false);
            var markRt = markObj.GetComponent<RectTransform>();
            markRt.anchorMin = new Vector2(1, 0.5f); markRt.anchorMax = new Vector2(1, 0.5f);
            markRt.anchoredPosition = new Vector2(-40, 0);
            markRt.sizeDelta = new Vector2(30, 30);
        }
    }

    static void PatchPicker(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError($"File not found: {path}");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var root = scope.prefabContentsRoot;
            ClearChildren(root.transform);

            var rect = GetOrAdd<RectTransform>(root);
            rect.sizeDelta = new Vector2(900, 600);
            GetOrAdd<Image>(root).color = new Color(0.1f, 0.1f, 0.1f, 0.98f);
            GetOrAdd<CanvasGroup>(root);

            // --- Header ---
            var header = CreateChild(root.transform, "Header");
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1); hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 60);
            hRt.anchoredPosition = Vector2.zero;

            var title = CreateChild(header.transform, "TitleText");
            var titleTxt = GetOrAdd<TextMeshProUGUI>(title);
            titleTxt.text = "选择干员";
            titleTxt.fontSize = 32;
            titleTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(30, 0); tRt.offsetMax = new Vector2(-100, 0);

            var close = CreateChild(header.transform, "CloseBT");
            GetOrAdd<Image>(close).color = new Color(0.8f, 0.2f, 0.2f);
            GetOrAdd<Button>(close);
            var cRt = close.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(1, 0.5f); cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-30, 0);
            cRt.sizeDelta = new Vector2(40, 40);
            GetOrAdd<TextMeshProUGUI>(CreateChild(close.transform, "X")).text = "X";

            // --- Footer ---
            var footer = CreateChild(root.transform, "Footer");
            var fRt = footer.GetComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0, 0); fRt.anchorMax = new Vector2(1, 0);
            fRt.pivot = new Vector2(0.5f, 0);
            fRt.sizeDelta = new Vector2(0, 80);
            fRt.anchoredPosition = Vector2.zero;

            var count = CreateChild(footer.transform, "SelectedCountText");
            var countTxt = GetOrAdd<TextMeshProUGUI>(count);
            countTxt.text = "已选: 0/1";
            countTxt.fontSize = 24;
            countTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var countRt = count.GetComponent<RectTransform>();
            countRt.anchorMin = new Vector2(0, 0); countRt.anchorMax = new Vector2(0.5f, 1);
            countRt.offsetMin = new Vector2(30, 0);

            var confirm = CreateChild(footer.transform, "ConfirmBT");
            GetOrAdd<Image>(confirm).color = new Color(0.2f, 0.6f, 0.2f);
            GetOrAdd<Button>(confirm);
            var cfRt = confirm.GetComponent<RectTransform>();
            cfRt.anchorMin = new Vector2(1, 0.5f); cfRt.anchorMax = new Vector2(1, 0.5f);
            cfRt.anchoredPosition = new Vector2(-100, 0);
            cfRt.sizeDelta = new Vector2(160, 50);
            var cfTxt = GetOrAdd<TextMeshProUGUI>(CreateChild(confirm.transform, "Text"));
            cfTxt.text = "确认派遣";
            cfTxt.fontSize = 24;
            cfTxt.alignment = TextAlignmentOptions.Center;

            // --- ScrollView ---
            var sv = CreateChild(root.transform, "ScrollView");
            var svRt = sv.GetComponent<RectTransform>();
            svRt.anchorMin = Vector2.zero; svRt.anchorMax = Vector2.one;
            svRt.offsetMin = new Vector2(20, 80); // Bottom = Footer Height
            svRt.offsetMax = new Vector2(-20, -60); // Top = -Header Height
            GetOrAdd<Image>(sv).color = new Color(0,0,0,0.3f);
            var scrollRect = GetOrAdd<ScrollRect>(sv);

            var vp = CreateChild(sv.transform, "Viewport");
            GetOrAdd<Image>(vp); GetOrAdd<Mask>(vp).showMaskGraphic = false;
            var vpRt = vp.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;

            var content = CreateChild(vp.transform, "Content");
            var vlg = GetOrAdd<VerticalLayoutGroup>(content);
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 5;
            vlg.padding = new RectOffset(5, 5, 5, 5);
            GetOrAdd<ContentSizeFitter>(content).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 300);

            scrollRect.content = contentRt;
            scrollRect.viewport = vpRt;
        }
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(t.GetChild(i).gameObject);
    }

    static GameObject CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }
}