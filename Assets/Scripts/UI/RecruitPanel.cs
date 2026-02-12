using System;
using System.Collections.Generic;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecruitPanel : MonoBehaviour, IModalClosable
{
    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text costText;

    [Header("Buttons")]
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text refreshLabel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text cancelLabel;
    [SerializeField] private Button dimmerButton;

    [Header("Agent List")]
    [SerializeField] private RectTransform agentListContent;
    [SerializeField] private ScrollRect agentListScrollRect;
    [SerializeField] private AgentPickerItemView itemPrefab;
    [SerializeField] private RectTransform agentGridContent;

    private readonly List<GameObject> _agentItems = new();
    private const int RecruitPoolSize = 3;
    private const int FreeRefreshPerDay = 1;
    private const int RefreshBaseCost = 500;

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
        EnsureRecruitPool(false);
        UpdateHeaderTexts();

        // Rebuild agent list to show current status
        EnsureListLayout();
        RebuildCandidateList();
        EnsureListLayout();
    }

    private void BindButtons()
    {
        if (refreshButton)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(OnRefreshClicked);
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

    private void RebuildCandidateList()
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
        var pool = gc?.State?.RecruitPool;
        if (gc == null || pool?.candidates == null) return;

        // Create an item for each candidate (max 3)
        int count = 0;
        foreach (var candidate in pool.candidates)
        {
            if (candidate?.agent == null) continue;
            if (count >= RecruitPoolSize) break;

            bool isHired = candidate.isHired;
            string statusLine = isHired
                ? "<color=#AAAAAA>已雇佣</color>"
                : "<color=#66FF66>HIRE</color>";

            // Create candidate item UI from prefab
            var item = Instantiate(itemPrefab, contentRoot, false);
            item.name = $"CandidateItem_{candidate.cid}";
            string displayName = BuildCandidateDisplayName(candidate);
            string attrLine = BuildCandidateAttrLine(candidate);
            item.Bind(candidate.agent, displayName, attrLine, isHired, false, _ => OnHireCandidate(candidate), statusLine);
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
            count++;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        Canvas.ForceUpdateCanvases();
    }

    private static string BuildCandidateAttrLine(RecruitCandidate c)
    {
        var a = c?.agent;
        if (a == null) return "";
        int level = Mathf.Max(1, a.Level);
        int hpMax = Mathf.Max(1, a.MaxHP);
        int sanMax = Mathf.Max(1, a.MaxSAN);
        int expNeed = Sim.ExpToNext(level);
        string vitals = $"HP {a.HP}/{hpMax}  SAN {a.SAN}/{sanMax}  EXP {a.Exp}/{expNeed}";
        string attrSummary = $"P{a.Perception} O{a.Operation} R{a.Resistance} Pow{a.Power} | ${c.cost}";
        return $"{vitals}  {attrSummary}";
    }

    private static string BuildCandidateDisplayName(RecruitCandidate c)
    {
        var a = c?.agent;
        if (a == null) return string.Empty;
        return "Candidate";
    }

    private void OnHireCandidate(RecruitCandidate candidate)
    {
        if (GameController.I == null) return;

        if (candidate == null) return;
        if (candidate.isHired) return;

        if (!GameController.I.TryHireAgent(candidate, out var agent))
        {
            Refresh();
            return;
        }
        candidate.isHired = true;
        candidate.hiredAgentId = agent?.Id;
        candidate.hiredName = agent?.Name;
        UIEvents.RaiseAgentsChanged();
        UpdateHeaderTexts();
        UpdateCandidateItem(candidate);
    }

    private void UpdateHeaderTexts()
    {
        if (!ValidateBindings() || GameController.I == null) return;

        int money = GameController.I.State?.Money ?? 0;
        int candidateCount = GameController.I.State?.RecruitPool?.candidates?.Count ?? 0;
        var pool = GameController.I.State?.RecruitPool;
        int refreshUsedToday = pool?.refreshUsedToday ?? 0;
        int remainingFree = Mathf.Max(0, FreeRefreshPerDay - refreshUsedToday);
        int paidIndex = Mathf.Max(0, refreshUsedToday - FreeRefreshPerDay);
        int nextPaidCost = RefreshBaseCost * (paidIndex + 1);

        if (titleText) titleText.text = "Personnel Management";
        if (moneyText) moneyText.text = $"Money: {money}";
        if (costText) costText.text = $"候选池：{candidateCount}/{RecruitPoolSize}";
        if (refreshLabel)
        {
            refreshLabel.text = remainingFree > 0
                ? $"免费刷新（{remainingFree}/{FreeRefreshPerDay}）"
                : $"刷新 ¥{nextPaidCost}";
        }
        if (cancelLabel) cancelLabel.text = "Close";
    }

    private void UpdateCandidateItem(RecruitCandidate candidate)
    {
        if (candidate == null) return;
        var contentRoot = GetContentRoot();
        if (!contentRoot) return;

        var itemTransform = contentRoot.Find($"CandidateItem_{candidate.cid}");
        if (!itemTransform) return;

        var item = itemTransform.GetComponent<AgentPickerItemView>();
        if (!item) return;

        bool isHired = candidate.isHired;
        string statusLine = isHired
            ? "<color=#AAAAAA>已雇佣</color>"
            : "<color=#66FF66>HIRE</color>";
        string displayName = BuildCandidateDisplayName(candidate);
        string attrLine = BuildCandidateAttrLine(candidate);

        item.Bind(candidate.agent, displayName, attrLine, isHired, false, _ => OnHireCandidate(candidate), statusLine);
    }

    private void OnRefreshClicked()
    {
        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        EnsureRecruitPool(false);
        var pool = gc.State.RecruitPool;
        if (pool == null) return;

        int refreshUsedToday = pool.refreshUsedToday;
        int remainingFree = Mathf.Max(0, FreeRefreshPerDay - refreshUsedToday);
        int paidIndex = Mathf.Max(0, refreshUsedToday - FreeRefreshPerDay);
        int nextPaidCost = RefreshBaseCost * (paidIndex + 1);
        int cost = remainingFree > 0 ? 0 : nextPaidCost;

        if (cost > 0 && gc.State.Money < cost)
        {
            UIPanelRoot.I?.ShowInfo("资金不足", "资金不足，无法刷新候选池。");
            return;
        }

        if (cost > 0)
        {
            gc.State.Money -= cost;
        }

        pool.refreshUsedToday += 1;
        pool.candidates = BuildRecruitCandidates(gc, RecruitPoolSize);
        Refresh();
    }

    private void EnsureRecruitPool(bool forceRefresh)
    {
        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        if (gc.State.RecruitPool == null)
        {
            gc.State.RecruitPool = new RecruitPoolState();
        }

        var pool = gc.State.RecruitPool;
        bool dayChanged = pool.day != gc.State.Day;
        bool empty = pool.candidates == null || pool.candidates.Count == 0;

        if (dayChanged)
        {
            pool.day = gc.State.Day;
            pool.refreshUsedToday = 0;
        }

        if (forceRefresh || dayChanged || empty)
        {
            pool.candidates = BuildRecruitCandidates(gc, RecruitPoolSize);
        }
    }

    private static List<RecruitCandidate> BuildRecruitCandidates(GameController gc, int count)
    {
        var list = new List<RecruitCandidate>();
        if (gc == null) return list;

        for (int i = 0; i < count; i++)
        {
            var c = gc.GenerateRecruitCandidate();
            if (c == null) continue;
            if (string.IsNullOrEmpty(c.cid))
            {
                c.cid = $"RC_{gc.State.Day}_{i}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            }
            c.isHired = false;
            c.hiredAgentId = null;
            c.hiredName = null;
            list.Add(c);
        }

        return list;
    }

    private bool ValidateBindings()
    {
        if (!itemPrefab)
        {
            Debug.LogError("RecruitPanel: itemPrefab not assigned in Inspector.");
            return false;
        }

        if (!agentListContent && !agentGridContent)
        {
            Debug.LogError("RecruitPanel: agentListContent or agentGridContent not assigned in Inspector.");
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
