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

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnomalyManagePanel : MonoBehaviour
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
        Generic
    }

    [Header("Left: Anomaly list")]
    [SerializeField] private Transform anomalyListContent;
    [SerializeField] private GameObject anomalyListItemPrefab; // must have Button + TMP_Text named "Label" (or any TMP_Text)

    [Header("Right: Agent list (AgentPickerItemView)")]
    [SerializeField] private Transform agentListContent;
    [SerializeField] private GameObject agentPickerItemPrefab; // prefab with AgentPickerItemView

    [Header("Actions")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text headerText; // optional
    [SerializeField] private TMP_Text hintText;   // optional

    private readonly List<GameObject> _anomalyItems = new();
    private readonly List<AgentPickerItemView> _agentItems = new();
    private readonly List<TargetEntry> _currentTargets = new();

    private string _selectedTargetId;
    private string _nodeId; // management context node id (set by UIPanelRoot.ManageNodeId)
    private readonly HashSet<string> _selectedAgentIds = new();
    private int _slotsMin = 1;
    private int _slotsMax = int.MaxValue;
    private AssignPanelMode _mode = AssignPanelMode.Manage;
    private Action<string, List<string>> _onConfirm;

    void Awake()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }

        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(Confirm);
        }
    }

    void OnEnable()
    {
        var root = UIPanelRoot.I;
        _nodeId = root != null ? root.ManageNodeId : null;
        // IMPORTANT: do not auto RefreshUI here.
        // This panel is reused for Investigate/Contain assignment and auto-refresh would rebuild Manage mode & spam logs.
    }

    public void Show()
    {
        _mode = AssignPanelMode.Manage;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshUI();
    }

    // Optional explicit node binding (recommended if you call panel directly)
    public void Show(string nodeId)
    {
        ShowForNode(nodeId);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        GameControllerTaskExt.LogBusySnapshot(GameController.I, "AnomalyManagePanel.Hide");
    }

    public void ShowForNode(string nodeId)
    {
        _mode = AssignPanelMode.Manage;
        _nodeId = nodeId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshUI();
    }

    // --------------------
    // Refresh
    // --------------------

    public void RefreshUI()
    {
        if (_mode != AssignPanelMode.Manage) return;

        var gc = GameController.I;
        if (gc == null) return;
        var registry = DataRegistry.Instance;
        (_slotsMin, _slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Manage, 1, int.MaxValue);

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetNode(_nodeId) : null;
        var list = GetFavoritedAnomalies(node);

        // Keep selection valid
        if (string.IsNullOrEmpty(_selectedTargetId) || !list.Any(x => x.Id == _selectedTargetId))
            _selectedTargetId = list.FirstOrDefault()?.Id;
        if (list.Count == 0)
        {
            _selectedTargetId = null;
            _selectedAgentIds.Clear();
        }

        var targets = list
            .Select(a => new TargetEntry
            {
                id = a.Id,
                title = BuildAnomalyLabel(a, GetCurrentManagerCount(node, a.Id)),
                subtitle = null,
                disabled = false
            })
            .ToList();

        string hint = "";
        if (list.Count == 0)
        {
            if (string.IsNullOrEmpty(_nodeId)) hint = "管理面板缺少节点上下文（ManageNodeId 为空）。";
            else hint = "该节点暂无已收容异常。请先完成收容。";
        }
        else
        {
            hint = "选择异常，然后分配干员开始管理（每日产出负熵）。";
        }

        ShowGenericInternal("管理：选择异常", hint, targets, _slotsMin, _slotsMax, HandleManageConfirm, "Manage");
        UpdateHeader();
    }

    private static List<ManagedAnomalyState> GetFavoritedAnomalies(NodeState node)
    {
        if (node?.ManagedAnomalies == null) return new List<ManagedAnomalyState>();
        return node.ManagedAnomalies
            .Where(x => x != null && x.Favorited)
            .OrderByDescending(x => x.Level)
            .ThenBy(x => x.Name)
            .ToList();
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
        _mode = modeLabel == "Manage" ? AssignPanelMode.Manage : AssignPanelMode.Generic;
        _onConfirm = onConfirm;
        _slotsMin = agentSlotsMin;
        _slotsMax = agentSlotsMax;

        if (headerText) headerText.text = header ?? "";
        if (hintText) hintText.text = hint ?? "";

        var safeTargets = targets ?? new List<TargetEntry>();
        if (string.IsNullOrEmpty(_selectedTargetId) || !safeTargets.Any(x => x != null && x.id == _selectedTargetId))
            _selectedTargetId = safeTargets.FirstOrDefault()?.id;

        RebuildTargetList(safeTargets);
        RebuildAgentList();
        RefreshConfirmState();
        if (confirmButton) confirmButton.interactable = confirmButton.interactable && safeTargets.Count > 0;

        Debug.Log($"[AssignPanel] mode={modeLabel} targets={safeTargets.Count} slots={_slotsMin}-{_slotsMax}");
    }

    private void RebuildTargetList(List<TargetEntry> list)
    {
        // Clear
        for (int i = 0; i < _anomalyItems.Count; i++)
            if (_anomalyItems[i]) Destroy(_anomalyItems[i]);
        _anomalyItems.Clear();
        _currentTargets.Clear();

        if (!anomalyListContent || !anomalyListItemPrefab)
            return;

        if (list == null || list.Count == 0) return;

        foreach (var entry in list)
        {
            var go = Instantiate(anomalyListItemPrefab, anomalyListContent);
            string entryId = entry?.id ?? "";
            go.name = "Target_" + entryId;
            _anomalyItems.Add(go);
            _currentTargets.Add(entry);

            SetListItemLabels(go, entry);

            bool selected = (entryId == _selectedTargetId);
            SetListItemSelectedVisual(go, selected);

            var btn = go.GetComponentInChildren<Button>(true);
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                string id = entryId;
                btn.interactable = entry == null || !entry.disabled;
                btn.onClick.AddListener(() => SelectTarget(id));
            }
        }
    }

    private static string BuildAnomalyLabel(ManagedAnomalyState a, int mgr)
    {
        if (a == null) return "";
        return $"Lv{a.Level} {a.Name}  (管理:{mgr})";
    }

    private void SetListItemSelectedVisual(GameObject go, bool selected)
    {
        // Minimal visual: try Image tint if present.
        var img = go.GetComponentInChildren<Image>(true);
        if (img)
        {
            img.color = selected ? new Color(0f, 0.68f, 0.71f, 0.25f) : new Color(1f, 1f, 1f, 0.05f);
        }
    }

    private void SelectTarget(string targetId)
    {
        _selectedTargetId = targetId;
        _selectedAgentIds.Clear();

        Debug.Log($"[AssignPanelSelect] targetId={_selectedTargetId}");

        if (_mode == AssignPanelMode.Manage)
        {
            // Pull current assignment (prefer Manage Task, fallback legacy field)
            var gc = GameController.I;
            var node = (gc != null && !string.IsNullOrEmpty(_nodeId)) ? gc.GetNode(_nodeId) : null;
            var mt = FindManageTask(node, targetId);
            if (mt?.AssignedAgentIds != null)
            {
                foreach (var id in mt.AssignedAgentIds)
                    _selectedAgentIds.Add(id);
            }
        }

        RebuildAgentList();
        RefreshTargetSelectionVisuals();
        UpdateHeader();
    }

    private void RebuildAgentList()
    {
        // Clear
        for (int i = 0; i < _agentItems.Count; i++)
            if (_agentItems[i]) Destroy(_agentItems[i].gameObject);
        _agentItems.Clear();

        if (!agentListContent || !agentPickerItemPrefab)
            return;

        var gc = GameController.I;
        if (gc == null) return;

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetNode(_nodeId) : null;
        if (_mode == AssignPanelMode.Manage)
        {
            var anomaly = FindManagedAnomaly(node, _selectedTargetId);
            if (anomaly == null)
                return;

            // Sync selection from Manage Task (source of truth), fallback legacy.
            var mt = FindManageTask(node, anomaly.Id);
            _selectedAgentIds.Clear();
            if (mt?.AssignedAgentIds != null)
                foreach (var id in mt.AssignedAgentIds) _selectedAgentIds.Add(id);
        }

        foreach (var ag in gc.State.Agents)
        {
            if (ag == null) continue;

            bool selected = _selectedAgentIds.Contains(ag.Id);

            // Busy check (global): any active task (including Manage) OR legacy management occupancy.
            bool busyTask = GameControllerTaskExt.AreAgentsBusy(gc, new List<string> { ag.Id });

            // Allow clicking to deselect even if currently busy (soft lock)
            bool isBusyOther = (!selected) && busyTask;

            var go = Instantiate(agentPickerItemPrefab, agentListContent);
            go.name = "Agent_" + ag.Id;

            var item = go.GetComponent<AgentPickerItemView>();
            if (item == null) item = go.AddComponent<AgentPickerItemView>();

            item.Bind(
                ag.Id,
                ag.Name,
                BuildAgentAttrLine(ag),
                isBusyOther,
                selected,
                OnAgentClicked);

            _agentItems.Add(item);
        }

        // Update confirm button
        RefreshConfirmState();
    }

    private static string BuildAgentAttrLine(AgentState a)
    {
        if (a == null) return "";
        return $"P{a.Perception} O{a.Operation} R{a.Resistance} Pow{a.Power}";
    }

    private void OnAgentClicked(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

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

        UpdateHeader();
        RefreshConfirmState();
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
        var node = (gc != null && !string.IsNullOrEmpty(_nodeId)) ? gc.GetNode(_nodeId) : null;
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
            var n = gc.GetNode(_nodeId);
            nodeName = n != null ? n.Name : "";
        }

        headerText.text = string.IsNullOrEmpty(nodeName)
            ? $"管理：{m.Name}  Lv{m.Level}  管理人数:{mgr}  累计负熵:{m.TotalNegEntropy}"
            : $"管理：[{nodeName}] {m.Name}  Lv{m.Level}  管理人数:{mgr}  累计负熵:{m.TotalNegEntropy}";
    }

    private void HandleManageConfirm(string targetId, List<string> agentIds)
    {
        var gc = GameController.I;
        if (gc == null) return;

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetNode(_nodeId) : null;
        var m = FindManagedAnomaly(node, targetId);
        if (m == null) return;

        // Write back as a formal Manage task.
        var mt = gc.CreateManageTask(_nodeId, m.Id);
        if (mt == null) return;

        // If clearing selection, cancel/retreat the manage task to release occupancy.
        if (agentIds.Count == 0)
        {
            gc.CancelOrRetreatTask(mt.Id);
        }
        else
        {
            gc.AssignTask(mt.Id, agentIds);

            // Optional: set StartDay for UX (Sim will also set on first yield)
            if (m.StartDay <= 0) m.StartDay = gc.State.Day;
        }

        // Update UI
        RefreshUI();
        gc.Notify();
    }

    // --------------------
    // Data helpers
    // --------------------

    private static ManagedAnomalyState FindManagedAnomaly(NodeState node, string anomalyId)
    {
        if (node?.ManagedAnomalies == null || string.IsNullOrEmpty(anomalyId)) return null;
        return node.ManagedAnomalies.FirstOrDefault(x => x != null && x.Id == anomalyId);
    }

    private static NodeTask FindManageTask(NodeState node, string anomalyId)
    {
        if (node?.Tasks == null || string.IsNullOrEmpty(anomalyId)) return null;

        // Prefer active task
        var active = node.Tasks.LastOrDefault(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == anomalyId);
        if (active != null) return active;

        // Fallback: any manage task (history)
        return node.Tasks.LastOrDefault(t => t != null && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == anomalyId);
    }

    private static int GetCurrentManagerCount(NodeState node, string anomalyId)
    {
        int mgr = 0;
        var mtForLabel = FindManageTask(node, anomalyId);
        if (mtForLabel?.AssignedAgentIds != null) mgr = mtForLabel.AssignedAgentIds.Count;
        return mgr;
    }

    private void SetListItemLabels(GameObject go, TargetEntry entry)
    {
        if (go == null || entry == null) return;

        var labels = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels == null || labels.Length == 0) return;

        if (labels.Length == 1)
        {
            labels[0].text = entry.title ?? "";
            return;
        }

        TextMeshProUGUI titleLabel = null;
        TextMeshProUGUI subtitleLabel = null;

        foreach (var label in labels)
        {
            if (label == null) continue;
            string name = label.name.ToLowerInvariant();
            if (titleLabel == null && (name.Contains("title") || name.Contains("name") || name.Contains("label")))
                titleLabel = label;
            if (subtitleLabel == null && (name.Contains("sub") || name.Contains("desc") || name.Contains("detail")))
                subtitleLabel = label;
        }

        titleLabel ??= labels[0];
        if (labels.Length > 1 && subtitleLabel == null)
            subtitleLabel = labels[1];

        if (titleLabel) titleLabel.text = entry.title ?? "";
        if (subtitleLabel) subtitleLabel.text = entry.subtitle ?? "";
    }

    private void RefreshTargetSelectionVisuals()
    {
        for (int i = 0; i < _anomalyItems.Count; i++)
        {
            var go = _anomalyItems[i];
            if (go == null) continue;
            string targetId = i < _currentTargets.Count ? _currentTargets[i]?.id : null;
            bool selected = !string.IsNullOrEmpty(targetId) && targetId == _selectedTargetId;
            SetListItemSelectedVisual(go, selected);
        }
    }
}
// </EXPORT_BLOCK>
