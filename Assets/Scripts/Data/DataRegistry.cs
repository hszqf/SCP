using System;
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
    public enum CauseType
    {
        TaskInvestigate,
        TaskContain,
        TaskManage,
        Anomaly,
        LocalPanic,
        Fixed,
        Random,
    }

    public enum BlockPolicy
    {
        None,
        BlockOriginTask,
        BlockAllTasksOnNode,
    }

    public enum IgnoreApplyMode
    {
        ApplyOnceThenRemove,
        ApplyDailyKeep,
        NeverAuto,
    }

    public enum AffectScopeKind
    {
        OriginTask,
        Node,
        Global,
        TaskType,
    }

    public enum EffectOpType
    {
        Add,
        Mul,
        Set,
        ClampAdd,
    }

    public readonly struct AffectScope
    {
        public AffectScope(AffectScopeKind kind, TaskType? taskType = null)
        {
            Kind = kind;
            TaskType = taskType;
        }

        public AffectScopeKind Kind { get; }
        public TaskType? TaskType { get; }
        public string Raw => Kind == AffectScopeKind.TaskType && TaskType.HasValue ? $"TaskType:{TaskType.Value}" : Kind.ToString();

        public override string ToString() => Raw;
    }

    public sealed class EffectOp
    {
        public string EffectId;
        public AffectScope Scope;
        public string StatKey;
        public EffectOpType Op;
        public float Value;
        public float? Min;
        public float? Max;
        public string Comment;
    }

    public sealed class EventTrigger
    {
        public string EventDefId;
        public int? MinDay;
        public int? MaxDay;
        public List<string> RequiresNodeTagsAny = new();
        public List<string> RequiresNodeTagsAll = new();
        public List<string> RequiresAnomalyTagsAny = new();
        public bool? RequiresSecured;
        public int? MinLocalPanic;
        public TaskType? TaskType;
        public bool? OnlyAffectOriginTask;
    }

    public sealed class DataRegistry
    {
        private const string GameDataFileName = "game_data.json";

        private static DataRegistry _instance;
        public static DataRegistry Instance => _instance ??= LoadFromStreamingAssets();

        public GameDataRoot Root { get; private set; }
        public Dictionary<string, NodeDef> NodesById { get; private set; } = new();
        public Dictionary<string, AnomalyDef> AnomaliesById { get; private set; } = new();
        public Dictionary<TaskType, TaskDef> TaskDefsByType { get; private set; } = new();
        public Dictionary<string, EventDef> EventsById { get; private set; } = new();
        public Dictionary<string, List<EventOptionDef>> OptionsByEventId { get; private set; } = new();
        public Dictionary<string, Dictionary<string, EventOptionDef>> OptionsByEventAndId { get; private set; } = new();
        public Dictionary<string, EffectDef> EffectsById { get; private set; } = new();
        public Dictionary<string, List<EffectOp>> EffectOpsByEffectId { get; private set; } = new();
        public Dictionary<string, List<EventTrigger>> TriggersByEventDefId { get; private set; } = new();
        public Dictionary<string, BalanceValue> Balance { get; private set; } = new();
        public TableRegistry Tables { get; private set; } = new();

        public int LocalPanicHighThreshold { get; private set; } = 6;
        public double RandomEventBaseProb { get; private set; } = 0.15d;
        public int DefaultAutoResolveAfterDays { get; private set; } = 0;
        public IgnoreApplyMode DefaultIgnoreApplyMode { get; private set; } = IgnoreApplyMode.ApplyDailyKeep;

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
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Include,
                };
                Root = JsonConvert.DeserializeObject<GameDataRoot>(json, settings) ?? new GameDataRoot();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataRegistry] Failed to load JSON: {ex}");
                throw;
            }

            BuildIndexes();
            GameDataValidator.ValidateOrThrow(this);
            LogSummary();
        }

        private static string LoadJsonText(string path)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using var request = UnityWebRequest.Get(path);
                var op = request.SendWebRequest();
                while (!op.isDone) { }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException($"[DataRegistry] Failed to load JSON from {path}: {request.error}");
                }
                return request.downloadHandler.text;
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"[DataRegistry] Missing game data at: {path}", path);
            }

            return File.ReadAllText(path);
        }

        private void BuildIndexes()
        {
            Balance = Root.balance ?? new Dictionary<string, BalanceValue>();

            NodesById = (Root.nodes ?? new List<NodeDef>())
                .Where(n => n != null && !string.IsNullOrEmpty(n.nodeId))
                .ToDictionary(n => n.nodeId, n => n);

            AnomaliesById = (Root.anomalies ?? new List<AnomalyDef>())
                .Where(a => a != null && !string.IsNullOrEmpty(a.anomalyId))
                .ToDictionary(a => a.anomalyId, a => a);

            TaskDefsByType = new Dictionary<TaskType, TaskDef>();
            foreach (var taskDef in Root.taskDefs ?? new List<TaskDef>())
            {
                if (taskDef == null) continue;
                if (!TryParseTaskType(taskDef.taskType, out var type, out _)) continue;
                TaskDefsByType[type] = taskDef;
            }

            EventsById = (Root.events ?? new List<EventDef>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.eventDefId))
                .ToDictionary(e => e.eventDefId, e => e);

            OptionsByEventId = new Dictionary<string, List<EventOptionDef>>();
            OptionsByEventAndId = new Dictionary<string, Dictionary<string, EventOptionDef>>();
            foreach (var option in Root.eventOptions ?? new List<EventOptionDef>())
            {
                if (option == null || string.IsNullOrEmpty(option.eventDefId) || string.IsNullOrEmpty(option.optionId)) continue;
                if (!OptionsByEventId.TryGetValue(option.eventDefId, out var list))
                {
                    list = new List<EventOptionDef>();
                    OptionsByEventId[option.eventDefId] = list;
                }
                list.Add(option);

                if (!OptionsByEventAndId.TryGetValue(option.eventDefId, out var dict))
                {
                    dict = new Dictionary<string, EventOptionDef>();
                    OptionsByEventAndId[option.eventDefId] = dict;
                }
                dict[option.optionId] = option;
            }

            EffectsById = (Root.effects ?? new List<EffectDef>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.effectId))
                .ToDictionary(e => e.effectId, e => e);

            EffectOpsByEffectId = new Dictionary<string, List<EffectOp>>();
            foreach (var row in Root.effectOps ?? new List<EffectOpRow>())
            {
                if (row == null || string.IsNullOrEmpty(row.effectId)) continue;
                if (!TryParseEffectOp(row, out var ops)) continue;

                if (!EffectOpsByEffectId.TryGetValue(row.effectId, out var list))
                {
                    list = new List<EffectOp>();
                    EffectOpsByEffectId[row.effectId] = list;
                }
                list.AddRange(ops);
            }

            TriggersByEventDefId = new Dictionary<string, List<EventTrigger>>();
            foreach (var row in Root.eventTriggers ?? new List<EventTriggerRow>())
            {
                if (row == null || string.IsNullOrEmpty(row.eventDefId)) continue;
                if (!TryParseTrigger(row, out var trigger)) continue;
                if (!TriggersByEventDefId.TryGetValue(row.eventDefId, out var list))
                {
                    list = new List<EventTrigger>();
                    TriggersByEventDefId[row.eventDefId] = list;
                }
                list.Add(trigger);
            }

            Tables = new TableRegistry(Root.tables);
            Debug.Log($"[Tables] loaded {Tables.TableCount} tables");
            LogTablesSanity();

            LocalPanicHighThreshold = GetBalanceInt("LocalPanicHighThreshold", LocalPanicHighThreshold);
            RandomEventBaseProb = GetBalanceFloat("RandomEventBaseProb", (float)RandomEventBaseProb);
            DefaultAutoResolveAfterDays = GetBalanceInt("DefaultAutoResolveAfterDays", DefaultAutoResolveAfterDays);

            var defaultIgnoreApplyModeRaw = GetBalanceString("DefaultIgnoreApplyMode", DefaultIgnoreApplyMode.ToString());
            if (TryParseIgnoreApplyMode(defaultIgnoreApplyModeRaw, out var parsedMode, out _))
                DefaultIgnoreApplyMode = parsedMode;
        }

        private void LogSummary()
        {
            var schema = Root?.meta?.schemaVersion ?? "unknown";
            var dataVersion = Root?.meta?.dataVersion ?? "unknown";
            int optionsCount = Root?.eventOptions?.Count ?? 0;
            int opsCount = EffectOpsByEffectId?.Values.Sum(list => list?.Count ?? 0) ?? 0;
            int triggersCount = Root?.eventTriggers?.Count ?? 0;

            Debug.Log($"[Data] schema={schema} dataVersion={dataVersion} events={EventsById.Count} options={optionsCount} effects={EffectsById.Count} ops={opsCount} triggers={triggersCount}");
        }

        private void LogTablesSanity()
        {
            if (Tables == null) return;
            if (Root?.tables != null && Root.tables.TryGetValue("Balance", out var balanceTable))
            {
                var columnNames = balanceTable?.columns?
                    .Select(col => col?.name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList() ?? new List<string>();
                var expected = new HashSet<string>(new[] { "key", "p1", "p2", "p3" }, StringComparer.Ordinal);
                var missing = expected.Where(name => !columnNames.Contains(name, StringComparer.Ordinal)).ToList();
                if (missing.Count > 0)
                {
                    var columns = columnNames.Count > 0 ? string.Join(", ", columnNames) : "none";
                    Debug.LogWarning($"[Tables] Balance missing columns: {string.Join(", ", missing)}. columns=[{columns}]");
                }
            }
            if (Tables.TryFindFirstValue("test", out var tableName, out var rowId, out var raw))
            {
                var value = Tables.GetString(tableName, rowId, "test", raw?.ToString() ?? string.Empty);
                Debug.Log($"[Tables] sanity {tableName}[{rowId}].test={value}");
            }
            else
            {
                Debug.LogWarning("[Tables] sanity test column not found");
            }
        }

        private bool TryParseEffectOp(EffectOpRow row, out List<EffectOp> ops)
        {
            ops = new List<EffectOp>();
            if (!TryParseEffectOpType(row.op, out var opType, out _)) return false;
            if (!TryParseAffectScopes(row.scope, out var scopes, out _)) return false;

            foreach (var scope in scopes)
            {
                ops.Add(new EffectOp
                {
                    EffectId = row.effectId,
                    Scope = scope,
                    StatKey = row.statKey,
                    Op = opType,
                    Value = row.value,
                    Min = row.min,
                    Max = row.max,
                    Comment = row.comment,
                });
            }
            return true;
        }

        private bool TryParseTrigger(EventTriggerRow row, out EventTrigger trigger)
        {
            trigger = new EventTrigger
            {
                EventDefId = row.eventDefId,
                MinDay = row.minDay,
                MaxDay = row.maxDay,
                RequiresNodeTagsAny = row.requiresNodeTagsAny ?? new List<string>(),
                RequiresNodeTagsAll = row.requiresNodeTagsAll ?? new List<string>(),
                RequiresAnomalyTagsAny = row.requiresAnomalyTagsAny ?? new List<string>(),
                RequiresSecured = row.requiresSecured,
                MinLocalPanic = row.minLocalPanic,
                OnlyAffectOriginTask = row.onlyAffectOriginTask,
            };

            if (!string.IsNullOrEmpty(row.taskType))
            {
                if (!TryParseTaskType(row.taskType, out var type, out _)) return false;
                trigger.TaskType = type;
            }

            return true;
        }

        public bool TryGetEvent(string eventDefId, out EventDef def)
            => EventsById.TryGetValue(eventDefId, out def);

        public bool TryGetOption(string eventDefId, string optionId, out EventOptionDef option)
        {
            option = null;
            if (!OptionsByEventAndId.TryGetValue(eventDefId, out var dict)) return false;
            return dict.TryGetValue(optionId, out option);
        }

        public bool TryGetTaskDef(TaskType type, out TaskDef def)
            => TaskDefsByType.TryGetValue(type, out def);

        public int GetTaskBaseDays(TaskType type, int fallback)
        {
            return TryGetTaskDef(type, out var def) && def.baseDays > 0 ? def.baseDays : fallback;
        }

        public float GetTaskProgressPerDay(TaskType type, float fallback)
        {
            return TryGetTaskDef(type, out var def) && def.progressPerDay > 0f ? def.progressPerDay : fallback;
        }

        public IgnoreApplyMode GetIgnoreApplyMode(EventDef def)
        {
            if (def != null && TryParseIgnoreApplyMode(def.ignoreApplyMode, out var mode, out _))
                return mode;
            return DefaultIgnoreApplyMode;
        }

        public int GetAutoResolveAfterDays(EventDef def)
        {
            if (def != null && def.autoResolveAfterDays > 0) return def.autoResolveAfterDays;
            return DefaultAutoResolveAfterDays;
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

        public static bool TryParseTaskType(string raw, out TaskType type, out string error)
        {
            error = null;
            type = TaskType.Investigate;
            if (string.IsNullOrEmpty(raw))
            {
                error = "TaskType empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out type)) return true;
            error = $"Invalid TaskType: {raw}";
            return false;
        }

        public static bool TryParseEventSource(string raw, out EventSource source, out string error)
        {
            error = null;
            source = EventSource.Random;
            if (string.IsNullOrEmpty(raw))
            {
                error = "EventSource empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out source)) return true;
            error = $"Invalid EventSource: {raw}";
            return false;
        }

        public static bool TryParseCauseType(string raw, out CauseType causeType, out string error)
        {
            error = null;
            causeType = CauseType.Random;
            if (string.IsNullOrEmpty(raw))
            {
                error = "CauseType empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out causeType)) return true;
            error = $"Invalid CauseType: {raw}";
            return false;
        }

        public static bool TryParseBlockPolicy(string raw, out BlockPolicy policy, out string error)
        {
            error = null;
            policy = BlockPolicy.None;
            if (string.IsNullOrEmpty(raw))
            {
                error = "BlockPolicy empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out policy)) return true;
            error = $"Invalid BlockPolicy: {raw}";
            return false;
        }

        public static bool TryParseIgnoreApplyMode(string raw, out IgnoreApplyMode mode, out string error)
        {
            error = null;
            mode = IgnoreApplyMode.ApplyDailyKeep;
            if (string.IsNullOrEmpty(raw))
            {
                error = "IgnoreApplyMode empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out mode)) return true;
            error = $"Invalid IgnoreApplyMode: {raw}";
            return false;
        }

        public static bool TryParseEffectOpType(string raw, out EffectOpType opType, out string error)
        {
            error = null;
            opType = EffectOpType.Add;
            if (string.IsNullOrEmpty(raw))
            {
                error = "EffectOpType empty";
                return false;
            }

            if (Enum.TryParse(raw, true, out opType)) return true;
            error = $"Invalid EffectOpType: {raw}";
            return false;
        }

        public static bool TryParseAffectScopes(IEnumerable<string> raws, out List<AffectScope> scopes, out string error)
        {
            scopes = new List<AffectScope>();
            error = null;
            if (raws == null)
            {
                error = "AffectScope empty";
                return false;
            }

            foreach (var raw in raws)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!TryParseAffectScope(raw, out var scope, out error)) return false;
                scopes.Add(scope);
            }

            if (scopes.Count == 0)
            {
                error = "AffectScope empty";
                return false;
            }
            return true;
        }

        public static bool TryParseAffectScopes(string raw, out List<AffectScope> scopes, out string error)
        {
            scopes = new List<AffectScope>();
            error = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "AffectScope empty";
                return false;
            }

            var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!TryParseAffectScope(part.Trim(), out var scope, out error)) return false;
                scopes.Add(scope);
            }

            if (scopes.Count == 0)
            {
                error = "AffectScope empty";
                return false;
            }

            return true;
        }

        public static bool TryParseAffectScope(string raw, out AffectScope scope, out string error)
        {
            error = null;
            scope = new AffectScope(AffectScopeKind.Node);
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "AffectScope empty";
                return false;
            }

            if (raw.StartsWith("TaskType:", StringComparison.OrdinalIgnoreCase))
            {
                var typeRaw = raw.Substring("TaskType:".Length);
                if (!TryParseTaskType(typeRaw, out var taskType, out error)) return false;
                scope = new AffectScope(AffectScopeKind.TaskType, taskType);
                return true;
            }

            if (Enum.TryParse(raw, true, out AffectScopeKind kind))
            {
                scope = new AffectScope(kind);
                return true;
            }

            error = $"Invalid AffectScope: {raw}";
            return false;
        }
    }
}
