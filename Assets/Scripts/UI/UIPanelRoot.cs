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
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
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
    [SerializeField] private RecruitPanel recruitPanelPrefab;

    // --- 运行时实例 (自动生成) ---
    private HUD _hud;
    private NodePanelView _nodePanel;
    private EventPanel _eventPanel;
    private NewsPanel _newsPanel;
    private AgentPickerView _agentPicker;
    private ConfirmDialog _confirmDialog;
    private GameObject _managePanel;
    private AnomalyManagePanel _managePanelView;
    private RecruitPanel _recruitPanel;

    private string _currentNodeId;
    private string _manageNodeId; // 当前打开的管理面板所对应的节点（与 NodePanel 的当前节点解耦）
    private string _pickerTaskId;
    private bool _suppressAutoOpenEvent;

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
        ForceCancelPickerIfNeeded(true);
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
            onInvestigate: () => OpenInvestigateAssignPanel(),
            onContain: () => OpenContainAssignPanel(),
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

    // ================== ASSIGNMENT PANEL (Investigate / Contain) ==================

    void OpenInvestigateAssignPanel()
    {
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        EnsureManagePanel();
        if (!_managePanelView) return;

        var gc = GameController.I;
        var node = gc.GetNode(_currentNodeId);
        if (node == null)
        {
            ShowInfo("派遣失败", "节点不存在");
            return;
        }

        var registry = DataRegistry.Instance;
        var targets = BuildInvestigateTargets(node, registry, gc.State?.NewsLog);
        var (slotsMin, slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Investigate, 1, int.MaxValue);

        _manageNodeId = _currentNodeId;
        if (_managePanel) _managePanel.SetActive(true);
        _managePanel.transform.SetAsLastSibling();
        _managePanelView.ShowGeneric(
            header: $"Investigate | {_currentNodeId}",
            hint: "选择新闻线索（可选）并派遣干员",
            targets: targets,
            agentSlotsMin: slotsMin,
            agentSlotsMax: slotsMax,
            onConfirm: (targetId, agentIds) =>
            {
                if (agentIds == null || agentIds.Count == 0)
                {
                    ShowInfo("派遣失败", "未选择干员");
                    return;
                }

                if (GameControllerTaskExt.AreAgentsBusy(gc, agentIds, null))
                {
                    ShowInfo("派遣失败", "部分干员正在其他任务执行中");
                    return;
                }

                var task = gc.CreateInvestigateTask(_currentNodeId);
                if (task == null)
                {
                    ShowInfo("派遣失败", "创建任务失败");
                    return;
                }

                string targetNewsId = string.IsNullOrEmpty(targetId) ? null : targetId;
                task.TargetNewsId = targetNewsId;

                string sourceAnomalyId = null;
                if (!string.IsNullOrEmpty(targetNewsId))
                {
                    var news = gc.State?.NewsLog?.FirstOrDefault(n => n != null && n.Id == targetNewsId);
                    sourceAnomalyId = news?.SourceAnomalyId;
                    task.SourceAnomalyId = sourceAnomalyId;
                    Debug.Log($"[InvestigateBindNews] taskId={task.Id} newsId={targetNewsId} srcAnom={sourceAnomalyId} nodeId={_currentNodeId}");
                }

                gc.AssignTask(task.Id, agentIds);
                _managePanelView.Hide();
                RefreshNodePanel();
            },
            modeLabel: "Investigate"
        );
    }

    void OpenContainAssignPanel()
    {
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(_currentNodeId)) return;

        EnsureManagePanel();
        if (!_managePanelView) return;

        var gc = GameController.I;
        var node = gc.GetNode(_currentNodeId);
        if (node == null)
        {
            ShowInfo("派遣失败", "节点不存在");
            return;
        }

        var registry = DataRegistry.Instance;
        var targets = BuildContainTargets(node, registry);
        var (slotsMin, slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Contain, 1, int.MaxValue);

        string hint = targets.Count == 0
            ? "无已确认异常：请先调查（随意或针对新闻）以发现异常"
            : "选择要收容的异常并派遣干员";

        _manageNodeId = _currentNodeId;
        if (_managePanel) _managePanel.SetActive(true);
        _managePanel.transform.SetAsLastSibling();
        _managePanelView.ShowGeneric(
            header: $"Contain | {_currentNodeId}",
            hint: hint,
            targets: targets,
            agentSlotsMin: slotsMin,
            agentSlotsMax: slotsMax,
            onConfirm: (targetAnomalyId, agentIds) =>
            {
                if (string.IsNullOrEmpty(targetAnomalyId))
                {
                    ShowInfo("派遣失败", "未选择收容目标");
                    return;
                }

                if (agentIds == null || agentIds.Count == 0)
                {
                    ShowInfo("派遣失败", "未选择干员");
                    return;
                }

                if (GameControllerTaskExt.AreAgentsBusy(gc, agentIds, null))
                {
                    ShowInfo("派遣失败", "部分干员正在其他任务执行中");
                    return;
                }

                string containableId = EnsureContainableForAnomaly(node, registry, targetAnomalyId);
                var task = gc.CreateContainTask(_currentNodeId, containableId);
                if (task == null)
                {
                    ShowInfo("派遣失败", "创建任务失败");
                    return;
                }

                task.SourceAnomalyId = targetAnomalyId;
                gc.AssignTask(task.Id, agentIds);
                _managePanelView.Hide();
                RefreshNodePanel();
            },
            modeLabel: "Contain"
        );
    }

    private List<AnomalyManagePanel.TargetEntry> BuildInvestigateTargets(NodeState node, DataRegistry registry, List<NewsInstance> newsLog)
    {
        var targets = new List<AnomalyManagePanel.TargetEntry>
        {
            new AnomalyManagePanel.TargetEntry
            {
                id = "",
                title = "随意调查",
                subtitle = "不针对任何新闻（较慢）",
                disabled = false
            }
        };

        if (newsLog == null) return targets;

        foreach (var news in newsLog)
        {
            if (news == null) continue;
            if (!string.Equals(news.NodeId, node.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (news.IsResolved) continue;

            var def = registry.GetNewsDefById(news.NewsDefId);
            string title = def?.title ?? news.NewsDefId ?? "";
            string subtitle = !string.IsNullOrEmpty(news.SourceAnomalyId)
                ? $"线索指向：{news.SourceAnomalyId}"
                : "线索来源未知";

            targets.Add(new AnomalyManagePanel.TargetEntry
            {
                id = news.Id,
                title = title,
                subtitle = subtitle,
                disabled = false
            });
        }

        return targets;
    }

    private List<AnomalyManagePanel.TargetEntry> BuildContainTargets(NodeState node, DataRegistry registry)
    {
        var targets = new List<AnomalyManagePanel.TargetEntry>();
        var known = node?.KnownAnomalyDefIds;
        if (known == null || known.Count == 0) return targets;

        // Use intersection: Known ∩ ActiveAnomalyIds (to avoid containing anomalies that have disappeared)
        var activeSet = new HashSet<string>(node.ActiveAnomalyIds ?? new List<string>());

        foreach (var anomalyId in known)
        {
            if (string.IsNullOrEmpty(anomalyId)) continue;
            if (!activeSet.Contains(anomalyId)) continue;  // Only include if still active

            var def = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
            string title = def?.name ?? anomalyId;

            targets.Add(new AnomalyManagePanel.TargetEntry
            {
                id = anomalyId,
                title = title,
                subtitle = null,
                disabled = false
            });
        }

        return targets;
    }

    private string EnsureContainableForAnomaly(NodeState node, DataRegistry registry, string anomalyId)
    {
        if (node == null || string.IsNullOrEmpty(anomalyId)) return null;
        if (node.Containables == null) node.Containables = new List<ContainableItem>();

        var existing = node.Containables.FirstOrDefault(c => c != null && c.AnomalyId == anomalyId);
        if (existing != null) return existing.Id;

        var def = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
        int level = def != null ? Math.Max(1, def.baseThreat) : Math.Max(1, node.AnomalyLevel);
        var item = new ContainableItem
        {
            Id = $"SCP_{node.Id}_{Guid.NewGuid().ToString("N")[..6]}",
            Name = def != null ? $"{def.name} 线索（{node.Name}）" : $"未编号异常（{node.Name}）",
            Level = level,
            AnomalyId = anomalyId,
        };
        node.Containables.Add(item);
        return item.Id;
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
        _managePanelView = _managePanel.GetComponent<AnomalyManagePanel>();
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
        EnsureManagePanel();
        if (_managePanel)
        {
            if (_managePanelView) _managePanelView.ShowForNode(_manageNodeId);
            _managePanel.SetActive(true);
            if (_managePanelView) _managePanelView.ShowForNode(_manageNodeId);
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

    // ================== RECRUIT ==================

    void EnsureRecruitPanel()
    {
        if (_recruitPanel) return;
        if (recruitPanelPrefab)
        {
            _recruitPanel = Instantiate(recruitPanelPrefab, transform);
        }
        else
        {
            var go = new GameObject("RecruitPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(RecruitPanel));
            go.transform.SetParent(transform, false);
            _recruitPanel = go.GetComponent<RecruitPanel>();
        }

        if (_recruitPanel) _recruitPanel.gameObject.SetActive(false);
    }

    public void OpenRecruit()
    {
        EnsureRecruitPanel();
        if (_recruitPanel)
        {
            _recruitPanel.Show();
            _recruitPanel.transform.SetAsLastSibling();
        }
    }

    public void CloseRecruit()
    {
        if (_recruitPanel) _recruitPanel.Hide();
    }

    public void OpenNodeEvent(string nodeId)
    {
        if (GameController.I == null || string.IsNullOrEmpty(nodeId)) return;
        var node = GameController.I.GetNode(nodeId);
        if (node == null || node.PendingEvents == null || node.PendingEvents.Count == 0) return;

        if (!_eventPanel && eventPanelPrefab) _eventPanel = Instantiate(eventPanelPrefab, transform);
        if (!_eventPanel) return;

        _suppressAutoOpenEvent = true;
        var ev = node.PendingEvents[0];
        Debug.Log($"[EventUI] OpenNodeEvent node={nodeId} eventInstanceId={ev.EventInstanceId} pending={node.PendingEvents.Count}");
        _eventPanel.Show(ev, optionId =>
        {
            _suppressAutoOpenEvent = true;
            var res = GameController.I.ResolveEvent(nodeId, ev.EventInstanceId, optionId);
            return res.text;
        }, onClose: null);
    }

    void TryAutoOpenEvent()
    {
        // Auto-open is disabled. Events should only be opened via manual entry points.
        return;
    }

    private bool TryGetFirstPendingEvent(out string nodeId, out EventInstance ev)
    {
        nodeId = null;
        ev = null;

        foreach (var n in GameController.I.State.Nodes)
        {
            if (n?.PendingEvents == null || n.PendingEvents.Count == 0) continue;
            ev = n.PendingEvents[0];
            nodeId = n.Id;
            return !string.IsNullOrEmpty(nodeId) && ev != null;
        }

        return false;
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
        ForceCancelPickerIfNeeded(true);
        CloseNode();
        CloseEvent();
        CloseManage();
        CloseRecruit();
        if (_newsPanel) _newsPanel.Hide();

        if (_confirmDialog) _confirmDialog.Hide();
    }

    // ================== HELPERS ==================
    void ForceCancelPickerIfNeeded(bool hidePicker = true)
    {
        var taskId = _pickerTaskId;
        _pickerTaskId = null;

        if (!string.IsNullOrEmpty(taskId) && GameController.I != null)
        {
            GameController.I.CancelOrRetreatTask(taskId);
            GameControllerTaskExt.LogBusySnapshot(GameController.I, $"UIPanelRoot.ForceCancelPickerIfNeeded(task:{taskId})");
            RefreshNodePanel();
        }

        if (hidePicker && _agentPicker)
        {
            GameControllerTaskExt.LogBusySnapshot(GameController.I, "UIPanelRoot.ForceCancelPickerIfNeeded(hidePicker)");
            _agentPicker.Hide();
        }
    }

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
// </EXPORT_BLOCK>
