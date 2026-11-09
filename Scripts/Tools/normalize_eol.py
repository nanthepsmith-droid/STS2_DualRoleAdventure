#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
换行符规范化（normalize_eol）—— 把工作区里「按 `.gitattributes` 应为 LF」的文本文件从 CRLF 改回 LF。

背景（`TODO.md` 维护性 backlog 第 2 项 / 2026-10-11）：
  `.gitattributes` 声明 `*.cs … eol=lf`，index 里存的也是 LF，但**写文件工具默认产 CRLF**
  ⇒ 工作区跨轮累积（2026-10-11 一次清掉 67 个文本文件）。`git status` 会多列一批 ` M`（stat 缓存噪声），
  且**规范化会改变 dll 字节**（本仓库构建不逐字节可复现）⇒ **必须排在最后一次构建之前**，
  否则要重跑 build + deploy 并订正 sha。

与门禁的关系：`static_checks.py` S8 会把这些文件判 **FAIL**（提示就是让跑本脚本）。

用法:
  python Scripts/Tools/normalize_eol.py --repo .            # 规范化（改文件）
  python Scripts/Tools/normalize_eol.py --repo . --dry-run  # 只看哪些文件会被改
  python Scripts/Tools/normalize_eol.py --repo . --json
退出码: 0 = 已规范化（或本来就没有 CRLF）；1 = 有文件读写失败
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from static_checks import ENCODING_SKIP_DIRS, EOL_LF_EXTS  # noqa: E402  （复用同一份口径，防两处漂移）


def iter_targets(repo: Path):
    for p in sorted(repo.rglob("*")):
        if p.suffix.lower() not in EOL_LF_EXTS:
            continue
        if any(part in ENCODING_SKIP_DIRS for part in p.parts):
            continue
        if p.is_file():
            yield p


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="把应为 LF 的文本文件从 CRLF 规范化成 LF")
    parser.add_argument("--repo", default=".", help="仓库根目录（默认当前目录）")
    parser.add_argument("--dry-run", action="store_true", help="只列文件，不写盘")
    parser.add_argument("--json", action="store_true", help="输出 JSON")
    args = parser.parse_args(argv)

    repo = Path(args.repo).resolve()
    if not repo.is_dir():
        print(f"仓库目录不存在: {repo}", file=sys.stderr)
        return 1

    changed, failed, scanned = [], [], 0
    for p in iter_targets(repo):
        scanned += 1
        rel = str(p.relative_to(repo)).replace("\\", "/")
        try:
            raw = p.read_bytes()
        except OSError as exc:
            failed.append(f"{rel}: {exc}")
            continue
        if b"\r\n" not in raw:
            continue
        changed.append((rel, raw.count(b"\r\n")))
        if not args.dry_run:
            try:
                p.write_bytes(raw.replace(b"\r\n", b"\n"))
            except OSError as exc:
                failed.append(f"{rel}: {exc}")

    if args.json:
        print(json.dumps({
            "repo": str(repo),
            "dry_run": args.dry_run,
            "scanned": scanned,
            "changed": [{"file": f, "crlf": n} for f, n in changed],
            "failed": failed,
        }, ensure_ascii=False, indent=2))
    else:
        verb = "将规范化" if args.dry_run else "已规范化"
        print(f"[normalize_eol] 扫描 {scanned} 个 LF 文本文件，{verb} {len(changed)} 个")
        for rel, n in changed:
            print(f"  - {rel}（{n} 处 CRLF）")
        for item in failed:
            print(f"  ! 失败 {item}", file=sys.stderr)
        if changed and not args.dry_run:
            # 只打 ASCII 箭头：Windows 控制台默认 GBK，'⇒' 会直接抛 UnicodeEncodeError
            print("[normalize_eol] 提醒：换行变化会改变 dll 字节 -> 若已构建/部署，请重跑 "
                  "build + preflight -Deploy 并重新校验 sha。")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
