using TMPro;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Component for individual news item in scrollable newspaper list.
    /// Displays title (bold/larger) and body (multi-line).
    /// </summary>
    public class NewsItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;

        public void SetContent(string title, string body)
        {
            if (titleText != null)
            {
                titleText.text = string.IsNullOrEmpty(title) ? "无标题" : title;
            }

            if (bodyText != null)
            {
                bodyText.text = string.IsNullOrEmpty(body) ? "暂无内容" : body;
            }
        }

        public void Clear()
        {
            if (titleText != null) titleText.text = "";
            if (bodyText != null) bodyText.text = "";
        }
    }
}
