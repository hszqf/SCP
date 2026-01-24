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
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnomalyManagePanel : MonoBehaviour
{
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

    private string _selectedAnomalyId;
    private string _nodeId; // management context node id (set by UIPanelRoot.ManageNodeId)
    private readonly HashSet<string> _selectedAgentIds = new();

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
        RefreshUI();
    }

    public void Show()
    {
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
        var gc = GameController.I;
        if (gc == null) return;

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetNode(_nodeId) : null;
        var list = GetFavoritedAnomalies(node);

        // Keep selection valid
        if (string.IsNullOrEmpty(_selectedAnomalyId) || !list.Any(x => x.Id == _selectedAnomalyId))
            _selectedAnomalyId = list.FirstOrDefault()?.Id;
        if (list.Count == 0)
        {
            _selectedAnomalyId = null;
            _selectedAgentIds.Clear();
        }

        RebuildAnomalyList(list);
        RebuildAgentList();
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

    private void RebuildAnomalyList(List<ManagedAnomalyState> list)
    {
        // Clear
        for (int i = 0; i < _anomalyItems.Count; i++)
            if (_anomalyItems[i]) Destroy(_anomalyItems[i]);
        _anomalyItems.Clear();

        if (!anomalyListContent || !anomalyListItemPrefab)
            return;

        if (list == null || list.Count == 0)
        {
            // Optional hint
            if (hintText)
            {
                if (string.IsNullOrEmpty(_nodeId)) hintText.text = "管理面板缺少节点上下文（ManageNodeId 为空）。";
                else hintText.text = "该节点暂无已收容异常。请先完成收容。";
            }
            return;
        }

        if (hintText) hintText.text = "选择异常，然后分配干员开始管理（每日产出负熵）。";

        foreach (var a in list)
        {
            var go = Instantiate(anomalyListItemPrefab, anomalyListContent);
            go.name = "Anomaly_" + a.Id;
            _anomalyItems.Add(go);

            var btn = go.GetComponentInChildren<Button>(true);
            var label = go.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            int mgr = 0;
            var nodeForLabel = (GameController.I != null && !string.IsNullOrEmpty(_nodeId)) ? GameController.I.GetNode(_nodeId) : null;
            var mtForLabel = FindManageTask(nodeForLabel, a.Id);
            if (mtForLabel?.AssignedAgentIds != null) mgr = mtForLabel.AssignedAgentIds.Count;

            if (label) label.text = BuildAnomalyLabel(a, mgr);

            bool selected = (a.Id == _selectedAnomalyId);
            SetListItemSelectedVisual(go, selected);

            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                string id = a.Id;
                btn.onClick.AddListener(() => SelectAnomaly(id));
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

    private void SelectAnomaly(string anomalyId)
    {
        _selectedAnomalyId = anomalyId;
        _selectedAgentIds.Clear();

        // Pull current assignment (prefer Manage Task, fallback legacy field)
        var gc = GameController.I;
        var node = (gc != null && !string.IsNullOrEmpty(_nodeId)) ? gc.GetNode(_nodeId) : null;
        var mt = FindManageTask(node, anomalyId);
        if (mt?.AssignedAgentIds != null)
        {
            foreach (var id in mt.AssignedAgentIds)
                _selectedAgentIds.Add(id);
        }

        RefreshUI();
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
        var anomaly = FindManagedAnomaly(node, _selectedAnomalyId);
        if (anomaly == null)
            return;

        // Sync selection from Manage Task (source of truth), fallback legacy.
        var mt = FindManageTask(node, anomaly.Id);
        _selectedAgentIds.Clear();
        if (mt?.AssignedAgentIds != null)
            foreach (var id in mt.AssignedAgentIds) _selectedAgentIds.Add(id);

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
        if (confirmButton) confirmButton.interactable = true;
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
            _selectedAgentIds.Remove(agentId);
        else
            _selectedAgentIds.Add(agentId);

        // Refresh only selection visuals
        foreach (var it in _agentItems)
        {
            if (it == null) continue;
            it.SetSelected(_selectedAgentIds.Contains(it.AgentId));
        }

        UpdateHeader();
    }

    private void Confirm()
    {
        var gc = GameController.I;
        if (gc == null) return;

        var node = !string.IsNullOrEmpty(_nodeId) ? gc.GetNode(_nodeId) : null;
        var m = FindManagedAnomaly(node, _selectedAnomalyId);
        if (m == null) return;

        // Write back as a formal Manage task.
        var mt = gc.CreateManageTask(_nodeId, m.Id);
        if (mt == null) return;

        var newIds = _selectedAgentIds.ToList();

        // If clearing selection, cancel/retreat the manage task to release occupancy.
        if (newIds.Count == 0)
        {
            gc.CancelOrRetreatTask(mt.Id);
        }
        else
        {
            gc.AssignTask(mt.Id, newIds);

            // Optional: set StartDay for UX (Sim will also set on first yield)
            if (m.StartDay <= 0) m.StartDay = gc.State.Day;
        }

        // Update UI
        RefreshUI();
        gc.Notify();
    }

    private void UpdateHeader()
    {
        if (!headerText) return;

        var gc = GameController.I;
        var node = (gc != null && !string.IsNullOrEmpty(_nodeId)) ? gc.GetNode(_nodeId) : null;
        var m = FindManagedAnomaly(node, _selectedAnomalyId);

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
}
// </EXPORT_BLOCK>
