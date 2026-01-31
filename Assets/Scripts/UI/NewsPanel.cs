using System.Collections.Generic;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NewsPanel : MonoBehaviour, IModalClosable
{
    [Header("Refs")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backgroundButton; // 蒙版按钮
    [SerializeField] private Transform contentRoot;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private GameObject linePrefab;

    void Awake()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close btn"));
        }
        if (backgroundButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(() => UIPanelRoot.I?.CloseTopModal("dimmer"));
        }

        if (scrollRect == null) scrollRect = GetComponentInChildren<ScrollRect>(true);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void CloseFromRoot()
    {
        Hide();
    }

    void Refresh()
    {
        // 清理
        foreach (Transform t in contentRoot) Destroy(t.gameObject);

        // 获取数据
        var logs = GameController.I.State.News;
        // 倒序显示（最新的在最上面）
        for (int i = logs.Count - 1; i >= 0; i--)
        {
            AddLine(logs[i]);
        }
    }

    void AddLine(string text)
    {
        if (!linePrefab || !contentRoot) return;
        var go = Instantiate(linePrefab, contentRoot);
        go.SetActive(true);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp) tmp.text = text;
    }
}