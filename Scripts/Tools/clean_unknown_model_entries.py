#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""清理 progress.save 里「指向已卸载 mod 的残留条目」（离线小工具，r407）。

背景（2026-11-25，游戏 0.114 适配时发现）：
  0.114 新增了进度读取校验，`ProgressSaveManager.LoadProgress` 会把存档里认不出的
  ModelId 逐个打 WARN：

      [WARN] Progress parse: ValidationError { Severity = Warning, Path = DiscoveredCards,
              Message = Unknown CardModel ID: CARD.XXX, IsFatal = False }

  一局实测 7009 条（占日志 36%），来源是**以前装过、现在没启用**的 mod 留下的
  `discovered_*` / `*_stats` 残留。功能上无害，但会淹掉日志、拖慢判读。

做法：
  ① 从**游戏自己打的日志**里读出"它认不出哪些 id"（不猜、不维护白名单）；
  ② 按 Path → 存档字段 的映射，从 `progress.save` 里删掉这些条目；
  ③ 默认**只出计划不动文件**，要加 `--apply` 才写，且**写前自动备份**（`progress.save.bak-<时间戳>`）。

用法：
  python Scripts/Tools/clean_unknown_model_entries.py                      # 计划（最新日志 + 所有 profile）
  python Scripts/Tools/clean_unknown_model_entries.py --apply              # 真删（自动备份）
  python Scripts/Tools/clean_unknown_model_entries.py --log <godot.log> --save <progress.save> --apply
  python Scripts/Tools/clean_unknown_model_entries.py --json               # 机器可读摘要

⚠ 这工具**只删"游戏当前认不出"的条目**：相应 mod 重新启用后，对应角色的战绩/图鉴会重新从零累计
   （所以默认要你看过计划再决定）。备份文件就在存档旁边，改名即可还原。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from typing import Dict, List, Optional, Sequence, Tuple

# ---------------------------------------------------------------- 常量

DEFAULT_LOGS_DIR = os.path.join(
    os.environ.get("APPDATA", ""), "SlayTheSpire2", "logs"
)
DEFAULT_STEAM_ROOT = os.path.join(
    os.environ.get("APPDATA", ""), "SlayTheSpire2", "steam"
)

# 校验路径（C# 属性名）→ (存档 JSON 字段, 取值方式)
#   "list_str"  = 字符串数组
#   "<字段名>"  = 对象数组里取该字段
SECTION_MAP: Dict[str, Tuple[str, str]] = {
    "DiscoveredCards": ("discovered_cards", "list_str"),
    "DiscoveredRelics": ("discovered_relics", "list_str"),
    "DiscoveredEvents": ("discovered_events", "list_str"),
    "DiscoveredPotions": ("discovered_potions", "list_str"),
    "CharStats": ("character_stats", "id"),
    "CardStats": ("card_stats", "id"),
    "EnemyStats": ("enemy_stats", "enemy_id"),
    "EncounterStats": ("encounter_stats", "encounter_id"),
}

# [WARN] Progress parse: ValidationError { Severity = Warning, Path = DiscoveredCards.[7],
#        Message = Unknown card ID: CARD.X, IsFatal = False }
VALIDATION_RE = re.compile(
    r"Progress parse:\s*ValidationError\s*\{.*?Path\s*=\s*([A-Za-z_]+[A-Za-z0-9_]*)"
    r"(?:\.\[\d+\])?.*?Message\s*=\s*Unknown [^:]*:\s*([^,}]+)",
)
# 兜底：老/新措辞都认一遍（有些条目 Message 里带逗号，先用上面的精确式）
UNKNOWN_ID_RE = re.compile(r"Unknown [A-Za-z ]*ID:\s*([A-Za-z0-9_.\-]+)")


# ---------------------------------------------------------------- 日志 → 未知 id

def newest_log(logs_dir: str) -> Optional[str]:
    if not os.path.isdir(logs_dir):
        return None
    cands = [
        os.path.join(logs_dir, name)
        for name in os.listdir(logs_dir)
        if name.startswith("godot") and name.endswith(".log")
    ]
    if not cands:
        return None
    return max(cands, key=os.path.getmtime)


