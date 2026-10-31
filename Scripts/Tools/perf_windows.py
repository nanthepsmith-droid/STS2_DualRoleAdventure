#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""perf_windows.py —— 「[性能探针] 窗口=…」日志的判读工具（r327）

定位：**按需分析工具，不进门禁链**（与 `play_path_bench.py` 同类）。

为什么需要它：卡顿类问题每次都要手工把 `[性能探针]` 行抠出来算三分位、算我方占比、
算"加速档开/关"两段的差 —— 手算既慢又容易把**混淆因素**（越到后面 / 楼层更深 / 加速档）
混成同一个结论。本工具把四件事固定下来：

  1. **三分位**：开局 / 中段 / 后段各自的 fps、帧时、process 占比 ⇒ 回答"是不是越到后面越卡"；
  2. **我方占比**：所有逐帧槽位合计 / 帧时间 ⇒ 回答"卡是不是我们的逐帧回调造成的"；
  3. **分档切片**：按窗口里的 `加速档=开/关`（或 `[性能探针] 配置档切换` 锚点）切两段对比
     ⇒ 回答"开加速是不是真的更卡"，并且**同时打印两段的楼层/回合**，避免把"楼层更深"当成"加速更卡"；
  4. **资源趋势**：VRAM / 静态内存 / 托管堆 / 资源数 / 孤儿节点 / GC 次数的首窗 vs 末窗
     ⇒ 回答"是不是漏了东西"（节点数与对象数平的时候，泄漏只可能在这几项里）。

数据来源（`Scripts/Runtime/LocalPerfProbe.cs`）：
  - 老格式：`窗口=…ms, fps均=…, 引擎process=…ms/帧, 节点=…, 对象=…, 帧=…, <槽位>=…`
  - r327 新增：`帧时=…ms(process均=…ms=…%, process最大=…ms)`、`战斗=/回合=/楼层=/席位=`、
    `加速档=开|关`、`资源[VRAM=…MB(+…), 静态=…, 托管堆=…, 资源数=…, 孤儿=…, 根信号=…, 绘制=…, 图元=…, GC=…/…/…]`

用法：
  python Scripts/Tools/perf_windows.py                       # 取 pain\\logs-archive 最新一份
  python Scripts/Tools/perf_windows.py --file <日志>
  python Scripts/Tools/perf_windows.py --all --top 3         # 最近 3 份各出一份报告
  python Scripts/Tools/perf_windows.py --file <日志> --out report.md
退出码：0 = 正常；2 = 用法/环境错误（找不到日志、一份窗口都没有）。

