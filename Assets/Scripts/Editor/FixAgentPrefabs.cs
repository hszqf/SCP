using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class FixAgentPrefabs
{
    [MenuItem("Tools/AI/Patch Agent Prefabs (Beautify)")]
    public static void Run()
    {
        PatchItem("Assets/Prefabs/UI/AgentPickerItem.prefab");
        PatchPicker("Assets/Prefabs/UI/AgentPicker.prefab");
        AssetDatabase.Refresh();
        Debug.Log("Agent Prefabs Beautified!");
    }

    // ---------------- HELPER COLORS ----------------
    static Color Col(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }
    static Color BgDark = Col("#1E1E1E");
    static Color BgDarker = Col("#121212");
    static Color AccentCyan = Col("#00ADB5");
    static Color TextWhite = Col("#EEEEEE");
    static Color TextGray = Col("#AAAAAA");
    static Color BusyRed = Col("#FF4C4C");
    static Color ConfirmGreen = Col("#4CAF50");

    // ---------------- ITEM PREFAB ----------------
    static void PatchItem(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var root = scope.prefabContentsRoot;
            ClearChildren(root.transform);

            // 1. 卡片底板
            var rect = GetOrAdd<RectTransform>(root);
            rect.sizeDelta = new Vector2(860, 90);
            var bg = GetOrAdd<Image>(root);
            bg.color = new Color(0.18f, 0.18f, 0.18f); // 浅一点的灰
            GetOrAdd<Button>(root);

            // 2. 左侧装饰条 (选中时可变色)
            var bar = CreateChild(root.transform, "AccentBar");
            GetOrAdd<Image>(bar).color = AccentCyan;
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = Vector2.zero; barRt.anchorMax = new Vector2(0, 1);
            barRt.sizeDelta = new Vector2(6, 0);
            barRt.pivot = new Vector2(0, 0.5f);
            barRt.anchoredPosition = Vector2.zero;

            // 3. 名字 (大号)
            var nameObj = CreateChild(root.transform, "NameText");
            var nameTxt = GetOrAdd<TextMeshProUGUI>(nameObj);
            nameTxt.text = "AGENT NAME";
            nameTxt.fontSize = 32;
            nameTxt.fontStyle = FontStyles.Bold;
            nameTxt.color = TextWhite;
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var nameRt = nameObj.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 0); nameRt.anchorMax = new Vector2(0.4f, 1);
            nameRt.offsetMin = new Vector2(30, 0); 

            // 4. 属性 (中号，单色)
            var attrObj = CreateChild(root.transform, "AttrText");
            var attrTxt = GetOrAdd<TextMeshProUGUI>(attrObj);
            attrTxt.text = "PER: 5  OPS: 5  RES: 5";
            attrTxt.fontSize = 20;
            attrTxt.color = TextGray;
            attrTxt.alignment = TextAlignmentOptions.Midline;
            var attrRt = attrObj.GetComponent<RectTransform>();
            attrRt.anchorMin = new Vector2(0.4f, 0); attrRt.anchorMax = new Vector2(0.8f, 1);
            attrRt.offsetMin = Vector2.zero; attrRt.offsetMax = Vector2.zero;

            // 5. 忙碌标签 (BUSY)
            var busyObj = CreateChild(root.transform, "BusyTagText");
            var busyTxt = GetOrAdd<TextMeshProUGUI>(busyObj);
            busyTxt.text = "[BUSY]";
            busyTxt.fontSize = 20;
            busyTxt.color = BusyRed;
            busyTxt.fontStyle = FontStyles.Bold;
            busyTxt.alignment = TextAlignmentOptions.MidlineRight;
            var busyRt = busyObj.GetComponent<RectTransform>();
            busyRt.anchorMin = new Vector2(0.8f, 0); busyRt.anchorMax = new Vector2(0.95f, 1);
            busyRt.offsetMin = Vector2.zero; busyRt.offsetMax = Vector2.zero;

            // 6. 选中对勾 (默认隐藏)
            var checkObj = CreateChild(root.transform, "SelectedMark");
            var checkImg = GetOrAdd<Image>(checkObj);
            checkImg.color = AccentCyan;
            checkObj.SetActive(false);
            var checkRt = checkObj.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(1, 1);
            checkRt.anchoredPosition = new Vector2(-10, -10);
            checkRt.sizeDelta = new Vector2(16, 16);
        }
    }

    // ---------------- PICKER WINDOW ----------------
    static void PatchPicker(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var root = scope.prefabContentsRoot;
            ClearChildren(root.transform);

            var rect = GetOrAdd<RectTransform>(root);
            rect.sizeDelta = new Vector2(900, 650);
            // 全屏半透明遮罩背景
            GetOrAdd<Image>(root).color = new Color(0, 0, 0, 0.85f);
            GetOrAdd<CanvasGroup>(root);

            // --- 主窗口 Frame ---
            var frame = CreateChild(root.transform, "WindowFrame");
            var fRt = frame.GetComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0.5f, 0.5f); fRt.anchorMax = new Vector2(0.5f, 0.5f);
            fRt.sizeDelta = new Vector2(900, 650);
            GetOrAdd<Image>(frame).color = BgDark;
            // 加个 Outline
            var outline = GetOrAdd<Outline>(frame);
            outline.effectColor = new Color(0.3f, 0.3f, 0.3f);
            outline.effectDistance = new Vector2(2, -2);

            // --- Header ---
            var header = CreateChild(frame.transform, "Header");
            GetOrAdd<Image>(header).color = BgDarker;
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1); hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 70);
            hRt.anchoredPosition = Vector2.zero;

            var title = CreateChild(header.transform, "TitleText");
            var titleTxt = GetOrAdd<TextMeshProUGUI>(title);
            titleTxt.text = "// SELECT PERSONNEL";
            titleTxt.fontSize = 28;
            titleTxt.fontStyle = FontStyles.Bold;
            titleTxt.color = AccentCyan;
            titleTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(30, 0);

            // Close BT
            var close = CreateChild(header.transform, "CloseBT");
            GetOrAdd<Image>(close).color = Color.clear; // 透明点击区域
            GetOrAdd<Button>(close);
            var cRt = close.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(1, 0.5f); cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-40, 0);
            cRt.sizeDelta = new Vector2(40, 40);
            
            var xTxt = GetOrAdd<TextMeshProUGUI>(CreateChild(close.transform, "X"));
            xTxt.text = "×";
            xTxt.fontSize = 40;
            xTxt.color = Color.gray;
            xTxt.alignment = TextAlignmentOptions.Center;

            // --- Footer ---
            var footer = CreateChild(frame.transform, "Footer");
            GetOrAdd<Image>(footer).color = BgDarker;
            var footerRt = footer.GetComponent<RectTransform>();
            footerRt.anchorMin = new Vector2(0, 0); footerRt.anchorMax = new Vector2(1, 0);
            footerRt.pivot = new Vector2(0.5f, 0);
            footerRt.sizeDelta = new Vector2(0, 80);
            footerRt.anchoredPosition = Vector2.zero;

            // Selected Info
            var count = CreateChild(footer.transform, "SelectedCountText");
            var countTxt = GetOrAdd<TextMeshProUGUI>(count);
            countTxt.text = "SELECTED: 0/1";
            countTxt.fontSize = 20;
            countTxt.color = TextGray;
            countTxt.alignment = TextAlignmentOptions.MidlineLeft;
            var countRt = count.GetComponent<RectTransform>();
            countRt.anchorMin = new Vector2(0, 0); countRt.anchorMax = new Vector2(0.5f, 1);
            countRt.offsetMin = new Vector2(30, 0);

            // Confirm BT
            var confirm = CreateChild(footer.transform, "ConfirmBT");
            GetOrAdd<Image>(confirm).color = ConfirmGreen;
            GetOrAdd<Button>(confirm);
            var cfRt = confirm.GetComponent<RectTransform>();
            cfRt.anchorMin = new Vector2(1, 0.5f); cfRt.anchorMax = new Vector2(1, 0.5f);
            cfRt.anchoredPosition = new Vector2(-120, 0);
            cfRt.sizeDelta = new Vector2(180, 50);
            
            var cfTxt = GetOrAdd<TextMeshProUGUI>(CreateChild(confirm.transform, "Text"));
            cfTxt.text = "CONFIRM";
            cfTxt.fontSize = 22;
            cfTxt.fontStyle = FontStyles.Bold;
            cfTxt.color = Color.white;
            cfTxt.alignment = TextAlignmentOptions.Center;

            // --- ScrollView ---
            var sv = CreateChild(frame.transform, "ScrollView");
            var svRt = sv.GetComponent<RectTransform>();
            svRt.anchorMin = Vector2.zero; svRt.anchorMax = Vector2.one;
            svRt.offsetMin = new Vector2(20, 90); // Bottom + padding
            svRt.offsetMax = new Vector2(-20, -80); // Top + padding
            
            // Scrollbar Track (Optional visual)
            GetOrAdd<Image>(sv).color = new Color(0,0,0,0.2f);
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
            vlg.spacing = 8; // 增加间距
            vlg.padding = new RectOffset(0, 10, 0, 0);
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