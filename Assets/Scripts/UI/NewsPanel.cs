using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NewsPanel : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform contentRoot;     // Viewport/Content
    [SerializeField] private ScrollRect scrollRect;     // Scroll View
    [SerializeField] private GameObject linePrefab;     // NewsLine.prefab

    [Header("Behavior")]
    [SerializeField] private int maxLinesToShow = 200;

    // 如果用户当前在底部附近，新增内容时自动贴底；如果用户在翻旧内容，不抢滚动
    [SerializeField] private bool autoStickToBottom = true;
    [SerializeField] private float stickThreshold = 0.02f; // 0~1，越大越容易判定在底部

    // incremental cache
    private readonly List<GameObject> _active = new List<GameObject>(256);
    private readonly Stack<GameObject> _pool = new Stack<GameObject>(256);
    private int _lastNewsCount = -1;
    private int _startIndex = 0;
    private bool _isVisible;

    void Awake()
    {
        // 容错：不拖也尽量自动找
        if (scrollRect == null) scrollRect = GetComponentInChildren<ScrollRect>(true);

        if (contentRoot == null && scrollRect != null)
            contentRoot = scrollRect.content;

        // 监听滚动：用来判断用户是否在底部
        if (scrollRect != null)
            scrollRect.onValueChanged.AddListener(_ => { /* keep current state */ });
    }

    void OnEnable()
    {
        _isVisible = true;
        // 如果外部只是 SetActive(true) 没走 Show()，也尝试刷新并贴底
        Refresh(forceFull: false);
        StartCoroutine(ScrollToBottomNextFrame());
    }

    void OnDisable()
    {
        _isVisible = false;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        Refresh(forceFull: false);
        StartCoroutine(ScrollToBottomNextFrame());
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 增量刷新：只追加新行；超过 maxLinesToShow 时回收最旧行。
    /// 当 startIndex 发生变化或结构不一致时，会自动全量重建。
    /// </summary>
    public void Refresh(bool forceFull)
    {
        if (contentRoot == null || linePrefab == null) return;
        if (GameController.I == null || GameController.I.State == null) return;

        var news = GameController.I.State.News;
        if (news == null) return;

        int total = news.Count;
        int newStart = Mathf.Max(0, total - maxLinesToShow);

        // 是否需要全量重建
        bool needFull =
            forceFull ||
            _lastNewsCount < 0 ||
            newStart != _startIndex ||
            total < _lastNewsCount ||                       // 列表被清空/回滚
            _active.Count > maxLinesToShow ||               // 防御
            _active.Count < Mathf.Min(maxLinesToShow, total - newStart); // 防御

        bool wasAtBottom = IsAtBottom();

        if (needFull)
        {
            RebuildAll(news, newStart);
        }
        else
        {
            AppendIncremental(news);
        }

        _lastNewsCount = total;
        _startIndex = newStart;

        // 只在用户原本就在底部时自动贴底
        if (autoStickToBottom && wasAtBottom)
            StartCoroutine(ScrollToBottomNextFrame());
    }

    // 兼容你之前的调用方式
    public void Refresh()
    {
        Refresh(forceFull: false);
    }

    private void RebuildAll(IList<string> news, int start)
    {
        // 回收当前 active
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i] != null) RecycleLine(_active[i]);
        }
        _active.Clear();

        int total = news.Count;
        if (total <= 0) return;

        int from = start;
        for (int i = from; i < total; i++)
        {
            var go = GetLine();
            BindLine(go, news[i]);
            _active.Add(go);
        }
    }

    private void AppendIncremental(IList<string> news)
    {
        int total = news.Count;
        // 当前应该已经显示到：_startIndex + _active.Count
        int displayedUntil = _startIndex + _active.Count;

        // 追加新增的
        for (int i = displayedUntil; i < total; i++)
        {
            // 超出上限：回收最旧的一个（头部）
            if (_active.Count >= maxLinesToShow)
            {
                var oldest = _active[0];
                _active.RemoveAt(0);
                if (oldest != null) RecycleLine(oldest);
                _startIndex++;
            }

            var go = GetLine();
            BindLine(go, news[i]);
            _active.Add(go);
        }
    }

    private GameObject GetLine()
    {
        GameObject go;
        if (_pool.Count > 0)
        {
            go = _pool.Pop();
            if (go != null) go.SetActive(true);
        }
        else
        {
            go = Instantiate(linePrefab);
        }

        go.transform.SetParent(contentRoot, false);
        return go;
    }

    private void RecycleLine(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        go.transform.SetParent(null, false);
        _pool.Push(go);
    }

    private void BindLine(GameObject go, string text)
    {
        if (go == null) return;
        var t = go.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = text ?? "";
    }

    private bool IsAtBottom()
    {
        if (scrollRect == null) return true;
        // verticalNormalizedPosition: 1=top, 0=bottom
        return scrollRect.verticalNormalizedPosition <= stickThreshold;
    }

    private IEnumerator ScrollToBottomNextFrame()
    {
        if (!_isVisible) yield break;
        if (scrollRect == null) yield break;

        // 等布局稳定（ContentSizeFitter / LayoutGroup）
        yield return null;
        yield return null;

        Canvas.ForceUpdateCanvases();

        // 固定横向，贴底
        scrollRect.horizontalNormalizedPosition = 0f;
        scrollRect.verticalNormalizedPosition = 0f;
    }
}

// <NEWS_PANEL_FULL_DUMP>
// dump marker
// </NEWS_PANEL_FULL_DUMP>
