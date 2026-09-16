#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""日志解析器：从日志中提取错误 / 异常 / Harmony 补丁堆栈，支持流式读取。

兼容格式：
  STS2 自定义格式:  [ERROR] message\n   at MegaCrit.Sts2...()\n   at ...
  Godot 原生格式:   ERROR: message\n   at: method (res://path:line)
  .NET 异常格式:    Unhandled exception. System.NullReferenceException: ...\n   at ...

用法示例：
  python log_parser.py player.log
  python log_parser.py player.log --no-frames
  python log_parser.py player.log --kw "Harmony|act4"
  python log_parser.py player.log --categories error,harmony --tail 10
  python log_parser.py player.log --count --json
  python log_parser.py                # 自动在常见位置找最新日志
"""

import argparse
import collections
import fnmatch
import glob
import io
import json
import os
import re
import sys

LEVEL_RE = re.compile(r"^\[(ERROR|ERR|CRITICAL|FATAL|EXCEPTION|WARN|WARNING|INFO)\]\s*(.*)$", re.I)

# 初始化终态锚点（Entry.cs 输出，互斥）：不要再用「Mod 初始化完成」判健康
RE_INIT_OK = re.compile(r"\bINIT_OK\b")
RE_INIT_FAILED = re.compile(r"\bINIT_FAILED\b")
RE_FATAL_CODE = re.compile(r"\[(CONFIG001|MODEL001|CLR001|CLR002|ASM002|DEP001|DEP002|DEP003|"
                           r"PATCH001|PATCH002|PATCH003|STG001)\]")
GODOT_ERR_RE = re.compile(r"^(SCRIPT ERROR|ERROR|USER ERROR)\s*:\s*(.*)$", re.I)
EXC_RE = re.compile(r"(Unhandled exception|FailFast|\w+Exception\s*[:\[]|\bFatal\b)", re.I)
HARMONY_RE = re.compile(r"harmony.*(?:fail|error|exception|throw|denied|reject)|(?:fail|error|exception).*harmony", re.I)

LEVEL_CAT = {
    "ERROR": "error", "ERR": "error", "CRITICAL": "error", "FATAL": "error",
    "EXCEPTION": "exception", "WARN": "warning", "WARNING": "warning",
}
CATS_ALL = ("error", "exception", "harmony", "warning")

STACK_FRAME_RE = re.compile(r"^\s+(at |at:|in |->|\[|at\s+[A-Za-z_])")


def classify_line(line):
    """返回该行是否触发新事件，以及类别集合。"""
    cats = set()
    m = LEVEL_RE.match(line)
    if m:
        lvl, _ = m.group(1).upper(), m.group(2)
        cat = LEVEL_CAT.get(m.group(1).upper())
        if cat in ("error", "exception"):
            cats.add(cat)
        elif cat == "warning":
            cats.add("warning")
    m2 = GODOT_ERR_RE.match(line)
    if m2:
        cats.add("error")
    if EXC_RE.search(line):
        cats.add("exception")
    if HARMONY_RE.search(line):
        cats.add("harmony")
    return cats


def is_stack_frame(line):
    return bool(STACK_FRAME_RE.match(line))


class Incident:
    __slots__ = ("path", "line_no", "cats", "lines")

    def __init__(self, path, line_no, cats, first_line):
        self.path = path
        self.line_no = line_no
        self.cats = set(cats)
        self.lines = [first_line]


def open_log(path, encoding=None):
    """按 BOM 自动识别编码打开日志；无 BOM 按 UTF-8 宽容读。
    桌面上的日志副本可能是 UTF-16（带 BOM），此前手动转码才能搜中文关键字。"""
    if encoding:
        return io.open(path, "r", encoding=encoding, errors="replace")
    with open(path, "rb") as fh:
        head = fh.read(4)
    if head[:2] == b"\xff\xfe":
        return io.open(path, "r", encoding="utf-16", errors="replace")
    if head[:2] == b"\xfe\xff":
        return io.open(path, "r", encoding="utf-16-be", errors="replace")
    return io.open(path, "r", encoding="utf-8", errors="replace")


def iter_log(path, encoding=None):
    with open_log(path, encoding) as fh:
        for lineno, raw in enumerate(fh, 1):
            yield lineno, raw.rstrip("\r\n")


def scan(path, want_cats, encoding=None):
    incidents = []
    cur = None
    prev_blank = False
    for lineno, line in iter_log(path, encoding):
        cats = classify_line(line)
        is_frame = is_stack_frame(line)
        if cats and (cats & want_cats):
            if cur is not None:
                incidents.append(cur)
            cur = Incident(path, lineno, cats, line)
            prev_blank = False
            continue
        if line.strip() == "":
            if cur is not None:
                incidents.append(cur)
                cur = None
            prev_blank = True
            continue
        if cur is not None:
            if is_frame or line[0] in " \t" or (prev_blank and not line[0] in " \t"):
                cur.lines.append(line)
            else:
                incidents.append(cur)
                cur = None
        prev_blank = False
    if cur is not None:
        incidents.append(cur)
    return incidents


def find_logs():
    """自动发现候选日志。返回按 mtime 倒序的列表。"""
    cands = []
    roots = []
    if os.name == "nt":
        roots.append(os.path.join(os.environ.get("APPDATA", ""), "SlayTheSpire2", "logs"))
        roots.append(os.path.join(os.environ.get("APPDATA", ""), "Godot", "app_userdata"))
    else:
        roots.append(os.path.expanduser("~/.local/share/godot/app_userdata"))
    for root in roots:
        if root and os.path.isdir(root):
            for f in glob.glob(os.path.join(root, "**", "*.log"), recursive=True):
                cands.append(f)
    for pat in ("*.log", "logs/*.log", "player.log"):
        cands.extend(glob.glob(os.path.join(os.getcwd(), pat)))
    cands = [c for c in cands if os.path.isfile(c)]
    return sorted(cands, key=os.path.getmtime, reverse=True)


def scan_init_status(logs):
    """扫描初始化终态锚点：INIT_OK / INIT_FAILED 互斥，并统计致命错误码。"""
    ok = failed = 0
    fatal_codes = []
    fatal_samples = []
    for path in logs:
        if not os.path.isfile(path):
            continue
        with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                if RE_INIT_FAILED.search(line):
                    failed += 1
                elif RE_INIT_OK.search(line):
                    ok += 1
                match = RE_FATAL_CODE.search(line)
                if match:
                    fatal_codes.append(match.group(1))
                    if len(fatal_samples) < 20:
                        fatal_samples.append(line.rstrip())

    if failed:
        status = "FAILED"
    elif ok:
        status = "OK"
    else:
        status = "UNKNOWN"
    return status, ok, failed, fatal_codes, fatal_samples


def report_init_status(logs, as_json=False):
    status, ok, failed, codes, samples = scan_init_status(logs)
    if as_json:
        print(json.dumps({
            "init_status": status,
            "init_ok_lines": ok,
            "init_failed_lines": failed,
            "fatal_count": len(codes),
            "fatal_codes": sorted(set(codes)),
            "fatal_samples": samples,
        }, ensure_ascii=False, indent=2))
    else:
        print("INIT_STATUS=%s" % status)
        print("INIT_OK_LINES=%d  INIT_FAILED_LINES=%d" % (ok, failed))
        print("FATAL_COUNT=%d" % len(codes))
        if codes:
            print("FATAL_CODES=%s" % ",".join(sorted(set(codes))))
            for sample in samples:
                print("  %s" % sample)
        if status == "UNKNOWN":
            print("（未在日志中找到 INIT_OK / INIT_FAILED：mod 可能未加载，或日志不是本次启动）")
    return 0 if status == "OK" else (1 if status == "FAILED" else 2)


def main():
    ap = argparse.ArgumentParser(description="日志错误/异常/Harmony 提取器")
    ap.add_argument("logs", nargs="*", help="日志文件（默认自动查找最新）")
    ap.add_argument("--categories", help="逗号分隔: error,exception,harmony,warning（默认前三个）")
    ap.add_argument("--all", action="store_true", help="等价于 --categories 全选")
    ap.add_argument("--kw", help="只保留匹配该正则的事件")
    ap.add_argument("--kw-file", help="从文件按行读关键词（UTF-8），每行作为一个备选子串（re.escape），"
                                      "与 --kw 可叠加。规避 Windows 控制台向 python 传中文参数被 GBK 误码的问题")
    ap.add_argument("--encoding", help="强制日志编码（默认按 BOM/UTF-8 自动识别）")
    ap.add_argument("--frames", dest="frames", action="store_true", default=True, help="输出完整堆栈（默认）")
    ap.add_argument("--no-frames", dest="frames", action="store_false", help="只输出每个事件的首行")
    ap.add_argument("--tail", type=int, help="只输出最后 N 个事件")
    ap.add_argument("--count", action="store_true", help="输出统计摘要")
    ap.add_argument("--init-status", dest="init_status", action="store_true",
                    help="输出 mod 初始化终态 INIT_STATUS=OK|FAILED|UNKNOWN 与 FATAL_COUNT（可当门禁）")
    ap.add_argument("--json", action="store_true", help="JSON 输出")
    ap.add_argument("--out", help="写入文件（默认 stdout）")
    args = ap.parse_args()

    if args.all:
        want = set(CATS_ALL)
    elif args.categories:
        want = {c.strip().lower() for c in args.categories.split(",") if c.strip()}
        bad = want - set(CATS_ALL)
        if bad:
            ap.error("未知类别: %s（可选 %s）" % (",".join(bad), ",".join(CATS_ALL)))
    else:
        want = {"error", "exception", "harmony"}

    kw = None
    kw_parts = []
    if args.kw:
        kw_parts.append(args.kw)
    if args.kw_file:
        try:
            with io.open(args.kw_file, "r", encoding="utf-8", errors="replace") as fh:
                for line in fh:
                    s = line.strip()
                    if s:
                        kw_parts.append(re.escape(s))
        except OSError as e:
            print("读取 --kw-file 失败: %s" % e, file=sys.stderr)
            return 2
    if kw_parts:
        # --kw 视为原始正则，--kw-file 的行已被 re.escape 为字面串；合并时用 | 连接。
        kw = re.compile("|".join(kw_parts), re.I)

    logs = args.logs
    if not logs:
        auto = find_logs()
        if not auto:
            print("未指定日志，自动查找也没找到。请给出日志路径。", file=sys.stderr)
            return 1
        logs = [auto[0]]
        print("# 自动选择: %s" % logs[0], file=sys.stderr)

    if args.init_status:
        return report_init_status(logs, args.json)

    all_incidents = []
    for p in logs:
        if not os.path.isfile(p):
            print("找不到文件: %s" % p, file=sys.stderr)
            continue
        all_incidents.extend(scan(p, want, args.encoding))

    if kw:
        all_incidents = [inc for inc in all_incidents if kw.search("\n".join(inc.lines))]

    if args.tail:
        all_incidents = all_incidents[-args.tail:]

    if args.count:
        cnt = collections.Counter()
        for inc in all_incidents:
            for c in inc.cats:
                cnt[c] += 1
        summary = {"total": len(all_incidents), "by_category": dict(cnt)}
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return 0

    if args.json:
        out = [{"file": inc.path, "line": inc.line_no, "categories": sorted(inc.cats),
                "lines": inc.lines} for inc in all_incidents]
        text = json.dumps(out, ensure_ascii=False, indent=2)
    else:
        lines = []
        for i, inc in enumerate(all_incidents, 1):
            header = "----- #%d [%s] %s:%d -----" % (i, ",".join(sorted(inc.cats)),
                                                     os.path.basename(inc.path), inc.line_no)
            lines.append(header)
            body = inc.lines if args.frames else inc.lines[:1]
            lines.extend(body)
        text = "\n".join(lines) + ("\n" if lines else "")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text)
        print("已写入 %s（%d 个事件）" % (args.out, len(all_incidents)), file=sys.stderr)
    else:
        sys.stdout.write(text)

    return 0


if __name__ == "__main__":
    sys.exit(main())