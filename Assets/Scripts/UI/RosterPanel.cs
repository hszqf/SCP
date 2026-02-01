using System;
using System.Collections.Generic;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RosterPanel : MonoBehaviour, IModalClosable
{
    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;

    [Header("Buttons")]
    [SerializeField] private Button hireButton;
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text cancelLabel;
    [SerializeField] private Button dimmerButton;

    [Header("Agent List")]
    [SerializeField] private RectTransform agentListContent;
    [SerializeField] private ScrollRect agentListScrollRect;
    [SerializeField] private AgentPickerItemView itemPrefab;

    private readonly List<GameObject> _agentItems = new();

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
        UIEvents.AgentsChanged += Refresh;
    }

    private void OnDisable()
    {
        UIEvents.AgentsChanged -= Refresh;
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

        if (titleText) titleText.text = "Personnel Management";

        if (confirmLabel) confirmLabel.text = "Hire";
        if (cancelLabel) cancelLabel.text = "Close";

        // Rebuild agent list to show current status
        EnsureListLayout();
        RebuildAgentList();
        EnsureListLayout();
    }

    private void BindButtons()
    {
        if (hireButton)
        {
            hireButton.onClick.RemoveAllListeners();
            hireButton.onClick.AddListener(() => UIPanelRoot.I?.OpenRecruitPanel());
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
        Debug.Log($"[UIBind] RosterPanel close={closeState} dimmer={dimmerState}");
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


    private bool ValidateBindings()
    {
        if (!agentListContent || !agentListScrollRect || !itemPrefab)
        {
            Debug.LogError("RosterPanel: agentListContent/agentListScrollRect/itemPrefab not assigned in Inspector.");
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
