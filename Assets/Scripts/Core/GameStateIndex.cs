using System;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// Non-serialized runtime index for fast lookups.
    /// Rebuild is cheap (O(n)) and should be called after mutations (or lazily via EnsureUpToDate).
    /// </summary>
    public sealed class GameStateIndex
    {
        private readonly Dictionary<string, AnomalyState> _anomByInstanceId =
            new Dictionary<string, AnomalyState>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, CityState> _cityById =
            new Dictionary<string, CityState>(StringComparer.OrdinalIgnoreCase);

        private int _anomCount = -1;
        private int _cityCount = -1;

        public void Clear()
        {
            _anomByInstanceId.Clear();
            _cityById.Clear();
            _anomCount = -1;
            _cityCount = -1;
        }

        public void Rebuild(GameState s)
        {
            _anomByInstanceId.Clear();
            _cityById.Clear();

            if (s?.Anomalies != null)
            {
                for (int i = 0; i < s.Anomalies.Count; i++)
                {
                    var a = s.Anomalies[i];
                    if (a == null) continue;
                    if (string.IsNullOrEmpty(a.Id)) continue;

                    // last write wins (should not happen if ids are unique)
                    _anomByInstanceId[a.Id] = a;
                }
                _anomCount = s.Anomalies.Count;
            }
            else
            {
                _anomCount = 0;
            }

            if (s?.Cities != null)
            {
                for (int i = 0; i < s.Cities.Count; i++)
                {
                    var c = s.Cities[i];
                    if (c == null) continue;
                    if (string.IsNullOrEmpty(c.Id)) continue;

                    _cityById[c.Id] = c;
                }
                _cityCount = s.Cities.Count;
            }
            else
            {
                _cityCount = 0;
            }
        }

        public void EnsureUpToDate(GameState s)
        {
            if (_anomCount < 0 || _cityCount < 0) Rebuild(s);
        }

        public AnomalyState GetAnomaly(string anomalyInstanceId)
        {
            if (string.IsNullOrEmpty(anomalyInstanceId)) return null;
            _anomByInstanceId.TryGetValue(anomalyInstanceId, out var a);
            return a;
        }

        public CityState GetCity(string cityId)
        {
            if (string.IsNullOrEmpty(cityId)) return null;
            _cityById.TryGetValue(cityId, out var c);
            return c;
        }
    }
}
