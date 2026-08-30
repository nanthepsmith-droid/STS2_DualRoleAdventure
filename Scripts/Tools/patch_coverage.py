#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
补丁目标覆盖清单生成器（patch_coverage）

扫描 DualRoleAdventure / LocalMultiControl 仓库的 Scripts/Patch/*.cs，
解析全部 HarmonyPatch 声明（类级 / 方法级 / 裸容器 / 字符串目标），
并（可选）与反编译参考源码（sts2src）交叉核对，输出 Markdown 覆盖清单。

用途（对应《mod系统维护性改进实施方案》任务 1.1）：
  1. 一眼看清 96 个补丁类打在哪、是否是字符串目标、是否已核实；
  2. 自动标记「方法级-only [HarmonyPatch]」——此类会被 PatchAll 静默跳过（本 mod 坑 1）；
  3. 游戏更新后，先跑本工具再跑 check_string_targets.py，按清单逐项核对。

用法:
  python patch_coverage.py --repo <STS2仓库路径>
                           [--out ../maintenance-docs/patch-coverage.md]  # 默认只打印到 stdout；维护文档目录在 pain/（无 git）
                           [--src <反编译源码目录>]          # 默认探测 repo/src 与 repo/../sts2src/src
                           [--no-src-check]                 # 跳过反编译交叉核对
                           [--json]                         # 输出结构化 JSON（供其它工具复用）

示例:
  python ..\\tools\\patch_coverage.py --repo . --out ../maintenance-docs/patch-coverage.md
  python ..\\tools\\patch_coverage.py --repo . --json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# ------------------------------------------------------------------ 常量
RE_HARMONY_ATTR = re.compile(r"\[(Harmony[A-Za-z]*)")
# 补丁方法属性（用于判定类是否"真正带补丁"）
PATCH_METHOD_ATTRS = {"HarmonyPrefix", "HarmonyPostfix", "HarmonyTranspiler", "HarmonyFinalizer"}

RE_CLASS = re.compile(
    r"^\s*(?:(?:public|internal|private|protected)\s+)*"
    r"(?:(?:static|abstract|sealed|partial)\s+)*"
    r"class\s+([A-Za-z_][A-Za-z0-9_]*)"
)
RE_STRUCT = re.compile(
    r"^\s*(?:(?:public|internal|private|protected)\s+)*"
    r"(?:(?:static|readonly|ref|partial)\s+)*"
    r"(?:struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)"
)
# 方法声明：可选修饰符 + 返回类型 + 方法名 + (  。要求行首不可能是调用/控制流。
RE_METHOD = re.compile(
    r"^\s*(?:(?:public|private|internal|protected)\s+)?"
    r"(?:(?:static|async|virtual|override|abstract|sealed|partial|unsafe|extern)\s+)*"
    r"([A-Za-z_][A-Za-z0-9_<>,\.\s\?\[\]]+?)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\("
)
RE_TYPEOF = re.compile(r"typeof\s*\(\s*([^\)]+?)\s*\)")
RE_STRING = re.compile(r'^\s*"((?:[^"\\]|\\.)*)"\s*$')
RE_NAMEOF = re.compile(r"nameof\s*\(\s*([^\)]+?)\s*\)")

# 方法定义（用于在反编译源码中定位）—— 排除调用点
def _method_def_pattern(name):
    return re.compile(
        r"^\s*(?:(?:public|private|internal|protected)\s+)*"
        r"(?:(?:static|virtual|override|async|abstract|sealed|partial|ref|readonly|extern|unsafe)\s+)*"
        r"[\w<>,\.\s\?\[\]]+?\s+" + re.escape(name) + r"\s*\(",
        re.MULTILINE,
    )


# ------------------------------------------------------------------ 解析工具
def scan_attributes(source: str):
    """返回 [(start, end, attr_name, argtext, lineno)]，argtext 为括号内原始文本（无括号则为 ''）。"""
    out = []
    n = len(source)
    i = 0
    while i < n:
        idx = source.find("[", i)
        if idx == -1:
            break
        m = RE_HARMONY_ATTR.match(source, idx)
        if not m:
            i = idx + 1
            continue
        attr_name = m.group(1)
        # 跳过注释：该行以 // 或 * 开头
        line_start = source.rfind("\n", 0, idx) + 1
        stripped = source[line_start:idx].strip()
        if stripped.startswith("//") or stripped.startswith("*"):
            i = idx + len(attr_name)
            continue
        # 匹配到配对的 ]
        depth = 0
        j = idx
        while j < n:
            c = source[j]
            if c == "[":
                depth += 1
            elif c == "]":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        if j >= n:  # 没闭合，跳过
            i = idx + 1
            continue
        inner = source[idx + len(attr_name) + 1:j].strip()
        lineno = source.count("\n", 0, idx) + 1
        out.append((idx, j, attr_name, inner, lineno))
        i = j + 1
    return out


def split_top_level(text: str, sep: str = ","):
    parts, depth, cur = [], 0, ""
    for c in text:
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth = max(depth - 1, 0)
        if c == sep and depth == 0:
            parts.append(cur.strip())
            cur = ""
            continue
        cur += c
    if cur.strip():
        parts.append(cur.strip())
    return parts


def strip_namespace(name: str) -> str:
    name = name.strip()
    return name.split(".")[-1].strip()


def parse_args(inner: str):
    """解析 [HarmonyPatch(...)] 参数，返回 [(kind, value)]。
    kind: type / string / nameof / name / other。
    裸 [HarmonyPatch]（无括号）返回 []。
    """
    text = inner.strip()
    if text.startswith("(") and text.endswith(")"):
        text = text[1:-1].strip()
    if not text:
        return []
    args = []
    for p in split_top_level(text):
        m = RE_TYPEOF.search(p)
        if m:
            args.append(("type", strip_namespace(m.group(1))))
            continue
        m = RE_STRING.match(p)
        if m:
            args.append(("string", m.group(1)))
            continue
        m = RE_NAMEOF.search(p)
        if m:
            args.append(("nameof", strip_namespace(m.group(1))))
            continue
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", p):
            args.append(("name", p))
        else:
            args.append(("other", p))
    return args


def interpret_args(args):
    """把参数列表解释为目标。返回 (kind, type_simple, method, is_string)。
    kind: container(裸容器) / target / unknown。
    """
    if not args:
        return ("container", None, None, False)
    type_simple, method, is_string = None, None, False
    if args[0][0] in ("type", "string", "nameof", "name"):
        type_simple = args[0][1]
    if len(args) >= 2 and args[1][0] in ("string", "nameof", "name"):
        method = args[1][1]
        is_string = args[1][0] == "string"
    return ("target", type_simple, method, is_string)


# ------------------------------------------------------------------ 文件解析
class PatchClass:
    def __init__(self, name, path, line):
        self.name = name
        self.path = path
        self.line = line
        self.class_attrs = []      # [(argtext, lineno)]
        self.method_attrs = []     # [(method_name, argtext, attr_lineno)]
        self.has_class_attr = False
        self.has_patch_method = False   # 是否有 Prefix/Postfix/Transpiler/Finalizer 方法


class Decl:
    def __init__(self, lineno, kind, name):
        self.lineno = lineno
        self.kind = kind          # 'class' | 'method'
        self.name = name


def parse_patch_file(path: Path):
    """返回该文件的 PatchClass 列表。"""
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    attrs = scan_attributes(text)
    if not attrs:
        return []

    decls = []
    for i, line in enumerate(text.splitlines(), start=1):
        m = RE_CLASS.match(line)
        if m:
            decls.append(Decl(i, "class", m.group(1)))
            continue
        m = RE_METHOD.match(line)
        if m:
            decls.append(Decl(i, "method", m.group(2)))
    decls.sort(key=lambda d: d.lineno)

    # 属性归属：
    #   属性总是写在「其后最近的声明」上方 —— 若其后最近声明是 class，则该属性是类级；
    #   若是 method，则该属性是方法级，且属于「该 method 之前最近声明的 class」。
    #   这一规则天然覆盖：类级属性、方法级属性、以及"类本身没有类级标记"的方法级-only 类。
    classes = []
    cls_by_key = {}
    class_lines = [d for d in decls if d.kind == "class"]
    method_owner = {}  # method.lineno -> (class_name, class_line)
    cur_class = None
    for d in decls:
        if d.kind == "class":
            cur_class = d.name
        else:
            method_owner[d.lineno] = cur_class

    def get_class(name, line):
        key = (name, line)
        if key not in cls_by_key:
            cls = PatchClass(name, str(path), line)
            cls_by_key[key] = cls
            classes.append(cls)
        return cls_by_key[key]

    for attr_name, argtext, lineno in [(a[2], a[3], a[4]) for a in attrs]:
        # 其后最近的声明
        next_decl = None
        for d in decls:
            if d.lineno > lineno:
                next_decl = d
                break
        if next_decl is None:
            continue
        if next_decl.kind == "class":
            # 类级属性 → 归属该类
            cls = get_class(next_decl.name, next_decl.lineno)
            if attr_name == "HarmonyPatch":
                cls.class_attrs.append((argtext, lineno))
                cls.has_class_attr = True
            elif attr_name in PATCH_METHOD_ATTRS:
                cls.has_patch_method = True
        else:
            # 方法级属性 → 归属该方法所在类
            owner = method_owner.get(next_decl.lineno)
            if owner is None:
                continue
            cls = get_class(owner, next(iter(cl.lineno for cl in class_lines if cl.name == owner and cl.lineno < next_decl.lineno), 0))
            if attr_name == "HarmonyPatch":
                cls.method_attrs.append((next_decl.name, argtext, lineno))
            elif attr_name in PATCH_METHOD_ATTRS:
                cls.has_patch_method = True
    return classes


# ------------------------------------------------------------------ 反编译交叉核对
def locate_src_dir(repo: Path, explicit: str | None) -> Path | None:
    if explicit:
        p = Path(explicit)
        return p if p.exists() else None
    for cand in (repo / "src", repo.parent / "sts2src" / "src"):
        if cand.exists():
            return cand
    return None


def build_type_index(src_root: Path):
    """返回 {简单类型名: set(文件路径str)}。"""
    index = {}
    total_files = 0
    for f in src_root.rglob("*.cs"):
        total_files += 1
        try:
            with open(f, encoding="utf-8-sig", errors="replace") as fh:
                for line in fh:
                    m = RE_CLASS.match(line)
                    if m:
                        index.setdefault(m.group(1), set()).add(str(f))
                        continue
                    m = RE_STRUCT.match(line)
                    if m:
                        index.setdefault(m.group(1), set()).add(str(f))
        except OSError:
            continue
    return index, total_files


def _property_def_pattern(name):
    """属性声明：可选修饰符 + 返回类型 + 属性名 + ( => | { | ; )。"""
    return re.compile(
        r"^\s*(?:(?:public|private|internal|protected)\s+)*"
        r"(?:(?:static|virtual|override|abstract|sealed|partial|new|readonly|extern|unsafe)\s+)*"
        r"[\w<>,\.\s\?\[\]]+?\s+" + re.escape(name) + r"\s*(?:=>|\{|;)",
        re.MULTILINE,
    )


def find_method_in_files(method, files):
    """返回 (文件路径, 行号) 或 None。方法名若是 CLR getter/setter（get_X），
    会同时按属性 X 的定义查找。"""
    cache = {}
    pat = _method_def_pattern(method)
    prop_names = [method]
    if method.startswith("get_") or method.startswith("set_"):
        prop_names.append(method[4:])
    prop_pats = [_property_def_pattern(n) for n in prop_names]
    for f in files:
        if f not in cache:
            try:
                cache[f] = Path(f).read_text(encoding="utf-8-sig", errors="replace")
            except OSError:
                continue
        m = pat.search(cache[f])
        if m:
            lineno = cache[f].count("\n", 0, m.start()) + 1
            return f, lineno
        for pp in prop_pats:
            m = pp.search(cache[f])
            if m:
                lineno = cache[f].count("\n", 0, m.start()) + 1
                return f, lineno
    return None


# ------------------------------------------------------------------ 主流程
def verify_target(index, type_simple, method):
    """返回 (status, anchor)。
    status: verified / STALE-TYPE / STALE-METHOD / UNKNOWN(类型简名无索引命中)
    """
    if type_simple is None:
        return "UNKNOWN", ""
    files = index.get(type_simple)
    if not files:
        return "STALE-TYPE", ""
    if method:
        hit = find_method_in_files(method, files)
        if hit:
            rel = Path(hit[0])
            return "verified", f"{rel} (L{hit[1]})"
        return "STALE-METHOD", ""
    # 只指定类型（构造函数补丁）—— 读取不修改，避免破坏共享索引
    return "verified", f"{next(iter(files))}"


# ------------------------------------------------------------------ 分析主逻辑
class TargetRow:
    def __init__(self, patch_class, kind, type_simple, method, is_string,
                 attr_line, status, anchor, note=""):
        self.patch_class = patch_class    # PatchClass
        self.kind = kind                  # 'target' | 'container-unknown'
        self.type_simple = type_simple
        self.method = method
        self.is_string = is_string
        self.attr_line = attr_line
        self.status = status
        self.anchor = anchor
        self.note = note


def analyze(repo: Path, src_root: Path | None):
    patch_dir = repo / "Scripts" / "Patch"
    if not patch_dir.exists():
        print(f"错误: 找不到补丁目录 {patch_dir}", file=sys.stderr)
        sys.exit(2)

    classes = []
    files = sorted(patch_dir.rglob("*.cs"))
    for f in files:
        classes.extend(parse_patch_file(f))

    type_index, src_file_count = build_type_index(src_root) if src_root else ({}, 0)

    rows = []
    method_only_classes = []
    for cls in classes:
        # 1) 类级属性解释
        class_targets = []
        class_is_container = False
        for argtext, lineno in cls.class_attrs:
            args = parse_args(argtext)
            kind, ts, m, is_str = interpret_args(args)
            if kind == "container":
                class_is_container = True
            else:
                class_targets.append((ts, m, is_str, lineno))

        # 2) 方法级属性收集
        method_targets = []
        for method_name, argtext, lineno in cls.method_attrs:
            args = parse_args(argtext)
            kind, ts, m, is_str = interpret_args(args)
            if kind == "target" and (ts or m):
                method_targets.append((ts, m, is_str, lineno))

        # 3) 归类
        if not cls.has_class_attr and method_targets:
            # 方法级-only → PatchAll 静默跳过
            method_only_classes.append(cls)
            note = "方法级-only(会被 PatchAll 静默跳过)"
            for ts, m, is_str, lineno in method_targets:
                status, anchor = ("WARN-METHOD-ONLY", "")
                rows.append(TargetRow(cls, "target", ts, m, is_str, lineno,
                                      status, anchor, note))
            continue

        if class_is_container:
            # 裸容器：目标来自方法级
            for ts, m, is_str, lineno in method_targets:
                rows.append(TargetRow(cls, "target", ts, m, is_str, lineno,
                                      "pending", "", ""))
            if not method_targets:
                if not cls.has_patch_method:
                    # 纯容器类：补丁全在嵌套类中（如 LocalPlayerLimitNetworkPatch），PatchAll 正常处理嵌套类
                    rows.append(TargetRow(cls, "container", None, None, False,
                                          cls.line, "container", "", "纯容器类（补丁在嵌套类中），正常"))
                else:
                    rows.append(TargetRow(cls, "container-unknown", None, None, False,
                                          cls.line, "UNKNOWN", "", "裸容器但无方法级目标"))
            continue

        if class_targets:
            for ts, m, is_str, lineno in class_targets:
                rows.append(TargetRow(cls, "target", ts, m, is_str, lineno,
                                      "pending", "", ""))
            continue

        # 有类级 [HarmonyPatch] 但解释不出目标（罕见）
        if cls.has_class_attr:
            rows.append(TargetRow(cls, "container-unknown", None, None, False,
                                  cls.line, "UNKNOWN", "", "类级属性存在但目标无法解析"))

    # 交叉核对状态
    if type_index:
        for r in rows:
            if r.status in ("pending", ""):
                r.status, r.anchor = verify_target(type_index, r.type_simple, r.method)
        status_counts = {}
        for r in rows:
            status_counts[r.status] = status_counts.get(r.status, 0) + 1
    else:
        for r in rows:
            if r.status == "pending":
                r.status = "unverified"
        status_counts = {"unverified": len(rows)}

    result = {
        "repo": str(repo),
        "src_root": str(src_root) if src_root else None,
        "patch_files": len(files),
        "patch_classes": len(classes),
        "src_file_count": src_file_count,
        "status_counts": status_counts,
        "method_only_classes": [c.name for c in method_only_classes],
        "rows": rows,
    }
    return result


# ------------------------------------------------------------------ Markdown 输出
def render_markdown(result) -> str:
    now = __import__("datetime").datetime.now().strftime("%Y-%m-%d %H:%M")
    src_line = result["src_root"] or "未找到反编译参考源码（标记 unverified）"
    L = []
    L.append("# Patch Coverage — 补丁目标覆盖清单")
    L.append("")
    L.append(f"> 生成时间：{now}")
    L.append(f"> 扫描目录：`Scripts/Patch/`（{result['patch_files']} 个文件，{result['patch_classes']} 个补丁类）")
    L.append(f"> 反编译参考：{src_line}" + (f"（{result['src_file_count']} 个 .cs）" if result["src_file_count"] else ""))
    L.append("> 用途：游戏更新适配与「静默跳过」排查的核对基线。由 `Scripts/Tools/patch_coverage.py` 生成，勿手改。")
    L.append("")

    sc = result["status_counts"]
    verified = sc.get("verified", 0)
    L.append("## 统计")
    L.append("")
    L.append(f"- 补丁类总数：**{result['patch_classes']}**")
    L.append(f"- 目标行总数：**{len(result['rows'])}**")
    L.append(f"- 状态分布：verified **{verified}** / " +
             " / ".join(f"{k} **{v}**" for k, v in sorted(sc.items()) if k != "verified") or "无")
    L.append("")

    mo = result["method_only_classes"]
    L.append("## ⚠️ 方法级-only 补丁类（PatchAll 会静默跳过）")
    L.append("")
    if mo:
        L.append("以下类的 `[HarmonyPatch]` 只写在方法上、类上没有类级标记，**`PatchAll` 不会应用它们**，")
        L.append("补丁永不触发且无任何报错（本 mod 坑 1，已踩过 `adae5aa` 与 `CardSelectManualConfirmationPatch` 两次）。")
        L.append("")
        L.append("| 补丁类 | 文件 |")
        L.append("|---|---|")
        for name in sorted(set(mo)):
            L.append(f"| `{name}` | 见下方目标行 |")
        L.append("")
    else:
        L.append("无。")
        L.append("")

    L.append("## 覆盖清单")
    L.append("")
    L.append("| # | 补丁类 | 目标类型 | 目标方法 | 字符串目标 | 状态 | 反编译锚点 |")
    L.append("|---|---|---|---|---|---|---|")
    for i, r in enumerate(result["rows"], start=1):
        loc = f"`{r.patch_class.path}` L{r.attr_line}"
        type_col = f"`{r.type_simple}`" if r.type_simple else "—"
        method_col = f"`{r.method}`" if r.method else "—"
        is_str = "字符串" if r.is_string else ("nameof" if r.method else "—")
        note = f" {r.note}" if r.note else ""
        anchor = f"`{r.anchor}`" if r.anchor else "—"
        L.append(f"| {i} | `{r.patch_class.name}` | {type_col} | {method_col} | {is_str} | **{r.status}**{note} | {anchor} |")
    L.append("")
    L.append("> 状态含义：`verified` 类型与方法（或属性）均在反编译源码中找到；`STALE-TYPE` / `STALE-METHOD` 疑似失效（游戏更新后常见）；")
    L.append("> `UNKNOWN` 目标无法解析；`unverified` 未做反编译核对；`WARN-METHOD-ONLY` 该类会被 PatchAll 跳过；")
    L.append("> `container` 纯容器类（补丁全部在嵌套类中，PatchAll 正常处理，无需关注）。")
    L.append("> 反编译锚点为近似定位（按类型简名 + 方法/属性定义行匹配），仅供人工核对参考。")
    return "\n".join(L)


# ------------------------------------------------------------------ main
def main():
    ap = argparse.ArgumentParser(description="补丁目标覆盖清单生成器")
    ap.add_argument("--repo", required=True, help="STS2_DualRoleAdventure 仓库路径")
    ap.add_argument("--out", help="输出 Markdown 路径（默认只打印到 stdout）")
    ap.add_argument("--src", help="反编译参考源码目录（默认探测 repo/src 与 repo/../sts2src/src）")
    ap.add_argument("--no-src-check", action="store_true", help="跳过反编译交叉核对")
    ap.add_argument("--json", action="store_true", help="输出结构化 JSON 而非 Markdown")
    args = ap.parse_args()

    repo = Path(args.repo).resolve()
    src_root = None
    if not args.no_src_check:
        src_root = locate_src_dir(repo, args.src)
        if src_root is None:
            print(f"警告: 未找到反编译源码（已尝试 repo/src 与 repo/../sts2src/src），"
                  f"目标状态将为 unverified。可用 --src 指定。", file=sys.stderr)

    result = analyze(repo, src_root)

    if args.json:
        payload = {
            "repo": result["repo"],
            "src_root": result["src_root"],
            "patch_files": result["patch_files"],
            "patch_classes": result["patch_classes"],
            "src_file_count": result["src_file_count"],
            "status_counts": result["status_counts"],
            "method_only_classes": result["method_only_classes"],
            "rows": [
                {
                    "patch_class": r.patch_class.name,
                    "file": r.patch_class.path,
                    "attr_line": r.attr_line,
                    "kind": r.kind,
                    "type": r.type_simple,
                    "method": r.method,
                    "is_string": r.is_string,
                    "status": r.status,
                    "anchor": r.anchor,
                    "note": r.note,
                }
                for r in result["rows"]
            ],
        }
        text = json.dumps(payload, ensure_ascii=False, indent=2)
        if args.out:
            Path(args.out).write_text(text, encoding="utf-8")
        else:
            sys.stdout.write(text + "\n")
        return

    md = render_markdown(result)
    if args.out:
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(md, encoding="utf-8")
        print(f"已生成: {out}")
    else:
        sys.stdout.write(md + "\n")


if __name__ == "__main__":
    main()
