using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Modal")]
    [SerializeField] private GameObject dim;

    [Header("Node Panel (drag NodePanel root)")]
    [SerializeField] private GameObject nodePanel;     // 拖 NodePanel（父物体）
    [SerializeField] private TMP_Text nodeTitle;        // 拖 NodePanel/Panel/TItle
    [SerializeField] private TMP_Text nodeStatus;       // 拖 NodePanel/Panel/Status
    [SerializeField] private TMP_Text progressText;     // 拖 NodePanel/Panel/progressText

    [Header("Node Panel Buttons (bind in code; Inspector OnClick should be empty)")]
    [SerializeField] private Button investigateButton;  // 拖 NodePanel/Panel/InvestigateBT
    [SerializeField] private Button containButton;      // 拖 NodePanel/Panel/ContainBT
    [SerializeField] private Button closeNodeButton;    // 拖 NodePanel/Panel/CloseBT

    [Header("Event Panel")]
    [SerializeField] private EventPanel eventPanel;     // 拖场景里的 EventPanel 实例

    [Header("News Panel")]
    [SerializeField] private NewsPanel newsPanel;       // 拖场景里的 NewsPanel 实例
    [Header("News Panel Buttons (bind in code)")]
    [SerializeField] private Button closeNewsButton; // 拖 NewsPanel 里的关闭按钮（可选，没拖也会自动找）


    [Header("Confirm/Withdraw (Milestone 1; optional in Milestone 0)")]
    [SerializeField] private ConfirmDialog confirmDialog;
    [SerializeField] private Button withdrawButton;
    [SerializeField] private TMP_Text withdrawButtonLabel;


    [SerializeField] private Button dimButton;


    // ===================== Agent Picker (existing approach) =====================
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

    [Header("Agent Picker Prefab (new)")]
    [SerializeField] private AgentPickerView agentPickerPrefab;

    private AgentPickerView _agentPicker;






    // ===========================================================================

    private string _currentNodeId;

    private void Awake()
    {
        I = this;

        if (!ValidateBindings())
        {
            enabled = false;
            return;
        }

        AutoWireNodePanelButtonsIfMissing();
        BindNodePanelButtonsInCode();
        AutoWireNewsPanelButtonsIfMissing();
        BindNewsPanelButtonsInCode();

        if (!dimButton && dim) dimButton = dim.GetComponent<Button>();
        if (dimButton)
        {
            dimButton.onClick.RemoveAllListeners();
            dimButton.onClick.AddListener(CloseAll);
        }
        else
        {
            Debug.LogWarning("[UIPanelRoot] Dim has no Button; click-to-close disabled.", this);
        }


        ResetUI();
        EnsureAgentPickerPrefab();



    }



    private void OnEnable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged += OnGameStateChanged;

        OnGameStateChanged();
    }

    private void OnDisable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged -= OnGameStateChanged;
    }

    bool ValidateBindings()
    {
        bool ok = true;

        void Req(object o, string name)
        {
            if (o == null)
            {
                Debug.LogError($"[UIPanelRoot] Missing binding: {name}", this);
                ok = false;
            }
        }

        Req(dim, nameof(dim));
        Req(nodePanel, nameof(nodePanel));
        Req(nodeTitle, nameof(nodeTitle));
        Req(nodeStatus, nameof(nodeStatus));
        Req(progressText, nameof(progressText));
        Req(eventPanel, nameof(eventPanel));
        Req(newsPanel, nameof(newsPanel));

        // Milestone 0: confirm/withdraw 可为空（先不硬要求）
        if (!confirmDialog) Debug.LogWarning("[UIPanelRoot] confirmDialog not bound (OK in Milestone 0)", this);
        if (!withdrawButton) Debug.LogWarning("[UIPanelRoot] withdrawButton not bound (OK in Milestone 0)", this);

        return ok;
    }

    void AutoWireNodePanelButtonsIfMissing()
    {
        // 场景层级：NodePanel/Panel/InvestigateBT, ContainBT, CloseBT :contentReference[oaicite:2]{index=2}
        if (!nodePanel) return;

        var panel = nodePanel.transform.Find("Panel");
        if (!panel) return;

        if (!investigateButton)
        {
            var t = panel.Find("InvestigateBT");
            if (t) investigateButton = t.GetComponent<Button>();
        }

        if (!containButton)
        {
            var t = panel.Find("ContainBT");
            if (t) containButton = t.GetComponent<Button>();
        }

        if (!closeNodeButton)
        {
            var t = panel.Find("CloseBT");
            if (t) closeNodeButton = t.GetComponent<Button>();
        }
    }

    void BindNodePanelButtonsInCode()
    {
        if (investigateButton)
        {
            investigateButton.onClick.RemoveAllListeners();
            investigateButton.onClick.AddListener(OnInvestigate);
        }
        else Debug.LogWarning("[UIPanelRoot] investigateButton not bound (expected NodePanel/Panel/InvestigateBT)", this);

        if (containButton)
        {
            containButton.onClick.RemoveAllListeners();
            containButton.onClick.AddListener(OnContain);
        }
        else Debug.LogWarning("[UIPanelRoot] containButton not bound (expected NodePanel/Panel/ContainBT)", this);

        if (closeNodeButton)
        {
            closeNodeButton.onClick.RemoveAllListeners();
            closeNodeButton.onClick.AddListener(CloseNode);
        }
        else Debug.LogWarning("[UIPanelRoot] closeNodeButton not bound (expected NodePanel/Panel/CloseBT)", this);
    }
    void AutoWireNewsPanelButtonsIfMissing()
    {
        if (closeNewsButton != null) return;
        if (!newsPanel) return;

        // 尝试用“名字包含 close / x”的规则自动找一个按钮作为关闭
        var buttons = newsPanel.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            if (!b) continue;
            var n = b.name;
            if (n.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("x", StringComparison.OrdinalIgnoreCase) == 0 ||   // "X"
                string.Equals(n, "CloseBT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "Close", StringComparison.OrdinalIgnoreCase))
            {
                closeNewsButton = b;
                break;
            }
        }
    }

    void BindNewsPanelButtonsInCode()
    {
        if (!closeNewsButton)
        {
            Debug.LogWarning("[UIPanelRoot] closeNewsButton not bound (NewsPanel close will not work). Drag it in Inspector or rename it to CloseBT.", this);
            return;
        }

        closeNewsButton.onClick.RemoveAllListeners();
        closeNewsButton.onClick.AddListener(CloseNews);
    }

    void OnGameStateChanged()
    {
        // 如果节点面板开着，刷新它
        if (!string.IsNullOrEmpty(_currentNodeId) && nodePanel && nodePanel.activeSelf)
            RefreshNodePanel();

        // 统一“弹事件”的入口：避免 HUD 多次 Refresh 导致重复开
        TryAutoOpenEvent();
    }

    void TryAutoOpenEvent()
    {
        if (GameController.I == null) return;

        // 有别的 modal 在显示时不自动弹事件（避免抢焦点）
        if (IsAnyModalOpen())
            return;

        var list = GameController.I.State.PendingEvents;
        if (list == null || list.Count == 0) return;

        OpenEventIfAny();
    }

    bool IsAnyModalOpen()
    {
        if (confirmDialog && confirmDialog.gameObject.activeSelf) return true;
        if (eventPanel && eventPanel.gameObject.activeSelf) return true;
        if (newsPanel && newsPanel.gameObject.activeSelf) return true;
        if (nodePanel && nodePanel.activeSelf) return true;
        return false;
    }

    private void ResetUI()
    {
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);
        if (dim) dim.SetActive(false);

        _currentNodeId = null;
        HideAgentPicker();
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

        // topPanel 永远最上层
        topPanel.SetAsLastSibling();

        // dim 永远在 topPanel 下一层，避免盖住 topPanel
        int topIndex = topPanel.GetSiblingIndex();
        dim.transform.SetSiblingIndex(Mathf.Max(0, topIndex - 1));
    }

    RectTransform GetTopModal()
    {
        if (confirmDialog && confirmDialog.gameObject.activeSelf) return (RectTransform)confirmDialog.transform;
        if (eventPanel && eventPanel.gameObject.activeSelf) return (RectTransform)eventPanel.transform;
        if (newsPanel && newsPanel.gameObject.activeSelf) return (RectTransform)newsPanel.transform;
        if (nodePanel && nodePanel.activeSelf) return (RectTransform)nodePanel.transform;
        return null;
    }

    // ========= Node Panel =========
    public void OpenNode(string nodeId)
    {
        _currentNodeId = nodeId;

        if (nodePanel) nodePanel.SetActive(true);
        SetModalTop((RectTransform)nodePanel.transform);

        RefreshNodePanel();
    }

    public void RefreshNodePanel()
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;
        if (GameController.I == null) return;

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
    }

    public void CloseNode()
    {
        HideAgentPicker();

        if (nodePanel) nodePanel.SetActive(false);
        _currentNodeId = null;

        // 恢复到当前仍开着的 top modal；否则关 dim
        SetModalTop(GetTopModal());
    }

    // ========= Event Panel =========
    public void OpenEventIfAny()
    {
        if (GameController.I == null) return;

        var list = GameController.I.State.PendingEvents;
        if (list == null || list.Count == 0) return;

        if (!eventPanel)
        {
            Debug.LogError("[UIPanelRoot] eventPanel is NULL (Inspector 没拖场景实例)", this);
            return;
        }

        // 幂等：已经开着就不重复 Show
        if (eventPanel.gameObject.activeSelf) return;

        eventPanel.gameObject.SetActive(true);
        eventPanel.Show(list[0]);

        SetModalTop((RectTransform)eventPanel.transform);
    }

    public void CloseEvent()
    {
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        SetModalTop(GetTopModal());
    }

    // ========= News Panel =========
    public void OpenNews()
    {
        if (!newsPanel)
        {
            Debug.LogError("[UIPanelRoot] newsPanel is NULL（Inspector 没拖）", this);
            return;
        }

        newsPanel.Show();
        SetModalTop((RectTransform)newsPanel.transform);
    }

    public void CloseNews()
    {
        if (newsPanel) newsPanel.Hide();
        SetModalTop(GetTopModal());
    }

    // ========= 统一关闭（可绑在 dim 点击上） =========
    public void CloseAll()
    {
        HideAgentPicker();

        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);
        if (_agentPicker) _agentPicker.Hide();

        _currentNodeId = null;
        SetModalTop(null);
    }

    // ===================== Dispatch UI: OnInvestigate/OnContain =====================
    public void OnInvestigate()
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        EnsureAgentPickerPrefab();
        if (_agentPicker)
        {
            OpenAgentPicker(AgentPickerView.Mode.Investigate);
            return;
        }

        // 兜底：旧逻辑
        AssignInvestigate("A1");
    }

    public void OnContain()
    {
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        EnsureAgentPickerPrefab();
        if (_agentPicker)
        {
            OpenAgentPicker(AgentPickerView.Mode.Contain);
            return;
        }

        AssignContain("A1");
    }


    // 旧兼容：如果你场景里还保留了 A1/A2/A3 按钮（目前是 Inactive）:contentReference[oaicite:3]{index=3}
    public void AssignInvestigate_A1() => AssignInvestigate("A1");
    public void AssignInvestigate_A2() => AssignInvestigate("A2");
    public void AssignInvestigate_A3() => AssignInvestigate("A3");

    public void AssignContain_A1() => AssignContain("A1");
    public void AssignContain_A2() => AssignContain("A2");
    public void AssignContain_A3() => AssignContain("A3");

    void OpenAgentPicker(AgentPickerView.Mode mode)
    {
        if (!_agentPicker) return;

        // 这里先用 State.Agents。你当前 GameState 里有 Agents 列表。:contentReference[oaicite:4]{index=4}
        var agents = GameController.I != null ? GameController.I.State.Agents : new List<Core.AgentState>();

        // preSelected：先用当前节点 AssignedAgentId（现结构单人），后续你加多选字段再改成列表
        var n = GameController.I.GetNode(_currentNodeId);
        var pre = new List<string>();
        if (n != null && !string.IsNullOrEmpty(n.AssignedAgentId)) pre.Add(n.AssignedAgentId);

        bool IsBusyOtherNode(string agentId)
        {
            // 现阶段：用 NodeState.AssignedAgentId 判忙（单人）。后面做多人任务时这里会升级。
            foreach (var node in GameController.I.State.Nodes)
            {
                if (node == null) continue;
                if (node.Id == _currentNodeId) continue;
                if (node.AssignedAgentId == agentId &&
                    (node.Status == Core.NodeStatus.Investigating || node.Status == Core.NodeStatus.Containing))
                    return true;
            }
            return false;
        }

        _agentPicker.Show(
            mode: mode,
            nodeId: _currentNodeId,
            agents: agents,
            preSelected: pre,
            isBusyOtherNode: IsBusyOtherNode,
            onConfirm: (selectedIds) =>
            {
                // 重要：你现在 NodeState 只支持单人，所以这里先取第一个，保证不破现有 Sim/进度逻辑
                var pick = selectedIds != null && selectedIds.Count > 0 ? selectedIds[0] : null;
                if (string.IsNullOrEmpty(pick)) return;

                if (mode == AgentPickerView.Mode.Investigate) AssignInvestigate(pick);
                else AssignContain(pick);
            },
            onCancel: () =>
            {
                // 取消：什么都不做
            });

        // Picker 作为 top modal
        SetModalTop((RectTransform)_agentPicker.transform);
    }



    void AssignInvestigate(string agentId)
    {
        if (GameController.I == null || string.IsNullOrEmpty(_currentNodeId)) return;
        GameController.I.AssignInvestigate(_currentNodeId, agentId);
        RefreshNodePanel();
    }

    void AssignContain(string agentId)
    {
        if (GameController.I == null || string.IsNullOrEmpty(_currentNodeId)) return;
        GameController.I.AssignContain(_currentNodeId, agentId);
        RefreshNodePanel();
    }

    // ===================== Agent Picker Impl (simple, stable) =====================

    void EnsureAgentPickerPrefab()
    {
        if (_agentPicker) return;
        if (!agentPickerPrefab)
        {
            Debug.LogWarning("[UIPanelRoot] agentPickerPrefab not bound; fallback to old picker.", this);
            return;
        }

        // 放到 nodePanel 同级（或放到 UIPanelRoot 下都行；建议放 Canvas 的 Panels 容器下）
        var parent = nodePanel ? nodePanel.transform.parent : transform;
        _agentPicker = Instantiate(agentPickerPrefab, parent);
        _agentPicker.gameObject.SetActive(false);
    }



    void ShowAgentPicker(AssignMode mode)
    {
        _pickerMode = mode;

        if (_pickerTitle)
            _pickerTitle.text = mode == AssignMode.Investigate ? "Select Agent - Investigate" : "Select Agent - Contain";

        // 清空旧条目
        for (int i = _pickerContent.childCount - 1; i >= 0; i--)
            Destroy(_pickerContent.GetChild(i).gameObject);

        var ids = GetAgentIds();
        foreach (var id in ids)
            CreateAgentButton(id);

        _agentPickerRoot.SetActive(true);
        _agentPickerRoot.transform.SetAsLastSibling();

        // NodePanel 仍当 top
        if (nodePanel && nodePanel.activeSelf)
            SetModalTop((RectTransform)nodePanel.transform);
    }

    void HideAgentPicker()
    {
        if (_agentPickerRoot) _agentPickerRoot.SetActive(false);
    }

    List<string> GetAgentIds()
    {
        var result = new List<string>();

        try
        {
            var s = GameController.I != null ? GameController.I.State : null;
            if (s != null && s.Agents != null && s.Agents.Count > 0)
                result.AddRange(s.Agents.Select(a => a.Id));
        }
        catch { /* ignore */ }

        if (result.Count == 0 && fallbackAgentIds != null)
            result.AddRange(fallbackAgentIds);

        // 去重
        var seen = new HashSet<string>();
        var uniq = new List<string>();
        foreach (var id in result)
            if (!string.IsNullOrEmpty(id) && seen.Add(id)) uniq.Add(id);

        return uniq;
    }

    void CreateAgentButton(string agentId)
    {
        var row = new GameObject($"Agent_{agentId}",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));

        row.transform.SetParent(_pickerContent, false);

        var le = row.GetComponent<LayoutElement>();
        le.preferredHeight = 110f;

        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);

        var btn = row.GetComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            HideAgentPicker();
            if (_pickerMode == AssignMode.Investigate) AssignInvestigate(agentId);
            else AssignContain(agentId);
        });

        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(row.transform, false);

        var rt = (RectTransform)txtGO.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(20f, 10f);
        rt.offsetMax = new Vector2(-20f, -10f);

        var t = txtGO.GetComponent<TextMeshProUGUI>();
        t.enableWordWrapping = true;
        t.fontSize = 32;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.text = agentId;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void LateUpdate()
    {
        // 轻量自检：有 modal 开着时 dim 不应是最后一个 sibling
        var top = GetTopModal();
        if (top == null) return;

        if (!dim || !dim.activeSelf)
        {
            Debug.LogWarning("[Modal] Expected dim active but it is not", this);
            return;
        }

        if (dim.transform.GetSiblingIndex() == dim.transform.parent.childCount - 1)
            Debug.LogWarning("[Modal] dim is last sibling (may cover top panel)", this);
    }
#endif
}
