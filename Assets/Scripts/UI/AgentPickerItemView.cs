// <EXPORT_BLOCK>
using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerItemView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    private Image background;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text attrText;
    [SerializeField] private TMP_Text busyTagText;

    // 不再依赖 SelectedMark GameObject，直接用颜色
    [SerializeField] private GameObject selectedIcon; // 可选：保留一个勾选图标

    [Header("Style Colors")]
    private Color colNormal = new Color(0.18f, 0.18f, 0.18f, 0.05f); // 极淡灰
    private Color colSelected = new Color(0f, 0.68f, 0.71f, 0.8f); // 战术青 (高亮)
    private Color colBusy = new Color(0.3f, 0.1f, 0.1f, 0.4f); // 暗红

    public string AgentId { get; private set; }
    private bool _isBusy;

    private static int ResolveLevel(AgentState agent, string agentId)
    {
        if (agent != null && agent.Level > 0) return agent.Level;
        if (!string.IsNullOrEmpty(agentId))
        {
            var gc = GameController.I;
            var list = gc?.State?.Agents;
            if (list != null)
            {
                foreach (var a in list)
                {
                    if (a == null) continue;
                    if (a.Id == agentId && a.Level > 0) return a.Level;
                }
            }
        }

        return 1;
    }

    private static string FormatName(AgentState agent, string displayName, string agentId)
    {
        string baseName = string.IsNullOrEmpty(displayName) ? agentId : displayName;
        int level = Mathf.Max(1, ResolveLevel(agent, agentId));
        return $"{baseName}  Lv{level}";
    }

    private void BindCore(
        AgentState agent,
        string displayName,
        string agentId,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText)
    {
        AgentId = agentId;
        _isBusy = isBusyOtherNode;

        if (!button) button = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();

        if (nameText) nameText.text = FormatName(agent, displayName, agentId);
        if (attrText) attrText.text = attrLine;

        if (busyTagText)
        {
            string statusLine = string.IsNullOrEmpty(busyText) ? (_isBusy ? "BUSY" : "") : busyText;
            bool showStatus = !string.IsNullOrEmpty(statusLine);
            busyTagText.gameObject.SetActive(showStatus);
            busyTagText.text = showStatus ? statusLine : "";
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = !_isBusy;
            button.onClick.AddListener(() => onClick?.Invoke(AgentId));
        }

        UpdateVisuals(selected);
    }

    public void Bind(
        AgentState agent,
        string displayName,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText = null)
    {
        string agentId = agent != null ? agent.Id : string.Empty;
        BindCore(agent, displayName, agentId, attrLine, isBusyOtherNode, selected, onClick, busyText);
    }

    // Backward-compatible overload (legacy signature)
    public void Bind(
        AgentState agent,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText = null)
    {
        string displayName = agent == null ? "" : (string.IsNullOrEmpty(agent.Name) ? agent.Id : agent.Name);
        Bind(agent, displayName, attrLine, isBusyOtherNode, selected, onClick, busyText);
    }

    // Backward-compatible overload (legacy callers)
    public void Bind(
        string agentId,
        string displayName,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText = null)
    {
        BindCore(null, displayName, agentId, attrLine, isBusyOtherNode, selected, onClick, busyText);
    }

    public void BindSimple(string displayName, string attrLine, string statusLine, bool selected = false)
    {
        AgentId = string.Empty;
        string statusText = statusLine ?? string.Empty;
        bool isIdle = statusText.IndexOf("IDLE", StringComparison.OrdinalIgnoreCase) >= 0;
        _isBusy = !string.IsNullOrEmpty(statusText) && !isIdle;

        if (!button) button = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();

        if (nameText) nameText.text = displayName ?? string.Empty;
        if (attrText) attrText.text = attrLine;

        if (busyTagText)
        {
            busyTagText.gameObject.SetActive(!string.IsNullOrEmpty(statusLine));
            busyTagText.text = statusLine ?? string.Empty;
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        UpdateVisuals(selected);
    }

    public void SetSelected(bool selected)
    {
        UpdateVisuals(selected);
    }

    void UpdateVisuals(bool selected)
    {
        // 1. 背景颜色逻辑
        if (background)
        {
            if (_isBusy)
            {
                background.color = colBusy;
            }
            else if (selected)
            {
                background.color = colSelected; // 选中：亮青色
            }
            else
            {
                background.color = colNormal; // 普通：透明
            }
        }

        // 2. 勾选图标 (辅助)
        if (selectedIcon) selectedIcon.SetActive(selected);

        // 3. 选中时文字变亮/变黑以适应背景
        if (nameText) nameText.color = selected ? Color.black : Color.white;
        if (attrText) attrText.color = selected ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.8f, 0.8f, 0.8f);
    }
}
// </EXPORT_BLOCK>
