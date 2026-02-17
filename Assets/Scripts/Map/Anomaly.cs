using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private TMP_Text displayNameText;
    [SerializeField] private Transform agentGridRoot;
    [SerializeField] private Image rangeIndicator;
    [SerializeField] private float rangeIndicatorAlpha = 0.25f;

    private float _progressWidth;
    private bool _progressWidthCached;

    private static Sprite[] _cachedAvatars;
    private const string AvatarResourcePath = "Avatar";
    private const string AvatarSpriteSheetName = "Avatar";

    private static Sprite _rangeSprite;

    private string _canonicalAnomalyKey;
    private string _anomalyId;
    private string _managedAnomalyId;

    // registration key actually registered in MapEntityRegistry
    private string _regKeyCanonical;

    public void Bind(string anomalyDefId, string anomalyInstanceId)
    {
        _anomalyId = anomalyDefId;

        // 兼容现有字段：本工程已不再区分“managedId”，这里用它来存 instanceId（唯一真相）
        _managedAnomalyId = anomalyInstanceId;

        // MapEntityRegistry 的注册 key：必须是实例ID
        _canonicalAnomalyKey = anomalyInstanceId;

        EnsureRefs();
        Refresh();
        // registration moved to Refresh() only to avoid duplicate registration from Bind+Refresh
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

        var reg = MapEntityRegistry.I;
        if (reg == null) return;

        if (!string.IsNullOrEmpty(_regKeyCanonical))
        {
            reg.UnregisterAnomaly(_regKeyCanonical, this);
            _regKeyCanonical = null;
        }
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
        if (gc?.State == null || string.IsNullOrEmpty(_anomalyId))
            return;

        // instanceId is the only truth for resolving state
        if (string.IsNullOrEmpty(_canonicalAnomalyKey))
            return;

        // Resolve simulation state ONLY by instanceId
        var anom = Core.DispatchSystem.FindAnomaly(gc.State, _canonicalAnomalyKey);

        // Always register using canonical (instance) key so other systems can resolve positions
        RegisterToRegistry();

        // Determine display name reveal based on phase/progress
        bool revealName = false;
        if (anom != null)
        {
            revealName = (anom.Phase != AnomalyPhase.Investigate) || (anom.InvestigateProgress >= 0.2f);
        }

        // Display name
        if (displayNameText)
            displayNameText.text = revealName ? ResolveAnomalyName(_anomalyId) : "□□□□";

        // Sprite
        var spriteLibrary = AnomalySpriteLibrary.I;
        var sprite = spriteLibrary != null ? spriteLibrary.ResolveAnomalySprite(_anomalyId, revealName) : null;
        if (icon)
            icon.sprite = sprite;

        // Progress handling driven by phase (NO fallback scanning)
        float progress01 = 0f;
        string progressPrefix = string.Empty;
        string overrideText = null;
        bool alwaysVisible = false;
        bool showPercent = true;

        if (anom != null)
        {
            switch (anom.Phase)
            {
                case AnomalyPhase.Investigate:
                    alwaysVisible = true;
                    progress01 = Mathf.Clamp01(anom.InvestigateProgress);
                    if (progress01 <= 0f)
                        overrideText = "待调查";
                    break;

                case AnomalyPhase.Contain:
                    alwaysVisible = true;
                    progress01 = Mathf.Clamp01(anom.ContainProgress);
                    if (progress01 <= 0f)
                        overrideText = "未收容";
                    break;

                case AnomalyPhase.Operate:
                    // arrived count -> binary progress
                    var arrived = CollectArrivedAgentIds(null).Count;
                    progress01 = arrived > 0 ? 1f : 0f;
                    overrideText = arrived > 0 ? "管理中" : "待管理";
                    showPercent = false;
                    alwaysVisible = true;
                    break;

                default:
                    Debug.LogWarning("No Phase");
                    alwaysVisible = false;
                    progress01 = 0f;
                    overrideText = "未知";
                    showPercent = false;
                    break;
            }
        }
        else
        {
            // No state found for this instanceId: do NOT guess by defId.
            Debug.LogWarning("No state found for this instanceId");
            alwaysVisible = false;
            progress01 = 0f;
            overrideText = "未知";
            showPercent = false;
        }

        UpdateProgressBar(progress01, progressPrefix, alwaysVisible, overrideText, showPercent);

        UpdateRangeIndicator(_anomalyId);
        RefreshAgentAvatars();

        // Short-term validation log: single line
        int opArrivedCount = 0;
        if (anom != null)
        {
            var state = gc?.State;
            if (state?.Agents != null)
            {
                for (int i = 0; i < state.Agents.Count; i++)
                {
                    var ag = state.Agents[i];
                    if (ag == null) continue;
                    if (ag.LocationKind != AgentLocationKind.AtAnomaly) continue;
                    if (!string.Equals(ag.LocationAnomalyInstanceId, anom.Id, System.StringComparison.Ordinal)) continue;
                    if (ag.LocationSlot == AssignmentSlot.Operate)
                        opArrivedCount++;
                }
            }
        }

        Debug.Log($"[AnomUI] key={_canonicalAnomalyKey} def={_anomalyId} phase={(anom != null ? anom.Phase.ToString() : "null")} inv={(anom != null ? anom.InvestigateProgress : -1f):0.###} con={(anom != null ? anom.ContainProgress : -1f):0.###} revealName={revealName} opArr={opArrivedCount}");
    }

    private void UpdateProgressBar(float progress01, string progressPrefix, bool alwaysVisible = false, string overrideText = null, bool showPercent = true)
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

            progressBar.gameObject.SetActive(alwaysVisible || clamped > 0f);
        }

        if (progressBackground)
            progressBackground.gameObject.SetActive(alwaysVisible || progress01 > 0f);

        if (progressText)
        {
            if (!string.IsNullOrEmpty(overrideText))
            {
                progressText.text = overrideText;
                progressText.gameObject.SetActive(true);
            }
            else
            {
                var percentText = $"{Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f)}%";
                var prefix = progressPrefix ?? string.Empty;
                var text = string.IsNullOrEmpty(prefix) ? (showPercent ? percentText : string.Empty) : $"{prefix}{(showPercent ? percentText : string.Empty)}";
                progressText.text = text;
                progressText.gameObject.SetActive((showPercent && progress01 > 0f) || (!showPercent && !string.IsNullOrEmpty(text)));
            }
        }
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


    void HandleClick()
    {
        if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsInteractionLocked)
            return;

        var root = UIPanelRoot.I;
        if (root == null) return;

        var gc = GameController.I;
        var state = gc?.State;
        if (state == null) return;

        // 唯一真源：实例ID（instanceId）
        if (string.IsNullOrEmpty(_canonicalAnomalyKey))
        {
            Debug.LogError($"[AnomUI] HandleClick missing instanceId. defId={_anomalyId ?? "null"}");
            return;
        }

        var anomalyState = Core.DispatchSystem.FindAnomaly(state, _canonicalAnomalyKey);
        if (anomalyState == null)
        {
            Debug.LogWarning($"[AnomUI] HandleClick instance not found. defId={_anomalyId ?? "null"} instanceId={_canonicalAnomalyKey}");
            return;
        }

        switch (anomalyState.Phase)
        {
            case AnomalyPhase.Investigate:
                root.OpenInvestigateAssignPanelForAnomaly(anomalyState.Id);
                return;

            case AnomalyPhase.Contain:
                root.OpenContainAssignPanelForAnomaly(anomalyState.Id);
                return;

            case AnomalyPhase.Operate:
                root.OpenOperateAssignPanelForAnomaly(anomalyState.Id);
                return;

            default:
                Debug.LogWarning("AnomalyPhase error");
                return;
        }
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

        var node = gc.State.Cities.Find(n => n != null);
        if (node == null)
        {
            Debug.Log("2");

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

    private List<string> CollectArrivedAgentIds(CityState node)
    {
        var result = new List<string>();

        var gc = GameController.I;
        var state = gc?.State;
        if (state?.Agents == null) return result;
        if (string.IsNullOrEmpty(_canonicalAnomalyKey)) return result;

        for (int i = 0; i < state.Agents.Count; i++)
        {
            var ag = state.Agents[i];
            if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;
            if (ag.IsDead || ag.IsInsane) continue;

            // only show agents whose location is at this anomaly
            if (ag.LocationKind != AgentLocationKind.AtAnomaly) continue;
            if (!string.Equals(ag.LocationAnomalyInstanceId, _canonicalAnomalyKey, System.StringComparison.Ordinal)) continue;

            result.Add(ag.Id);
        }

        return result;
    }




    private static string ResolveTaskAnomalyId(CityState node, NodeTask task)
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

    private static float GetInvestigateProgress01(GameState state, CityState node, string anomalyId)
    {
        if (state == null || string.IsNullOrEmpty(anomalyId)) return 0f;
        var anomalyState = FindAnomalyState(state, anomalyId);
        if (anomalyState == null) return 0f;
        int baseDays = GetAnomalyBaseDays(anomalyId);
        return baseDays > 0 ? Mathf.Clamp01(anomalyState.InvestigateProgress) : 0f;
    }

    // Prefer AnomalyState normalized 0..1 progress; fallback to legacy task/baseDays when missing
    private float GetInvestigateProgress01_FromAnomalyState(GameState s)
    {
        if (s == null) return 0f;

        // prefer managed id -> def id (same as canonicalKey logic)
        var preferKey = !string.IsNullOrEmpty(_managedAnomalyId) ? _managedAnomalyId : _anomalyId;
        var a = Core.DispatchSystem.FindAnomaly(s, preferKey);
        if (a == null) return 0f;
        Debug.Log($"[UIProgSrc] key={preferKey} found={a != null} anomId={a?.Id ?? "null"} inv01={(a?.InvestigateProgress ?? -1f):0.###}");

        return Mathf.Clamp01(a.InvestigateProgress);
    }

    // Prefer AnomalyState normalized 0..1 progress; fallback to legacy task/baseDays when missing


    private static int GetAnomalyBaseDays(string anomalyId)
    {
        if (string.IsNullOrEmpty(anomalyId)) return 1;
        var registry = DataRegistry.Instance;
        if (registry == null) return 1;
        return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
    }

    private static AnomalyState FindAnomalyState(GameState state, string anomalyId)
    {
        if (state?.Anomalies == null || string.IsNullOrEmpty(anomalyId)) return null;
        var found = state.Anomalies.FirstOrDefault(a => a != null && string.Equals(a.AnomalyDefId, anomalyId, System.StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        return null;
    }


    // register anomaly views with the MapEntityRegistry for world position lookup
    private void RegisterToRegistry()
    {
        var reg = MapEntityRegistry.I;
        if (reg == null) return;

        var newKey = _canonicalAnomalyKey;
        if (string.Equals(_regKeyCanonical, newKey, System.StringComparison.OrdinalIgnoreCase))
            return; // idempotent

        if (!string.IsNullOrEmpty(_regKeyCanonical))
            reg.UnregisterAnomaly(_regKeyCanonical, this);

        if (!string.IsNullOrEmpty(newKey))
            reg.RegisterAnomaly(newKey, this);

        _regKeyCanonical = newKey;
    }
}
