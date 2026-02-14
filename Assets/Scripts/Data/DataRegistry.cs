using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Data
{


    public sealed class DataRegistry
    {
        private const string GameDataFileName = "game_data.json";
        private const string RequirementAny = "ANY";

        private static DataRegistry _instance;
        public static DataRegistry Instance => _instance ??= LoadFromStreamingAssets();

        private readonly HashSet<TaskType> _taskDefMissingWarned = new();
        private bool _hasTaskDefsTable;

        public GameDataRoot Root { get; private set; }
        public Dictionary<string, AnomalyDef> AnomaliesById { get; private set; } = new();
        public Dictionary<TaskType, TaskDef> TaskDefsByType { get; private set; } = new();
        public Dictionary<string, TaskDef> TaskDefsById { get; private set; } = new();

        public Dictionary<string, BalanceValue> Balance { get; private set; } = new();
        public TableRegistry Tables { get; private set; } = new();
        public List<AnomaliesGenDef> AnomaliesGen { get; private set; } = new();

        public int GetAnomaliesGenNumForDay(int day)
        {
            if (AnomaliesGen == null || AnomaliesGen.Count == 0) return 0;
            int total = 0;
            foreach (var row in AnomaliesGen)
            {
                if (row == null) continue;
                if (row.day != day) continue;
                if (row.AnomaliesGenNum <= 0) continue;
                total += row.AnomaliesGenNum;
            }
            return total;
        }

        private DataRegistry() { }

        private static DataRegistry LoadFromStreamingAssets()
        {
            var registry = new DataRegistry();
            registry.Reload();
            return registry;
        }

        public void Reload()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, GameDataFileName);
                var json = LoadJsonText(path);
                LoadFromJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataRegistry] Failed to load JSON: {ex}");
                throw;
            }
        }

        private void LoadFromJson(string json)
        {
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
            };
            Root = JsonConvert.DeserializeObject<GameDataRoot>(json, settings) ?? new GameDataRoot();

            BuildIndexes();
            GameDataValidator.ValidateOrThrow(this);
            LogSummary();
        }

        private static string LoadJsonText(string path)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                throw new InvalidOperationException("[DataRegistry] WebGL cannot load JSON synchronously. Use LoadJsonTextCoroutine / LoadJsonTextAsync.");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"[DataRegistry] Missing game data at: {path}", path);
            }

            return File.ReadAllText(path);
        }

        public static IEnumerator LoadJsonTextCoroutine(string url, Action<string> onOk, Action<Exception> onErr)
        {
            Debug.Log($"[DataRegistry] Sending HTTP request to: {url}");
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            Debug.Log($"[DataRegistry] Request completed - result={request.result} responseCode={request.responseCode} error={request.error ?? "none"}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[DataRegistry] HTTP request FAILED - code={request.responseCode} error={request.error}");
                onErr?.Invoke(new InvalidOperationException(
                    $"[DataRegistry] Failed to load JSON from {url}: code={request.responseCode} error={request.error}"));
                yield break;
            }

            int textLen = request.downloadHandler.text?.Length ?? 0;
            Debug.Log($"[DataRegistry] HTTP request SUCCESS - text length={textLen} characters");
            onOk?.Invoke(request.downloadHandler.text);
        }

        public static DataRegistry InitFromJson(string json)
        {
            var registry = new DataRegistry();
            registry.LoadFromJson(json);
            _instance = registry;
            return registry;
        }

        private void BuildIndexes()
        {
            _taskDefMissingWarned.Clear();
            Balance = Root.balance ?? new Dictionary<string, BalanceValue>();

            Tables = new TableRegistry(Root.tables);
            Debug.Log($"[Tables] loaded {Tables.TableCount} tables");
            LogTablesSanity();
            _hasTaskDefsTable = Tables.TryGetTable("TaskDefs", out _);

            AnomaliesById = new Dictionary<string, AnomalyDef>();
            foreach (var row in Tables.GetRows("Anomalies"))
            {
                var anomalyId = GetRowString(row, "anomalyId");
                if (string.IsNullOrEmpty(anomalyId)) continue;
                AnomaliesById[anomalyId] = new AnomalyDef
                {
                    anomalyId = anomalyId,
                    name = GetRowString(row, "name"),
                    @class = GetRowString(row, "class"),
                    baseDays = GetRowInt(row, "baseDays"),
                    actPeopleKill = GetRowInt(row, "actPeopleKill"),
                    range = GetRowFloat(row, "range"),
                    invExp = GetRowInt(row, "invExp"),
                    conExp = GetRowInt(row, "conExp"),
                    manExpPerDay = GetRowInt(row, "manExpPerDay"),
                    manNegentropyPerDay = GetRowInt(row, "manNegentropyPerDay"),
                    invHp = GetRowInt(row, "invHp"),
                    invSan = GetRowInt(row, "invSan"),
                    conHp = GetRowInt(row, "conHp"),
                    conSan = GetRowInt(row, "conSan"),
                    manHp = GetRowInt(row, "manHp"),
                    manSan = GetRowInt(row, "manSan"),
                    invhpDmg = GetRowInt(row, "invhpDmg"),
                    invsanDmg = GetRowInt(row, "invsanDmg"),
                    conhpDmg = GetRowInt(row, "conhpDmg"),
                    consanDmg = GetRowInt(row, "consanDmg"),
                    manhpDmg = GetRowInt(row, "manhpDmg"),
                    mansanDmg = GetRowInt(row, "mansanDmg"),
                    invReq = GetRowIntArray4(row, "invReq", anomalyId),
                    conReq = GetRowIntArray4(row, "conReq", anomalyId),
                    manReq = GetRowIntArray4(row, "manReq", anomalyId),
                };
            }

            AnomaliesGen = new List<AnomaliesGenDef>();
            foreach (var row in Tables.GetRows("AnomaliesGen"))
            {
                if (row == null) continue;
                AnomaliesGen.Add(new AnomaliesGenDef
                {
                    day = GetRowInt(row, "day"),
                    AnomaliesGenNum = GetRowInt(row, "AnomaliesGenNum"),
                });
            }

            // --- TaskDefs: build dictionaries by id and by TaskType ---
            TaskDefsById = new Dictionary<string, TaskDef>(StringComparer.Ordinal);
            TaskDefsByType = new Dictionary<TaskType, TaskDef>();
            foreach (var row in Tables.GetRows("TaskDefs"))
            {
                if (row == null) continue;
                var id = GetRowString(row, "taskDefId");
                if (string.IsNullOrEmpty(id)) continue;

                var def = new TaskDef
                {
                    taskDefId = id,
                    taskType = GetRowString(row, "taskType"),
                    name = GetRowString(row, "name"),
                    agentSlotsMin = GetRowInt(row, "agentSlotsMin"),
                    agentSlotsMax = GetRowInt(row, "agentSlotsMax"),
                };

                // store by id
                TaskDefsById[id] = def;

                // try map to enum type
                var typeStr = def.taskType;
                if (!string.IsNullOrEmpty(typeStr))
                {
                    if (Enum.TryParse(typeof(TaskType), typeStr, true, out var enumObj) && enumObj is TaskType tt)
                    {
                        if (!TaskDefsByType.ContainsKey(tt))
                        {
                            TaskDefsByType[tt] = def;
                        }
                        else
                        {
                            Debug.LogWarning($"[TaskDefs] Duplicate TaskDefs entry for taskType={tt} (ids: existing={TaskDefsByType[tt].taskDefId} new={id}). Using first.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[TaskDefs] Unknown taskType value '{typeStr}' in TaskDefs row id={id}.");
                    }
                }
            }

            if (AnomaliesById.TryGetValue("AN_001", out var sampleAnomaly))
            {
                Debug.Log($"[DataSample] AN_001 invHp={sampleAnomaly.invHp} invSan={sampleAnomaly.invSan} conHp={sampleAnomaly.conHp} conSan={sampleAnomaly.conSan} manHp={sampleAnomaly.manHp} manSan={sampleAnomaly.manSan}");
                Debug.Log($"[DataSample] AN_001 invReq={FormatIntArray(sampleAnomaly.invReq)} conReq={FormatIntArray(sampleAnomaly.conReq)} manReq={FormatIntArray(sampleAnomaly.manReq)}");
            }

            Debug.Log($"[TaskDefs] loaded ids={TaskDefsById.Count} byType={TaskDefsByType.Count}");

        }

        private void LogSummary()
        {
            var schema = Root?.meta?.schemaVersion ?? "unknown";
            var dataVersion = Root?.meta?.dataVersion ?? "unknown";
            if (Tables != null && Tables.TryGetTable("Meta", out var metaTable) && metaTable?.rows?.Count > 0)
            {
                var row = metaTable.rows[0];
                schema = GetRowString(row, "schemaVersion", schema);
                dataVersion = GetRowString(row, "dataVersion", dataVersion);
            }

            Debug.Log($"[Data] schema={schema} dataVersion={dataVersion}");
        }

        private void LogTablesSanity()
        {
            if (Tables == null) return;
            if (Root?.tables != null)
            {
                foreach (var tableEntry in Root.tables)
                {
                    var rowCount = tableEntry.Value?.rows?.Count ?? 0;
                    Debug.Log($"[Tables] {tableEntry.Key} rows={rowCount}");
                }
            }

            CheckTableColumns("Meta", new[] { "schemaVersion", "dataVersion" });
            CheckTableColumns("Balance", new[] { "key", "p1", "p2", "p3" });
            CheckTableColumns("Anomalies", new[]
            {
                "anomalyId", "name", "class", "baseDays", "actPeopleKill", "range", "invExp", "conExp", "manExpPerDay", "manNegentropyPerDay",
                "invhpDmg", "invsanDmg", "conhpDmg", "consanDmg", "manhpDmg", "mansanDmg",
                "worldPanicPerDayUncontained", "maintenanceCostPerDay",
            });
            CheckTableColumns("TaskDefs", new[] { "taskDefId", "taskType", "name", "agentSlotsMin", "agentSlotsMax" });
            CheckTableColumns("AnomaliesGen", new[]
            {
                "day", "AnomaliesGenNum",
            });

        }

        private void CheckTableColumns(string tableName, IEnumerable<string> requiredColumns)
        {
            if (Root?.tables == null || !Root.tables.TryGetValue(tableName, out var table) || table == null)
            {
                Debug.LogWarning($"[Tables] Missing table: {tableName}.");
                return;
            }

            var columnNames = table.columns?
                .Select(col => col?.name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList() ?? new List<string>();
            var normalizedColumns = new HashSet<string>(columnNames.Select(NormalizeColumnName), StringComparer.Ordinal);


            var missing = requiredColumns
                .Where(name => !normalizedColumns.Contains(NormalizeColumnName(name)))
                .ToList();
            if (missing.Count > 0)
            {
                var columns = columnNames.Count > 0 ? string.Join(", ", columnNames) : "none";
                Debug.LogWarning($"[Tables] {tableName} missing columns: {string.Join(", ", missing)}. columns=[{columns}]");
            }
        }

        private static string NormalizeColumnName(string name)
            => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().ToLowerInvariant();


        private int GetTableRowCountWithWarn(string tableName)
        {
            if (Tables == null || !Tables.TryGetTable(tableName, out var table) || table?.rows == null)
            {
                Debug.LogWarning($"[WARN] Missing table: {tableName}.");
                return 0;
            }

            return table.rows.Count;
        }

        private void LogGroupIndexSummary<T>(string tableName, string columnName, Dictionary<string, List<T>> groups)
        {
            var rows = Tables?.GetRows(tableName)?.Count ?? 0;
            var groupCount = groups?.Count ?? 0;
            Debug.Log($"[DataIndex] {tableName} rows={rows} groups({columnName})={groupCount}");
        }

        private static string GetRowString(Dictionary<string, object> row, string column, string fallback = "")
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return fallback;
            return TableRegistry.TryCoerceString(raw, out var value) ? value ?? fallback : fallback;
        }

        private static int GetRowInt(Dictionary<string, object> row, string column, int fallback = 0)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return fallback;
            return TableRegistry.TryCoerceInt(raw, out var value) ? value : fallback;
        }

        private static int? GetRowIntNullable(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return null;
            return TableRegistry.TryCoerceInt(raw, out var value) ? value : null;
        }

        private static float GetRowFloat(Dictionary<string, object> row, string column, float fallback = 0f)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return fallback;
            return TableRegistry.TryCoerceFloat(raw, out var value) ? value : fallback;
        }

        private static float? GetRowFloatNullable(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return null;
            return TableRegistry.TryCoerceFloat(raw, out var value) ? value : null;
        }

        private static float[] GetRowFloatList(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return null;
            var list = TableRegistry.CoerceFloatList(raw);
            return list != null && list.Count > 0 ? list.ToArray() : null;
        }

        private static int[] GetRowIntArray4(Dictionary<string, object> row, string column, string anomalyId)
        {
            object raw = null;
            if (row == null || !row.TryGetValue(column, out raw) || raw == null)
            {
                LogAnomalyArrayParseWarn(anomalyId, column, raw);
                return new int[4];
            }

            var list = TableRegistry.CoerceList(raw);
            if (list == null || list.Count == 0)
            {
                LogAnomalyArrayParseWarn(anomalyId, column, raw);
                return new int[4];
            }

            var result = new int[4];
            var count = Math.Min(list.Count, 4);
            for (var i = 0; i < count; i++)
            {
                if (!TableRegistry.TryCoerceInt(list[i], out var value))
                {
                    LogAnomalyArrayParseWarn(anomalyId, column, raw);
                    return new int[4];
                }
                result[i] = value;
            }

            return result;
        }

        private static void LogAnomalyArrayParseWarn(string anomalyId, string field, object raw)
        {
            var rawText = RawToString(raw);
            Debug.LogWarning($"[DataParseWarn] AnomalyDef {anomalyId} field={field} raw=\"{rawText}\" -> fallback [0,0,0,0]");
        }

        private static string RawToString(object raw)
        {
            if (raw == null) return string.Empty;
            if (raw is Newtonsoft.Json.Linq.JToken token) return token.ToString();
            return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string FormatIntArray(int[] values)
        {
            if (values == null) return "null";
            return $"[{string.Join(",", values)}]";
        }

        private static bool? GetRowBoolNullable(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return null;
            return TableRegistry.TryCoerceBool(raw, out var value) ? value : null;
        }

        private static List<string> GetRowStringList(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return new List<string>();
            return TableRegistry.CoerceStringList(raw);
        }

     

        public TaskDef GetTaskDefById(string taskDefId)
            => !string.IsNullOrEmpty(taskDefId) && TaskDefsById.TryGetValue(taskDefId, out var def) ? def : null;

        public bool TryGetTaskDefForType(TaskType type, out TaskDef def)
        {
            if (!_hasTaskDefsTable)
            {
                def = null;
                return false;
            }
            if (TaskDefsByType.TryGetValue(type, out def)) return true;
            WarnMissingTaskDef(type);
            def = null;
            return false;
        }

        public (int min, int max) GetTaskAgentSlotRangeWithWarn(TaskType type, int fallbackMin, int fallbackMax)
        {
            if (TryGetTaskDefForType(type, out var def))
            {
                int min = def.agentSlotsMin > 0 ? def.agentSlotsMin : fallbackMin;
                int max = def.agentSlotsMax > 0 ? def.agentSlotsMax : fallbackMax;
                if (max < min) max = min;
                return (min, max);
            }

            return (fallbackMin, fallbackMax);
        }

        private void WarnMissingTaskDef(TaskType type)
        {
            if (!_hasTaskDefsTable) return;
            if (_taskDefMissingWarned.Add(type))
                Debug.LogWarning($"[TaskDef] Missing TaskDefs entry for taskType={type}. Using fallback values.");
        }

        private int GetBalanceInt(string key, int fallback)
        {
            if (!TryGetBalanceRow(key, out _))
            {
                Debug.LogWarning($"[WARN] Missing Balance row: {key}. Using fallback={fallback}.");
                return fallback;
            }

            var ints = GetBalanceIntArray(key);
            if (ints.Count > 0) return ints[0];

            var floats = GetBalanceFloatArray(key);
            if (floats.Count > 0) return Mathf.RoundToInt(floats[0]);

            var strings = GetBalanceStringArray(key);
            if (strings.Count > 0 && int.TryParse(strings[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            Debug.LogWarning($"[WARN] Missing Balance value: Balance.{key}. Using fallback={fallback}.");
            return fallback;
        }

        private float GetBalanceFloat(string key, float fallback)
        {
            if (!TryGetBalanceRow(key, out _))
            {
                Debug.LogWarning($"[WARN] Missing Balance row: {key}. Using fallback={fallback}.");
                return fallback;
            }

            var floats = GetBalanceFloatArray(key);
            if (floats.Count > 0) return floats[0];

            var ints = GetBalanceIntArray(key);
            if (ints.Count > 0) return ints[0];

            var strings = GetBalanceStringArray(key);
            if (strings.Count > 0 && float.TryParse(strings[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            Debug.LogWarning($"[WARN] Missing Balance value: Balance.{key}. Using fallback={fallback}.");
            return fallback;
        }

        private string GetBalanceString(string key, string fallback)
        {
            if (!TryGetBalanceRow(key, out _))
            {
                Debug.LogWarning($"[WARN] Missing Balance row: {key}. Using fallback={fallback}.");
                return fallback;
            }

            var strings = GetBalanceStringArray(key);
            if (strings.Count > 0) return strings[0];

            Debug.LogWarning($"[WARN] Missing Balance value: Balance.{key}. Using fallback={fallback}.");
            return fallback;
        }

        public List<int> GetBalanceIntArray(string key)
        {
            if (!TryGetBalanceRow(key, out _)) return new List<int>();
            return Tables.GetIntList("Balance", key, "p1") ?? new List<int>();
        }

        public List<float> GetBalanceFloatArray(string key)
        {
            if (!TryGetBalanceRow(key, out _)) return new List<float>();
            return Tables.GetFloatList("Balance", key, "p2") ?? new List<float>();
        }

        public List<string> GetBalanceStringArray(string key)
        {
            if (!TryGetBalanceRow(key, out _)) return new List<string>();
            return Tables.GetStringList("Balance", key, "p3") ?? new List<string>();
        }

        private bool TryGetBalanceRow(string key, out Dictionary<string, object> row)
        {
            row = null;
            if (Tables == null || string.IsNullOrEmpty(key)) return false;
            return Tables.TryGetRow("Balance", key, out row);
        }

        public int GetBalanceIntWithWarn(string key, int fallback = 0)
            => GetBalanceInt(key, fallback);

        public float GetBalanceFloatWithWarn(string key, float fallback = 0f)
            => GetBalanceFloat(key, fallback);

        public string GetBalanceStringWithWarn(string key, string fallback = "")
            => GetBalanceString(key, fallback);

        public int GetAnomalyIntWithWarn(string anomalyId, string column, int fallback = 0)
            => GetTableIntWithWarn("Anomalies", anomalyId, column, fallback);

        public float GetAnomalyFloatWithWarn(string anomalyId, string column, float fallback = 0f)
            => GetTableFloatWithWarn("Anomalies", anomalyId, column, fallback);

        public int GetAnomalyBaseDaysWithWarn(string anomalyId, int fallback = 1)
            => GetTableIntWithWarn("Anomalies", anomalyId, "baseDays", fallback);

        private int GetTableIntWithWarn(string tableName, string rowId, string column, int fallback)
        {
            if (!TryGetTableValue(tableName, rowId, column, out var raw))
            {
                Debug.LogWarning($"[WARN] Missing table value: {tableName}.{rowId}.{column}. Using fallback={fallback}.");
                return fallback;
            }

            if (TryParseInt(raw, out var value)) return value;
            Debug.LogWarning($"[WARN] Invalid int table value: {tableName}.{rowId}.{column}={raw}. Using fallback={fallback}.");
            return fallback;
        }

        private float GetTableFloatWithWarn(string tableName, string rowId, string column, float fallback)
        {
            if (!TryGetTableValue(tableName, rowId, column, out var raw))
            {
                Debug.LogWarning($"[WARN] Missing table value: {tableName}.{rowId}.{column}. Using fallback={fallback}.");
                return fallback;
            }

            if (TryParseFloat(raw, out var value)) return value;
            Debug.LogWarning($"[WARN] Invalid float table value: {tableName}.{rowId}.{column}={raw}. Using fallback={fallback}.");
            return fallback;
        }

        private string GetTableStringWithWarn(string tableName, string rowId, string column, string fallback)
        {
            if (!TryGetTableValue(tableName, rowId, column, out var raw))
            {
                Debug.LogWarning($"[WARN] Missing table value: {tableName}.{rowId}.{column}. Using fallback={fallback}.");
                return fallback;
            }

            var parsed = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(parsed)) return parsed;
            Debug.LogWarning($"[WARN] Invalid string table value: {tableName}.{rowId}.{column}={raw}. Using fallback={fallback}.");
            return fallback;
        }

        private bool TryGetTableValue(string tableName, string rowId, string column, out object raw)
        {
            raw = null;
            if (Tables == null || string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(rowId) || string.IsNullOrEmpty(column))
                return false;

            if (!Tables.TryGetRow(tableName, rowId, out var row) || row == null)
                return false;

            return row.TryGetValue(column, out raw);
        }

        private static bool TryParseInt(object raw, out int value)
        {
            value = 0;
            if (raw == null) return false;
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
                default:
                    var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
        }

        private static bool TryParseFloat(object raw, out float value)
        {
            value = 0f;
            if (raw == null) return false;
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
                default:
                    var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }
    }
}
