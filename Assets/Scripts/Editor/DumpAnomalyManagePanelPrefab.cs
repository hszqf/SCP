using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class DumpAnomalyManagePanelPrefab
{
    // If you know exact prefab path, set it here.
    private const string FORCE_PREFAB_PATH = ""; // e.g. "Assets/Prefabs/UI/ManagePanel.prefab"

    [MenuItem("Tools/AI/Dump AnomalyManagePanel Prefab")]
    public static void Run()
    {
        try
        {
            string path = ResolvePrefabPath();
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Dump ManagePanel", "未找到包含 AnomalyManagePanel 的 Prefab。\n请在脚本里设置 FORCE_PREFAB_PATH。", "OK");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Dump ManagePanel", $"加载 Prefab 失败：{path}", "OK");
                return;
            }

            var panel = root.GetComponentInChildren<AnomalyManagePanel>(true);
            var sb = new StringBuilder();

            sb.AppendLine("=== Dump AnomalyManagePanel Prefab ===");
            sb.AppendLine("Prefab: " + path);
            sb.AppendLine("Root: " + root.name);
            sb.AppendLine();

            if (panel == null)
            {
                sb.AppendLine("[ERROR] Prefab contains no AnomalyManagePanel component.");
            }
            else
            {
                sb.AppendLine("-- Component Bindings (AnomalyManagePanel) --");
                DumpField(sb, panel, "anomalyListContent");
                DumpField(sb, panel, "anomalyListItemPrefab");
                DumpField(sb, panel, "agentListContent");
                DumpField(sb, panel, "agentPickerItemPrefab");
                DumpField(sb, panel, "confirmButton");
                DumpField(sb, panel, "closeButton");
                DumpField(sb, panel, "headerText");
                DumpField(sb, panel, "hintText");
                sb.AppendLine();

                // Validate likely layout
                sb.AppendLine("-- Layout Checks --");
                CheckContent(sb, panel, "anomalyListContent");
                CheckContent(sb, panel, "agentListContent");
                sb.AppendLine();

                // Inspect item prefabs
                sb.AppendLine("-- Item Prefab Checks --");
                CheckAnomalyItemPrefab(sb, panel);
                CheckAgentItemPrefab(sb, panel);
                sb.AppendLine();
            }

            sb.AppendLine("-- Hierarchy --");
            DumpHierarchy(sb, root.transform, 0);

            PrefabUtility.UnloadPrefabContents(root);

            string outPath = "Assets/Temp/manage_panel_dump.txt";
            Directory.CreateDirectory("Assets/Temp");
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Dump ManagePanel", $"已输出报告：{outPath}\n同时已打印到 Console。", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            EditorUtility.DisplayDialog("Dump ManagePanel", "Dump 失败，详情见 Console。", "OK");
        }
    }

    private static string ResolvePrefabPath()
    {
        if (!string.IsNullOrEmpty(FORCE_PREFAB_PATH)) return FORCE_PREFAB_PATH;

        var guids = AssetDatabase.FindAssets("t:Prefab");
        var candidates = guids
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Select(p => new { path = p, go = AssetDatabase.LoadAssetAtPath<GameObject>(p) })
            .Where(x => x.go != null)
            .Where(x => x.go.GetComponentInChildren<AnomalyManagePanel>(true) != null)
            .ToList();

        if (candidates.Count == 0) return "";

        // Prefer name contains
        var preferred = candidates
            .OrderByDescending(x => x.go.name.IndexOf("Manage", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(x => x.go.name.IndexOf("Anomaly", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (candidates.Count > 1)
        {
            Debug.LogWarning("[DumpManagePanel] Multiple prefabs found:\n" + string.Join("\n", candidates.Select(c => $"- {c.go.name} => {c.path}")) + "\nUsing: " + preferred[0].path);
        }

        return preferred[0].path;
    }

    private static void DumpField(StringBuilder sb, Component comp, string fieldName)
    {
        var so = new SerializedObject(comp);
        var sp = so.FindProperty(fieldName);
        if (sp == null)
        {
            sb.AppendLine($"[WARN] Field not found: {fieldName}");
            return;
        }

        UnityEngine.Object obj = sp.objectReferenceValue;
        if (obj == null)
        {
            sb.AppendLine($"[MISSING] {fieldName} = null");
            return;
        }

        string extra = "";
        if (obj is Component c) extra = $" ({c.GetType().Name}) path={GetPath(c.transform)}";
        else if (obj is GameObject g) extra = $" (GameObject) name={g.name}";
        else extra = $" ({obj.GetType().Name})";

        sb.AppendLine($"[OK] {fieldName} = {obj.name}{extra}");
    }

    private static void CheckContent(StringBuilder sb, AnomalyManagePanel panel, string fieldName)
    {
        var so = new SerializedObject(panel);
        var sp = so.FindProperty(fieldName);
        var tr = sp != null ? sp.objectReferenceValue as Transform : null;
        if (tr == null)
        {
            sb.AppendLine($"[MISSING] {fieldName}: Transform is null");
            return;
        }

        var scroll = tr.GetComponentInParent<ScrollRect>(true);
        var vlg = tr.GetComponent<VerticalLayoutGroup>();
        var hlg = tr.GetComponent<HorizontalLayoutGroup>();
        var grid = tr.GetComponent<GridLayoutGroup>();
        var fitter = tr.GetComponent<ContentSizeFitter>();

        sb.AppendLine($"{fieldName} path={GetPath(tr)}");
        sb.AppendLine($"  - In ScrollRect: {(scroll ? scroll.name : "NO")}");
        sb.AppendLine($"  - LayoutGroup: {(vlg ? "Vertical" : hlg ? "Horizontal" : grid ? "Grid" : "NO")}");
        sb.AppendLine($"  - ContentSizeFitter: {(fitter ? fitter.verticalFit.ToString() : "NO")}");

        var rt = tr as RectTransform;
        if (rt)
        {
            sb.AppendLine($"  - Rect: anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} sizeDelta={rt.sizeDelta}");
        }
    }

    private static void CheckAnomalyItemPrefab(StringBuilder sb, AnomalyManagePanel panel)
    {
        var so = new SerializedObject(panel);
        var sp = so.FindProperty("anomalyListItemPrefab");
        var prefab = sp != null ? sp.objectReferenceValue as GameObject : null;
        if (prefab == null)
        {
            sb.AppendLine("[MISSING] anomalyListItemPrefab is null");
            return;
        }

        sb.AppendLine($"anomalyListItemPrefab: {prefab.name}");
        var btn = prefab.GetComponentInChildren<Button>(true);
        var tmp = prefab.GetComponentInChildren<TMP_Text>(true);
        sb.AppendLine($"  - Button: {(btn ? "YES" : "NO")}");
        sb.AppendLine($"  - TMP_Text: {(tmp ? (tmp.name + " path=" + GetPath(tmp.transform)) : "NO")}");
    }

    private static void CheckAgentItemPrefab(StringBuilder sb, AnomalyManagePanel panel)
    {
        var so = new SerializedObject(panel);
        var sp = so.FindProperty("agentPickerItemPrefab");
        var prefab = sp != null ? sp.objectReferenceValue as GameObject : null;
        if (prefab == null)
        {
            sb.AppendLine("[MISSING] agentPickerItemPrefab is null");
            return;
        }

        sb.AppendLine($"agentPickerItemPrefab: {prefab.name}");
        var view = prefab.GetComponentInChildren<AgentPickerItemView>(true);
        sb.AppendLine($"  - AgentPickerItemView: {(view ? "YES" : "NO")}");

        // Also check if it has a LayoutElement (common cause: rows collapse)
        var le = prefab.GetComponentInChildren<LayoutElement>(true);
        sb.AppendLine($"  - LayoutElement: {(le ? "YES" : "NO")}");
    }

    private static void DumpHierarchy(StringBuilder sb, Transform t, int depth)
    {
        if (t == null) return;
        sb.AppendLine(new string(' ', depth * 2) + "- " + t.name + SummarizeUI(t));
        for (int i = 0; i < t.childCount; i++)
        {
            DumpHierarchy(sb, t.GetChild(i), depth + 1);
        }
    }

    private static string SummarizeUI(Transform t)
    {
        var rt = t as RectTransform;
        if (!rt) return "";

        bool hasScroll = t.GetComponent<ScrollRect>() != null;
        bool hasLayout = t.GetComponent<VerticalLayoutGroup>() != null || t.GetComponent<HorizontalLayoutGroup>() != null || t.GetComponent<GridLayoutGroup>() != null;
        bool hasFitter = t.GetComponent<ContentSizeFitter>() != null;

        string flags = "";
        if (hasScroll) flags += " [ScrollRect]";
        if (hasLayout) flags += " [LayoutGroup]";
        if (hasFitter) flags += " [Fitter]";

        return flags;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "";
        var sb = new StringBuilder(t.name);
        while (t.parent != null)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }
}
