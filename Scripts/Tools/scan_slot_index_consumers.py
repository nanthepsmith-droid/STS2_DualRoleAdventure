#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""scan_slot_index_consumers.py —— 扫「按玩家槽位索引（GetPlayerSlotIndex）消费」的原版调用点。

为什么需要它（2026-10-11 r264，坑详见 references AE-24）：
本 mod 的「战神」（战灵召唤的临时席位）**刻意不进** `runState.Players`（AE-1/AE-3），于是
`IPlayerCollection.GetPlayerSlotIndex(player)` 对它恒返回 **-1**（接口注释原文：
"The slot index of the player, or **-1** if the player is not in Players"）。凡是不判 -1 就
**拿它当索引/下标**的原版代码，战神一到就抛 `ArgumentOutOfRangeException`
（实机铁证：`PlayerChoiceSynchronizer.ReserveChoiceId` 里 `_choiceIds[-1]` 冒穿
`CombatManager.StartTurn` ⇒ 原版打 "turn loop died … the combat is stuck until the room is
restarted" ⇒ **整场战斗废掉**：不能出牌 / 不能切人 / 不能结束回合）。

本工具把 `sts2src`（反编译源码，仓库外用源码隔离）里**所有** `GetPlayerSlotIndex(` 调用点列出来，
逐点给「一看就知道是不是雷」的分类：

  GUARDED     附近有 `-1` / `< 0` / `>= 0` 判断 ⇒ 原版自己防了；
  INDEXED     直接拿去当**下标**（`xxx[slot]` / `[slot]` 索引、`ElementAt(slot)`…）且未见防护 ⇒ **雷**；
  REVIEW      其它用法（传参 / 比较 / 算术）⇒ 需要人看一眼。

再与脚本内嵌的 `KNOWN` 登记表（"我们已经知道并处理过的位置"）比对，输出 `未登记` 明细。
`--strict` 时只要有「INDEXED 且未登记」就返回非 0（供门禁使用；默认只报告）。

用法：
    python Scripts/Tools/scan_slot_index_consumers.py --repo .
    python Scripts/Tools/scan_slot_index_consumers.py --repo . --json
    python Scripts/Tools/scan_slot_index_consumers.py --repo . --out scan.md
    python Scripts/Tools/scan_slot_index_consumers.py --repo . --src D:\\path\\to\\sts2src\\src

