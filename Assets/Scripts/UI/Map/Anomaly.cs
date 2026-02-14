using System.Collections.Generic;
using Core;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Anomaly : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private Button button;
    [SerializeField] private Image progressBar;
    [SerializeField] private Image progressBackground;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Transform nameRoot;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform agentGridRoot;
    [SerializeField] private Image rangeIndicator;
    [SerializeField] private float rangeIndicatorAlpha = 0.25f;

    private float _progressWidth;
    private bool _progressWidthCached;

    private static Sprite[] _cachedAvatars;
    private const string AvatarResourcePath = "Avatar";
    private const string AvatarSpriteSheetName = "Avatar";

    private static Sprite _rangeSprite;

    private string _nodeId;
    private string _anomalyId;
    private string _managedAnomalyId;
    private bool _isKnown;
    private bool _isContained;

    public void Bind(string nodeId, string anomalyId, string managedAnomalyId)
    {
        _nodeId = nodeId;
        _anomalyId = anomalyId;
        _managedAnomalyId = managedAnomalyId;

        EnsureRefs();
        Refresh();
    }

    private void OnEnable()
    {
        EnsureRefs();
        if (GameController.I != null)
            GameController.I.OnStateChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged -= Refresh;
    }

    private void EnsureRefs()
    {
        if (!icon)
            icon = GetComponentInChildren<Image>(true);
        if (!button)
            button = GetComponent<Button>() ?? GetComponentInChildren<Button>(true) ?? gameObject.AddComponent<Button>();
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
        if (icon)
            icon.raycastTarget = true;
    }

    private void Refresh()
    {
        var gc = GameController.I;
        if (gc?.State?.Cities == null || string.IsNullOrEmpty(_nodeId) || string.IsNullOrEmpty(_anomalyId))
            return;

        var node = gc.State.Cities.Find(n => n != null && n.Id == _nodeId);
        if (node == null) return;

        var managed = ResolveManagedAnomaly(node);
        _managedAnomalyId = managed?.Id ?? _managedAnomalyId;
        _isContained = managed != null;
        _isKnown = _isContained || (node.KnownAnomalyDefIds != null && node.KnownAnomalyDefIds.Contains(_anomalyId));

        bool displayKnown = _isKnown;
        float progress01 = 0f;
        string progressPrefix = string.Empty;
        string nameSuffix = string.Empty;
        bool hideNameWhileProgress = false;

        bool isManaging = IsManagingAnomaly(node, _anomalyId);

        if (_isContained)
        {
            nameSuffix = isManaging ? "(管理中)" : "(已收容)";
        }
        else if (_isKnown)
        {
            nameSuffix = "(未收容)";
        }

        if (_isKnown && TryGetRevealProgress01(node, _anomalyId, out var revealProgress))
        {
            progress01 = revealProgress;
            progressPrefix = "调查中：";
            displayKnown = revealProgress >= 1f;
        }
        else if (!_isKnown)
        {
            progress01 = GetUnknownAnomalyProgress01(node, _anomalyId);
            if (progress01 > 0f)
                progressPrefix = "调查中：";
        }
        else
        {
            progress01 = GetContainProgress01(node, _anomalyId);
            if (progress01 > 0f)
            {
                progressPrefix = "收容中：";
                hideNameWhileProgress = true;
            }
        }

        var spriteLibrary = AnomalySpriteLibrary.I;
        var sprite = spriteLibrary != null ? spriteLibrary.ResolveAnomalySprite(_anomalyId, displayKnown) : null;
        if (icon)
            icon.sprite = sprite;

        UpdateProgressBar(progress01, progressPrefix);
        UpdateName(displayKnown, nameSuffix, hideNameWhileProgress, progress01);
        UpdateRangeIndicator(_anomalyId);
        RefreshAgentAvatars();
    }

    private void UpdateProgressBar(float progress01, string progressPrefix)
    {
        if (progressBar)
        {
            CacheProgressWidth();

            var clamped = Mathf.Clamp01(progress01);
            if (progressBar.type == Image.Type.Filled)
            {
                progressBar.fillMethod = Image.FillMethod.Horizontal;
                progressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
                progressBar.fillAmount = clamped;
            }
            else if (_progressWidthCached)
            {
                progressBar.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _progressWidth * clamped);
            }

            progressBar.gameObject.SetActive(clamped > 0f);
        }

        if (progressBackground)
            progressBackground.gameObject.SetActive(progress01 > 0f);

        if (progressText)
        {
            var percentText = $"{Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f)}%";
            var prefix = progressPrefix ?? string.Empty;
            progressText.text = string.IsNullOrEmpty(prefix) ? percentText : $"{prefix}{percentText}";
            progressText.gameObject.SetActive(progress01 > 0f);
        }
    }

    private void UpdateName(bool displayKnown, string nameSuffix, bool hideNameWhileProgress, float progress01)
    {
        if (nameText)
        {
            nameText.text = displayKnown ? ResolveAnomalyName(_anomalyId) + (nameSuffix ?? string.Empty) : string.Empty;
        }

        if (nameRoot)
            nameRoot.gameObject.SetActive(displayKnown && !(hideNameWhileProgress && progress01 > 0f));
    }

    private void UpdateRangeIndicator(string anomalyId)
    {
        if (string.IsNullOrEmpty(anomalyId))
        {
            if (rangeIndicator) rangeIndicator.gameObject.SetActive(false);
            return;
        }

        var registry = DataRegistry.Instance;
        float range = 0f;
        if (registry != null && registry.AnomaliesById.TryGetValue(anomalyId, out var def) && def != null)
            range = def.range;

        if (range <= 0f)
        {
            if (rangeIndicator) rangeIndicator.gameObject.SetActive(false);
            return;
        }

        if (!rangeIndicator)
            rangeIndicator = CreateRangeIndicator();

        if (!rangeIndicator) return;

        var rt = rangeIndicator.rectTransform;
        float diameter = range * 2f;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
        rangeIndicator.color = new Color(1f, 0f, 0f, Mathf.Clamp01(rangeIndicatorAlpha));
        rangeIndicator.gameObject.SetActive(true);
        rangeIndicator.transform.SetAsFirstSibling();
    }

    private Image CreateRangeIndicator()
    {
        var go = new GameObject("RangeIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        var image = go.GetComponent<Image>();
        image.sprite = GetRangeSprite();
        image.raycastTarget = false;
        return image;
    }

    private static Sprite GetRangeSprite()
    {
        if (_rangeSprite != null) return _rangeSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = dist <= radius ? 1f : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _rangeSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _rangeSprite;
    }

    private ManagedAnomalyState ResolveManagedAnomaly(NodeState node)
    {
        if (node?.ManagedAnomalies == null || node.ManagedAnomalies.Count == 0) return null;
        if (!string.IsNullOrEmpty(_managedAnomalyId))
            return node.ManagedAnomalies.Find(m => m != null && m.Id == _managedAnomalyId);
        return node.ManagedAnomalies.Find(m => m != null && string.Equals(m.AnomalyId, _anomalyId, System.StringComparison.OrdinalIgnoreCase));
    }

    private void HandleClick()
    {
        if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsInteractionLocked)
            return;
        var root = UIPanelRoot.I;
        if (root == null || string.IsNullOrEmpty(_nodeId)) return;

        var node = GameController.I?.GetNode(_nodeId);
        var managed = ResolveManagedAnomaly(node);
        if (managed != null)
        {
            root.OpenManage(_nodeId, managed.Id);
            return;
        }

        if (_isKnown)
        {
            root.OpenContainAssignPanelForNode(_nodeId);
            return;
        }

        root.OpenInvestigateAssignPanelForNode(_nodeId, _anomalyId);
    }

    private void CacheProgressWidth()
    {
        float width = progressBar ? progressBar.rectTransform.rect.width : 0f;
        if (progressBackground)
        {
            float bgWidth = progressBackground.rectTransform.rect.width;
            if (bgWidth > 0.01f) width = bgWidth;
        }

        if (width > 0.01f && (!_progressWidthCached || Mathf.Abs(_progressWidth - width) > 0.5f))
        {
            _progressWidth = width;
            _progressWidthCached = true;
        }
    }

    private static string ResolveAnomalyName(string anomalyId)
    {
        if (string.IsNullOrEmpty(anomalyId)) return string.Empty;
        var registry = DataRegistry.Instance;
        if (registry != null && registry.AnomaliesById.TryGetValue(anomalyId, out var def) && def != null && !string.IsNullOrEmpty(def.name))
            return def.name;
        return anomalyId;
    }

    private void RefreshAgentAvatars()
    {
        if (!agentGridRoot)
            return;

        foreach (Transform child in agentGridRoot)
        {
            if (child) Destroy(child.gameObject);
        }

        var gc = GameController.I;
        if (gc?.State?.Cities == null)
        {
            Debug.Log("1");
            agentGridRoot.gameObject.SetActive(false);
            return;
        }

        var node = gc.State.Cities.Find(n => n != null && n.Id == _nodeId);
        if (node == null)
        {
            Debug.Log("2");

            agentGridRoot.gameObject.SetActive(false);
            return;
        }

        var agentIds = CollectArrivedAgentIds(node);
        if (agentIds.Count == 0)
        {
            Debug.Log("3");

            agentGridRoot.gameObject.SetActive(false);
            return;
        }

        agentGridRoot.gameObject.SetActive(true);
        foreach (var agentId in agentIds)
        {
            if (string.IsNullOrEmpty(agentId)) continue;
            var agent = gc.State.Agents.Find(a => a != null && a.Id == agentId);
            var sprite = ResolveAvatarSprite(agent, agentId, agent?.Name ?? string.Empty);
            if (sprite == null) continue;

            var image = CreateAvatarImage(agentGridRoot);
            image.name = $"Avatar_{agentId}";
            image.sprite = sprite;
            image.raycastTarget = false;
        }
    }

    private List<string> CollectArrivedAgentIds(NodeState node)
    {
        var result = new List<string>();
        if (node?.Tasks == null) return result;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;
            if (task.Progress <= 0f && task.VisualProgress <= 0f) continue;
            if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsTaskInTransit(task.Id)) continue;

            if (!IsTaskForThisAnomaly(node, task)) continue;

            foreach (var agentId in task.AssignedAgentIds)
            {
                if (string.IsNullOrEmpty(agentId)) continue;
                if (!result.Contains(agentId)) result.Add(agentId);
            }
        }

        return result;
    }

    private bool IsTaskForThisAnomaly(NodeState node, NodeTask task)
    {
        if (task.Type == TaskType.Manage)
        {
            if (!string.IsNullOrEmpty(_managedAnomalyId))
                return string.Equals(task.TargetManagedAnomalyId, _managedAnomalyId, System.StringComparison.OrdinalIgnoreCase);

            var managed = node.ManagedAnomalies?.Find(m => m != null && m.Id == task.TargetManagedAnomalyId);
            return managed != null && string.Equals(managed.AnomalyId, _anomalyId, System.StringComparison.OrdinalIgnoreCase);
        }

        var taskAnomalyId = ResolveTaskAnomalyId(node, task);
        return !string.IsNullOrEmpty(taskAnomalyId) && string.Equals(taskAnomalyId, _anomalyId, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTaskAnomalyId(NodeState node, NodeTask task)
    {
        if (task == null || node == null) return null;
        if (!string.IsNullOrEmpty(task.SourceAnomalyId)) return task.SourceAnomalyId;
        if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0)
            return node.ActiveAnomalyIds[0];
        return null;
    }

    private static Image CreateAvatarImage(Transform parent)
    {
        var go = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
        var image = go.GetComponent<Image>();
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return image;
    }

    private static Sprite ResolveAvatarSprite(AgentState agent, string agentId, string displayName)
    {
        Sprite sprite = null;
        if (!string.IsNullOrEmpty(agentId))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{agentId}");
        if (sprite == null && !string.IsNullOrEmpty(displayName))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{displayName}");

        var pool = GetAvatarPool();
        if (pool.Length == 0) return null;

        int seed = agent != null ? agent.AvatarSeed : -1;
        if (seed < 0)
        {
            var key = !string.IsNullOrEmpty(agentId) ? agentId : displayName;
            seed = string.IsNullOrEmpty(key) ? 0 : key.GetHashCode();
            if (agent != null) agent.AvatarSeed = seed;
        }

        int index = Mathf.Abs(seed) % pool.Length;
        return pool[index];
    }

    private static Sprite[] GetAvatarPool()
    {
        if (_cachedAvatars != null) return _cachedAvatars;
        _cachedAvatars = Resources.LoadAll<Sprite>(AvatarResourcePath) ?? System.Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarSpriteSheetName}") ?? System.Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
            _cachedAvatars = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarResourcePath}") ?? System.Array.Empty<Sprite>();
        if (_cachedAvatars.Length == 0)
        {
            var single = Resources.Load<Sprite>(AvatarResourcePath);
            _cachedAvatars = single != null ? new[] { single } : System.Array.Empty<Sprite>();
        }
        return _cachedAvatars;
    }

    private static float GetUnknownAnomalyProgress01(NodeState node, string anomalyId)
    {
        if (node == null) return 0f;
        if (string.IsNullOrEmpty(anomalyId)) return 0f;
        if (node.Tasks == null) return 0f;

        float best = 0f;
        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Investigate) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;

            float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
            if (progress <= 0f) continue;

            int baseDays = GetTaskBaseDays(task);
            float progress01 = baseDays > 0 ? Mathf.Clamp01(progress / baseDays) : 0f;
            if (progress01 > best) best = progress01;
        }

        return best;
    }

    private static bool TryGetRevealProgress01(NodeState node, string anomalyId, out float progress01)
    {
        progress01 = 0f;
        if (node == null || string.IsNullOrEmpty(anomalyId)) return false;
        if (node.Tasks == null) return false;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.Type != TaskType.Investigate) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (task.VisualProgress < 0f) continue;

            int baseDays = GetTaskBaseDays(task);
            progress01 = baseDays > 0 ? Mathf.Clamp01(task.VisualProgress / baseDays) : 0f;
            return true;
        }

        return false;
    }

    private static bool IsManagingAnomaly(NodeState node, string anomalyId)
    {
        if (node == null || string.IsNullOrEmpty(anomalyId)) return false;
        if (node.Tasks == null) return false;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Manage) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;

            var managed = node.ManagedAnomalies?.Find(m => m != null && m.Id == task.TargetManagedAnomalyId);
            var taskAnomalyId = managed?.AnomalyId ?? task.SourceAnomalyId;
            if (string.Equals(taskAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static float GetContainProgress01(NodeState node, string anomalyId)
    {
        if (node == null || string.IsNullOrEmpty(anomalyId)) return 0f;
        if (node.Tasks == null) return 0f;

        float best = 0f;
        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Contain) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;

            float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
            if (progress <= 0f) continue;
            int baseDays = GetTaskBaseDays(task);
            float progress01 = baseDays > 0 ? Mathf.Clamp01(progress / baseDays) : 0f;
            if (progress01 > best) best = progress01;
        }

        return best;
    }

    private static int GetTaskBaseDays(NodeTask task)
    {
        if (task == null) return 1;
        var registry = DataRegistry.Instance;
        if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            return task.InvestigateNoResultBaseDays;
        string anomalyId = task.SourceAnomalyId;
        if (string.IsNullOrEmpty(anomalyId) || registry == null) return 1;
        return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
    }
}
