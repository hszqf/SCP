// <EXPORT_BLOCK>
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerItemView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text attrText;
    [SerializeField] private TMP_Text busyTagText;
    
    // 不再依赖 SelectedMark GameObject，直接用颜色
    [SerializeField] private GameObject selectedIcon; // 可选：保留一个勾选图标

    [Header("Style Colors")]
    private Color colNormal = new Color(1f, 1f, 1f, 0.05f); // 极淡灰
    private Color colSelected = new Color(0f, 0.68f, 0.71f, 0.8f); // 战术青 (高亮)
    private Color colBusy = new Color(0.3f, 0.1f, 0.1f, 0.4f); // 暗红

    public string AgentId { get; private set; }
    private bool _isBusy;

    public void Bind(
        string agentId,
        string displayName,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText = null)
    {
        AgentId = agentId;
        _isBusy = isBusyOtherNode;

        if (!button) button = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();

        // 文本设置
        if (nameText) nameText.text = string.IsNullOrEmpty(displayName) ? agentId : displayName;
        if (attrText) attrText.text = attrLine;

        // 忙碌状态 - 显示完整的 busy text 或默认 "BUSY"
        if (busyTagText)
        {
            busyTagText.gameObject.SetActive(_isBusy);
            busyTagText.text = _isBusy ? (string.IsNullOrEmpty(busyText) ? "BUSY" : busyText) : "";
        }

        // 按钮交互
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = !_isBusy;
            button.onClick.AddListener(() => onClick?.Invoke(agentId));
        }

        // 立即刷新视觉
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
