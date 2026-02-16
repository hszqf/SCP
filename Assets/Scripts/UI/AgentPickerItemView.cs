// <EXPORT_BLOCK>
using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AgentPickerItemView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    private Image background;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text attrText;
    [SerializeField] private TMP_Text busyTagText;

    // 不再依赖 SelectedMark GameObject，直接用颜色
    [SerializeField] private GameObject selectedIcon; // 可选：保留一个勾选图标
    [SerializeField] private Image avatarImage;
    private const string AvatarResourcePath = "Avatar";
    private const string AvatarSpriteSheetName = "Avatar";
    private static Sprite[] _cachedAvatars;

    [Header("Style Colors")]
    private Color colNormal = new Color(0.18f, 0.18f, 0.18f, 0.05f); // 极淡灰
    private Color colSelected = new Color(0f, 0.68f, 0.71f, 0.8f); // 战术青 (高亮)
    private Color colBusy = new Color(0.3f, 0.1f, 0.1f, 0.4f); // 暗红

    public string AgentId { get; private set; }
    private bool _isBusy;

    private static int ResolveLevel(AgentState agent, string agentId)
    {
        if (agent != null && agent.Level > 0) return agent.Level;
        if (!string.IsNullOrEmpty(agentId))
        {
            var gc = GameController.I;
            var list = gc?.State?.Agents;
            if (list != null)
            {
                foreach (var a in list)
                {
                    if (a == null) continue;
                    if (a.Id == agentId && a.Level > 0) return a.Level;
                }
            }
        }

        return 1;
    }

    private static string FormatName(AgentState agent, string displayName, string agentId)
    {
        string baseName = string.IsNullOrEmpty(displayName) ? agentId : displayName;
        int level = Mathf.Max(1, ResolveLevel(agent, agentId));
        return $"{baseName}  Lv{level}";
    }

    private static string FormatBaseName(string displayName, string agentId)
        => string.IsNullOrEmpty(displayName) ? agentId : displayName;

    private void BindCore(
        AgentState agent,
        string displayName,
        string agentId,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText)
    {
        AgentId = agentId;
        _isBusy = isBusyOtherNode;

        Debug.Log($"[AgentItemBind] agent={agentId} name={displayName} hasAvatarImage={(avatarImage != null ? 1 : 0)}");

        if (!button) button = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();

        if (nameText)
        {
            nameText.text = levelText ? FormatBaseName(displayName, agentId) : FormatName(agent, displayName, agentId);
        }
        if (levelText)
        {
            int level = Mathf.Max(1, ResolveLevel(agent, agentId));
            levelText.text = $"Lv{level}";
        }
        if (attrText) attrText.text = attrLine;

        if (busyTagText)
        {
            string statusLine = string.IsNullOrEmpty(busyText) ? (_isBusy ? "BUSY" : "") : busyText;
            bool showStatus = !string.IsNullOrEmpty(statusLine);
            busyTagText.gameObject.SetActive(showStatus);
            busyTagText.text = showStatus ? statusLine : "";
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = !_isBusy;
            button.onClick.AddListener(() => onClick?.Invoke(AgentId));
        }

        BindAvatarImage(agent, agentId, displayName);

        UpdateVisuals(selected);
    }

    public static int PreloadAvatars()
    {
        _cachedAvatars = null;
        _cachedAvatars = Resources.LoadAll<Sprite>(AvatarResourcePath) ?? Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarSpriteSheetName}") ?? Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarResourcePath}") ?? Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
        {
            var single = Resources.Load<Sprite>(AvatarResourcePath);
            _cachedAvatars = single != null ? new[] { single } : Array.Empty<Sprite>();
        }
        return _cachedAvatars.Length;
    }

    public void Bind(
        AgentState agent,
        string displayName,
        string attrLine,
        bool isBusyOtherNode,
        bool selected,
        System.Action<string> onClick,
        string busyText = null)
    {
        string agentId = agent != null ? agent.Id : string.Empty;
        BindCore(agent, displayName, agentId, attrLine, isBusyOtherNode, selected, onClick, busyText);
    }

    

    private Sprite ResolveAvatarSprite(AgentState agent, string agentId, string displayName)
    {
        Sprite sprite = null;
        if (!string.IsNullOrEmpty(agentId))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{agentId}");
        if (sprite == null && !string.IsNullOrEmpty(displayName))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{displayName}");
        if (sprite != null)
        {
            Debug.Log($"[AvatarPick] agent={agentId} name={displayName} source=direct sprite={sprite.name}");
            return sprite;
        }

        var pool = GetAvatarPool();
        if (pool.Length == 0)
        {
            Debug.LogWarning($"[AvatarPick] agent={agentId} name={displayName} source=pool empty");
            return null;
        }
        int seed = agent != null ? agent.AvatarSeed : -1;
        if (seed < 0)
        {
            var key = !string.IsNullOrEmpty(agentId) ? agentId : displayName;
            seed = string.IsNullOrEmpty(key) ? 0 : key.GetHashCode();
            if (agent != null) agent.AvatarSeed = seed;
        }

        int index = Mathf.Abs(seed) % pool.Length;
        var picked = pool[index];
        Debug.Log($"[AvatarPick] agent={agentId} name={displayName} source=pool count={pool.Length} index={index} sprite={(picked != null ? picked.name : "null")}");
        return picked;
    }

    private Sprite[] GetAvatarPool()
    {
        if (_cachedAvatars != null) return _cachedAvatars;
        _cachedAvatars = Resources.LoadAll<Sprite>(AvatarResourcePath) ?? Array.Empty<Sprite>();
        Debug.Log($"[AvatarLoad] path={AvatarResourcePath} count={_cachedAvatars.Length}");

        if (_cachedAvatars.Length == 0)
        {
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarSpriteSheetName}") ?? Array.Empty<Sprite>();
            Debug.Log($"[AvatarLoad] sheet={AvatarSpriteSheetName} count={_cachedAvatars.Length}");
        }

        if (_cachedAvatars.Length == 0)
        {
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarResourcePath}") ?? Array.Empty<Sprite>();
            Debug.Log($"[AvatarLoad] fallbackSheet={AvatarResourcePath} count={_cachedAvatars.Length}");
        }

        if (_cachedAvatars.Length == 0)
        {
            var single = Resources.Load<Sprite>(AvatarResourcePath);
            _cachedAvatars = single != null ? new[] { single } : Array.Empty<Sprite>();
            Debug.Log($"[AvatarLoad] single path={AvatarResourcePath} found={(single != null ? 1 : 0)}");
        }
        return _cachedAvatars;
    }

    public void SetSelected(bool selected)
    {
        UpdateVisuals(selected);
    }

    void UpdateVisuals(bool selected)
    {
        // 1. 背景颜色逻辑
        if (background)
        {
            if (_isBusy)
            {
                background.color = colBusy;
            }
            else if (selected)
            {
                background.color = colSelected; // 选中：亮青色
            }
            else
            {
                background.color = colNormal; // 普通：透明
            }
        }

        // 2. 勾选图标 (辅助)
        if (selectedIcon) selectedIcon.SetActive(selected);

        // 3. 选中时文字变亮/变黑以适应背景
        if (nameText) nameText.color = selected ? Color.black : Color.white;
        if (attrText) attrText.color = selected ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.8f, 0.8f, 0.8f);
    }
    private void BindAvatarImage(AgentState agent, string agentId, string displayName)
    {
        if (avatarImage)
        {
            Debug.Log($"[AvatarBind] agent={agentId} name={displayName} start");
            var sprite = ResolveAvatarSprite(agent, agentId, displayName);
            if (sprite != null)
            {
                avatarImage.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"[AvatarBind] agent={agentId} name={displayName} sprite=null");
            }
        }
    }
}
// </EXPORT_BLOCK>
