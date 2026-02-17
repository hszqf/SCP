// Canvas-maintained file: UI/UIPanelRoot
// Source: Assets/Scripts/UI/UIPanelRoot.cs
// Version: UI_UIPanelRoot_v2_20260114a
// Updated for N-task backend:
// - Each click on 调查/收容 creates a NEW task (NodeTask) and opens AgentPicker bound to that taskId.
// - This enables multiple investigate tasks and multiple contain tasks (one per containable).
// - On picker cancel (or close), the newly created task is cancelled to avoid leaving invisible active tasks.
//
// Notes:
// - Busy check still uses GameControllerTaskExt.AreAgentsBusy (global task scan).
using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using UnityEngine;

public class UIPanelRoot : MonoBehaviour
{
    public static UIPanelRoot I { get; private set; }

    [Header("Prefabs (请把 Assets/Prefabs/UI 下的文件拖进来)")]
    [SerializeField] private HUD hudPrefab; // HUD Prefab
    [SerializeField] private ConfirmDialog confirmDialogPrefab;
    [SerializeField] private GameObject managePanelPrefab; // 绠＄悊闈㈡澘 Prefab锛堜綘鏂板缓鐨勭鐞嗙晫闈級
    [SerializeField] private RecruitPanel recruitPanelPrefab;
    [SerializeField] private RosterPanel rosterPanelPrefab;


    // --- 杩愯鏃跺疄渚?(鑷姩鐢熸垚) ---
    private HUD _hud;
    private ConfirmDialog _confirmDialog;
    private GameObject _managePanel;
    private AnomalyManagePanel _managePanelView;
    private RecruitPanel _recruitPanel;
    private RosterPanel _rosterPanel;

    private List<GameObject> _modalStack = new List<GameObject>();
    private bool _confirmDialogOnClosedHooked;

    private string _currentCityId;
    private string _manageCityId; // 当前打开的管理面板所对应的节点（与 NodePanel 的当前节点解耦）
    private string _pickerTaskId;
    private string _manageTargetAnomalyId;

    public string CurrentNodeId => _currentCityId;
    public string ManageNodeId => _manageCityId;

    private void Awake()
    {
        I = this;
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private void Start()
    {
        InitHUD();
        int count = AgentPickerItemView.PreloadAvatars();
        Debug.Log($"[AvatarPreload] count={count}");
    }

    private void OnEnable()
    {
        //if (GameController.I != null) GameController.I.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        //if (GameController.I != null) GameController.I.OnStateChanged -= OnGameStateChanged;
    }

    void InitHUD()
    {
        if (_hud) return;
        if (hudPrefab)
        {
            _hud = Instantiate(hudPrefab, transform);
            // HUD should be at the bottom so panels can cover it
            _hud.transform.SetAsFirstSibling();
        }
    }



    // ================== CONFIRM DIALOG ==================

    void EnsureConfirmDialog()
    {
        if (_confirmDialog) return;
        if (!confirmDialogPrefab) return;
        _confirmDialog = Instantiate(confirmDialogPrefab, transform);
        HookConfirmDialogOnClosed();
    }

    public void ShowInfo(string title, string message)
    {
        EnsureConfirmDialog();
        if (_confirmDialog)
        {
            _confirmDialog.ShowInfo(title, message);
            _confirmDialog.transform.SetAsLastSibling();
            PushModal(_confirmDialog.gameObject, "show_confirm");
            RefreshModalStack("show_confirm", _confirmDialog.gameObject);
        }
        else
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog prefab not set. Info: {title} / {message}");
        }
    }

    public void ShowConfirm(
        string title,
        string message,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = "确认",
        string cancelText = "取消")
    {
        EnsureConfirmDialog();
        if (_confirmDialog)
        {
            _confirmDialog.ShowConfirm(title, message, onConfirm, onCancel, confirmText, cancelText);
            _confirmDialog.transform.SetAsLastSibling();
            PushModal(_confirmDialog.gameObject, "show_confirm");
            RefreshModalStack("show_confirm", _confirmDialog.gameObject);
        }
        else
        {
            Debug.LogWarning($"[UIPanelRoot] ConfirmDialog prefab not set. Confirm: {title} / {message}");
        }
    }

    // ================== ASSIGNMENT PANEL (Investigate / Contain) ==================
    public void OpenInvestigateAssignPanelForAnomaly(string anomalyInstanceId)
    {
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(anomalyInstanceId)) return;

        EnsureManagePanel();
        if (!_managePanelView) return;

        var gc = GameController.I;
        var state = gc.State;
        var registry = DataRegistry.Instance;

        // Resolve anomaly + display name (def name)
        var anom = Core.DispatchSystem.FindAnomaly(state, anomalyInstanceId);
        var defId = anom?.AnomalyDefId;

