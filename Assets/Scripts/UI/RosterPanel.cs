using Core;
using Settlement;
using System;
using System.Collections.Generic;
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
    [SerializeField] private RectTransform agentGridContent;

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
        var contentRoot = GetContentRoot();
        foreach (Transform child in contentRoot)
        {
            if (child) Destroy(child.gameObject);
        }

        if (contentRoot == null || itemPrefab == null)
        {
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
            var item = Instantiate(itemPrefab, contentRoot, false);
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

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        Canvas.ForceUpdateCanvases();
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

    private static string BuildAgentDisplayName(AgentState a)
    {
        if (a == null) return string.Empty;
        return string.IsNullOrEmpty(a.Name) ? a.Id : a.Name;
    }


    private bool ValidateBindings()
    {
        if (!itemPrefab)
        {
            Debug.LogError("RosterPanel: itemPrefab not assigned in Inspector.");
            return false;
        }

        if (!agentListContent && !agentGridContent)
        {
            Debug.LogError("RosterPanel: agentListContent or agentGridContent not assigned in Inspector.");
            return false;
        }

        return true;
    }

    private void EnsureListLayout()
    {
        var contentRoot = GetContentRoot();
        var scrollRect = GetScrollRect();
        if (!contentRoot || !scrollRect) return;

        if (contentRoot == agentGridContent)
        {
            if (scrollRect.content == null)
                scrollRect.content = contentRoot;
            return;
        }

        var vlg = contentRoot.GetComponent<VerticalLayoutGroup>() ?? contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        var csf = contentRoot.GetComponent<ContentSizeFitter>() ?? contentRoot.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (scrollRect.content == null)
        {
            scrollRect.content = contentRoot;
        }
    }

    private RectTransform GetContentRoot()
        => agentGridContent ? agentGridContent : agentListContent;

    private ScrollRect GetScrollRect()
        => agentListScrollRect;

}
