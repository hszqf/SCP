// Canvas-maintained file: Assets/Scripts/UI/NodePanelView.cs
// Purpose (current iteration): Read-only refresh for NodePanel Task Status cards (Investigate/Contain)
// Notes:
// - Prefab expected to contain: TaskStatusSection/TaskStatusList/TaskCard_Investigate and TaskCard_Contain
// - Each TaskCard contains children: Title, Status, People (TextMeshProUGUI)
// - Current data model appears to have a single AssignedAgentIds list on Node, so per-task assignment is inferred.
// - We do NOT introduce a separate dedicated pending data structure.
//   UI-wise, when progress is still 0 we label it as "待开始" to reflect: assigned today, advancement happens on next StepDay.

using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodePanelView : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text progressText;

    [Header("Buttons")]
    [SerializeField] private Button investigateButton;
    [SerializeField] private Button containButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backgroundButton; // 蒙版按钮

    // 回调函数
    private Action _onInvestigate;
    private Action _onContain;
    private Action _onClose;

    private string _nodeId;

    // --- Task Status UI (read-only) ---
    private bool _taskUiCached;
    private TMP_Text _invTitle;
    private TMP_Text _invStatus;
    private TMP_Text _invPeople;

    private TMP_Text _conTitle;
    private TMP_Text _conStatus;
    private TMP_Text _conPeople;

    // --- Task Card Action Buttons ---
    private enum TaskActionMode { None, Cancel, Retreat }

    private Button _invActionBtn;
    private TMP_Text _invActionLabel;
    private TaskActionMode _invActionMode = TaskActionMode.None;
    private float _invConfirmUntil = 0f;

    private Button _conActionBtn;
    private TMP_Text _conActionLabel;
    private TaskActionMode _conActionMode = TaskActionMode.None;
    private float _conConfirmUntil = 0f;

    public void Init(Action onInvestigate, Action onContain, Action onClose)
    {
        _onInvestigate = onInvestigate;
        _onContain = onContain;
        _onClose = onClose;

        if (investigateButton) { investigateButton.onClick.RemoveAllListeners(); investigateButton.onClick.AddListener(() => _onInvestigate?.Invoke()); }
        if (containButton) { containButton.onClick.RemoveAllListeners(); containButton.onClick.AddListener(() => _onContain?.Invoke()); }
        if (closeButton) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(() => _onClose?.Invoke()); }

        // 蒙版点击 = 关闭
        if (backgroundButton) { backgroundButton.onClick.RemoveAllListeners(); backgroundButton.onClick.AddListener(() => _onClose?.Invoke()); }

        // Best-effort cache for patched task status section
        CacheTaskStatusUIIfNeeded();
    }

    public void Show(string nodeId)
    {
        _nodeId = nodeId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        CacheTaskStatusUIIfNeeded();
        Refresh();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(_nodeId)) return;
        if (GameController.I == null) return;

        var n = GameController.I.GetNode(_nodeId);
        if (n == null) return;

        if (titleText) titleText.text = n.Name;

        string s = $"{n.Status}";
        if (n.HasAnomaly) s += " <color=red>[ANOMALY]</color>";
        if (statusText) statusText.text = s;

        // 预定占用：一旦节点已有预定/进行中的任务（含待开始），不允许再次打开选人进行重复派遣。
        // 必须通过任务卡片的【取消/撤退】释放后再派遣。
        UpdateDispatchButtons(n);

        if (progressText)
        {
            // Avoid hard dependency on NodeStatus / AssignedAgentIds declarations.
            // Use reflection for robustness and to keep this iteration read-only.
            float invP = TryGetFloat(n, "InvestigateProgress");
            float conP = TryGetFloat(n, "ContainProgress");
            string statusName = TryGetMemberString(n, "Status");

            float p = invP;
            if (statusName.IndexOf("Contain", StringComparison.OrdinalIgnoreCase) >= 0) p = conP;
            else if (statusName.IndexOf("Investigat", StringComparison.OrdinalIgnoreCase) >= 0) p = invP;

            int count = TryGetCollectionCount(n, "AssignedAgentIds");
            progressText.text = $"Progress: {(int)(p * 100)}% | Squad: {count}";
        }

        // Read-only Task Cards
        CacheTaskStatusUIIfNeeded();
        RefreshTaskCardsReadOnly(n);
    }

    // ----------------------
    // Task Status UI helpers
    // ----------------------

    private void CacheTaskStatusUIIfNeeded()
    {
        if (_taskUiCached) return;

        // Defensive: allow for different root nesting; find by name anywhere under this panel.
        Transform invCard = FindDeepChild(transform, "TaskCard_Investigate");
        if (invCard != null)
        {
            _invTitle = GetTmp(invCard, "Title");
            _invStatus = GetTmp(invCard, "Status");
            _invPeople = GetTmp(invCard, "People");

            var btnT = invCard.Find("Btn_Action");
            if (btnT != null)
            {
                _invActionBtn = btnT.GetComponent<Button>();
                _invActionLabel = GetTmp(btnT, "Label");
                if (_invActionBtn)
                {
                    _invActionBtn.onClick.RemoveAllListeners();
                    _invActionBtn.onClick.AddListener(OnInvActionClicked);
                }
            }
        }

        Transform conCard = FindDeepChild(transform, "TaskCard_Contain");
        if (conCard != null)
        {
            _conTitle = GetTmp(conCard, "Title");
            _conStatus = GetTmp(conCard, "Status");
            _conPeople = GetTmp(conCard, "People");

            var btnT = conCard.Find("Btn_Action");
            if (btnT != null)
            {
                _conActionBtn = btnT.GetComponent<Button>();
                _conActionLabel = GetTmp(btnT, "Label");
                if (_conActionBtn)
                {
                    _conActionBtn.onClick.RemoveAllListeners();
                    _conActionBtn.onClick.AddListener(OnConActionClicked);
                }
            }
        }

        // Consider cached if we found at least one card; this avoids repeating deep searches each Refresh.
        _taskUiCached = (invCard != null || conCard != null);
    }

    private void RefreshTaskCardsReadOnly(object node)
    {
        // If task cards are not present (prefab not patched / different variant), do nothing.
        if (_invTitle == null && _invStatus == null && _invPeople == null && _conTitle == null && _conStatus == null && _conPeople == null)
            return;

        // Fixed titles (avoid localization work now)
        if (_invTitle) _invTitle.text = "调查";
        if (_conTitle) _conTitle.text = "收容";

        // NOTE: We intentionally avoid `dynamic` here.
        // Unity projects may not reference Microsoft.CSharp.RuntimeBinder, which breaks compilation when using dynamic.
        // Instead, we use lightweight reflection to read AssignedAgentIds / Status.

        int squad = TryGetCollectionCount(node, "AssignedAgentIds");
        string statusName = TryGetMemberString(node, "Status");

        bool isInvestigating = statusName.IndexOf("Investigat", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isContaining = statusName.IndexOf("Contain", StringComparison.OrdinalIgnoreCase) >= 0;

        // Current model limitation: a single squad list. We infer which card “owns” the squad by current status.
        // - If Investigating: squad belongs to Investigate card, Contain card shows 0.
        // - If Containing: squad belongs to Contain card, Investigate card shows 0.
        // - Else (unexpected but possible): both cards show the squad count, and we mark as “已指派（未归属）”。

        // Per-card progress (for distinguishing "未推进" vs running)
        float invP = TryGetFloat(node, "InvestigateProgress");
        float conP = TryGetFloat(node, "ContainProgress");
        const float EPS = 0.0001f;

        // Investigate card
        if (_invStatus)
        {
            if (isInvestigating)
            {
                // Assigned today; progress will start advancing on next StepDay.
                if (squad > 0 && invP <= EPS) _invStatus.text = "状态：待开始";
                else _invStatus.text = $"状态：进行中（{(int)(invP * 100)}%）";
            }
            else if (!isContaining && squad > 0)
            {
                _invStatus.text = "状态：已指派（未归属）";
            }
            else
            {
                _invStatus.text = "状态：未指派";
            }
        }

        if (_invPeople)
        {
            int c = isInvestigating ? squad : (isContaining ? 0 : squad);
            _invPeople.text = $"人员：{c}人";
        }

        // Contain card
        if (_conStatus)
        {
            if (isContaining)
            {
                if (squad > 0 && conP <= EPS) _conStatus.text = "状态：待开始";
                else _conStatus.text = $"状态：进行中（{(int)(conP * 100)}%）";
            }
            else if (!isInvestigating && squad > 0)
            {
                _conStatus.text = "状态：已指派（未归属）";
            }
            else
            {
                _conStatus.text = "状态：未指派";
            }
        }

        if (_conPeople)
        {
            int c = isContaining ? squad : (isInvestigating ? 0 : squad);
            _conPeople.text = $"人员：{c}人";
        }

        // Action buttons (Cancel vs Retreat)
        UpdateActionButtons(isInvestigating, isContaining, squad, invP, conP, EPS);
    }

    private static float TryGetFloat(object obj, string memberName)
    {
        try
        {
            var v = TryGetMemberValue(obj, memberName);
            if (v == null) return 0f;

            if (v is float f) return f;
            if (v is double d) return (float)d;
            if (v is int i) return i;

            if (float.TryParse(v.ToString(), out var parsed)) return parsed;
            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private static int TryGetCollectionCount(object obj, string memberName)
    {
        try
        {
            var v = TryGetMemberValue(obj, memberName);
            if (v == null) return 0;

            // Most Unity-friendly collections implement ICollection
            if (v is System.Collections.ICollection c) return c.Count;

            // Fallback: try property "Count" if present
            var t = v.GetType();
            var p = t.GetProperty("Count");
            if (p != null && p.PropertyType == typeof(int))
                return (int)p.GetValue(v);

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string TryGetMemberString(object obj, string memberName)
    {
        try
        {
            var v = TryGetMemberValue(obj, memberName);
            return v != null ? v.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static object TryGetMemberValue(object obj, string memberName)
    {
        if (obj == null || string.IsNullOrEmpty(memberName)) return null;

        var t = obj.GetType();
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        var p = t.GetProperty(memberName, Flags);
        if (p != null) return p.GetValue(obj);

        var f = t.GetField(memberName, Flags);
        if (f != null) return f.GetValue(obj);

        return null;
    }

    private void UpdateActionButtons(bool isInvestigating, bool isContaining, int squad, float invP, float conP, float eps)
    {
        // Investigate action
        if (_invActionBtn != null)
        {
            if (isInvestigating && squad > 0)
            {
                _invActionMode = (invP > eps) ? TaskActionMode.Retreat : TaskActionMode.Cancel;
                _invActionBtn.gameObject.SetActive(true);
                if (_invActionLabel)
                {
                    _invActionLabel.text = (_invActionMode == TaskActionMode.Cancel)
                        ? "取消"
                        : (Time.unscaledTime <= _invConfirmUntil ? "确认撤退" : "撤退");
                }
            }
            else
            {
                _invActionMode = TaskActionMode.None;
                _invConfirmUntil = 0f;
                _invActionBtn.gameObject.SetActive(false);
            }
        }

        // Contain action
        if (_conActionBtn != null)
        {
            if (isContaining && squad > 0)
            {
                _conActionMode = (conP > eps) ? TaskActionMode.Retreat : TaskActionMode.Cancel;
                _conActionBtn.gameObject.SetActive(true);
                if (_conActionLabel)
                {
                    _conActionLabel.text = (_conActionMode == TaskActionMode.Cancel)
                        ? "取消"
                        : (Time.unscaledTime <= _conConfirmUntil ? "确认撤退" : "撤退");
                }
            }
            else
            {
                _conActionMode = TaskActionMode.None;
                _conConfirmUntil = 0f;
                _conActionBtn.gameObject.SetActive(false);
            }
        }
    }

    private void OnInvActionClicked()
    {
        HandleActionClick(ref _invActionMode, ref _invConfirmUntil);
    }

    private void OnConActionClicked()
    {
        HandleActionClick(ref _conActionMode, ref _conConfirmUntil);
    }

    private void HandleActionClick(ref TaskActionMode mode, ref float confirmUntil)
    {
        if (string.IsNullOrEmpty(_nodeId)) return;
        if (GameController.I == null) return;

        if (mode == TaskActionMode.Cancel)
        {
            // cancel = progress == 0
            GameController.I.ForceWithdraw(_nodeId);
            confirmUntil = 0f;
            Refresh();
            return;
        }

        if (mode == TaskActionMode.Retreat)
        {
            // retreat = progress > 0, requires confirm (second click within window)
            const float ConfirmWindowSec = 3f;
            if (Time.unscaledTime <= confirmUntil)
            {
                GameController.I.ForceWithdraw(_nodeId);
                confirmUntil = 0f;
                Refresh();
            }
            else
            {
                confirmUntil = Time.unscaledTime + ConfirmWindowSec;
                Refresh();
            }
        }
    }

    private void UpdateDispatchButtons(NodeState n)
    {
        if (n == null) return;

        bool hasReservedTask =
            (n.Status == NodeStatus.Investigating || n.Status == NodeStatus.Containing) &&
            n.AssignedAgentIds != null &&
            n.AssignedAgentIds.Count > 0;

        // When reserved or running, lock dispatch entry points; cancellation/withdrawal is done on task cards.
        if (investigateButton) investigateButton.interactable = !hasReservedTask;
        if (containButton) containButton.interactable = !hasReservedTask;
    }

    private static TMP_Text GetTmp(Transform parent, string childName)
    {
        var t = parent.Find(childName);
        if (t == null) return null;
        return t.GetComponent<TMP_Text>();
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            var r = FindDeepChild(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
