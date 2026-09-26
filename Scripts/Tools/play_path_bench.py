#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""play_path_bench.py —— 瓦库出牌路径基准（方案 D 转正判定数据）

定位：**按需分析工具，不进门禁链**（与 `thirdparty_patch_overlap.py` 同类）。
用途：回答《多瓦库并行托管可行性与方案.md》§5.4 / §12.6 的「转正判定」三问 ——
  ① **出牌总时延**（这套路径到底快不快）；② **真人插队响应**（并发档有没有真让真人先走）；
  ③ **卡面 / 节点回归**（加速与队列有没有留下滞留节点与异常）。

埋点语义（读 `Scripts/Runtime/LocalWakuuRelicRuntime.cs` 确认，**不是猜的**）：
  - `瓦库回合开始→出牌启动延迟: … delayMs=`（L1006） 回合开始 hook 登记出牌意图 → 看门狗真正开始出牌。
    §12.6 正是用它证明「多瓦库串行」：后一个瓦库的 delayMs ≈ 前序瓦库出牌耗时之和。
  - `瓦库出牌耗时: … ms=`（L1020）                  看门狗的**一次自动出牌 pass**（一个瓦库一回合把可出的牌出完）。
  - `瓦库出牌走动作队列完成（方案 D 实验档）: … ms=`（L688） 单张牌「入队 → 动作彻底跑完」的端到端耗时，
    **含排队等待**（并发档下会被别的动作影响）⇒ 看中位 + p90 + max，别只看均值。
  - `瓦库选择器闸门已进入 / 瓦库出牌作用域已进入（并发档…）: … waitMs=`（L452） 全局 1 槽闸门等待；
    并发档不占闸门 ⇒ waitMs 恒 0，这正是「不互相挡」的直接度量。

用法：
  python Scripts/Tools/play_path_bench.py                                   # 默认扫上级目录的 logs-archive
  python Scripts/Tools/play_path_bench.py --logs-dir <dir> --out <file.md>
  python Scripts/Tools/play_path_bench.py --file <single.log>
