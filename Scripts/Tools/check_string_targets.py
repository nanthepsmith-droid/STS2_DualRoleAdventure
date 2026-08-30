#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
字符串 / 反射目标核对器（check_string_targets）

扫描 DualRoleAdventure / LocalMultiControl 仓库的 Scripts/Patch/*.cs，
找出所有「字符串式」目标，并在反编译参考源码（sts2src）中核对是否存在。

字符串式目标 = 运行期才解析、编译期无法检查的目标，游戏更新改名 / 删成员会静默失效：

  A. HarmonyPatch 字符串目标：[HarmonyPatch(typeof(X), "成员名")]
  B. AccessTools 反射调用：AccessTools.Field/Method/Property(typeof(X), "成员")、
     AccessTools.TypeByName("MegaCrit.Sts2...X")
  C. 类型反射：typeof(X).GetMethod / GetProperty / GetNestedType("成员")

复用 patch_coverage.py 的解析与反编译核对机制（对应《mod系统维护性改进实施方案》任务 1.3）。

用法:
  python check_string_targets.py --repo <STS2仓库路径>
                                 [--src <反编译源码目录>] [--no-src-check]
                                 [--json] [--out FILE]

输出：Markdown（默认）或 JSON，含统计与失效项行号。
退出码：0 = 无失效；1 = 有 STALE-TYPE / STALE-METHOD（可进 CI）；2 = 用法 / 环境错误。

示例:
  python Scripts/Tools/check_string_targets.py --repo .
  python Scripts/Tools/check_string_targets.py --repo . --json
  python Scripts/Tools/check_string_targets.py --repo . --out docs/string-targets.md
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# 复用任务 1.1 的解析与核对机制（脚本与 patch_coverage.py 同目录）
sys.path.insert(0, str(Path(__file__).resolve().parent))
import patch_coverage as pc

# ------------------------------------------------------------------ 正则
RE_ACCESSTOOLS = re.compile(r"AccessTools\.(\w+)\s*\(")
RE_TYPEOF = re.compile(r"typeof\s*\(\s*([^)]+?)\s*\)")
RE_STRING_LIT = re.compile(r'"((?:[^"\\]|\\.)*)"')
RE_NAMEOF = re.compile(r"nameof\s*\(\s*([^)]+?)\s*\)")
RE_GET_MEMBER = re.compile(r"\.(GetMethod|GetProperty|GetNestedType|GetField|GetEvent)\s*\(")
RE_TYPEOF_GETMEMBER = re.compile(
    r"typeof\s*\(\s*([^)]+?)\s*\)\.(GetMethod|GetProperty|GetNestedType|GetField|GetEvent)"
    r"\s*\(\s*\"((?:[^\"\\]|\\.)*)\""
)
# 只保留反编译源码中像「声明」的字段/属性行，排除调用点
RE_FIELD_DEF = re.compile(
    r"^\s*(?:(?:public|private|internal|protected)\s+)*"
    r"(?:(?:static|const|readonly|volatile|new)\s+)*"
    r"[\w<>,\.\s\?\[\]]+?\s+"
)

TYPE_KIND = {"Field": "field", "Property": "property", "PropertyGetter": "property",
             "PropertySetter": "property", "Method": "method", "Constructor": "method",
             "MethodDelegate": "method", "DeclaredMethod": "method", "DeclaredProperty": "property"}

# 已知「新名优先 + 旧名反射回退」目标（AGENTS.md §5 的既定模式）——
# 反编译源码中只存在新名，旧名是给旧游戏版本兜底的，**不是失效**。
# 匹配用「文件名 + 成员名」，命中即标 LEGACY-FALLBACK 且不参与失败计数。
KNOWN_LEGACY_FALLBACKS = {
    ("CombatManagerReadyEnemyTurnPatch.cs", "_playersReadyToBeginEnemyTurn"),
    ("LoadRunLobbyPatch.cs", "BeginRunIfAllPlayersReady"),
    ("StartRunLobbySetReadyPatch.cs", "BeginRunIfAllPlayersReady"),
}


def balanced_extract(text: str, open_idx: int):
    """text[open_idx] 为 '('，返回 (配平括号内文本, 结束下标)。未闭合时返回 (剩余文本, 末尾)。"""
    depth = 0
    i = open_idx
    n = len(text)
    while i < n:
        c = text[i]
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[open_idx + 1:i], i
        i += 1
    return text[open_idx + 1:], n


def line_of(text: str, pos: int) -> int:
    return text.count("\n", 0, pos) + 1


def typeof_to_simple(expr: str) -> str:
    """typeof(...) 参数 → 简单类型名。
    全限定名（MegaCrit.Sts2...）取最后一段；含成员访问表达式（如 X.Instance）取第一段。"""
    expr = expr.strip()
    if expr.startswith("MegaCrit.Sts2") or expr.startswith("Sts2"):
        return expr.split(".")[-1]
    return expr.split(".")[0]


def op_to_kind(op: str) -> str:
    return TYPE_KIND.get(op, "method")


def field_def_pattern(name: str):
    return re.compile(
        RE_FIELD_DEF.pattern + re.escape(name) + r"\s*(?:;|=|\{|$)",
        re.MULTILINE,
    )


# ------------------------------------------------------------------ 目标对象
class Target:
    def __init__(self, kind: str, type_simple=None, fqn=None, member=None,
                 member_kind: str = "method", nameof: bool = False,
                 dynamic: bool = False, path: str = "", lineno: int = 0, note: str = ""):
        self.kind = kind            # harmony | accesstools | getmember
        self.type_simple = type_simple
        self.fqn = fqn
        self.member = member
        self.member_kind = member_kind
        self.nameof = nameof
        self.dynamic = dynamic
        self.path = path
        self.lineno = lineno
        self.note = note
        self.status = "pending"
        self.anchor = ""


# ------------------------------------------------------------------ 核对
def find_member_in_files(member: str, kind: str, files):
    """在候选文件集中定位成员（方法/属性/字段）定义，返回 (文件路径, 行号) 或 None。"""
    files = list(files)
    if kind in ("method", "property"):
        hit = pc.find_method_in_files(member, files)
        if hit:
            return hit
    if kind == "property":
        pat = pc._property_def_pattern(member)
        for f in files:
            try:
                text = Path(f).read_text(encoding="utf-8-sig", errors="replace")
            except OSError:
                continue
            m = pat.search(text)
            if m:
                return f, text.count("\n", 0, m.start()) + 1
    if kind == "field":
        pat = field_def_pattern(member)
        for f in files:
            try:
                text = Path(f).read_text(encoding="utf-8-sig", errors="replace")
            except OSError:
                continue
            m = pat.search(text)
            if m:
                return f, text.count("\n", 0, m.start()) + 1
    return None


def verify_target(t: Target, index, fqn_index) -> None:
    if t.dynamic:
        t.status = "SKIP-DYNAMIC"
        t.note = "接收者为运行时表达式，无法静态核对"
        return
    if t.nameof:
        t.status = "SKIP-NAMEOF"
        t.note = "nameof 由编译期保证，无需核对"
        return
    if t.fqn:
        if fqn_index and t.fqn in fqn_index:
            t.status = "verified"
            t.anchor = fqn_index[t.fqn]
        else:
            t.status = "STALE-TYPE"
            t.note = f"全限定类型 {t.fqn} 未找到"
        return
    files = index.get(t.type_simple)
    if not files:
        t.status = "STALE-TYPE"
        t.note = f"类型 {t.type_simple} 未找到"
        return
    if t.member:
        hit = find_member_in_files(t.member, t.member_kind, files)
        if hit:
            t.status = "verified"
            t.anchor = f"{hit[0]} (L{hit[1]})"
        else:
            t.status = "STALE-METHOD"
            t.note = f"类型 {t.type_simple} 的 {t.member_kind}「{t.member}」未找到"
        return
    t.status = "verified"
    t.anchor = next(iter(files))


# ------------------------------------------------------------------ 旧名回退识别
def split_camel(name: str) -> set[str]:
    """把 camelCase / PascalCase 拆成小写词元（≥3 字符），用于近似成员名比较。"""
    return {p.lower() for p in re.findall(r"[A-Z][a-z0-9]*|[a-z][a-z0-9]*", name) if len(p) >= 3}


def mark_legacy_fallback(targets: list[Target]) -> None:
    """把两类「预期旧名」从 STALE 中摘出：
    1. 命中 KNOWN_LEGACY_FALLBACKS 白名单（按文件名+成员名）；
    2. 同一文件、同一类型下已有 verified 成员，且与 STALE 成员名共享 ≥2 个词元
       （典型的新名优先 + 旧名回退双写，如 BeginRunForAllPlayersIfAllReady 与 BeginRunIfAllPlayersReady）。
    """
    by_key = {}
    for t in targets:
        if not t.type_simple:
            continue
        by_key.setdefault((t.path, t.type_simple), []).append(t)

    for (path, _type), group in by_key.items():
        verified = [t for t in group if t.status == "verified" and t.member]
        for t in group:
            if t.status not in ("STALE-TYPE", "STALE-METHOD") or not t.member:
                continue
            if (Path(path).name, t.member) in KNOWN_LEGACY_FALLBACKS:
                t.status = "LEGACY-FALLBACK"
                t.note = "已知旧名回退（新名优先，旧名供旧版本兜底），预期非失效"
                continue
            tk = split_camel(t.member)
            for v in verified:
                vk = split_camel(v.member)
                if len(tk & vk) >= 2:
                    t.status = "LEGACY-FALLBACK"
                    t.note = f"同类型已有 verified 的近似成员「{v.member}」（疑似新名优先的旧名回退）"
                    break


# ------------------------------------------------------------------ 扫描
def scan_access_tools(text: str, path: str) -> list[Target]:
    """扫描 AccessTools.*(...) 反射调用。"""
    out = []
    for m in RE_ACCESSTOOLS.finditer(text):
        op = m.group(1)
        inner, _ = balanced_extract(text, m.end() - 1)
        lineno = line_of(text, m.start())

        # TypeByName("全限定名")
        if op == "TypeByName":
            sm = RE_STRING_LIT.search(inner)
            if sm:
                out.append(Target("accesstools", fqn=sm.group(1),
                                  path=str(path), lineno=lineno))
            else:
                out.append(Target("accesstools", dynamic=True,
                                  path=str(path), lineno=lineno))
            continue

        tm = RE_TYPEOF.search(inner)
        if tm:
            type_simple = typeof_to_simple(tm.group(1))
            nm = RE_NAMEOF.search(inner)
            if nm:
                member = nm.group(1).split(".")[-1]
                out.append(Target("accesstools", type_simple=type_simple,
                                  member=member, member_kind=op_to_kind(op),
                                  nameof=True, path=str(path), lineno=lineno))
                continue
            sm = RE_STRING_LIT.search(inner)
            if sm:
                out.append(Target("accesstools", type_simple=type_simple,
                                  member=sm.group(1), member_kind=op_to_kind(op),
                                  path=str(path), lineno=lineno))
                continue
            # 有 typeof 但无字符串/nameof 成员（如构造函数补丁）→ 类型级目标
            out.append(Target("accesstools", type_simple=type_simple,
                              path=str(path), lineno=lineno))
            continue

        # 无 typeof → 动态接收者（如 xxx.GetType()）
        out.append(Target("accesstools", dynamic=True,
                          path=str(path), lineno=lineno))
    return out


def scan_get_member(text: str, path: str) -> list[Target]:
    """扫描 .GetMethod/.GetProperty/.GetNestedType("...") 类型反射调用。"""
    out = []
    for m in RE_TYPEOF_GETMEMBER.finditer(text):
        type_simple = typeof_to_simple(m.group(1))
        member = m.group(3)
        op = m.group(2)
        if op == "GetNestedType":
            out.append(Target("getmember", fqn=None, type_simple=type_simple,
                              member=member, member_kind="type",
                              path=str(path), lineno=line_of(text, m.start())))
        else:
            kind = "property" if op == "GetProperty" else "method"
            out.append(Target("getmember", type_simple=type_simple,
                              member=member, member_kind=kind,
                              path=str(path), lineno=line_of(text, m.start())))
    # 其余 .GetXxx("...")：接收者为运行时表达式，标 SKIP-DYNAMIC 备查
    for m in RE_GET_MEMBER.finditer(text):
        if RE_TYPEOF_GETMEMBER.search(text, m.start(), m.end()):
            continue
        inner, _ = balanced_extract(text, m.end() - 1)
        sm = RE_STRING_LIT.search(inner)
        if not sm:
            continue
        out.append(Target("getmember", member=sm.group(1), dynamic=True,
                          path=str(path), lineno=line_of(text, m.start())))
    return out


# ------------------------------------------------------------------ 全限定名索引
def build_fqn_index(src_root: Path) -> dict:
    """返回 {命名空间.类名: 文件路径}。仅解析 namespace + 顶层类/结构，近似够用。"""
    fqn = {}
    re_ns = re.compile(r"^\s*namespace\s+([A-Za-z_][\w\.]*)")
    for f in src_root.rglob("*.cs"):
        try:
            text = f.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        ns = None
        for line in text.splitlines():
            m = re_ns.match(line)
            if m:
                ns = m.group(1)
                break
        if ns is None:
            continue
        for line in text.splitlines():
            m = pc.RE_CLASS.match(line)
            if m:
                fqn.setdefault(ns + "." + m.group(1), str(f))
                continue
            m = pc.RE_STRUCT.match(line)
            if m:
                fqn.setdefault(ns + "." + m.group(1), str(f))
    return fqn


# ------------------------------------------------------------------ 主流程
def collect_targets(repo: Path, src_root: Path | None):
    """返回 (targets, patch_files, patch_classes, src_file_count)。"""
    result = pc.analyze(repo, src_root)
    targets = []
    # Part A：HarmonyPatch 字符串目标（is_string=True）
    for r in result["rows"]:
        if not r.is_string:
            continue
        note = r.note or ""
        targets.append(Target("harmony", type_simple=r.type_simple,
                              member=r.method, member_kind="method",
                              path=r.patch_class.path, lineno=r.attr_line, note=note))
    # Part B：反射调用
    patch_dir = repo / "Scripts" / "Patch"
    for f in sorted(patch_dir.rglob("*.cs")):
        text = f.read_text(encoding="utf-8-sig", errors="replace")
        targets.extend(scan_access_tools(text, f))
        targets.extend(scan_get_member(text, f))
    return targets, result["patch_files"], result["patch_classes"], result["src_file_count"]


def render_markdown(targets, repo, src_root, src_file_count) -> str:
    now = __import__("datetime").datetime.now().strftime("%Y-%m-%d %H:%M")
    L = []
    L.append("# String Targets — 字符串/反射目标核对")
    L.append("")
    L.append(f"> 生成时间：{now}")
    L.append(f"> 扫描目录：`Scripts/Patch/`（由 patch_coverage 提供补丁行）")
    L.append(f"> 反编译参考：{src_root or '未找到（--no-src-check 模式）'}"
             + (f"（{src_file_count} 个 .cs）" if src_file_count else ""))
    L.append("> 用途：游戏更新后核对**运行期字符串目标**是否失效。由 `Scripts/Tools/check_string_targets.py` 生成，勿手改。")
    L.append("")

    counts = {}
    for t in targets:
        counts[t.status] = counts.get(t.status, 0) + 1
    total = len(targets)
    stale = counts.get("STALE-TYPE", 0) + counts.get("STALE-METHOD", 0)

    L.append("## 统计")
    L.append("")
    L.append(f"- 字符串/反射目标总数：**{total}**")
    L.append(f"- 已验证：**{counts.get('verified', 0)}**")
    L.append(f"- **失效（STALE-TYPE / STALE-METHOD）：{stale}**")
    if stale:
        L.append(f"- 提示：`exit code = 1`，请优先修复（见下方失效清单）。")
    L.append(f"- LEGACY-FALLBACK：{counts.get('LEGACY-FALLBACK', 0)}（新名优先的旧名回退，预期非失效）")
    L.append(f"- SKIP-DYNAMIC：{counts.get('SKIP-DYNAMIC', 0)}（运行时表达式，无法静态核对）")
    L.append(f"- SKIP-NAMEOF：{counts.get('SKIP-NAMEOF', 0)}（nameof 编译期安全）")
    L.append("")

    stale_rows = [t for t in targets if t.status in ("STALE-TYPE", "STALE-METHOD")]
    L.append("## ⚠️ 失效目标（游戏更新后最可能出现在这里）")
    L.append("")
    if stale_rows:
        L.append("| 文件 | 行号 | 来源 | 目标类型 | 目标成员 | 状态 | 说明 |")
        L.append("|---|---|---|---|---|---|---|")
        for t in sorted(stale_rows, key=lambda x: (x.path, x.lineno)):
            src = {"harmony": "HarmonyPatch", "accesstools": "AccessTools",
                   "getmember": "GetXxx"}[t.kind]
            type_col = t.fqn or t.type_simple or "—"
            L.append(f"| `{t.path}` | {t.lineno} | {src} | `{type_col}` | "
                     f"`{t.member or '—'}` | **{t.status}** | {t.note} |")
        L.append("")
    else:
        L.append("无。")
        L.append("")

    legacy_rows = [t for t in targets if t.status == "LEGACY-FALLBACK"]
    if legacy_rows:
        L.append("## 旧名回退（LEGACY-FALLBACK，预期，不计失败）")
        L.append("")
        L.append("| 文件 | 行号 | 目标类型 | 目标成员 | 说明 |")
        L.append("|---|---|---|---|---|")
        for t in sorted(legacy_rows, key=lambda x: (x.path, x.lineno)):
            L.append(f"| `{t.path}` | {t.lineno} | `{t.type_simple}` | `{t.member}` | {t.note} |")
        L.append("")

    L.append("## 全量清单")
    L.append("")
    L.append("| # | 文件 | 行号 | 来源 | 类型 | 成员 | 类型(成员) | 状态 | 反编译锚点 |")
    L.append("|---|---|---|---|---|---|---|---|---|")
    for i, t in enumerate(targets, start=1):
        src = {"harmony": "HarmonyPatch", "accesstools": "AccessTools",
               "getmember": "GetXxx"}[t.kind]
        type_col = f"`{t.fqn or t.type_simple}`" if (t.fqn or t.type_simple) else "—"
        member_col = f"`{t.member}`" if t.member else "—"
        kind_col = t.member_kind if t.member else "—"
        anchor = f"`{t.anchor}`" if t.anchor else "—"
        note = f" {t.note}" if t.note else ""
        L.append(f"| {i} | `{t.path}` | {t.lineno} | {src} | {type_col} | {member_col} | "
                 f"{kind_col} | **{t.status}**{note} | {anchor} |")
    L.append("")
    L.append("> 状态含义：`verified` 类型与成员均在反编译源码中找到；`STALE-TYPE` 类型疑似失效；")
    L.append("> `STALE-METHOD` 类型在但成员疑似失效（改名为最常见原因）；`LEGACY-FALLBACK` 新名优先的旧名回退（预期）；")
    L.append("> `SKIP-DYNAMIC` 运行时表达式无法核对；`SKIP-NAMEOF` 编译期保证。锚点为近似定位（按类型简名 + 成员定义行匹配）。")
    return "\n".join(L)


def main():
    ap = argparse.ArgumentParser(description="字符串/反射目标核对器")
    ap.add_argument("--repo", required=True, help="STS2_DualRoleAdventure 仓库路径")
    ap.add_argument("--src", help="反编译参考源码目录（默认探测 repo/src 与 repo/../sts2src/src）")
    ap.add_argument("--no-src-check", action="store_true", help="跳过反编译交叉核对（状态均置 unverified）")
    ap.add_argument("--json", action="store_true", help="输出 JSON 而非 Markdown")
    ap.add_argument("--out", help="输出文件路径（默认 stdout）")
    args = ap.parse_args()

    # Windows 控制台编码兜底，避免中文输出崩
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass

    repo = Path(args.repo).resolve()
    patch_dir = repo / "Scripts" / "Patch"
    if not patch_dir.exists():
        print(f"错误: 找不到补丁目录 {patch_dir}", file=sys.stderr)
        sys.exit(2)

    src_root = None
    if not args.no_src_check:
        src_root = pc.locate_src_dir(repo, args.src)
        if src_root is None:
            print("错误: 未找到反编译源码（已尝试 repo/src 与 repo/../sts2src/src）。"
                  "可用 --src 指定，或 --no-src-check 跳过核对。", file=sys.stderr)
            sys.exit(2)

    index, _ = pc.build_type_index(src_root) if src_root else ({}, 0)
    fqn_index = build_fqn_index(src_root) if src_root else {}

    targets, patch_files, patch_classes, src_file_count = collect_targets(repo, src_root)
    for t in targets:
        if src_root:
            verify_target(t, index, fqn_index)
        else:
            t.status = "unverified"
    mark_legacy_fallback(targets)

    counts = {}
    for t in targets:
        counts[t.status] = counts.get(t.status, 0) + 1
    stale = counts.get("STALE-TYPE", 0) + counts.get("STALE-METHOD", 0)

    if args.json:
        payload = {
            "repo": str(repo),
            "src_root": str(src_root) if src_root else None,
            "patch_files": patch_files,
            "patch_classes": patch_classes,
            "src_file_count": src_file_count,
            "counts": counts,
            "stale": stale,
            "rows": [
                {
                    "file": t.path,
                    "lineno": t.lineno,
                    "kind": t.kind,
                    "type": t.fqn or t.type_simple,
                    "member": t.member,
                    "member_kind": t.member_kind,
                    "status": t.status,
                    "anchor": t.anchor,
                    "note": t.note,
                }
                for t in targets
            ],
        }
        text = json.dumps(payload, ensure_ascii=False, indent=2)
    else:
        text = render_markdown(targets, repo, src_root, src_file_count)

    if args.out:
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")
        print(f"已生成: {out}")
    else:
        sys.stdout.write(text + "\n")

    print(f"\n统计: 总数={len(targets)} verified={counts.get('verified', 0)} "
          f"STALE={stale} SKIP-DYNAMIC={counts.get('SKIP-DYNAMIC', 0)} "
          f"SKIP-NAMEOF={counts.get('SKIP-NAMEOF', 0)}", file=sys.stderr)
    sys.exit(1 if stale > 0 else 0)


if __name__ == "__main__":
    main()
