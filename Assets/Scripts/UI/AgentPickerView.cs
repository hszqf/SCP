// Canvas-maintained file: UI/AgentPickerView
// Source: Assets/Scripts/UI/AgentPickerView.cs
// Updated for rule-set: 预定占用
// - PreSelected agents stay BUSY (locked) even in current node.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using Core;
using Settlement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerView : MonoBehaviour, IModalClosable
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
    private HashSet<string> _baseline = new HashSet<string>(); // 用于判断是否有变更（dirty）

    private Mode _mode;
    private Action<List<string>> _onConfirm;
    private Action _onCancel;
    private bool _multiSelect;
    private int _slotsMin = 1;
    private int _slotsMax = int.MaxValue;

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
            cancelButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close btn"));
        }
        // 蒙版点击逻辑
        if (backgroundButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(() => UIPanelRoot.I?.CloseTopModal("dimmer"));
        }
    }

    // Confirm 仅在“有变更”时可点（避免无操作误触 + 触发派遣失败提示）
    private void Update()
    {
        RefreshConfirmInteractable();
    }

    public void Show(
        Mode mode,
        string nodeId,
        IEnumerable<AgentState> agents,
        IEnumerable<string> preSelected,
        Func<string, bool> isBusyOtherNode,
        int slotsMin,
        int slotsMax,
        Action<List<string>> onConfirm,
        Action onCancel,
        bool multiSelect = true)
    {
        _mode = mode;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _multiSelect = multiSelect;
        _slotsMin = Mathf.Max(1, slotsMin);
        _slotsMax = (slotsMax <= 0) ? int.MaxValue : slotsMax;
        if (_slotsMax < _slotsMin) _slotsMax = _slotsMin;

        _selected.Clear();
        _baseline.Clear();
        if (preSelected != null)
        {
            foreach (var id in preSelected)
            {
                _selected.Add(id);
                _baseline.Add(id);
            }
        }

        gameObject.SetActive(true);
        UpdateTitle(); // 初始化标题

        RefreshList(agents, isBusyOtherNode);
        RefreshConfirmInteractable();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        GameControllerTaskExt.LogBusySnapshot(GameController.I, "AgentPickerView.Hide");
    }

    public void CloseFromRoot()
    {
        OnCancelClicked();
    }

    void RefreshList(IEnumerable<AgentState> agents, Func<string, bool> isBusyOtherNode)
    {
        foreach (Transform child in contentRoot) if (child.gameObject.activeSelf) Destroy(child.gameObject);
        _items.Clear();

        if (!itemPrefab) return;

        var gc = GameController.I;
        foreach (var agent in agents)
        {
            var item = Instantiate(itemPrefab, contentRoot);
            item.gameObject.SetActive(true);

            // 预定占用：即便是当前节点的预选成员，也保持 busy（锁定不可点）。
            bool isBusy = isBusyOtherNode(agent.Id);

            // Get busy text using BuildAgentBusyText
            string busyText = (gc != null) ? Sim.BuildAgentBusyText(gc.State, agent.Id) : null;
            string statusText = isBusy
                ? (string.IsNullOrEmpty(busyText) ? "BUSY" : busyText)
                : "IDLE";

            string displayName = string.IsNullOrEmpty(agent.Name) ? agent.Id : agent.Name;
            item.Bind(
                agent,
                displayName,
                BuildAgentAttrLine(agent),
                isBusy,
                _selected.Contains(agent.Id),
                OnItemClicked,
                statusText
            );
            _items.Add(item);
        }
    }

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

    void OnItemClicked(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        if (_multiSelect)
        {
            if (_selected.Contains(agentId)) _selected.Remove(agentId);
            else
            {
                if (_selected.Count >= _slotsMax)
                {
                    Debug.LogWarning($"[TaskDef] slot selection exceeds max. mode={_mode} slotsMax={_slotsMax}");
                    return;
                }
                _selected.Add(agentId);
            }

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
        RefreshConfirmInteractable();
    }

    void UpdateTitle()
    {
        if (!titleText) return;
        string baseTitle = _mode == Mode.Investigate ? "SELECT TEAM" : "CONTAINMENT TEAM";
        titleText.text = $"{baseTitle} ({_selected.Count})";
    }

    bool HasChanges()
    {
        if (_selected.Count != _baseline.Count) return true;
        foreach (var id in _selected)
            if (!_baseline.Contains(id)) return true;
        return false;
    }

    void RefreshConfirmInteractable()
    {
        if (!confirmButton) return;
        bool dirty = HasChanges();
        bool withinMin = _selected.Count >= _slotsMin;
        bool withinMax = _selected.Count <= _slotsMax;
        confirmButton.interactable = dirty && withinMin && withinMax;
    }

    void OnConfirmClicked()
    {
        // 无变更：直接关闭，不触发派遣（也就不会弹出失败提示）。
        if (!HasChanges())
        {
            Hide();
            return;
        }

        if (_selected.Count < _slotsMin || _selected.Count > _slotsMax)
        {
            Debug.LogWarning($"[TaskDef] slot selection invalid. mode={_mode} count={_selected.Count} slotsMin={_slotsMin} slotsMax={_slotsMax}");
            return;
        }

        _onConfirm?.Invoke(new List<string>(_selected));
        Hide();
    }

    void OnCancelClicked()
    {
        // 取消就是不做任何改动，直接关闭
        GameControllerTaskExt.LogBusySnapshot(GameController.I, "AgentPickerView.OnCancelClicked");
        _onCancel?.Invoke();
        Hide();
    }
}
// </EXPORT_BLOCK>
