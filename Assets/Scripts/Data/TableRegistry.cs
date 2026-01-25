using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Data
{
    public sealed class TableRegistry
    {
        private static readonly char[] ListSeparators = { ',', ';', '，' };
        private readonly Dictionary<string, GameDataTable> _tables;
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, object>>> _rowsByTable;

        public TableRegistry()
            : this(new Dictionary<string, GameDataTable>())
        {
        }

        public TableRegistry(Dictionary<string, GameDataTable> tables)
        {
            _tables = tables ?? new Dictionary<string, GameDataTable>();
            _rowsByTable = new Dictionary<string, Dictionary<string, Dictionary<string, object>>>(StringComparer.Ordinal);
            BuildIndex();
        }

        public int TableCount => _tables.Count;

        public bool TryGetRow(string tableName, string rowId, out Dictionary<string, object> row)
        {
            row = null;
            if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(rowId)) return false;
            if (!_rowsByTable.TryGetValue(tableName, out var tableIndex)) return false;
            return tableIndex.TryGetValue(rowId, out row);
        }

        public int GetInt(string tableName, string rowId, string column, int fallback = 0)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return fallback;
            return TryCoerceInt(raw, out var value) ? value : fallback;
        }

        public float GetFloat(string tableName, string rowId, string column, float fallback = 0f)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return fallback;
            return TryCoerceFloat(raw, out var value) ? value : fallback;
        }

        public string GetString(string tableName, string rowId, string column, string fallback = "")
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return fallback;
            return TryCoerceString(raw, out var value) ? value : fallback;
        }

        public bool GetBool(string tableName, string rowId, string column, bool fallback = false)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return fallback;
            return TryCoerceBool(raw, out var value) ? value : fallback;
        }

        public List<string> GetStringList(string tableName, string rowId, string column)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return new List<string>();
            return CoerceStringList(raw);
        }

        public List<int> GetIntList(string tableName, string rowId, string column)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return new List<int>();
            return CoerceIntList(raw);
        }

        public List<float> GetFloatList(string tableName, string rowId, string column)
        {
            if (!TryGetValue(tableName, rowId, column, out var raw)) return new List<float>();
            return CoerceFloatList(raw);
        }

        public bool TryFindFirstValue(string column, out string tableName, out string rowId, out object value)
        {
            tableName = null;
            rowId = null;
            value = null;
            if (string.IsNullOrEmpty(column)) return false;

            foreach (var tableEntry in _rowsByTable)
            {
                foreach (var rowEntry in tableEntry.Value)
                {
                    if (!rowEntry.Value.TryGetValue(column, out var raw)) continue;
                    tableName = tableEntry.Key;
                    rowId = rowEntry.Key;
                    value = raw;
                    return true;
                }
            }

            return false;
        }

        private void BuildIndex()
        {
            _rowsByTable.Clear();
            foreach (var entry in _tables)
            {
                var tableName = entry.Key;
                var table = entry.Value;
                var index = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
                if (table?.rows == null || string.IsNullOrEmpty(table.idField))
                {
                    _rowsByTable[tableName] = index;
                    continue;
                }

                foreach (var row in table.rows)
                {
                    if (row == null) continue;
                    if (!TryGetRowId(row, table.idField, out var rowId)) continue;
                    if (string.IsNullOrEmpty(rowId)) continue;
                    index[rowId] = row;
                }

                _rowsByTable[tableName] = index;
            }
        }

        private bool TryGetRowId(Dictionary<string, object> row, string idField, out string rowId)
        {
            rowId = null;
            if (row == null || string.IsNullOrEmpty(idField)) return false;
            if (!row.TryGetValue(idField, out var raw)) return false;
            if (!TryCoerceString(raw, out rowId)) return false;
            return !string.IsNullOrEmpty(rowId);
        }

        private bool TryGetValue(string tableName, string rowId, string column, out object value)
        {
            value = null;
            if (!TryGetRow(tableName, rowId, out var row)) return false;
            return row.TryGetValue(column, out value);
        }

        private static bool TryCoerceString(object raw, out string value)
        {
            value = null;
            if (raw == null) return false;
            switch (raw)
            {
                case string str:
                    value = str;
                    return true;
                case JValue jValue:
                    if (jValue.Value == null) return false;
                    value = Convert.ToString(jValue.Value, CultureInfo.InvariantCulture);
                    return true;
                case JToken token:
                    value = token.Type == JTokenType.Null ? null : token.ToString();
                    return value != null;
                default:
                    value = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    return value != null;
            }
        }

        private static bool TryCoerceInt(object raw, out int value)
        {
            value = 0;
            if (raw == null) return false;
            if (raw is JValue jValue)
            {
                raw = jValue.Value;
                if (raw == null) return false;
            }

            switch (raw)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = (int)longValue;
                    return true;
                case float floatValue:
                    value = (int)floatValue;
                    return true;
                case double doubleValue:
                    value = (int)doubleValue;
                    return true;
                case decimal decimalValue:
                    value = (int)decimalValue;
                    return true;
                case bool boolValue:
                    value = boolValue ? 1 : 0;
                    return true;
                case string str:
                    return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    return int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out value);
            }
        }

        private static bool TryCoerceFloat(object raw, out float value)
        {
            value = 0f;
            if (raw == null) return false;
            if (raw is JValue jValue)
            {
                raw = jValue.Value;
                if (raw == null) return false;
            }

            switch (raw)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case double doubleValue:
                    value = (float)doubleValue;
                    return true;
                case decimal decimalValue:
                    value = (float)decimalValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case string str:
                    return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    return float.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out value);
            }
        }

        private static bool TryCoerceBool(object raw, out bool value)
        {
            value = false;
            if (raw == null) return false;
            if (raw is JValue jValue)
            {
                raw = jValue.Value;
                if (raw == null) return false;
            }

            switch (raw)
            {
                case bool boolValue:
                    value = boolValue;
                    return true;
                case int intValue:
                    value = intValue != 0;
                    return true;
                case long longValue:
                    value = longValue != 0;
                    return true;
                case float floatValue:
                    value = Math.Abs(floatValue) > 0f;
                    return true;
                case double doubleValue:
                    value = Math.Abs(doubleValue) > 0d;
                    return true;
                case string str:
                    return TryParseBoolString(str, out value);
                default:
                    return TryParseBoolString(Convert.ToString(raw, CultureInfo.InvariantCulture), out value);
            }
        }

        private static bool TryParseBoolString(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var text = raw.Trim().ToLowerInvariant();
            if (text is "1" or "true" or "yes")
            {
                value = true;
                return true;
            }

            if (text is "0" or "false" or "no")
            {
                value = false;
                return true;
            }

            return false;
        }

        private static List<string> CoerceStringList(object raw)
        {
            var list = CoerceList(raw);
            if (list == null) return new List<string>();
            return list.Select(item =>
            {
                if (item == null) return string.Empty;
                if (item is JValue jValue && jValue.Value != null) return Convert.ToString(jValue.Value, CultureInfo.InvariantCulture);
                return Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty;
            }).ToList();
        }

        private static List<int> CoerceIntList(object raw)
        {
            var list = CoerceList(raw);
            if (list == null) return new List<int>();
            var result = new List<int>();
            foreach (var item in list)
            {
                if (TryCoerceInt(item, out var value))
                    result.Add(value);
            }
            return result;
        }

        private static List<float> CoerceFloatList(object raw)
        {
            var list = CoerceList(raw);
            if (list == null) return new List<float>();
            var result = new List<float>();
            foreach (var item in list)
            {
                if (TryCoerceFloat(item, out var value))
                    result.Add(value);
            }
            return result;
        }

        private static List<object> CoerceList(object raw)
        {
            if (raw == null) return null;
            if (raw is JValue jValue)
            {
                raw = jValue.Value;
                if (raw == null) return null;
            }

            if (raw is JArray jArray)
            {
                return jArray.ToObject<List<object>>() ?? new List<object>();
            }

            if (raw is IEnumerable<object> enumerable && raw is not string)
            {
                return enumerable.ToList();
            }

            if (raw is System.Collections.IEnumerable genericEnumerable && raw is not string)
            {
                var list = new List<object>();
                foreach (var item in genericEnumerable)
                {
                    list.Add(item);
                }
                return list;
            }

            if (raw is string str)
            {
                return SplitStringList(str).Cast<object>().ToList();
            }

            return new List<object> { raw };
        }

        private static List<string> SplitStringList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw
                .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrEmpty(part))
                .ToList();
        }
    }
}
