using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NodeButton : MonoBehaviour
{
    public string NodeId;
    [SerializeField] private TMP_Text label;
    private Button _btn;

    private void Awake()
    {
        _btn = GetComponent<Button>();
        _btn.onClick.AddListener(() => UIPanelRoot.I.OpenNode(NodeId));
    }

    public void Set(string nodeId, string text)
    {
        NodeId = nodeId;
        if (label != null) label.text = text;
    }
}
