using Core;
using UnityEngine;

public class MapNodeSpawner : MonoBehaviour
{
    public static MapNodeSpawner I { get; private set; }

    [SerializeField] private RectTransform mapRect;      // MapImage 的 RectTransform
    [SerializeField] private RectTransform nodeLayer;    // NodeLayer
    [SerializeField] private NodeButton nodePrefab;
    [SerializeField] private GameObject mapRoot;         // MapRoot GameObject reference

    [Header("New Map System")]
    [Tooltip("When true, disables old map generation and uses NewMapRuntime instead")]
    public bool UseNewMap = true;

    private void Awake()
    {
        I = this;
    }

    private void Start()
    {
        if (UseNewMap)
        {
            // Disable old map system
            if (mapRoot != null)
            {
                mapRoot.SetActive(false);
                Debug.Log("[MapUI] Old MapRoot disabled (UseNewMap=true)");
            }
            Debug.Log("[MapUI] Old map generation skipped (UseNewMap=true)");
            return;
        }

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

    public void RefreshMapNodes()
    {
        Build();
    }

    private void Refresh()
    {
        RefreshMapNodes();
    }
}