        string displayName = null;
        if (!string.IsNullOrEmpty(defId) &&
            registry != null &&
            registry.AnomaliesById != null &&
            registry.AnomaliesById.TryGetValue(defId, out var def) &&
            def != null)
        {
            displayName = def.name;
        }
        if (string.IsNullOrEmpty(displayName))
            displayName = !string.IsNullOrEmpty(defId) ? defId : anomalyInstanceId;

        var (slotsMin, slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Investigate, 1, int.MaxValue);

        if (_managePanel) _managePanel.SetActive(true);
        _managePanel.transform.SetAsLastSibling();
        PushModal(_managePanel, "open manage");
        RefreshModalStack("open manage", _managePanel);

        var targets = new List<AnomalyManagePanel.TargetEntry>
    {
        new AnomalyManagePanel.TargetEntry
        {
            id = anomalyInstanceId,
            title = $"调查：{displayName}",
            subtitle = null,
            disabled = false
        }
    };

        _managePanelView.ShowGeneric(
            header: "调查",
            hint: "派遣干员调查该异常",
            targets: targets,
            agentSlotsMin: slotsMin,
            agentSlotsMax: slotsMax,
            onConfirm: (targetId, agentIds) =>
            {
                if (agentIds == null || agentIds.Count == 0)
                {
                    ShowInfo("派遣失败", "未选择干员");
                    return;
                }

                // single-target flow: fallback to anomalyInstanceId
                var key = string.IsNullOrEmpty(targetId) ? anomalyInstanceId : targetId;

                string err;
                if (!Core.DispatchSystem.TrySetRoster(state, key, AssignmentSlot.Investigate, agentIds, out err))
                {
                    ShowInfo("派遣失败", err);
                    return;
                }

                if (!GameControllerTaskExt.AreAgentsUsable(gc, agentIds, out var usableReason))
                {
                    ShowInfo("派遣失败", usableReason);
                    return;
                }

                if (GameControllerTaskExt.AreAgentsBusy(gc, agentIds, null))
                {
                    ShowInfo("派遣失败", "部分干员正在其他任务执行中");
                    return;
                }

                CloseModal(_managePanel, "assign_confirm");
                // RefreshNodePanel(); // 新结构不需要
            },
            modeLabel: "Investigate"
        );

