// NewMapRuntime - Dynamically generates new map UI at runtime
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    /// <summary>
    /// Runtime map generator that creates NewMapRoot and 4 node widgets dynamically
    /// </summary>
    public class NewMapRuntime : MonoBehaviour
    {
        public static NewMapRuntime Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private Color backgroundColor = new Color(0.12f, 0.12f, 0.18f, 1f);
        [SerializeField] private Color nodeDotColor = new Color(0.3f, 0.7f, 1f, 1f);
        [SerializeField] private Color nodeTextColor = Color.white;

        private GameObject _newMapRoot;
        private GameObject _nodesRoot;
        private Dictionary<string, GameObject> _nodeWidgets = new Dictionary<string, GameObject>();

        // Fixed node positions (relative to screen, using anchors)
        private readonly Dictionary<string, Vector2> _nodePositions = new Dictionary<string, Vector2>
        {
            ["BASE"] = new Vector2(-0.25f, 0.25f),   // Left-top area
            ["N1"] = new Vector2(0.25f, 0.25f),      // Right-top area
            ["N2"] = new Vector2(-0.25f, -0.25f),    // Left-bottom area
            ["N3"] = new Vector2(0.25f, -0.25f)      // Right-bottom area
        };

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            // Wait for next frame to ensure DataRegistry and GameState are initialized
            StartCoroutine(InitializeNextFrame());
        }

        private System.Collections.IEnumerator InitializeNextFrame()
        {
            yield return null; // Wait one frame
            Initialize();
        }

        private void Initialize()
        {
            Debug.Log("[MapUI] NewMapRuntime initializing...");

            // Find Canvas to attach to
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[MapUI] Cannot find Canvas to attach NewMapRoot");
                return;
            }

            // Check old map status
            GameObject oldMapRoot = GameObject.Find("MapRoot");
            bool oldMapActive = oldMapRoot != null && oldMapRoot.activeSelf;
            string oldMapStatus = oldMapRoot != null ? "FOUND" : "NOT_FOUND";
            Debug.Log($"[MapUI] Verify oldMap={oldMapStatus}(active={oldMapActive})");

            // Create NewMapRoot structure
            CreateNewMapRoot(canvas.transform);

            // Get node data
            List<string> nodeIds = GetNodeData();

            // Create node widgets
            CreateNodeWidgets(nodeIds);

            // Log verification
            Debug.Log($"[MapUI] Verify oldMap={(oldMapRoot != null ? "FOUND" : "NOT_FOUND")}(active={oldMapActive}) newMap={(_newMapRoot != null ? "CREATED" : "FAILED")} nodes={_nodeWidgets.Count}");
        }

        private void CreateNewMapRoot(Transform canvasTransform)
        {
            // Create NewMapRoot container
            _newMapRoot = new GameObject("NewMapRoot");
            _newMapRoot.transform.SetParent(canvasTransform, false);

            RectTransform rootRect = _newMapRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            // Create Background
            GameObject background = new GameObject("Background");
            background.transform.SetParent(_newMapRoot.transform, false);

            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            bgRect.anchoredPosition = Vector2.zero;

            Image bgImage = background.AddComponent<Image>();
            bgImage.color = backgroundColor;

            // Create NodesRoot container
            _nodesRoot = new GameObject("NodesRoot");
            _nodesRoot.transform.SetParent(_newMapRoot.transform, false);

            RectTransform nodesRect = _nodesRoot.AddComponent<RectTransform>();
            nodesRect.anchorMin = Vector2.zero;
            nodesRect.anchorMax = Vector2.one;
            nodesRect.sizeDelta = Vector2.zero;
            nodesRect.anchoredPosition = Vector2.zero;

            Debug.Log("[MapUI] NewMapRoot structure created");
        }

        private List<string> GetNodeData()
        {
            List<string> nodeIds = new List<string>();

            // Try to get from GameState
            if (GameController.I != null && GameController.I.State != null && GameController.I.State.Nodes != null)
            {
                // Use first 4 nodes from GameState
                int count = 0;
                foreach (var node in GameController.I.State.Nodes)
                {
                    if (count >= 4) break;
                    nodeIds.Add(node.Id);
                    count++;
                }

                if (nodeIds.Count > 0)
                {
                    Debug.Log($"[MapUI] Nodes = {string.Join(",", nodeIds)} source=GameState");
                    return nodeIds;
                }
            }

            // Fallback to hardcoded
            nodeIds.AddRange(new[] { "BASE", "N1", "N2", "N3" });
            Debug.Log($"[MapUI] Nodes = {string.Join(",", nodeIds)} source=Hardcoded");
            return nodeIds;
        }

        private void CreateNodeWidgets(List<string> nodeIds)
        {
            foreach (var nodeId in nodeIds)
            {
                CreateNodeWidget(nodeId);
            }

            Debug.Log($"[MapUI] Created {_nodeWidgets.Count} node widgets");
        }

        private void CreateNodeWidget(string nodeId)
        {
            // Create NodeWidget container
            GameObject nodeWidget = new GameObject($"NodeWidget_{nodeId}");
            nodeWidget.transform.SetParent(_nodesRoot.transform, false);

            RectTransform widgetRect = nodeWidget.AddComponent<RectTransform>();
            widgetRect.anchorMin = new Vector2(0.5f, 0.5f);
            widgetRect.anchorMax = new Vector2(0.5f, 0.5f);
            widgetRect.sizeDelta = new Vector2(100, 100);

            // Position based on nodeId
            if (_nodePositions.ContainsKey(nodeId))
            {
                Vector2 relativePos = _nodePositions[nodeId];
                widgetRect.anchoredPosition = new Vector2(relativePos.x * 400, relativePos.y * 300);
            }

            // Add Button component for click handling
            Image widgetImage = nodeWidget.AddComponent<Image>();
            widgetImage.color = new Color(0, 0, 0, 0); // Transparent

            Button widgetButton = nodeWidget.AddComponent<Button>();
            widgetButton.onClick.AddListener(() => OnNodeClick(nodeId));

            // Create Dot (circle)
            GameObject dot = new GameObject("Dot");
            dot.transform.SetParent(nodeWidget.transform, false);

            RectTransform dotRect = dot.AddComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(40, 40);
            dotRect.anchoredPosition = Vector2.zero;

            Image dotImage = dot.AddComponent<Image>();
            dotImage.color = nodeDotColor;
            // Note: Using square shape as circular sprites require additional resources

            // Create Name Text
            GameObject nameObj = new GameObject("Name");
            nameObj.transform.SetParent(nodeWidget.transform, false);

            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.5f, 0);
            nameRect.anchorMax = new Vector2(0.5f, 0);
            nameRect.sizeDelta = new Vector2(100, 30);
            nameRect.anchoredPosition = new Vector2(0, -35);

            Text nameText = nameObj.AddComponent<Text>();
            // Display node.Name if available, otherwise fallback to nodeId
            string displayName = nodeId;
            if (GameController.I != null)
            {
                var node = GameController.I.GetNode(nodeId);
                if (node != null && !string.IsNullOrEmpty(node.Name))
                {
                    displayName = node.Name;
                }
            }
            nameText.text = displayName;
            nameText.color = nodeTextColor;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.fontSize = 14;
            nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Create TaskBarRoot (placeholder container)
            GameObject taskBarRoot = new GameObject("TaskBarRoot");
            taskBarRoot.transform.SetParent(nodeWidget.transform, false);

            RectTransform taskBarRect = taskBarRoot.AddComponent<RectTransform>();
            taskBarRect.anchorMin = new Vector2(0.5f, 0);
            taskBarRect.anchorMax = new Vector2(0.5f, 0);
            taskBarRect.sizeDelta = new Vector2(80, 10);
            taskBarRect.anchoredPosition = new Vector2(0, -55);

            // Create EventBadge (placeholder, hidden by default)
            GameObject eventBadge = new GameObject("EventBadge");
            eventBadge.transform.SetParent(nodeWidget.transform, false);

            RectTransform badgeRect = eventBadge.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1, 1);
            badgeRect.anchorMax = new Vector2(1, 1);
            badgeRect.sizeDelta = new Vector2(20, 20);
            badgeRect.anchoredPosition = new Vector2(0, 0);

            Image badgeImage = eventBadge.AddComponent<Image>();
            badgeImage.color = new Color(1f, 0.5f, 0f, 1f);

            eventBadge.SetActive(false); // Hidden by default

            // Create UnknownAnomIcon (placeholder)
            GameObject unknownIcon = new GameObject("UnknownAnomIcon");
            unknownIcon.transform.SetParent(nodeWidget.transform, false);

            RectTransform iconRect = unknownIcon.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1);
            iconRect.anchorMax = new Vector2(0.5f, 1);
            iconRect.sizeDelta = new Vector2(30, 30);
            iconRect.anchoredPosition = new Vector2(0, 10);

            Text iconText = unknownIcon.AddComponent<Text>();
            iconText.text = "?";
            iconText.color = Color.yellow;
            iconText.alignment = TextAnchor.MiddleCenter;
            iconText.fontSize = 20;
            iconText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // TODO: Show icon when node has unknown anomaly or pending events
            // Hide by default to avoid misleading placeholder
            unknownIcon.SetActive(false);

            _nodeWidgets[nodeId] = nodeWidget;
        }

        private void OnNodeClick(string nodeId)
        {
            Debug.Log($"[MapUI] Click nodeId={nodeId}");

            // Validate nodeId
            if (string.IsNullOrEmpty(nodeId))
            {
                Debug.LogWarning("[MapUI] OnNodeClick: nodeId is null or empty");
                return;
            }

            // Use UIPanelRoot to open the node panel
            if (UIPanelRoot.I != null)
            {
                // Primary path: open the existing NodePanelView
                UIPanelRoot.I.OpenNode(nodeId);
            }
            else
            {
                Debug.LogWarning("[MapUI] UIPanelRoot.I is null, cannot open node panel");
            }
        }

        // Public method for refreshing node states (future enhancement)
        // TODO: Subscribe to GameController.OnStateChanged and update node visuals
        // (task bars, event badges, anomaly icons) based on current GameState
        public void RefreshNodes()
        {
            Debug.Log("[MapUI] RefreshNodes called (implementation deferred)");
        }
    }
}
