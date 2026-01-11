using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Modal")]
    [SerializeField] private GameObject dim;
    [SerializeField] private Button dimButton;

    [Header("Node Panel")]
    [SerializeField] private GameObject nodePanel;
    [SerializeField] private TMP_Text nodeTitle;
    [SerializeField] private TMP_Text nodeStatus;
    [SerializeField] private TMP_Text progressText;

    [Header("Node Panel Buttons")]
    [SerializeField] private Button investigateButton;
    [SerializeField] private Button containButton;
    [SerializeField] private Button closeNodeButton;

    [Header("Event Panel")]
    [SerializeField] private EventPanel eventPanel;

    [Header("News Panel")]
    [SerializeField] private NewsPanel newsPanel;
    [SerializeField] private Button closeNewsButton;

    [Header("Agent Picker (New)")]
    [SerializeField] private AgentPickerView agentPickerPrefab;
    private AgentPickerView _agentPickerInstance;

    [Header("Optional")]
    [SerializeField] private ConfirmDialog confirmDialog;
    [SerializeField] private Button withdrawButton;

    private string _currentNodeId;

    private void Awake()
    {
        I = this;
        BindButtons();
        ResetUI();
    }

    private void OnEnable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged -= OnGameStateChanged;
    }

    // ----------------INIT & BINDING----------------

    void BindButtons()
    {
        // Node Panel
        if (investigateButton) { investigateButton.onClick.RemoveAllListeners(); investigateButton.onClick.AddListener(OnInvestigateClicked); }
        if (containButton) { containButton.onClick.RemoveAllListeners(); containButton.onClick.AddListener(OnContainClicked); }
        if (closeNodeButton) { closeNodeButton.onClick.RemoveAllListeners(); closeNodeButton.onClick.AddListener(CloseNode); }

        // News
        if (closeNewsButton) { closeNewsButton.onClick.RemoveAllListeners(); closeNewsButton.onClick.AddListener(CloseNews); }

        // Dim
        if (!dimButton && dim) dimButton = dim.GetComponent<Button>();
        if (dimButton) { dimButton.onClick.RemoveAllListeners(); dimButton.onClick.AddListener(CloseAll); }
    }

    void ResetUI()
    {
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);
        if (_agentPickerInstance) _agentPickerInstance.Hide();
        
        _currentNodeId = null;
        UpdateDimState();
    }

    // ----------------CORE: STATE SYNC----------------

    void OnGameStateChanged()
    {
        if (!string.IsNullOrEmpty(_currentNodeId) && nodePanel && nodePanel.activeSelf)
            RefreshNodePanel();
        
        TryAutoOpenEvent();
    }

    // ----------------CORE: DIM MANAGEMENT----------------

    // 核心修复：根据当前谁开着，决定 Dim 在哪，或者关掉 Dim
    void UpdateDimState()
    {
        if (!dim) return;

        RectTransform top = GetTopModal();
        if (top == null)
        {
            dim.SetActive(false);
        }
        else
        {
            dim.SetActive(true);
            top.SetAsLastSibling(); // 确保窗口在最前
            // Dim 插在窗口前一位
            dim.transform.SetSiblingIndex(Mathf.Max(0, top.GetSiblingIndex() - 1));
        }
    }

    RectTransform GetTopModal()
    {
        // 优先级：Confirm > Picker > Event > News > NodePanel
        if (confirmDialog && confirmDialog.gameObject.activeSelf) return (RectTransform)confirmDialog.transform;
        if (_agentPickerInstance && _agentPickerInstance.IsShown) return (RectTransform)_agentPickerInstance.transform;
        if (eventPanel && eventPanel.gameObject.activeSelf) return (RectTransform)eventPanel.transform;
        if (newsPanel && newsPanel.gameObject.activeSelf) return (RectTransform)newsPanel.transform;
        if (nodePanel && nodePanel.activeSelf) return (RectTransform)nodePanel.transform;
        return null;
    }

    // ----------------INTERACTION: NODE----------------

    public void OpenNode(string nodeId)
    {
        _currentNodeId = nodeId;
        if (nodePanel) nodePanel.SetActive(true);
        RefreshNodePanel();
        UpdateDimState();
    }

    public void CloseNode()
    {
        if (nodePanel) nodePanel.SetActive(false);
        _currentNodeId = null;
        UpdateDimState();
    }

    void RefreshNodePanel()
    {
        if (GameController.I == null) return;
        var n = GameController.I.GetNode(_currentNodeId);
        if (n == null) return;

        if (nodeTitle) nodeTitle.text = n.Name;
        
        // 简单的状态文本
        string statusStr = n.Status.ToString();
        if (n.HasAnomaly) statusStr += " <color=red>[ANOMALY]</color>";
        if (nodeStatus) nodeStatus.text = statusStr;

        // 进度
        float p = (n.Status == NodeStatus.Investigating) ? n.InvestigateProgress : 
                  (n.Status == NodeStatus.Containing) ? n.ContainProgress : 0f;
        
        if (progressText) 
            progressText.text = $"{(int)(p*100)}% | Agent: {n.AssignedAgentId ?? "-"}";
    }

    // ----------------INTERACTION: DISPATCH (PICKER)----------------

    void OnInvestigateClicked() => OpenPicker(AgentPickerView.Mode.Investigate);
    void OnContainClicked() => OpenPicker(AgentPickerView.Mode.Contain);

    void OpenPicker(AgentPickerView.Mode mode)
    {
        if (!agentPickerPrefab) { Debug.LogError("AgentPickerPrefab missing!"); return; }
        if (!_agentPickerInstance)
        {
             // 实例化到 Canvas 下 (和 NodePanel 同级)
             Transform parent = nodePanel ? nodePanel.transform.parent : transform;
             _agentPickerInstance = Instantiate(agentPickerPrefab, parent);
        }

        var agents = GameController.I.State.Agents;
        var n = GameController.I.GetNode(_currentNodeId);
        var preSelected = new List<string>();
        if (n != null && !string.IsNullOrEmpty(n.AssignedAgentId)) preSelected.Add(n.AssignedAgentId);

        _agentPickerInstance.Show(
            mode,
            _currentNodeId,
            agents,
            preSelected,
            isBusyOtherNode: (id) => IsAgentBusy(id, _currentNodeId),
            onConfirm: (selectedIds) => 
            {
                // 执行派遣
                string agentId = selectedIds.Count > 0 ? selectedIds[0] : null;
                if (mode == AgentPickerView.Mode.Investigate) GameController.I.AssignInvestigate(_currentNodeId, agentId);
                else GameController.I.AssignContain(_currentNodeId, agentId);
                
                RefreshNodePanel();
                UpdateDimState(); // 关键：关闭后刷新遮罩
            },
            onCancel: () => 
            {
                UpdateDimState(); // 关键：取消后也刷新遮罩
            }
        );

        UpdateDimState(); // 打开时刷新遮罩
    }

    bool IsAgentBusy(string agentId, string currentTaskNodeId)
    {
        foreach (var node in GameController.I.State.Nodes)
        {
            if (node.Id == currentTaskNodeId) continue;
            if (node.AssignedAgentId == agentId && 
               (node.Status == NodeStatus.Investigating || node.Status == NodeStatus.Containing))
                return true;
        }
        return false;
    }

    // ----------------INTERACTION: OTHERS----------------

    public void OpenNews() { if(newsPanel) { newsPanel.Show(); UpdateDimState(); } }
    public void CloseNews() { if(newsPanel) { newsPanel.Hide(); UpdateDimState(); } }

    public void CloseAll()
    {
        if (_agentPickerInstance) _agentPickerInstance.Hide();
        if (eventPanel) eventPanel.gameObject.SetActive(false);
        if (newsPanel) newsPanel.Hide();
        if (nodePanel) nodePanel.SetActive(false);
        _currentNodeId = null;
        UpdateDimState();
    }

    void TryAutoOpenEvent()
    {
        if (GetTopModal() != null) return; // 有窗口开着就不弹
        var list = GameController.I?.State.PendingEvents;
        if (list != null && list.Count > 0 && eventPanel)
        {
             eventPanel.gameObject.SetActive(true);
             eventPanel.Show(list[0]);
             UpdateDimState();
        }
    }
}