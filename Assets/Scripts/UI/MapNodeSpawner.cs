using Core;
using UnityEngine;

public class MapNodeSpawner : MonoBehaviour
{
    [SerializeField] private RectTransform mapRect;      // MapImage 的 RectTransform
    [SerializeField] private RectTransform nodeLayer;    // NodeLayer
    [SerializeField] private NodeButton nodePrefab;

    private void Start()
    {
        Build();
        GameController.I.OnStateChanged += Refresh; // 需要的话
    }

    private void Build()
    {
        foreach (Transform c in nodeLayer) Destroy(c.gameObject);

        foreach (var n in GameController.I.State.Nodes)
        {
            var btn = Instantiate(nodePrefab, nodeLayer);
            btn.Set(n.Id, n.Name);

            var rt = (RectTransform)btn.transform;

            // 关键：percent -> anchoredPosition（以 mapRect 尺寸换算）
            var size = mapRect.rect.size;
            rt.anchoredPosition = new Vector2((n.X - 0.5f) * size.x, (n.Y - 0.5f) * size.y);
        }
    }

    private void Refresh()
    {
        // 以后你可以根据状态改颜色/图标，这里先不做
    }
}
