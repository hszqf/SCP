#!/usr/bin/env python3
"""Export game_data.xlsx into Unity-friendly game_data.json."""
from __future__ import annotations

import argparse
import json
import logging
import re
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from openpyxl import load_workbook

LIST_SPLIT_PATTERN = re.compile(r"[;,，]")
LOGGER_NAME = "xlsx_to_json"
LOG_LEVELS = {
    "DEBUG": logging.DEBUG,
    "INFO": logging.INFO,
    "WARN": logging.WARN,
    "WARNING": logging.WARNING,
    "ERROR": logging.ERROR,
}

REQUIRED_COLUMNS: dict[str, set[str]] = {
    "Meta": {"schemaVersion", "dataVersion", "comment"},
    "Balance": {"key", "value", "type", "comment"},
    "Nodes": {"nodeId", "name", "tags", "startLocalPanic", "startPopulation", "startAnomalyIds"},
    "Anomalies": {
        "anomalyId",
        "name",
        "class",
        "tags",
        "baseThreat",
        "investigateDifficulty",
        "containDifficulty",
        "manageRisk",
    },
    "TaskDefs": {
        "taskDefId",
        "taskType",
        "name",
        "baseDays",
        "progressPerDay",
        "agentSlotsMin",
        "agentSlotsMax",
        "yieldKey",
        "yieldPerDay",
    },
    "Events": {
        "eventDefId",
        "source",
        "causeType",
        "weight",
        "title",
        "desc",
        "blockPolicy",
        "defaultAffects",
        "autoResolveAfterDays",
        "ignoreApplyMode",
        "ignoreEffectId",
    },
    "EventOptions": {"eventDefId", "optionId", "text", "resultText", "affects", "effectId"},
    "Effects": {"effectId", "comment"},
    "EffectOps": {"effectId", "scope", "statKey", "op", "value", "min", "max", "comment"},
    "EventTriggers": {
        "eventDefId",
        "minDay",
        "maxDay",
        "requiresNodeTagsAny",
        "requiresNodeTagsAll",
        "requiresAnomalyTagsAny",
        "requiresSecured",
        "minLocalPanic",
        "taskType",
        "onlyAffectOriginTask",
    },
}

REQUIRED_SHEETS = tuple(REQUIRED_COLUMNS.keys())

TASK_TYPES = {"Investigate", "Contain", "Manage"}
EVENT_SOURCES = {
    "Investigate",
    "Contain",
    "Manage",
    "LocalPanicHigh",
    "Fixed",
    "SecuredManage",
    "Random",
}
CAUSE_TYPES = {
    "TaskInvestigate",
    "TaskContain",
    "TaskManage",
    "Anomaly",
    "LocalPanic",
    "Fixed",
    "Random",
}
BLOCK_POLICIES = {"None", "BlockOriginTask", "BlockAllTasksOnNode"}
IGNORE_APPLY_MODES = {"ApplyOnceThenRemove", "ApplyDailyKeep", "NeverAuto"}
AFFECT_SCOPES = {
    "OriginTask",
    "Node",
    "Global",
    "TaskType:Investigate",
    "TaskType:Contain",
    "TaskType:Manage",
}
ANOMALY_CLASSES = {"Safe", "Euclid", "Keter"}
EFFECT_OPS = {"Add", "Mul", "Set", "ClampAdd"}
OPTIONAL_EMPTY_TOKENS = {"none", "null", "n/a", "na", "-"}
TABLE_TYPE_MARKERS = {"int", "float", "string", "bool", "int[]", "float[]", "string[]"}


@dataclass(slots=True)
class SheetInfo:
    name: str
    headers: list[str]
    rows: list[dict[str, Any]]

    @property
    def row_count(self) -> int:
        return len(self.rows)


def configure_logging(level_name: str) -> logging.Logger:
    level = LOG_LEVELS.get(level_name.upper())
    if level is None:
        raise ValueError(f"Unsupported log level: {level_name}")
    logging.basicConfig(level=level, format="[%(levelname)s] %(message)s")
    logger = logging.getLogger(LOGGER_NAME)
    logger.setLevel(level)
    return logger