def collect_unknown_ids(log_path: str) -> Dict[str, List[str]]:
    """从日志里按「校验路径」收集未知 id（保序去重）。"""
    found: Dict[str, List[str]] = {}
    seen: Dict[str, set] = {}

    with open(log_path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if "Progress parse:" not in line or "ValidationError" not in line:
                continue

            match = VALIDATION_RE.search(line)
            if match:
                section, raw_id = match.group(1), match.group(2)
            else:
                section = ""
                id_match = UNKNOWN_ID_RE.search(line)
                raw_id = id_match.group(1) if id_match else ""

            model_id = (raw_id or "").strip().strip(".").strip()
            if not model_id or "." not in model_id:
                continue
            if section not in SECTION_MAP:
                continue

            bucket = seen.setdefault(section, set())
            if model_id in bucket:
                continue
            bucket.add(model_id)
            found.setdefault(section, []).append(model_id)

    return found


# ---------------------------------------------------------------- 存档读写

def load_save(path: str) -> dict:
    with open(path, "r", encoding="utf-8-sig") as fh:
        return json.load(fh)


def dump_save(path: str, data: dict) -> None:
    # 与游戏自己的落盘格式一致：2 空格缩进、CRLF、无 BOM、非 ASCII 不转义
    text = json.dumps(data, ensure_ascii=False, indent=2)
    with open(path, "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(text)
        fh.write("\r\n")


def plan_section(data: dict, section: str, unknown: Sequence[str]) -> Tuple[List, List, str]:
    """返回 (将被删除的条目, 保留的条目, 字段说明)；不修改 data。"""
    json_key, mode = SECTION_MAP[section]
    entries = data.get(json_key)
    if not isinstance(entries, list):
        return [], [], f"{json_key} 不存在或不是数组"

    unknown_set = set(unknown)
    removed, kept = [], []
    for entry in entries:
        if mode == "list_str":
            value = entry
        elif isinstance(entry, dict):
            value = entry.get(mode)
        else:
            value = None

        if isinstance(value, str) and value in unknown_set:
            removed.append(entry)
        else:
            kept.append(entry)

    return removed, kept, f"{json_key}（{'字符串数组' if mode == 'list_str' else '字段 ' + mode}）"


def nested_unknown_characters(data: dict, unknown_chars: Sequence[str]) -> Dict[str, int]:
    """顺带统计：`*_stats.fight_stats` / `ancient_stats.character_stats` 里也引用了未知角色多少处。"""
    unknown = set(unknown_chars)
    report: Dict[str, int] = {}

    for key, id_field in (("enemy_stats", "enemy_id"), ("encounter_stats", "encounter_id")):
        hits = 0
        for entry in data.get(key) or []:
            if not isinstance(entry, dict):
                continue
            for item in entry.get("fight_stats") or []:
                if isinstance(item, dict) and item.get("character") in unknown:
                    hits += 1
        if hits:
            report[key + ".fight_stats"] = hits

    hits = 0
    for entry in data.get("ancient_stats") or []:
        if not isinstance(entry, dict):
            continue
        for item in entry.get("character_stats") or []:
            if isinstance(item, dict) and item.get("character") in unknown:
                hits += 1
    if hits:
        report["ancient_stats.character_stats"] = hits

    return report


# ---------------------------------------------------------------- 主流程

def find_saves(steam_root: str) -> List[str]:
    out: List[str] = []
    if not os.path.isdir(steam_root):
        return out
    for account in sorted(os.listdir(steam_root)):
        account_dir = os.path.join(steam_root, account)
        if not os.path.isdir(account_dir):
            continue
        for sub in ("modded", ""):
            base = os.path.join(account_dir, sub) if sub else account_dir
            if not os.path.isdir(base):
                continue
            for profile in sorted(os.listdir(base)):
                candidate = os.path.join(base, profile, "saves", "progress.save")
                if os.path.isfile(candidate):
                    out.append(candidate)
    return out


def process_save(
    save_path: str,
    unknown: Dict[str, List[str]],
    apply: bool,
    out_json: bool,
) -> dict:
    result: dict = {"save": save_path, "applied": False, "sections": {}, "nested": {}, "error": None}

    try:
        data = load_save(save_path)
    except Exception as exc:  # noqa: BLE001 - 工具层：坏档只报告，不猜
        result["error"] = f"读取/解析失败: {exc}"
        return result

    total_removed = 0
    to_write: Dict[str, List] = {}

    for section, ids in unknown.items():
        removed, kept, field_note = plan_section(data, section, ids)
        if not removed:
            continue
        result["sections"][section] = {
            "field": field_note,
            "removed": len(removed),
            "kept": len(kept),
            "samples": [describe_entry(entry) for entry in removed[:6]],
        }
        total_removed += len(removed)
        to_write[section] = kept

    chars = unknown.get("CharStats") or []
    if chars:
        nested = nested_unknown_characters(data, chars)
        if nested:
            result["nested"] = nested

    result["total_removed"] = total_removed

    if not apply or total_removed == 0:
        return result

    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup = f"{save_path}.bak-{stamp}"
    try:
        with open(save_path, "rb") as src, open(backup, "wb") as dst:
            dst.write(src.read())

        for section, kept in to_write.items():
            json_key = SECTION_MAP[section][0]
            data[json_key] = kept

        dump_save(save_path, data)
        load_save(save_path)          # 写完立刻回读校验，坏档立刻能发现
        result["applied"] = True
        result["backup"] = backup
    except Exception as exc:  # noqa: BLE001
        result["error"] = f"写入失败（已尝试备份 {backup}）: {exc}"

    return result


def describe_entry(entry) -> str:
    if isinstance(entry, str):
        return entry
    if isinstance(entry, dict):
        for key in ("id", "enemy_id", "encounter_id", "ancient_id"):
            value = entry.get(key)
            if isinstance(value, str):
                stats = {
                    k: entry[k]
                    for k in ("playtime", "total_wins", "total_losses", "times_picked")
                    if k in entry
                }
                return f"{value} {stats}" if stats else value
    return str(entry)[:80]


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="清理 progress.save 里指向已卸载 mod 的残留条目（0.114 起会刷 Progress parse WARN）",
    )
    parser.add_argument("--log", help="游戏日志路径（默认取 logs 目录里最新的 godot*.log）")
    parser.add_argument("--logs-dir", default=DEFAULT_LOGS_DIR, help="日志目录（配合默认日志使用）")
    parser.add_argument("--steam-root", default=DEFAULT_STEAM_ROOT, help="存档根目录（默认 <APPDATA>/SlayTheSpire2/steam）")
    parser.add_argument("--save", action="append", default=[], help="指定 progress.save（可重复）")
    parser.add_argument("--apply", action="store_true", help="真删（默认只打印计划；写前自动备份）")
    parser.add_argument("--json", action="store_true", help="输出机器可读 JSON 摘要")
    args = parser.parse_args(argv)

    log_path = args.log or newest_log(args.logs_dir)
    if not log_path or not os.path.isfile(log_path):
        print(f"[FAIL] 找不到日志：{log_path or '(logs 目录为空)'}", file=sys.stderr)
        return 2

    unknown = collect_unknown_ids(log_path)
    if not unknown:
        print(f"[OK] 日志里没有 Unknown *Model ID 的校验告警，无需清理：{log_path}")
        return 0

    saves = list(args.save) or find_saves(args.steam_root)
    if not saves:
        print(f"[FAIL] 没找到 progress.save（root={args.steam_root}）", file=sys.stderr)
        return 2

    results = [process_save(path, unknown, args.apply, args.json) for path in saves]

    if args.json:
        print(json.dumps({"log": log_path, "unknown": unknown, "results": results},
                         ensure_ascii=False, indent=2))
        return 0

    print(f"日志: {log_path}")
    print("日志里的未知 id（按校验段）:")
    for section, ids in unknown.items():
        print(f"  {section:18s} {len(ids):4d} 个   例: {', '.join(ids[:3])}")
    print()

    for result in results:
        save_path = result["save"]
        print(f"== {save_path}")
        if result["error"]:
            print(f"   [FAIL] {result['error']}")
            continue

        if not result["sections"]:
            print("   无残留条目，跳过")
            continue

        for section, info in result["sections"].items():
            print(f"   {section:18s} 删除 {info['removed']:5d} / 保留 {info['kept']:5d}"
                  f"   [{info['field']}]")
            for sample in info["samples"]:
                print(f"       - {sample}")

        if result["nested"]:
            print("   （另有嵌套引用未处理，仅统计）: "
                  + ", ".join(f"{k}={v}" for k, v in result["nested"].items()))

        if result["applied"]:
            print(f"   [OK] 已写入，共删 {result['total_removed']} 条；备份 = {result['backup']}")
        else:
            print(f"   [DRY] 计划删除 {result['total_removed']} 条（加 --apply 才会写，写前自动备份）")
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
