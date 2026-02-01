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
    [SerializeField] private GameObject NewspaperPanelPrefab;
    [SerializeField] private AgentPickerView agentPickerPrefab;
    [SerializeField] private ConfirmDialog confirmDialogPrefab;
    [SerializeField] private GameObject managePanelPrefab; // 管理面板 Prefab（你新建的管理界面）
    [SerializeField] private RecruitPanel recruitPanelPrefab;

    // --- 运行时实例 (自动生成) ---
    private HUD _hud;
    private NodePanelView _nodePanel;
    private EventPanel _eventPanel;
    private NewsPanel _newsPanel;
    private GameObject _newspaperPanelInstance;
    private AgentPickerView _agentPicker;
    private ConfirmDialog _confirmDialog;
    private GameObject _managePanel;
    private AnomalyManagePanel _managePanelView;
    private RecruitPanel _recruitPanel;

    private List<GameObject> _modalStack = new List<GameObject>();
    private bool _confirmDialogOnClosedHooked;

    private string _currentNodeId;
    private string _manageNodeId; // 当前打开的管理面板所对应的节点（与 NodePanel 的当前节点解耦）
    private string _pickerTaskId;

    public string CurrentNodeId => _currentNodeId;
    public string ManageNodeId => _manageNodeId;

    private void Awake()
    {
        I = this;
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
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
        if (_nodePanel) PushModal(_nodePanel.gameObject, "open node");
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
        HookConfirmDialogOnClosed();
    }

    public void ShowInfo(string title, string message)
    {
        EnsureConfirmDialog();
        if (_confirmDialog)
        {
            _confirmDialog.ShowInfo(title, message);
            _confirmDialog.transform.SetAsLastSibling();
            PushModal(_confirmDialog.gameObject, "show_confirm");
            RefreshModalStack("show_confirm", _confirmDialog.gameObject);
        }
        else
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog prefab not set. Info: {title} / {message}");
        }
    }

    public void ShowConfirm(
        string title,
        string message,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = "确认",
        string cancelText = "取消")
    {
        EnsureConfirmDialog();
        if (_confirmDialog)
        {
            _confirmDialog.ShowConfirm(title, message, onConfirm, onCancel, confirmText, cancelText);
            _confirmDialog.transform.SetAsLastSibling();
            PushModal(_confirmDialog.gameObject, "show_confirm");
            RefreshModalStack("show_confirm", _confirmDialog.gameObject);
        }
        else
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog prefab not set. Confirm: {title} / {message}");
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
        PushModal(_managePanel, "open manage");
        RefreshModalStack("open manage", _managePanel);
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
        PushModal(_managePanel, "open manage");
        RefreshModalStack("open manage", _managePanel);
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
        node.KnownAnomalyDefIds ??= new List<string>();
        if (!node.KnownAnomalyDefIds.Contains(anomalyId))
            node.KnownAnomalyDefIds.Add(anomalyId);
        return anomalyId;
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
            PushModal(_managePanel, "open manage");
            RefreshModalStack("open manage", _managePanel);
        }
    }

    public void CloseManage()
    {
        if (_managePanel) CloseModal(_managePanel, "close manage");
    }

    // ================== OTHERS ==================

    public void OpenNews()
    {
        OpenNewspaperPanel();
    }

    void EnsureNewspaperPanel()
    {
        if (_newspaperPanelInstance) return;
        if (!NewspaperPanelPrefab)
        {
            Debug.LogError("[UIPanelRoot] NewspaperPanelPrefab 未配置！");
            return;
        }

        _newspaperPanelInstance = Instantiate(NewspaperPanelPrefab, transform);
        if (_newspaperPanelInstance.GetComponent<UI.NewspaperPanelView>() == null)
        {
            _newspaperPanelInstance.AddComponent<UI.NewspaperPanelView>();
        }
        _newspaperPanelInstance.SetActive(false);
    }

    public void OpenNewspaperPanel()
    {
        EnsureNewspaperPanel();
        if (_newspaperPanelInstance)
        {
            _newspaperPanelInstance.SetActive(true);
            var view = _newspaperPanelInstance.GetComponent<UI.NewspaperPanelView>();
            if (view != null) view.Render();
            _newspaperPanelInstance.transform.SetAsLastSibling();
            PushModal(_newspaperPanelInstance, "open_newspaper");
            RefreshModalStack("open_newspaper", _newspaperPanelInstance);
        }
    }

    public void HideNewspaperPanel()
    {
        if (_newspaperPanelInstance) CloseModal(_newspaperPanelInstance, "close newspaper");
    }

    // ================== RECRUIT ==================

    void EnsureRecruitPanel()
    {
        if (_recruitPanel) return;
        if (!recruitPanelPrefab)
        {
            Debug.LogError("[UIPanelRoot] recruitPanelPrefab 未配置，无法打开 RecruitPanel。");
            return;
        }

        _recruitPanel = Instantiate(recruitPanelPrefab, transform);

        if (_recruitPanel) _recruitPanel.gameObject.SetActive(false);
    }

    public void OpenRecruit()
    {
        EnsureRecruitPanel();
        if (_recruitPanel)
        {
            _recruitPanel.Show();
            _recruitPanel.gameObject.SetActive(true);
            _recruitPanel.transform.SetAsLastSibling();
            PushModal(_recruitPanel.gameObject, "open_recruit");
            RefreshModalStack("open_recruit", _recruitPanel.gameObject);
        }
    }

    public void CloseRecruit()
    {
        if (_recruitPanel) CloseModal(_recruitPanel.gameObject, "close recruit");
    }

    public void OpenNodeEvent(string nodeId)
    {
        if (GameController.I == null || string.IsNullOrEmpty(nodeId)) return;
        var node = GameController.I.GetNode(nodeId);
        if (node == null || node.PendingEvents == null || node.PendingEvents.Count == 0) return;

        if (!_eventPanel && eventPanelPrefab) _eventPanel = Instantiate(eventPanelPrefab, transform);
        if (!_eventPanel) return;

        var ev = node.PendingEvents[0];
        Debug.Log($"[EventUI] OpenNodeEvent node={nodeId} eventInstanceId={ev.EventInstanceId} pending={node.PendingEvents.Count}");
        _eventPanel.Show(ev, optionId =>
        {
            var res = GameController.I.ResolveEvent(nodeId, ev.EventInstanceId, optionId);
            return res.text;
        }, onClose: null);
        _eventPanel.gameObject.SetActive(true);
        _eventPanel.transform.SetAsLastSibling();
        PushModal(_eventPanel.gameObject, "open event");
        RefreshModalStack("open event", _eventPanel.gameObject);
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
        if (_eventPanel) CloseModal(_eventPanel.gameObject, "close event");
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
        if (_nodePanel) CloseModal(_nodePanel.gameObject, "close all");
        if (_eventPanel) CloseModal(_eventPanel.gameObject, "close all");
        if (_managePanel) CloseModal(_managePanel, "close all");
        if (_recruitPanel) CloseModal(_recruitPanel.gameObject, "close all");
        if (_newspaperPanelInstance) CloseModal(_newspaperPanelInstance, "close all");
        if (_newsPanel) CloseModal(_newsPanel.gameObject, "close all");
        if (_confirmDialog) CloseModal(_confirmDialog.gameObject, "close all");
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

    // ================== MODAL STACK ==================

    public void CloseModal(GameObject panel, string reason = null)
    {
        var safeReason = string.IsNullOrEmpty(reason) ? "close" : reason;
        PopModal(panel, safeReason);

        if (panel == null)
        {
            RefreshModalStack("close");
            return;
        }

        var confirm = panel.GetComponent<ConfirmDialog>();
        if (confirm != null)
        {
            confirm.Hide();
        }
        else
        {
            var closable = panel.GetComponent<IModalClosable>();
            if (closable != null)
            {
                closable.CloseFromRoot();
            }
            else
            {
                panel.SetActive(false);
            }
        }

        RefreshModalStack("close", panel);
    }

    public void CloseTopModal(string reason = null)
    {
        RefreshModalStack("close_top_pre");
        if (_modalStack == null) _modalStack = new List<GameObject>();
        _modalStack.RemoveAll(p => p == null);

        if (_modalStack.Count == 0) return;

        var top = _modalStack[_modalStack.Count - 1];
        CloseModal(top, string.IsNullOrEmpty(reason) ? "close_top" : reason);
    }

    private void PushModal(GameObject panel, string reason)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        if (panel != null)
        {
            _modalStack.Remove(panel);
            _modalStack.Add(panel);
        }

        LogModalStack("Push", panel, reason);
    }

    private void PopModal(GameObject panel, string reason)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        if (panel != null)
        {
            _modalStack.Remove(panel);
        }

        LogModalStack("Pop", panel, reason);
    }

    private void RefreshModalStack(string reason, GameObject relatedPanel = null, bool sortBySiblingIndex = false)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        _modalStack.RemoveAll(p => p == null);

        if (sortBySiblingIndex)
        {
            _modalStack.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        }

        LogModalStack("Refresh", relatedPanel, reason);
        RefreshDimmerStack(reason, relatedPanel);
    }

    private void RefreshDimmerStack(string reason, GameObject relatedPanel = null)
    {
        if (_modalStack == null) return;

        ModalDimmerHandle topHandle = null;
        for (int i = _modalStack.Count - 1; i >= 0; i--)
        {
            var panel = _modalStack[i];
            if (panel == null) continue;

            var handle = panel.GetComponent<ModalDimmerHandle>();
            if (handle != null)
            {
                topHandle = handle;
                break;
            }
        }

        foreach (var panel in _modalStack)
        {
            if (panel == null) continue;
            var handle = panel.GetComponent<ModalDimmerHandle>();
            if (handle == null) continue;

            handle.SetDimmerActive(handle == topHandle);
        }
    }

    private void LogModalStack(string action, GameObject panel, string reason)
    {
        var panelName = panel != null ? panel.name : "ALL";
        var safeReason = string.IsNullOrEmpty(reason) ? "(no-reason)" : reason;
        Debug.Log($"[ModalStack] action={action} panel={panelName} count={_modalStack?.Count ?? 0} reason={safeReason}");
    }

    private void HookConfirmDialogOnClosed()
    {
        if (_confirmDialog == null || _confirmDialogOnClosedHooked) return;

        _confirmDialog.OnClosed += HandleConfirmDialogClosed;
        _confirmDialogOnClosedHooked = true;
    }

    private void HandleConfirmDialogClosed()
    {
        if (_confirmDialog == null) return;
        if (_modalStack != null && _modalStack.Contains(_confirmDialog.gameObject))
        {
            PopModal(_confirmDialog.gameObject, "ConfirmDialog.OnClosed");
        }
    }

    // Pick a containable that is not already targeted by an active containment task when possible.
    string PickNextContainableId(NodeState node)
    {
        if (node == null || node.KnownAnomalyDefIds == null || node.KnownAnomalyDefIds.Count == 0) return null;

        var activeTargets = (node.Tasks == null)
            ? new HashSet<string>()
            : new HashSet<string>(node.Tasks
                .Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain)
                .Select(t => t.SourceAnomalyId)
                .Where(x => !string.IsNullOrEmpty(x)));

        foreach (var defId in node.KnownAnomalyDefIds)
        {
            if (!activeTargets.Contains(defId)) return defId;
        }

        // Fallback: allow multiple tasks for same anomaly if all are already targeted.
        return node.KnownAnomalyDefIds[0];
    }
}
// </EXPORT_BLOCK>
