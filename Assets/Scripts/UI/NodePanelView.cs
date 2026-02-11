// Canvas-maintained file: Assets/Scripts/UI/NodePanelView.cs
// N-task model compatible (Core.NodeState.Tasks)
//
// UI behavior:
// 1) If a scrollable task list exists in the prefab (Content + Row template), render ALL tasks as rows.
// 2) Otherwise, fall back to the legacy 2 summary cards (调查 / 收容) by selecting one representative task per type.
//
// Required prefab names for full task list (recommended):
// - TaskListScroll (ScrollRect)
//   - Viewport
//     - Content
//       - TaskRowTemplate (inactive)
//          - Title (TMP_Text)
//          - Status (TMP_Text)
//          - People (TMP_Text)
//          - Btn_Action (Button)
//             - Label (TMP_Text)
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodePanelView : MonoBehaviour, IModalClosable
{
    [Header("UI Components")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text eventCountText;

    [Header("Buttons")]
    [SerializeField] private Button investigateButton;
    [SerializeField] private Button containButton;
    [SerializeField] private Button manageButton; // 收容后管理（打开管理面板）
    [SerializeField] private Button processEventButton; // 处理事件
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimmerButton;
    [SerializeField] private Button backgroundButton; // 蒙版按钮

    // 回调函数
    private Action _onInvestigate;
    private Action _onContain;
    private Action _onClose;

    // Cached root (for opening manage panel without changing Init signature)
    private UIPanelRoot _uiRoot;

    private bool _dimmerBound;

    private string _nodeId;

    // --- Task Status UI (read-only summary cards) ---
    private bool _taskUiCached;
    private Transform _invCardRoot;
    private Transform _conCardRoot;
    private TMP_Text _invTitle;
    private TMP_Text _invStatus;
    private TMP_Text _invPeople;

    private TMP_Text _conTitle;
    private TMP_Text _conStatus;
    private TMP_Text _conPeople;

    // --- Task List UI (scrollable) ---
    private bool _taskListCached;
    private Transform _taskListContent;
    private GameObject _taskRowTemplate;
    private readonly List<GameObject> _taskRowInstances = new();
    private readonly Dictionary<string, float> _rowConfirmUntil = new();

    // --- Task Card / Row Action Buttons ---
    private enum TaskActionMode { None, Cancel, Retreat }

    private Button _invActionBtn;
    private TMP_Text _invActionLabel;
    private TaskActionMode _invActionMode = TaskActionMode.None;
    private float _invConfirmUntil = 0f;

    private Button _conActionBtn;
    private TMP_Text _conActionLabel;
    private TaskActionMode _conActionMode = TaskActionMode.None;
    private float _conConfirmUntil = 0f;

    // Representative task ids for current refresh
    private string _invTaskId;
    private string _conTaskId;


    // Track if contain button is disabled and why
    private bool _containDisabledForNoAnomalies = false;
    // Track current containables state for guard checks

    private const float EPS = 0.0001f;

    private void Awake()
    {
        BindDimmerButton();
    }

    private void BindDimmerButton()
    {
        if (_dimmerBound) return;
        _dimmerBound = true;

        if (dimmerButton != null)
        {
            dimmerButton.onClick.RemoveAllListeners();
            dimmerButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "dimmer"));

            var img = dimmerButton.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var cg = dimmerButton.GetComponent<CanvasGroup>();
            if (cg != null) { cg.blocksRaycasts = true; cg.interactable = true; }

            Debug.Log("[UIBind] NodePanel dimmer=ok");
        }
        else
        {
            Debug.LogWarning("[UIBind] NodePanel dimmer=missing");
        }

        // Legacy fallback (if backgroundButton is still used in older prefabs)
        if (backgroundButton != null && backgroundButton != dimmerButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "dimmer"));
        }
    }

    public void Init(Action onInvestigate, Action onContain, Action onClose)
    {
        // Cache UIPanelRoot once
        if (_uiRoot == null) _uiRoot = FindFirstObjectByType<UIPanelRoot>();
        _onInvestigate = onInvestigate;
        _onContain = onContain;
        _onClose = onClose;

        if (investigateButton)
        {
            investigateButton.onClick.RemoveAllListeners();
            investigateButton.onClick.AddListener(() => _onInvestigate?.Invoke());
        }

        if (containButton)
        {
            containButton.onClick.RemoveAllListeners();
            containButton.onClick.AddListener(() => _onContain?.Invoke());
        }

        if (manageButton)
        {
            manageButton.onClick.RemoveAllListeners();
            manageButton.onClick.AddListener(() =>
            {
                // Manage panel is global (not per-node). If missing, do nothing.
                _uiRoot?.OpenManage(_nodeId);
            });
        }

        if (!processEventButton)
        {
            var btnT = FindDeepChild(transform, "Btn_ProcessEvent");
            if (btnT != null) processEventButton = btnT.GetComponent<Button>();
        }

        if (processEventButton)
        {
            processEventButton.onClick.RemoveAllListeners();
            processEventButton.onClick.AddListener(() => _uiRoot?.OpenNodeEvent(_nodeId));
        }

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close btn"));
        }

        CacheTaskStatusUIIfNeeded();
        CacheTaskListUIIfNeeded();
    }

    public void Show(string nodeId)
    {
        _nodeId = nodeId;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        CacheTaskStatusUIIfNeeded();
        CacheTaskListUIIfNeeded();
        Refresh();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void CloseFromRoot()
    {
        if (_onClose != null) _onClose.Invoke();
        else Hide();
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(_nodeId)) return;
        if (GameController.I == null) return;

        var n = GameController.I.GetNode(_nodeId);
        if (n == null) return;

        if (!eventCountText)
        {
            var evtT = FindDeepChild(transform, "EventCountText");
            if (evtT != null) eventCountText = evtT.GetComponent<TMP_Text>();
        }

        if (!processEventButton)
        {
            var btnT = FindDeepChild(transform, "Btn_ProcessEvent");
            if (btnT != null) processEventButton = btnT.GetComponent<Button>();
        }

        if (processEventButton && processEventButton.onClick.GetPersistentEventCount() == 0)
        {
            processEventButton.onClick.RemoveAllListeners();
            processEventButton.onClick.AddListener(() => _uiRoot?.OpenNodeEvent(_nodeId));
        }

        if (titleText) titleText.text = $"{n.Name} ({n.Population}人口)";

        // 统一口径：可收容 = 已发现 - 已收容
        int containableCount = GetContainableCount(n);
        bool hasContainables = containableCount > 0;

        UpdateDispatchButtons(n, containableCount, hasContainables);

        UpdateManageButton(n);

        int pendingEvents = n.HasPendingEvent ? n.PendingEvents.Count : 0;

        if (eventCountText)
        {
            eventCountText.text = pendingEvents > 0 ? $"待处理事件：{pendingEvents}" : "待处理事件：0";
        }

        if (processEventButton)
        {
            processEventButton.gameObject.SetActive(pendingEvents > 0);
            processEventButton.interactable = pendingEvents > 0;
        }

        if (progressText)
        {
            progressText.text = BuildAnomalyStatusText(n);
        }

        CacheTaskListUIIfNeeded();
        if (RefreshTaskListIfPresent(n))
        {
            // Optional: if list exists, you may want to hide legacy summary cards to reduce clutter.
            // Keep them visible by default to avoid layout surprises.
            return;
        }

        CacheTaskStatusUIIfNeeded();
        RefreshTaskCardsSummary(n);
    }

    // ----------------------
    // Task Status UI helpers (summary cards)
    // ----------------------

    private void CacheTaskStatusUIIfNeeded()
    {
        if (_taskUiCached) return;

        _invCardRoot = FindDeepChild(transform, "TaskCard_Investigate");
        if (_invCardRoot != null)
        {
            _invTitle = GetTmp(_invCardRoot, "Title");
            _invStatus = GetTmp(_invCardRoot, "Status");
            _invPeople = GetTmp(_invCardRoot, "People");

            var btnT = _invCardRoot.Find("Btn_Action");
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

        _conCardRoot = FindDeepChild(transform, "TaskCard_Contain");
        if (_conCardRoot != null)
        {
            _conTitle = GetTmp(_conCardRoot, "Title");
            _conStatus = GetTmp(_conCardRoot, "Status");
            _conPeople = GetTmp(_conCardRoot, "People");

            var btnT = _conCardRoot.Find("Btn_Action");
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

        _taskUiCached = (_invCardRoot != null || _conCardRoot != null);
    }

    private void RefreshTaskCardsSummary(NodeState node)
    {
        // If task cards are not present (prefab not patched / different variant), do nothing.
        if (_invTitle == null && _invStatus == null && _invPeople == null && _conTitle == null && _conStatus == null && _conPeople == null)
            return;

        if (_invTitle) _invTitle.text = "调查";
        if (_conTitle) _conTitle.text = "收容";

        var tasks = node.Tasks ?? new List<NodeTask>();
        var inv = tasks.Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Investigate).ToList();
        var con = tasks.Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain).ToList();

        // Representative task selection:
        // 1) Prefer tasks with squad.
        // 2) Among squad tasks, prefer "not started" (progress==0) so user can see/Cancel the newest assignment.
        // 3) Then CreatedDay desc -> Progress desc.
        NodeTask invMain = inv
            .OrderByDescending(t => t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0)
            .ThenBy(t => (t.Progress > EPS) ? 1 : 0)
            .ThenByDescending(t => t.CreatedDay)
            .ThenByDescending(t => t.Progress)
            .FirstOrDefault();

        NodeTask conMain = con
            .OrderByDescending(t => t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0)
            .ThenBy(t => (t.Progress > EPS) ? 1 : 0)
            .ThenByDescending(t => t.CreatedDay)
            .ThenByDescending(t => t.Progress)
            .FirstOrDefault();

        _invTaskId = invMain?.Id;
        _conTaskId = conMain?.Id;

        int containablesCount = GetContainableCount(node);

        // Investigate card
        if (_invStatus)
        {
            if (invMain == null)
            {
                _invStatus.text = "状态：可调查";
            }
            else
            {
                bool hasSquad = invMain.AssignedAgentIds != null && invMain.AssignedAgentIds.Count > 0;
                if (hasSquad && invMain.Progress <= EPS) _invStatus.text = "状态：待开始";
                else if (hasSquad) _invStatus.text = $"状态：进行中（{(int)(GetTaskProgress01(invMain) * 100)}%）";
                else _invStatus.text = "状态：未指派";

                if (inv.Count > 1) _invStatus.text += $"（+{inv.Count - 1}）";
            }
        }

        if (_invPeople)
        {
            int c = (invMain != null && invMain.AssignedAgentIds != null) ? invMain.AssignedAgentIds.Count : 0;
            _invPeople.text = $"人员：{c}人";
        }

        // Contain card
        if (_conStatus)
        {
            if (conMain != null)
            {
                bool hasSquad = conMain.AssignedAgentIds != null && conMain.AssignedAgentIds.Count > 0;
                if (hasSquad && conMain.Progress <= EPS) _conStatus.text = "状态：待开始";
                else if (hasSquad) _conStatus.text = $"状态：进行中（{(int)(GetTaskProgress01(conMain) * 100)}%）";
                else _conStatus.text = "状态：未指派";

                if (con.Count > 1) _conStatus.text += $"（+{con.Count - 1}）";
            }
            else if (containablesCount > 0)
            {
                _conStatus.text = containablesCount == 1 ? "状态：可收容" : $"状态：可收容（{containablesCount}）";
            }
            else
            {
                _conStatus.text = "状态：未指派";
            }
        }

        if (_conPeople)
        {
            if (conMain != null)
            {
                int c = (conMain.AssignedAgentIds != null) ? conMain.AssignedAgentIds.Count : 0;

                string targetName = ResolveContainableName(node, conMain.SourceAnomalyId);
                _conPeople.text = string.IsNullOrEmpty(targetName)
                    ? $"人员：{c}人"
                    : $"目标：{targetName}\n人员：{c}人";
            }
            else if (containablesCount > 0)
            {
                string targetName = ResolveContainableName(node, null);
                _conPeople.text = string.IsNullOrEmpty(targetName)
                    ? "目标：可收容\n人员：0人"
                    : $"目标：{targetName}\n人员：0人";
            }
            else
            {
                _conPeople.text = "人员：0人";
            }
        }

        UpdateActionButtons(invMain, conMain);
    }

    private void UpdateActionButtons(NodeTask invTask, NodeTask conTask)
    {
        // Investigate action
        if (_invActionBtn != null)
        {
            bool show = invTask != null && invTask.State == TaskState.Active && invTask.AssignedAgentIds != null && invTask.AssignedAgentIds.Count > 0;
            if (show)
            {
                _invActionMode = (invTask.Progress > EPS) ? TaskActionMode.Retreat : TaskActionMode.Cancel;
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
            bool show = conTask != null && conTask.State == TaskState.Active && conTask.AssignedAgentIds != null && conTask.AssignedAgentIds.Count > 0;
            if (show)
            {
                _conActionMode = (conTask.Progress > EPS) ? TaskActionMode.Retreat : TaskActionMode.Cancel;
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
        HandleActionClick(_invTaskId, ref _invActionMode, ref _invConfirmUntil);
    }

    private void OnConActionClicked()
    {
        HandleActionClick(_conTaskId, ref _conActionMode, ref _conConfirmUntil);
    }

    private void HandleActionClick(string taskId, ref TaskActionMode mode, ref float confirmUntil)
    {
        if (string.IsNullOrEmpty(taskId)) return;
        if (GameController.I == null) return;

        if (mode == TaskActionMode.Cancel)
        {
            GameController.I.CancelOrRetreatTask(taskId);
            confirmUntil = 0f;
            Refresh();
            return;
        }

        if (mode == TaskActionMode.Retreat)
        {
            const float ConfirmWindowSec = 3f;
            if (Time.unscaledTime <= confirmUntil)
            {
                GameController.I.CancelOrRetreatTask(taskId);
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

    // ----------------------
    // Task List UI (scrollable)
    // ----------------------

    private void CacheTaskListUIIfNeeded()
    {
        if (_taskListCached) return;

        // Try a few common roots.
        Transform scroll = FindDeepChild(transform, "TaskListScroll")
                         ?? FindDeepChild(transform, "Scroll_Tasks")
                         ?? FindDeepChild(transform, "TaskList");

        if (scroll != null)
        {
            var viewport = scroll.Find("Viewport") ?? FindDeepChild(scroll, "Viewport");
            var content = viewport != null ? (viewport.Find("Content") ?? FindDeepChild(viewport, "Content")) : (scroll.Find("Content") ?? FindDeepChild(scroll, "Content"));

            if (content != null)
            {
                _taskListContent = content;

                // Template search:
                var tplT = content.Find("TaskRowTemplate");
                if (tplT == null)
                {
                    // Find first inactive child as template.
                    for (int i = 0; i < content.childCount; i++)
                    {
                        var c = content.GetChild(i);
                        if (c != null && !c.gameObject.activeSelf)
                        {
                            tplT = c;
                            break;
                        }
                    }
                }

                if (tplT != null)
                {
                    _taskRowTemplate = tplT.gameObject;
                    _taskRowTemplate.SetActive(false);
                }
            }
        }

        _taskListCached = true;
    }

    private bool RefreshTaskListIfPresent(NodeState node)
    {
        if (_taskListContent == null || _taskRowTemplate == null) return false;

        // Clear existing instances
        for (int i = 0; i < _taskRowInstances.Count; i++)
        {
            if (_taskRowInstances[i] != null)
                Destroy(_taskRowInstances[i]);
        }
        _taskRowInstances.Clear();

        if (node.Tasks == null || node.Tasks.Count == 0)
        {
            // Keep empty; template stays hidden.
            return true;
        }

        var tasks = node.Tasks
            .Where(t => t != null && t.State == TaskState.Active)
            .OrderBy(t => TaskTypeOrder(t.Type))
            .ThenByDescending(t => t.CreatedDay)
            .ThenByDescending(t => t.Progress)
            .ToList();

        foreach (var t in tasks)
        {
            var go = Instantiate(_taskRowTemplate, _taskListContent);
            go.name = $"TaskRow_{t.Type}_{t.Id}";
            go.SetActive(true);
            _taskRowInstances.Add(go);

            var row = go.transform;
            var title = GetTmp(row, "Title");
            var status = GetTmp(row, "Status");
            var people = GetTmp(row, "People");

            string taskDefLabel = ResolveTaskDefLabel(t);
            string titleTextLocal = !string.IsNullOrEmpty(taskDefLabel)
                ? taskDefLabel
                : t.Type switch
                {
                    TaskType.Investigate => "调查",
                    TaskType.Contain => "收容",
                    TaskType.Manage => "管理",
                    _ => t.Type.ToString()
                };
            if (t.Type == TaskType.Contain)
            {
                string tn = ResolveContainableName(node, t.TargetContainableId);
                if (!string.IsNullOrEmpty(tn)) titleTextLocal += $"：{tn}";
            }
            else if (t.Type == TaskType.Manage)
            {
                string an = ResolveManagedAnomalyName(node, t.TargetManagedAnomalyId);
                if (!string.IsNullOrEmpty(an)) titleTextLocal += $"：{an}";
            }
            if (title) title.text = titleTextLocal;

            bool hasSquad = t.AssignedAgentIds != null && t.AssignedAgentIds.Count > 0;
            if (status)
            {
                status.text = BuildTaskStatusText(t, hasSquad);
            }

            if (people)
            {
                int c = hasSquad ? t.AssignedAgentIds.Count : 0;
                people.text = $"人员：{c}人";
            }

            // Action button
            var btnT = row.Find("Btn_Action");
            var btn = btnT ? btnT.GetComponent<Button>() : null;
            var lbl = btnT ? GetTmp(btnT, "Label") : null;

            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();

                var mode = GetActionMode(t, hasSquad);
                if (mode == TaskActionMode.None)
                {
                    btn.gameObject.SetActive(false);
                }
                else
                {
                    btn.gameObject.SetActive(true);
                    if (lbl) lbl.text = BuildActionLabel(mode, t.Id);
                    string taskId = t.Id;

                    btn.onClick.AddListener(() => OnRowActionClicked(taskId));
                }
            }
        }

        // Layout rebuild (best-effort)
        var rt = _taskListContent as RectTransform;
        if (rt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        return true;
    }

    private void OnRowActionClicked(string taskId)
    {
        if (string.IsNullOrEmpty(taskId) || GameController.I == null) return;

        if (!GameController.I.TryGetTask(taskId, out var node, out var task) || task == null) return;
        if (task.State != TaskState.Active) return;

        bool hasSquad = task.AssignedAgentIds != null && task.AssignedAgentIds.Count > 0;
        var mode = GetActionMode(task, hasSquad);

        if (mode == TaskActionMode.Cancel)
        {
            GameController.I.CancelOrRetreatTask(taskId);
            _rowConfirmUntil.Remove(taskId);
            Refresh();
            return;
        }

        if (mode == TaskActionMode.Retreat)
        {
            const float ConfirmWindowSec = 3f;
            float until = 0f;
            _rowConfirmUntil.TryGetValue(taskId, out until);

            if (Time.unscaledTime <= until)
            {
                GameController.I.CancelOrRetreatTask(taskId);
                _rowConfirmUntil.Remove(taskId);
                Refresh();
            }
            else
            {
                _rowConfirmUntil[taskId] = Time.unscaledTime + ConfirmWindowSec;
                Refresh();
            }
        }
    }

    private static int TaskStateOrder(TaskState s)
    {
        // Active first, then Completed, then Cancelled
        return s switch
        {
            TaskState.Active => 0,
            TaskState.Completed => 1,
            TaskState.Cancelled => 2,
            _ => 9
        };
    }

    private static int TaskTypeOrder(TaskType t)
    {
        // Investigate first, then Contain, then Manage
        return t switch
        {
            TaskType.Investigate => 0,
            TaskType.Contain => 1,
            TaskType.Manage => 2,
            _ => 9
        };
    }

    private static int GetContainableCount(NodeState node)
    {
        if (node?.KnownAnomalyDefIds == null || node.KnownAnomalyDefIds.Count == 0) return 0;

        HashSet<string> contained = null;
        if (node.ManagedAnomalies != null && node.ManagedAnomalies.Count > 0)
        {
            contained = new HashSet<string>(node.ManagedAnomalies
                .Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                .Select(m => m.AnomalyId));
        }

        int count = 0;
        foreach (var defId in node.KnownAnomalyDefIds)
        {
            if (string.IsNullOrEmpty(defId)) continue;
            if (contained != null && contained.Contains(defId)) continue;
            count += 1;
        }
        return count;
    }

    private static string ResolveContainableName(NodeState node, string containableId)
    {
        if (node == null || node.KnownAnomalyDefIds == null || node.KnownAnomalyDefIds.Count == 0) return "";
        var registry = DataRegistry.Instance;
        string defId = containableId;
        if (string.IsNullOrEmpty(defId))
            defId = node.KnownAnomalyDefIds[0];
        if (string.IsNullOrEmpty(defId)) return "";
        if (registry.AnomaliesById.TryGetValue(defId, out var def) && def != null)
            return def.name;
        return defId;
    }

    private static string BuildAnomalyStatusText(NodeState node)
    {
        if (node == null) return "异常：无";

        var registry = DataRegistry.Instance;
        var active = node.ActiveAnomalyIds ?? new List<string>();
        var known = node.KnownAnomalyDefIds ?? new List<string>();

        var contained = new HashSet<string>();
        if (node.ManagedAnomalies != null)
        {
            foreach (var m in node.ManagedAnomalies)
            {
                if (m == null || string.IsNullOrEmpty(m.AnomalyId)) continue;
                contained.Add(m.AnomalyId);
            }
        }

        var ids = new HashSet<string>();
        foreach (var id in active) if (!string.IsNullOrEmpty(id)) ids.Add(id);
        foreach (var id in known) if (!string.IsNullOrEmpty(id)) ids.Add(id);
        foreach (var id in contained) if (!string.IsNullOrEmpty(id)) ids.Add(id);

        if (ids.Count == 0) return "异常：无";

        var parts = new List<string>();
        foreach (var id in ids)
        {
            string name = id;
            if (registry != null && registry.AnomaliesById.TryGetValue(id, out var def) && def != null && !string.IsNullOrEmpty(def.name))
                name = def.name;

            bool isContained = contained.Contains(id);
            bool isDiscovered = known.Contains(id);

            string discover = isDiscovered ? "已发现" : "未发现";
            string contain = isContained ? "已收容" : "未收容";
            parts.Add($"{name}({discover}/{contain})");
        }

        return "异常：" + string.Join("，", parts);
    }

    private static string ResolveManagedAnomalyName(NodeState node, string managedAnomalyId)
    {
        if (node == null || node.ManagedAnomalies == null || node.ManagedAnomalies.Count == 0) return "";
        if (!string.IsNullOrEmpty(managedAnomalyId))
        {
            var m = node.ManagedAnomalies.FirstOrDefault(x => x != null && x.Id == managedAnomalyId);
            if (m != null) return m.Name ?? "";
        }
        return "";
    }

    private static string ResolveTaskDefLabel(NodeTask task)
    {
        if (task == null) return "";
        var def = DataRegistry.Instance != null ? DataRegistry.Instance.GetTaskDefById(task.TaskDefId) : null;
        if (def != null && !string.IsNullOrEmpty(def.name)) return def.name;
        return task.TaskDefId ?? "";
    }

    private static string BuildTaskStatusText(NodeTask t, bool hasSquad)
    {
        if (t.State == TaskState.Completed) return "状态：已完成";
        if (t.State == TaskState.Cancelled) return "状态：已取消";

        if (!hasSquad) return "状态：未指派";

        // Manage tasks: no "待开始" concept; once assigned, it is immediately "管理中".
        if (t.Type == TaskType.Manage)
            return "状态：管理中";

        if (t.Progress <= EPS) return "状态：待开始";

        return $"状态：进行中（{(int)(GetTaskProgress01(t) * 100)}%）";
    }

    private static TaskActionMode GetActionMode(NodeTask t, bool hasSquad)
    {
        if (t == null) return TaskActionMode.None;
        if (t.State != TaskState.Active) return TaskActionMode.None;
        if (!hasSquad) return TaskActionMode.None;
        // Manage tasks: always allow Cancel to release agents; no Retreat confirmation flow.
        if (t.Type == TaskType.Manage) return TaskActionMode.Cancel;

        return (t.Progress > EPS) ? TaskActionMode.Retreat : TaskActionMode.Cancel;
    }

    private static float GetTaskProgress01(NodeTask task)
    {
        if (task == null) return 0f;
        if (task.Type == TaskType.Manage) return 0f;
        int baseDays = GetTaskBaseDays(task);
        float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
        return Mathf.Clamp01(progress / baseDays);
    }

    private static int GetTaskBaseDays(NodeTask task)
    {
        if (task == null) return 1;
        var registry = DataRegistry.Instance;
        if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            return task.InvestigateNoResultBaseDays;
        string anomalyId = task.SourceAnomalyId;
        if (string.IsNullOrEmpty(anomalyId) || registry == null) return 1;
        return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
    }

    private string BuildActionLabel(TaskActionMode mode, string taskId)
    {
        if (mode == TaskActionMode.Cancel) return "取消";
        if (mode == TaskActionMode.Retreat)
        {
            float until = 0f;
            _rowConfirmUntil.TryGetValue(taskId, out until);
            return (Time.unscaledTime <= until) ? "确认撤退" : "撤退";
        }
        return "";
    }

    // ----------------------
    // Dispatch buttons
    // ----------------------

    private void UpdateDispatchButtons(NodeState n, int containableCount, bool hasContainables)
    {
        if (n == null) return;

        // N-task model:
        // - Investigate: can be initiated freely.
        // - Contain: requires discovered containables.

        bool canInvestigate = true; // 调查随时可发起
        bool canContain = hasContainables; // 收容必须有可收容物（已发现且未收容）

        _containDisabledForNoAnomalies = !hasContainables;


        if (investigateButton) investigateButton.interactable = canInvestigate;
        if (containButton) containButton.interactable = canContain && hasContainables;

        Debug.Log($"[NodePanelContainGate] nodeId={_nodeId} containableCount={containableCount} hasContainables={hasContainables} canContain={canContain} btn={(containButton ? containButton.interactable : false)}");
    }

    private void UpdateManageButton(NodeState n)
    {
        if (!manageButton) return;
        if (n == null)
        {
            manageButton.interactable = false;
            return;
        }

        // 管理入口属于“节点内异常”，因此按当前节点是否存在已收容异常来决定是否可点。
        // 如需允许空态也可打开，把下面的 hasAny 判断改为 true。
        bool hasAny = n.ManagedAnomalies != null && n.ManagedAnomalies.Any(x => x != null && x.Favorited);
        manageButton.interactable = hasAny;
    }

    // ----------------------
    // Utilities
    // ----------------------

    private static TMP_Text GetTmp(Transform parent, string childName)
    {
        if (parent == null) return null;
        // Deep search to support templates like TaskRowTemplate/Col/Title
        var t = FindDeepChild(parent, childName);
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
// </EXPORT_BLOCK>
