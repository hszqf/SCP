// Canvas-maintained file: Assets/Scripts/UI/AnomalyManagePanel.cs
// Purpose: Management panel for contained anomalies (NODE-SCOPED).
// - Left: favorited managed anomalies of a specific node (NodeState.ManagedAnomalies)
// - Right: agent list (reuses AgentPickerItemView) to assign managers
// - Confirm creates/updates a NodeTask (TaskType.Manage) and assigns agents via NodeTask.AssignedAgentIds.
//   Sim awards daily NegEntropy based on active Manage tasks.
//
// Data scope note:
// - Containables & post-containment management belong to the node that contained them.
// - Global currency NegEntropy is accumulated in GameState, but per-anomaly state is stored under NodeState.
// <EXPORT_BLOCK>

using Core;
using Data;
using Settlement;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnomalyManagePanel : MonoBehaviour, IModalClosable
{
    public class TargetEntry
    {
        public string id;
        public string title;
        public string subtitle;
        public bool disabled;
    }

    private enum AssignPanelMode
    {
        Manage,
        Investigate,
        Contain,
        Operate,
        Generic
    }

    [Header("Right: Agent list (AgentPickerItemView)")]
    [SerializeField] private Transform agentListContent;
    [SerializeField] private RectTransform agentGridContent;
    [SerializeField] private ScrollRect agentListScrollRect;
    [SerializeField] private GameObject agentPickerItemPrefab; // prefab with AgentPickerItemView

    [Header("Actions")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimmerButton;
    [SerializeField] private TMP_Text headerText; // optional
    [SerializeField] private TMP_Text hintText;   // optional

    private readonly List<AgentPickerItemView> _agentItems = new();

    private string _selectedTargetId;
    private string _nodeId; // management context node id (set by UIPanelRoot.ManageNodeId)
    private readonly HashSet<string> _selectedAgentIds = new();
    private int _slotsMin = 1;
    private int _slotsMax = int.MaxValue;
    private AssignPanelMode _mode = AssignPanelMode.Manage;
    private Action<string, List<string>> _onConfirm;
    private bool _listenStateChanged;

    void Awake()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close_btn"));
        }

        if (dimmerButton)
        {
            dimmerButton.onClick.RemoveAllListeners();
            dimmerButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "dimmer"));

            var dimmerImage = dimmerButton.GetComponent<Image>();
            if (dimmerImage) dimmerImage.raycastTarget = true;

            var dimmerCg = dimmerButton.GetComponent<CanvasGroup>();
            if (dimmerCg) dimmerCg.blocksRaycasts = true;
        }

        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(Confirm);
        }

        LogBindings();
    }

    private void LogBindings()
    {
        string closeState = closeButton ? "ok" : "missing";
        string dimmerState = dimmerButton ? "ok" : "missing";
        Debug.Log($"[UIBind] AnomalyManagePanel close={closeState} dimmer={dimmerState}");
    }

    void OnEnable()
    {
        var root = UIPanelRoot.I;
        _nodeId = root != null ? root.ManageNodeId : null;
        // IMPORTANT: do not auto RefreshUI here.
        // This panel is reused for Investigate/Contain assignment and auto-refresh would rebuild Manage mode & spam logs.
        // Only RefreshUI here.
        var gc = GameController.I;
        if (!_listenStateChanged && gc != null)
        {
            gc.OnStateChanged += HandleStateChanged;
            _listenStateChanged = true;
        }
    }

    void OnDisable()
    {
        var gc = GameController.I;
        if (_listenStateChanged && gc != null)
        {
            gc.OnStateChanged -= HandleStateChanged;
            _listenStateChanged = false;
        }
    }

    public void Show()
    {
        ClearSelectionState();
        _mode = AssignPanelMode.Manage;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    // Optional explicit node binding (recommended if you call panel directly)
    public void Show(string nodeId)
    {
        ClearSelectionState();
        ShowForNode(nodeId);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        GameControllerTaskExt.LogBusySnapshot(GameController.I, "AnomalyManagePanel.Hide");
    }

    public void CloseFromRoot()
    {
        Hide();
    }

    public void ShowForNode(string nodeId)
    {
        ClearSelectionState();
        _mode = AssignPanelMode.Manage;
        _nodeId = nodeId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void ShowForNode(string nodeId, string preselectTargetId)
    {
        ClearSelectionState();
        _mode = AssignPanelMode.Manage;
        _nodeId = nodeId;
        _selectedTargetId = preselectTargetId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

   

    public void ShowGeneric(
        string header,
        string hint,
        List<TargetEntry> targets,
        int agentSlotsMin,
        int agentSlotsMax,
        Action<string, List<string>> onConfirm,
        string modeLabel = "Generic")
    {
        ShowGenericInternal(header, hint, targets, agentSlotsMin, agentSlotsMax, onConfirm, modeLabel);
    }

    private void ShowGenericInternal(
        string header,
        string hint,
        List<TargetEntry> targets,
        int agentSlotsMin,
        int agentSlotsMax,
        Action<string, List<string>> onConfirm,
        string modeLabel)
    {
        ClearSelectionState();
        // Map incoming mode label to internal AssignPanelMode so slot can be derived.
        if (string.Equals(modeLabel, "Investigate", StringComparison.OrdinalIgnoreCase)) _mode = AssignPanelMode.Investigate;
        else if (string.Equals(modeLabel, "Contain", StringComparison.OrdinalIgnoreCase)) _mode = AssignPanelMode.Contain;
        else if (string.Equals(modeLabel, "Operate", StringComparison.OrdinalIgnoreCase)) _mode = AssignPanelMode.Operate;
        else _mode = AssignPanelMode.Generic;
        _onConfirm = onConfirm;
        _slotsMin = agentSlotsMin;
        _slotsMax = agentSlotsMax;

        // Allow zero-agent selection for all modes (withdraw)
        _slotsMin = 0;

        if (headerText) headerText.text = header ?? "";
        if (hintText) hintText.text = hint ?? "";

        // Hide the confirm button - we apply roster immediately on click
        if (confirmButton && confirmButton.gameObject != null)
            confirmButton.gameObject.SetActive(false);

        var safeTargets = targets ?? new List<TargetEntry>();
        // If there's exactly one target, auto-select it immediately so agent list can bind correctly.
        if (safeTargets != null && safeTargets.Count == 1)
            _selectedTargetId = safeTargets[0].id;
        if (string.IsNullOrEmpty(_selectedTargetId) || !safeTargets.Any(x => x != null && x.id == _selectedTargetId))
            _selectedTargetId = safeTargets.FirstOrDefault()?.id;

        if (string.IsNullOrEmpty(_selectedTargetId) && safeTargets.Count > 0)
            _selectedTargetId = safeTargets[0]?.id;
        RebuildAgentList();
        RefreshConfirmState();

        // Disable confirm if no targets available (targets=0 must disable Confirm)
        if (confirmButton)
        {
            confirmButton.interactable = (safeTargets.Count > 0) && confirmButton.interactable;
        }

        // Log assignment panel state (Investigate/Contain should not trigger this repeatedly)
        Debug.Log($"[AssignPanel] mode={modeLabel} targets={safeTargets.Count} slots={_slotsMin}-{_slotsMax}");
    }


    private AssignmentSlot GetCurrentSlot()
    {
        switch (_mode)
        {
            case AssignPanelMode.Investigate: return AssignmentSlot.Investigate;
            case AssignPanelMode.Contain:     return AssignmentSlot.Contain;
            case AssignPanelMode.Operate:     return AssignmentSlot.Operate;
            case AssignPanelMode.Manage:      return AssignmentSlot.Operate;
            default:                          return AssignmentSlot.Operate;
        }
    }

    private void RebuildAgentList()
    {
        // Clear
        for (int i = 0; i < _agentItems.Count; i++)
            if (_agentItems[i]) Destroy(_agentItems[i].gameObject);
        _agentItems.Clear();

        if (agentListScrollRect && agentListScrollRect.content == null)
        {
            var scrollContent = GetContentRoot();
            if (scrollContent is RectTransform rect)
                agentListScrollRect.content = rect;
        }

        var contentRoot = GetContentRoot();
        if (contentRoot)
        {
            foreach (Transform child in contentRoot)
            {
                if (child) Destroy(child.gameObject);
            }
        }

        if (!contentRoot || !agentPickerItemPrefab)
            return;

        var gc = GameController.I;
        if (gc == null) return;

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetCity(_nodeId) : null;
        string canonicalKeyForFilter = null;

        // Determine slot-driven roster source and sync selected ids from AnomalyState.GetRoster(slot) if available.
        var slot = GetCurrentSlot();

        Core.AnomalyState anomState = null;
        if (!string.IsNullOrEmpty(_selectedTargetId) && gc?.State != null)
        {
            // 1) managedId/instanceId/legacy id 先尝试直接找
            anomState = Core.DispatchSystem.FindAnomaly(gc.State, _selectedTargetId);

            // 2) 如果找不到，视为 defId：按 node+def 消歧（避免跨节点同 def 串台）
            if (anomState == null && !string.IsNullOrEmpty(_nodeId))
            {
                var list = gc.State.Anomalies;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = list[i];
                        if (a == null) continue;
                        if (!string.IsNullOrEmpty(a.NodeId) && a.NodeId == _nodeId &&
                            !string.IsNullOrEmpty(a.AnomalyDefId) && a.AnomalyDefId == _selectedTargetId)
                        {
                            anomState = a;
                            break;
                        }
                    }
                }
            }
        }

        // canonical key: 一律用实例 id（能找到就用 anomState.Id），找不到才退回 targetId（迁移期兜底）
        canonicalKeyForFilter = anomState != null ? anomState.Id : _selectedTargetId;

        // Sync selected ids: if anomState exists and has a roster for this slot, use it even if empty.
        _selectedAgentIds.Clear();
        var roster = anomState?.GetRoster(slot);
        if (anomState != null && roster != null)
        {
            foreach (var id in roster) _selectedAgentIds.Add(id);
            // canonical key for filtering is anomaly state id
            canonicalKeyForFilter = anomState.Id;
        }

        foreach (var ag in gc.State.Agents)
        {
            if (ag == null) continue;

            if (ag.IsDead || ag.IsInsane) continue;



            bool show =
                ag.LocationKind == AgentLocationKind.Base
                || (ag.LocationAnomalyKey == canonicalKeyForFilter &&
                    (ag.LocationKind == AgentLocationKind.AtAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToBase));

            if (!show) continue;

            bool selected = _selectedAgentIds.Contains(ag.Id);

            bool unusable = ag.IsDead || ag.IsInsane;

            // Disable interaction when unusable OR (legacy busy and not using pipeline)
            bool disable = unusable;

            string statusText1 = SettlementUtil.BuildAgentBusyText(gc.State, ag.Id);
            string statusText = string.IsNullOrEmpty(statusText1) ? "<color=#66FF66>空闲</color>" : statusText1;

            var go = Instantiate(agentPickerItemPrefab, contentRoot);
            go.name = "Agent_" + ag.Id;

            var item = go.GetComponent<AgentPickerItemView>();
            if (item == null) item = go.AddComponent<AgentPickerItemView>();

            // 先全部不选
            string displayName = string.IsNullOrEmpty(ag.Name) ? ag.Id : ag.Name;
            item.Bind(
                ag,
                displayName,
                BuildAgentAttrLine(ag),
                disable,
                selected,
                OnAgentClicked,
                statusText);

            _agentItems.Add(item);
        }

        // Update confirm button
        RefreshConfirmState();
    }

    private Transform GetContentRoot()
        => agentGridContent ? agentGridContent : agentListContent;

    private static string BuildAgentAttrLine(AgentState a)
    {
        if (a == null) return "";
        int level = Mathf.Max(1, a.Level);
        int hpMax = Mathf.Max(1, a.MaxHP);
        int sanMax = Mathf.Max(1, a.MaxSAN);
        int expNeed = SettlementUtil.ExpToNext(level);
        string vitals = $"HP {a.HP}/{hpMax}  SAN {a.SAN}/{sanMax}  EXP {a.Exp}/{expNeed}";
        string attrSummary = $"P{a.Perception} O{a.Operation} R{a.Resistance} Pow{a.Power}";
        return $"{vitals}  {attrSummary}";
    }


    private void OnAgentClicked(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        var gc = GameController.I;
        var agent = gc?.State?.Agents?.FirstOrDefault(a => a != null && a.Id == agentId);
        if (agent != null && (agent.IsDead || agent.IsInsane))
            return;

        if (_selectedAgentIds.Contains(agentId))
        {
            _selectedAgentIds.Remove(agentId);
        }
        else
        {
            if (_selectedAgentIds.Count >= _slotsMax)
            {
                Debug.LogWarning($"[TaskDef] manage slot selection exceeds max. slotsMax={_slotsMax}");
                return;
            }
            _selectedAgentIds.Add(agentId);
        }

        // Refresh only selection visuals
        foreach (var it in _agentItems)
        {
            if (it == null) continue;
            it.SetSelected(_selectedAgentIds.Contains(it.AgentId));
        }

        // Immediately apply roster changes
        ApplyRosterImmediate();

        UpdateHeader();
        RefreshConfirmState();
    }

    private void ApplyRosterImmediate()
    {
        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        if (_mode == AssignPanelMode.Generic) return;

        if (string.IsNullOrEmpty(_selectedTargetId))
        {
            Debug.LogWarning("[AssignPanel] No target selected.");
            return;
        }

        var slot = GetCurrentSlot();
        var ids = _selectedAgentIds.ToList();

        // Log entering state: mode/slot/targetId/idsCount
        Debug.Log($"[AssignPanel] ApplyRosterImmediate enter mode={_mode} slot={slot} targetId={_selectedTargetId} idsCount={ids.Count}");

        string err;
        var state = gc.State;
        var anomalyKey = _selectedTargetId;

        // Canonicalize anomalyKey: prefer instance id (AnomalyState.Id) if resolvable.
        Core.AnomalyState anom = null;

        // First try using DispatchSystem.FindAnomaly with the provided key (managedId / instanceId / legacy id).
        anom = Core.DispatchSystem.FindAnomaly(state, anomalyKey);

        // If not found, treat anomalyKey as defId and disambiguate by node+def.
        if (anom == null && !string.IsNullOrEmpty(_nodeId) && !string.IsNullOrEmpty(anomalyKey))
        {
            var list = state.Anomalies;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    if (a == null) continue;
                    if (!string.IsNullOrEmpty(a.NodeId) && a.NodeId == _nodeId &&
                        !string.IsNullOrEmpty(a.AnomalyDefId) && a.AnomalyDefId == anomalyKey)
                    {
                        anom = a;
                        break;
                    }
                }
            }
        }

        // If resolved, use canonical instance key.
        if (anom != null)
            anomalyKey = anom.Id;

        if (!Core.DispatchSystem.TrySetRoster(state, anomalyKey, slot, ids, out err))
        {
            Debug.LogError($"[AssignPanel] TrySetRoster failed: {err}");
            return;
        }

        // Log success
        Debug.Log("[AssignPanel] applied ok");

        // Sync legacy task system minimally (do not close panel)
        SyncLegacyTask(slot, _selectedTargetId, ids);

        // Do not rebuild left targets (legacy Manage RefreshUI). Only refresh agent list & confirm state.
        RebuildAgentList();
        RefreshConfirmState();
        gc.Notify();
    }

    private void SyncLegacyTask(AssignmentSlot slot, string targetId, List<string> ids)
    {
        var gc = GameController.I;
        if (gc == null) return;
        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetCity(_nodeId) : null;
        if (node == null) return;

        // Manage -> NodeTask.Manage targeting managed anomaly id
        if (slot == AssignmentSlot.Operate)
        {
            // try to resolve managed anomaly in this node
            var managed = FindManagedAnomaly(node, targetId);
            if (managed == null && node.ManagedAnomalies != null)
            {
                managed = node.ManagedAnomalies.FirstOrDefault(m => m != null && m.AnomalyId == targetId);
            }
            if (managed == null) return;

            var mt = gc.CreateManageTask(_nodeId, managed.Id);
            if (mt == null) return;

            if (ids == null || ids.Count == 0)
            {
                gc.CancelOrRetreatTask(mt.Id);
            }
            else
            {
                gc.AssignTask(mt.Id, ids);
                if (managed.StartDay <= 0) managed.StartDay = gc.State.Day;
            }

            return;
        }

        // Investigate
        if (slot == AssignmentSlot.Investigate)
        {
            var t = node.Tasks?.LastOrDefault(x => x != null && x.Type == TaskType.Investigate && x.State == TaskState.Active && x.SourceAnomalyId == targetId);
            if (t == null)
            {
                t = gc.CreateInvestigateTask(_nodeId);
                if (t != null) t.SourceAnomalyId = targetId;
            }

            if (t == null) return;
            if (ids == null || ids.Count == 0) gc.CancelOrRetreatTask(t.Id);
            else gc.AssignTask(t.Id, ids);

            return;
        }

        // Contain
        if (slot == AssignmentSlot.Contain)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            if (node.KnownAnomalyDefIds == null) node.KnownAnomalyDefIds = new List<string>();
            if (!node.KnownAnomalyDefIds.Contains(targetId)) node.KnownAnomalyDefIds.Add(targetId);

            var t = node.Tasks?.LastOrDefault(x => x != null && x.Type == TaskType.Contain && x.State == TaskState.Active && x.SourceAnomalyId == targetId);
            if (t == null)
            {
                t = gc.CreateContainTask(_nodeId, targetId);
            }

            if (t == null) return;
            if (ids == null || ids.Count == 0) gc.CancelOrRetreatTask(t.Id);
            else gc.AssignTask(t.Id, ids);

            return;
        }
    }

    // --------------------
    // Data helpers
    // --------------------

    private static ManagedAnomalyState FindManagedAnomaly(CityState node, string anomalyId)
    {
        if (node?.ManagedAnomalies == null || string.IsNullOrEmpty(anomalyId)) return null;
        return node.ManagedAnomalies.FirstOrDefault(x => x != null && x.Id == anomalyId);
    }

    private static NodeTask FindManageTask(CityState node, string anomalyId)
    {
        if (node?.Tasks == null || string.IsNullOrEmpty(anomalyId)) return null;

        // Prefer active task
        var active = node.Tasks.LastOrDefault(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == anomalyId);
        if (active != null) return active;

        // Fallback: any manage task (history)
        return node.Tasks.LastOrDefault(t => t != null && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == anomalyId);
    }



    private void Confirm()
    {
        var gc = GameController.I;
        if (gc == null) return;

        if (_selectedAgentIds.Count < _slotsMin || _selectedAgentIds.Count > _slotsMax)
        {
            string message = $"需要选择 {_slotsMin}-{_slotsMax} 名干员，目前为 {_selectedAgentIds.Count}。";
            if (hintText) hintText.text = message;
            Debug.LogWarning($"[TaskDef] manage slot selection invalid. count={_selectedAgentIds.Count} slotsMin={_slotsMin} slotsMax={_slotsMax}");
            return;
        }

        var targetId = _selectedTargetId ?? "";
        var agentIds = _selectedAgentIds.ToList();

        Debug.Log($"[AssignPanelConfirm] targetId={targetId} agents={string.Join(",", agentIds)}");

        _onConfirm?.Invoke(targetId, agentIds);
    }

    private void RefreshConfirmState()
    {
        if (!confirmButton) return;
        bool withinMin = _selectedAgentIds.Count >= _slotsMin;
        bool withinMax = _selectedAgentIds.Count <= _slotsMax;
        confirmButton.interactable = withinMin && withinMax;
    }

    private void UpdateHeader()
    {
        if (!headerText) return;
        if (_mode != AssignPanelMode.Manage) return;

        var gc = GameController.I;
        var node = (gc != null && !string.IsNullOrEmpty(_nodeId)) ? gc.GetCity(_nodeId) : null;
        var m = FindManagedAnomaly(node, _selectedTargetId);

        if (m == null)
        {
            headerText.text = "管理：未选择异常";
            return;
        }

        int mgr = 0;
        var mt = FindManageTask(node, m.Id);
        if (mt?.AssignedAgentIds != null) mgr = mt.AssignedAgentIds.Count;
        string nodeName = "";
        if (gc != null && !string.IsNullOrEmpty(_nodeId))
        {
            var n = gc.GetCity(_nodeId);
            nodeName = n != null ? n.Name : "";
        }

        headerText.text = string.IsNullOrEmpty(nodeName)
            ? $"管理：{m.Name}  Lv{m.Level}  管理人数:{mgr}  累计负熵:{m.TotalNegEntropy}"
            : $"管理：[{nodeName}] {m.Name}  Lv{m.Level}  管理人数:{mgr}  累计负熵:{m.TotalNegEntropy}";
    }

    private void HandleStateChanged()
    {
        if (!isActiveAndEnabled) return;

        // Manage 模式不要自动刷新（历史原因：以前会重建 targets / spam）
        if (_mode == AssignPanelMode.Manage) return;

        // Generic/Investigate/Contain/Operate：只要面板在开着就刷新列表
        RebuildAgentList();
        RefreshConfirmState();
    }


    // 清空所有选中状态（agent/target/缓存）
    private void ClearSelectionState()
    {
        _selectedAgentIds.Clear();
        _selectedTargetId = null;
        // 清空 agent item 选中
        foreach (var item in _agentItems)
        {
            if (item != null) item.SetSelected(false);
        }
    }

}
// </EXPORT_BLOCK>
