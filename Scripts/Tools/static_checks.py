#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
离线静态自检（static_checks）—— 唯一「不需要游戏安装」也能跑的门禁层。

定位（见 AGENTS.md §9）：
  门禁 1/2/3/4/5/7/10 都需要本机装好 Slay the Spire 2（`sts2.dll` / `0Harmony.dll` /
  `Steamworks.NET.dll` / `GodotSharp.dll`），托管 CI 上**跑不了**（游戏是闭源，不能上传）。
  本脚本补的是**纯仓库静态**那一层，让 push / PR 也能第一时间发现回归。

检查项：
  S1 产物 / 反编译源码入库：obj/ bin/ .godot/ src/ sts2src/ *.dll *.pck（依据 `git ls-files`）
  S2 ps1 编码：含非 ASCII 字符的 .ps1 必须带 UTF-8 BOM（PowerShell 5.1 否则按 GBK 解析报语法错）
  S3 元数据版本一致：根 json / `workshop\\content` json / `mod_manifest.json` 的 `version` 必须一致
  S4 补丁类级 [HarmonyPatch]：复用 `patch_coverage.py` 解析，`method_only_classes` 必须为空
     （本 mod 坑 1：只写在方法上的 [HarmonyPatch] 会被 PatchAll **静默跳过**，编译期毫无提示）
  S5 csproj 源码隔离：`<Compile Remove="src/**">` 与 `sts2src/**` 必须同时存在（门禁 2 的静态版）
  S6 BuildMarker 身份：`Scripts/Entry.cs` 必须有一个 marker 格式的 `BuildMarker` 常量（门禁 7 的静态版）

用法:
  python Scripts/Tools/static_checks.py --repo .
  python Scripts/Tools/static_checks.py --repo . --json
退出码: 0 = 全过；1 = 有 FAIL；2 = 用法 / 环境错误

注意：本脚本**只读**，不改任何文件；`--json` 供 CI / 其它工具复用。
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

# ------------------------------------------------------------------ 检查项定义
PASS, FAIL, WARN = "PASS", "FAIL", "WARN"

# S1：入库即违规的产物 / 反编译源码（AGENTS.md §1、diff_lint P0-非法产物）
RE_ARTIFACT = re.compile(
    r"(^|/)(obj|bin|\.godot|\.import|src|sts2src)/|\.(dll|pck|uid)$",
    re.IGNORECASE,
)
# S2：需要扫描的目录（跳过产物与新生成目录）
SKIP_DIRS = {"obj", "bin", ".godot", ".import", ".git", "__pycache__"}
# S3：版本三处来源
VERSION_JSONS = ("DualRoleAdventure.json", "workshop/content/DualRoleAdventure.json", "mod_manifest.json")
RE_VERSION = re.compile(r'"version"\s*:\s*"([^"]+)"')
RE_BUILD_MARKER = re.compile(r'BuildMarker\s*=\s*"([^"]*marker=[^"]*)"')
RE_COMPILE_REMOVE = re.compile(r'<Compile\s+Remove\s*=\s*"(src|sts2src)/\*\*"\s*/>')


class Check:
    def __init__(self, cid: str, name: str, level: str, detail: str = "", extra=None):
        self.id = cid
        self.name = name
        self.level = level
        self.detail = detail
        self.extra = extra or {}


def _iter_files(root: Path, pattern: str):
    for p in root.rglob(pattern):
        if any(part in SKIP_DIRS for part in p.parts):
            continue
        if p.is_file():
            yield p


# ------------------------------------------------------------------ S1
def check_artifacts_tracked(repo: Path) -> Check:
    name = "产物/反编译源码未入库"
    try:
        proc = subprocess.run(
            ["git", "-C", str(repo), "ls-files"],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
        )
    except (FileNotFoundError, OSError) as exc:
        return Check("S1", name, WARN, f"无法执行 git（{exc}），跳过", {"skipped": True})
    if proc.returncode != 0:
        return Check("S1", name, WARN, "git ls-files 失败（非 git 仓库？），跳过", {"skipped": True})

    tracked = [ln.replace("\\", "/") for ln in proc.stdout.splitlines() if ln.strip()]
    hits = [t for t in tracked if RE_ARTIFACT.search(t)]
    if hits:
        return Check("S1", name, FAIL, f"{len(hits)} 个产物/源码被 git 跟踪（`git rm --cached` 后加进 .gitignore）",
                     {"files": hits[:30]})
    return Check("S1", name, PASS, f"已跟踪 {len(tracked)} 个文件，无产物/反编译源码")


# ------------------------------------------------------------------ S2
def check_ps1_bom(repo: Path) -> Check:
    name = "ps1 含中文必须带 UTF-8 BOM"
    bad, checked = [], 0
    for p in _iter_files(repo, "*.ps1"):
        checked += 1
        raw = p.read_bytes()
        has_bom = raw[:3] == b"\xef\xbb\xbf"
        try:
            ascii_only = raw.decode("ascii") is not None
        except UnicodeDecodeError:
            ascii_only = False
        if not ascii_only and not has_bom:
            bad.append(str(p.relative_to(repo)).replace("\\", "/"))
    if bad:
        return Check("S2", name, FAIL, f"{len(bad)}/{checked} 个 ps1 含非 ASCII 但无 BOM（PS 5.1 会按 GBK 解析）",
                     {"files": bad})
    return Check("S2", name, PASS, f"{checked} 个 ps1 全部合规")


