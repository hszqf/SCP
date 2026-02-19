// Canvas-maintained file: Assets/Scripts/UI/AnomalyManagePanel.cs
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
        Generic,
        Rescue
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

    // Rescue mode: pick one rescuer agent at base to retrieve a dead/insane agent from an anomaly.
    private string _rescueAnomalyInstanceId;
    private string _rescueTargetAgentId;
    private Action<string, string> _onRescueConfirm;

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


    public void Hide()
    {
        gameObject.SetActive(false);
        GameControllerTaskExt.LogBusySnapshot(GameController.I, "AnomalyManagePanel.Hide");
    }

    public void CloseFromRoot()
    {
        Hide();
    }
   

    public void ShowGeneric(
        string header,
        string hint,
        List<TargetEntry> targets,
        int agentSlotsMax,
        Action<string, List<string>> onConfirm,
        string modeLabel = "Generic")
    {
        ShowGenericInternal(header, hint, targets, agentSlotsMax, onConfirm, modeLabel);
    }

    public void ShowRescue(
        string anomalyInstanceId,
        string targetAgentId,
        string targetDisplayName,
        Action<string, string> onConfirm)
    {
        ClearSelectionState();
        _mode = AssignPanelMode.Rescue;
        _onConfirm = null;
        _onRescueConfirm = onConfirm;

        _rescueAnomalyInstanceId = anomalyInstanceId;
        _rescueTargetAgentId = targetAgentId;

        _slotsMin = 1;
        _slotsMax = 1;

        if (headerText) headerText.text = "接回人员";
        if (hintText)
        {
            var name = string.IsNullOrEmpty(targetDisplayName) ? targetAgentId : targetDisplayName;
            hintText.text = $"选择一名干员接回：{name}";
        }

        // In Rescue mode we select and confirm immediately on click.
        if (confirmButton && confirmButton.gameObject != null)
            confirmButton.gameObject.SetActive(false);

        _selectedTargetId = null; // unused in Rescue mode
        RebuildAgentList();
        RefreshConfirmState();

        Debug.Log($"[AssignPanel] mode=Rescue anomaly={anomalyInstanceId} targetAgent={targetAgentId}");
    }

    private void ShowGenericInternal(
        string header,
        string hint,
        List<TargetEntry> targets,
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

    void RebuildAgentList()
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

        // Rescue mode: only show base, usable agents.
        if (_mode == AssignPanelMode.Rescue)
        {
            _selectedAgentIds.Clear();

            foreach (var ag in gc.State.Agents)
            {
                if (ag == null) continue;
                if (ag.IsDead || ag.IsInsane) continue;
                if (ag.LocationKind != AgentLocationKind.Base) continue;

                bool disable = false;
                bool selected = _selectedAgentIds.Contains(ag.Id);

                string statusText = "<color=#66FF66>空闲</color>";

                var go = Instantiate(agentPickerItemPrefab, contentRoot);
                go.name = "Agent_" + ag.Id;

                var item = go.GetComponent<AgentPickerItemView>();
                if (item == null) item = go.AddComponent<AgentPickerItemView>();


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

            RefreshConfirmState();
            return;
        }

        string canonicalKeyForFilter = null;

        // Determine slot-driven roster source and sync selected ids from AnomalyState.GetRoster(slot) if available.
        var slot = GetCurrentSlot();

        Core.AnomalyState anomState = null;
        if (!string.IsNullOrEmpty(_selectedTargetId) && gc?.State != null)
        {
            // 只认实例 id：直接 resolve
            anomState = Core.DispatchSystem.FindAnomaly(gc.State, _selectedTargetId);
        }

        // canonical key: 一律用实例 id（必须能 resolve 到 AnomalyState）
        if (anomState == null)
        {
            Debug.LogError($"[AssignPanel] RebuildAgentList: instance not found. instanceId={_selectedTargetId}");
            canonicalKeyForFilter = _selectedTargetId;
        }
        else
        {
            canonicalKeyForFilter = anomState.Id;
        }

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
                || (ag.LocationAnomalyInstanceId == canonicalKeyForFilter &&
                    (ag.LocationKind == AgentLocationKind.AtAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToBase));

            if (!show) continue;

            // Disable rule
            bool disable = false;

            // if already selected & exceeding max slot count, disable others later in RefreshConfirmState
            // Here we only disable if busy on another anomaly or traveling to another anomaly.
            if (ag.LocationKind == AgentLocationKind.AtAnomaly ||
                ag.LocationKind == AgentLocationKind.TravellingToAnomaly ||
                ag.LocationKind == AgentLocationKind.TravellingToBase)
            {
                if (!string.Equals(ag.LocationAnomalyInstanceId, canonicalKeyForFilter, System.StringComparison.Ordinal))
                    disable = true;
            }

            bool selected = _selectedAgentIds.Contains(ag.Id);

            // Status text 目前基地一定是空闲
            string statusText1 = string.Empty;
            if (ag.LocationKind == AgentLocationKind.Base)
                statusText1 = "<color=#66FF66>空闲</color>";
            else if (ag.LocationKind == AgentLocationKind.AtAnomaly)
                statusText1 = "已到达";
            else if (ag.LocationKind == AgentLocationKind.TravellingToAnomaly)
                statusText1 = "前往中";
            else if (ag.LocationKind == AgentLocationKind.TravellingToBase)
                statusText1 = "返程中";
            else
                statusText1 = ag.LocationKind.ToString();

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

        if (_mode == AssignPanelMode.Rescue)
        {
            // Pick-and-confirm immediately
            if (agent == null) return;
            if (agent.LocationKind != AgentLocationKind.Base) return;

            _selectedAgentIds.Clear();
            _selectedAgentIds.Add(agentId);

            foreach (var it in _agentItems)
            {
                if (it == null) continue;
                it.SetSelected(it.AgentId == agentId);
            }

            _onRescueConfirm?.Invoke(_rescueAnomalyInstanceId, agentId);
            UIPanelRoot.I?.CloseModal(gameObject, "rescue_confirm");
            return;
        }

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

        var state = gc.State;

        // 只认 instanceId：selectedTargetId 就是 AnomalyState.Id
        var anomalyInstanceId = _selectedTargetId;

        // 必须能用 instanceId 直接 resolve 到 AnomalyState；找不到就失败，不允许猜 defId
        var anom = Core.DispatchSystem.FindAnomaly(state, anomalyInstanceId);
        if (anom == null)
        {
            Debug.LogError($"[AssignPanel] ApplyRosterImmediate: instance not found. instanceId={anomalyInstanceId}");
            return;
        }

        string err;
        if (!Core.DispatchSystem.TrySetRoster(state, anomalyInstanceId, slot, ids, out err))
        {
            Debug.LogError($"[AssignPanel] TrySetRoster failed: {err}");
            return;
        }

        Debug.Log("[AssignPanel] applied ok");

        // Legacy task sync:
        // - Operate: legacy manage task targets managed anomaly instanceId
        // - Investigate/Contain: legacy tasks & KnownAnomalyDefIds are defId-based
        string legacyTargetId;
        if (slot == AssignmentSlot.Operate)
            legacyTargetId = anomalyInstanceId;
        else
            legacyTargetId = !string.IsNullOrEmpty(anom.AnomalyDefId) ? anom.AnomalyDefId : anomalyInstanceId;

        SyncLegacyTask(slot, legacyTargetId, ids);

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


        // Contain：仍可维护“已知异常 defId”列表（如果你后续还用它做解锁/提示）
        if (slot == AssignmentSlot.Contain)
        {
            if (string.IsNullOrEmpty(targetId)) return;

            if (node.KnownAnomalyDefIds == null) node.KnownAnomalyDefIds = new List<string>();
            if (!node.KnownAnomalyDefIds.Contains(targetId)) node.KnownAnomalyDefIds.Add(targetId);
            return;
        }

        // Operate：写 managed.StartDay（可选，保留你原行为）
        if (slot == AssignmentSlot.Operate)
        {
            var managed = FindManagedAnomaly(node, targetId);
            if (managed == null && node.ManagedAnomalies != null)
                managed = node.ManagedAnomalies.FirstOrDefault(m => m != null && m.AnomalyDefId == targetId);

            if (managed != null && managed.StartDay <= 0)
                managed.StartDay = gc.State.Day;

            return;
        }

        // Investigate：不做任何 legacy 同步
    }

    // --------------------
    // Data helpers
    // --------------------

    private static ManagedAnomalyState FindManagedAnomaly(CityState node, string anomalyId)
    {
        if (node?.ManagedAnomalies == null || string.IsNullOrEmpty(anomalyId)) return null;
        return node.ManagedAnomalies.FirstOrDefault(x => x != null && x.AnomalyInstanceId == anomalyId);
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
        if (gc != null && gc.State != null && !string.IsNullOrEmpty(m.AnomalyInstanceId))
        {
            var anom = Core.DispatchSystem.FindAnomaly(gc.State, m.AnomalyInstanceId);
            var roster = anom?.GetRoster(AssignmentSlot.Operate);
            mgr = roster != null ? roster.Count : 0;
        }

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
