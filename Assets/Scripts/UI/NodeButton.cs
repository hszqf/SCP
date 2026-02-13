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
        if (node == null || node.Type == 0)
            return;

        var active = node.ActiveAnomalyIds != null ? string.Join(",", node.ActiveAnomalyIds) : "";
        var known = node.KnownAnomalyDefIds != null ? string.Join(",", node.KnownAnomalyDefIds) : "";
        int managedCount = node.ManagedAnomalies?.Count ?? 0;
        int taskCount = node.Tasks?.Count ?? 0;
        int pendingEvents = node.PendingEvents?.Count ?? 0;

        Debug.Log($"[NodeClick] nodeId={node.Id} name={node.Name} hasAnomaly={node.HasAnomaly} active=[{active}] known=[{known}] managedCount={managedCount} tasks={taskCount} pendingEvents={pendingEvents}");
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

        if (_btn) _btn.interactable = node.Type != 0;

        // 只显示城市名字和人口信息，格式：名字\n人口：xxxx
        if (label)
        {
            int displayPopulation = node.Population;
            if (node.Type == 0 && DispatchAnimationSystem.I != null)
                displayPopulation = DispatchAnimationSystem.I.GetVisualAvailableAgentCount();

            label.text = $"{node.Name}\n人口：{displayPopulation}";
        }
    }

    private static float GetTaskProgress01(NodeTask task)
    {
        if (task == null) return 0f;
        int baseDays = GetTaskBaseDays(task);
        float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
        return Mathf.Clamp01(progress / baseDays);
    }

    private static int GetTaskBaseDays(NodeTask task)
    {
        if (task == null) return 1;
        var registry = DataRegistry.Instance;
        if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            return task.InvestigateNoResultBaseDays;
        string anomalyId = task.SourceAnomalyId;
        if (string.IsNullOrEmpty(anomalyId) || registry == null) return 1;
        return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
    }
}
