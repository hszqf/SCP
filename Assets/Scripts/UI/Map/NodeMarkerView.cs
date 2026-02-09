// Node marker view - displays city info, task bars, attention badge, and anomaly pins
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    public class NodeMarkerView : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Text nodeNameText;
        [SerializeField] private Image nodeCircle;
        [SerializeField] private GameObject taskBarContainer;
        [SerializeField] private GameObject attentionBadge;
        [SerializeField] private Transform anomalyPinsContainer;

        [Header("Prefabs")]
        [SerializeField] private GameObject taskBarPrefab;
        [SerializeField] private GameObject anomalyPinPrefab;

        [Header("Settings")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color anomalyColor = new Color(1f, 0.3f, 0.3f);

        private string _nodeId;
        private Button _clickButton;
        private readonly List<GameObject> _taskBarInstances = new List<GameObject>();
        private readonly List<AnomalyPinView> _anomalyPins = new List<AnomalyPinView>();

        private void Awake()
        {
            _clickButton = GetComponent<Button>();
            if (_clickButton == null)
                _clickButton = gameObject.AddComponent<Button>();

            _clickButton.onClick.AddListener(OnClick);
        }

        public void Initialize(string nodeId)
        {
            _nodeId = nodeId;
            Refresh();
        }

        public void Refresh()
        {
            if (string.IsNullOrEmpty(_nodeId) || GameController.I == null)
                return;

            var node = GameController.I.GetNode(_nodeId);
            if (node == null)
                return;

            RefreshNodeName(node);
            RefreshNodeCircle(node);
            RefreshTaskBars(node);
            RefreshAttentionBadge(node);
            RefreshAnomalyPins(node);
        }

        private void RefreshNodeName(NodeState node)
        {
            if (nodeNameText == null)
                return;

            // Fallback to nodeId if name is placeholder
            string displayName = node.Name;
            if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("??"))
                displayName = node.Id;

            nodeNameText.text = displayName;
        }

        private void RefreshNodeCircle(NodeState node)
        {
            if (nodeCircle == null)
                return;

            // Change color if anomaly present
            nodeCircle.color = node.HasAnomaly ? anomalyColor : normalColor;
        }

        private void RefreshTaskBars(NodeState node)
        {
            if (taskBarContainer == null)
                return;

            // Clear existing task bars
            foreach (var bar in _taskBarInstances)
            {
                if (bar != null)
                    Destroy(bar);
            }
            _taskBarInstances.Clear();

            if (node.Tasks == null)
            {
                taskBarContainer.SetActive(false);
                return;
            }

            // Get active investigate tasks
            var investigateTasks = node.Tasks
                .Where(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Investigate)
                .ToList();

            if (investigateTasks.Count == 0)
            {
                taskBarContainer.SetActive(false);
                return;
            }

            taskBarContainer.SetActive(true);

            // Show the most progressed investigate task
            var task = investigateTasks.OrderByDescending(t => t.Progress).First();
            
            if (taskBarPrefab != null && taskBarContainer != null)
            {
                var taskBarObj = Instantiate(taskBarPrefab, taskBarContainer.transform);
                var taskBarView = taskBarObj.GetComponent<TaskBarView>();
                if (taskBarView != null)
                {
                    taskBarView.SetTask(task, node.Id);
                }
                _taskBarInstances.Add(taskBarObj);
            }
        }

        private void RefreshAttentionBadge(NodeState node)
        {
            if (attentionBadge == null)
                return;

            bool needsAttention = false;

            // Check for unknown anomalies (active but not known)
            var active = node.ActiveAnomalyIds ?? new List<string>();
            var known = node.KnownAnomalyDefIds ?? new List<string>();
            int unknownCount = active.Except(known).Count();

            if (unknownCount > 0)
                needsAttention = true;

            // Check for discovered but not contained anomalies
            var containedIds = node.ManagedAnomalies?
                .Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                .Select(m => m.AnomalyId)
                .ToList() ?? new List<string>();

            var inProgressIds = node.Tasks?
                .Where(t => t != null && t.State == TaskState.Active && 
                           (t.Type == TaskType.Contain || t.Type == TaskType.Manage) &&
                           !string.IsNullOrEmpty(t.SourceAnomalyId))
                .Select(t => t.SourceAnomalyId)
                .ToList() ?? new List<string>();

            int containableCount = known
                .Except(containedIds)
                .Except(inProgressIds)
                .Count();

            if (containableCount > 0)
                needsAttention = true;

            // Check for pending events
            if (node.HasPendingEvent)
                needsAttention = true;

            attentionBadge.SetActive(needsAttention);
        }

        private void RefreshAnomalyPins(NodeState node)
        {
            if (anomalyPinsContainer == null)
                return;

            // Clear existing pins
            foreach (var pin in _anomalyPins)
            {
                if (pin != null && pin.gameObject != null)
                    Destroy(pin.gameObject);
            }
            _anomalyPins.Clear();

            if (anomalyPinPrefab == null)
                return;

            var active = node.ActiveAnomalyIds ?? new List<string>();
            var known = node.KnownAnomalyDefIds ?? new List<string>();
            var contained = node.ManagedAnomalies?
                .Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                .Select(m => m.AnomalyId)
                .ToList() ?? new List<string>();

            // Show unknown anomaly pin if there are unknown anomalies
            int unknownCount = active.Except(known).Count();
            if (unknownCount > 0)
            {
                var pinObj = Instantiate(anomalyPinPrefab, anomalyPinsContainer);
                var pinView = pinObj.GetComponent<AnomalyPinView>();
                if (pinView != null)
                {
                    pinView.Initialize(node.Id, null, AnomalyPinState.Unknown);
                    _anomalyPins.Add(pinView);
                }
            }

            // Show pins for known anomalies (limit to 1-2 for simplicity)
            int pinCount = 0;
            const int maxPins = 2;
            foreach (var anomalyId in known)
            {
                if (pinCount >= maxPins)
                    break;

                AnomalyPinState state;
                if (contained.Contains(anomalyId))
                {
                    // Check if managed
                    bool isManaged = node.Tasks?.Any(t => 
                        t != null && 
                        t.State == TaskState.Active && 
                        t.Type == TaskType.Manage &&
                        t.SourceAnomalyId == anomalyId &&
                        t.AssignedAgentIds != null &&
                        t.AssignedAgentIds.Count > 0) ?? false;

                    state = isManaged ? AnomalyPinState.Managed : AnomalyPinState.Contained;
                }
                else
                {
                    state = AnomalyPinState.Discovered;
                }

                var pinObj = Instantiate(anomalyPinPrefab, anomalyPinsContainer);
                var pinView = pinObj.GetComponent<AnomalyPinView>();
                if (pinView != null)
                {
                    pinView.Initialize(node.Id, anomalyId, state);
                    _anomalyPins.Add(pinView);
                }

                pinCount++;
            }

            // Position pins around the marker
            PositionPins();
        }

        private void PositionPins()
        {
            float radius = 80f;
            float startAngle = 45f;
            float angleStep = 90f;

            for (int i = 0; i < _anomalyPins.Count; i++)
            {
                if (_anomalyPins[i] == null)
                    continue;

                float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius
                );

                var rt = _anomalyPins[i].GetComponent<RectTransform>();
                if (rt != null)
                    rt.anchoredPosition = offset;
            }
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_nodeId))
                return;

            Debug.Log($"[MapUI] Node marker clicked: {_nodeId}");

            // Open investigate panel
            if (UIPanelRoot.I != null)
            {
                UIPanelRoot.I.OpenNode(_nodeId);
            }
        }
    }
}