退出码：0 = 正常（含"样本不足"）；2 = 用法/环境错误。
"""

from __future__ import annotations

import argparse
import re
import statistics as st
import sys
from pathlib import Path

# ---------------------------------------------------------------- 锚点（改埋点务必同步这里）
RX_CFG = re.compile(
    r"fastVakuuPlay=(True|False), vakuuPlayQueue=(True|False), vakuuPlayOverlap=(True|False)")
RX_MARKER = re.compile(r"marker=([0-9A-Za-z\-]+)")
RX_PLAYERS = re.compile(r"目标玩家数: (\d+)")
RX_DELAY = re.compile(r"出牌启动延迟: player=(\d+), round=(\d+), delayMs=(\d+)")
RX_PASS = re.compile(r"瓦库出牌耗时: player=(\d+), round=(\d+), ms=(\d+)")
RX_CARD = re.compile(r"走动作队列完成（方案 D 实验档）: player=(\d+), round=(\d+), "
                     r"card=([^,]+), actionId=([^,]+), ms=(\d+)")
RX_GATE = re.compile(r"waitMs=(\d+)")

COUNTERS = {
    "让真人插队": "让真人插队",
    "跳过全局闸门": "跳过全局选择器闸门",
    "熔断跳过": "熔断跳过",
    "收回滞留节点": "收回滞留节点",
    "幽灵弹层已自愈": "幽灵弹层已自愈",
    "add_child failed": "add_child() failed",
    "手牌UI与数据差异": "手牌UI与数据存在差异",
    "奖励已自动领取": "卡牌奖励已自动领取",
}


def pct(vals, p):
    if not vals:
        return 0
    s = sorted(vals)
    idx = min(len(s) - 1, max(0, int(round((p / 100.0) * (len(s) - 1)))))
    return s[idx]


def desc(vals):
    """n / 中位 / 均值 / p90 / max（ms）——样本太少时只给 n。"""
    if not vals:
        return "—"
    if len(vals) < 3:
        return f"n={len(vals)} 值={','.join(str(v) for v in sorted(vals))}"
    return (f"n={len(vals)} 中位={int(st.median(vals))} 均值={int(st.mean(vals))} "
            f"p90={pct(vals, 90)} max={max(vals)}")


def parse_session(path: Path) -> dict | None:
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None

    cfg = RX_CFG.search(text)
    marker = RX_MARKER.search(text)
    players = RX_PLAYERS.search(text)
    s = {
        "file": path.name,
        "marker": marker.group(1) if marker else "?",
        "players": int(players.group(1)) if players else 0,
        "fast": cfg.group(1) if cfg else "?",
        "queue": cfg.group(2) if cfg else "?",
        "overlap": cfg.group(3) if cfg else "?",
        "delay": [int(m[2]) for m in RX_DELAY.findall(text)],
        # (round, player, delayMs) —— 用于「同回合多瓦库」的串行特征检查
        "delay_detail": [(int(m[1]), int(m[0]), int(m[2])) for m in RX_DELAY.findall(text)],
        "pass": [int(m[2]) for m in RX_PASS.findall(text)],
        "card": [int(m[4]) for m in RX_CARD.findall(text)],
        "gate": [int(x) for x in RX_GATE.findall(text)],
        "counters": {k: text.count(v) for k, v in COUNTERS.items()},
    }
    if not (s["delay"] or s["pass"] or s["card"]):
        return None
    return s


def serial_round_rows(sessions: list[dict]):
    """同回合内出现 ≥2 个瓦库时，列出各自的 delayMs 序列。

    判据（来自方案 §12.6）：**inline / 串行**路径下，后一个瓦库要等前一个出完牌，
    所以 delayMs 会随席位递增（历史实测 0.5s → 6.3s → 9.7s）；
    并发档（`vakuuPlayOverlap`）下各瓦库各自入队、不再占全局闸门 ⇒ 不应出现这种递增。
    """
    rows = []
    for s in sessions:
        per_round: dict[int, dict[int, int]] = {}
        for round_no, player, delay in s["delay_detail"]:
            per_round.setdefault(round_no, {}).setdefault(player, delay)
        for round_no in sorted(per_round):
            seats = per_round[round_no]
            if len(seats) < 2:
                continue
            seq = [(p, seats[p]) for p in sorted(seats)]
            vals = [v for _, v in seq]
            increasing = all(vals[i] < vals[i + 1] for i in range(len(vals) - 1))
            rows.append((s["file"], round_no, seq, increasing, vals[-1] - vals[0]))
    return rows


# 「串行等待」的判定量级：§12.6 的 inline 基线增量是**秒级**（494 → 6297 → 9724ms）。
# 并发档下仍会看到严格递增，但那只是看门狗 tick 的调度顺序，增量只有几十毫秒。
SERIAL_SPAN_MS = 1000


def render(sessions: list[dict]) -> str:
    out = []
    out.append("# 瓦库出牌路径基准（方案 D 转正判定数据）\n")
    out.append("> 由 `Scripts/Tools/play_path_bench.py` 生成，**勿手工编辑**；")
    out.append("> 单位一律 ms；`delayMs` = 回合开始→出牌启动，`pass` = 一个瓦库一回合的出牌总耗时，")
    out.append("> `单张` = 队列路径单张端到端（含排队等待），`闸门` = 全局 1 槽闸门等待（并发档恒 0）。\n")

    out.append("## 一、逐会话\n")
    out.append("| 会话 | marker | 席 | fast | queue | overlap | 启动延迟 delayMs | 出牌 pass ms | 队列单张 ms | 闸门等待 ms |")
    out.append("|---|---|---|---|---|---|---|---|---|---|")
    for s in sessions:
        out.append("| {f} | {m} | {p} | {fa} | {q} | {o} | {d} | {ps} | {c} | {g} |".format(
            f=s["file"], m=s["marker"], p=s["players"] or "?", fa=s["fast"][:1], q=s["queue"][:1],
            o=s["overlap"][:1], d=desc(s["delay"]), ps=desc(s["pass"]), c=desc(s["card"]), g=desc(s["gate"])))

    out.append("\n## 二、按配置分组汇总（本机归档全量）\n")
    groups: dict[tuple[str, str, str], list[dict]] = {}
    for s in sessions:
        groups.setdefault((s["fast"], s["queue"], s["overlap"]), []).append(s)
    out.append("| fast | queue | overlap | 会话数 | 启动延迟 delayMs | 出牌 pass ms | 队列单张 ms | 闸门等待 ms |")
    out.append("|---|---|---|---|---|---|---|---|")
    for key, items in sorted(groups.items()):
        out.append("| {fa} | {q} | {o} | {n} | {d} | {ps} | {c} | {g} |".format(
            fa=key[0][:1], q=key[1][:1], o=key[2][:1], n=len(items),
            d=desc([v for s in items for v in s["delay"]]),
            ps=desc([v for s in items for v in s["pass"]]),
            c=desc([v for s in items for v in s["card"]]),
            g=desc([v for s in items for v in s["gate"]])))

    out.append("\n## 三、同回合多瓦库的启动延迟（串行是否被打断）\n")
    rows = serial_round_rows(sessions)
    inc = sum(1 for r in rows if r[3])
    serial = [r for r in rows if r[4] >= SERIAL_SPAN_MS]
    if rows:
        spans = sorted(r[4] for r in rows)
        out.append(f"同一回合内出现 ≥2 个瓦库的回合共 **{len(rows)}** 个；delayMs 严格递增的有 **{inc}** 个。")
        out.append("")
        out.append("⚠ **严格递增本身不是判据** —— 看门狗 tick 依次调度本就会让后位多几十毫秒。"
                   "真正的「串行等待」要看**增量量级**：")
        out.append("§12.6 的 inline 基线增量是**秒级**（494 → 6297 → 9724ms，后位要等前位把牌出完）。")
        out.append("")
        out.append(f"本机归档：增量（末位−首位）中位 **{spans[len(spans) // 2]}ms**、最大 **{spans[-1]}ms**；"
                   f"达到秒级（≥{SERIAL_SPAN_MS}ms）的回合 **{len(serial)}/{len(rows)}** 个。")
        out.append("")
        out.append("| 会话 | round | 各瓦库 delayMs（席位:值） | 严格递增 | 增量(末−首) | 属串行级？ |")
        out.append("|---|---|---|---|---|---|")
        for f, round_no, seq, increasing, span in rows:
            out.append(f"| {f} | {round_no} | "
                       + ", ".join(f"…{p % 100}:{v}" for p, v in seq)
                       + f" | {'是' if increasing else '否'} | {span} | "
                       + ("**是**" if span >= SERIAL_SPAN_MS else "否") + " |")
    else:
        out.append("（本机归档里没有「同回合 ≥2 个瓦库」的样本 ⇒ 并发收益缺少同会话对照，")
        out.append("结论只能靠跨会话的 delayMs 分布 + §12.6 历史基线做旁证。）")

    out.append("\n## 四、争用与回归计数\n")
    out.append("| 会话 | " + " | ".join(COUNTERS.keys()) + " |")
    out.append("|---" * (len(COUNTERS) + 1) + "|")
    for s in sessions:
        out.append("| {f} | ".format(f=s["file"])
                   + " | ".join(str(s["counters"][k]) for k in COUNTERS) + " |")

    out.append("\n## 五、附：埋点出处\n")
    out.append("`Scripts/Runtime/LocalWakuuRelicRuntime.cs` —— ")
    out.append("L1006 `出牌启动延迟` / L1020 `瓦库出牌耗时` / L688 `走动作队列完成` / L452 `waitMs`；")
    out.append("`WakuuPlaySpeedPolicy.cs` 记录 r116 的历史基线（内联单张 1.0~1.4s）。\n")
    return "\n".join(out) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description="瓦库出牌路径基准（方案 D 转正判定数据）")
    default_dir = Path(__file__).resolve().parents[3] / "logs-archive"
    ap.add_argument("--logs-dir", default=str(default_dir), help="归档日志目录（默认 pain\\logs-archive）")
    ap.add_argument("--file", default="", help="只分析单个日志文件")
    ap.add_argument("--out", default="", help="把 markdown 报告写到该文件")
    args = ap.parse_args()

    if args.file:
        files = [Path(args.file)]
    else:
        d = Path(args.logs_dir)
        if not d.is_dir():
            print(f"日志目录不存在: {d}（用 --logs-dir 指定，或先跑 tools\\log_archive.py）", file=sys.stderr)
            return 2
        files = sorted(d.glob("*.log"))

    sessions = [s for s in (parse_session(p) for p in files) if s]
    if not sessions:
        print("没有解析到任何会话（锚点未命中：确认日志版本与埋点是否一致）", file=sys.stderr)
        return 2

    report = render(sessions)
    if args.out:
        Path(args.out).write_text(report, encoding="utf-8")
        print(f"已写出: {args.out}")
    else:
        sys.stdout.write(report)
    print(f"会话数={len(sessions)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
