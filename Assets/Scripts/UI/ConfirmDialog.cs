using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConfirmDialog : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Button backgroundButton;   // 点背景=取消（可选）
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;

    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmLabel;

    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text cancelLabel;

    private Action _onConfirm;
    private Action _onCancel;

    public event Action OnClosed;

    private void Awake()
    {
        // 防止重复绑定（Domain Reload / 重新进 Play）
        if (backgroundButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(Cancel);
        }

        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(Confirm);
        }

        if (cancelButton)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(Cancel);
        }

        gameObject.SetActive(false);
    }

    public void ShowConfirm(
        string title,
        string message,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = "确认",
        string cancelText = "取消")
    {
        _onConfirm = onConfirm;
        _onCancel = onCancel;

        if (titleText) titleText.text = title ?? "";
        if (messageText) messageText.text = message ?? "";

        if (confirmLabel) confirmLabel.text = string.IsNullOrEmpty(confirmText) ? "确认" : confirmText;
        if (cancelLabel) cancelLabel.text = string.IsNullOrEmpty(cancelText) ? "取消" : cancelText;

        if (cancelButton) cancelButton.gameObject.SetActive(true);

        gameObject.SetActive(true);
    }

    public void ShowInfo(string title, string message, string okText = "知道了")
    {
        _onConfirm = null;
        _onCancel = null;

        if (titleText) titleText.text = title ?? "";
        if (messageText) messageText.text = message ?? "";

        if (confirmLabel) confirmLabel.text = string.IsNullOrEmpty(okText) ? "知道了" : okText;
        if (cancelButton) cancelButton.gameObject.SetActive(false);

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _onConfirm = null;
        _onCancel = null;
        OnClosed?.Invoke();
    }

    private void Confirm()
    {
        var cb = _onConfirm;
        Hide();
        cb?.Invoke();
    }

    private void Cancel()
    {
        var cb = _onCancel;
        Hide();
        cb?.Invoke();
    }
}

// <CONFIRM_DIALOG_PREFAB_BUILDER>
// <CONFIRM_DIALOG_PREFAB_BUILDER>
#if UNITY_EDITOR
// Auto-generated helper: create/update ConfirmDialog.prefab via Unity menu.
// Usage: Tools > SCP Manager > Create ConfirmDialog Prefab
public static class ConfirmDialogPrefabBuilder
{
    [UnityEditor.MenuItem("Tools/SCP Manager/Create ConfirmDialog Prefab")]
    public static void CreateOrUpdate()
    {
        const string prefabPath = "Assets/Prefabs/ConfirmDialog.prefab";
        var dir = System.IO.Path.GetDirectoryName(prefabPath);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

        var root = new UnityEngine.GameObject("ConfirmDialog",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.CanvasGroup),
            typeof(ConfirmDialog));

        var rootRT = root.GetComponent<UnityEngine.RectTransform>();
        rootRT.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
        rootRT.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = new UnityEngine.Vector2(900f, 560f);
        rootRT.anchoredPosition = UnityEngine.Vector2.zero;

        var rootImg = root.GetComponent<UnityEngine.UI.Image>();
        rootImg.color = new UnityEngine.Color(0f, 0f, 0f, 0.65f);
        rootImg.raycastTarget = true;

        var cg = root.GetComponent<UnityEngine.CanvasGroup>();
        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        // Panel
        var panel = new UnityEngine.GameObject("Panel",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(UnityEngine.UI.Image));
        panel.transform.SetParent(root.transform, false);

        var panelRT = panel.GetComponent<UnityEngine.RectTransform>();
        panelRT.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
        panelRT.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new UnityEngine.Vector2(820f, 460f);
        panelRT.anchoredPosition = UnityEngine.Vector2.zero;

        var panelImg = panel.GetComponent<UnityEngine.UI.Image>();
        panelImg.color = new UnityEngine.Color(0.10f, 0.10f, 0.10f, 0.95f);
        panelImg.raycastTarget = true;

        // Title
        var titleGO = new UnityEngine.GameObject("Title",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(TMPro.TextMeshProUGUI));
        titleGO.transform.SetParent(panel.transform, false);

        var titleRT = titleGO.GetComponent<UnityEngine.RectTransform>();
        titleRT.anchorMin = new UnityEngine.Vector2(0f, 1f);
        titleRT.anchorMax = new UnityEngine.Vector2(1f, 1f);
        titleRT.pivot = new UnityEngine.Vector2(0.5f, 1f);
        titleRT.sizeDelta = new UnityEngine.Vector2(0f, 80f);
        titleRT.anchoredPosition = new UnityEngine.Vector2(0f, -20f);

        var titleText = titleGO.GetComponent<TMPro.TextMeshProUGUI>();
        titleText.text = "Confirm";
        titleText.fontSize = 48;
        titleText.alignment = TMPro.TextAlignmentOptions.Center;

        // Message
        var msgGO = new UnityEngine.GameObject("Message",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(TMPro.TextMeshProUGUI));
        msgGO.transform.SetParent(panel.transform, false);

