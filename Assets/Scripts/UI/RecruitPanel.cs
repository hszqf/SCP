using System;
using System.Collections.Generic;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecruitPanel : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text statusText;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text cancelLabel;

    [Header("Agent List")]
    [SerializeField] private Transform agentListContent;
    [SerializeField] private ScrollRect agentListScrollRect;

    private readonly List<GameObject> _agentItems = new();

    private void Awake()
    {
        EnsureRuntimeUI();
        BindButtons();
        gameObject.SetActive(false);
    }

    public void Show()
    {
        Refresh();
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (GameController.I == null) return;

        int hireCost = GetHireCost();
        int money = GameController.I.State?.Money ?? 0;
        bool canAfford = money >= hireCost;

        if (titleText) titleText.text = "Personnel Management";
        if (moneyText) moneyText.text = $"Money: {money}";
        if (costText) costText.text = $"HireCost: {hireCost}";
        if (statusText) statusText.text = canAfford ? "Ready" : "资金不足";

        if (confirmButton) confirmButton.interactable = canAfford;
        if (confirmLabel) confirmLabel.text = "Hire";
        if (cancelLabel) cancelLabel.text = "Close";

        // Rebuild agent list to show current status
        RebuildAgentList();
    }

    private void BindButtons()
    {
        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);
        }

        if (cancelButton)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(Hide);
        }
    }

    private int GetHireCost()
    {
        return DataRegistry.Instance.GetBalanceIntWithWarn("HireCost", 100);
    }

    private void RebuildAgentList()
    {
        // Clear existing items
        foreach (var item in _agentItems)
        {
            if (item) Destroy(item);
        }
        _agentItems.Clear();

        if (agentListContent == null)
        {
            // Agent list not set up yet
            return;
        }

        var gc = GameController.I;
        if (gc == null || gc.State?.Agents == null) return;

        // Create an item for each agent
        foreach (var agent in gc.State.Agents)
        {
            if (agent == null) continue;

            // Get busy status using BuildAgentBusyText
            string busyText = Sim.BuildAgentBusyText(gc.State, agent.Id);
            string statusText = string.IsNullOrEmpty(busyText) ? "Idle" : busyText;

            // Create agent item UI
            var item = CreateAgentListItem(agentListContent, agent.Name, BuildAgentAttrLine(agent), statusText);
            _agentItems.Add(item);
        }
    }

    private static string BuildAgentAttrLine(AgentState a)
    {
        if (a == null) return "";
        return $"P{a.Perception} O{a.Operation} R{a.Resistance} Pow{a.Power}";
    }

    private static GameObject CreateAgentListItem(Transform parent, string name, string attrLine, string statusLine)
    {
        var itemGO = new GameObject("AgentItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        itemGO.transform.SetParent(parent, false);

        var rt = itemGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 70f);

        var img = itemGO.GetComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);

        // Name text
        var nameText = CreateText(itemGO.transform, "Name", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(-20f, 24f), new Vector2(10f, -5f), 20, TextAlignmentOptions.Left);
        nameText.text = name;
        nameText.fontStyle = FontStyles.Bold;

        // Attributes text
        var attrText = CreateText(itemGO.transform, "Attributes", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(-20f, 18f), new Vector2(10f, -3f), 16, TextAlignmentOptions.Left);
        attrText.text = attrLine;
        attrText.color = new Color(0.8f, 0.8f, 0.8f);

        // Status text
        var statusText = CreateText(itemGO.transform, "Status", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            new Vector2(-20f, 18f), new Vector2(10f, 5f), 15, TextAlignmentOptions.Left);
        statusText.text = statusLine;
        statusText.color = statusLine == "Idle" ? new Color(0.4f, 0.8f, 0.4f) : new Color(1f, 0.7f, 0.3f);

        return itemGO;
    }

    private void OnConfirm()
    {
        if (GameController.I == null) return;

        int hireCost = GetHireCost();
        if (!GameController.I.TryHireAgent(hireCost, out var agent))
        {
            if (statusText) statusText.text = "资金不足";
            Refresh();
            return;
        }

        if (statusText) statusText.text = $"已招募 {agent.Name}";
        Refresh();
    }

    private void EnsureRuntimeUI()
    {
        if (titleText && moneyText && costText && confirmButton && cancelButton) return;
        BuildRuntimeUI();
    }

    private void BuildRuntimeUI()
    {
        var rootRT = GetComponent<RectTransform>();
        if (!rootRT) rootRT = gameObject.AddComponent<RectTransform>();

        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = new Vector2(800f, 600f); // Increased size for agent list
        rootRT.anchoredPosition = Vector2.zero;

        var bg = gameObject.GetComponent<Image>();
        if (!bg) bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        bg.raycastTarget = true;

        var panel = new GameObject("Panel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(720f, 520f); // Increased size
        panelRT.anchoredPosition = Vector2.zero;
        var panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

        titleText = CreateText(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 60f), new Vector2(0f, -10f), 36, TextAlignmentOptions.Center);

        // Agent list scroll area
        CreateAgentListScrollArea(panel.transform);

        moneyText = CreateText(panel.transform, "MoneyText", new Vector2(0f, 0.35f), new Vector2(1f, 0.35f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 50f), new Vector2(0f, 40f), 24, TextAlignmentOptions.Center);

        costText = CreateText(panel.transform, "CostText", new Vector2(0f, 0.25f), new Vector2(1f, 0.25f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 50f), new Vector2(0f, 20f), 24, TextAlignmentOptions.Center);

        statusText = CreateText(panel.transform, "StatusText", new Vector2(0f, 0.15f), new Vector2(1f, 0.15f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 50f), new Vector2(0f, 0f), 20, TextAlignmentOptions.Center);

        var btnRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        btnRow.transform.SetParent(panel.transform, false);
        var btnRowRT = btnRow.GetComponent<RectTransform>();
        btnRowRT.anchorMin = new Vector2(0f, 0f);
        btnRowRT.anchorMax = new Vector2(1f, 0f);
        btnRowRT.pivot = new Vector2(0.5f, 0f);
        btnRowRT.sizeDelta = new Vector2(0f, 70f);
        btnRowRT.anchoredPosition = new Vector2(0f, 10f);

        var hlg = btnRow.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(40, 40, 10, 10);
        hlg.spacing = 20f;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = true;

        confirmButton = CreateButton(btnRow.transform, "ConfirmButton", "Hire", out confirmLabel);
        cancelButton = CreateButton(btnRow.transform, "CancelButton", "Close", out cancelLabel);
    }

    private void CreateAgentListScrollArea(Transform parent)
    {
        // Create scroll rect container
        var scrollGO = new GameObject("AgentListScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(parent, false);
        var scrollRT = scrollGO.GetComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0f, 0.4f);
        scrollRT.anchorMax = new Vector2(1f, 0.95f);
        scrollRT.offsetMin = new Vector2(20f, 0f);
        scrollRT.offsetMax = new Vector2(-20f, -60f);

        var scrollImg = scrollGO.GetComponent<Image>();
        scrollImg.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        agentListScrollRect = scrollGO.GetComponent<ScrollRect>();

        // Create viewport
        var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = new Vector2(5f, 5f);
        viewportRT.offsetMax = new Vector2(-5f, -5f);
        
        var viewportImg = viewportGO.GetComponent<Image>();
        viewportImg.color = Color.clear;

        var mask = viewportGO.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        // Create content container with VerticalLayoutGroup
        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;

        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(5, 5, 5, 5);
        vlg.spacing = 5f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;

        var csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        agentListContent = contentGO.transform;

        // Configure ScrollRect
        agentListScrollRect.content = contentRT;
        agentListScrollRect.viewport = viewportRT;
        agentListScrollRect.horizontal = false;
        agentListScrollRect.vertical = true;
        agentListScrollRect.movementType = ScrollRect.MovementType.Clamped;
    }

    private static TMP_Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 anchoredPos, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = align;
        text.enableWordWrapping = false;
        return text;
    }

    private static Button CreateButton(Transform parent, string name, string label, out TMP_Text labelText)
    {
        var btnGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);
        var img = btnGO.GetComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        labelText = textGO.GetComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 26;
        labelText.alignment = TextAlignmentOptions.Center;

        return btnGO.GetComponent<Button>();
    }
}