# ------------------------------------------------------------------ S3
def check_metadata_version(repo: Path) -> Check:
    name = "三处元数据 version 一致"
    seen, missing = {}, []
    for rel in VERSION_JSONS:
        p = repo / rel
        if not p.is_file():
            missing.append(rel)
            continue
        m = RE_VERSION.search(p.read_text(encoding="utf-8-sig", errors="replace"))
        if not m:
            missing.append(rel + "（无 version 字段）")
            continue
        seen[rel] = m.group(1)
    if missing:
        return Check("S3", name, FAIL, "缺失/无法解析：" + "、".join(missing), {"versions": seen})
    values = sorted(set(seen.values()))
    if len(values) != 1:
        return Check("S3", name, FAIL, f"版本不一致：{seen}", {"versions": seen})
    return Check("S3", name, PASS, f"三处均为 {values[0]}", {"versions": seen})


# ------------------------------------------------------------------ S4
def check_patch_class_level(repo: Path) -> Check:
    name = "补丁类级 [HarmonyPatch]（PatchAll 静默跳过）"
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    try:
        import patch_coverage as pc
    except Exception as exc:  # noqa: BLE001
        return Check("S4", name, FAIL, f"无法 import patch_coverage：{exc}")
    try:
        result = pc.analyze(repo, None)
    except Exception as exc:  # noqa: BLE001
        return Check("S4", name, FAIL, f"解析失败：{exc}")

    method_only = sorted(set(result["method_only_classes"]))
    info = {"patch_files": result["patch_files"], "patch_classes": result["patch_classes"],
            "target_rows": len(result["rows"]), "method_only_classes": method_only}
    if method_only:
        return Check("S4", name, FAIL,
                     f"{len(method_only)} 个类只有方法级 [HarmonyPatch]（补丁永不生效且无报错）："
                     + "、".join(method_only), info)
    return Check("S4", name, PASS,
                 f"{result['patch_classes']} 个补丁类 / {len(result['rows'])} 目标行，方法级-only = 0", info)


# ------------------------------------------------------------------ S5
def check_source_isolation(repo: Path) -> Check:
    name = "csproj 源码隔离（src/ 与 sts2src/）"
    csproj = repo / "LocalMultiControl.csproj"
    if not csproj.is_file():
        return Check("S5", name, FAIL, "缺少 LocalMultiControl.csproj")
    text = csproj.read_text(encoding="utf-8-sig", errors="replace")
    found = {m.group(1) for m in RE_COMPILE_REMOVE.finditer(text)}
    missing = [d for d in ("src", "sts2src") if d not in found]
    if missing:
        return Check("S5", name, FAIL,
                     f"缺少 <Compile Remove=\"{missing[0]}/**\" />（AGENTS.md §9 门禁 2 / §1）",
                     {"found": sorted(found)})
    return Check("S5", name, PASS, "src/** 与 sts2src/** 均已排除", {"found": sorted(found)})


# ------------------------------------------------------------------ S6
def check_build_marker(repo: Path) -> Check:
    name = "Entry.cs BuildMarker 身份"
    entry = repo / "Scripts/Entry.cs"
    if not entry.is_file():
        return Check("S6", name, FAIL, "缺少 Scripts/Entry.cs")
    m = RE_BUILD_MARKER.search(entry.read_text(encoding="utf-8-sig", errors="replace"))
    if not m:
        return Check("S6", name, FAIL,
                     'Entry.cs 未找到形如 BuildMarker = "...marker=YYYY-MM-DD-rN" 的常量')
    return Check("S6", name, PASS, m.group(1))


# ------------------------------------------------------------------ main
CHECKS = (
    ("S1", "产物/反编译源码入库", check_artifacts_tracked),
    ("S2", "ps1 编码（UTF-8 BOM）", check_ps1_bom),
    ("S3", "元数据 version 一致", check_metadata_version),
    ("S4", "补丁类级 [HarmonyPatch]", check_patch_class_level),
    ("S5", "csproj 源码隔离", check_source_isolation),
    ("S6", "BuildMarker 身份", check_build_marker),
)


def main() -> int:
    ap = argparse.ArgumentParser(description="离线静态自检（不需要游戏安装）")
    ap.add_argument("--repo", required=True, help="仓库根目录")
    ap.add_argument("--json", action="store_true", help="输出结构化 JSON")
    args = ap.parse_args()

    repo = Path(args.repo).resolve()
    if not repo.is_dir():
        print(f"仓库目录不存在: {repo}", file=sys.stderr)
        return 2

    results = []
    for cid, label, fn in CHECKS:
        try:
            results.append(fn(repo))
        except Exception as exc:  # noqa: BLE001
            results.append(Check(cid, label, FAIL, f"检查本身抛异常：{exc!r}"))

    fails = [r for r in results if r.level == FAIL]
    warns = [r for r in results if r.level == WARN]

    if args.json:
        payload = {
            "repo": str(repo),
            "fail": len(fails), "warn": len(warns),
            "checks": [
                {"id": r.id, "name": r.name, "level": r.level, "detail": r.detail, "extra": r.extra}
                for r in results
            ],
        }
        sys.stdout.write(json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
    else:
        print("")
        print("离线静态自检（不需要游戏安装；构建/单测/ABI/部署仍需本机，见 AGENTS.md §9）")
        print("-" * 96)
        for r in results:
            mark = {"PASS": "PASS", "FAIL": "FAIL", "WARN": "WARN"}[r.level]
            print(f"[{mark}] {r.id} {r.name}")
            if r.detail:
                print(f"       {r.detail}")
            for f in (r.extra.get("files") or [])[:10]:
                print(f"         - {f}")
        print("-" * 96)
        print(f"汇总: {len(results) - len(fails) - len(warns)} PASS / {len(warns)} WARN / {len(fails)} FAIL")
        print("")

    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
