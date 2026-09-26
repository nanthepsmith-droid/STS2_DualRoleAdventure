#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
第三方补丁目标交叉分析（thirdparty_patch_overlap）

回答一个问题：**某个第三方 mod 和我们打在同一个游戏方法上吗？**

为什么需要它（背景）：本 mod 与「Co-op Bots」这类第三方 mod 是**版本锁定依赖** —— 游戏或第三方一更新，
双方的补丁目标字符串会同时失效。r144 做 CB 兼容 POC 时临时写过一次性脚本，结论是
「16 个类型级重叠 / 其中 9 个类型 11 个方法**同方法**重叠」（多于 `Co-op_Bots联机队友兼容可行性分析.md`
§6 表里列的 6 处），但那个脚本**没有落库**，导致：
  - Phase 3「补丁面回归」没有可复跑的清单；
  - 第三方更新后无法一条命令重新对账。
本脚本把它固化（对应分析文档 §6 的 2026-09-25 订正：**Phase 3 若要固化，先把这个脚本写进 Scripts/Tools/**）。

输入（两侧都是「声明的 Harmony 补丁目标」）：
  A. **我方**：`Scripts/Tools/baselines/targets.baseline.txt` 的 `P` 行
     （列：P / 补丁类 / 目标类型 / 目标方法 / 种类），由 `static_checks.py --update-baseline` 生成。
  B. **第三方**：源码目录下所有 `*.cs` 里声明的 `[HarmonyPatch(...)]`（类型级 + 方法级）。

用法:
  python Scripts/Tools/thirdparty_patch_overlap.py --thirdparty "D:\\Download\\STS2-Co-op_Bots-main\\src"
  python Scripts/Tools/thirdparty_patch_overlap.py --thirdparty <dir> --md report.md --json report.json
  python Scripts/Tools/thirdparty_patch_overlap.py --thirdparty <dir> --strict --expect-overlap-methods 11

退出码: 0 = 正常（含「第三方源码目录不存在 → SKIP」）；
        1 = 带 --strict 且有期望值不符，或带 --strict 但没有第三方源码目录。

⚠ 本脚本**只读**，不参与强制门禁链（依赖仓库外的第三方源码目录；`git clone` 后自包含性不成立）。

对账（2026-09-25，第三方 = `STS2-Co-op_Bots-main` v0.39.0，我方 = marker r148 的 baseline）：
  **类型级重叠 17 个 / 同方法重叠 11 处（9 个类型、我方 12 个补丁类）** —— 与 r144 一次性脚本的
  「9 个类型 11 个方法」**逐条一致**；「类型级 16」那一处差 1，r144 未留脚本，按本脚本口径（17）为准。
"""

import argparse
import io
import json
import os
import re
import sys

# 本机控制台是 GBK，直接 print 中文会 UnicodeEncodeError → 强制 stdout/stderr 走 UTF-8。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_BASELINE = os.path.join("Scripts", "Tools", "baselines", "targets.baseline.txt")

# 我方补丁类 → 定义文件（报告里好定位；baseline 刻意不含文件与行号，所以这里另扫一遍源码）。
CLASS_DECL_RE = re.compile(r"\b(?:static\s+)?(?:partial\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)")
TYPEOF_RE = re.compile(r"typeof\s*\(\s*([A-Za-z_][A-Za-z0-9_\.]*)\s*\)")
NAMEOF_RE = re.compile(r"nameof\s*\(\s*([A-Za-z_][A-Za-z0-9_\.]*)\s*\)")
STRING_ARG_RE = re.compile(r'"([^"]*)"')


def read_text(path):
    """按 BOM 自动识别编码读取（UTF-16 LE/BE / UTF-8），失败时按 UTF-8 宽容读。"""
    with open(path, "rb") as handle:
        head = handle.read(4)
    if head.startswith(b"\xff\xfe"):
        encoding = "utf-16-le"
    elif head.startswith(b"\xfe\xff"):
        encoding = "utf-16-be"
    else:
        encoding = "utf-8-sig"
    return io.open(path, "r", encoding=encoding, errors="replace", newline="").read()


def split_args(text):
    """把 `typeof(Foo), nameof(Foo.Bar), MethodType.Getter` 拆成顶层参数（跳过嵌套圆括号与字符串）。"""
    args = []
    depth = 0
    current = []
    in_string = False
    for char in text:
        if char == '"':
            in_string = not in_string
            current.append(char)
            continue
        if not in_string:
            if char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
                if depth < 0:
                    break
            elif char == "," and depth == 0:
                args.append("".join(current).strip())
                current = []
                continue
        current.append(char)
    tail = "".join(current).strip()
    if tail:
        args.append(tail)
    return args


def iter_harmony_patch_args(source):
    """遍历源码里每个 `[HarmonyPatch(...)]` 的属性参数文本（按括号配平切分，支持跨行）。"""
    needle = "HarmonyPatch("
    index = 0
    while True:
        index = source.find(needle, index)
        if index < 0:
            return
        start = index + len(needle)
        depth = 1
        cursor = start
        in_string = False
        while cursor < len(source) and depth > 0:
            char = source[cursor]
            if char == '"':
                in_string = not in_string
            elif not in_string:
                if char == "(":
                    depth += 1
                elif char == ")":
                    depth -= 1
            cursor += 1
        yield source[start:cursor - 1]
        index = cursor


def parse_targets(args_text):
    """属性参数 → (目标类型 或 None, 目标方法 或 None)。"""
    args = split_args(args_text)
    if not args:
        return None, None
    typeof_match = TYPEOF_RE.search(args[0])
    if typeof_match is None:
        # 方法级 `[HarmonyPatch("MethodName")]`（写在补丁方法上、类型由同类别的类级属性给出）：
        # 这里保守地只认字符串/nameof，不猜类型。
        name = None
        nameof_match = NAMEOF_RE.search(args_text)
        if nameof_match:
            name = nameof_match.group(1).split(".")[-1]
        else:
            string_match = STRING_ARG_RE.search(args_text)
            if string_match:
                name = string_match.group(1)
        return None, name
    type_name = typeof_match.group(1).split(".")[-1]
    method_name = None
    for arg in args[1:]:
        nameof_match = NAMEOF_RE.search(arg)
        if nameof_match:
            method_name = nameof_match.group(1).split(".")[-1]
            break
        string_match = STRING_ARG_RE.search(arg)
        if string_match:
            method_name = string_match.group(1)
            break
    return type_name, method_name


def scan_thirdparty(root):
    """扫第三方源码 → {类型: {方法 或 None: [(相对文件, 行号)]}}。"""
    result = {}
    for dirpath, _dirnames, filenames in os.walk(root):
        for filename in filenames:
            if not filename.endswith(".cs"):
                continue
            full = os.path.join(dirpath, filename)
            try:
                source = read_text(full)
            except OSError:
                continue
            for args_text in iter_harmony_patch_args(source):
                type_name, method_name = parse_targets(args_text)
                if type_name is None and method_name is None:
                    continue
                line = source.count("\n", 0, source.find(args_text)) + 1
                rel = os.path.relpath(full, root)
                result.setdefault(type_name, {}).setdefault(method_name, []).append((rel, line))
    return result


def scan_our_classes(repo):
    """扫我方 Scripts/**/*.cs → {补丁类: 相对文件}（仅用于报告定位）。"""
    result = {}
    root = os.path.join(repo, "Scripts")
    for dirpath, _dirnames, filenames in os.walk(root):
        for filename in filenames:
            if not filename.endswith(".cs"):
                continue
            full = os.path.join(dirpath, filename)
            try:
                source = read_text(full)
            except OSError:
                continue
            for match in CLASS_DECL_RE.finditer(source):
                result.setdefault(match.group(1), os.path.relpath(full, repo))
    return result


def load_our_targets(baseline_path):
    """读 baseline 的 P 行 → [(补丁类, 类型, 方法)]（去重）。"""
    rows = []
    seen = set()
    if not os.path.isfile(baseline_path):
        return rows
    for raw in read_text(baseline_path).splitlines():
        if not raw.startswith("P\t"):
            continue
        cells = raw.split("\t")
        if len(cells) < 5:
            continue
        key = (cells[1], cells[2], cells[3])
        if key in seen:
            continue
        seen.add(key)
        rows.append(key)
    return rows


def main():
    parser = argparse.ArgumentParser(
        description="第三方补丁目标交叉分析（我方 baseline × 第三方 [HarmonyPatch] 声明）")
    parser.add_argument("--repo", default=".", help="本 mod 仓库根（默认当前目录）")
    parser.add_argument("--baseline", default=None, help="我方目标基线文件（默认 Scripts/Tools/baselines/targets.baseline.txt）")
    parser.add_argument("--thirdparty", required=True, help="第三方 mod 源码目录（其下递归找 *.cs）")
    parser.add_argument("--md", help="把报告写成 markdown（给决策文档 / TODO 用）")
    parser.add_argument("--json", dest="json_out", help="把结果写成 JSON")
    parser.add_argument("--strict", action="store_true", help="收尾按期望值断言（不符退出码 1）")
    parser.add_argument("--expect-overlap-types", type=int, help="期望的「类型级重叠」数量")
    parser.add_argument("--expect-overlap-methods", type=int, help="期望的「同方法重叠」数量")
    args = parser.parse_args()

    repo = os.path.abspath(args.repo)
    baseline_path = args.baseline or os.path.join(repo, DEFAULT_BASELINE)
    our_targets = load_our_targets(baseline_path)
    if not our_targets:
        sys.stderr.write("没有读到我的目标基线: {0}\n".format(baseline_path))
        return 1

    if not os.path.isdir(args.thirdparty):
        print("[SKIP] 第三方源码目录不存在，跳过交叉分析: {0}".format(args.thirdparty))
        print("       （想跑就把它 clone 到本地并用 --thirdparty 指过去）")
        return 1 if args.strict else 0

    theirs = scan_thirdparty(args.thirdparty)
    our_classes = scan_our_classes(repo)

    their_types = set(theirs.keys()) - {None}
    our_types = {type_name for _, type_name, _ in our_targets if type_name}

    type_overlap = sorted(their_types & our_types)

    # 按「游戏方法」聚合（同一个方法可能被我方多个补丁类打 —— 那仍是 1 处重叠）。
    grouped = {}
    for patch_class, type_name, method_name in our_targets:
        by_method = theirs.get(type_name)
        if not by_method or method_name not in by_method:
            continue
        key = "{0}.{1}".format(type_name, method_name)
        entry = grouped.setdefault(key, {"target": key, "ourPatches": [], "theirSources": []})
        entry["ourPatches"].append({
            "patchClass": patch_class,
            "file": our_classes.get(patch_class, "?"),
        })
        entry["theirSources"].extend("{0}:{1}".format(rel, line) for rel, line in by_method[method_name])
    method_overlap = []
    for entry in grouped.values():
        entry["ourPatches"].sort(key=lambda item: item["patchClass"])
        entry["theirSources"] = sorted(set(entry["theirSources"]))
        method_overlap.append(entry)
    method_overlap.sort(key=lambda item: item["target"])
    overlap_type_names = sorted({item["target"].split(".")[0] for item in method_overlap})
    our_patch_count = sum(len(item["ourPatches"]) for item in method_overlap)

    print("== 补丁目标交叉分析 ==")
    print("我方基线: {0}（补丁目标 {1} 行（去重后）/ 类型 {2} 个）".format(
        os.path.relpath(baseline_path, repo), len(our_targets), len(our_types)))
    print("第三方源: {0}（声明目标类型 {1} 个）".format(args.thirdparty, len(their_types)))
    print("")
    print("类型级重叠: {0} 个".format(len(type_overlap)))
    for name in type_overlap:
        print("    - {0}".format(name))
    print("")
    print("方法级重叠（同方法）: {0} 处 / 涉及 {1} 个类型（我方共 {2} 个补丁类）".format(
        len(method_overlap), len(overlap_type_names), our_patch_count))
    for item in method_overlap:
        print("    - {0}".format(item["target"]))
        for patch in item["ourPatches"]:
            print("        我方: {0}（{1}）".format(patch["patchClass"], patch["file"]))
        print("        对方: {0}".format("; ".join(item["theirSources"])))
    print("")
    print("注：这里是**静态声明**层面的重叠（谁和谁打了同一个方法），")
    print("    不代表行为冲突 —— 逐条的实机判据见决策文档 §R2。")

    if args.md:
        with io.open(args.md, "w", encoding="utf-8", newline="") as handle:
            handle.write("# 第三方补丁目标交叉分析报告\n\n")
            handle.write("> 由 `Scripts/Tools/thirdparty_patch_overlap.py` 生成，勿手改。\n")
            handle.write("> 第三方源: `{0}`\n\n".format(args.thirdparty))
            handle.write("类型级重叠 **{0}** 个；同方法重叠 **{1}** 处 / 涉及 **{2}** 个类型"
                         "（我方共 **{3}** 个补丁类）。\n\n"
                         .format(len(type_overlap), len(method_overlap),
                                 len(overlap_type_names), our_patch_count))
            handle.write("| 游戏方法 | 本 mod 补丁类（文件） | 第三方声明位置 |\n|---|---|---|\n")
            for item in method_overlap:
                ours = "<br>".join("`{0}`（`{1}`）".format(patch["patchClass"], patch["file"])
                                   for patch in item["ourPatches"])
                handle.write("| `{0}` | {1} | {2} |\n".format(
                    item["target"], ours,
                    "<br>".join("`{0}`".format(s) for s in item["theirSources"])))
            handle.write("\n> 逐条**实机判据**（看哪些日志锚点）写在 "
                         "`Co-op_Bots联机队友兼容可行性分析.md` §R2「逐条实测清单」。\n")
            handle.write("\n## 类型级重叠（不同方法，风险低）\n\n")
            for name in type_overlap:
                handle.write("- `{0}`\n".format(name))
        sys.stderr.write("# 已写出 markdown: {0}\n".format(args.md))

    if args.json_out:
        payload = {
            "thirdpartyRoot": args.thirdparty,
            "overlapTypes": type_overlap,
            "overlapMethods": method_overlap,
            "thirdpartyTypes": sorted(their_types),
        }
        with io.open(args.json_out, "w", encoding="utf-8", newline="") as handle:
            handle.write(json.dumps(payload, ensure_ascii=False, indent=2))
        sys.stderr.write("# 已写出 JSON: {0}\n".format(args.json_out))

    if args.strict:
        failures = []
        if args.expect_overlap_types is not None and len(type_overlap) != args.expect_overlap_types:
            failures.append("类型级重叠 {0} != 期望 {1}".format(len(type_overlap), args.expect_overlap_types))
        if args.expect_overlap_methods is not None and len(method_overlap) != args.expect_overlap_methods:
            failures.append("同方法重叠 {0} != 期望 {1}".format(len(method_overlap), args.expect_overlap_methods))
        for text in failures:
            sys.stderr.write("# [strict] {0}\n".format(text))
        if failures:
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