def split_list(value: Any) -> list[str]:
    if value is None:
        return []
    text = str(value).strip()
    if not text:
        return []
    parts = [part.strip() for part in LIST_SPLIT_PATTERN.split(text)]
    return [part for part in parts if part]


def to_int(value: Any) -> int | None:
    if value is None or value == "":
        return None
    return int(value)


def to_float(value: Any) -> float | None:
    if value is None or value == "":
        return None
    return float(value)


def to_bool(value: Any) -> int | None:
    if value is None or value == "":
        return None
    if isinstance(value, bool):
        return 1 if value else 0
    if isinstance(value, (int, float)):
        return 1 if int(value) != 0 else 0
    text = str(value).strip().lower()
    if text in {"1", "true", "yes"}:
        return 1
    if text in {"0", "false", "no"}:
        return 0
    raise ValueError(f"Cannot parse bool from: {value!r}")


def normalize_optional_cell(value: Any) -> str:
    if value is None:
        return ""
    text = str(value).strip()
    if not text:
        return ""
    if text.lower() in OPTIONAL_EMPTY_TOKENS:
        return ""
    return text


def sheet_rows(ws) -> tuple[list[str], list[dict[str, Any]]]:
    rows = list(ws.iter_rows(values_only=True))
    if not rows:
        return [], []
    header = [str(col).strip() if col is not None else "" for col in rows[0]]
    result: list[dict[str, Any]] = []
    for row in rows[1:]:
        if row is None:
            continue
        entry: dict[str, Any] = {}
        has_value = False
        for key, cell in zip(header, row):
            if not key:
                continue
            entry[key] = cell
            has_value = has_value or cell not in (None, "")
        if has_value:
            result.append(entry)
    return header, result


def load_meta(rows: list[dict[str, Any]]) -> dict[str, Any]:
    if not rows:
        return {}
    row = rows[0]
    return {
        "schemaVersion": str(row.get("schemaVersion", "")).strip(),
        "dataVersion": str(row.get("dataVersion", "")).strip(),
        "comment": str(row.get("comment", "")).strip(),
    }


def load_balance(rows: list[dict[str, Any]]) -> dict[str, Any]:
    balance: dict[str, Any] = {}
    for row in rows:
        key = str(row.get("key", "")).strip()
        if not key:
            continue
        balance[key] = {
            "value": "" if row.get("value") is None else str(row.get("value")),
            "type": "" if row.get("type") is None else str(row.get("type")),
            "comment": "" if row.get("comment") is None else str(row.get("comment")),
        }
    return balance


