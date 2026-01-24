using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core;
using Newtonsoft.Json;
using UnityEngine;

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
        public Dictionary<string, List<EventOptionDef>> OptionsByEvent { get; private set; } = new();
        public Dictionary<string, Dictionary<string, EventOptionDef>> OptionsByEventAndId { get; private set; } = new();
        public Dictionary<string, EffectDef> EffectsById { get; private set; } = new();
        public Dictionary<string, List<EffectOp>> EffectOpsByEffectId { get; private set; } = new();
        public Dictionary<string, List<EventTrigger>> TriggersByEventId { get; private set; } = new();
        public Dictionary<string, BalanceValue> Balance { get; private set; } = new();

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
            string path = Path.Combine(Application.streamingAssetsPath, GameDataFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[DataRegistry] Missing game data at: {path}");
                Root = new GameDataRoot();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
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
                Root = new GameDataRoot();
            }

            BuildIndexes();
            GameDataValidator.ValidateOrThrow(this);
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

            OptionsByEvent = new Dictionary<string, List<EventOptionDef>>();
            OptionsByEventAndId = new Dictionary<string, Dictionary<string, EventOptionDef>>();
            foreach (var option in Root.eventOptions ?? new List<EventOptionDef>())
            {
                if (option == null || string.IsNullOrEmpty(option.eventDefId) || string.IsNullOrEmpty(option.optionId)) continue;
                if (!OptionsByEvent.TryGetValue(option.eventDefId, out var list))
                {
                    list = new List<EventOptionDef>();
                    OptionsByEvent[option.eventDefId] = list;
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

            TriggersByEventId = new Dictionary<string, List<EventTrigger>>();
            foreach (var row in Root.eventTriggers ?? new List<EventTriggerRow>())
            {
                if (row == null || string.IsNullOrEmpty(row.eventDefId)) continue;
                if (!TryParseTrigger(row, out var trigger)) continue;
                if (!TriggersByEventId.TryGetValue(row.eventDefId, out var list))
                {
                    list = new List<EventTrigger>();
                    TriggersByEventId[row.eventDefId] = list;
                }
                list.Add(trigger);
            }

            LocalPanicHighThreshold = GetBalanceInt("LocalPanicHighThreshold", LocalPanicHighThreshold);
            RandomEventBaseProb = GetBalanceFloat("RandomEventBaseProb", (float)RandomEventBaseProb);
            DefaultAutoResolveAfterDays = GetBalanceInt("DefaultAutoResolveAfterDays", DefaultAutoResolveAfterDays);

            var defaultIgnoreApplyModeRaw = GetBalanceString("DefaultIgnoreApplyMode", DefaultIgnoreApplyMode.ToString());
            if (TryParseIgnoreApplyMode(defaultIgnoreApplyModeRaw, out var parsedMode, out _))
                DefaultIgnoreApplyMode = parsedMode;
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
            if (!Balance.TryGetValue(key, out var val)) return fallback;
            if (val == null || string.IsNullOrEmpty(val.value)) return fallback;
            return int.TryParse(val.value, out var parsed) ? parsed : fallback;
        }

        private float GetBalanceFloat(string key, float fallback)
        {
            if (!Balance.TryGetValue(key, out var val)) return fallback;
            if (val == null || string.IsNullOrEmpty(val.value)) return fallback;
            return float.TryParse(val.value, out var parsed) ? parsed : fallback;
        }

        private string GetBalanceString(string key, string fallback)
        {
            if (!Balance.TryGetValue(key, out var val)) return fallback;
            return string.IsNullOrEmpty(val?.value) ? fallback : val.value;
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
