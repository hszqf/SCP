// <EXPORT_BLOCK>
using System;
using System.Collections.Generic;
using System.Reflection;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Modal")]
    [SerializeField] private GameObject dim;

    [Header("Node Panel")]
    [SerializeField] private GameObject nodePanel;     // 拖 NodePanel(父物体)那层
    [SerializeField] private TMP_Text nodeTitle;
    [SerializeField] private TMP_Text nodeStatus;
    [SerializeField] private TMP_Text progressText;

    [Header("Event Panel")]
    [SerializeField] private EventPanel eventPanel;    // 拖场景里的 EventPanel 实例

    [Header("News Panel")]
    [SerializeField] private NewsPanel newsPanel;

    [Header("Confirm Dialog (Prefab Instance)")]
    [SerializeField] private ConfirmDialog confirmDialog;   // 拖场景里的 ConfirmDialog 实例（inactive 也行）

    [Header("Withdraw Button (in NodePanel)")]
    [SerializeField] private Button withdrawButton;         // 拖 NodePanel 里的“撤回任务”按钮
    [SerializeField] private TMP_Text withdrawButtonLabel;  // 可选：按钮文字

// <AGENT_PICKER_BLOCK>

    // ===================== Agent Picker (NEW) =====================
    [Header("Dispatch UI (Agent Picker)")]
    [SerializeField] private bool useAgentPicker = true;

    [SerializeField] private string[] fallbackAgentIds = new[] { "A1", "A2", "A3" };
    [SerializeField] private Vector2 pickerSize = new Vector2(900, 600);

    enum AssignMode { Investigate, Contain }

    private GameObject _agentPickerRoot;
    private TMP_Text _pickerTitle;
    private ScrollRect _pickerScroll;
    private RectTransform _pickerContent;
    private AssignMode _pickerMode;

