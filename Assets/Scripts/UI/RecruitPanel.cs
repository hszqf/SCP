using System;
using System.Collections.Generic;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecruitPanel : MonoBehaviour, IModalClosable
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
    [SerializeField] private Button dimmerButton;

    [Header("Agent List")]
    [SerializeField] private RectTransform agentListContent;
    [SerializeField] private ScrollRect agentListScrollRect;
    [SerializeField] private AgentPickerItemView itemPrefab;

    private readonly List<GameObject> _agentItems = new();
    private RecruitCandidate _candidate;

    private void Awake()
    {
        BindButtons();
        LogBindings();
        if (!ValidateBindings()) return;
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!ValidateBindings()) return;
    }

    public void Show()
    {
        if (!ValidateBindings()) return;
        Refresh();
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void CloseFromRoot()
    {
        Hide();
    }

    public void Refresh()
    {
        if (!ValidateBindings()) return;
        if (GameController.I == null) return;

        if (_candidate == null)
        {
            _candidate = GameController.I.GenerateRecruitCandidate();
        }

        int hireCost = _candidate?.cost ?? GetHireCost();
        int candidateLevel = _candidate?.agent?.Level ?? 1;

        int money = GameController.I.State?.Money ?? 0;
        bool canAfford = money >= hireCost;

        if (titleText) titleText.text = "Personnel Management";
        if (moneyText) moneyText.text = $"Money: {money}";
        if (costText) costText.text = $"雇佣费用：{hireCost}（Lv{candidateLevel}）";
        if (statusText) statusText.text = canAfford ? "Ready" : "资金不足";

        if (confirmButton) confirmButton.interactable = canAfford;
        if (confirmLabel) confirmLabel.text = "Hire";
        if (cancelLabel) cancelLabel.text = "Close";

        // Rebuild agent list to show current status
        EnsureListLayout();
        RebuildAgentList();
        EnsureListLayout();
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
            cancelButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close_btn"));
        }

        if (dimmerButton)
        {
            dimmerButton.onClick.RemoveAllListeners();
            dimmerButton.onClick.AddListener(() => UIPanelRoot.I?.CloseTopModal("dimmer"));
        }
    }

    private void LogBindings()
    {
        string closeState = cancelButton ? "ok" : "missing";
        string dimmerState = dimmerButton ? "ok" : "missing";
        Debug.Log($"[UIBind] RecruitPanel close={closeState} dimmer={dimmerState}");
    }

    private int GetHireCost()
    {
        return DataRegistry.Instance.GetBalanceIntWithWarn("HireCost", 100);
    }

    private void RebuildAgentList()
    {
        if (!ValidateBindings()) return;
        EnsureListLayout();
        // Clear existing items (only children, keep content)
        _agentItems.Clear();
        foreach (Transform child in agentListContent)
        {
            if (child) Destroy(child.gameObject);
        }

        if (agentListContent == null || itemPrefab == null)
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
            bool isBusy = !string.IsNullOrEmpty(busyText);
            string statusText = isBusy
                ? $"<color=#FF6666>{busyText}</color>"
                : "<color=#66FF66>IDLE</color>";

            // Create agent item UI from prefab
            var item = Instantiate(itemPrefab, agentListContent, false);
            item.name = $"AgentItem_{agent.Id}";
            string displayName = BuildAgentDisplayName(agent);
            item.Bind(agent, displayName, BuildAgentAttrLine(agent), isBusy, false, null, statusText);
            var itemGo = item.gameObject;
            var le = itemGo.GetComponent<LayoutElement>() ?? itemGo.AddComponent<LayoutElement>();
            le.minHeight = 70f;
            le.preferredHeight = 70f;
            le.flexibleHeight = 0f;

            var rt = itemGo.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }
            _agentItems.Add(item.gameObject);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(agentListContent);
        Canvas.ForceUpdateCanvases();
    }

    private static string BuildAgentAttrLine(AgentState a)
    {
        if (a == null) return "";
        return $"P{a.Perception} O{a.Operation} R{a.Resistance} Pow{a.Power}";
    }

    private static string BuildAgentDisplayName(AgentState a)
    {
        if (a == null) return string.Empty;
        return string.IsNullOrEmpty(a.Name) ? a.Id : a.Name;
    }

    private void OnConfirm()
    {
        if (GameController.I == null) return;

        if (_candidate == null)
        {
            _candidate = GameController.I.GenerateRecruitCandidate();
        }

        if (!GameController.I.TryHireAgent(_candidate, out var agent))
        {
            if (statusText) statusText.text = "资金不足";
            Refresh();
            return;
        }

        if (statusText) statusText.text = $"已招募 {agent.Name}";
        _candidate = GameController.I.GenerateRecruitCandidate();
        Refresh();
        Canvas.ForceUpdateCanvases();
        if (agentListScrollRect) agentListScrollRect.verticalNormalizedPosition = 0f;
    }

    private void EnsureRuntimeUI()
    {
        if (titleText && moneyText && costText && confirmButton && cancelButton) return;
        Debug.LogError("RecruitPanel: UI references missing. Assign via Inspector; runtime UI creation is disabled.");
    }

    private bool ValidateBindings()
    {
        if (!agentListContent || !agentListScrollRect || !itemPrefab)
        {
            Debug.LogError("RecruitPanel: agentListContent/agentListScrollRect/itemPrefab not assigned in Inspector.");
            return false;
        }

        return true;
    }

    private void EnsureListLayout()
    {
        if (!agentListContent || !agentListScrollRect) return;

        var vlg = agentListContent.GetComponent<VerticalLayoutGroup>() ?? agentListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        var csf = agentListContent.GetComponent<ContentSizeFitter>() ?? agentListContent.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (agentListScrollRect.content == null)
        {
            agentListScrollRect.content = agentListContent;
        }
    }

}