缺反编译源码（CI / 别人机器）时打印 SKIP 并退出 0 —— 与 check_string_targets.py 同口径
（**源码隔离**：`sts2src` 不入库，所以这条门禁只能本机跑）。
"""

import argparse
import io
import json
import os
import re
import sys

# 本机控制台是 GBK：强制 UTF-8，否则中文经管道乱码。
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")
except Exception:
    pass

CALL_RE = re.compile(r"GetPlayerSlotIndex\s*\(")

# 同一次计算里"接收槽位的结果"常见变量名（用于找下标用法）。
SLOT_VAR_RE = re.compile(r"(?:int\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*[^;\n]*GetPlayerSlotIndex\s*\(")

GUARD_RE = re.compile(r"(<\s*0|>=\s*0|==\s*-1|!=\s*-1|>\s*-1|IsPlayerSlotValid|slot\s*<\s*0)")
INDEX_RE = re.compile(r"(\[[^\]]*\]|ElementAt|GetRange|Skip\(|Take\()")

# 每个位置看多少行来判"是不是当索引用"（同一方法内的就近上下文）。
WINDOW_BEFORE = 3
WINDOW_AFTER = 6

# 已登记：我们知道它会对临时席位踩 -1，并且已经处理 / 已确认不可达。
# key = "类型.方法"（声明点所在方法名即可），value = 说明。
KNOWN = {
    "PlayerChoiceSynchronizer.ReserveChoiceId":
        "已补（r264 `PlayerChoiceSynchronizerTemporarySeatPatch`：临时席位走专属槽，不再 -1）",
    "PlayerChoiceSynchronizer.GetChoiceId":
        "已补（同上；`ValidateChoiceId` 会经它取当前值）",
    "NullRunState.GetPlayerSlotIndex":
        "不是消费者，接口的空实现（恒 -1）",
    "MerchantRoom.GetLocalInventory":
        "只在商店房间；战神只存在于战斗内 ⇒ 不可达（登记备查）",
    "Player.InitializeSeed":
        "只在开局建种子时调；战神是战斗内临时生成、不走这条 ⇒ 不可达（登记备查）",
    "Rng.Rng":
        "`new Rng(player, id)` 用 slotIndex 只是**换个种子**、不抛 ⇒ 低风险（登记）",
    "HoveredModelTracker.OnPlayerStateChanged":
        "**已防护**（r265 `HoveredModelTrackerForeignSeatGuardPatch`：结构外成员直接跳过；"
        "实机从未触发 —— `_playerCollection` 是 `RunState`、`InputSynchronizer.StateChanged` 只对有输入通道的 peer 触发）",
    "HoveredModelTracker.OnPlayerStateRemoved":
        "**已防护**（r265，同上）",
    "HoveredModelTracker.GetHoveredModel":
        "**已防护**（r265，同上；返回 null，唯一调用方 `NMultiplayerPlayerIntentHandler` 接受 null）",
    "EventCombatSynchronizer.ReadyToEnterCombat":
        "**不防护（刻意）**：`_states[slot]` 是「全员就位才进战斗」的计数，跳过会让 "
        "`_states.All(s => s != null)` 永远不满足 ⇒ 反而卡住事件；且战神只在战斗内生成，"
        "不会走「事件 → 进入战斗」（2026-10-11 扫描未实机触发）",
}


def find_method_name(lines, idx):
    """从命中行往上找最近的成员声明（方法/构造/属性），给个"类型.方法"用的方法名。

    要排除 `if (…)` / `for (…)` 这类关键字行 —— 否则命中行在语句块里时会把方法名识别成 `if`。
    """
    keywords = {"if", "for", "while", "switch", "catch", "using", "lock", "return", "new",
                "foreach", "fixed", "do", "else", "get", "set", "await", "yield", "throw", "case", "when"}
    for i in range(idx, max(-1, idx - 300), -1):
        line = lines[i]
        m = re.search(r"(?:public|private|protected|internal|static|async|override|virtual|sealed|extern|partial)"
                      r"[\w\s<>\[\],\.\?]*?\s(\w+)\s*\(", line)
        if m and m.group(1) not in keywords:
            return m.group(1)
        # 属性 getter / 表达式体
        m2 = re.search(r"(?:public|private|protected|internal)[\w\s<>\[\],\.\?]*?\s(\w+)\s*(?:=>|\{)", line)
        if m2 and m2.group(1) not in keywords:
            return m2.group(1)
    return "?"


def find_type_name(lines, idx):
    """取**最外层**类型名（反编译源码里顶层类型缩进为 0；嵌套类型缩进更深）。"""
    for i in range(idx, -1, -1):
        m = re.match(r"(class|struct|record|interface)\s+(\w+)", lines[i])
        if m:
            return m.group(2)
    # 兜底：命名空间内的顶层声明带修饰符
    for i in range(idx, -1, -1):
        m = re.search(r"(?:public|internal|private)?\s*(?:abstract\s+|sealed\s+|static\s+|partial\s+)*"
                      r"(?:class|struct|record|interface)\s+(\w+)", lines[i])
        if m:
            return m.group(1)
    return "?"


# 「战神是否在场」的可达性判断：战神只存在于**战斗内**（战斗中生成、战斗结束/退局摘除）。
# 房间 / 地图 / 奖励 / 商店 这些流程战神根本不在场 ⇒ 那里的 -1 只是"理论雷"，不会打到。
OUT_HINTS = (
    "RestSite", "Rooms/", "Rewards", "TreasureRoom", "MapSelection", "ActChange",
    "EventSynchronizer", "EventModel", "Merchant", "Shop", "NMap", "NRestSite",
    "Cards/", "Unlocks", "Daily", "Lobby", "Screens/Load", "Screens/MainMenu",
)
IN_HINTS = (
    "Combat", "Commands/", "GameActions/", "PeerInput", "Powers/", "Monsters/",
    "Nodes/Vfx", "Nodes/Combat", "Nodes/Cards", "GameActions",
)


def reachability(file_rel, type_name):
    hay = file_rel + "/" + type_name
    if any(h in hay for h in OUT_HINTS):
        return "out"
    if any(h in hay for h in IN_HINTS):
        return "battle"
    return "unknown"


def classify(lines, idx0):
    """返回 (分类, 片段)。idx0 = 命中行下标（0 基）。"""
    start = max(0, idx0 - WINDOW_BEFORE)
    end = min(len(lines), idx0 + WINDOW_AFTER + 1)
    window = lines[start:end]
    snippet = " / ".join(l.strip() for l in window if l.strip())[:400]

    slot_var = None
    m = SLOT_VAR_RE.search(lines[idx0]) or SLOT_VAR_RE.search(snippet)
    if m:
        slot_var = m.group(1)

    guarded = any(GUARD_RE.search(l) for l in window)

    indexed = False
    if slot_var:
        # 变量被当索引：`xxx[slotVar]` / `[slotVar]`
        if re.search(r"\[\s*" + re.escape(slot_var) + r"\s*\]", snippet):
            indexed = True
        if re.search(r"ElementAt\s*\(\s*" + re.escape(slot_var), snippet):
            indexed = True
    if not indexed and INDEX_RE.search(lines[idx0]):
        indexed = True

    if indexed and not guarded:
        return "INDEXED", snippet
    if guarded:
        return "GUARDED", snippet
    return "REVIEW", snippet


def scan(src_dir):
    rows = []
    for root, _dirs, files in os.walk(src_dir):
        for fname in files:
            if not fname.endswith(".cs"):
                continue
            path = os.path.join(root, fname)
            try:
                text = open(path, encoding="utf-8-sig", errors="replace").read()
            except OSError:
                continue
            lines = text.splitlines()
            for i, line in enumerate(lines):
                if not CALL_RE.search(line):
                    continue
                # 接口/实现里的**声明行**（形如 `int GetPlayerSlotIndex(Player player)`）不是消费点。
                if re.search(r"GetPlayerSlotIndex\s*\(\s*(Player|ulong)\s+\w+\s*\)\s*(;|\{)?\s*$", line) \
                        and "=" not in line:
                    continue
                kind, snippet = classify(lines, i)
                type_name = find_type_name(lines, i)
                file_rel = os.path.relpath(path, src_dir).replace("\\", "/")
                rows.append({
                    "file": file_rel,
                    "line": i + 1,
                    "type": type_name,
                    "method": find_method_name(lines, i),
                    "kind": kind,
                    "reachability": reachability(file_rel, type_name),
                    "snippet": snippet,
                })
    return rows


def registry_key(row):
    return f"{row['type']}.{row['method']}"


def known_note(row):
    """登记表两级匹配：先 `类型.方法`，再退化为「任意类型 + 该方法名」。"""
    key = registry_key(row)
    if key in KNOWN:
        return KNOWN[key]
    for k, v in KNOWN.items():
        if k.split(".", 1)[-1] == row["method"]:
            return v
    return ""


# ---------------------------------------------------------------- 基线快照
# 与 S7「运行期目标基线」同一思路：把"当前这版反编译源码里的消费点集合"存成基线，
# 之后**新增/消失**都能看见（游戏更新、第三方 mod 改写法、我们自己挪代码）。
# 只存「语义标识」（不含行号 ⇒ 挪代码不会造成 diff 噪声）。
BASELINE_REL = os.path.join("Scripts", "Tools", "baselines", "slot_index_consumers.baseline.txt")


def baseline_key(row):
    return f"{row['kind']}\t{row['reachability']}\t{row['type']}.{row['method']}\t{row['file']}"


def load_baseline(repo, path):
    full = path or os.path.join(repo, BASELINE_REL)
    if not os.path.isfile(full):
        return None, full
    keys = set()
    for line in open(full, encoding="utf-8-sig", errors="replace").read().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            keys.add(line)
    return keys, full


def write_baseline(full, rows):
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8") as fh:
        fh.write("# GetPlayerSlotIndex 消费点基线（scan_slot_index_consumers.py --update-baseline 生成）\n")
        fh.write("# 格式: 分类\t可达性\t类型.方法\t文件（不含行号）\n")
        for key in sorted({baseline_key(r) for r in rows}):
            fh.write(key + "\n")
    return len({baseline_key(r) for r in rows})




def main():
    ap = argparse.ArgumentParser(description="扫按 GetPlayerSlotIndex 消费的原版调用点（战神 -1 风险）")
    ap.add_argument("--repo", default=".")
    ap.add_argument("--src", default="")
    ap.add_argument("--out", default="")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--strict", action="store_true",
                    help="有「新增调用点且是当索引用、战神可能在场」时返回非 0（门禁用）")
    ap.add_argument("--baseline", default="", help="基线文件路径；缺省 = Scripts/Tools/baselines/…")
    ap.add_argument("--update-baseline", action="store_true", help="把当前扫描结果写成新基线")
    args = ap.parse_args()

    repo = os.path.abspath(args.repo)
    src = args.src or os.path.join(os.path.dirname(repo), "sts2src", "src")
    if not os.path.isdir(src):
        print(f"[SKIP] 找不到反编译源码目录: {src}（sts2src 不入库 ⇒ 本检查只能本机跑）")
        return 0

    rows = scan(src)
    reach_order = {"battle": 0, "unknown": 1, "out": 2}
    kind_order = {"INDEXED": 0, "REVIEW": 1, "GUARDED": 2}
    rows.sort(key=lambda r: (reach_order.get(r["reachability"], 1), kind_order.get(r["kind"], 1),
                             r["file"], r["line"]))

    for r in rows:
        note = known_note(r)
        r["registered"] = bool(note)
        r["registry_note"] = note

    counts = {"INDEXED": 0, "GUARDED": 0, "REVIEW": 0}
    reach_counts = {"battle": 0, "unknown": 0, "out": 0}
    for r in rows:
        counts[r["kind"]] = counts.get(r["kind"], 0) + 1
        reach_counts[r["reachability"]] = reach_counts.get(r["reachability"], 0) + 1

    # **重点** = 战斗内 / 待定 且 当索引用 且 未登记（战神真的会走到）
    hot = [r for r in rows if r["kind"] == "INDEXED" and not r["registered"]
           and r["reachability"] in ("battle", "unknown")]
    out_only = [r for r in rows if r["kind"] == "INDEXED" and r["reachability"] == "out"]

    baseline_keys, baseline_path = load_baseline(repo, args.baseline)
    if args.update_baseline:
        n = write_baseline(baseline_path, rows)
        print(f"[OK] 基线已更新: {baseline_path}（{n} 条语义标识）")
        baseline_keys = {baseline_key(r) for r in rows}

    current_keys = {baseline_key(r) for r in rows}
    added, removed = [], []
    if baseline_keys is not None:
        added = sorted(current_keys - baseline_keys)
        removed = sorted(baseline_keys - current_keys)

    if args.json:
        print(json.dumps({
            "src": src, "counts": counts, "reachability": reach_counts, "total": len(rows),
            "hot_unregistered_indexed": hot, "out_of_combat_indexed": out_only,
            "baseline": baseline_path, "added": added, "removed": removed, "rows": rows,
        }, ensure_ascii=False, indent=2))
    else:
        print(f"扫描: {src}")
        print(f"合计 {len(rows)} 个调用点：INDEXED(当索引)={counts['INDEXED']} / "
              f"GUARDED(原版已防)={counts['GUARDED']} / REVIEW(需人看)={counts['REVIEW']}")
        print(f"可达性：战斗内={reach_counts['battle']} / 待定={reach_counts['unknown']} / 战斗外(战神不在场)={reach_counts['out']}")
        if baseline_keys is None:
            print(f"（未找到基线 {baseline_path} ⇒ 不报新增；用 --update-baseline 建立基线）")
        else:
            print(f"基线比对（{os.path.relpath(baseline_path, repo)}）：新增 {len(added)} / 消失 {len(removed)}")
            for key in added:
                print(f"  + {key}")
            for key in removed:
                print(f"  - {key}")
        print("")

        def dump(title, group):
            if not group:
                return
            print(f"---- {title} ({len(group)})")
            for r in group:
                reach = {"battle": "战斗内", "unknown": "待定", "out": "战斗外"}[r["reachability"]]
                mark = "已登记" if r["registered"] else "未登记"
                print(f"  [{reach}][{mark}] {r['type']}.{r['method']}  {r['file']}:{r['line']}")
                if r["registry_note"]:
                    print(f"          └ {r['registry_note']}")
            print("")

        dump("★ 重点：战斗内/待定 + 当索引用 + 未登记（战神走到就抛）",
             [r for r in rows if r in hot and r["reachability"] == "battle"])
        dump("★ 重点（待定可达性）：同上，但要人确认战神是否在场",
             [r for r in rows if r in hot and r["reachability"] == "unknown"])
        dump("其它：当索引用但战神不在场（战斗外流程，登记备查）", out_only)
        dump("★ 战斗内/待定 + 当索引用 + 已登记（已补丁 或 已确认不可达）",
             [r for r in rows if r["kind"] == "INDEXED" and r["registered"]
              and r["reachability"] in ("battle", "unknown")])
        dump("REVIEW：其它用法（传参/比较/算术）",
             [r for r in rows if r["kind"] == "REVIEW" and r["reachability"] != "out"])
        dump("GUARDED：原版自己防了 -1", [r for r in rows if r["kind"] == "GUARDED"])

        if hot:
            print(f"⚠ 有 {len(hot)} 处「当索引用 + 战神可能在场 + 未登记」—— 需要逐个确认/补丁/登记。")
        else:
            print("✓ 没有「当索引用 + 战神可能在场 + 未登记」的调用点。")
        if added:
            print(f"⚠ 基线外新增 {len(added)} 处（游戏更新或第三方改动后请逐个看）。")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write("# GetPlayerSlotIndex 消费点扫描（战神 -1 风险）\n\n")
            fh.write(f"源码目录：`{src}`；合计 {len(rows)} 个调用点，"
                     f"INDEXED={counts['INDEXED']} / GUARDED={counts['GUARDED']} / REVIEW={counts['REVIEW']}；"
                     f"可达性 战斗内={reach_counts['battle']} / 待定={reach_counts['unknown']} / 战斗外={reach_counts['out']}\n\n")
            fh.write(f"**重点（战斗内/待定 + 当索引用 + 未登记）：{len(hot)} 处**\n\n")
            for r in rows:
                reach = {"battle": "战斗内", "unknown": "待定", "out": "战斗外"}[r["reachability"]]
                mark = "已登记" if r["registered"] else "未登记"
                fh.write(f"- [{reach}][{mark}] `{r['type']}.{r['method']}` — `{r['file']}:{r['line']}`（{r['kind']}）\n")
                if r["registry_note"]:
                    fh.write(f"  - {r['registry_note']}\n")
        print(f"已写出: {args.out}")

    # 门禁判据：**基线外新增**里出现「当索引用 + 战神可能在场」⇒ 挂了（新入口需要人看/补丁）。
    added_hot = [k for k in added if k.startswith("INDEXED\tbattle") or k.startswith("INDEXED\tunknown")]
    if args.strict and added_hot:
        print(f"[FAIL] 基线外新增 {len(added_hot)} 处「当索引用 + 战神可能在场」的调用点。")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
