// Simplified world map panel - displays HQ + 3 cities with task bars and anomaly pins
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    public class SimpleWorldMapPanel : MonoBehaviour
    {
        public static SimpleWorldMapPanel Instance { get; private set; }

        [Header("Map Container")]
        [SerializeField] private RectTransform mapContainer;
        [SerializeField] private Image backgroundImage;

        [Header("Prefabs")]
        [SerializeField] private GameObject nodeMarkerPrefab;
        [SerializeField] private GameObject hqMarkerPrefab;

        [Header("Map Settings")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);

        // Node positions (hardcoded as per requirements)
        private readonly Dictionary<string, Vector2> _nodePositions = new Dictionary<string, Vector2>
        {
            ["HQ"] = new Vector2(0, -200),      // Bottom center
            ["N1"] = new Vector2(-300, 100),    // Left
            ["N2"] = new Vector2(300, 100),     // Right
            ["N3"] = new Vector2(0, 250)        // Top
        };

        private readonly Dictionary<string, NodeMarkerView> _nodeMarkers = new Dictionary<string, NodeMarkerView>();
        private GameObject _hqMarker;

        private void Awake()
        {
            Instance = this;
            InitializeMap();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            if (GameController.I != null)
                GameController.I.OnStateChanged += RefreshMap;
            RefreshMap();
        }

        private void OnDisable()
        {
            if (GameController.I != null)
                GameController.I.OnStateChanged -= RefreshMap;
        }

        private void InitializeMap()
        {
            if (backgroundImage != null)
                backgroundImage.color = backgroundColor;

            Debug.Log("[MapUI] Initializing simple world map");
        }

        private void Start()
        {
            SpawnMarkers();
            RefreshMap();
        }

        private void SpawnMarkers()
        {
            if (mapContainer == null)
            {
                Debug.LogError("[MapUI] MapContainer not assigned");
                return;
            }

            // Spawn HQ marker
            if (hqMarkerPrefab != null && _hqMarker == null)
            {
                _hqMarker = Instantiate(hqMarkerPrefab, mapContainer);
                var rt = _hqMarker.GetComponent<RectTransform>();
                if (rt != null)
                    rt.anchoredPosition = _nodePositions["HQ"];
                Debug.Log("[MapUI] Spawned HQ marker");
            }

            // Spawn city markers
            if (nodeMarkerPrefab != null && GameController.I != null)
            {
                foreach (var node in GameController.I.State.Nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.Id))
                        continue;

                    if (!_nodePositions.ContainsKey(node.Id))
                        continue;

                    if (_nodeMarkers.ContainsKey(node.Id))
                        continue;

                    var markerObj = Instantiate(nodeMarkerPrefab, mapContainer);
                    var rt = markerObj.GetComponent<RectTransform>();
                    if (rt != null)
                        rt.anchoredPosition = _nodePositions[node.Id];

                    var markerView = markerObj.GetComponent<NodeMarkerView>();
                    if (markerView != null)
                    {
                        markerView.Bind(node.Id, OnNodeClick);
                        _nodeMarkers[node.Id] = markerView;
                    }

                    Debug.Log($"[MapUI] Spawned marker for node {node.Id}");
                }
            }
        }

        public void RefreshMap()
        {
            if (GameController.I == null)
                return;

            foreach (var kvp in _nodeMarkers)
            {
                var marker = kvp.Value;
                var nodeId = kvp.Key;
                
                if (marker != null)
                {
                    var node = GameController.I.State.Nodes.Find(n => n.Id == nodeId);
                    if (node != null)
                        marker.Refresh(node);
                }
            }
        }

        public Vector2 GetNodePosition(string nodeId)
        {
            if (_nodePositions.TryGetValue(nodeId, out var pos))
                return pos;
            return Vector2.zero;
        }

        public Vector2 GetNodeWorldPosition(string nodeId)
        {
            if (!_nodePositions.TryGetValue(nodeId, out var anchoredPos))
                return Vector2.zero;

            if (mapContainer == null)
                return anchoredPos;

            // Convert anchored position to world position
            return mapContainer.TransformPoint(anchoredPos);
        }

        private void OnNodeClick(string nodeId)
        {
            Debug.Log($"[MapUI] Node clicked: {nodeId}");
            
            // Open node panel via UIPanelRoot
            if (UIPanelRoot.I != null)
            {
                UIPanelRoot.I.OpenNode(nodeId);
            }
        }
    }
}
