using System;
using System.Collections.Generic;
using Core;
using UnityEngine;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Prefabs (请把 Assets/Prefabs/UI 下的文件拖进来)")]
    [SerializeField] private NodePanelView nodePanelPrefab;
    [SerializeField] private EventPanel eventPanelPrefab;
    [SerializeField] private NewsPanel newsPanelPrefab;
    [SerializeField] private AgentPickerView agentPickerPrefab;
    [SerializeField] private ConfirmDialog confirmDialogPrefab;

    // --- 运行时实例 (自动生成) ---
    private NodePanelView _nodePanel;
    private EventPanel _eventPanel;
    private NewsPanel _newsPanel;
    private AgentPickerView _agentPicker;
    private ConfirmDialog _confirmDialog;

    private string _currentNodeId;

    private void Awake()
    {
        I = this;
    }

    private void OnEnable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        if (GameController.I != null) GameController.I.OnStateChanged -= OnGameStateChanged;
    }

    void OnGameStateChanged()
    {
        // 1. 如果节点面板开着，刷新它
        if (_nodePanel != null && _nodePanel.gameObject.activeSelf)
            _nodePanel.Refresh();

        // 2. 尝试弹事件
        TryAutoOpenEvent();
    }

    // ================== NODE PANEL ==================

    public void OpenNode(string nodeId)
    {
        _currentNodeId = nodeId;
        
        EnsureNodePanel();
        _nodePanel.Show(nodeId);
    }

    public void CloseNode()
    {
        if (_nodePanel) _nodePanel.Hide();
        _currentNodeId = null;
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
        // 注入逻辑：告诉面板，当按钮被点击时该找谁
        _nodePanel.Init(
            onInvestigate: () => OpenPicker(AgentPickerView.Mode.Investigate),
            onContain: () => OpenPicker(AgentPickerView.Mode.Contain),
            onClose: () => CloseNode()
        );
    }

    // ================== AGENT PICKER ==================

    void OpenPicker(AgentPickerView.Mode mode)
    {
        if (!_agentPicker)
        {
            if (!agentPickerPrefab) return;
            _agentPicker = Instantiate(agentPickerPrefab, transform);
        }

        // 准备数据
        var agents = GameController.I.State.Agents;
        var n = GameController.I.GetNode(_currentNodeId);
        var pre = new List<string>();
        if (n != null && n.AssignedAgentIds != null) pre.AddRange(n.AssignedAgentIds);

        _agentPicker.transform.SetAsLastSibling(); // 确保在最上层
        _agentPicker.Show(
            mode,
            _currentNodeId,
            agents,
            pre,
            isBusyOtherNode: (aid) => GameControllerTaskExt.AreAgentsBusy(GameController.I, new List<string>{aid}, _currentNodeId),
            onConfirm: (ids) =>
            {
                if (mode == AgentPickerView.Mode.Investigate) GameController.I.AssignInvestigate(_currentNodeId, ids);
                else GameController.I.AssignContain(_currentNodeId, ids);
                
                RefreshNodePanel(); // 刷新底下的面板
            },
            onCancel: () => { },
            multiSelect: true
        );
    }

    // ================== OTHERS ==================

    public void OpenNews()
    {
        if (!_newsPanel && newsPanelPrefab) _newsPanel = Instantiate(newsPanelPrefab, transform);
        if (_newsPanel) 
        {
            _newsPanel.Show();
            _newsPanel.transform.SetAsLastSibling();
        }
    }

    void TryAutoOpenEvent()
    {
        // 如果有高级弹窗（Picker）开着，先不弹事件
        if (_agentPicker && _agentPicker.IsShown) return;

        var list = GameController.I.State.PendingEvents;
        if (list == null || list.Count == 0) return;

        if (!_eventPanel && eventPanelPrefab) _eventPanel = Instantiate(eventPanelPrefab, transform);
        
        // 只有当前没开才弹（避免重复 Refresh 导致闪烁）
        if (_eventPanel && !_eventPanel.gameObject.activeSelf)
        {
            _eventPanel.gameObject.SetActive(true);
            _eventPanel.transform.SetAsLastSibling();
            _eventPanel.Show(list[0]);
        }
    }
    
    public void CloseEvent() 
    { 
        if (_eventPanel) _eventPanel.gameObject.SetActive(false); 
    }

    // ================== COMPATIBILITY ==================
    // 兼容可能还存在的旧按钮调用，避免报错
    public void AssignInvestigate_A1() => Debug.Log("Old button clicked");
    public void AssignInvestigate_A2() => Debug.Log("Old button clicked");
    public void AssignInvestigate_A3() => Debug.Log("Old button clicked");
    public void AssignContain_A1() => Debug.Log("Old button clicked");
    public void AssignContain_A2() => Debug.Log("Old button clicked");
    public void AssignContain_A3() => Debug.Log("Old button clicked");
    public void CloseAll() { CloseNode(); CloseEvent(); if(_newsPanel)_newsPanel.Hide(); if(_agentPicker)_agentPicker.Hide(); }
}