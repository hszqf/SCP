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
        public string RowId;
        public string EventDefId;
        public int? MinDay;
        public int? MaxDay;
        public string RequiresNodeId;
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

        private readonly HashSet<TaskType> _taskDefMissingWarned = new();
        private bool _warnedMissingTriggerRequiresNodeId;
        private bool _warnedMissingEventRequiresAnomalyId;
        private bool _warnedMissingEventRequiresTaskType;
        private bool _warnedDeprecatedTriggerNodeTags;
        private bool _warnedDeprecatedTriggerAnomalyTags;

        public GameDataRoot Root { get; private set; }
        public Dictionary<string, NodeDef> NodesById { get; private set; } = new();
        public Dictionary<string, AnomalyDef> AnomaliesById { get; private set; } = new();
        public Dictionary<TaskType, TaskDef> TaskDefsByType { get; private set; } = new();
        public Dictionary<string, TaskDef> TaskDefsById { get; private set; } = new();
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
            _taskDefMissingWarned.Clear();
            Balance = Root.balance ?? new Dictionary<string, BalanceValue>();

            Tables = new TableRegistry(Root.tables);
            Debug.Log($"[Tables] loaded {Tables.TableCount} tables");
            LogTablesSanity();

            NodesById = new Dictionary<string, NodeDef>();
            foreach (var row in Tables.GetRows("Nodes"))
            {
                var nodeId = GetRowString(row, "nodeId");
                if (string.IsNullOrEmpty(nodeId)) continue;
                NodesById[nodeId] = new NodeDef
                {
                    nodeId = nodeId,
                    name = GetRowString(row, "name"),
                    tags = GetRowStringList(row, "tags"),
                    startLocalPanic = GetRowInt(row, "startLocalPanic"),
                    startPopulation = GetRowInt(row, "startPopulation"),
                    startAnomalyIds = GetRowStringList(row, "startAnomalyIds"),
                };
            }

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
                    tags = GetRowStringList(row, "tags"),
                    baseThreat = GetRowInt(row, "baseThreat"),
                    investigateDifficulty = GetRowInt(row, "investigateDifficulty"),
                    containDifficulty = GetRowInt(row, "containDifficulty"),
                    manageRisk = GetRowInt(row, "manageRisk"),
                };
            }

            TaskDefsByType = new Dictionary<TaskType, TaskDef>();
            TaskDefsById = new Dictionary<string, TaskDef>();
            foreach (var row in Tables.GetRows("TaskDefs"))
            {
                var taskTypeRaw = GetRowString(row, "taskType");
                if (string.IsNullOrEmpty(taskTypeRaw)) continue;
                if (!TryParseTaskType(taskTypeRaw, out var type, out _)) continue;
                var taskDef = new TaskDef
                {
                    taskDefId = GetRowString(row, "taskDefId"),
                    taskType = taskTypeRaw,
                    name = GetRowString(row, "name"),
                    baseDays = GetRowInt(row, "baseDays"),
                    progressPerDay = GetRowFloat(row, "progressPerDay"),
                    agentSlotsMin = GetRowInt(row, "agentSlotsMin"),
                    agentSlotsMax = GetRowInt(row, "agentSlotsMax"),
                    yieldKey = GetRowString(row, "yieldKey"),
                    yieldPerDay = GetRowFloat(row, "yieldPerDay"),
                    hasYieldKey = row != null && row.ContainsKey("yieldKey"),
                    hasYieldPerDay = row != null && row.ContainsKey("yieldPerDay"),
                };
                TaskDefsByType[type] = taskDef;
                if (!string.IsNullOrEmpty(taskDef.taskDefId))
                {
                    TaskDefsById[taskDef.taskDefId] = taskDef;
                }
            }

            EventsById = new Dictionary<string, EventDef>();
            foreach (var row in Tables.GetRows("Events"))
            {
                var eventDefId = GetRowString(row, "eventDefId");
                if (string.IsNullOrEmpty(eventDefId)) continue;
                WarnOnMissingEventRequirements();
                EventsById[eventDefId] = new EventDef
                {
                    eventDefId = eventDefId,
                    source = GetRowString(row, "source"),
                    causeType = GetRowString(row, "causeType"),
                    weight = GetRowInt(row, "weight"),
                    title = GetRowString(row, "title"),
                    desc = GetRowString(row, "desc"),
                    blockPolicy = GetRowString(row, "blockPolicy"),
                    defaultAffects = GetRowStringList(row, "defaultAffects"),
                    autoResolveAfterDays = GetRowInt(row, "autoResolveAfterDays"),
                    ignoreApplyMode = GetRowString(row, "ignoreApplyMode"),
                    ignoreEffectId = GetRowString(row, "ignoreEffectId"),
                    requiresAnomalyId = NormalizeRequirement(GetRowString(row, "requiresAnomalyId")),
                    requiresTaskType = NormalizeRequirement(GetEventTaskTypeAlias(row)),
                };
            }

            OptionsByEventId = new Dictionary<string, List<EventOptionDef>>();
            OptionsByEventAndId = new Dictionary<string, Dictionary<string, EventOptionDef>>();
            foreach (var row in Tables.GetRows("EventOptions"))
            {
                var eventDefId = GetRowString(row, "eventDefId");
                var optionId = GetRowString(row, "optionId");
                if (string.IsNullOrEmpty(eventDefId) || string.IsNullOrEmpty(optionId)) continue;
                var option = new EventOptionDef
                {
                    eventDefId = eventDefId,
                    optionId = optionId,
                    text = GetRowString(row, "text"),
                    resultText = GetRowString(row, "resultText"),
                    affects = GetRowStringList(row, "affects"),
                    effectId = GetRowString(row, "effectId"),
                };

                if (!OptionsByEventId.TryGetValue(eventDefId, out var list))
                {
                    list = new List<EventOptionDef>();
                    OptionsByEventId[eventDefId] = list;
                }
                list.Add(option);

                if (!OptionsByEventAndId.TryGetValue(eventDefId, out var dict))
                {
                    dict = new Dictionary<string, EventOptionDef>();
                    OptionsByEventAndId[eventDefId] = dict;
                }
                dict[optionId] = option;
            }

            EffectsById = new Dictionary<string, EffectDef>();
            foreach (var row in Tables.GetRows("Effects"))
            {
                var effectId = GetRowString(row, "effectId");
                if (string.IsNullOrEmpty(effectId)) continue;
                EffectsById[effectId] = new EffectDef
                {
                    effectId = effectId,
                    comment = GetRowString(row, "comment"),
                };
            }

            EffectOpsByEffectId = new Dictionary<string, List<EffectOp>>();
            foreach (var row in Tables.GetRows("EffectOps"))
            {
                var effectId = GetRowString(row, "effectId");
                if (string.IsNullOrEmpty(effectId)) continue;
                var rowModel = new EffectOpRow
                {
                    effectId = effectId,
                    scope = GetRowString(row, "scope"),
                    statKey = GetRowString(row, "statKey"),
                    op = GetRowString(row, "op"),
                    value = GetRowFloat(row, "value"),
                    min = GetRowFloatNullable(row, "min"),
                    max = GetRowFloatNullable(row, "max"),
                    comment = GetRowString(row, "comment"),
                };
                if (!TryParseEffectOp(rowModel, out var ops)) continue;

                if (!EffectOpsByEffectId.TryGetValue(effectId, out var list))
                {
                    list = new List<EffectOp>();
                    EffectOpsByEffectId[effectId] = list;
                }
                list.AddRange(ops);
            }

            TriggersByEventDefId = new Dictionary<string, List<EventTrigger>>();
            foreach (var row in Tables.GetRows("EventTriggers"))
            {
                var eventDefId = GetRowString(row, "eventDefId");
                if (string.IsNullOrEmpty(eventDefId)) continue;
                WarnOnDeprecatedTriggerFields();
                WarnOnMissingTriggerRequiresNodeId();
                var rowId = GetRowString(row, "rowId");
                if (string.IsNullOrEmpty(rowId))
                {
                    rowId = GetRowString(row, "key");
                    if (string.IsNullOrEmpty(rowId))
                    {
                        var rowKey = GetRowIntNullable(row, "key");
                        rowId = rowKey?.ToString();
                    }
                }
                var rowModel = new EventTriggerRow
                {
                    rowId = rowId,
                    eventDefId = eventDefId,
                    minDay = GetRowIntNullable(row, "minDay"),
                    maxDay = GetRowIntNullable(row, "maxDay"),
                    requiresNodeId = NormalizeRequirement(GetRowString(row, "requiresNodeId")),
                    requiresSecured = GetRowBoolNullable(row, "requiresSecured"),
                    minLocalPanic = GetRowIntNullable(row, "minLocalPanic"),
                    taskType = GetRowString(row, "taskType"),
                    onlyAffectOriginTask = GetRowBoolNullable(row, "onlyAffectOriginTask"),
                };
                if (!TryParseTrigger(rowModel, out var trigger)) continue;
                if (!TriggersByEventDefId.TryGetValue(eventDefId, out var list))
                {
                    list = new List<EventTrigger>();
                    TriggersByEventDefId[eventDefId] = list;
                }
                list.Add(trigger);
            }

            LogGroupIndexSummary("EventOptions", "eventDefId", OptionsByEventId);
            LogGroupIndexSummary("EffectOps", "effectId", EffectOpsByEffectId);
            LogGroupIndexSummary("EventTriggers", "eventDefId", TriggersByEventDefId);

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
            if (Tables != null && Tables.TryGetTable("Meta", out var metaTable) && metaTable?.rows?.Count > 0)
            {
                var row = metaTable.rows[0];
                schema = GetRowString(row, "schemaVersion", schema);
                dataVersion = GetRowString(row, "dataVersion", dataVersion);
            }

            int eventsCount = GetTableRowCountWithWarn("Events");
            int optionsCount = GetTableRowCountWithWarn("EventOptions");
            int effectsCount = GetTableRowCountWithWarn("Effects");
            int opsCount = GetTableRowCountWithWarn("EffectOps");
            int triggersCount = GetTableRowCountWithWarn("EventTriggers");

            Debug.Log($"[Data] schema={schema} dataVersion={dataVersion} events={eventsCount} options={optionsCount} effects={effectsCount} ops={opsCount} triggers={triggersCount}");
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
            CheckTableColumns("Nodes", new[]
            {
                "nodeId", "name", "tags", "startLocalPanic", "startPopulation", "startAnomalyIds",
            });
            CheckTableColumns("Anomalies", new[]
            {
                "anomalyId", "name", "class", "baseThreat", "investigateDifficulty",
                "containDifficulty", "manageRisk", "worldPanicPerDayUncontained", "maintenanceCostPerDay",
            });
            CheckTableColumns("Events", new[]
            {
                "eventDefId", "source", "causeType", "weight", "title", "desc", "blockPolicy",
                "defaultAffects", "autoResolveAfterDays", "ignoreApplyMode", "ignoreEffectId",
                "requiresAnomalyId", "requiresTaskType",
            });
            CheckTableColumns("EventOptions", new[]
            {
                "rowId", "eventDefId", "optionId", "text", "resultText", "affects", "effectId",
            });
            CheckTableColumns("Effects", new[] { "effectId" });
            CheckTableColumns("EffectOps", new[]
            {
                "rowId", "effectId", "scope", "statKey", "op", "value", "min", "max",
            });
            CheckTableColumns("EventTriggers", new[]
            {
                "rowId", "eventDefId", "taskType", "onlyAffectOriginTask", "minDay", "maxDay",
                "requiresNodeId", "requiresSecured", "minLocalPanic",
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

            if (IsRowIdOptionalForKey(tableName, table))
            {
                normalizedColumns.Add(NormalizeColumnName("rowId"));
            }

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

        private static bool IsRowIdOptionalForKey(string tableName, GameDataTable table)
        {
            if (table?.columns == null || table.columns.Count == 0) return false;
            if (!string.Equals(tableName, "EventOptions", StringComparison.Ordinal) &&
                !string.Equals(tableName, "EffectOps", StringComparison.Ordinal) &&
                !string.Equals(tableName, "EventTriggers", StringComparison.Ordinal))
            {
                return false;
            }

            var firstColumn = table.columns[0]?.name;
            return string.Equals(NormalizeColumnName(firstColumn), "key", StringComparison.Ordinal);
        }

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
                RowId = string.IsNullOrEmpty(row.rowId) ? row.eventDefId : row.rowId,
                EventDefId = row.eventDefId,
                MinDay = row.minDay,
                MaxDay = row.maxDay,
                RequiresNodeId = NormalizeRequirement(row.requiresNodeId),
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

        private string NormalizeRequirement(string raw)
            => string.IsNullOrWhiteSpace(raw) ? "Any" : raw;

        private string GetEventTaskTypeAlias(Dictionary<string, object> row)
        {
            var value = GetRowString(row, "requiresTaskType");
            if (!string.IsNullOrEmpty(value)) return value;
            return GetRowString(row, "requirestaskType");
        }

        private void WarnOnMissingTriggerRequiresNodeId()
        {
            if (_warnedMissingTriggerRequiresNodeId) return;
            if (TableHasColumn("EventTriggers", "requiresNodeId")) return;
            _warnedMissingTriggerRequiresNodeId = true;
            Debug.LogWarning("[DataWarn] missing trigger field requiresNodeId; defaulting to Any");
        }

        private void WarnOnMissingEventRequirements()
        {
            if (!_warnedMissingEventRequiresAnomalyId && !TableHasColumn("Events", "requiresAnomalyId"))
            {
                _warnedMissingEventRequiresAnomalyId = true;
                Debug.LogWarning("[DataWarn] missing event field requiresAnomalyId; defaulting to Any");
            }

            if (!_warnedMissingEventRequiresTaskType &&
                !TableHasColumn("Events", "requiresTaskType") &&
                !TableHasColumn("Events", "requirestaskType"))
            {
                _warnedMissingEventRequiresTaskType = true;
                Debug.LogWarning("[DataWarn] missing event field requiresTaskType; defaulting to Any");
            }
        }

        private void WarnOnDeprecatedTriggerFields()
        {
            if (!_warnedDeprecatedTriggerNodeTags &&
                (TableHasColumn("EventTriggers", "requiresNodeTagsAny") || TableHasColumn("EventTriggers", "requiresNodeTagsAll")))
            {
                _warnedDeprecatedTriggerNodeTags = true;
                Debug.LogWarning("[DataWarn] deprecated trigger field requiresNodeTagsAny/All ignored");
            }

            if (!_warnedDeprecatedTriggerAnomalyTags && TableHasColumn("EventTriggers", "requiresAnomalyTagsAny"))
            {
                _warnedDeprecatedTriggerAnomalyTags = true;
                Debug.LogWarning("[DataWarn] deprecated trigger field requiresAnomalyTagsAny ignored");
            }
        }

        private bool TableHasColumn(string tableName, string columnName)
        {
            if (Tables == null || string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(columnName)) return false;
            if (!Tables.TryGetTable(tableName, out var table) || table?.columns == null) return false;
            return table.columns.Any(col => string.Equals(col?.name, columnName, StringComparison.Ordinal));
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

        public TaskDef GetTaskDefById(string taskDefId)
            => !string.IsNullOrEmpty(taskDefId) && TaskDefsById.TryGetValue(taskDefId, out var def) ? def : null;

        public bool TryGetTaskDefForType(TaskType type, out TaskDef def)
        {
            if (TaskDefsByType.TryGetValue(type, out def)) return true;
            WarnMissingTaskDef(type);
            def = null;
            return false;
        }

        public int GetTaskBaseDaysWithWarn(TaskType type, int fallback)
        {
            return TryGetTaskDefForType(type, out var def) && def.baseDays > 0 ? def.baseDays : fallback;
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

        private void WarnMissingTaskDef(TaskType type)
        {
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
