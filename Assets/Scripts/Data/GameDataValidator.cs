using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using UnityEngine;

namespace Data
{
    public static class GameDataValidator
    {
        public static void ValidateOrThrow(DataRegistry registry)
        {
            var errors = new List<string>();
            if (registry == null)
            {
                throw new InvalidOperationException("[GameDataValidator] Registry is null.");
            }

            ValidateEnums(registry, errors);
            ValidateForeignKeys(registry, errors);
            ValidateTriggers(registry, errors);
            ValidateBlockOriginTaskLogic(registry, errors);

            if (errors.Count > 0)
            {
                var message = "[GameDataValidator] Validation failed:\n - " + string.Join("\n - ", errors);
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            Debug.Log($"[GameDataValidator] Validation passed. events={registry.EventsById.Count} effects={registry.EffectsById.Count}");
        }

        private static void ValidateEnums(DataRegistry registry, List<string> errors)
        {
            var events = registry.Root.events ?? new List<EventDef>();
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null) continue;
                int row = i + 1;

                if (!DataRegistry.TryParseEventSource(ev.source, out _, out var sourceError))
                    errors.Add(FormatCellError("Events", row, "source", ev.source, sourceError));
                if (!DataRegistry.TryParseCauseType(ev.causeType, out _, out var causeError))
                    errors.Add(FormatCellError("Events", row, "causeType", ev.causeType, causeError));
                if (!DataRegistry.TryParseBlockPolicy(ev.blockPolicy, out _, out var blockError))
                    errors.Add(FormatCellError("Events", row, "blockPolicy", ev.blockPolicy, blockError));

                var affectsRaw = ev.defaultAffects ?? new List<string>();
                if (!DataRegistry.TryParseAffectScopes(affectsRaw, out _, out var affectsError))
                    errors.Add(FormatCellError("Events", row, "defaultAffects", string.Join(";", affectsRaw), affectsError));

                if (!string.IsNullOrEmpty(ev.ignoreApplyMode) &&
                    !DataRegistry.TryParseIgnoreApplyMode(ev.ignoreApplyMode, out _, out var modeError))
                    errors.Add(FormatCellError("Events", row, "ignoreApplyMode", ev.ignoreApplyMode, modeError));
            }

