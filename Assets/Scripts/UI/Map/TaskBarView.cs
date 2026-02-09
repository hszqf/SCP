// Task bar view - displays task progress with agent avatars, HP/SAN, and progress bar
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    public class TaskBarView : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Transform agentAvatarsContainer;
        [SerializeField] private GameObject agentAvatarPrefab;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text statusText;

        private readonly List<GameObject> _avatarInstances = new List<GameObject>();

        public void SetTask(NodeTask task, string nodeId)
        {
            if (task == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            RefreshAgentAvatars(task);
            RefreshProgress(task, nodeId);
        }

        private void RefreshAgentAvatars(NodeTask task)
        {
            // Clear existing avatars
            foreach (var avatar in _avatarInstances)
            {
                if (avatar != null)
                    Destroy(avatar);
            }
            _avatarInstances.Clear();

            if (agentAvatarsContainer == null || agentAvatarPrefab == null)
                return;

            var agentIds = task.AssignedAgentIds ?? new List<string>();
            
            // If no agents assigned but task has requirements, show placeholder avatars
            if (agentIds.Count == 0)
            {
                var registry = DataRegistry.Instance;
                if (registry != null)
                {
                    var (minSlots, maxSlots) = registry.GetTaskAgentSlotRangeWithWarn(task.Type, 1, int.MaxValue);
                    
                    // Show minimum required agents as placeholders
                    for (int i = 0; i < minSlots && i < 4; i++)
                    {
                        CreatePlaceholderAvatar();
                    }
                }
                return;
            }

            // Show actual agents
            var gc = GameController.I;
            if (gc == null)
                return;

            foreach (var agentId in agentIds.Take(4)) // Limit to 4 avatars for space
            {
                var agent = gc.State.Agents.FirstOrDefault(a => a != null && a.Id == agentId);
                if (agent != null)
                {
                    CreateAgentAvatar(agent);
                }
                else
                {
                    CreatePlaceholderAvatar();
                }
            }
        }

        private void CreateAgentAvatar(AgentState agent)
        {
            var avatarObj = Instantiate(agentAvatarPrefab, agentAvatarsContainer);
            
            // Find avatar components
            var avatarImage = avatarObj.transform.Find("Avatar")?.GetComponent<Image>();
            var hpText = avatarObj.transform.Find("HP")?.GetComponent<TMP_Text>();
            var sanText = avatarObj.transform.Find("SAN")?.GetComponent<TMP_Text>();

            if (hpText != null)
                hpText.text = $"HP {agent.HP}";

            if (sanText != null)
                sanText.text = $"SAN {agent.SAN}";

            _avatarInstances.Add(avatarObj);
        }

        private void CreatePlaceholderAvatar()
        {
            var avatarObj = Instantiate(agentAvatarPrefab, agentAvatarsContainer);
            
            var hpText = avatarObj.transform.Find("HP")?.GetComponent<TMP_Text>();
            var sanText = avatarObj.transform.Find("SAN")?.GetComponent<TMP_Text>();

            if (hpText != null)
                hpText.text = "HP -";

            if (sanText != null)
                sanText.text = "SAN -";

            _avatarInstances.Add(avatarObj);
        }

        private void RefreshProgress(NodeTask task, string nodeId)
        {
            if (progressBar != null)
            {
                float progress01 = GetTaskProgress01(task);
                progressBar.value = progress01;
            }

            if (statusText != null)
            {
                int progressPercent = (int)(GetTaskProgress01(task) * 100);
                string taskTypeStr = GetTaskTypeString(task.Type);
                statusText.text = $"{taskTypeStr} {progressPercent}%";
            }
        }

        private float GetTaskProgress01(NodeTask task)
        {
            if (task == null)
                return 0f;

            int baseDays = GetTaskBaseDays(task);
            if (baseDays <= 0)
                return 0f;

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

        private string GetTaskTypeString(TaskType type)
        {
            switch (type)
            {
                case TaskType.Investigate:
                    return "调查";
                case TaskType.Contain:
                    return "收容";
                case TaskType.Manage:
                    return "管理";
                default:
                    return "任务";
            }
        }
    }
}
