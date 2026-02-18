using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]

public sealed class MapEntityRegistry : MonoBehaviour
{
    public static MapEntityRegistry I { get; private set; }

    private readonly Dictionary<string, City> _cityById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Anomaly> _anomByKey = new(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        if (I != null && I != this)
        {
            Debug.LogWarning("[MapEntityRegistry] Duplicate instance, destroying.");
            Destroy(gameObject);
            return;
        }
        I = this;
    }

    // ---------- City ----------
    public void RegisterCity(City view)
    {
        if (view == null) return;

        var key = NormalizeKey(view.CityId);
        if (string.IsNullOrEmpty(key)) return;
        _cityById[key] = view;

        Debug.Log($"[MapEntityRegistry] RegisterCity: key={key} RegisterCityname: key={view.CityName} totalCities={_cityById.Count}");
    }

    public void UnregisterCity(City view)
    {
        if (view == null) return;
        var key = NormalizeKey(view.CityId);
        if (string.IsNullOrEmpty(key)) return;
        if (_cityById.TryGetValue(key, out var cur) && cur == view) _cityById.Remove(key);

        Debug.Log($"[MapEntityRegistry] UnregisterCity: key={key} totalCities={_cityById.Count}");
    }

    public bool TryGetCityView(string cityId, out City view)
    {
        view = null;
        var key = NormalizeKey(cityId);
        if (string.IsNullOrEmpty(key)) return false;
        return _cityById.TryGetValue(key, out view) && view != null;
    }

    public bool TryGetCityWorldPos(string cityId, out Vector3 pos)
    {
        pos = default;
        if (TryGetCityView(cityId, out var view))
        {
            pos = view.transform.position;
            return true;
        }
        return false;
    }

    // ---------- Anomaly ----------
    public void RegisterAnomaly(string key, Anomaly view)
    {
        var k = NormalizeKey(key);
        if (string.IsNullOrEmpty(k) || view == null) return;
        _anomByKey[k] = view;

        Debug.Log($"[MapEntityRegistry] RegisterAnomaly: key={k} totalAnomalies={_anomByKey.Count}");
    }

    public void UnregisterAnomaly(string key, Anomaly view)
    {
        var k = NormalizeKey(key);
        if (string.IsNullOrEmpty(k) || view == null) return;
        if (_anomByKey.TryGetValue(k, out var cur) && cur == view) _anomByKey.Remove(k);

        Debug.Log($"[MapEntityRegistry] UnregisterAnomaly: key={k} totalAnomalies={_anomByKey.Count}");
    }


    public bool TryGetAnomalyView(string key, out Anomaly view)
    {
        view = null;
        var k = NormalizeKey(key);
        if (string.IsNullOrEmpty(k)) return false;
        return _anomByKey.TryGetValue(k, out view) && view != null;
    }

    public bool TryGetAnomalyWorldPos(string key, out Vector3 pos)
    {
        pos = default;
        var k = NormalizeKey(key);
        if (string.IsNullOrEmpty(k)) return false;
        if (_anomByKey.TryGetValue(k, out var view) && view != null)
        {
            pos = view.transform.position;
            return true;
        }
        return false;
    }

    private static string NormalizeKey(object id)
    {
        if (id == null) return null;
        // ºÊ»› CityId ø…ƒ‹ « int / string
        return id.ToString();
    }
}
