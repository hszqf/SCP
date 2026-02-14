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
            ValidatePrimaryKeys(registry, errors);

            if (errors.Count > 0)
            {
                var message = "[GameDataValidator] Validation failed:\n - " + string.Join("\n - ", errors);
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            int effectsCount = GetTableRowCount(registry, "Effects");
            int opsCount = GetTableRowCount(registry, "EffectOps");
            Debug.Log($"[GameDataValidator] Validation passed. effects={effectsCount} ops={opsCount}");
        }

        private static void ValidateEnums(DataRegistry registry, List<string> errors)
        {
            var opRows = GetTableRows(registry, "EffectOps");
            for (int i = 0; i < opRows.Count; i++)
            {
                var rowData = opRows[i];
                if (rowData == null) continue;
                int rowIndex = i + 1;


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
