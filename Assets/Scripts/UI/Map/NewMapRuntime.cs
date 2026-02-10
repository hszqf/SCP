// NewMapRuntime - Dynamically generates new map UI at runtime
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        [SerializeField] private NodeMarkerView nodeMarkerPrefab;

        private GameObject _newMapRoot;
        private GameObject _nodesRoot;
        private Dictionary<string, NodeMarkerView> _nodeViews = new Dictionary<string, NodeMarkerView>();

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

            // Unsubscribe from state changes
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged -= OnGameStateChanged;
            }
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

            // A. DIAGNOSTIC LOGS - Check EventSystem BEFORE UI creation
            EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
            Debug.Log($"[MapUI] EventSystem found={eventSystem != null} (before UI creation)");
            if (eventSystem != null)
            {
                Debug.Log($"[MapUI] EventSystem gameObject={eventSystem.gameObject.name} active={eventSystem.gameObject.activeInHierarchy} enabled={eventSystem.enabled}");
            }

            // Find Canvas to attach to
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[MapUI] Cannot find Canvas to attach NewMapRoot");
                return;
            }

            // Check Canvas GraphicRaycaster
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            Debug.Log($"[MapUI] Canvas GraphicRaycaster found={raycaster != null} canvas={canvas.gameObject.name}");
            if (raycaster != null)
            {
                Debug.Log($"[MapUI] GraphicRaycaster enabled={raycaster.enabled} ignoreReversedGraphics={raycaster.ignoreReversedGraphics} blockingObjects={raycaster.blockingObjects}");
            }

            // Check old map status
            GameObject oldMapRoot = GameObject.Find("MapRoot");
            bool oldMapActive = oldMapRoot != null && oldMapRoot.activeSelf;
            string oldMapStatus = oldMapRoot != null ? "FOUND" : "NOT_FOUND";
            Debug.Log($"[MapUI] Verify oldMap={oldMapStatus}(active={oldMapActive})");

            // Validate prefab reference
            if (nodeMarkerPrefab == null)
            {
                Debug.LogError("[MapUI] NodeMarkerPrefab is not assigned! Please assign it in the Inspector.");
                return;
            }

            // Create NewMapRoot structure
            CreateNewMapRoot(canvas.transform);

            // Get node data
            List<string> nodeIds = GetNodeData();

            // Create node widgets
            CreateNodeWidgets(nodeIds);

            // Subscribe to state changes
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged += OnGameStateChanged;
                Debug.Log("[MapUI] Subscribed to GameController.OnStateChanged");
            }

            // A. DIAGNOSTIC LOGS - Check EventSystem AFTER UI creation
            eventSystem = FindFirstObjectByType<EventSystem>();
            Debug.Log($"[MapUI] EventSystem found={eventSystem != null} (after UI creation)");
            if (eventSystem != null)
            {
                Debug.Log($"[MapUI] EventSystem gameObject={eventSystem.gameObject.name} active={eventSystem.gameObject.activeInHierarchy} enabled={eventSystem.enabled}");
            }
            else
            {
                Debug.LogWarning("[MapUI] EventSystem is missing! UI clicks will not work without EventSystem.");
                Debug.LogWarning("[MapUI] Creating EventSystem automatically...");
                
                // Create EventSystem GameObject
                GameObject eventSystemObj = new GameObject("EventSystem");
                EventSystem newEventSystem = eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<StandaloneInputModule>();
                
                Debug.Log($"[MapUI] EventSystem created: gameObject={newEventSystem.gameObject.name} enabled={newEventSystem.enabled}");
            }
            
            // Ensure Canvas has GraphicRaycaster (required for UI clicks)
            if (raycaster == null)
            {
                Debug.LogWarning($"[MapUI] Canvas {canvas.gameObject.name} missing GraphicRaycaster! Adding it now...");
                raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log($"[MapUI] GraphicRaycaster added to Canvas");
            }

            // Check NewMapRoot raycast settings
            if (_newMapRoot != null)
            {
                Image bgImage = _newMapRoot.transform.Find("Background")?.GetComponent<Image>();
                if (bgImage != null)
                {
                    Debug.Log($"[MapUI] Background Image raycastTarget={bgImage.raycastTarget}");
                }
            }

            // Log verification
            Debug.Log($"[MapUI] Verify oldMap={(oldMapRoot != null ? "FOUND" : "NOT_FOUND")}(active={oldMapActive}) newMap={(_newMapRoot != null ? "CREATED" : "FAILED")} nodes={_nodeViews.Count}");
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
            bgImage.raycastTarget = false; // Don't block raycasts to node buttons

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

            Debug.Log($"[MapUI] Created {_nodeViews.Count} node widgets");
        }

        private void CreateNodeWidget(string nodeId)
        {
            if (nodeMarkerPrefab == null)
            {
                Debug.LogError($"[MapUI] Cannot create node widget: nodeMarkerPrefab is null");
                return;
            }

            // Instantiate prefab
            NodeMarkerView view = Instantiate(nodeMarkerPrefab, _nodesRoot.transform);
            
            // Setup RectTransform position
            RectTransform viewRect = view.GetComponent<RectTransform>();
            if (viewRect != null && _nodePositions.ContainsKey(nodeId))
            {
                Vector2 relativePos = _nodePositions[nodeId];
                viewRect.anchoredPosition = new Vector2(relativePos.x * 400, relativePos.y * 300);
            }

            // Bind view
            view.Bind(nodeId, OnNodeClick);

            // Initial refresh
            if (GameController.I != null)
            {
                var node = GameController.I.GetNode(nodeId);
                if (node != null)
                {
                    view.Refresh(node);
                }
            }

            _nodeViews[nodeId] = view;
            
            Debug.Log($"[MapUI] NodeWidget created for nodeId={nodeId}");
        }

        private void OnGameStateChanged()
        {
            // Refresh all node views when state changes
            if (GameController.I == null) return;

            foreach (var kvp in _nodeViews)
            {
                string nodeId = kvp.Key;
                NodeMarkerView view = kvp.Value;
                
                if (view != null)
                {
                    var node = GameController.I.GetNode(nodeId);
                    if (node != null)
                    {
                        view.Refresh(node);
                    }
                }
            }
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

            // Diagnostic: Check UIPanelRoot status before calling OpenNode
            if (UIPanelRoot.I != null)
            {
                var uiRoot = UIPanelRoot.I;
                Debug.Log($"[MapUI] UIPanelRoot alive=true name={uiRoot.gameObject.name} active={uiRoot.gameObject.activeInHierarchy}");
                
                // Primary path: open the existing NodePanelView
                UIPanelRoot.I.OpenNode(nodeId);
            }
            else
            {
                Debug.LogWarning("[MapUI] UIPanelRootMissing (cannot open node panel)");
            }
        }

        /// <summary>
        /// Public method for manually refreshing all node views.
        /// Also called automatically via GameController.OnStateChanged subscription.
        /// </summary>
        public void RefreshNodes()
        {
            Debug.Log("[MapUI] RefreshNodes called");
            OnGameStateChanged();
        }
    }
}
