// NodeMarkerView - Simplified node display component for NewMap system
// Author: Canvas
// Version: 2.0 (Step 2 - Prefab-based)

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    /// <summary>
    /// Displays node information: name, task bar, event badge, unknown anomaly icon.
    /// Only handles display and click events; does not manage data sources.
    /// </summary>
    public class NodeMarkerView : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Image dot;
        [SerializeField] private Text nameText;
        [SerializeField] private RectTransform taskBar;
        [SerializeField] private Transform avatarsContainer;
        [SerializeField] private Image avatarTemplate;
        [SerializeField] private Text statsText;
        [SerializeField] private Image progressBg;
        [SerializeField] private Image progressFill;
        [SerializeField] private GameObject eventBadge;
        [SerializeField] private Text eventBadgeText;
        [SerializeField] private GameObject unknownIcon;

        private string _nodeId;
        private System.Action<string> _onClick;
        private readonly List<GameObject> _avatarInstances = new List<GameObject>();
        
        // Cache font to avoid repeated Resources.GetBuiltinResource calls
        private static Font _cachedFont;

        private void Awake()
        {
            // Ensure avatarTemplate is inactive
            if (avatarTemplate != null)
                avatarTemplate.gameObject.SetActive(false);

            // Setup button click
            var button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(OnButtonClick);
            }
        }

        /// <summary>
        /// Binds this view to a node and sets up click handler.
        /// </summary>
        public void Bind(string nodeId, System.Action<string> onClick)
        {
            _nodeId = nodeId;
            _onClick = onClick;
            Debug.Log($"[MapUI] NodeMarkerView.Bind nodeId={nodeId}");
        }

        /// <summary>
        /// Refreshes display based on current node state.
        /// </summary>
        public void Refresh(NodeState node)
        {
            if (node == null)
            {
                Debug.LogWarning($"[MapUI] NodeMarkerView.Refresh: node is null for nodeId={_nodeId}");
                return;
            }

            RefreshName(node);
            RefreshEventBadge(node);
            RefreshUnknownIcon(node);
            RefreshTaskBar(node);
        }

        private void RefreshName(NodeState node)
        {
            if (nameText == null) return;
            
            string displayName = node.Name;
            if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("??"))
                displayName = node.Id;
            
            nameText.text = displayName;
        }

        private void RefreshEventBadge(NodeState node)
        {
            if (eventBadge == null) return;

            int eventCount = node.PendingEvents?.Count ?? 0;
            bool hasEvents = eventCount > 0;
            
            eventBadge.SetActive(hasEvents);
            
            if (hasEvents && eventBadgeText != null)
            {
                eventBadgeText.text = eventCount.ToString();
            }
        }

        private void RefreshUnknownIcon(NodeState node)
        {
            if (unknownIcon == null) return;

            bool hasUnknown = HasUnknownAnomaly(node);
            unknownIcon.SetActive(hasUnknown);
        }

        private bool HasUnknownAnomaly(NodeState node)
        {
            // Check if there are active anomalies that are not in the known list
            var active = node.ActiveAnomalyIds ?? new List<string>();
            var known = node.KnownAnomalyDefIds ?? new List<string>();
            
            int unknownCount = active.Except(known).Count();
            return unknownCount > 0;
        }

        private void RefreshTaskBar(NodeState node)
        {
            if (taskBar == null) return;

            // Find representative task (Contain > Investigate > Manage priority)
            var representativeTask = GetRepresentativeTask(node);
            
            if (representativeTask == null)
            {
                taskBar.gameObject.SetActive(false);
                return;
            }

            taskBar.gameObject.SetActive(true);
            
            RefreshAvatars(representativeTask);
            RefreshStats(representativeTask);
            RefreshProgress(representativeTask);
        }

        private NodeTask GetRepresentativeTask(NodeState node)
        {
            if (node.Tasks == null || node.Tasks.Count == 0)
                return null;

            var activeTasks = node.Tasks.Where(t => t != null && t.State == TaskState.Active).ToList();
            if (activeTasks.Count == 0)
                return null;

            // Priority: Contain > Investigate > Manage
            var containTask = activeTasks.FirstOrDefault(t => t.Type == TaskType.Contain);
            if (containTask != null) return containTask;

            var investigateTask = activeTasks.FirstOrDefault(t => t.Type == TaskType.Investigate);
            if (investigateTask != null) return investigateTask;

            return activeTasks.FirstOrDefault(t => t.Type == TaskType.Manage);
        }

        private void RefreshAvatars(NodeTask task)
        {
            // Clear existing avatars
            foreach (var avatar in _avatarInstances)
            {
                if (avatar != null)
                    Destroy(avatar);
            }
            _avatarInstances.Clear();

            if (avatarsContainer == null || avatarTemplate == null)
                return;

            var agentIds = task.AssignedAgentIds;
            int agentCount = agentIds?.Count ?? 0;
            
            // Create avatar for each assigned agent (up to 4)
            for (int i = 0; i < agentCount && i < 4; i++)
            {
                var avatarObj = Instantiate(avatarTemplate.gameObject, avatarsContainer);
                avatarObj.SetActive(true);
                _avatarInstances.Add(avatarObj);
                
                // Get agent data
                string agentId = agentIds[i];
                var agentState = GameController.I?.GetAgent(agentId);
                
                // Add/update HP and SAN text components for this specific avatar
                CreateOrUpdateAvatarStats(avatarObj, agentState);
            }
        }
        
        private void CreateOrUpdateAvatarStats(GameObject avatarObj, AgentState agentState)
        {
            if (avatarObj == null)
                return;
            
            // Find or create stats container
            Transform statsContainer = avatarObj.transform.Find("Stats");
            if (statsContainer == null)
            {
                GameObject statsObj = new GameObject("Stats");
                statsObj.transform.SetParent(avatarObj.transform, false);
                statsContainer = statsObj.transform;
                
                RectTransform statsRT = statsObj.AddComponent<RectTransform>();
                statsRT.anchorMin = new Vector2(0, 0);
                statsRT.anchorMax = new Vector2(1, 0);
                statsRT.pivot = new Vector2(0.5f, 1);
                statsRT.anchoredPosition = new Vector2(0, -2);
                statsRT.sizeDelta = new Vector2(0, 20);
            }
            
            // Find or create HP text
            Transform hpTransform = statsContainer.Find("HPText");
            Text hpText;
            if (hpTransform == null)
            {
                GameObject hpObj = new GameObject("HPText");
                hpObj.transform.SetParent(statsContainer, false);
                hpText = hpObj.AddComponent<Text>();
                
                // Use cached font
                if (_cachedFont == null)
                    _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                hpText.font = _cachedFont;
                
                hpText.fontSize = 10;
                hpText.alignment = TextAnchor.MiddleCenter;
                hpText.color = Color.green;
                
                RectTransform hpRT = hpObj.GetComponent<RectTransform>();
                hpRT.anchorMin = new Vector2(0, 0);
                hpRT.anchorMax = new Vector2(1, 0.5f);
                hpRT.offsetMin = Vector2.zero;
                hpRT.offsetMax = Vector2.zero;
            }
            else
            {
                hpText = hpTransform.GetComponent<Text>();
            }
            
            // Find or create SAN text
            Transform sanTransform = statsContainer.Find("SANText");
            Text sanText;
            if (sanTransform == null)
            {
                GameObject sanObj = new GameObject("SANText");
                sanObj.transform.SetParent(statsContainer, false);
                sanText = sanObj.AddComponent<Text>();
                
                // Use cached font
                if (_cachedFont == null)
                    _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                sanText.font = _cachedFont;
                
                sanText.fontSize = 10;
                sanText.alignment = TextAnchor.MiddleCenter;
                sanText.color = Color.cyan;
                
                RectTransform sanRT = sanObj.GetComponent<RectTransform>();
                sanRT.anchorMin = new Vector2(0, 0.5f);
                sanRT.anchorMax = new Vector2(1, 1);
                sanRT.offsetMin = Vector2.zero;
                sanRT.offsetMax = Vector2.zero;
            }
            else
            {
                sanText = sanTransform.GetComponent<Text>();
            }
            
            // Update text content
            if (agentState != null)
            {
                hpText.text = $"HP {agentState.HP}";
                sanText.text = $"SAN {agentState.SAN}";
            }
            else
            {
                hpText.text = "HP 100";
                sanText.text = "SAN 100";
            }
        }

        private void RefreshStats(NodeTask task)
        {
            if (statsText == null) return;

            // Display task type instead of unified stats (stats are now per-avatar)
            string taskTypeLabel = GetTaskTypeLabel(task.Type);
            statsText.text = taskTypeLabel;
        }
        
        private string GetTaskTypeLabel(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.Investigate:
                    return "Investigating";
                case TaskType.Contain:
                    return "Containing";
                case TaskType.Manage:
                    return "Managing";
                default:
                    return "Active";
            }
        }

        private void RefreshProgress(NodeTask task)
        {
            if (progressFill == null) return;

            float progress01 = GetTaskProgress01(task);
            
            // Update fill amount (assuming Image type is Filled)
            if (progressFill.type == Image.Type.Filled)
            {
                progressFill.fillAmount = progress01;
            }
            else
            {
                // Fallback: adjust sizeDelta
                var rt = progressFill.GetComponent<RectTransform>();
                if (rt != null && progressBg != null)
                {
                    var bgRect = progressBg.GetComponent<RectTransform>();
                    if (bgRect != null)
                    {
                        float width = bgRect.sizeDelta.x * progress01;
                        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
                    }
                }
            }
        }

        private float GetTaskProgress01(NodeTask task)
        {
            if (task == null)
                return 0f;

            int baseDays = GetTaskBaseDays(task);
            if (baseDays <= 0)
                return Mathf.Clamp01(task.Progress);

            return Mathf.Clamp01(task.Progress / baseDays);
        }

        private int GetTaskBaseDays(NodeTask task)
        {
            if (task == null)
                return 1;

            var registry = DataRegistry.Instance;
            if (registry == null)
                return 1;

            // Handle investigate no-result case
            if (task.Type == TaskType.Investigate && 
                task.InvestigateTargetLocked && 
                string.IsNullOrEmpty(task.SourceAnomalyId) && 
                task.InvestigateNoResultBaseDays > 0)
            {
                return task.InvestigateNoResultBaseDays;
            }

            // Get anomaly base days
            string anomalyId = task.SourceAnomalyId;
            if (string.IsNullOrEmpty(anomalyId))
                return 1;

            return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
        }

        private void OnButtonClick()
        {
            if (string.IsNullOrEmpty(_nodeId))
            {
                Debug.LogWarning("[MapUI] NodeMarkerView.OnButtonClick: nodeId is null or empty");
                return;
            }

            Debug.Log($"[MapUI] NodeMarkerView.OnButtonClick nodeId={_nodeId}");
            _onClick?.Invoke(_nodeId);
        }

        /// <summary>
        /// Optional: Set selected state visual feedback.
        /// </summary>
        public void SetSelected(bool selected)
        {
            // TODO: Add visual feedback for selection if needed
            // e.g., change dot color or add border
        }
    }
}
