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
            ValidatePrimaryKeys(registry, errors);
            ValidateBlockOriginTaskLogic(registry, errors);

            if (errors.Count > 0)
            {
                var message = "[GameDataValidator] Validation failed:\n - " + string.Join("\n - ", errors);
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            int eventsCount = GetTableRowCount(registry, "Events");
            int optionsCount = GetTableRowCount(registry, "EventOptions");
            int newsCount = GetTableRowCount(registry, "NewsDefs");
            int effectsCount = GetTableRowCount(registry, "Effects");
            int opsCount = GetTableRowCount(registry, "EffectOps");
            Debug.Log($"[GameDataValidator] Validation passed. events={eventsCount} options={optionsCount} news={newsCount} effects={effectsCount} ops={opsCount}");
        }

        private static void ValidateEnums(DataRegistry registry, List<string> errors)
        {
            var events = GetTableRows(registry, "Events");
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null) continue;
                int row = i + 1;

                var source = GetRowString(ev, "source");
                var blockPolicy = GetRowString(ev, "blockPolicy");

                if (!string.IsNullOrEmpty(source) && !string.Equals(source, "RandomDaily", StringComparison.OrdinalIgnoreCase))
                    errors.Add(FormatCellError("Events", row, "source", source, "RandomDaily"));
                if (!DataRegistry.TryParseBlockPolicy(blockPolicy, out _, out var blockError))
                    errors.Add(FormatCellError("Events", row, "blockPolicy", blockPolicy, blockError));

                var affectsRaw = GetRowStringList(ev, "defaultAffects");
                if (!DataRegistry.TryParseAffectScopes(affectsRaw, out _, out var affectsError))
                    errors.Add(FormatCellError("Events", row, "defaultAffects", string.Join(";", affectsRaw), affectsError));

                var ignoreApplyMode = GetRowString(ev, "ignoreApplyMode");
                if (!DataRegistry.TryParseIgnoreApplyMode(ignoreApplyMode, out _, out var modeError))
                    errors.Add(FormatCellError("Events", row, "ignoreApplyMode", ignoreApplyMode, modeError));
            }

            var options = GetTableRows(registry, "EventOptions");
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt == null) continue;
                int row = i + 1;
                var affects = GetRowStringList(opt, "affects");
                if (!DataRegistry.TryParseAffectScopes(affects, out _, out var affectsError))
                    errors.Add(FormatCellError("EventOptions", row, "affects", string.Join(";", affects), affectsError));
            }

            var newsDefs = GetTableRows(registry, "NewsDefs");
            for (int i = 0; i < newsDefs.Count; i++)
            {
                var news = newsDefs[i];
                if (news == null) continue;
                int row = i + 1;
                var source = GetRowString(news, "source");
                if (!string.IsNullOrEmpty(source) && !string.Equals(source, "RandomDaily", StringComparison.OrdinalIgnoreCase))
                    errors.Add(FormatCellError("NewsDefs", row, "source", source, "RandomDaily"));
            }

            var opRows = GetTableRows(registry, "EffectOps");
            for (int i = 0; i < opRows.Count; i++)
            {
                var rowData = opRows[i];
                if (rowData == null) continue;
                int rowIndex = i + 1;
                var scope = GetRowString(rowData, "scope");
                var op = GetRowString(rowData, "op");
                if (!DataRegistry.TryParseAffectScopes(scope, out _, out var scopeError))
                    errors.Add(FormatCellError("EffectOps", rowIndex, "scope", scope, scopeError));
                if (!DataRegistry.TryParseEffectOpType(op, out _, out var opError))
                    errors.Add(FormatCellError("EffectOps", rowIndex, "op", op, opError));
            }

        }

        private static void ValidateForeignKeys(DataRegistry registry, List<string> errors)
        {
            var eventIds = new HashSet<string>(registry.EventsById.Keys, StringComparer.Ordinal);
            var effectIds = new HashSet<string>(registry.EffectsById.Keys, StringComparer.Ordinal);

            var options = GetTableRows(registry, "EventOptions");
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt == null) continue;
                int row = i + 1;
                var eventDefId = GetRowString(opt, "eventDefId");
                var effectId = GetRowString(opt, "effectId");
                if (string.IsNullOrEmpty(eventDefId) || !eventIds.Contains(eventDefId))
                {
                    errors.Add(FormatCellError("EventOptions", row, "eventDefId", eventDefId, "existing Events.eventDefId"));
                }

                if (!string.IsNullOrEmpty(effectId) && !effectIds.Contains(effectId))
                {
                    errors.Add(FormatCellError("EventOptions", row, "effectId", effectId, "existing Effects.effectId"));
                }
            }

            var events = GetTableRows(registry, "Events");
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null) continue;
                int row = i + 1;
                var ignoreEffectId = GetRowString(ev, "ignoreEffectId");
                if (!string.IsNullOrEmpty(ignoreEffectId) && !effectIds.Contains(ignoreEffectId))
                {
                    errors.Add(FormatCellError("Events", row, "ignoreEffectId", ignoreEffectId, "existing Effects.effectId"));
                }
            }

            var effectOps = GetTableRows(registry, "EffectOps");
            for (int i = 0; i < effectOps.Count; i++)
            {
                var rowData = effectOps[i];
                if (rowData == null) continue;
                int rowIndex = i + 1;
                var effectId = GetRowString(rowData, "effectId");
                if (string.IsNullOrEmpty(effectId) || !effectIds.Contains(effectId))
                {
                    errors.Add(FormatCellError("EffectOps", rowIndex, "effectId", effectId, "existing Effects.effectId"));
                }
            }

            var optionRows = GetTableRows(registry, "EventOptions");
            var duplicates = optionRows
                .Select((option, index) => new { option, row = index + 1 })
                .Where(entry => entry.option != null)
                .GroupBy(entry => (eventDefId: GetRowString(entry.option, "eventDefId"), optionId: GetRowString(entry.option, "optionId")))
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

        private static void ValidateBlockOriginTaskLogic(DataRegistry registry, List<string> errors)
        {
            var events = GetTableRows(registry, "Events");
            for (int i = 0; i < events.Count; i++)
            {
                var rowData = events[i];
                if (rowData == null) continue;
                int row = i + 1;
                var eventDefId = GetRowString(rowData, "eventDefId");
                var blockPolicy = GetRowString(rowData, "blockPolicy");
                if (string.IsNullOrEmpty(eventDefId)) continue;
                if (!DataRegistry.TryParseBlockPolicy(blockPolicy, out var policy, out _)) continue;
                if (policy != BlockPolicy.BlockOriginTask) continue;

                var relatedEffectIds = new List<string>();
                var ignoreEffectId = GetRowString(rowData, "ignoreEffectId");
                if (!string.IsNullOrEmpty(ignoreEffectId)) relatedEffectIds.Add(ignoreEffectId);
                if (registry.OptionsByEventId.TryGetValue(eventDefId, out var options))
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
                    errors.Add(FormatCellError("Events", row, "blockPolicy", blockPolicy, "BlockOriginTask with OriginTask TaskProgressDelta Add op"));
                }
            }
        }

        private static void ValidatePrimaryKeys(DataRegistry registry, List<string> errors)
        {
            if (registry?.Tables == null || registry.Root?.tables == null) return;
            foreach (var entry in registry.Root.tables)
            {
                var tableName = entry.Key;
                var table = entry.Value;
                if (table == null || string.IsNullOrEmpty(table.idField)) continue;
                if (table.rows == null) continue;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < table.rows.Count; i++)
                {
                    var row = table.rows[i];
                    var rowIndex = i + 1;
                    var rowId = GetRowString(row, table.idField);
                    if (string.IsNullOrEmpty(rowId))
                    {
                        errors.Add(FormatCellError(tableName, rowIndex, table.idField, rowId, "non-empty idField"));
                        continue;
                    }

                    if (!seen.Add(rowId))
                    {
                        errors.Add(FormatCellError(tableName, rowIndex, table.idField, rowId, "unique idField"));
                    }
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

        private static List<Dictionary<string, object>> GetTableRows(DataRegistry registry, string tableName)
        {
            if (registry?.Tables == null) return new List<Dictionary<string, object>>();
            return registry.Tables.GetRows(tableName).ToList();
        }

        private static int GetTableRowCount(DataRegistry registry, string tableName)
        {
            if (registry?.Tables == null) return 0;
            return registry.Tables.GetRows(tableName).Count;
        }

        private static string GetRowString(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return string.Empty;
            return TableRegistry.TryCoerceString(raw, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static List<string> GetRowStringList(Dictionary<string, object> row, string column)
        {
            if (row == null || !row.TryGetValue(column, out var raw)) return new List<string>();
            return TableRegistry.CoerceStringList(raw);
        }
    }
}
