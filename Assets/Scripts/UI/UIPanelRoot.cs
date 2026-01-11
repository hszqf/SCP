// Canvas-maintained file: UI/UIPanelRoot
// Source: Assets/Scripts/UI/UIPanelRoot.cs
// Updated for rule-set: 预定占用
// - Use TryAssign* (not Assign*) so UI respects "预定后只能取消再改派".
// - When assignment fails, show ConfirmDialog info.

using System;
using System.Collections.Generic;
using Core;
using UnityEngine;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Prefabs (请把 Assets/Prefabs/UI 下的文件拖进来)")]
    [SerializeField] private HUD hudPrefab; // HUD Prefab
    [SerializeField] private NodePanelView nodePanelPrefab;
    [SerializeField] private EventPanel eventPanelPrefab;
    [SerializeField] private NewsPanel newsPanelPrefab;
    [SerializeField] private AgentPickerView agentPickerPrefab;
    [SerializeField] private ConfirmDialog confirmDialogPrefab;

    // --- 运行时实例 (自动生成) ---
    private HUD _hud;
    private NodePanelView _nodePanel;
    private EventPanel _eventPanel;
    private NewsPanel _newsPanel;
    private AgentPickerView _agentPicker;
    private ConfirmDialog _confirmDialog;

    private string _currentNodeId;

    private void Awake()
    {
        I = this;
    }

    private void Start()
    {
        InitHUD();
    }

    private void OnEnable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged -= OnGameStateChanged;
    }

    void InitHUD()
    {
        if (_hud) return;
        if (hudPrefab)
        {
            _hud = Instantiate(hudPrefab, transform);
            // HUD should be at the bottom so panels can cover it
            _hud.transform.SetAsFirstSibling();
        }
    }

    void OnGameStateChanged()
    {
        // 1. If node panel is open, refresh it
        if (_nodePanel != null && _nodePanel.gameObject.activeSelf)
            _nodePanel.Refresh();

        // 2. Try auto-open event
        TryAutoOpenEvent();
    }

    // ================== NODE PANEL ==================

    public void OpenNode(string nodeId)
    {
        _currentNodeId = nodeId;

        EnsureNodePanel();
        _nodePanel.Show(nodeId);
    }

    public void CloseNode()
    {
        if (_nodePanel) _nodePanel.Hide();
        _currentNodeId = null;
    }

    public void RefreshNodePanel() // 兼容旧接口
    {
        if (_nodePanel && _nodePanel.gameObject.activeSelf) _nodePanel.Refresh();
    }

    void EnsureNodePanel()
    {
        if (_nodePanel) return;
        if (!nodePanelPrefab) { Debug.LogError("NodePanelPrefab 未配置！"); return; }

        _nodePanel = Instantiate(nodePanelPrefab, transform);
        // Inject callbacks
        _nodePanel.Init(
            onInvestigate: () => OpenPicker(AgentPickerView.Mode.Investigate),
            onContain: () => OpenPicker(AgentPickerView.Mode.Contain),
            onClose: () => CloseNode()
        );
    }

    // ================== CONFIRM DIALOG ==================

    void EnsureConfirmDialog()
    {
        if (_confirmDialog) return;
        if (!confirmDialogPrefab) return;
        _confirmDialog = Instantiate(confirmDialogPrefab, transform);
    }

    void ShowInfo(string title, string message)
    {
        EnsureConfirmDialog();
        if (_confirmDialog)
        {
            _confirmDialog.ShowInfo(title, message);
            _confirmDialog.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog prefab not set. Info: {title} / {message}");
        }
    }

    // ================== AGENT PICKER ==================

    void OpenPicker(AgentPickerView.Mode mode)
    {
        if (!_agentPicker)
        {
            if (!agentPickerPrefab) return;
            _agentPicker = Instantiate(agentPickerPrefab, transform);
        }

        // Prepare data
        var agents = GameController.I.State.Agents;
        var n = GameController.I.GetNode(_currentNodeId);
        var pre = new List<string>();
        if (n != null && n.AssignedAgentIds != null) pre.AddRange(n.AssignedAgentIds);

        _agentPicker.transform.SetAsLastSibling();
        _agentPicker.Show(
            mode,
            _currentNodeId,
            agents,
            pre,
            isBusyOtherNode: (aid) => GameControllerTaskExt.AreAgentsBusy(GameController.I, new List<string> { aid }, _currentNodeId),
            onConfirm: (ids) =>
            {
                // IMPORTANT: Use TryAssign so "预定占用" and "撤退/取消" rules are enforced in one place.
                GameControllerTaskExt.AssignResult res;
                if (mode == AgentPickerView.Mode.Investigate)
                    res = GameController.I.TryAssignInvestigate(_currentNodeId, ids);
                else
                    res = GameController.I.TryAssignContain(_currentNodeId, ids);

                if (!res.ok)
                {
                    ShowInfo("派遣失败", res.reason);
                    return;
                }

                RefreshNodePanel();
            },
            onCancel: () => { },
            multiSelect: true
        );
    }

    // ================== OTHERS ==================

    public void OpenNews()
    {
        if (!_newsPanel && newsPanelPrefab) _newsPanel = Instantiate(newsPanelPrefab, transform);
        if (_newsPanel)
        {
            _newsPanel.Show();
            _newsPanel.transform.SetAsLastSibling();
        }
    }

    void TryAutoOpenEvent()
    {
        // If a higher priority panel is shown, do not open events
        if (_agentPicker && _agentPicker.IsShown) return;

        var list = GameController.I.State.PendingEvents;
        if (list == null || list.Count == 0) return;

        if (!_eventPanel && eventPanelPrefab) _eventPanel = Instantiate(eventPanelPrefab, transform);

        // Only open when currently closed
        if (_eventPanel && !_eventPanel.gameObject.activeSelf)
        {
            _eventPanel.gameObject.SetActive(true);
            _eventPanel.transform.SetAsLastSibling();
            _eventPanel.Show(list[0]);
        }
    }

    public void CloseEvent()
    {
        if (_eventPanel) _eventPanel.gameObject.SetActive(false);
    }

    // ================== COMPATIBILITY ==================

    public void AssignInvestigate_A1() => Debug.Log("Old button clicked");
    public void AssignInvestigate_A2() => Debug.Log("Old button clicked");
    public void AssignInvestigate_A3() => Debug.Log("Old button clicked");
    public void AssignContain_A1() => Debug.Log("Old button clicked");
    public void AssignContain_A2() => Debug.Log("Old button clicked");
    public void AssignContain_A3() => Debug.Log("Old button clicked");

    public void CloseAll()
    {
        CloseNode();
        CloseEvent();
        if (_newsPanel) _newsPanel.Hide();
        if (_agentPicker) _agentPicker.Hide();
        if (_confirmDialog) _confirmDialog.Hide();
    }
}
