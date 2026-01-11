using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerView : MonoBehaviour
{
    public enum Mode { Investigate, Contain }

    [Header("Refs")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text selectedCountText;
    [SerializeField] private Button confirmButton;

    [Header("List")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private AgentPickerItemView itemPrefab;

    private readonly List<AgentPickerItemView> _items = new();
    private readonly HashSet<string> _selected = new();

    private Mode _mode;
    private string _nodeId;

    private Action<List<string>> _onConfirm;
    private Action _onCancel;

    public bool IsShown => gameObject.activeSelf;

    public void Show(
        Mode mode,
        string nodeId,
        IEnumerable<AgentState> agents,
        IEnumerable<string> preSelected,
        Func<string, bool> isBusyOtherNode,
        Action<List<string>> onConfirm,
        Action onCancel)
    {
        _mode = mode;
        _nodeId = nodeId;
        _onConfirm = onConfirm;
        _onCancel = onCancel;

        _selected.Clear();
        if (preSelected != null)
            foreach (var id in preSelected) if (!string.IsNullOrEmpty(id)) _selected.Add(id);

        gameObject.SetActive(true);

        WireButtons();
        RebuildList(agents, isBusyOtherNode);
        RefreshHeader();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _onConfirm = null;
        _onCancel = null;
        _nodeId = null;
        _selected.Clear();
    }

    void WireButtons()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() =>
            {
                _onCancel?.Invoke();
                Hide();
            });
        }

        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(() =>
            {
                _onConfirm?.Invoke(_selected.ToList());
                Hide();
            });
        }
    }

    void RebuildList(IEnumerable<AgentState> agents, Func<string, bool> isBusyOtherNode)
    {
        // clear old
        for (int i = 0; i < contentRoot.childCount; i++)
            Destroy(contentRoot.GetChild(i).gameObject);
        _items.Clear();

        foreach (var a in agents)
        {
            var id = a.Id;
            bool busy = isBusyOtherNode != null && isBusyOtherNode(id);

            var row = Instantiate(itemPrefab, contentRoot);
            row.gameObject.SetActive(true);

            string attrs = $"P:{a.Perception}  O:{a.Operation}  R:{a.Resistance}  Pow:{a.Power}";
            bool selected = _selected.Contains(id);

            row.Bind(
                agentId: id,
                displayName: a.Name,
                attrLine: attrs,
                isBusyOtherNode: busy,
                selected: selected,
                onClick: OnItemClicked);

            _items.Add(row);
        }

        // 可选：滚动回顶
        if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;
    }

    void OnItemClicked(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        // 多选：点击切换选中
        if (_selected.Contains(agentId)) _selected.Remove(agentId);
        else _selected.Add(agentId);

        foreach (var it in _items)
            if (it && it.AgentId == agentId) it.SetSelected(_selected.Contains(agentId));

        RefreshHeader();
    }

    void RefreshHeader()
    {
        if (titleText)
            titleText.text = _mode == Mode.Investigate ? "Select Agents - Investigate" : "Select Agents - Contain";

        if (selectedCountText)
            selectedCountText.text = $"Selected: {_selected.Count}";

        if (confirmButton)
            confirmButton.interactable = _selected.Count > 0;
    }
}
