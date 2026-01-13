// Canvas-maintained file: UI/UIPanelRoot
// Source: Assets/Scripts/UI/UIPanelRoot.cs
// Version: UI_UIPanelRoot_v2_20260114a
// Updated for N-task backend:
// - Each click on 调查/收容 creates a NEW task (NodeTask) and opens AgentPicker bound to that taskId.
// - This enables multiple investigate tasks and multiple contain tasks (one per containable).
// - On picker cancel (or close), the newly created task is cancelled to avoid leaving invisible active tasks.
//
// Notes:
// - Busy check still uses GameControllerTaskExt.AreAgentsBusy (global task scan).
// - Contain requires node.Containables.Count > 0; target selection picks a containable not already targeted by an active contain task when possible.
// - UI still uses ConfirmDialog for info prompts.

using System;
using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private GameObject managePanelPrefab; // 管理面板 Prefab（你新建的管理界面）

    // --- 运行时实例 (自动生成) ---
    private HUD _hud;
    private NodePanelView _nodePanel;
    private EventPanel _eventPanel;
    private NewsPanel _newsPanel;
    private AgentPickerView _agentPicker;
    private ConfirmDialog _confirmDialog;
    private GameObject _managePanel;

    private string _currentNodeId;
    private string _manageNodeId; // 当前打开的管理面板所对应的节点（与 NodePanel 的当前节点解耦）

    public string CurrentNodeId => _currentNodeId;
    public string ManageNodeId => _manageNodeId;

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
        // 注意：不要清理 _manageNodeId。管理面板可能仍在打开，需要保持其上下文。
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
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        if (!_agentPicker)
        {
            if (!agentPickerPrefab) return;
            _agentPicker = Instantiate(agentPickerPrefab, transform);
        }

        var node = GameController.I.GetNode(_currentNodeId);
        if (node == null)
        {
            ShowInfo("派遣失败", "节点不存在");
            return;
        }

        // Create a NEW task for this dispatch action.
        NodeTask createdTask = null;
        if (mode == AgentPickerView.Mode.Investigate)
        {
            createdTask = GameController.I.CreateInvestigateTask(_currentNodeId);
        }
        else
        {
            // Contain requires containables.
            if (node.Containables == null || node.Containables.Count == 0)
            {
                ShowInfo("派遣失败", "未发现可收容目标：请先完成调查产出收容物");
                return;
            }

            string targetId = PickNextContainableId(node);
            createdTask = GameController.I.CreateContainTask(_currentNodeId, targetId);
        }

        if (createdTask == null)
        {
            ShowInfo("派遣失败", "创建任务失败");
            return;
        }

        string taskId = createdTask.Id;

        // Prepare data
        var agents = GameController.I.State.Agents;
        var pre = new List<string>(); // new task => no preselected agents

        _agentPicker.transform.SetAsLastSibling();
        _agentPicker.Show(
            mode,
            _currentNodeId,
            agents,
            pre,
            // Busy means: already assigned to ANY active task (any node) OR managing ANY anomaly (any node).
            // Do NOT ignore current node here; otherwise agents could be double-assigned within the same node.
            isBusyOtherNode: (aid) => GameControllerTaskExt.AreAgentsBusy(GameController.I, new List<string> { aid }, null),
            onConfirm: (ids) =>
            {
                if (ids == null || ids.Count == 0)
                {
                    ShowInfo("派遣失败", "未选择干员");
                    return;
                }

                // Global busy check (multi-select)
                if (GameControllerTaskExt.AreAgentsBusy(GameController.I, ids, null))
                {
                    ShowInfo("派遣失败", "部分干员正在其他任务执行中");
                    return;
                }

                GameController.I.AssignTask(taskId, ids);
                RefreshNodePanel();
            },
            onCancel: () =>
            {
                // Cancel the newly created task if the user backs out of picker.
                GameController.I.CancelOrRetreatTask(taskId);
                RefreshNodePanel();
            },
            multiSelect: true
        );
    }

    // ================== MANAGE PANEL ==================

    void EnsureManagePanel()
    {
        if (_managePanel) return;
        if (!managePanelPrefab)
        {
            Debug.LogWarning("[UIPanelRoot] managePanelPrefab 未配置！");
            return;
        }
        _managePanel = Instantiate(managePanelPrefab, transform);
        _managePanel.SetActive(false);
    }

    // 由 NodePanel 的“管理”按钮调用（推荐：传入当前节点 id）
    public void OpenManage(string nodeId)
    {
        _manageNodeId = nodeId;
        OpenManage();
    }

    // 兼容旧调用：若未传 nodeId，则默认使用当前打开的节点
    public void OpenManage()
    {
        if (string.IsNullOrEmpty(_manageNodeId)) _manageNodeId = _currentNodeId;

        EnsureManagePanel();
        if (_managePanel)
        {
            _managePanel.SetActive(true);
            _managePanel.transform.SetAsLastSibling();
        }
    }

    public void CloseManage()
    {
        if (_managePanel) _managePanel.SetActive(false);
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
        CloseManage();
        if (_newsPanel) _newsPanel.Hide();

        // If picker is being closed implicitly, also cancel its newly created task via onCancel callback.
        // (AgentPickerView.Hide() is expected to invoke onCancel internally; if not, worst case the task remains Active+empty and will be ignored by Sim.)
        if (_agentPicker) _agentPicker.Hide();

        if (_confirmDialog) _confirmDialog.Hide();
    }

    // ================== HELPERS ==================

    // Pick a containable that is not already targeted by an active containment task when possible.
    string PickNextContainableId(NodeState node)
    {
        if (node == null || node.Containables == null || node.Containables.Count == 0) return null;

        var activeTargets = (node.Tasks == null)
            ? new HashSet<string>()
            : new HashSet<string>(node.Tasks
                .Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain)
                .Select(t => t.TargetContainableId)
                .Where(x => !string.IsNullOrEmpty(x)));

        foreach (var c in node.Containables)
        {
            if (c == null) continue;
            if (!activeTargets.Contains(c.Id)) return c.Id;
        }

        // Fallback: allow multiple tasks for same containable if all are already targeted.
        return node.Containables[0].Id;
    }
}
