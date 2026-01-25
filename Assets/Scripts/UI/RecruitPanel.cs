using System;
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

        if (titleText) titleText.text = "Recruit";
        if (moneyText) moneyText.text = $"Money: {money}";
        if (costText) costText.text = $"HireCost: {hireCost}";
        if (statusText) statusText.text = canAfford ? "Ready" : "资金不足";

        if (confirmButton) confirmButton.interactable = canAfford;
        if (confirmLabel) confirmLabel.text = "Hire";
        if (cancelLabel) cancelLabel.text = "Close";
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
        rootRT.sizeDelta = new Vector2(720f, 420f);
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
        panelRT.sizeDelta = new Vector2(640f, 340f);
        panelRT.anchoredPosition = Vector2.zero;
        var panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

        titleText = CreateText(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 70f), new Vector2(0f, -10f), 42, TextAlignmentOptions.Center);

        moneyText = CreateText(panel.transform, "MoneyText", new Vector2(0f, 0.6f), new Vector2(1f, 0.6f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(0f, 40f), 30, TextAlignmentOptions.Center);

        costText = CreateText(panel.transform, "CostText", new Vector2(0f, 0.45f), new Vector2(1f, 0.45f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(0f, 20f), 30, TextAlignmentOptions.Center);

        statusText = CreateText(panel.transform, "StatusText", new Vector2(0f, 0.25f), new Vector2(1f, 0.25f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(0f, 0f), 24, TextAlignmentOptions.Center);

        var btnRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        btnRow.transform.SetParent(panel.transform, false);
        var btnRowRT = btnRow.GetComponent<RectTransform>();
        btnRowRT.anchorMin = new Vector2(0f, 0f);
        btnRowRT.anchorMax = new Vector2(1f, 0f);
        btnRowRT.pivot = new Vector2(0.5f, 0f);
        btnRowRT.sizeDelta = new Vector2(0f, 90f);
        btnRowRT.anchoredPosition = new Vector2(0f, 15f);

        var hlg = btnRow.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(40, 40, 10, 10);
        hlg.spacing = 20f;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = true;

        confirmButton = CreateButton(btnRow.transform, "ConfirmButton", "Hire", out confirmLabel);
        cancelButton = CreateButton(btnRow.transform, "CancelButton", "Close", out cancelLabel);
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