def load_nodes(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    nodes: list[dict[str, Any]] = []
    for row in rows:
        nodes.append(
            {
                "nodeId": str(row.get("nodeId", "")).strip(),
                "name": str(row.get("name", "")).strip(),
                "tags": split_list(row.get("tags")),
                "startLocalPanic": to_int(row.get("startLocalPanic")) or 0,
                "startPopulation": to_int(row.get("startPopulation")) or 0,
                "startAnomalyIds": split_list(row.get("startAnomalyIds")),
            }
        )
    return nodes


def load_anomalies(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    anomalies: list[dict[str, Any]] = []
    for row in rows:
        anomalies.append(
            {
                "anomalyId": str(row.get("anomalyId", "")).strip(),
                "name": str(row.get("name", "")).strip(),
                "class": str(row.get("class", "")).strip(),
                "tags": split_list(row.get("tags")),
                "baseThreat": to_int(row.get("baseThreat")) or 0,
                "investigateDifficulty": to_int(row.get("investigateDifficulty")) or 0,
                "containDifficulty": to_int(row.get("containDifficulty")) or 0,
                "manageRisk": to_int(row.get("manageRisk")) or 0,
            }
        )
    return anomalies


def load_task_defs(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    task_defs: list[dict[str, Any]] = []
    for row in rows:
        task_defs.append(
            {
                "taskDefId": str(row.get("taskDefId", "")).strip(),
                "taskType": str(row.get("taskType", "")).strip(),
                "name": str(row.get("name", "")).strip(),
                "baseDays": to_int(row.get("baseDays")) or 0,
                "progressPerDay": to_float(row.get("progressPerDay")) or 0.0,
                "agentSlotsMin": to_int(row.get("agentSlotsMin")) or 0,
                "agentSlotsMax": to_int(row.get("agentSlotsMax")) or 0,
                "yieldKey": "" if row.get("yieldKey") is None else str(row.get("yieldKey")),
                "yieldPerDay": to_float(row.get("yieldPerDay")) or 0.0,
            }
        )
    return task_defs


def load_events(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for row in rows:
        events.append(
            {
                "eventDefId": str(row.get("eventDefId", "")).strip(),
                "source": str(row.get("source", "")).strip(),
                "causeType": str(row.get("causeType", "")).strip(),
                "weight": to_int(row.get("weight")) or 1,
                "title": str(row.get("title", "")).strip(),
                "desc": str(row.get("desc", "")).strip(),
                "blockPolicy": str(row.get("blockPolicy", "")).strip(),
                "defaultAffects": split_list(row.get("defaultAffects")),
                "autoResolveAfterDays": to_int(row.get("autoResolveAfterDays")) or 0,
                "ignoreApplyMode": "" if row.get("ignoreApplyMode") is None else str(row.get("ignoreApplyMode")),
                "ignoreEffectId": "" if row.get("ignoreEffectId") is None else str(row.get("ignoreEffectId")),
            }
        )
    return events


def load_event_options(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    options: list[dict[str, Any]] = []
    for row in rows:
        options.append(
            {
                "eventDefId": str(row.get("eventDefId", "")).strip(),
                "optionId": str(row.get("optionId", "")).strip(),
                "text": str(row.get("text", "")).strip(),
                "resultText": str(row.get("resultText", "")).strip(),
                "affects": split_list(row.get("affects")),
                "effectId": str(row.get("effectId", "")).strip(),
            }
        )
    return options


def load_effects(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    effects: list[dict[str, Any]] = []
    for row in rows:
        effects.append(
            {
                "effectId": str(row.get("effectId", "")).strip(),
                "comment": "" if row.get("comment") is None else str(row.get("comment")),
            }
        )
    return effects


def load_effect_ops(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ops: list[dict[str, Any]] = []
    for row in rows:
        entry = {
            "effectId": str(row.get("effectId", "")).strip(),
            "scope": str(row.get("scope", "")).strip(),
            "statKey": str(row.get("statKey", "")).strip(),
            "op": str(row.get("op", "")).strip(),
            "value": to_float(row.get("value")) or 0.0,
            "min": to_float(row.get("min")),
            "max": to_float(row.get("max")),
            "comment": "" if row.get("comment") is None else str(row.get("comment")),
        }
        ops.append(entry)
    return ops


def load_triggers(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    triggers: list[dict[str, Any]] = []
    for row in rows:
        task_type = normalize_optional_cell(row.get("taskType"))
        triggers.append(
            {
                "eventDefId": str(row.get("eventDefId", "")).strip(),
                "minDay": to_int(row.get("minDay")),
                "maxDay": to_int(row.get("maxDay")),
                "requiresNodeTagsAny": split_list(row.get("requiresNodeTagsAny")),
                "requiresNodeTagsAll": split_list(row.get("requiresNodeTagsAll")),
                "requiresAnomalyTagsAny": split_list(row.get("requiresAnomalyTagsAny")),
                "requiresSecured": to_bool(row.get("requiresSecured")),
                "minLocalPanic": to_int(row.get("minLocalPanic")),
                "taskType": task_type or None,
                "onlyAffectOriginTask": to_bool(row.get("onlyAffectOriginTask")),
            }
        )
    return triggers


def _sheet_has_proto_v1(rows: list[tuple[Any, ...]]) -> bool:
    if len(rows) < 3:
        return False
    field_row = rows[1]
    type_row = rows[2]
    if not any(cell is not None and str(cell).strip() for cell in field_row):
        return False
    saw_marker = False
    for cell in type_row:
        if cell is None:
            continue
        text = str(cell).strip()
        if not text:
            continue
        if text in TABLE_TYPE_MARKERS:
            saw_marker = True
            continue
        return False
    return saw_marker


def _parse_table_value(type_name: str, value: Any) -> Any:
    if type_name == "string":
        return "" if value is None else str(value).strip()
    if type_name == "int":
        return to_int(value)
    if type_name == "float":
        return to_float(value)
    if type_name == "bool":
        return to_bool(value)
    if type_name == "string[]":
        return split_list(value)
    if type_name == "int[]":
        return [int(part) for part in split_list(value)]
    if type_name == "float[]":
        return [float(part) for part in split_list(value)]
    return "" if value is None else str(value).strip()


def _normalize_header(value: Any) -> str:
    if value is None:
        return ""
    return str(value).strip()


def _collect_table(
    sheet_name: str,
    rows: list[tuple[Any, ...]],
    logger: logging.Logger,
) -> dict[str, Any] | None:
    if not rows:
        logger.info("sheet %s empty", sheet_name)
        return None
    mode = "proto_v1" if _sheet_has_proto_v1(rows) else "legacy"
    if mode == "proto_v1":
        header_row = rows[1]
        type_row = rows[2]
        data_rows = rows[3:]
    else:
        header_row = rows[0]
        type_row = []
        data_rows = rows[1:]

    columns: list[dict[str, str]] = []
    column_map: list[tuple[int, str, str]] = []
    for idx, raw_name in enumerate(header_row):
        name = _normalize_header(raw_name)
        if not name or name.startswith("#"):
            continue
        if mode == "proto_v1":
            raw_type = type_row[idx] if idx < len(type_row) else None
            type_name = _normalize_header(raw_type) or "string"
            if type_name not in TABLE_TYPE_MARKERS:
                type_name = "string"
        else:
            type_name = "string"
        columns.append({"name": name, "type": type_name})
        column_map.append((idx, name, type_name))

    if not columns:
        logger.info("sheet %s mode=%s cols=%d rows=%d exportedCols=0", sheet_name, mode, len(header_row), len(data_rows))
        return {
            "mode": mode,
            "idField": "",
            "columns": [],
            "rows": [],
        }

    id_field = columns[0]["name"]
    row_entries: list[dict[str, Any]] = []
    id_index: dict[Any, int] = {}
    skipped_empty_id_logged = False
    for row in data_rows:
        raw_id = row[column_map[0][0]] if row and column_map[0][0] < len(row) else None
        if raw_id is None or (isinstance(raw_id, str) and not raw_id.strip()):
            if not skipped_empty_id_logged:
                logger.info("sheet %s empty id rows skipped", sheet_name)
                skipped_empty_id_logged = True
            continue
        entry: dict[str, Any] = {}
        for col_idx, name, type_name in column_map:
            cell_value = row[col_idx] if row and col_idx < len(row) else None
            entry[name] = _parse_table_value(type_name, cell_value)
        parsed_id = entry[id_field]
        if parsed_id in id_index:
            logger.warning("sheet %s duplicate id=%s (last wins)", sheet_name, parsed_id)
            row_entries[id_index[parsed_id]] = entry
        else:
            id_index[parsed_id] = len(row_entries)
            row_entries.append(entry)

    logger.info(
        "sheet %s mode=%s cols=%d rows=%d exportedCols=%d",
        sheet_name,
        mode,
        len(header_row),
        len(data_rows),
        len(columns),
    )
    return {
        "mode": mode,
        "idField": id_field,
        "columns": columns,
        "rows": row_entries,
    }


def build_tables(workbook, logger: logging.Logger) -> dict[str, Any]:
    tables: dict[str, Any] = {}
    for sheet_name in workbook.sheetnames:
        ws = workbook[sheet_name]
        rows = list(ws.iter_rows(values_only=True))
        table = _collect_table(sheet_name, rows, logger)
        if table is not None:
            tables[sheet_name] = table
    return tables


def _parse_cell(row: dict[str, Any], key: str, parser) -> Any:
    try:
        return parser(row.get(key))
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{key}={row.get(key)!r} ({exc})") from exc


def _enum_issues(sheet: str, key: str, value: str, allowed: set[str], row_index: int) -> list[str]:
    if not value or value in allowed:
        return []
    allowed_text = ", ".join(sorted(allowed))
    return [f"{sheet}[row {row_index}].{key} invalid enum {value!r} (allowed: {allowed_text})"]


def _optional_enum_issues(
    logger: logging.Logger,
    sheet: str,
    key: str,
    raw_value: Any,
    allowed: set[str],
    row_index: int,
) -> list[str]:
    normalized_value = normalize_optional_cell(raw_value)
    logger.debug(
        "normalize optional enum %s[row %d].%s raw=%r normalized=%r",
        sheet,
        row_index,
        key,
        raw_value,
        normalized_value,
    )
    if not normalized_value:
        return []
    return _enum_issues(sheet, key, normalized_value, allowed, row_index)


def _scopes_issues(sheet: str, key: str, scopes: Iterable[str], row_index: int) -> list[str]:
    issues: list[str] = []
    for scope in scopes:
        if scope and scope not in AFFECT_SCOPES:
            allowed_text = ", ".join(sorted(AFFECT_SCOPES))
            issues.append(f"{sheet}[row {row_index}].{key} invalid scope {scope!r} (allowed: {allowed_text})")
    return issues


def collect_sheet_info(wb, logger: logging.Logger) -> dict[str, SheetInfo]:
    infos: dict[str, SheetInfo] = {}
    for sheet_name in REQUIRED_SHEETS:
        if sheet_name not in wb.sheetnames:
            logger.error("sheet %-12s missing", sheet_name)
            continue
        ws = wb[sheet_name]
        headers, rows = sheet_rows(ws)
        logger.info("sheet %-12s present rows=%d", sheet_name, len(rows))
        infos[sheet_name] = SheetInfo(sheet_name, headers, rows)
    return infos


def validate_workbook(infos: dict[str, SheetInfo], logger: logging.Logger) -> list[str]:
    issues: list[str] = []

    missing_sheets = [sheet for sheet in REQUIRED_SHEETS if sheet not in infos]
    for sheet in missing_sheets:
        issues.append(f"Missing sheet: {sheet}")

    if missing_sheets:
        return issues

    headers_map: dict[str, set[str]] = {
        name: {header for header in info.headers if header} for name, info in infos.items()
    }
    for sheet, required in REQUIRED_COLUMNS.items():
        missing_cols = sorted(required - headers_map.get(sheet, set()))
        for col in missing_cols:
            issues.append(f"Missing column: {sheet}.{col}")

    if any(issue.startswith("Missing column") for issue in issues):
        return issues

    anomalies_rows = infos["Anomalies"].rows
    effects_rows = infos["Effects"].rows
    events_rows = infos["Events"].rows
    options_rows = infos["EventOptions"].rows
    effect_ops_rows = infos["EffectOps"].rows
    triggers_rows = infos["EventTriggers"].rows

    anomaly_ids = {str(row.get("anomalyId", "")).strip() for row in anomalies_rows if row.get("anomalyId")}
    effect_ids = {str(row.get("effectId", "")).strip() for row in effects_rows if row.get("effectId")}
    event_ids = {str(row.get("eventDefId", "")).strip() for row in events_rows if row.get("eventDefId")}

    for idx, row in enumerate(anomalies_rows, start=2):
        anomalies_class = str(row.get("class", "")).strip()
        issues.extend(_enum_issues("Anomalies", "class", anomalies_class, ANOMALY_CLASSES, idx))

    for idx, row in enumerate(infos["TaskDefs"].rows, start=2):
        task_type = str(row.get("taskType", "")).strip()
        issues.extend(_enum_issues("TaskDefs", "taskType", task_type, TASK_TYPES, idx))

    for idx, row in enumerate(events_rows, start=2):
        source = str(row.get("source", "")).strip()
        cause_type = str(row.get("causeType", "")).strip()
        block_policy = str(row.get("blockPolicy", "")).strip()
        ignore_mode = str(row.get("ignoreApplyMode", "")).strip()
        default_affects = split_list(row.get("defaultAffects"))

        issues.extend(_enum_issues("Events", "source", source, EVENT_SOURCES, idx))
        issues.extend(_enum_issues("Events", "causeType", cause_type, CAUSE_TYPES, idx))
        issues.extend(_enum_issues("Events", "blockPolicy", block_policy, BLOCK_POLICIES, idx))
        if ignore_mode:
            issues.extend(_enum_issues("Events", "ignoreApplyMode", ignore_mode, IGNORE_APPLY_MODES, idx))
        issues.extend(_scopes_issues("Events", "defaultAffects", default_affects, idx))

        ignore_effect_id = str(row.get("ignoreEffectId", "")).strip()
        if ignore_effect_id and ignore_effect_id not in effect_ids:
            issues.append(f"Events[row {idx}] missing ignoreEffectId={ignore_effect_id}")

    option_keys: set[tuple[str, str]] = set()
    duplicate_keys: set[tuple[str, str]] = set()
    for idx, row in enumerate(options_rows, start=2):
        event_id = str(row.get("eventDefId", "")).strip()
        option_id = str(row.get("optionId", "")).strip()
        affects = split_list(row.get("affects"))
        effect_id = str(row.get("effectId", "")).strip()

        if event_id not in event_ids:
            issues.append(f"EventOptions[row {idx}] missing eventDefId={event_id}")
        if effect_id and effect_id not in effect_ids:
            issues.append(f"EventOptions[row {idx}] missing effectId={effect_id}")
        issues.extend(_scopes_issues("EventOptions", "affects", affects, idx))

        key = (event_id, option_id)
        if key in option_keys:
            duplicate_keys.add(key)
        option_keys.add(key)

    for event_id, option_id in sorted(duplicate_keys):
        issues.append(f"EventOptions duplicate key ({event_id},{option_id})")

    for idx, row in enumerate(effect_ops_rows, start=2):
        effect_id = str(row.get("effectId", "")).strip()
        scope = str(row.get("scope", "")).strip()
        op = str(row.get("op", "")).strip()

        if effect_id not in effect_ids:
            issues.append(f"EffectOps[row {idx}] missing effectId={effect_id}")
        issues.extend(_scopes_issues("EffectOps", "scope", [scope], idx))
        issues.extend(_enum_issues("EffectOps", "op", op, EFFECT_OPS, idx))

    for idx, row in enumerate(triggers_rows, start=2):
        event_id = str(row.get("eventDefId", "")).strip()
        if event_id not in event_ids:
            issues.append(f"EventTriggers[row {idx}] missing eventDefId={event_id}")

        min_day = _parse_cell(row, "minDay", to_int)
        max_day = _parse_cell(row, "maxDay", to_int)
        min_local_panic = _parse_cell(row, "minLocalPanic", to_int)

        if min_day is not None and max_day is not None and min_day > max_day:
            issues.append(f"EventTriggers[row {idx}] invalid day range: {min_day}>{max_day}")
        if min_local_panic is not None and min_local_panic < 0:
            issues.append(f"EventTriggers[row {idx}] minLocalPanic < 0")
        issues.extend(_optional_enum_issues(logger, "EventTriggers", "taskType", row.get("taskType"), TASK_TYPES, idx))

    for idx, row in enumerate(infos["Nodes"].rows, start=2):
        start_anomaly_ids = split_list(row.get("startAnomalyIds"))
        missing = [anomaly_id for anomaly_id in start_anomaly_ids if anomaly_id not in anomaly_ids]
        for anomaly_id in missing:
            issues.append(f"Nodes[row {idx}] missing startAnomalyId={anomaly_id}")

    return issues


def build_data(infos: dict[str, SheetInfo], tables: dict[str, Any]) -> dict[str, Any]:
    return {
        "meta": load_meta(infos["Meta"].rows),
        "balance": load_balance(infos["Balance"].rows),
        "nodes": load_nodes(infos["Nodes"].rows),
        "anomalies": load_anomalies(infos["Anomalies"].rows),
        "taskDefs": load_task_defs(infos["TaskDefs"].rows),
        "events": load_events(infos["Events"].rows),
        "eventOptions": load_event_options(infos["EventOptions"].rows),
        "effects": load_effects(infos["Effects"].rows),
        "effectOps": load_effect_ops(infos["EffectOps"].rows),
        "eventTriggers": load_triggers(infos["EventTriggers"].rows),
        "tables": tables,
    }


def find_git_root(start: Path) -> Path | None:
    try:
        completed = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=start,
            check=True,
            capture_output=True,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return None
    root = completed.stdout.strip()
    return Path(root) if root else None


def resolve_project_root(explicit_root: str | None) -> Path:
    if explicit_root:
        return Path(explicit_root).expanduser().resolve()
    script_root = find_git_root(Path(__file__).resolve().parent)
    if script_root:
        return script_root.resolve()
    return Path.cwd().resolve()


def resolve_path(root: Path, value: str | None, default_rel: str) -> Path:
    raw = value or default_rel
    path = Path(raw).expanduser()
    if not path.is_absolute():
        path = root / path
    return path.resolve()


def parse_args(argv: list[str]) -> argparse.Namespace:
    legacy_xlsx: str | None = None
    legacy_out: str | None = None
    legacy_tail = argv[1:]
    if len(legacy_tail) >= 2 and not legacy_tail[0].startswith("-") and not legacy_tail[1].startswith("-"):
        legacy_xlsx, legacy_out = legacy_tail[0], legacy_tail[1]

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("legacy_xlsx", nargs="?", help=argparse.SUPPRESS)
    parser.add_argument("legacy_out", nargs="?", help=argparse.SUPPRESS)
    parser.add_argument("--xlsx", dest="xlsx", help="Path to game_data.xlsx")
    parser.add_argument("--out", dest="out", help="Path to output game_data.json")
    parser.add_argument(
        "--project-root",
        dest="project_root",
        help="Repository root; defaults to git root or current working directory.",
    )
    parser.add_argument(
        "--log-level",
        dest="log_level",
        default="INFO",
        choices=sorted({"DEBUG", "INFO", "WARN", "ERROR"}),
        help="Logging level (default: INFO)",
    )
    parser.add_argument(
        "--validate-only",
        "--dry-run",
        dest="validate_only",
        action="store_true",
        help="Validate workbook without writing output JSON.",
    )
    parser.add_argument(
        "--no-pretty",
        dest="no_pretty",
        action="store_true",
        help="Emit compact JSON instead of pretty-printed JSON.",
    )

    args = parser.parse_args(argv[1:])

    if not args.xlsx:
        args.xlsx = legacy_xlsx or args.legacy_xlsx
    if not args.out:
        args.out = legacy_out or args.legacy_out

    return args


def run(argv: list[str]) -> int:
    args = parse_args(argv)
    try:
        logger = configure_logging(args.log_level)
    except ValueError as exc:
        print(f"[ERROR] {exc}", file=sys.stderr)
        return 1

    start_time = time.perf_counter()

    project_root = resolve_project_root(args.project_root)
    xlsx_path = resolve_path(project_root, args.xlsx, "GameData/Local/game_data.xlsx")
    out_path = resolve_path(project_root, args.out, "Assets/StreamingAssets/game_data.json")

    logger.info("xlsx=%s out=%s", xlsx_path, out_path)

    if not xlsx_path.exists():
        logger.error("XLSX does not exist: %s", xlsx_path)
        return 1

    try:
        workbook = load_workbook(xlsx_path, data_only=True)
        infos = collect_sheet_info(workbook, logger)
        issues = validate_workbook(infos, logger)
        if issues:
            logger.error("validate=FAIL issues=%d", len(issues))
            for issue in issues:
                logger.error(" - %s", issue)
            return 2
        logger.info("validate=OK")

        tables = build_tables(workbook, logger)
        data = build_data(infos, tables)
        indent = None if args.no_pretty else 2
        json_text = json.dumps(data, ensure_ascii=False, indent=indent)
        json_bytes = json_text.encode("utf-8")
        logger.info("json_bytes=%d", len(json_bytes))

        if args.validate_only:
            elapsed_ms = (time.perf_counter() - start_time) * 1000
            logger.info("SUCCESS validate_only elapsed_ms=%.2f", elapsed_ms)
            return 0

        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_bytes(json_bytes)
        size_bytes = out_path.stat().st_size
        elapsed_ms = (time.perf_counter() - start_time) * 1000
        logger.info("SUCCESS out_bytes=%d elapsed_ms=%.2f", size_bytes, elapsed_ms)
        return 0
    except Exception:
        logger.exception("Unhandled exception during export")
        return 3


def main() -> None:
    sys.exit(run(sys.argv))


if __name__ == "__main__":
    main()
