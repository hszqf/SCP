using System.Collections.Generic;
using System.Linq;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class NewspaperPanelView : MonoBehaviour, IModalClosable
    {
        [Header("UI References")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;
        
        [Header("Scrollable List (New)")]
        [SerializeField] private GameObject newsItemPrefab;
        [SerializeField] private Transform newsContentRoot; // ScrollView content
        [SerializeField] private Button showMoreButton;
        [SerializeField] private TMP_Text showMoreButtonText;
        
        [Header("Legacy Slots (Fallback)")]
        [SerializeField] private bool useLegacySlots = true; // Toggle in inspector
        
        private bool _wired;
        // IMPORTANT: This must match the default tab (Paper1 = FORMAL per NewsConstants.AllMediaProfiles[0])
        private string _currentMediaProfileId = Core.NewsConstants.MediaProfileFormal; // Track current media selection
        
        private bool _isExpanded = false;
        private const int DefaultDisplayCount = 3;
        private const int MaxRenderCount = 30;
        
        private List<Core.NewsInstance> _currentNewsList = new List<Core.NewsInstance>();

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void CloseFromRoot()
        {
            Hide();
        }

        public void Render()
        {
            Render(_currentMediaProfileId); // Use current media selection
        }
        
        public void Render(string mediaProfileId)
        {
            _currentMediaProfileId = mediaProfileId; // Update current selection
            
            var state = GameController.I?.State;
            var data = DataRegistry.Instance;
            if (state == null || data == null) return;

            Core.NewsGenerator.EnsureBootstrapNews(state, data);
            
            // Filter news by current day AND media profile
            var pool = state.NewsLog?
                .Where(n => n != null && n.Day == state.Day && n.mediaProfileId == mediaProfileId)
                .ToList() ?? new List<Core.NewsInstance>();
            
            // Sort by newest first: day desc, severity desc
            _currentNewsList = pool
                .OrderByDescending(n => n.Day)
                .ThenByDescending(n => GetSeverity(n, data))
                .ToList();
            
            // Determine display mode
            string mode = _isExpanded ? "Expanded" : "Collapsed";
            int showCount = _isExpanded ? Mathf.Min(_currentNewsList.Count, MaxRenderCount) : Mathf.Min(_currentNewsList.Count, DefaultDisplayCount);
            
            // Log once per refresh (not spammy)
            Debug.Log($"[NewsUI] day={state.Day} media={mediaProfileId} total={_currentNewsList.Count} show={showCount} mode={mode}");

            var titleTmp = FindTMP("Window/Header/TitleTMP");
            if (titleTmp != null) titleTmp.text = "基金会晨报";

            var dayTmp = FindTMP("Window/Header/DayTMP");
            if (dayTmp != null) dayTmp.text = $"Day{state.Day}";

            // Choose rendering mode
            if (!useLegacySlots && newsContentRoot != null && newsItemPrefab != null)
            {
                RenderScrollableList(showCount, data);
            }
            else
            {
                RenderLegacySlots(data);
            }
        }

        private void RenderScrollableList(int showCount, DataRegistry data)
        {
            // Clear existing items
            if (newsContentRoot != null)
            {
                foreach (Transform child in newsContentRoot)
                {
                    Destroy(child.gameObject);
                }
            }
            
            // Handle empty state
            if (_currentNewsList.Count == 0)
            {
                // Create a single placeholder item
                var placeholderItem = Instantiate(newsItemPrefab, newsContentRoot);
                var itemView = placeholderItem.GetComponent<NewsItemView>();
                if (itemView != null)
                {
                    itemView.SetContent("暂无报道", "今日无新闻事件");
                }
                
                // Hide show-more button when no news
                if (showMoreButton != null)
                {
                    showMoreButton.gameObject.SetActive(false);
                }
                return;
            }
            
            // Render news items
            for (int i = 0; i < showCount; i++)
            {
                var news = _currentNewsList[i];
                var newsItem = Instantiate(newsItemPrefab, newsContentRoot);
                var itemView = newsItem.GetComponent<NewsItemView>();
                
                if (itemView != null)
                {
                    string title = GetTitle(news, data);
                    string body = GetDesc(news, data);
                    itemView.SetContent(title, body);
                }
            }
            
            // Update show-more button
            UpdateShowMoreButton();
        }
        
        private void RenderLegacySlots(DataRegistry data)
        {
            // Legacy rendering using 3 fixed slots per page
            // Each page corresponds to a media type, but we filter by current media
            // So we render the current media's news into all visible slots
            
            // Take top 3 items from sorted list
            var items = _currentNewsList.Take(DefaultDisplayCount).ToList();
            
            // Pad with nulls if less than 3
            while (items.Count < 3) items.Add(null);
            
            // Find the current active page based on media profile
            string pagePath = GetPagePathForMedia(_currentMediaProfileId);
            
            if (string.IsNullOrEmpty(pagePath))
            {
                Debug.LogWarning($"[NewsUI] No page path for media {_currentMediaProfileId}");
                return;
            }
            
            // Apply to slots: Headline, BlockA, BlockB
            SetText(FindTMP($"{pagePath}/Slot_Headline/HeadlineTitleTMP"), GetTitle(items[0], data));
            SetText(FindTMP($"{pagePath}/Slot_Headline/HeadlineDeckTMP"), GetDesc(items[0], data));
            SetText(FindTMP($"{pagePath}/Slot_BlockA/BlockATitleTMP"), GetTitle(items[1], data));
            SetText(FindTMP($"{pagePath}/Slot_BlockA/BlockABodyTMP"), GetDesc(items[1], data));
            SetText(FindTMP($"{pagePath}/Slot_BlockB/BlockBTitleTMP"), GetTitle(items[2], data));
            SetText(FindTMP($"{pagePath}/Slot_BlockB/BlockBBodyTMP"), GetDesc(items[2], data));
        }
        
        private string GetPagePathForMedia(string mediaProfileId)
        {
            // Map media profile to page index (must match NewspaperPanelSwitcher tab order)
            for (int i = 0; i < Core.NewsConstants.AllMediaProfiles.Length; i++)
            {
                if (Core.NewsConstants.AllMediaProfiles[i] == mediaProfileId)
                {
                    return $"Window/PaperPages/Paper{i + 1}";
                }
            }
            
            return "Window/PaperPages/Paper1"; // Default to Paper1 (FORMAL)
        }
        
        private void UpdateShowMoreButton()
        {
            if (showMoreButton == null)
            {
                return;
            }
            
            // Show button only if there are more than DefaultDisplayCount items
            bool hasMore = _currentNewsList.Count > DefaultDisplayCount;
            showMoreButton.gameObject.SetActive(hasMore);
            
            if (!hasMore)
            {
                return;
            }
            
            // Update button text
            if (showMoreButtonText != null)
            {
                if (_isExpanded)
                {
                    showMoreButtonText.text = "收起";
                }
                else
                {
                    int remainingCount = _currentNewsList.Count - DefaultDisplayCount;
                    showMoreButtonText.text = $"显示全部 ({remainingCount}条更多)";
                }
            }
            
            // Show overflow warning if needed
            if (_isExpanded && _currentNewsList.Count > MaxRenderCount)
            {
                // Could add a warning text here if desired
                Debug.Log($"[NewsUI] Showing {MaxRenderCount} of {_currentNewsList.Count} news items");
            }
        }
        
        public void ToggleShowMore()
        {
            _isExpanded = !_isExpanded;
            
            var state = GameController.I?.State;
            if (state != null)
            {
                string mode = _isExpanded ? "Expanded" : "Collapsed";
                int showCount = _isExpanded ? Mathf.Min(_currentNewsList.Count, MaxRenderCount) : DefaultDisplayCount;
                Debug.Log($"[NewsUI] day={state.Day} media={_currentMediaProfileId} total={_currentNewsList.Count} show={showCount} mode={mode}");
            }
            
            Render(_currentMediaProfileId);
        }
        
        private int GetSeverity(Core.NewsInstance news, DataRegistry data)
        {
            // Try to get severity from the news source (anomaly level, etc.)
            // For now, return a default value; can be enhanced later
            if (news == null) return 0;
            
            // Could look up severity from anomaly or event definition
            // For now, all news have equal priority
            return 0;
        }

        private void Awake()
        {
            WireButtons();
        }

        private void OnEnable()
        {
            WireButtons();
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged += OnGameStateChanged;
            }
            // Reset expanded state when panel is shown
            _isExpanded = false;
            // Render when panel is shown
            Render();
        }

        private void OnDisable()
        {
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged -= OnGameStateChanged;
            }
        }

        private void OnGameStateChanged()
        {
            // Refresh the newspaper when game state changes (e.g., day advances)
            if (gameObject.activeSelf)
            {
                Render();
            }
        }

        private void WireButtons()
        {
            if (_wired)
            {
                return;
            }

            if (closeButton == null)
            {
                Debug.LogWarning("[Newspaper] closeButton missing (assign in prefab)");
            }
            else
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() => UIPanelRoot.I?.CloseModal(gameObject, "close btn"));
                Debug.Log("[Newspaper] bind close ok");
            }

            if (dimmerButton != null)
            {
                dimmerButton.onClick.RemoveAllListeners();
                dimmerButton.onClick.AddListener(() => UIPanelRoot.I?.CloseTopModal("dimmer"));
            }
            
            if (showMoreButton != null)
            {
                showMoreButton.onClick.RemoveAllListeners();
                showMoreButton.onClick.AddListener(ToggleShowMore);
            }

            _wired = true;
        }

        private void ApplyPage(string pagePath, Core.NewsInstance preferredHeadline, List<Core.NewsInstance> coreSet, List<Core.NewsInstance> extra, DataRegistry data)
        {
            var used = new HashSet<Core.NewsInstance>();

            var headline = preferredHeadline ?? PickNext(coreSet, used) ?? PickNext(extra, used);
            if (headline != null) used.Add(headline);

            var blockA = PickNext(coreSet, used) ?? PickNext(extra, used);
            if (blockA != null) used.Add(blockA);

            var blockB = PickNext(coreSet, used) ?? PickNext(extra, used);
            if (blockB != null) used.Add(blockB);

            SetText(FindTMP($"{pagePath}/Slot_Headline/HeadlineTitleTMP"), GetTitle(headline, data));
            SetText(FindTMP($"{pagePath}/Slot_Headline/HeadlineDeckTMP"), GetDesc(headline, data));
            SetText(FindTMP($"{pagePath}/Slot_BlockA/BlockATitleTMP"), GetTitle(blockA, data));
            SetText(FindTMP($"{pagePath}/Slot_BlockA/BlockABodyTMP"), GetDesc(blockA, data));
            SetText(FindTMP($"{pagePath}/Slot_BlockB/BlockBTitleTMP"), GetTitle(blockB, data));
            SetText(FindTMP($"{pagePath}/Slot_BlockB/BlockBBodyTMP"), GetDesc(blockB, data));
        }

        private TMP_Text FindTMP(string path)
        {
            var target = transform.Find(path);
            return target ? target.GetComponent<TMP_Text>() : null;
        }

        private void SetText(TMP_Text tmp, string text)
        {
            if (tmp != null) tmp.text = string.IsNullOrEmpty(text) ? "暂无" : text;
        }

        private Core.NewsInstance FindByKeyword(List<Core.NewsInstance> pool, string keyword)
        {
            if (pool == null || string.IsNullOrEmpty(keyword)) return null;
            for (int i = 0; i < pool.Count; i++)
            {
                var id = pool[i]?.NewsDefId;
                if (string.IsNullOrEmpty(id)) continue;
                if (id.ToUpperInvariant().Contains(keyword)) return pool[i];
            }

            return null;
        }

        private Core.NewsInstance PickNext(List<Core.NewsInstance> pool, HashSet<Core.NewsInstance> used)
        {
            if (pool == null) return null;
            for (int i = 0; i < pool.Count; i++)
            {
                var item = pool[i];
                if (item == null) continue;
                if (used.Contains(item)) continue;
                return item;
            }

            return null;
        }

        private string GetTitle(Core.NewsInstance news, DataRegistry data)
        {
            if (news == null) return null;
            
            // For fact-based news, use the generated Title directly
            if (!string.IsNullOrEmpty(news.Title))
                return news.Title;
            
            // For legacy news, fallback to NewsDef lookup
            var def = data.GetNewsDefById(news.NewsDefId);
            if (def == null)
            {
                Debug.LogWarning($"[Newspaper] def missing id={news.NewsDefId}");
                return news.NewsDefId;
            }

            return def.title;
        }

        private string GetDesc(Core.NewsInstance news, DataRegistry data)
        {
            if (news == null) return null;
            
            // For fact-based news, use the generated Description directly
            if (!string.IsNullOrEmpty(news.Description))
                return news.Description;
            
            // For legacy news, fallback to NewsDef lookup
            var def = data.GetNewsDefById(news.NewsDefId);
            if (def == null)
            {
                Debug.LogWarning($"[Newspaper] def missing id={news.NewsDefId}");
                return null;
            }

            return def.desc;
        }
    }
}
