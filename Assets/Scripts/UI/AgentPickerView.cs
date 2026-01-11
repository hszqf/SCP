using System;
using System.Collections.Generic;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private AgentPickerItemView itemPrefab;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button backgroundButton; // 新增：蒙版按钮
    [SerializeField] private TMP_Text titleText;

    private List<AgentPickerItemView> _items = new List<AgentPickerItemView>();
    private HashSet<string> _selected = new HashSet<string>();
    
    private Mode _mode;
    private Action<List<string>> _onConfirm;
    private Action _onCancel;
    private bool _multiSelect;

    public bool IsShown => gameObject.activeSelf;

    public enum Mode { Investigate, Contain }

    private void Awake()
    {
        if (confirmButton) 
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        if (cancelButton) 
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
        // 蒙版点击逻辑
        if (backgroundButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(OnCancelClicked);
        }
    }

    private void Update()
    {
        if (confirmButton) confirmButton.interactable = true;
    }

    public void Show(
        Mode mode,
        string nodeId,
        IEnumerable<AgentState> agents,
        IEnumerable<string> preSelected,
        Func<string, bool> isBusyOtherNode,
        Action<List<string>> onConfirm,
        Action onCancel,
        bool multiSelect = true)
    {
        _mode = mode;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _multiSelect = multiSelect;

        _selected.Clear();
        if (preSelected != null)
        {
            foreach (var id in preSelected) _selected.Add(id);
        }

        gameObject.SetActive(true);
        UpdateTitle(); // 初始化标题

        RefreshList(agents, isBusyOtherNode);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    void RefreshList(IEnumerable<AgentState> agents, Func<string, bool> isBusyOtherNode)
    {
        foreach (Transform child in contentRoot) if (child.gameObject.activeSelf) Destroy(child.gameObject);
        _items.Clear();

        if (!itemPrefab) return;
        
        foreach (var agent in agents)
        {
            var item = Instantiate(itemPrefab, contentRoot);
            item.gameObject.SetActive(true);
            
            bool isBusy = isBusyOtherNode(agent.Id);
            if (_selected.Contains(agent.Id)) isBusy = false;

            item.Bind(
                agent.Id,
                agent.Id,
                $"Agent {agent.Id}",
                isBusy,
                _selected.Contains(agent.Id),
                OnItemClicked
            );
            _items.Add(item);
        }
    }

    void OnItemClicked(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        if (_multiSelect)
        {
            if (_selected.Contains(agentId)) _selected.Remove(agentId);
            else _selected.Add(agentId);
            
            foreach (var it in _items)
                if (it && it.AgentId == agentId) it.SetSelected(_selected.Contains(agentId));
        }
        else
        {
            _selected.Clear();
            _selected.Add(agentId);
            foreach (var it in _items)
                if (it) it.SetSelected(_selected.Contains(it.AgentId));
        }
        
        UpdateTitle(); // 每次点击都更新标题数量
    }

    void UpdateTitle()
    {
        if (!titleText) return;
        string baseTitle = _mode == Mode.Investigate ? "SELECT TEAM" : "CONTAINMENT TEAM";
        // 关键修复：显示已选数量
        titleText.text = $"{baseTitle} ({_selected.Count})";
    }

    void OnConfirmClicked()
    {
        _onConfirm?.Invoke(new List<string>(_selected));
        Hide();
    }

    void OnCancelClicked()
    {
        // 关键逻辑：取消就是不做任何改动，直接关闭
        // 不调用 _onConfirm，所以外部数据不会变
        _onCancel?.Invoke();
        Hide();
    }
}