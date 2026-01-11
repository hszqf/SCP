using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodeButton : MonoBehaviour
{
    public string NodeId;
    [SerializeField] private TMP_Text label;
    private Button _btn;

    private void Awake()
    {
        _btn = GetComponent<Button>();
        _btn.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        if (UIPanelRoot.I) UIPanelRoot.I.OpenNode(NodeId);
    }

    public void Set(string nodeId, string text)
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

        // --- Visual State Mapping ---
        string txt = node.Name;

        // 1. Anomaly
        if (node.HasAnomaly)
            txt += " <color=red>(!)</color>";

        // 2. Task Status
        if (node.Status == NodeStatus.Investigating)
            txt += $"\n<color=#FFD700>调查中 {(int)(node.InvestigateProgress * 100)}%</color>";
        else if (node.Status == NodeStatus.Containing)
            txt += $"\n<color=#00FFFF>收容中 {(int)(node.ContainProgress * 100)}%</color>";
        else if (node.Status == NodeStatus.Secured)
            txt += "\n<color=green>[已收容]</color>";

        // 3. Squad Count (New Feature)
        if (node.AssignedAgentIds != null && node.AssignedAgentIds.Count > 0)
            txt += $"\n[{node.AssignedAgentIds.Count} 人在编]";

        if (label) label.text = txt;
    }
}