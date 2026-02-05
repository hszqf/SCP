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

    public sealed class DataRegistry
    {
        private const string GameDataFileName = "game_data.json";
        private const string RequirementAny = "ANY";
        private const string RandomDailySource = "RandomDaily";

        private static DataRegistry _instance;
        public static DataRegistry Instance => _instance ??= LoadFromStreamingAssets();

        private readonly HashSet<TaskType> _taskDefMissingWarned = new();
        private bool _warnedMissingEventRequiresNodeId;
        private bool _warnedMissingEventRequiresAnomalyId;
        private bool _warnedMissingEventRequiresTaskType;
        private bool _warnedMissingEventSource;
        private bool _warnedMissingEventP;
        private bool _warnedMissingEventMinDay;
        private bool _warnedMissingEventMaxDay;
        private bool _warnedMissingEventCd;
        private bool _warnedMissingEventLimitNum;
        private bool _warnedMissingNewsRequiresNodeId;
        private bool _warnedMissingNewsRequiresAnomalyId;
        private bool _warnedMissingNewsSource;
        private bool _warnedMissingNewsP;
        private bool _warnedMissingNewsWeight;
        private bool _warnedMissingNewsMinDay;
        private bool _warnedMissingNewsMaxDay;
        private bool _warnedMissingNewsCd;
        private bool _warnedMissingNewsLimitNum;
        private bool _hasTaskDefsTable;

        public GameDataRoot Root { get; private set; }
        public Dictionary<string, NodeDef> NodesById { get; private set; } = new();
        public Dictionary<string, AnomalyDef> AnomaliesById { get; private set; } = new();
        public Dictionary<TaskType, TaskDef> TaskDefsByType { get; private set; } = new();
        public Dictionary<string, TaskDef> TaskDefsById { get; private set; } = new();
        public Dictionary<string, EventDef> EventsById { get; private set; } = new();
        public Dictionary<string, List<EventOptionDef>> OptionsByEventId { get; private set; } = new();
        public Dictionary<string, Dictionary<string, EventOptionDef>> OptionsByEventAndId { get; private set; } = new();
        public Dictionary<string, NewsDef> NewsDefsById { get; private set; } = new();
        public List<NewsDef> NewsDefs { get; private set; } = new();
        public Dictionary<string, EffectDef> EffectsById { get; private set; } = new();
        public Dictionary<string, List<EffectOp>> EffectOpsByEffectId { get; private set; } = new();
        public Dictionary<string, BalanceValue> Balance { get; private set; } = new();
        public TableRegistry Tables { get; private set; } = new();
        public List<AnomaliesGenDef> AnomaliesGen { get; private set; } = new();
        public Dictionary<string, MediaProfileDef> MediaProfilesById { get; private set; } = new();
        public List<MediaProfileDef> MediaProfiles { get; private set; } = new();
        public Dictionary<string, FactTemplateDef> FactTemplatesById { get; private set; } = new();
        public List<FactTemplateDef> FactTemplates { get; private set; } = new();
        public Dictionary<string, string> FactTypesById { get; private set; } = new();
        public List<string> FactTypes { get; private set; } = new();

        public int LocalPanicHighThreshold { get; private set; } = 6;
        public double RandomEventBaseProb { get; private set; } = 0.15d;
        public int DefaultAutoResolveAfterDays { get; private set; } = 0;
        public IgnoreApplyMode DefaultIgnoreApplyMode { get; private set; } = IgnoreApplyMode.ApplyDailyKeep;

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

            NodesById = new Dictionary<string, NodeDef>();
            foreach (var row in Tables.GetRows("Nodes"))
            {
                var nodeId = GetRowString(row, "nodeId");
                if (string.IsNullOrEmpty(nodeId)) continue;
                NodesById[nodeId] = new NodeDef
                {
                    nodeId = nodeId,
                    name = GetRowString(row, "name"),
                    startPopulation = GetRowInt(row, "startPopulation"),
                    unlocked = GetRowInt(row, "unlocked", 1),
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
                    baseThreat = GetRowInt(row, "baseThreat"),
                    baseDays = GetRowInt(row, "baseDays"),
                    actPeopleKill = GetRowInt(row, "actPeopleKill"),
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

            if (AnomaliesById.TryGetValue("AN_001", out var sampleAnomaly))
            {
                Debug.Log($"[DataSample] AN_001 invHp={sampleAnomaly.invHp} invSan={sampleAnomaly.invSan} conHp={sampleAnomaly.conHp} conSan={sampleAnomaly.conSan} manHp={sampleAnomaly.manHp} manSan={sampleAnomaly.manSan}");
                Debug.Log($"[DataSample] AN_001 invReq={FormatIntArray(sampleAnomaly.invReq)} conReq={FormatIntArray(sampleAnomaly.conReq)} manReq={FormatIntArray(sampleAnomaly.manReq)}");
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
                    agentSlotsMin = GetRowInt(row, "agentSlotsMin"),
                    agentSlotsMax = GetRowInt(row, "agentSlotsMax"),
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
                EventsById[eventDefId] = new EventDef
                {
                    eventDefId = eventDefId,
                    source = GetEventStringWithDefault(row, "source", "RandomDaily", ref _warnedMissingEventSource, "RandomDaily"),
                    weight = GetRowInt(row, "weight"),
                    title = GetRowString(row, "title"),
                    desc = GetRowString(row, "desc"),
                    blockPolicy = GetRowString(row, "blockPolicy"),
                    defaultAffects = GetRowStringList(row, "defaultAffects"),
                    autoResolveAfterDays = GetRowInt(row, "autoResolveAfterDays"),
                    ignoreApplyMode = GetRowString(row, "ignoreApplyMode"),
                    ignoreEffectId = GetRowString(row, "ignoreEffectId"),
                    requiresNodeId = NormalizeRequirement(GetEventStringWithDefault(row, "requiresNodeId", RequirementAny, ref _warnedMissingEventRequiresNodeId, "ANY")),
                    requiresAnomalyId = NormalizeRequirement(GetEventStringWithDefault(row, "requiresAnomalyId", RequirementAny, ref _warnedMissingEventRequiresAnomalyId, "ANY")),
                    requiresTaskType = NormalizeRequirement(GetEventStringWithDefault(row, "requiresTaskType", RequirementAny, ref _warnedMissingEventRequiresTaskType, "ANY")),
                    p = GetEventFloatWithDefault(row, "p", 0f, ref _warnedMissingEventP),
                    minDay = GetEventIntWithDefault(row, "minDay", 0, ref _warnedMissingEventMinDay),
                    maxDay = GetEventIntWithDefault(row, "maxDay", 0, ref _warnedMissingEventMaxDay),
                    cd = GetEventIntWithDefault(row, "CD", 0, ref _warnedMissingEventCd),
                    limitNum = GetEventIntWithDefault(row, "limitNum", 0, ref _warnedMissingEventLimitNum),
                };
            }

            NewsDefsById = new Dictionary<string, NewsDef>();
            NewsDefs = new List<NewsDef>();
            foreach (var row in Tables.GetRows("NewsDefs"))
            {
                var newsDefId = GetRowString(row, "newsDefId");
                if (string.IsNullOrEmpty(newsDefId)) continue;
                var def = new NewsDef
                {
                    newsDefId = newsDefId,
                    source = GetNewsStringWithDefault(row, "source", "RandomDaily", ref _warnedMissingNewsSource, "RandomDaily"),
                    weight = GetNewsIntWithDefault(row, "weight", 1, ref _warnedMissingNewsWeight),
                    p = Mathf.Clamp01(GetNewsFloatWithDefault(row, "p", 1f, ref _warnedMissingNewsP)),
                    minDay = GetNewsIntWithDefault(row, "minDay", 0, ref _warnedMissingNewsMinDay),
                    maxDay = GetNewsIntWithDefault(row, "maxDay", 0, ref _warnedMissingNewsMaxDay),
                    cd = GetNewsIntWithDefault(row, "CD", 0, ref _warnedMissingNewsCd),
                    limitNum = GetNewsIntWithDefault(row, "limitNum", 0, ref _warnedMissingNewsLimitNum),
                    requiresNodeId = NormalizeRequirement(GetNewsStringWithDefault(row, "requiresNodeId", RequirementAny, ref _warnedMissingNewsRequiresNodeId, "ANY")),
                    requiresAnomalyId = NormalizeRequirement(GetNewsStringWithDefault(row, "requiresAnomalyId", RequirementAny, ref _warnedMissingNewsRequiresAnomalyId, "ANY")),
                    title = GetRowString(row, "title"),
                    desc = GetRowString(row, "desc"),
                };
                NewsDefsById[newsDefId] = def;
                NewsDefs.Add(def);
            }

            if (NewsDefsById.Count > 0)
            {
                int randomDailyCount = NewsDefsById.Values.Count(def =>
                    def != null && string.Equals(def.source, RandomDailySource, StringComparison.OrdinalIgnoreCase));
                Debug.Log($"[NewsDefs] rows={NewsDefsById.Count} (source RandomDaily={randomDailyCount})");
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

            LogGroupIndexSummary("EventOptions", "eventDefId", OptionsByEventId);
            LogGroupIndexSummary("EffectOps", "effectId", EffectOpsByEffectId);

            // Load MediaProfiles
            MediaProfilesById = new Dictionary<string, MediaProfileDef>();
            MediaProfiles = new List<MediaProfileDef>();
            foreach (var row in Tables.GetRows("MediaProfiles"))
            {
                var profileId = GetRowString(row, "profileId");
                if (string.IsNullOrEmpty(profileId)) continue;
                var profile = new MediaProfileDef
                {
                    profileId = profileId,
                    name = GetRowString(row, "name"),
                    tone = GetRowString(row, "tone"),
                    weight = GetRowInt(row, "weight", 1),
                };
                MediaProfilesById[profileId] = profile;
                MediaProfiles.Add(profile);
            }

            // Load FactTemplates
            FactTemplatesById = new Dictionary<string, FactTemplateDef>();
            FactTemplates = new List<FactTemplateDef>();
            foreach (var row in Tables.GetRows("FactTemplates"))
            {
                var templateId = GetRowString(row, "templateId");
                if (string.IsNullOrEmpty(templateId)) continue;
                var template = new FactTemplateDef
                {
                    factType = GetRowString(row, "factType"),
                    mediaProfileId = GetRowString(row, "mediaProfileId"),
                    severityMin = GetRowInt(row, "severityMin", 1),
                    severityMax = GetRowInt(row, "severityMax", 5),
                };
                FactTemplatesById[templateId] = template;
                FactTemplates.Add(template);
            }

            // Load FactTypes
            FactTypesById = new Dictionary<string, string>();
            FactTypes = new List<string>();
            foreach (var row in Tables.GetRows("FactTypes"))
            {
                var typeId = GetRowString(row, "typeId");
                if (string.IsNullOrEmpty(typeId)) continue;
                var description = GetRowString(row, "description");
                FactTypesById[typeId] = description;
                FactTypes.Add(typeId);
            }

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
            int newsCount = GetTableRowCountWithWarn("NewsDefs");
            int effectsCount = GetTableRowCountWithWarn("Effects");
            int opsCount = GetTableRowCountWithWarn("EffectOps");
            int mediaProfilesCount = MediaProfiles?.Count ?? 0;
            int factTemplatesCount = FactTemplates?.Count ?? 0;
            int factTypesCount = FactTypes?.Count ?? 0;

            Debug.Log($"[Data] schema={schema} dataVersion={dataVersion} events={eventsCount} options={optionsCount} news={newsCount} effects={effectsCount} ops={opsCount}");
            Debug.Log($"[Data] mediaProfiles={mediaProfilesCount} factTemplates={factTemplatesCount} factTypes={factTypesCount}");
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
                "nodeId", "name", "startPopulation", "unlocked",
            });
            CheckTableColumns("Anomalies", new[]
            {
                "anomalyId", "name", "class", "baseThreat", "baseDays", "invExp", "conExp", "manExpPerDay", "manNegentropyPerDay",
                "invhpDmg", "invsanDmg", "conhpDmg", "consanDmg", "manhpDmg", "mansanDmg",
                "worldPanicPerDayUncontained", "maintenanceCostPerDay",
            });
            CheckTableColumns("AnomaliesGen", new[]
            {
                "day", "AnomaliesGenNum",
            });
            CheckTableColumns("Events", new[]
            {
                "eventDefId", "source", "weight", "title", "desc", "blockPolicy",
                "defaultAffects", "autoResolveAfterDays", "ignoreApplyMode", "ignoreEffectId",
                "requiresNodeId", "requiresAnomalyId", "requiresTaskType",
                "p", "minDay", "maxDay", "CD", "limitNum",
            });
            CheckTableColumns("NewsDefs", new[]
            {
                "newsDefId", "source", "weight", "title", "desc",
                "requiresNodeId", "requiresAnomalyId",
                "p", "minDay", "maxDay", "CD", "limitNum",
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
                !string.Equals(tableName, "EffectOps", StringComparison.Ordinal))
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

        private string GetEventStringWithDefault(Dictionary<string, object> row, string column, string fallback, ref bool warned, string fallbackLabel)
        {
            if (!TableHasColumn("Events", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing event field {column}; defaulting to {fallbackLabel}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceString(raw, out var value) || string.IsNullOrWhiteSpace(value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty event field {column}; defaulting to {fallbackLabel}");
                }
                return fallback;
            }

            return value;
        }

        private string GetNewsStringWithDefault(Dictionary<string, object> row, string column, string fallback, ref bool warned, string fallbackLabel)
        {
            if (!TableHasColumn("NewsDefs", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing news field {column}; defaulting to {fallbackLabel}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceString(raw, out var value) || string.IsNullOrWhiteSpace(value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty news field {column}; defaulting to {fallbackLabel}");
                }
                return fallback;
            }

            return value;
        }

        private static int GetRowInt(Dictionary<string, object> row, string column, int fallback = 0)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return fallback;
            return TableRegistry.TryCoerceInt(raw, out var value) ? value : fallback;
        }

        private int GetEventIntWithDefault(Dictionary<string, object> row, string column, int fallback, ref bool warned)
        {
            if (!TableHasColumn("Events", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing event field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceInt(raw, out var value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty event field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            return value;
        }

        private int GetNewsIntWithDefault(Dictionary<string, object> row, string column, int fallback, ref bool warned)
        {
            if (!TableHasColumn("NewsDefs", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing news field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceInt(raw, out var value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty news field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            return value;
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

        private float GetEventFloatWithDefault(Dictionary<string, object> row, string column, float fallback, ref bool warned)
        {
            if (!TableHasColumn("Events", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing event field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceFloat(raw, out var value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty event field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            return value;
        }

        private float GetNewsFloatWithDefault(Dictionary<string, object> row, string column, float fallback, ref bool warned)
        {
            if (!TableHasColumn("NewsDefs", column))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] missing news field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            if (row == null || !row.TryGetValue(column, out var raw) || !TableRegistry.TryCoerceFloat(raw, out var value))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[DataWarn] empty news field {column}; defaulting to {fallback}");
                }
                return fallback;
            }

            return value;
        }

        private static float? GetRowFloatNullable(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return null;
            return TableRegistry.TryCoerceFloat(raw, out var value) ? value : null;
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

        private string NormalizeRequirement(string raw)
            => string.IsNullOrWhiteSpace(raw) ? RequirementAny : raw.Trim();

        private bool TableHasColumn(string tableName, string columnName)
        {
            if (Tables == null || string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(columnName)) return false;
            if (!Tables.TryGetTable(tableName, out var table) || table?.columns == null) return false;
            return table.columns.Any(col => string.Equals(col?.name, columnName, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryGetEvent(string eventDefId, out EventDef def)
            => EventsById.TryGetValue(eventDefId, out def);

        public bool TryGetOption(string eventDefId, string optionId, out EventOptionDef option)
        {
            option = null;
            if (!OptionsByEventAndId.TryGetValue(eventDefId, out var dict)) return false;
            return dict.TryGetValue(optionId, out option);
        }

        public NewsDef GetNewsDefById(string newsDefId)
            => !string.IsNullOrEmpty(newsDefId) && NewsDefsById.TryGetValue(newsDefId, out var def) ? def : null;

        public bool TryGetTaskDef(TaskType type, out TaskDef def)
            => TaskDefsByType.TryGetValue(type, out def);

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
