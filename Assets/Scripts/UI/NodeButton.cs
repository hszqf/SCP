// Canvas-maintained file: UI/NodeButton
// Source: Assets/Scripts/UI/NodeButton.cs
// N-task model compatible: summarizes active investigate/contain tasks from node.Tasks.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodeButton : MonoBehaviour
{
    public string NodeId;
    [SerializeField] private TMP_Text label;
    private Button _btn;

    private const float EPS = 0.0001f;

    private void Awake()
    {
        _btn = GetComponent<Button>();
        if (_btn) _btn.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        if (UIPanelRoot.I == null || GameController.I == null || string.IsNullOrEmpty(NodeId))
            return;

        var node = GameController.I.GetNode(NodeId);
        if (node != null && node.PendingEvents != null && node.PendingEvents.Count > 0)
            UIPanelRoot.I.OpenNodeEvent(NodeId);
        else
            UIPanelRoot.I.OpenNode(NodeId);
    }

    public void Set(string nodeId, string _unusedText)
    {
        NodeId = nodeId;
        Refresh();
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(NodeId)) return;
        if (GameController.I == null) return;

        var node = GameController.I.GetNode(NodeId);
        if (node == null) return;

        var sb = new StringBuilder();
        sb.Append(node.Name);

        // 1) Anomaly
        if (node.HasAnomaly)
            sb.Append(" <color=red>(!)</color>");

        // 2) Node coarse status
        if (node.Status == NodeStatus.Secured)
        {
            sb.Append("\n<color=green>[已收容]</color>");
        }
        else
        {
            var tasks = node.Tasks;
            if (tasks != null)
            {
                var inv = tasks.Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Investigate).ToList();
                var con = tasks.Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain).ToList();

                if (inv.Count > 0)
                {
                    var t = inv.OrderByDescending(x => x.Progress)
                               .ThenByDescending(x => x.AssignedAgentIds != null && x.AssignedAgentIds.Count > 0)
                               .First();

                    bool hasSquad = t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0;
                    if (hasSquad && t.Progress <= EPS)
                        sb.Append("\n<color=#FFD700>调查：待开始</color>");
                    else
                        sb.Append($"\n<color=#FFD700>调查中 {(int)(GetTaskProgress01(t) * 100)}%</color>");

                    if (inv.Count > 1)
                        sb.Append($" <color=#FFD700>(+{inv.Count - 1})</color>");
                }

                if (con.Count > 0)
                {
                    var t = con.OrderByDescending(x => x.Progress)
                               .ThenByDescending(x => x.AssignedAgentIds != null && x.AssignedAgentIds.Count > 0)
                               .First();

                    bool hasSquad = t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0;
                    if (hasSquad && t.Progress <= EPS)
                        sb.Append("\n<color=#00FFFF>收容：待开始</color>");
                    else
                        sb.Append($"\n<color=#00FFFF>收容中 {(int)(GetTaskProgress01(t) * 100)}%</color>");

                    if (con.Count > 1)
                        sb.Append($" <color=#00FFFF>(+{con.Count - 1})</color>");
                }

                var known = node.KnownAnomalyDefIds ?? new List<string>();
                var contained = node.ManagedAnomalies != null
                    ? node.ManagedAnomalies.Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                        .Select(m => m.AnomalyId)
                        .ToList()
                    : new List<string>();

                var inProgressDefIds = new HashSet<string>(tasks
                    .Where(t => t != null && t.State == TaskState.Active &&
                                (t.Type == TaskType.Contain || t.Type == TaskType.Manage) &&
                                !string.IsNullOrEmpty(t.SourceAnomalyId))
                    .Select(t => t.SourceAnomalyId));

                int containableCount = known
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Except(contained.Where(id => !string.IsNullOrEmpty(id)))
                    .Except(inProgressDefIds)
                    .Count();

                var manageDefIds = new HashSet<string>(tasks
                    .Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage &&
                                t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0 &&
                                !string.IsNullOrEmpty(t.SourceAnomalyId))
                    .Select(t => t.SourceAnomalyId));

                int manageNoSource = tasks
                    .Count(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage &&
                                t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0 &&
                                string.IsNullOrEmpty(t.SourceAnomalyId));

                int managingCount = manageDefIds.Count + manageNoSource;

                if (con.Count == 0 && containableCount > 0)
                    sb.Append($"\n<color=#00FFFF>可收容 {containableCount}</color>");

                if (managingCount > 0)
                    sb.Append($"\n<color=#00FFFF>管理中 {managingCount}</color>");

                // 3) Squad count (sum of active task squads, distinct)
                int busy = tasks
                    .Where(t => t != null && t.State == TaskState.Active && t.AssignedAgentIds != null)
                    .SelectMany(t => t.AssignedAgentIds)
                    .Distinct()
                    .Count();

                if (busy > 0)
                    sb.Append($"\n[{busy} 人在编]");
            }
        }

        int pendingEvents = node.HasPendingEvent ? node.PendingEvents.Count : 0;
        if (pendingEvents > 0)
        {
            sb.Append($"\n<color=#FFA500>E:{pendingEvents}</color>");
        }

        if (label) label.text = sb.ToString();
    }

    private static float GetTaskProgress01(NodeTask task)
    {
        if (task == null) return 0f;
        int baseDays = Mathf.Max(1, DataRegistry.Instance.GetTaskBaseDaysWithWarn(task.Type, 1));
        return Mathf.Clamp01(task.Progress / baseDays);
    }
}