            var options = registry.Root.eventOptions ?? new List<EventOptionDef>();
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt == null) continue;
                int row = i + 1;
                if (opt.affects != null && opt.affects.Count > 0)
                {
                    if (!DataRegistry.TryParseAffectScopes(opt.affects, out _, out var affectsError))
                        errors.Add(FormatCellError("EventOptions", row, "affects", string.Join(";", opt.affects), affectsError));
                }
            }

            var opRows = registry.Root.effectOps ?? new List<EffectOpRow>();
            for (int i = 0; i < opRows.Count; i++)
            {
                var row = opRows[i];
                if (row == null) continue;
                int rowIndex = i + 1;
                if (!DataRegistry.TryParseAffectScopes(row.scope, out _, out var scopeError))
                    errors.Add(FormatCellError("EffectOps", rowIndex, "scope", row.scope, scopeError));
                if (!DataRegistry.TryParseEffectOpType(row.op, out _, out var opError))
                    errors.Add(FormatCellError("EffectOps", rowIndex, "op", row.op, opError));
            }

            var triggers = registry.Root.eventTriggers ?? new List<EventTriggerRow>();
            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger == null || string.IsNullOrEmpty(trigger.taskType)) continue;
                int row = i + 1;
                if (!DataRegistry.TryParseTaskType(trigger.taskType, out _, out var taskTypeError))
                    errors.Add(FormatCellError("EventTriggers", row, "taskType", trigger.taskType, taskTypeError));
            }
        }

        private static void ValidateForeignKeys(DataRegistry registry, List<string> errors)
        {
            var options = registry.Root.eventOptions ?? new List<EventOptionDef>();
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt == null) continue;
                int row = i + 1;
                if (string.IsNullOrEmpty(opt.eventDefId) || !registry.EventsById.ContainsKey(opt.eventDefId))
                {
                    errors.Add(FormatCellError("EventOptions", row, "eventDefId", opt.eventDefId, "existing Events.eventDefId"));
                }

                if (!string.IsNullOrEmpty(opt.effectId) && !registry.EffectsById.ContainsKey(opt.effectId))
                {
                    errors.Add(FormatCellError("EventOptions", row, "effectId", opt.effectId, "existing Effects.effectId"));
                }
            }

            var events = registry.Root.events ?? new List<EventDef>();
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null) continue;
                int row = i + 1;
                if (!string.IsNullOrEmpty(ev.ignoreEffectId) && !registry.EffectsById.ContainsKey(ev.ignoreEffectId))
                {
                    errors.Add(FormatCellError("Events", row, "ignoreEffectId", ev.ignoreEffectId, "existing Effects.effectId"));
                }
            }

            var effectOps = registry.Root.effectOps ?? new List<EffectOpRow>();
            for (int i = 0; i < effectOps.Count; i++)
            {
                var row = effectOps[i];
                if (row == null) continue;
                int rowIndex = i + 1;
                if (string.IsNullOrEmpty(row.effectId) || !registry.EffectsById.ContainsKey(row.effectId))
                {
                    errors.Add(FormatCellError("EffectOps", rowIndex, "effectId", row.effectId, "existing Effects.effectId"));
                }
            }

            var triggers = registry.Root.eventTriggers ?? new List<EventTriggerRow>();
            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger == null) continue;
                int row = i + 1;
                if (string.IsNullOrEmpty(trigger.eventDefId) || !registry.EventsById.ContainsKey(trigger.eventDefId))
                {
                    errors.Add(FormatCellError("EventTriggers", row, "eventDefId", trigger.eventDefId, "existing Events.eventDefId"));
                }
            }

            var optionRows = registry.Root.eventOptions ?? new List<EventOptionDef>();
            var duplicates = optionRows
                .Select((option, index) => new { option, row = index + 1 })
                .Where(entry => entry.option != null)
                .GroupBy(entry => (entry.option.eventDefId, entry.option.optionId))
                .Where(group => group.Count() > 1)
                .Select(group =>
                {
                    var rows = string.Join(",", group.Select(entry => entry.row));
                    var value = $"{group.Key.eventDefId}/{group.Key.optionId}";
                    return FormatCellError("EventOptions", rows, "eventDefId+optionId", value, "unique combination");
                })
                .ToList();
            if (duplicates.Count > 0) errors.AddRange(duplicates);
        }

        private static void ValidateTriggers(DataRegistry registry, List<string> errors)
        {
            var triggers = registry.Root.eventTriggers ?? new List<EventTriggerRow>();
            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger == null) continue;
                int row = i + 1;
                if (trigger.minDay.HasValue && trigger.maxDay.HasValue && trigger.minDay.Value > trigger.maxDay.Value)
                {
                    errors.Add(FormatCellError("EventTriggers", row, "minDay/maxDay", $"{trigger.minDay}>{trigger.maxDay}", "minDay <= maxDay"));
                }

                if (trigger.minLocalPanic.HasValue && trigger.minLocalPanic.Value < 0)
                {
                    errors.Add(FormatCellError("EventTriggers", row, "minLocalPanic", trigger.minLocalPanic.Value.ToString(), ">= 0"));
                }
            }
        }

        private static void ValidateBlockOriginTaskLogic(DataRegistry registry, List<string> errors)
        {
            var events = registry.Root.events ?? new List<EventDef>();
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null || string.IsNullOrEmpty(ev.eventDefId)) continue;
                int row = i + 1;
                if (!DataRegistry.TryParseBlockPolicy(ev.blockPolicy, out var policy, out _)) continue;
                if (policy != BlockPolicy.BlockOriginTask) continue;

                var relatedEffectIds = new List<string>();
                if (!string.IsNullOrEmpty(ev.ignoreEffectId)) relatedEffectIds.Add(ev.ignoreEffectId);
                if (registry.OptionsByEventId.TryGetValue(ev.eventDefId, out var options))
                {
                    relatedEffectIds.AddRange(options.Where(o => !string.IsNullOrEmpty(o.effectId)).Select(o => o.effectId));
                }

                bool hasOriginTaskProgressAdd = relatedEffectIds
                    .Distinct()
                    .SelectMany(id => registry.EffectOpsByEffectId.TryGetValue(id, out var ops) ? ops : new List<EffectOp>())
                    .Any(op => op.Scope.Kind == AffectScopeKind.OriginTask &&
                               string.Equals(op.StatKey, "TaskProgressDelta", StringComparison.OrdinalIgnoreCase) &&
                               op.Op == EffectOpType.Add);

                if (!hasOriginTaskProgressAdd)
                {
                    errors.Add(FormatCellError("Events", row, "blockPolicy", ev.blockPolicy, "BlockOriginTask with OriginTask TaskProgressDelta Add op"));
                }
            }
        }

        private static string FormatCellError(string sheet, int row, string col, string value, string expected)
        {
            return $"sheet={sheet} row={row} col={col} value={value ?? "<null>"} expected={expected}";
        }

        private static string FormatCellError(string sheet, string row, string col, string value, string expected)
        {
            return $"sheet={sheet} row={row} col={col} value={value ?? "<null>"} expected={expected}";
        }
    }
}
