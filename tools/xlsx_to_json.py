#!/usr/bin/env python3
"""Export game_data.xlsx into Unity-friendly game_data.json."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from openpyxl import load_workbook

LIST_SPLIT = ";"


def split_list(value: Any) -> list[str]:
    if value is None:
        return []
    text = str(value).strip()
    if not text:
        return []
    return [part.strip() for part in text.split(LIST_SPLIT) if part.strip()]


def to_int(value: Any) -> int | None:
    if value is None or value == "":
        return None
    return int(value)


def to_float(value: Any) -> float | None:
    if value is None or value == "":
        return None
    return float(value)


def to_bool(value: Any) -> bool | None:
    if value is None or value == "":
        return None
    if isinstance(value, bool):
        return value
    text = str(value).strip().lower()
    if text in {"1", "true", "yes"}:
        return True
    if text in {"0", "false", "no"}:
        return False
    raise ValueError(f"Cannot parse bool from: {value!r}")


def sheet_rows(ws) -> list[dict[str, Any]]:
    rows = list(ws.iter_rows(values_only=True))
    if not rows:
        return []
    header = [str(col).strip() if col is not None else "" for col in rows[0]]
    result: list[dict[str, Any]] = []
    for row in rows[1:]:
        if row is None:
            continue
        entry = {}
        has_value = False
        for key, cell in zip(header, row):
            if not key:
                continue
            entry[key] = cell
            has_value = has_value or cell not in (None, "")
        if has_value:
            result.append(entry)
    return result


def load_meta(ws) -> dict[str, Any]:
    rows = sheet_rows(ws)
    if not rows:
        return {}
    row = rows[0]
    return {
        "schemaVersion": str(row.get("schemaVersion", "")).strip(),
        "dataVersion": str(row.get("dataVersion", "")).strip(),
        "comment": str(row.get("comment", "")).strip(),
    }


def load_balance(ws) -> dict[str, Any]:
    balance: dict[str, Any] = {}
    for row in sheet_rows(ws):
        key = str(row.get("key", "")).strip()
        if not key:
            continue
        balance[key] = {
            "value": "" if row.get("value") is None else str(row.get("value")),
            "type": "" if row.get("type") is None else str(row.get("type")),
            "comment": "" if row.get("comment") is None else str(row.get("comment")),
        }
    return balance


def load_nodes(ws) -> list[dict[str, Any]]:
    nodes: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_anomalies(ws) -> list[dict[str, Any]]:
    anomalies: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_task_defs(ws) -> list[dict[str, Any]]:
    task_defs: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_events(ws) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_event_options(ws) -> list[dict[str, Any]]:
    options: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_effects(ws) -> list[dict[str, Any]]:
    effects: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
        effects.append(
            {
                "effectId": str(row.get("effectId", "")).strip(),
                "comment": "" if row.get("comment") is None else str(row.get("comment")),
            }
        )
    return effects


def load_effect_ops(ws) -> list[dict[str, Any]]:
    ops: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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


def load_triggers(ws) -> list[dict[str, Any]]:
    triggers: list[dict[str, Any]] = []
    for row in sheet_rows(ws):
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
                "taskType": "" if row.get("taskType") is None else str(row.get("taskType")),
                "onlyAffectOriginTask": to_bool(row.get("onlyAffectOriginTask")),
            }
        )
    return triggers


def export_game_data(xlsx_path: Path, json_path: Path) -> None:
    wb = load_workbook(xlsx_path)

    data = {
        "meta": load_meta(wb["Meta"]),
        "balance": load_balance(wb["Balance"]),
        "nodes": load_nodes(wb["Nodes"]),
        "anomalies": load_anomalies(wb["Anomalies"]),
        "taskDefs": load_task_defs(wb["TaskDefs"]),
        "events": load_events(wb["Events"]),
        "eventOptions": load_event_options(wb["EventOptions"]),
        "effects": load_effects(wb["Effects"]),
        "effectOps": load_effect_ops(wb["EffectOps"]),
        "eventTriggers": load_triggers(wb["EventTriggers"]),
    }

    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("xlsx", type=Path, help="Path to game_data.xlsx")
    parser.add_argument("json", type=Path, help="Path to output game_data.json")
    args = parser.parse_args()

    export_game_data(args.xlsx, args.json)


if __name__ == "__main__":
    main()
