using System.Collections.Generic;
using Data;
using UnityEngine;

public class AnomalySpriteLibrary : MonoBehaviour
{
    public static AnomalySpriteLibrary I { get; private set; }

    [Header("Sprites")]
    [SerializeField] private Sprite unknownAnomalySprite;
    [SerializeField] private List<Sprite> anomalySprites = new();
    [SerializeField] private string anomalySpritesResourcePath = "Anomalies";
    [SerializeField] private bool loadAnomalySpritesFromResources = true;

    private readonly Dictionary<string, Sprite> _anomalySpriteLookup = new(System.StringComparer.OrdinalIgnoreCase);
    private bool _spritesCached;

    public Sprite UnknownSprite => unknownAnomalySprite;

    private void Awake()
    {
        I = this;
        EnsureSpriteLookup();
    }

    private void OnDestroy()
    {
        if (I == this)
            I = null;
    }

    public Sprite ResolveAnomalySprite(string anomalyId, bool isKnown)
    {
        if (!isKnown) return unknownAnomalySprite;
        EnsureSpriteLookup();

        if (!string.IsNullOrEmpty(anomalyId) && _anomalySpriteLookup.TryGetValue(anomalyId, out var direct))
            return direct;

        var registry = DataRegistry.Instance;
        if (registry != null && !string.IsNullOrEmpty(anomalyId) && registry.AnomaliesById.TryGetValue(anomalyId, out var def))
        {
            var name = def?.name;
            if (!string.IsNullOrEmpty(name) && _anomalySpriteLookup.TryGetValue(name, out var sprite))
                return sprite;
        }

        return unknownAnomalySprite;
    }

    private void EnsureSpriteLookup()
    {
        if (_spritesCached) return;
        _anomalySpriteLookup.Clear();

        if (loadAnomalySpritesFromResources && !string.IsNullOrEmpty(anomalySpritesResourcePath))
        {
            var loaded = Resources.LoadAll<Sprite>(anomalySpritesResourcePath);
            if (loaded != null && loaded.Length > 0)
            {
                anomalySprites = new List<Sprite>(loaded);
            }
        }

        if (anomalySprites != null)
        {
            foreach (var sprite in anomalySprites)
            {
                if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
                if (!_anomalySpriteLookup.ContainsKey(sprite.name))
                    _anomalySpriteLookup[sprite.name] = sprite;
            }
        }

        _spritesCached = true;
    }
}