// </AGENT_PICKER_BLOCK>

    // ==============================================================
    private string _currentNodeId;

    private void Awake()
    {
        I = this;

        EnsureConfirmDialog();
        WireWithdrawButton();

        ResetUI();
        EnsureAgentPickerCreated(); // 运行时创建一次即可（不影响你现有场景结构）
    }

    private void EnsureConfirmDialog()
    {
        if (!confirmDialog)
            confirmDialog = FindObjectOfType<ConfirmDialog>(true);

        if (confirmDialog)
        {
            confirmDialog.gameObject.SetActive(false);
            confirmDialog.OnClosed -= RestoreTopModal;
            confirmDialog.OnClosed += RestoreTopModal;
        }
    }

    private void WireWithdrawButton()
    {
        if (!withdrawButton) return;

        withdrawButton.onClick.RemoveAllListeners();
        withdrawButton.onClick.AddListener(OnWithdrawClicked);
        withdrawButton.gameObject.SetActive(false);
    }

    private void ResetUI()
    {
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);
        if (dim) dim.SetActive(false);

        _currentNodeId = null;
        HideAgentPicker();

        if (confirmDialog) confirmDialog.Hide();
        if (withdrawButton) withdrawButton.gameObject.SetActive(false);
    }

    // ========= 核心：管理 Dim 与顶层面板的 sibling 顺序 =========
    void SetModalTop(RectTransform topPanel)
    {
        if (!dim) return;

        if (topPanel == null || !topPanel.gameObject.activeSelf)
        {
            dim.SetActive(false);
            return;
        }

        dim.SetActive(true);

        // 让 topPanel 成为最上层
        topPanel.SetAsLastSibling();

        // 把 dim 放在 topPanel 正下方，确保 dim 永远不会盖住 topPanel
        int topIndex = topPanel.GetSiblingIndex();
        dim.transform.SetSiblingIndex(Mathf.Max(0, topIndex - 1));
    }

    void RestoreTopModal()
    {
        // confirm 优先
        if (confirmDialog && confirmDialog.gameObject.activeSelf)
        {
            SetModalTop((RectTransform)confirmDialog.transform);
            return;
        }

        if (eventPanel && eventPanel.gameObject.activeSelf)
            SetModalTop((RectTransform)eventPanel.transform);
        else if (nodePanel && nodePanel.activeSelf)
            SetModalTop((RectTransform)nodePanel.transform);
        else if (newsPanel && newsPanel.gameObject.activeSelf)
            SetModalTop((RectTransform)newsPanel.transform);
        else
            SetModalTop(null);
    }

    static bool HasTask(NodeState n)
        => n != null && (n.Status == NodeStatus.Investigating || n.Status == NodeStatus.Containing);

    static bool IsTaskStarted(NodeState n)
    {
        if (n == null) return false;
        if (n.Status == NodeStatus.Investigating) return n.InvestigateProgress > 0.0001f;
        if (n.Status == NodeStatus.Containing) return n.ContainProgress > 0.0001f;
        return false;
    }

    void UpdateWithdrawUI(NodeState n)
    {
        if (!withdrawButton) return;

        bool show = HasTask(n);
        withdrawButton.gameObject.SetActive(show);

        if (!show)
            return;

        bool started = IsTaskStarted(n);
        if (withdrawButtonLabel)
            withdrawButtonLabel.text = started ? "强制撤回" : "取消派遣";
    }

    void ShowInfo(string title, string message)
    {
        EnsureConfirmDialog();
        if (!confirmDialog)
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog missing: {title} / {message}");
            return;
        }

        confirmDialog.ShowInfo(title, message);
        SetModalTop((RectTransform)confirmDialog.transform);
    }

    void ShowConfirm(string title, string message, Action onConfirm, string confirmText = "确认", string cancelText = "取消")
    {
        EnsureConfirmDialog();
        if (!confirmDialog)
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog missing: {title} / {message}");
            return;
        }

        confirmDialog.ShowConfirm(title, message, onConfirm, null, confirmText, cancelText);
        SetModalTop((RectTransform)confirmDialog.transform);
    }

    // ========= Node Panel =========
    public void OpenNode(string nodeId)
    {
        _currentNodeId = nodeId;

        if (nodePanel) nodePanel.SetActive(true);

        // NodePanel 成为 top
        SetModalTop((RectTransform)nodePanel.transform);

        RefreshNodePanel();
    }

    public void RefreshNodePanel()
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        NodeState n = GameController.I.GetNode(_currentNodeId);
        if (n == null) return;

        if (nodeTitle) nodeTitle.text = n.Name;

        float p =
            n.Status == NodeStatus.Investigating ? n.InvestigateProgress :
            n.Status == NodeStatus.Containing ? n.ContainProgress :
            0f;

        if (nodeStatus)
            nodeStatus.text = $"{n.Status} | Anomaly:{n.HasAnomaly} L{n.AnomalyLevel}";

        if (progressText)
            progressText.text =
                $"Progress: {Mathf.RoundToInt(p * 100)}%  |  Agent: {(string.IsNullOrEmpty(n.AssignedAgentId) ? "-" : n.AssignedAgentId)}";

        UpdateWithdrawUI(n);
    }

    public void CloseNode()
    {
        HideAgentPicker();

        if (nodePanel) nodePanel.SetActive(false);
        _currentNodeId = null;

        RestoreTopModal();
    }

    // ========= Event Panel =========
    public void OpenEventIfAny()
    {
        var list = GameController.I.State.PendingEvents;
        if (list == null || list.Count == 0) return;

        if (!eventPanel)
        {
            Debug.LogError("[UIPanelRoot] eventPanel is NULL (Inspector 没拖场景实例)");
            return;
        }

        eventPanel.gameObject.SetActive(true);
        eventPanel.Show(list[0]);
        SetModalTop((RectTransform)eventPanel.transform);
    }

    public void CloseEvent()
    {
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        RestoreTopModal();
    }

    // ========= News Panel =========
    public void OpenNews()
    {
        if (!newsPanel)
        {
            Debug.LogError("[UIPanelRoot] newsPanel is NULL（Inspector 没拖）");
            return;
        }

        newsPanel.Show();
        SetModalTop((RectTransform)newsPanel.transform);
    }

    public void CloseNews()
    {
        if (newsPanel) newsPanel.Hide();
        RestoreTopModal();
    }

    // ========= 统一关闭（你可以绑在 dim 点击上） =========
    public void CloseAll()
    {
        HideAgentPicker();

        if (confirmDialog && confirmDialog.gameObject.activeSelf)
            confirmDialog.Hide();

        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);

        _currentNodeId = null;
        SetModalTop(null);
    }

    // ===================== Withdraw =====================
    void OnWithdrawClicked()
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        var n = GameController.I.GetNode(_currentNodeId);
        if (!HasTask(n)) return;

        // 未开始：等价“取消派遣”，不做二次确认
        if (!IsTaskStarted(n))
        {
            GameController.I.ForceWithdraw(_currentNodeId);
            RefreshNodePanel();
            return;
        }

        // 已开始：二次确认
        float p =
            n.Status == NodeStatus.Investigating ? n.InvestigateProgress :
            n.Status == NodeStatus.Containing ? n.ContainProgress :
            0f;

        int pct = Mathf.RoundToInt(p * 100f);

        ShowConfirm(
            "强制撤回",
            $"将导致当前任务失败并清空进度。\n\n节点：{n.Name}\n任务：{n.Status}  {pct}%\n干员：{(string.IsNullOrEmpty(n.AssignedAgentId) ? "-" : n.AssignedAgentId)}\n\n确认撤回？",
            onConfirm: () =>
            {
                GameController.I.ForceWithdraw(_currentNodeId);
                RefreshNodePanel();
            },
            confirmText: "撤回",
            cancelText: "取消"
        );
    }

    // ===================== Dispatch UI: OnInvestigate/OnContain =====================
    public void OnInvestigate()
    {
        if (!useAgentPicker)
        {
            AssignInvestigate("A1");
            return;
        }

        if (string.IsNullOrEmpty(_currentNodeId)) return;

        var n = GameController.I.GetNode(_currentNodeId);
        if (IsTaskStarted(n))
        {
            ShowInfo("任务进行中", "任务已开始，无法直接换人或切换任务类型。\n\n请先点击【强制撤回】，再重新派遣。");
            return;
        }

        ShowAgentPicker(AssignMode.Investigate);
    }

    public void OnContain()
    {
        if (!useAgentPicker)
        {
            AssignContain("A1");
            return;
        }

        if (string.IsNullOrEmpty(_currentNodeId)) return;

        var n = GameController.I.GetNode(_currentNodeId);
        if (IsTaskStarted(n))
        {
            ShowInfo("任务进行中", "任务已开始，无法直接换人或切换任务类型。\n\n请先点击【强制撤回】，再重新派遣。");
            return;
        }

        ShowAgentPicker(AssignMode.Contain);
    }

    // ========= 旧的示例：派遣按钮（保留兼容） =========
    public void AssignInvestigate_A1() => AssignInvestigate("A1");
    public void AssignInvestigate_A2() => AssignInvestigate("A2");
    public void AssignInvestigate_A3() => AssignInvestigate("A3");

    void AssignInvestigate(string agentId)
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;
        GameController.I.AssignInvestigate(_currentNodeId, agentId);
        RefreshNodePanel();
    }

    public void AssignContain_A1() => AssignContain("A1");
    public void AssignContain_A2() => AssignContain("A2");
    public void AssignContain_A3() => AssignContain("A3");

    void AssignContain(string agentId)
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;
        GameController.I.AssignContain(_currentNodeId, agentId);
        RefreshNodePanel();
    }

    // ===================== Agent Picker Impl =====================
    void EnsureAgentPickerCreated()
    {
        if (_agentPickerRoot != null) return;

        var parent = nodePanel ? nodePanel.transform : transform;

        _agentPickerRoot = new GameObject("AgentPicker",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));

        _agentPickerRoot.transform.SetParent(parent, false);
        _agentPickerRoot.SetActive(false);

        var rootRT = (RectTransform)_agentPickerRoot.transform;
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = pickerSize;
        rootRT.anchoredPosition = Vector2.zero;

        var bg = _agentPickerRoot.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.92f);
        bg.raycastTarget = true;

        // Title
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(_agentPickerRoot.transform, false);
        var titleRT = (RectTransform)titleGO.transform;
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.sizeDelta = new Vector2(0f, 80f);
        titleRT.anchoredPosition = new Vector2(0f, -10f);

        _pickerTitle = titleGO.GetComponent<TextMeshProUGUI>();
        _pickerTitle.fontSize = 48;
        _pickerTitle.alignment = TextAlignmentOptions.Center;
        _pickerTitle.text = "Select Agent";

        // Close button
        var closeGO = new GameObject("Close", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        closeGO.transform.SetParent(_agentPickerRoot.transform, false);
        var closeRT = (RectTransform)closeGO.transform;
        closeRT.anchorMin = new Vector2(1f, 1f);
        closeRT.anchorMax = new Vector2(1f, 1f);
        closeRT.pivot = new Vector2(1f, 1f);
        closeRT.sizeDelta = new Vector2(80f, 80f);
        closeRT.anchoredPosition = new Vector2(-10f, -10f);

        var closeImg = closeGO.GetComponent<Image>();
        closeImg.color = new Color(1f, 1f, 1f, 0.08f);

        var closeBtn = closeGO.GetComponent<Button>();
        closeBtn.onClick.AddListener(HideAgentPicker);

        var closeTxtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        closeTxtGO.transform.SetParent(closeGO.transform, false);
        var closeTxtRT = (RectTransform)closeTxtGO.transform;
        closeTxtRT.anchorMin = Vector2.zero;
        closeTxtRT.anchorMax = Vector2.one;
        closeTxtRT.sizeDelta = Vector2.zero;

        var closeTxt = closeTxtGO.GetComponent<TextMeshProUGUI>();
        closeTxt.text = "X";
        closeTxt.fontSize = 44;
        closeTxt.alignment = TextAlignmentOptions.Center;

        // Scroll View
        var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(_agentPickerRoot.transform, false);

        var scrollRT = (RectTransform)scrollGO.transform;
        scrollRT.anchorMin = new Vector2(0f, 0f);
        scrollRT.anchorMax = new Vector2(1f, 1f);
        scrollRT.pivot = new Vector2(0.5f, 0.5f);
        scrollRT.offsetMin = new Vector2(20f, 20f);
        scrollRT.offsetMax = new Vector2(-20f, -110f);

        var scrollImg = scrollGO.GetComponent<Image>();
        scrollImg.color = new Color(1f, 1f, 1f, 0.04f);
        scrollImg.raycastTarget = true;

        _pickerScroll = scrollGO.GetComponent<ScrollRect>();
        _pickerScroll.horizontal = false;
        _pickerScroll.movementType = ScrollRect.MovementType.Clamped;
        _pickerScroll.scrollSensitivity = 30;

        // Viewport
        var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        vpGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = (RectTransform)vpGO.transform;
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.sizeDelta = Vector2.zero;

        var vpImg = vpGO.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.02f);

        var mask = vpGO.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        _pickerScroll.viewport = vpRT;

        // Content
        var contentGO = new GameObject("Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));

        contentGO.transform.SetParent(vpGO.transform, false);
        _pickerContent = (RectTransform)contentGO.transform;
        _pickerContent.anchorMin = new Vector2(0f, 1f);
        _pickerContent.anchorMax = new Vector2(1f, 1f);
        _pickerContent.pivot = new Vector2(0.5f, 1f);
        _pickerContent.anchoredPosition = Vector2.zero;
        _pickerContent.sizeDelta = new Vector2(0f, 0f);

        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 12f;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        var csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _pickerScroll.content = _pickerContent;
    }

    void ShowAgentPicker(AssignMode mode)
    {
        EnsureAgentPickerCreated();
        _pickerMode = mode;

        if (_pickerTitle)
            _pickerTitle.text = mode == AssignMode.Investigate ? "Select Agent - Investigate" : "Select Agent - Contain";

        for (int i = _pickerContent.childCount - 1; i >= 0; i--)
            Destroy(_pickerContent.GetChild(i).gameObject);

        var ids = GetAgentIds();
        if (ids.Count == 0) ids.AddRange(fallbackAgentIds);

        foreach (var id in ids)
            CreateAgentButton(id);

        _agentPickerRoot.SetActive(true);
        _agentPickerRoot.transform.SetAsLastSibling();

        if (nodePanel && nodePanel.activeSelf)
            SetModalTop((RectTransform)nodePanel.transform);
    }

    void HideAgentPicker()
    {
        if (_agentPickerRoot) _agentPickerRoot.SetActive(false);
    }

    void CreateAgentButton(string agentId)
    {
        var info = BuildAgentDisplayInfo(agentId);

        var row = new GameObject($"Agent_{agentId}",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));

        row.transform.SetParent(_pickerContent, false);

        var le = row.GetComponent<LayoutElement>();
        le.preferredHeight = 120f;

        var img = row.GetComponent<Image>();
        img.raycastTarget = true;

        img.color = info.busyOtherNode
            ? new Color(1f, 0.6f, 0.6f, 0.10f)
            : new Color(1f, 1f, 1f, 0.10f);

        var btn = row.GetComponent<Button>();
        btn.interactable = !info.busyOtherNode;
        btn.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(_currentNodeId)) return;

            // 关键修复：先 TryAssign，成功才关闭；失败不关闭并提示
            if (_pickerMode == AssignMode.Investigate)
            {
                var res = GameController.I.TryAssignInvestigate(_currentNodeId, agentId);
                if (res.ok)
                {
                    HideAgentPicker();
                    RefreshNodePanel();
                }
                else
                {
                    ShowInfo("无法派遣", res.reason);
                }
            }
            else
            {
                var res = GameController.I.TryAssignContain(_currentNodeId, agentId);
                if (res.ok)
                {
                    HideAgentPicker();
                    RefreshNodePanel();
                }
                else
                {
                    ShowInfo("无法派遣", res.reason);
                }
            }
        });

        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(row.transform, false);

        var rt = (RectTransform)txtGO.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(20f, 10f);
        rt.offsetMax = new Vector2(-20f, -10f);

        var t = txtGO.GetComponent<TextMeshProUGUI>();
        t.richText = true;
        t.enableWordWrapping = true;
        t.fontSize = 34;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.text = $"{info.title}\n{info.detail}";
    }

    private struct AgentDisplayInfo
    {
        public string title;
        public string detail;
        public bool busyOtherNode;
    }

    AgentDisplayInfo BuildAgentDisplayInfo(string agentId)
    {
        var info = new AgentDisplayInfo
        {
            title = $"{agentId}  | <color=#88FF88>Idle</color>",
            detail = "Available",
            busyOtherNode = false
        };

        NodeState assigned = null;
        foreach (var n in EnumerateAllNodesSafe())
        {
            if (n == null) continue;
            if (string.IsNullOrEmpty(n.AssignedAgentId)) continue;
            if (n.AssignedAgentId != agentId) continue;

            if (n.Status == NodeStatus.Investigating || n.Status == NodeStatus.Containing)
            {
                assigned = n;
                break;
            }
        }

        if (assigned == null) return info;

        string status = assigned.Status.ToString();
        float p = assigned.Status == NodeStatus.Investigating ? assigned.InvestigateProgress : assigned.ContainProgress;
        int pct = Mathf.RoundToInt(p * 100f);

        bool busyHere = !string.IsNullOrEmpty(_currentNodeId) && assigned.Id == _currentNodeId;
        if (!HasMember(assigned, "Id") && !HasMember(assigned, "ID"))
            busyHere = false;

        string etaText = "--";
        if (TryGetDailyRate(assigned.Status, out float perDay) && perDay > 0.00001f)
        {
            int days = Mathf.CeilToInt(Mathf.Max(0f, 1f - p) / perDay);
            etaText = $"{days}d";
        }

        string nodeName = string.IsNullOrEmpty(assigned.Name) ? "(node)" : assigned.Name;

        if (busyHere)
        {
            info.title = $"<b>{agentId}</b>  | <color=#FFD966>Assigned Here</color>";
            info.detail = $"{nodeName} - {status} {pct}%  |  ETA {etaText}";
            info.busyOtherNode = false;
        }
        else
        {
            info.title = $"{agentId}  | <color=#FF8888>Busy</color>";
            info.detail = $"{nodeName} - {status} {pct}%  |  ETA {etaText}";
            info.busyOtherNode = true;
        }

        return info;
    }

    IEnumerable<NodeState> EnumerateAllNodesSafe()
    {
        System.Collections.IEnumerable obj = null;

        try
        {
            var gc = GameController.I;
            if (gc != null)
            {
                var m = gc.GetType().GetMethod("GetAllNodes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m != null && typeof(System.Collections.IEnumerable).IsAssignableFrom(m.ReturnType))
                    obj = m.Invoke(gc, null) as System.Collections.IEnumerable;
            }
        }
        catch { }

        if (obj != null)
        {
            foreach (var e in obj)
                if (e is NodeState ns) yield return ns;
            yield break;
        }

        object container = null;
        try
        {
            var state = GameController.I != null ? GameController.I.State : null;
            if (state != null)
            {
                var t = state.GetType();
                container =
                    GetMemberValue(t, state, "Nodes") ??
                    GetMemberValue(t, state, "NodeStates") ??
                    GetMemberValue(t, state, "AllNodes") ??
                    GetMemberValue(t, state, "nodes");
            }
        }
        catch { }

        if (container == null) yield break;

        if (container is System.Collections.IDictionary dict)
        {
            foreach (var v in dict.Values)
                if (v is NodeState ns) yield return ns;
            yield break;
        }

        if (container is System.Collections.IEnumerable en)
        {
            foreach (var e in en)
            {
                if (e is NodeState ns) { yield return ns; continue; }

                var et = e?.GetType();
                if (et != null && et.IsGenericType && et.Name.StartsWith("KeyValuePair"))
                {
                    var vp = et.GetProperty("Value");
                    if (vp != null)
                    {
                        var v = vp.GetValue(e);
                        if (v is NodeState ns2) yield return ns2;
                    }
                }
            }
        }
    }

    bool TryGetDailyRate(NodeStatus status, out float perDay)
    {
        perDay = 0f;

        string[] keys = status == NodeStatus.Investigating
            ? new[] { "InvestigatePerDay", "InvestigateRate", "InvestigateSpeed", "InvestigateProgressPerDay" }
            : new[] { "ContainPerDay", "ContainRate", "ContainSpeed", "ContainProgressPerDay" };

        object[] targets =
        {
            GameController.I,
            GameController.I != null ? GameController.I.State : null
        };

        foreach (var target in targets)
        {
            if (target == null) continue;
            var t = target.GetType();
            foreach (var k in keys)
            {
                var v = GetMemberValue(t, target, k);
                if (v == null) continue;

                if (v is float f) { perDay = f; return true; }
                if (v is double d) { perDay = (float)d; return true; }
                if (v is int i) { perDay = i; return true; }
            }
        }

        return false;
    }

    static bool HasMember(object obj, string name)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        return t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null
            || t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
    }

    List<string> GetAgentIds()
    {
        var result = new List<string>();

        try
        {
            var state = GameController.I != null ? GameController.I.State : null;
            if (state != null)
            {
                var t = state.GetType();
                object listObj =
                    GetMemberValue(t, state, "Agents") ??
                    GetMemberValue(t, state, "AgentIds") ??
                    GetMemberValue(t, state, "Staff") ??
                    GetMemberValue(t, state, "Personnel") ??
                    GetMemberValue(t, state, "Operatives");

                if (listObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var e in enumerable)
                    {
                        if (e == null) continue;
                        if (e is string s) result.Add(s);
                        else
                        {
                            var et = e.GetType();
                            var id = GetMemberValue(et, e, "Id") ?? GetMemberValue(et, e, "ID") ?? GetMemberValue(et, e, "Name");
                            if (id != null) result.Add(id.ToString());
                        }
                    }
                }
            }
        }
        catch { }

        if (result.Count == 0 && fallbackAgentIds != null)
            result.AddRange(fallbackAgentIds);

        var seen = new HashSet<string>();
        var uniq = new List<string>();
        foreach (var s in result)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (seen.Add(s)) uniq.Add(s);
        }
        return uniq;
    }

    static object GetMemberValue(Type t, object obj, string name)
    {
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f.GetValue(obj);
        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) return p.GetValue(obj);
        return null;
    }



    
}

// <NEWS_SCROLL_AGENT>
// Removed: UIPanelRootAgent (MonoBehaviour class name must match file name to survive domain reload)
// </NEWS_SCROLL_AGENT>

// </EXPORT_BLOCK>