        // AnomalyManagePanel.ShowGeneric already auto-selects single target when targets.Count == 1.
    }

    // 新增：针对指定 anomalyKey 打开的 Contain 指派面板（targets 只有 1 个并自动选中）
    public void OpenContainAssignPanelForAnomaly(string anomalyKey)
    {
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(anomalyKey)) return;

        EnsureManagePanel();
        if (!_managePanelView) return;

        var gc = GameController.I;

        var registry = DataRegistry.Instance;
        var (slotsMin, slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Contain, 1, int.MaxValue);

        if (_managePanel) _managePanel.SetActive(true);
        _managePanel.transform.SetAsLastSibling();
        PushModal(_managePanel, "open manage");
        RefreshModalStack("open manage", _managePanel);

        var targets = new List<AnomalyManagePanel.TargetEntry>
        {
            new AnomalyManagePanel.TargetEntry
            {
                id = anomalyKey,
                title = "收容",
                subtitle = null,
                disabled = false
            }
        };

        _managePanelView.ShowGeneric(
            header: $"收容",
            hint: "派遣干员进行收容",
            targets: targets,
            agentSlotsMin: slotsMin,
            agentSlotsMax: slotsMax,
            onConfirm: (targetId, agentIds) =>
            {
                // 兜底：若用户仍点了 confirm，则写 roster 并关闭面板
                if (agentIds == null || agentIds.Count == 0)
                {
                    ShowInfo("派遣失败", "未选择干员");
                    return;
                }

                var state = gc.State;
                var key = string.IsNullOrEmpty(targetId) ? anomalyKey : targetId;

                string err;
                if (!Core.DispatchSystem.TrySetRoster(state, key, AssignmentSlot.Contain, agentIds, out err))
                {
                    ShowInfo("派遣失败", err);
                    return;
                }

                CloseModal(_managePanel, "assign_confirm");
                //RefreshNodePanel();
            },
            modeLabel: "Contain"
        );

        // AnomalyManagePanel.ShowGeneric already auto-selects single target when targets.Count == 1.
    }

    // 新增：针对指定 anomalyKey 打开的 Operate/Manage 指派面板（targets 只有 1 个并自动选中）
    public void OpenOperateAssignPanelForAnomaly( string anomalyKey)
    {
        if (GameController.I == null) return;
        if (string.IsNullOrEmpty(anomalyKey)) return;

        EnsureManagePanel();
        if (!_managePanelView) return;

        var gc = GameController.I;


        var registry = DataRegistry.Instance;
        var (slotsMin, slotsMax) = registry.GetTaskAgentSlotRangeWithWarn(TaskType.Manage, 0, int.MaxValue);


        if (_managePanel) _managePanel.SetActive(true);
        _managePanel.transform.SetAsLastSibling();
        PushModal(_managePanel, "open manage");
        RefreshModalStack("open manage", _managePanel);

        var targets = new List<AnomalyManagePanel.TargetEntry>
        {
            new AnomalyManagePanel.TargetEntry
            {
                id = anomalyKey,
                title = "管理",
                subtitle = null,
                disabled = false
            }
        };

        _managePanelView.ShowGeneric(
            header: $"管理",
            hint: "派遣干员进行管理（产出负熵）",
            targets: targets,
            agentSlotsMin: slotsMin,
            agentSlotsMax: slotsMax,
            onConfirm: (targetId, agentIds) =>
            {
                // 兜底：写一次 roster（Operate slot）并关闭
                if (agentIds == null) agentIds = new List<string>();
                var state = gc.State;
                var key = string.IsNullOrEmpty(targetId) ? anomalyKey : targetId;

                string err;
                if (!Core.DispatchSystem.TrySetRoster(state, key, AssignmentSlot.Operate, agentIds, out err))
                {
                    ShowInfo("派遣失败", err);
                    return;
                }

                CloseModal(_managePanel, "assign_confirm");
                //RefreshNodePanel();
            },
            modeLabel: "Operate"
        );

        // AnomalyManagePanel.ShowGeneric will auto-select the single provided target.
    }

    // ================== MANAGE PANEL ==================

    void EnsureManagePanel()
    {
        if (_managePanel) return;
        if (!managePanelPrefab)
        {
            Debug.LogWarning("[UIPanelRoot] managePanelPrefab 未配置！");
            return;
        }
        _managePanel = Instantiate(managePanelPrefab, transform);
        _managePanelView = _managePanel.GetComponent<AnomalyManagePanel>();
        _managePanel.SetActive(false);
    }


    // ================== ROSTER ==================

    void EnsureRosterPanel()
    {
        if (_rosterPanel) return;
        if (!rosterPanelPrefab)
        {
            Debug.LogError("[UIPanelRoot] rosterPanelPrefab 未配置，无法打开 RosterPanel。");
            return;
        }

        _rosterPanel = Instantiate(rosterPanelPrefab, transform);

        if (_rosterPanel) _rosterPanel.gameObject.SetActive(false);
    }

    public void OpenRosterPanel()
    {
        EnsureRosterPanel();
        if (_rosterPanel)
        {
            _rosterPanel.Show();
            _rosterPanel.gameObject.SetActive(true);
            _rosterPanel.transform.SetAsLastSibling();
            PushModal(_rosterPanel.gameObject, "open_roster");
            RefreshModalStack("open_roster", _rosterPanel.gameObject);
        }
    }

    public void HideRosterPanel()
    {
        if (_rosterPanel) CloseModal(_rosterPanel.gameObject, "close roster");
    }

    // ================== RECRUIT ==================

    void EnsureRecruitPanel()
    {
        if (_recruitPanel) return;
        if (!recruitPanelPrefab)
        {
            Debug.LogError("[UIPanelRoot] recruitPanelPrefab 未配置，无法打开 RecruitPanel。");
            return;
        }

        _recruitPanel = Instantiate(recruitPanelPrefab, transform);

        if (_recruitPanel) _recruitPanel.gameObject.SetActive(false);
    }

    public void OpenRecruit()
    {
        EnsureRecruitPanel();
        if (_recruitPanel)
        {
            _recruitPanel.Show();
            _recruitPanel.gameObject.SetActive(true);
            _recruitPanel.transform.SetAsLastSibling();
            PushModal(_recruitPanel.gameObject, "open_recruit");
            RefreshModalStack("open_recruit", _recruitPanel.gameObject);
        }
    }

    public void OpenRecruitPanel()
    {
        OpenRecruit();
    }

    public void CloseRecruit()
    {
        if (_recruitPanel) CloseModal(_recruitPanel.gameObject, "close recruit");
    }

    public void CloseAll()
    {
        if (_confirmDialog) CloseModal(_confirmDialog.gameObject, "close all");
        if (_managePanel) CloseModal(_managePanel, "close all");
        if (_recruitPanel) CloseModal(_recruitPanel.gameObject, "close all");
        if (_rosterPanel) CloseModal(_rosterPanel.gameObject, "close all");

        _modalStack.Clear();
    }



    // ================== MODAL STACK ==================

    public void CloseModal(GameObject panel, string reason = null)
    {
        var safeReason = string.IsNullOrEmpty(reason) ? "close" : reason;
        PopModal(panel, safeReason);

        if (panel == null)
        {
            RefreshModalStack("close");
            return;
        }

        var confirm = panel.GetComponent<ConfirmDialog>();
        if (confirm != null)
        {
            confirm.Hide();
        }
        else
        {
            var closable = panel.GetComponent<IModalClosable>();
            if (closable != null)
            {
                closable.CloseFromRoot();
            }
            else
            {
                panel.SetActive(false);
            }
        }

        RefreshModalStack("close", panel);
    }

    public void CloseTopModal(string reason = null)
    {
        RefreshModalStack("close_top_pre");
        if (_modalStack == null) _modalStack = new List<GameObject>();
        _modalStack.RemoveAll(p => p == null);

        if (_modalStack.Count == 0) return;

        var top = _modalStack[_modalStack.Count - 1];
        CloseModal(top, string.IsNullOrEmpty(reason) ? "close_top" : reason);
    }

    private void PushModal(GameObject panel, string reason)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        if (panel != null)
        {
            _modalStack.Remove(panel);
            _modalStack.Add(panel);
        }

        LogModalStack("Push", panel, reason);
    }

    private void PopModal(GameObject panel, string reason)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        if (panel != null)
        {
            _modalStack.Remove(panel);
        }

        LogModalStack("Pop", panel, reason);
    }

    private void RefreshModalStack(string reason, GameObject relatedPanel = null, bool sortBySiblingIndex = false)
    {
        if (_modalStack == null) _modalStack = new List<GameObject>();

        _modalStack.RemoveAll(p => p == null || !p.activeInHierarchy);

        if (sortBySiblingIndex)
        {
            _modalStack.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        }

        LogModalStack("Refresh", relatedPanel, reason);
        RefreshDimmerStack(reason, relatedPanel);
    }

    private void RefreshDimmerStack(string reason, GameObject relatedPanel = null)
    {
        if (_modalStack == null) return;

        _modalStack.RemoveAll(p => p == null || !p.activeInHierarchy);

        ModalDimmerHandle topHandle = null;
        for (int i = _modalStack.Count - 1; i >= 0; i--)
        {
            var panel = _modalStack[i];
            if (panel == null) continue;
            if (!panel.activeInHierarchy) continue;

            var handle = panel.GetComponent<ModalDimmerHandle>();
            if (handle != null)
            {
                topHandle = handle;
                break;
            }
        }

        foreach (var panel in _modalStack)
        {
            if (panel == null) continue;
            var handle = panel.GetComponent<ModalDimmerHandle>();
            if (handle == null) continue;

            handle.SetDimmerActive(handle == topHandle);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var topName = topHandle != null ? topHandle.gameObject.name : "(none)";
        Debug.Log($"[DimmerStack] top={topName} count={_modalStack.Count} reason={reason}");
#endif
    }

    private void LogModalStack(string action, GameObject panel, string reason)
    {
        var panelName = panel != null ? panel.name : "ALL";
        var safeReason = string.IsNullOrEmpty(reason) ? "(no-reason)" : reason;
        Debug.Log($"[ModalStack] action={action} panel={panelName} count={_modalStack?.Count ?? 0} reason={safeReason}");
    }

    private void HookConfirmDialogOnClosed()
    {
        if (_confirmDialog == null || _confirmDialogOnClosedHooked) return;

        _confirmDialog.OnClosed += HandleConfirmDialogClosed;
        _confirmDialogOnClosedHooked = true;
    }

    private void HandleConfirmDialogClosed()
    {
        if (_confirmDialog == null) return;
        if (_modalStack != null && _modalStack.Contains(_confirmDialog.gameObject))
        {
            PopModal(_confirmDialog.gameObject, "ConfirmDialog.OnClosed");
        }
    }

    // Pick a containable that is not already targeted by an active containment task when possible.
    string PickNextContainableId(CityState node)
    {
        if (node == null || node.KnownAnomalyDefIds == null || node.KnownAnomalyDefIds.Count == 0) return null;

        var activeTargets = (node.Tasks == null)
            ? new HashSet<string>()
            : new HashSet<string>(node.Tasks
                .Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain)
                .Select(t => t.SourceAnomalyId)
                .Where(x => !string.IsNullOrEmpty(x)));

        foreach (var defId in node.KnownAnomalyDefIds)
        {
            if (!activeTargets.Contains(defId)) return defId;
        }

        // Fallback: allow multiple tasks for same anomaly if all are already targeted.
        return node.KnownAnomalyDefIds[0];
    }
}
// </EXPORT_BLOCK>