⚠ 分析前先 `python D:\\Download\\pain\\tools\\log_archive.py` 归档，再读归档副本。
"""

from __future__ import annotations

import argparse
import re
import statistics as st
import sys
from pathlib import Path

DEFAULT_DIR = Path(r"D:\Download\pain\logs-archive")

RX_WINDOW = re.compile(r"\[性能探针\] 窗口=(\d+)ms, fps均=([\d.]+), 引擎process=([\d.]+)ms/帧")
RX_NODES = re.compile(r"节点=(\d+), 对象=(\d+), 帧=(\d+)")
RX_FRAME = re.compile(r"帧时=均(\d+)ms/最坏(\d+)ms\(>200ms:(\d+)帧\)")          # r328
RX_FRAME_R327 = re.compile(r"帧时=(\d+)ms\(process均=([\d.]+)ms=(\d+)%, process最大=(\d+)ms\)")  # 已作废，仅兼容
RX_GC = re.compile(r"GC暂停=(\d+)ms\(占(\d+)%\), 分配=([\d.]+)MB\(=([\d.]+)MB/s\)")
RX_PROGRESS = re.compile(r"战斗=(-?\d+), 回合=(-?\d+), 楼层=(-?\d+), 席位=(-?\d+)")
RX_FAST = re.compile(r"加速档=(开|关)")
RX_RES = re.compile(
    r"资源\[VRAM=(\d+)MB\(([+-][\d.]+)\), 静态=(\d+)MB\(([+-][\d.]+)\), 托管堆=(\d+)MB\(([+-][\d.]+)\), "
    r"资源数=(\d+)\(([+-]\d+)\), 孤儿=(\d+)\(([+-]\d+)\), 根信号=(-?\d+), 绘制=(\d+), 图元=(\d+), "
    r"GC=(\d+)/(\d+)/(\d+)\]")
RX_SLOT = re.compile(r"([^,=]+)=([\d.]+)ms/\d+ms窗口\(每帧[\d.]+ms, 最大([\d.]+)ms")
RX_MARKER = re.compile(r"marker=([\dA-Za-z\-]+)")
RX_SWITCH = re.compile(r"\[性能探针\] 配置档切换.*?: (.*)$")
RX_CFG = re.compile(r"瓦库托管生效配置: .*?fastVakuuPlay=(True|False)")

# 我方逐帧槽位（父槽与子槽会重叠 —— 判读"我方占多少"只取顶层，别把子槽再加一遍）
OUR_TOP_SLOTS = ("切人按钮刷新", "结束回合门", "立绘探针", "结束按钮对账", "手牌顺序对账")


def parse(path: Path):
    marker = None
    rows = []
    switches = []
    cfg_fast = []
    for i, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        if marker is None:
            m = RX_MARKER.search(line)
            if m:
                marker = m.group(1)
        m = RX_SWITCH.search(line)
        if m:
            switches.append((i, m.group(1).strip()))
        m = RX_CFG.search(line)
        if m:
            cfg_fast.append((i, m.group(1) == "True"))
        m = RX_WINDOW.search(line)
        if not m:
            continue
        row = {
            "line": i,
            "window_ms": int(m.group(1)),
            "fps": float(m.group(2)),
            "process_last": float(m.group(3)),
            "frames": 0,
            "slots": [],
        }
        n = RX_NODES.search(line)
        if n:
            row["nodes"] = int(n.group(1))
            row["objects"] = int(n.group(2))
            row["frames"] = int(n.group(3))
        f = RX_FRAME.search(line)
        if f:
            row["frame_ms"] = int(f.group(1))
            row["frame_max"] = int(f.group(2))
            row["long_frames"] = int(f.group(3))
        else:
            # r327 的旧格式（process 占比已作废，只取帧时那个数）
            f = RX_FRAME_R327.search(line)
            if f:
                row["frame_ms"] = int(f.group(1))
        g = RX_GC.search(line)
        if g:
            row["gc_pause_ms"] = int(g.group(1))
            row["gc_pause_pct"] = int(g.group(2))
            row["alloc_mb"] = float(g.group(3))
            row["alloc_mbs"] = float(g.group(4))
        p = RX_PROGRESS.search(line)
        if p:
            row["combat"] = int(p.group(1))
            row["round"] = int(p.group(2))
            row["floor"] = int(p.group(3))
            row["seats"] = int(p.group(4))
        f = RX_FAST.search(line)
        if f:
            row["fast"] = f.group(1) == "开"
        res = RX_RES.search(line)
        if res:
            g = res.groups()
            row.update(
                vram=int(g[0]), static=int(g[2]), gcmem=int(g[4]),
                resources=int(g[6]), orphans=int(g[8]), rootsignals=int(g[10]),
                drawcalls=int(g[11]), primitives=int(g[12]), gc0=int(g[13]),
            )
        row["slots"] = [(k.strip(), float(v), float(mx)) for k, v, mx in RX_SLOT.findall(line)]
        row["ours_ms"] = sum(v for k, v, _ in row["slots"] if k in OUR_TOP_SLOTS)
        rows.append(row)
    return marker, rows, switches, cfg_fast


def med(vals):
    vals = [v for v in vals if v is not None]
    return st.median(vals) if vals else None


def cell(value, suffix=""):
    if value is None:
        return "-"
    if isinstance(value, float):
        return f"{value:.1f}{suffix}"
    return f"{value}{suffix}"


def summarize(rows):
    if not rows:
        return None
    floors = [r["floor"] for r in rows if r.get("floor", -1) >= 0]
    return {
        "n": len(rows),
        "fps": med([r["fps"] for r in rows]),
        "frame": med([r.get("frame_ms") for r in rows]),
        "frame_max": med([r.get("frame_max") for r in rows]),
        "long": med([r.get("long_frames") for r in rows]),
        "gc_pct": med([r.get("gc_pause_pct") for r in rows]),
        "alloc": med([r.get("alloc_mbs") for r in rows]),
        "ours": st.mean([r["ours_ms"] for r in rows]),
        "floor": (min(floors), max(floors)) if floors else None,
        "round": max((r.get("round", -1) for r in rows), default=None),
    }


def third_split(rows):
    n = len(rows)
    if n < 6:
        return [("全部", rows)]
    a, b = n // 3, 2 * n // 3
    return [("前1/3", rows[:a]), ("中1/3", rows[a:b]), ("后1/3", rows[b:])]


def report(path: Path) -> str:
    marker, rows, switches, cfg_fast = parse(path)
    out = [f"# 性能窗口判读：{path.name}", ""]
    out.append(f"- marker: `{marker or '未知'}`")
    total_min = sum(r["window_ms"] for r in rows) / 60000.0
    out.append(f"- 窗口数: {len(rows)}（每窗约 5s，合计约 {total_min:.1f} 分钟）")
    if not rows:
        out += ["", "⚠ 这份日志里没有 `[性能探针] 窗口=` 行 —— 要么不是战斗局，要么是 r282 之前的版本。"]
        return "\n".join(out)

    first, last = rows[0], rows[-1]
    if "seats" in first:
        out.append(f"- 席位: {first.get('seats')}（末窗 {last.get('seats')}）")
    floors = [r["floor"] for r in rows if r.get("floor", -1) >= 0]
    if floors:
        out.append(f"- 楼层: {min(floors)} ~ {max(floors)}")
    fasts = sorted({"开" if r["fast"] else "关" for r in rows if "fast" in r})
    out.append(f"- 加速档: {'/'.join(fasts) if fasts else '未记录（r327 之前的日志）'}")
    for ln, desc in switches:
        out.append(f"- 配置档切换 @L{ln}: {desc}")

    # ---- 一、三分位 ----
    out += ["", "## 一、三分位（回答「是不是越到后面越卡」）", ""]
    out.append("| 段 | 窗口 | fps 中位 | 帧时均中位 | 最坏帧中位 | 长帧(>200ms)中位 | GC暂停占中位 | 我方顶层槽位均 |")
    out.append("|---|---|---|---|---|---|---|---|")
    for tag, sel in third_split(rows):
        s = summarize(sel)
        out.append(
            f"| {tag} | {s['n']} | {cell(s['fps'])} | {cell(s['frame'], 'ms')} | "
            f"{cell(s['frame_max'], 'ms')} | {cell(s['long'])} | {cell(s['gc_pct'], '%')} | "
            f"{s['ours']:.1f}ms/窗 |")
    out.append("")
    out.append("判读：fps 中位**逐段下降** = 同一份日志里越打越慢；三段持平而绝对值一直很低 "
               "= 不是「打久了才卡」，而是这一局/这一层本来就这样（换档、换楼层都不变的话更像结构极限）。")

    # ---- 二、我方占比 ----
    out += ["", "## 二、我方逐帧开销占帧时间多少（回答「是不是我们自己的锅」）", ""]
    with_frame = [r for r in rows if "frame_ms" in r]
    if with_frame:
        frame_med = med([r["frame_ms"] for r in with_frame])
        ours_per_frame = med([r["ours_ms"] / max(1, r["window_ms"]) * r["frame_ms"] for r in with_frame])
        pct = ours_per_frame / frame_med * 100.0 if frame_med else 0.0
        out.append(f"- 帧时中位 **{frame_med:.0f}ms**；我方顶层槽位折合 **{ours_per_frame:.1f}ms/帧** "
                   f"= **{pct:.1f}%**")
    else:
        per_sec = med([r["ours_ms"] / max(1, r["window_ms"]) * 1000.0 for r in rows])
        out.append(f"- 这份日志没有 `帧时=` 段（r327 之前）；我方顶层槽位折合 **{per_sec:.1f}ms/秒**")
        out.append("- 折算成占比需要帧时：`我方ms/秒 ÷ 1000`，再对比 100% 减去渲染/等待的部分。")

    slot_total, slot_max = {}, {}
    for r in rows:
        for k, v, mx in r["slots"]:
            slot_total[k] = slot_total.get(k, 0.0) + v
            slot_max[k] = max(slot_max.get(k, 0.0), mx)
    if slot_total:
        out += ["", "| 槽位 | 均(ms/窗) | 最大单次 |", "|---|---|---|"]
        for k, v in sorted(slot_total.items(), key=lambda kv: -kv[1]):
            out.append(f"| {k} | {v / len(rows):.1f} | {slot_max[k]:.1f}ms |")

    # ---- 三、加速档切片 ----
    out += ["", "## 三、加速档切片（回答「开加速是不是真的更卡」）", ""]
    on = [r for r in rows if r.get("fast") is True]
    off = [r for r in rows if r.get("fast") is False]
    if on and off:
        out.append("| 档 | 窗口 | fps 中位 | 帧时均中位 | 最坏帧中位 | 长帧中位 | 我方(ms/窗) | 楼层范围 |")
        out.append("|---|---|---|---|---|---|---|---|")
        for tag, sel in (("加速开", on), ("加速关", off)):
            s = summarize(sel)
            fl = "-" if not s["floor"] else f"{s['floor'][0]}~{s['floor'][1]}"
            out.append(
                f"| {tag} | {s['n']} | {cell(s['fps'])} | {cell(s['frame'], 'ms')} | "
                f"{cell(s['frame_max'], 'ms')} | {cell(s['long'])} | {s['ours']:.1f} | {fl} |")
        out.append("")
        out.append("⚠ **先看两段的楼层范围**：楼层不同就不是干净 A/B（深入楼层本来就更重）。"
                   "要干净结论请用 `配置档切换` 锚点，在同一局面里来回切一次。")
    else:
        out.append("日志里没有 `加速档=` 段（r327 之前），只能按 `瓦库托管生效配置` 的 fastVakuuPlay "
                   "切换点与窗口行号粗略对齐：")
        for ln, flag in cfg_fast:
            before = [r for r in rows if r["line"] < ln]
            after = [r for r in rows if r["line"] >= ln]
            if not before:
                continue  # 开局/读档那几条 dump 落在第一个窗口之前，切分没有意义
            b = cell(summarize(before)["fps"])
            a = cell(summarize(after)["fps"] if after else None)
            out.append(f"- L{ln} fast={flag}: 之前 {len(before)} 窗（fps 中位 {b}）→ "
                       f"之后 {len(after)} 窗（fps 中位 {a}）")
        out.append("")
        out.append("⚠ 这种对齐**不是干净 A/B**：切换点往往同时伴随读档/换局（本例即是）。")

    # ---- 四、资源趋势 ----
    out += ["", "## 四、资源 / 泄漏趋势（回答「是不是漏了东西」）", ""]
    res_rows = [r for r in rows if "vram" in r]
    if res_rows:
        a, b = res_rows[0], res_rows[-1]
        out.append("| 项 | 首窗 | 末窗 | 变化 |")
        out.append("|---|---|---|---|")
        for key, name, unit in (("vram", "VRAM", "MB"), ("static", "静态内存", "MB"), ("gcmem", "托管堆", "MB"),
                                ("resources", "资源数", ""), ("orphans", "孤儿节点", ""),
                                ("rootsignals", "根视口信号", ""), ("drawcalls", "绘制调用", ""),
                                ("primitives", "图元", ""), ("gc0", "GC0 次数", "")):
            delta = b.get(key, 0) - a.get(key, 0)
            out.append(f"| {name} | {a.get(key)} | {b.get(key)} | {delta:+d}{unit} |")
        out += ["", "判读：**VRAM 或 资源数 持续涨** ⇒ 贴图/材质泄漏（渲染越来越慢）；"
                    "**托管堆涨 + GC0 暴涨** ⇒ 分配压力（逐帧 LINQ 是头号嫌疑）；"
                    "**根视口信号涨** ⇒ 每帧回调越挂越多（比节点泄漏更阴）。", ""]
        gc_pct = med([r.get("gc_pause_pct") for r in rows])
        alloc = med([r.get("alloc_mbs") for r in rows])
        if gc_pct is not None or alloc is not None:
            out.append(f"- GC 暂停占窗口时间中位 **{cell(gc_pct, '%')}**、分配速率中位 **{cell(alloc, 'MB/s')}** "
                       f"⇒ 若暂停占比 >10% 就说明「GC 抖动」确实是帧时间的一部分（可以拿它直接解释卡顿）；"
                       f"若很小，那 98% 的帧时间就落在引擎/第三方逻辑与渲染上，只能靠 bisect（solo / 分组）。")
            out.append("")
        node_first, node_last = a.get("nodes"), b.get("nodes")
        if node_first and node_last:
            out.append(f"（节点 {node_first}→{node_last}、对象 {a.get('objects')}→{b.get('objects')}："
                       f"平的就不必再谈「对象泄漏」。）")
    else:
        out.append("日志里没有 `资源[` 段（r327 之前）⇒ 这一份只能证伪「对象泄漏」（节点/对象数平），"
                   "显存 / 托管堆 / 信号连接三种都看不到。")

    # ---- 五、逐窗序列 ----
    out += ["", "## 五、逐窗序列（每 10 窗一行 + 末窗）", ""]
    out.append("| # | L | fps | 帧数 | 帧时均 | 最坏帧 | 长帧 | GC暂停 | 分配MB/s | 我方ms/窗 | 楼层 | 回合 | 加速 |")
    out.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for i, r in enumerate(rows):
        if i % 10 and i != len(rows) - 1:
            continue
        fast = "-" if "fast" not in r else ("开" if r["fast"] else "关")
        out.append(
            f"| {i} | {r['line']} | {r['fps']:.1f} | {r.get('frames', '?')} | {r.get('frame_ms', '-')} | "
            f"{r.get('frame_max', '-')} | {r.get('long_frames', '-')} | "
            f"{r.get('gc_pause_pct', '-')}% | {r.get('alloc_mbs', '-')} | {r['ours_ms']:.1f} | "
            f"{r.get('floor', '-')} | {r.get('round', '-')} | {fast} |")

    return "\n".join(out)


def newest_logs(root: Path, top: int):
    files = sorted(root.glob("godot*.log"), key=lambda p: p.stat().st_mtime, reverse=True)
    return files[:top]


def main() -> int:
    ap = argparse.ArgumentParser(description="[性能探针] 窗口判读")
    ap.add_argument("--file", help="单个日志文件")
    ap.add_argument("--logs-dir", default=str(DEFAULT_DIR), help="归档目录（默认 pain\\logs-archive）")
    ap.add_argument("--all", action="store_true", help="最近 N 份各出一份")
    ap.add_argument("--top", type=int, default=1, help="配合 --all 的份数")
    ap.add_argument("--out", help="把报告写到该 markdown 文件")
    args = ap.parse_args()

    if args.file:
        targets = [Path(args.file)]
    else:
        targets = newest_logs(Path(args.logs_dir), args.top if args.all else 1)
    targets = [p for p in targets if p.is_file()]
    if not targets:
        print(f"找不到日志：{args.file or args.logs_dir}", file=sys.stderr)
        return 2

    text = "\n\n---\n\n".join(report(p) for p in targets)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"已写出: {args.out}")
        return 0
    print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
