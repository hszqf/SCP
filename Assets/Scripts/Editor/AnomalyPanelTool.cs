using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class AnomalyPanelTool
{
    private static Color ColWinBG = new Color(0.1f, 0.1f, 0.11f, 0.98f); // 深色背景
    private static Color ColAccent = new Color(0f, 0.68f, 0.71f, 1f);   // 青色强调

    [MenuItem("Tools/Project Fixes/Create Anomaly Management Panel", false, 2)]
    public static void CreatePanel()
    {
        // 1. 创建根节点
        GameObject root = new GameObject("AnomalyManagementPanel", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(1000, 700);

        // 2. 背景图层
        GameObject bg = new GameObject("Background", typeof(Image));
        bg.transform.SetParent(root.transform, false);
        bg.GetComponent<Image>().color = ColWinBG;
        Stretch(bg.GetComponent<RectTransform>());

        // 3. 标题
        GameObject title = new GameObject("Title", typeof(TextMeshProUGUI));
        title.transform.SetParent(root.transform, false);
        var txt = title.GetComponent<TextMeshProUGUI>();
        txt.text = "ANOMALY MANAGEMENT PROTOCOL";
        txt.color = ColAccent;
        txt.fontSize = 24;
        txt.fontStyle = FontStyles.Bold;
        RectTransform titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1); titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = new Vector2(20, -20);

        // 4. 水平容器 (分左右两半)
        GameObject mainH = new GameObject("MainContent", typeof(HorizontalLayoutGroup));
        mainH.transform.SetParent(root.transform, false);
        var hlg = mainH.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(20, 20, 60, 20);
        hlg.spacing = 20;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        Stretch(mainH.GetComponent<RectTransform>());

        // 5. 左侧：异常列表 (Anomaly List)
        GameObject left = new GameObject("Left_AnomalyList", typeof(Image), typeof(VerticalLayoutGroup));
        left.transform.SetParent(mainH.transform, false);
        left.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        var vlg = left.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10); vlg.spacing = 10;

        // 6. 右侧：管理区域 (Containment Ops)
        GameObject right = new GameObject("Right_OpsArea", typeof(VerticalLayoutGroup));
        right.transform.SetParent(mainH.transform, false);
        var vlgR = right.GetComponent<VerticalLayoutGroup>();
        vlgR.spacing = 10; vlgR.childControlHeight = false;

        // 7. 右侧上部：干员选择 Grid
        GameObject agentGrid = new GameObject("AgentGrid", typeof(GridLayoutGroup));
        agentGrid.transform.SetParent(right.transform, false);
        var grid = agentGrid.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280, 80); // 预估 AgentPickerItem 尺寸
        grid.spacing = new Vector2(10, 10);
        var le = agentGrid.AddComponent<LayoutElement>();
        le.flexibleHeight = 1;

        // 8. 底部：确认/关闭按钮
        GameObject btnClose = new GameObject("Btn_Close", typeof(Image), typeof(Button));
        btnClose.transform.SetParent(root.transform, false);
        btnClose.GetComponent<Image>().color = new Color(0.3f, 0.1f, 0.1f, 1f);
        RectTransform closeRT = btnClose.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1, 0); closeRT.anchorMax = new Vector2(1, 0);
        closeRT.sizeDelta = new Vector2(120, 40); closeRT.anchoredPosition = new Vector2(-70, 30);

        // 保存为 Prefab
        string path = "Assets/Prefabs/UI/AnomalyManagementPanel.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        GameObject.DestroyImmediate(root);
        Debug.Log("✅ AnomalyManagementPanel 已生成至: " + path);
    }

    static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}