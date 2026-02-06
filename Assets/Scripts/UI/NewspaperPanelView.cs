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
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;

        private bool _wired;
        // IMPORTANT: This must match the default tab (Paper1 = FORMAL per NewsConstants.AllMediaProfiles[0])
        private string _currentMediaProfileId = Core.NewsConstants.MediaProfileFormal; // Track current media selection

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
            
            Debug.Log($"[NewsUI] day={state.Day} media={mediaProfileId} count={pool.Count} first={pool.FirstOrDefault()?.Id ?? "none"}");

            var titleTmp = FindTMP("Window/Header/TitleTMP");
            if (titleTmp != null) titleTmp.text = "基金会晨报";

            var dayTmp = FindTMP("Window/Header/DayTMP");
            if (dayTmp != null) dayTmp.text = $"Day{state.Day}";

            var global = FindByKeyword(pool, "GLOBAL");
            var node = FindByKeyword(pool, "NODE");
            var rumor = FindByKeyword(pool, "RUMOR");

            var coreSet = new List<Core.NewsInstance> { global, node, rumor };
            var extra = new List<Core.NewsInstance>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null) continue;
                if (coreSet.Contains(pool[i])) continue;
                extra.Add(pool[i]);
            }

            ApplyPage("Window/PaperPages/Paper1", global, coreSet, extra, data);
            ApplyPage("Window/PaperPages/Paper2", node, coreSet, extra, data);
            ApplyPage("Window/PaperPages/Paper3", rumor, coreSet, extra, data);
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
