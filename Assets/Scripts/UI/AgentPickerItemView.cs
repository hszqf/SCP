using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerItemView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    [SerializeField] private Image accentBar;          // 可选
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text attrText;
    [SerializeField] private TMP_Text busyTagText;
    [SerializeField] private GameObject selectedMark;  // 选中标记（Inactive/Active）

    public string AgentId { get; private set; }

    public void Bind(
        string agentId,
        string displayName,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick)
    {
        AgentId = agentId;

        if (nameText) nameText.text = string.IsNullOrEmpty(displayName) ? agentId : displayName;
        if (attrText) attrText.text = attrLine;

        if (busyTagText)
        {
            busyTagText.gameObject.SetActive(isBusyOtherNode);
            busyTagText.text = isBusyOtherNode ? "BUSY" : "";
        }

        if (selectedMark) selectedMark.SetActive(selected);

        if (!button) button = GetComponent<Button>();
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = !isBusyOtherNode;
            button.onClick.AddListener(() => onClick?.Invoke(agentId));
        }

        // 轻量视觉：忙碌/可用的底色你也可以在 prefab 里调整
        var img = GetComponent<Image>();
        if (img)
        {
            img.color = isBusyOtherNode
                ? new Color(1f, 0.6f, 0.6f, 0.10f)
                : new Color(1f, 1f, 1f, 0.10f);
        }
    }

    public void SetSelected(bool selected)
    {
        if (selectedMark) selectedMark.SetActive(selected);
    }
}

