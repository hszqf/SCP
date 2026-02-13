using System.Collections.Generic;
using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnomalyMarker : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Button button;
    [SerializeField] private Image progressBar;
    [SerializeField] private Image progressBackground;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Transform nameRoot;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform agentGridRoot;
    private float _progressWidth;
    private bool _progressWidthCached;

    private static Sprite[] _cachedAvatars;
    private const string AvatarResourcePath = "Avatar";
    private const string AvatarSpriteSheetName = "Avatar";

    private string _nodeId;
    private string _anomalyId;
    private string _managedAnomalyId;
    private bool _isKnown;
    private bool _isContained;

    public void Bind(string nodeId, string anomalyId, string managedAnomalyId, Sprite sprite, bool isKnown, bool displayKnown, bool isContained, float progress01, string progressPrefix, string nameSuffix, bool hideNameWhileProgress)
    {
        _nodeId = nodeId;
        _anomalyId = anomalyId;
        _managedAnomalyId = managedAnomalyId;
        _isKnown = isKnown;
        _isContained = isContained;

        if (!icon)
            icon = GetComponentInChildren<Image>(true);
        if (icon)
        {
            icon.raycastTarget = true;
            icon.sprite = sprite;
        }

        if (!button)
            button = GetComponent<Button>() ?? GetComponentInChildren<Button>(true) ?? gameObject.AddComponent<Button>();
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

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

        if (nameText)
        {
            nameText.text = displayKnown ? ResolveAnomalyName(anomalyId) + (nameSuffix ?? string.Empty) : string.Empty;
        }

        if (nameRoot)
            nameRoot.gameObject.SetActive(displayKnown && !(hideNameWhileProgress && progress01 > 0f));

        RefreshAgentAvatars();
    }

    private void HandleClick()
    {
        if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsInteractionLocked)
            return;
        var root = UIPanelRoot.I;
        if (root == null || string.IsNullOrEmpty(_nodeId)) return;

        if (_isContained)
        {
            root.OpenManage(_nodeId, _managedAnomalyId);
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
        if (gc?.State?.Nodes == null)
        {
            agentGridRoot.gameObject.SetActive(false);
            return;
        }

        var node = gc.State.Nodes.Find(n => n != null && n.Id == _nodeId);
        if (node == null)
        {
            agentGridRoot.gameObject.SetActive(false);
            return;
        }

        var agentIds = CollectArrivedAgentIds(node);
        if (agentIds.Count == 0)
        {
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

    private List<string> CollectArrivedAgentIds(Core.NodeState node)
    {
        var result = new List<string>();
        if (node?.Tasks == null) return result;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != Core.TaskState.Active) continue;
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

    private bool IsTaskForThisAnomaly(Core.NodeState node, Core.NodeTask task)
    {
        if (task.Type == Core.TaskType.Manage)
        {
            if (!string.IsNullOrEmpty(_managedAnomalyId))
                return string.Equals(task.TargetManagedAnomalyId, _managedAnomalyId, System.StringComparison.OrdinalIgnoreCase);

            var managed = node.ManagedAnomalies?.Find(m => m != null && m.Id == task.TargetManagedAnomalyId);
            return managed != null && string.Equals(managed.AnomalyId, _anomalyId, System.StringComparison.OrdinalIgnoreCase);
        }

        var taskAnomalyId = ResolveTaskAnomalyId(node, task);
        return !string.IsNullOrEmpty(taskAnomalyId) && string.Equals(taskAnomalyId, _anomalyId, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTaskAnomalyId(Core.NodeState node, Core.NodeTask task)
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

    private static Sprite ResolveAvatarSprite(Core.AgentState agent, string agentId, string displayName)
    {
        Sprite sprite = null;
        if (!string.IsNullOrEmpty(agentId))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{agentId}");
        if (sprite == null && !string.IsNullOrEmpty(displayName))
            sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{displayName}");
        if (sprite != null) return sprite;

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
}
