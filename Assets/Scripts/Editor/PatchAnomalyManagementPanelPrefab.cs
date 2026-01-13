using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class PatchAnomalyManagementPanelPrefab
{
    // 如果你知道确切路径，填这里（最稳）。
    private const string FORCE_PREFAB_PATH = ""; // e.g. "Assets/Prefabs/UI/AnomalyManagementPanel.prefab"

    // 自动创建的左侧列表 Item Prefab 输出路径
    private const string AUTO_ITEM_PREFAB_PATH = "Assets/Prefabs/UI/AnomalyListItem.prefab";

    [MenuItem("Tools/AI/Patch AnomalyManagementPanel Prefab")]
    public static void Run()
    {
        try
        {
            string path = ResolveManagePanelPrefabPath();
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Patch ManagePanel", "未找到包含 AnomalyManagePanel 的 Prefab。\n请在脚本里设置 FORCE_PREFAB_PATH。", "OK");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            if (!root)
            {
                EditorUtility.DisplayDialog("Patch ManagePanel", $"加载 Prefab 失败：{path}", "OK");
                return;
            }

            bool changed = Patch(root, path);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Patch ManagePanel", changed ? $"Patch 完成：{path}" : $"无需修改：{path}", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            EditorUtility.DisplayDialog("Patch ManagePanel", "Patch 失败，详情见 Console。", "OK");
        }
    }

    private static string ResolveManagePanelPrefabPath()
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
        var pick = candidates
            .OrderByDescending(x => x.go.name.IndexOf("Manage", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(x => x.go.name.IndexOf("Anomaly", StringComparison.OrdinalIgnoreCase) >= 0)
            .First();

        if (candidates.Count > 1)
        {
            Debug.LogWarning("[PatchManagePanel] Multiple prefabs found:\n" +
                             string.Join("\n", candidates.Select(c => $"- {c.go.name} => {c.path}")) +
                             $"\nUsing: {pick.go.name} => {pick.path}\n" +
                             "If wrong, set FORCE_PREFAB_PATH in PatchAnomalyManagementPanelPrefab.cs");
        }

        return pick.path;
    }

    private static bool Patch(GameObject prefabRoot, string prefabPath)
    {
        bool changed = false;

        var panel = prefabRoot.GetComponentInChildren<AnomalyManagePanel>(true);
        if (!panel)
        {
            Debug.LogError($"[PatchManagePanel] No AnomalyManagePanel found in prefab: {prefabPath}");
            return false;
        }

        var so = new SerializedObject(panel);

        // ---- Auto-bind contents if missing ----
        var spLeftContent = so.FindProperty("anomalyListContent");
        var spRightContent = so.FindProperty("agentListContent");
        var spLeftItemPrefab = so.FindProperty("anomalyListItemPrefab");

        if (spLeftContent != null && spLeftContent.objectReferenceValue == null)
        {
            var t = FindByPathOrName(prefabRoot.transform, "Left_AnomalyList/Viewport/Content", "Content");
            if (t != null)
            {
                spLeftContent.objectReferenceValue = t;
                changed = true;
                Debug.Log($"[PatchManagePanel] Bound anomalyListContent => {GetPath(t)}");
            }
        }

        if (spRightContent != null && spRightContent.objectReferenceValue == null)
        {
            var t = FindByPathOrName(prefabRoot.transform, "Right_OpsArea/Viewport/Content", "Content");
            if (t != null)
            {
                spRightContent.objectReferenceValue = t;
                changed = true;
                Debug.Log($"[PatchManagePanel] Bound agentListContent => {GetPath(t)}");
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        // After binding, re-read actual transforms
        so.Update();
        var leftContent = spLeftContent != null ? spLeftContent.objectReferenceValue as Transform : null;
        var rightContent = spRightContent != null ? spRightContent.objectReferenceValue as Transform : null;

        // ---- Ensure layout on contents ----
        if (leftContent)
            changed |= EnsureVerticalLayoutAndFitter(leftContent.gameObject, "LeftContent");

        if (rightContent)
            changed |= EnsureVerticalLayoutAndFitter(rightContent.gameObject, "RightContent");

        // ---- Ensure anomaly list item prefab ----
        if (spLeftItemPrefab != null && spLeftItemPrefab.objectReferenceValue == null)
        {
            var itemPrefab = EnsureAnomalyListItemPrefab();
            if (itemPrefab != null)
            {
                spLeftItemPrefab.objectReferenceValue = itemPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
                Debug.Log($"[PatchManagePanel] Bound anomalyListItemPrefab => {AssetDatabase.GetAssetPath(itemPrefab)}");
            }
            else
            {
                Debug.LogError("[PatchManagePanel] Failed to create/find anomaly list item prefab.");
            }
        }

        // Final apply
        so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static bool EnsureVerticalLayoutAndFitter(GameObject go, string tag)
    {
        bool changed = false;

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = go.AddComponent<VerticalLayoutGroup>();
            changed = true;
        }

        // Apply sane defaults
        if (vlg.childControlWidth != true) { vlg.childControlWidth = true; changed = true; }
        if (vlg.childControlHeight != true) { vlg.childControlHeight = true; changed = true; }
        if (vlg.childForceExpandWidth != true) { vlg.childForceExpandWidth = true; changed = true; }
        if (vlg.childForceExpandHeight != false) { vlg.childForceExpandHeight = false; changed = true; }
        if (Math.Abs(vlg.spacing - 8f) > 0.01f) { vlg.spacing = 8f; changed = true; }
        if (vlg.padding == null || vlg.padding.left != 8 || vlg.padding.right != 8 || vlg.padding.top != 8 || vlg.padding.bottom != 8)
        {
            vlg.padding = new RectOffset(8, 8, 8, 8);
            changed = true;
        }

        var fitter = go.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = go.AddComponent<ContentSizeFitter>();
            changed = true;
        }

        if (fitter.verticalFit != ContentSizeFitter.FitMode.PreferredSize)
        {
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            changed = true;
        }

        if (fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            changed = true;
        }

        if (changed)
            Debug.Log($"[PatchManagePanel] Ensured layout+fitter on {tag}: {go.name}");

        return changed;
    }

    private static GameObject EnsureAnomalyListItemPrefab()
    {
        // If already exists, just load
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AUTO_ITEM_PREFAB_PATH);
        if (existing != null) return existing;

        // Ensure folder
        var dir = Path.GetDirectoryName(AUTO_ITEM_PREFAB_PATH);
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
        {
            // Create nested folders
            CreateFolders(dir);
        }

        // Create temp GO
        var root = new GameObject("AnomalyListItem", typeof(RectTransform));
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 72);

        var img = root.AddComponent<Image>();
        img.raycastTarget = true;

        var btn = root.AddComponent<Button>();
        btn.targetGraphic = img;

        var le = root.AddComponent<LayoutElement>();
        le.preferredHeight = 72;
        le.flexibleWidth = 1f;

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(root.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(12, 8);
        labelRt.offsetMax = new Vector2(-12, -8);

        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Lv1 异常名称 (管理:0)";
        tmp.fontSize = 26;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Save prefab
        var saved = PrefabUtility.SaveAsPrefabAsset(root, AUTO_ITEM_PREFAB_PATH);
        GameObject.DestroyImmediate(root);

        return saved;
    }

    private static void CreateFolders(string fullPath)
    {
        // fullPath like Assets/Prefabs/UI
        var parts = fullPath.Split('/');
        if (parts.Length == 0) return;

        string cur = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(cur, parts[i]);
            }
            cur = next;
        }
    }

    private static Transform FindByPathOrName(Transform root, string path, string fallbackName)
    {
        if (!root) return null;

        // Try exact path first
        var t = root.Find(path);
        if (t != null) return t;

        // Fallback: deep-search by name
        return FindDeepChild(root, fallbackName);
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeepChild(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "";
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }
}