        var msgRT = msgGO.GetComponent<UnityEngine.RectTransform>();
        msgRT.anchorMin = new UnityEngine.Vector2(0f, 0.25f);
        msgRT.anchorMax = new UnityEngine.Vector2(1f, 0.85f);
        msgRT.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
        msgRT.offsetMin = new UnityEngine.Vector2(30f, 0f);
        msgRT.offsetMax = new UnityEngine.Vector2(-30f, 0f);

        var msgText = msgGO.GetComponent<TMPro.TextMeshProUGUI>();
        msgText.text = "Are you sure?";
        msgText.fontSize = 32;
        msgText.alignment = TMPro.TextAlignmentOptions.TopLeft;
        msgText.enableWordWrapping = true;

        // Buttons row
        var btnRow = new UnityEngine.GameObject("Buttons",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.UI.HorizontalLayoutGroup));
        btnRow.transform.SetParent(panel.transform, false);

        var btnRowRT = btnRow.GetComponent<UnityEngine.RectTransform>();
        btnRowRT.anchorMin = new UnityEngine.Vector2(0f, 0f);
        btnRowRT.anchorMax = new UnityEngine.Vector2(1f, 0f);
        btnRowRT.pivot = new UnityEngine.Vector2(0.5f, 0f);
        btnRowRT.sizeDelta = new UnityEngine.Vector2(0f, 110f);
        btnRowRT.anchoredPosition = new UnityEngine.Vector2(0f, 20f);

        var hlg = btnRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.padding = new UnityEngine.RectOffset(30, 30, 10, 10);
        hlg.spacing = 20f;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = true;

        var okBtn = CreateButton(btnRow.transform, "ConfirmButton", "Confirm");
        var cancelBtn = CreateButton(btnRow.transform, "CancelButton", "Cancel");

        // Auto-wire fields on ConfirmDialog (reflection; name is fuzzy-safe)
        var dlg = root.GetComponent<ConfirmDialog>();
        AutoWireConfirmDialog(dlg, titleText, msgText, okBtn, cancelBtn);

        var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        UnityEditor.Selection.activeObject = prefab;
        UnityEditor.EditorGUIUtility.PingObject(prefab);
        UnityEngine.Debug.Log($"[ConfirmDialogPrefabBuilder] Created/Updated: {prefabPath}");
    }

    static UnityEngine.UI.Button CreateButton(UnityEngine.Transform parent, string name, string label)
    {
        var go = new UnityEngine.GameObject(name,
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button),
            typeof(UnityEngine.UI.LayoutElement));
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<UnityEngine.UI.LayoutElement>();
        le.preferredHeight = 90f;

        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.color = new UnityEngine.Color(1f, 1f, 1f, 0.10f);

        var textGO = new UnityEngine.GameObject("Text",
            typeof(UnityEngine.RectTransform),
            typeof(UnityEngine.CanvasRenderer),
            typeof(TMPro.TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        var rt = textGO.GetComponent<UnityEngine.RectTransform>();
        rt.anchorMin = UnityEngine.Vector2.zero;
        rt.anchorMax = UnityEngine.Vector2.one;
        rt.sizeDelta = UnityEngine.Vector2.zero;

        var t = textGO.GetComponent<TMPro.TextMeshProUGUI>();
        t.text = label;
        t.fontSize = 34;
        t.alignment = TMPro.TextAlignmentOptions.Center;

        return go.GetComponent<UnityEngine.UI.Button>();
    }

    static void AutoWireConfirmDialog(ConfirmDialog dlg, TMPro.TextMeshProUGUI title, TMPro.TextMeshProUGUI msg, UnityEngine.UI.Button ok, UnityEngine.UI.Button cancel)
    {
        if (dlg == null) return;
        var t = dlg.GetType();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        foreach (var f in t.GetFields(flags))
        {
            if (f.FieldType == typeof(TMPro.TextMeshProUGUI))
            {
                var n = f.Name.ToLowerInvariant();
                if (n.Contains("title") && title != null) f.SetValue(dlg, title);
                else if ((n.Contains("msg") || n.Contains("message") || n.Contains("desc") || n.Contains("content")) && msg != null) f.SetValue(dlg, msg);
            }
            else if (f.FieldType == typeof(UnityEngine.UI.Button))
            {
                var n = f.Name.ToLowerInvariant();
                if ((n.Contains("ok") || n.Contains("yes") || n.Contains("confirm") || n.Contains("accept")) && ok != null) f.SetValue(dlg, ok);
                else if ((n.Contains("no") || n.Contains("cancel") || n.Contains("close") || n.Contains("reject")) && cancel != null) f.SetValue(dlg, cancel);
            }
        }

        UnityEditor.EditorUtility.SetDirty(dlg);
    }
}
#endif
// </CONFIRM_DIALOG_PREFAB_BUILDER>
// </CONFIRM_DIALOG_PREFAB_BUILDER>
