using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodePanelView : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text progressText;
    
    [Header("Buttons")]
    [SerializeField] private Button investigateButton;
    [SerializeField] private Button containButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backgroundButton; // 蒙版按钮

    // 回调函数
    private Action _onInvestigate;
    private Action _onContain;
    private Action _onClose;

    private string _nodeId;

    public void Init(Action onInvestigate, Action onContain, Action onClose)
    {
        _onInvestigate = onInvestigate;
        _onContain = onContain;
        _onClose = onClose;

        if (investigateButton) { investigateButton.onClick.RemoveAllListeners(); investigateButton.onClick.AddListener(() => _onInvestigate?.Invoke()); }
        if (containButton) { containButton.onClick.RemoveAllListeners(); containButton.onClick.AddListener(() => _onContain?.Invoke()); }
        if (closeButton) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(() => _onClose?.Invoke()); }
        
        // 蒙版点击 = 关闭
        if (backgroundButton) { backgroundButton.onClick.RemoveAllListeners(); backgroundButton.onClick.AddListener(() => _onClose?.Invoke()); }
    }

    public void Show(string nodeId)
    {
        _nodeId = nodeId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        Refresh();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(_nodeId)) return;
        if (GameController.I == null) return;

        var n = GameController.I.GetNode(_nodeId);
        if (n == null) return;

        if (titleText) titleText.text = n.Name;

        string s = $"{n.Status}";
        if (n.HasAnomaly) s += " <color=red>[ANOMALY]</color>";
        if (statusText) statusText.text = s;

        if (progressText)
        {
            float p = (n.Status == NodeStatus.Investigating) ? n.InvestigateProgress : n.ContainProgress;
            int count = (n.AssignedAgentIds != null) ? n.AssignedAgentIds.Count : 0;
            progressText.text = $"Progress: {(int)(p * 100)}% | Squad: {count}";
        }
    }
}