using System.Collections.Generic;
using Core;
using UnityEngine;

public class MapNodeSpawner : MonoBehaviour
{
    public static MapNodeSpawner I { get; private set; }

    [SerializeField] private RectTransform mapRect;      // MapImage 的 RectTransform
    [SerializeField] private RectTransform nodeLayer;    // NodeLayer
    [SerializeField] private NodeButton cityPrefab;
    [SerializeField] private NodeButton basePrefab; // 也可以用同一个 prefab 但换样式

    private readonly Dictionary<string, NodeButton> _nodeButtons = new();

    private void Awake()
    {
        I = this;
    }

    private void Start()
    {
        if (!mapRect || !nodeLayer || !cityPrefab || !basePrefab)
        {
            Debug.LogError("[MapNodeSpawner] Missing refs: mapRect/nodeLayer/cityPrefab/basePrefab", this);
            enabled = false;
            return;
        }

        Build();
        if (GameController.I != null)
            GameController.I.OnStateChanged += Refresh; // 需要的话
    }

    private void Build()
    {
        if (GameController.I == null || GameController.I.State?.Nodes == null) return;

        var toRemove = new List<string>();
        foreach (var kvp in _nodeButtons)
        {
            if (kvp.Value == null) toRemove.Add(kvp.Key);
        }

        foreach (var n in GameController.I.State.Nodes)
        {
            if (n == null) continue;

            if (!n.Unlocked)
            {
                if (_nodeButtons.TryGetValue(n.Id, out var existingLocked) && existingLocked != null)
                {
                    Destroy(existingLocked.gameObject);
                    toRemove.Add(n.Id);
                }
                continue;
            }

            if (!_nodeButtons.TryGetValue(n.Id, out var btn) || btn == null)
            {
                var prefab = n.Type == 0 ? basePrefab : cityPrefab;
                btn = Instantiate(prefab, nodeLayer);
                _nodeButtons[n.Id] = btn;
            }

            btn.Set(n.Id, n.Name);

            var rt = (RectTransform)btn.transform;
            var size = mapRect.rect.size;
            var location = ResolveNodeLocation(n);
            rt.anchoredPosition = new Vector2((location.x - 0.5f) * size.x, (location.y - 0.5f) * size.y);

            DispatchAnimationSystem.I?.RegisterNode(n.Id, rt);
        }

        foreach (var nodeId in toRemove)
            _nodeButtons.Remove(nodeId);
    }

    public void RefreshMapNodes()
    {
        Build();
    }

    private void Refresh()
    {
        RefreshMapNodes();
    }

    private static Vector2 ResolveNodeLocation(NodeState node)
    {
        if (node?.Location != null && node.Location.Length >= 2)
            return new Vector2(node.Location[0], node.Location[1]);

        if (node != null && node.Type == 0 && Mathf.Abs(node.X) < 0.0001f && Mathf.Abs(node.Y) < 0.0001f)
            return new Vector2(0.5f, 0.5f);

        return node != null ? new Vector2(node.X, node.Y) : new Vector2(0.5f, 0.5f);
    }
}
