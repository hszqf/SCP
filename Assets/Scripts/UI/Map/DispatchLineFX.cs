// Dispatch line FX - animated line and icon when tasks start/complete
// Author: Canvas
// Version: 1.0

using System.Collections;
using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI.Map
{
    public class DispatchLineFX : MonoBehaviour
    {
        public static DispatchLineFX Instance { get; private set; }

        [Header("Line Settings")]
        [SerializeField] private GameObject linePrefab;
        [SerializeField] private float lineAnimDuration = 2.5f;
        [SerializeField] private Color lineColor = new Color(0.3f, 0.7f, 1f, 0.8f);

        [Header("Icon Settings")]
        [SerializeField] private GameObject dispatchIconPrefab;
        [SerializeField] private float iconMoveSpeed = 200f;

        [Header("Completion FX")]
        [SerializeField] private GameObject completionIconPrefab;
        [SerializeField] private float completionDisplayDuration = 1f;

        private readonly List<AnimatedLine> _activeLines = new List<AnimatedLine>();
        private readonly Dictionary<string, TaskState> _lastTaskStates = new Dictionary<string, TaskState>();

        private RectTransform _canvasRect;

        private void Awake()
        {
            Instance = this;
            _canvasRect = GetComponent<RectTransform>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            if (GameController.I != null)
                GameController.I.OnStateChanged += OnGameStateChanged;
        }

        private void OnDisable()
        {
            if (GameController.I != null)
                GameController.I.OnStateChanged -= OnGameStateChanged;
        }

        private void OnGameStateChanged()
        {
            CheckForTaskStateChanges();
        }

        private void CheckForTaskStateChanges()
        {
            if (GameController.I == null)
                return;

            foreach (var node in GameController.I.State.Nodes)
            {
                if (node?.Tasks == null)
                    continue;

                foreach (var task in node.Tasks)
                {
                    if (task == null)
                        continue;

                    string taskKey = task.Id;
                    TaskState newState = task.State;

                    if (!_lastTaskStates.TryGetValue(taskKey, out TaskState oldState))
                    {
                        // First time seeing this task
                        _lastTaskStates[taskKey] = newState;
                        
                        // Check if task just started (has progress > 0 or agents assigned)
                        bool justStarted = task.AssignedAgentIds != null && task.AssignedAgentIds.Count > 0;
                        if (justStarted && newState == TaskState.Active)
                        {
                            Debug.Log($"[MapUI] Task started: {task.Id} type={task.Type} node={node.Id}");
                            PlayDispatchAnimation("HQ", node.Id, task.Type);
                        }
                        continue;
                    }

                    // Check for state changes
                    if (oldState != newState)
                    {
                        _lastTaskStates[taskKey] = newState;

                        if (newState == TaskState.Active && oldState != TaskState.Active)
                        {
                            // Task started
                            Debug.Log($"[MapUI] Task started: {task.Id} type={task.Type} node={node.Id}");
                            PlayDispatchAnimation("HQ", node.Id, task.Type);
                        }
                        else if (newState == TaskState.Completed && oldState == TaskState.Active)
                        {
                            // Task completed
                            Debug.Log($"[MapUI] Task completed: {task.Id} type={task.Type} node={node.Id}");
                            PlayCompletionAnimation(node.Id, true);
                        }
                        else if (newState == TaskState.Cancelled)
                        {
                            // Task cancelled
                            Debug.Log($"[MapUI] Task cancelled: {task.Id}");
                        }
                    }
                }
            }
        }

        public void PlayDispatchAnimation(string fromNodeId, string toNodeId, TaskType taskType)
        {
            if (SimpleWorldMapPanel.Instance == null)
                return;

            Vector2 startPos = SimpleWorldMapPanel.Instance.GetNodePosition(fromNodeId);
            Vector2 endPos = SimpleWorldMapPanel.Instance.GetNodePosition(toNodeId);

            StartCoroutine(AnimateDispatchLine(startPos, endPos, taskType));
        }

        private IEnumerator AnimateDispatchLine(Vector2 startPos, Vector2 endPos, TaskType taskType)
        {
            // Create line
            GameObject lineObj = null;
            Image lineImage = null;

            if (linePrefab != null)
            {
                lineObj = Instantiate(linePrefab, transform);
                lineImage = lineObj.GetComponent<Image>();
                if (lineImage != null)
                {
                    lineImage.color = lineColor;
                }
            }
            else
            {
                // Create simple line using Image
                lineObj = new GameObject("DispatchLine");
                lineObj.transform.SetParent(transform, false);
                lineImage = lineObj.AddComponent<Image>();
                lineImage.color = lineColor;
            }

            // Position and rotate line
            RectTransform lineRT = lineObj.GetComponent<RectTransform>();
            Vector2 direction = endPos - startPos;
            float distance = direction.magnitude;
            
            lineRT.anchoredPosition = startPos;
            lineRT.sizeDelta = new Vector2(distance, 2f);
            lineRT.pivot = new Vector2(0, 0.5f);
            
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            lineRT.rotation = Quaternion.Euler(0, 0, angle);

            // Create moving icon
            GameObject iconObj = null;
            if (dispatchIconPrefab != null)
            {
                iconObj = Instantiate(dispatchIconPrefab, transform);
            }
            else
            {
                // Create simple icon
                iconObj = new GameObject("DispatchIcon");
                iconObj.transform.SetParent(transform, false);
                var iconImage = iconObj.AddComponent<Image>();
                iconImage.color = Color.white;
                
                var iconRT = iconObj.GetComponent<RectTransform>();
                iconRT.sizeDelta = new Vector2(30, 30);
                
                // Add text label for task type
                var textObj = new GameObject("Label");
                textObj.transform.SetParent(iconObj.transform, false);
                var text = textObj.AddComponent<TMP_Text>();
                text.text = GetTaskTypeIcon(taskType);
                text.fontSize = 20;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.black;
            }

            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            iconRT.anchoredPosition = startPos;

            // Animate icon moving along line
            float elapsed = 0f;
            while (elapsed < lineAnimDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / lineAnimDuration;
                
                iconRT.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                
                yield return null;
            }

            // Fade out line
            float fadeTime = 0.5f;
            float fadeElapsed = 0f;
            Color startColor = lineImage.color;

            while (fadeElapsed < fadeTime)
            {
                fadeElapsed += Time.deltaTime;
                float alpha = 1f - (fadeElapsed / fadeTime);
                lineImage.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * alpha);
                yield return null;
            }

            // Cleanup
            if (lineObj != null)
                Destroy(lineObj);
            if (iconObj != null)
                Destroy(iconObj);
        }

        public void PlayCompletionAnimation(string nodeId, bool success)
        {
            if (SimpleWorldMapPanel.Instance == null)
                return;

            Vector2 nodePos = SimpleWorldMapPanel.Instance.GetNodePosition(nodeId);
            StartCoroutine(AnimateCompletion(nodePos, success));
        }

        private IEnumerator AnimateCompletion(Vector2 position, bool success)
        {
            GameObject iconObj = null;

            if (completionIconPrefab != null)
            {
                iconObj = Instantiate(completionIconPrefab, transform);
            }
            else
            {
                // Create simple completion icon
                iconObj = new GameObject("CompletionIcon");
                iconObj.transform.SetParent(transform, false);
                
                var text = iconObj.AddComponent<TMP_Text>();
                text.text = success ? "✓" : "✗";
                text.fontSize = 40;
                text.alignment = TextAlignmentOptions.Center;
                text.color = success ? Color.green : Color.red;
                
                var iconRT = iconObj.GetComponent<RectTransform>();
                iconRT.sizeDelta = new Vector2(50, 50);
            }

            RectTransform rt = iconObj.GetComponent<RectTransform>();
            rt.anchoredPosition = position;

            // Scale animation
            float elapsed = 0f;
            float duration = completionDisplayDuration;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                // Scale up then down
                float scale = t < 0.3f ? Mathf.Lerp(0.5f, 1.2f, t / 0.3f) : Mathf.Lerp(1.2f, 0f, (t - 0.3f) / 0.7f);
                rt.localScale = Vector3.one * scale;
                
                yield return null;
            }

            if (iconObj != null)
                Destroy(iconObj);
        }

        private string GetTaskTypeIcon(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.Investigate:
                    return "🔍";
                case TaskType.Contain:
                    return "📦";
                case TaskType.Manage:
                    return "💧";
                default:
                    return "●";
            }
        }

        private class AnimatedLine
        {
            public GameObject LineObject;
            public GameObject IconObject;
            public float Progress;
        }
    }
}
