#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""DLL 部署校验器：验证复制到游戏 mods 目录的 .NET 程序集内容。

背景（本地多角色 mod 开发经验）：
  部署后无法直接运行，只能靠校验 DLL 字节确认"这次改动真的进去了"。
  .NET PE 里两类字符串编码不同，必须分开搜：
    - 类型名 / 成员名 / 命名空间 -> 元数据 #Strings 堆，UTF-8
    - 字符串字面量（如 Entry.cs 里的 BuildMarker）-> #US 用户字符串堆，UTF-16
  所以 "marker" 用 UTF-16 搜，补丁类型名用 UTF-8 搜；断言某个错误写法不存在
  （如 __runOriginal）用 UTF-8 搜缺失。

用法示例：
  python dll_check.py DualRoleAdventure.dll                     # 概要 + 自动找 marker
  python dll_check.py DualRoleAdventure.dll --marker "2026-08-19-r5"
  python dll_check.py DualRoleAdventure.dll ^
      --utf8 NPlayerHandSelectCardsSerializationPatch ^
      --utf8 CardSelectForegroundSwitchPatch ^
      --u16 "2026-08-19-r5" ^
      --absent-utf8 "__runOriginal"
  python dll_check.py --deployed --marker "2026-08-19-r5"       # 自动检查测试槽位 DLL
  python dll_check.py --deployed --expect-deployed               # 部署位与仓库根字节一致

退出码：0 = 全部断言通过；1 = 有缺失/意外出现；2 = 用法/文件错误。
"""

import argparse
import hashlib
import json
import os
import re
import struct
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")  # 避免 Windows 控制台按 GBK 解码中文输出

# 本项目的部署槽位（游戏实际加载的 mods 目录）；与 tools/deploy_dll.ps1 的默认槽位保持一致
DEFAULT_DEPLOYED = r"D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\DualRoleAdventure\DualRoleAdventurefixed.dll"
DEFAULT_ROOT = r"D:\Download\pain\STS2_DualRoleAdventure-itriedtofix\DualRoleAdventure.dll"

MARKER_RE = re.compile(r"20\d\d-\d\d-\d\d-r\d+")

# UTF-16 明文（.NET 用户字符串堆 #US：每个字符两字节 LE，前有长度前缀）里找 ASCII 子串。
# 简单方案：把整个 UTF-16 空间按 2 字节步进解出来再 Contains；对大文件（几百 KB）完全够快。
def _u16_text(data):
    # 去掉 BOM（如有），按 UTF-16LE 解读；无法解码的字节以 U+FFFD 代替，不影响 ASCII 匹配。
    return data.decode("utf-16-le", errors="replace")


def find_utf16_markers(data):
    # #US 堆的字符串起始偏移可能是奇数（长度前缀 1/2/4 字节），两种对齐都要扫，
    # 否则 marker 会假阴性（旧实现只解偶对齐）。
    hits = set()
    for offset in (0, 1):
        for m in MARKER_RE.finditer(_u16_text(data[offset:])):
            hits.add(m.group(0))
    return sorted(hits)


def validate_pe_structure(data):
    """最低级合法性验证：这是一个结构完整的 .NET 程序集吗？

    抓的是游戏启动前就该拦下的问题：截断 DLL / 损坏 DLL / 拿错文件 /
    把 native DLL 改名成 .dll 混进来。返回 (ok, 描述)。
    """
    def u16(off):
        return struct.unpack_from("<H", data, off)[0]

    def u32(off):
        return struct.unpack_from("<I", data, off)[0]

    if len(data) < 0x40:
        return False, "文件过小（<64 字节），不是 PE"
    if data[:2] != b"MZ":
        return False, "缺少 MZ 签名"
    pe = u32(0x3C)
    if pe + 24 > len(data):
        return False, "e_lfanew 越界，文件被截断"
    if data[pe:pe + 4] != b"PE\0\0":
        return False, "缺少 PE\\0\\0 签名（可能是 native DLL 或文件损坏）"

    coff = pe + 4
    sections = u16(coff + 2)
    size_opt = u16(coff + 16)
    opt = coff + 20
    if size_opt == 0:
        return False, "SizeOfOptionalHeader=0，不是托管可执行文件"
    magic = u16(opt)
    dir_off = opt + 96 if magic == 0x10B else opt + 112
    if magic not in (0x10B, 0x20B):
        return False, "未知 Optional header magic=0x%X" % magic

    sec_off = opt + size_opt
    table = []
    for i in range(sections):
        base = sec_off + i * 40
        if base + 40 > len(data):
            return False, "节表越界，文件被截断"
        table.append((u32(base + 12), u32(base + 8), u32(base + 20), u32(base + 16)))

    def rva_to_off(rva):
        for va, vs, raw_ptr, raw_size in table:
            if va <= rva < va + max(vs, raw_size):
                return raw_ptr + (rva - va)
        return None

    cli_dir = dir_off + 14 * 8
    if cli_dir + 8 > len(data):
        return False, "无法定位 DataDirectory[14]（CLI header）"
    cli_rva, cli_size = u32(cli_dir), u32(cli_dir + 4)
    if cli_rva == 0 or cli_size == 0:
        return False, "CLI header 为空：这是 native（非 .NET）模块"
    cli_off = rva_to_off(cli_rva)
    if cli_off is None:
        return False, "CLI header RVA 无法映射，文件损坏"
    md_off = rva_to_off(u32(cli_off + 8))
    if md_off is None:
        return False, "MetaData RVA 无法映射，文件损坏"
    if md_off + 4 > len(data):
        return False, "MetaData root 越界，文件被截断"
    if data[md_off:md_off + 4] != b"BSJB":
        return False, "缺少 BSJB 元数据签名"
    return True, "PE/CLI/MetaData 结构完整"


def check_utf8(data, needle):
    """类型名/成员名（元数据 UTF-8 字节）直接按字节搜。"""
    return needle.encode("utf-8") in data


def check_u16(data, needle):
    return needle.encode("utf-16-le") in data


def main():
    ap = argparse.ArgumentParser(description="DLL 部署校验器（.NET 程序集字节内容检查）")
    ap.add_argument("dll", nargs="?", help="要检查的 DLL 路径")
    ap.add_argument("--marker", action="append", default=[], metavar="STR",
                    help="断言 UTF-16 用户字符串中存在该 marker（可多次）")
    ap.add_argument("--u16", dest="u16_needles", action="append", default=[], metavar="STR",
                    help="断言 UTF-16 字符串中存在（可多次）")
    ap.add_argument("--utf8", dest="utf8_needles", action="append", default=[], metavar="STR",
                    help="断言 UTF-8 元数据中存在类型/成员名（可多次）")
    ap.add_argument("--absent-u16", action="append", default=[], metavar="STR",
                    help="断言 UTF-16 中不存在（可多次）")
    ap.add_argument("--absent-utf8", action="append", default=[], metavar="STR",
                    help="断言 UTF-8 中不存在，如错误的写法 __runOriginal（可多次）")
    ap.add_argument("--deployed", action="store_true",
                    help="检查部署槽位 DLL（DualRoleAdventure 槽位），用 --dll 时忽略此位")
    ap.add_argument("--slot-dll", default=DEFAULT_DEPLOYED,
                    help="部署位 DLL 路径（默认 %s）" % DEFAULT_DEPLOYED)
    ap.add_argument("--root-dll", default=DEFAULT_ROOT,
                    help="仓库根 DLL 路径（默认 %s）" % DEFAULT_ROOT)
    ap.add_argument("--expect-deployed", action="store_true",
                    help="同时断言：部署位 DLL 与仓库根 DLL 字节完全一致（缺任一文件 = FAIL，不允许跳过即绿）")
    ap.add_argument("--no-pe-check", action="store_true",
                    help="跳过 PE/CLI/MetaData 结构校验（默认开启）")
    ap.add_argument("--list-markers", action="store_true", help="列出文件中找到的所有 r-marker")
    ap.add_argument("--json", action="store_true", help="JSON 输出结果")
    args = ap.parse_args()

    target = args.dll
    if args.deployed or (not target and args.expect_deployed):
        target = args.slot_dll
    if not target:
        ap.error("缺少 DLL 路径（或加 --deployed）")

    if not os.path.isfile(target):
        print("找不到文件: %s" % target, file=sys.stderr)
        return 2

    try:
        with open(target, "rb") as fh:
            data = fh.read()
    except OSError as exc:
        print("读取失败: %s" % exc, file=sys.stderr)
        return 2

    st = os.stat(target)
    results = []

    def ok(check, msg):
        results.append((check, msg))

    # 概要信息
    info = {
        "file": target,
        "size": st.st_size,
        "mtime": st.st_mtime,
    }

    markers = find_utf16_markers(data)
    info["markers_found"] = markers

    # 结构校验：截断 / 损坏 / 拿错文件（含 native DLL 改名）必须在这里就被抓出来
    if not args.no_pe_check:
        pe_ok, pe_detail = validate_pe_structure(data)
        ok(pe_ok, "是结构完整的 .NET 程序集: %s" % pe_detail)

    # 断言
    for needle in args.utf8_needles:
        ok(check_utf8(data, needle), "UTF-8 存在: %s" % needle)
    for needle in args.u16_needles + args.marker:
        ok(check_u16(data, needle), "UTF-16 存在: %s" % needle)
    for needle in args.absent_utf8:
        ok(not check_utf8(data, needle), "UTF-8 不存在: %s" % needle)
    for needle in args.absent_u16:
        ok(not check_u16(data, needle), "UTF-16 不存在: %s" % needle)

    if args.expect_deployed:
        # false-green 修复：缺文件一律 FAIL，绝不允许「跳过即绿」
        if not os.path.isfile(args.root_dll):
            ok(False, "仓库根 DLL 不存在（--expect-deployed 无法完成比对）: %s" % args.root_dll)
        elif not os.path.isfile(args.slot_dll):
            ok(False, "部署位 DLL 不存在（--expect-deployed 无法完成比对）: %s" % args.slot_dll)
        else:
            h1 = hashlib.sha256(data).hexdigest()
            with open(args.root_dll, "rb") as fh2:
                h2 = hashlib.sha256(fh2.read()).hexdigest()
            ok(h1 == h2, "部署位与仓库根 DLL 字节一致 (sha256=%s...)" % h1[:12])

    if args.list_markers:
        info["markers_list"] = markers

    failed = [msg for check, msg in results if not check]
    ok_all = not failed

    if args.json:
        out = {
            "info": info,
            "assertions": [
                {"pass": c, "msg": m} for c, m in results
            ],
            "all_pass": ok_all,
        }
        print(json.dumps(out, ensure_ascii=False, indent=2))
    else:
        print("文件: %s" % target)
        print("大小: %s 字节  修改时间: %s" % (st.st_size, st.st_mtime))
        if markers:
            print("发现 marker: %s" % ", ".join(markers))
        for check, msg in results:
            print("[%s] %s" % ("OK " if check else "MISS", msg))
        if not results:
            print("# 未指定断言；加 --marker/--utf8/--absent-* 做部署校验")

    return 0 if ok_all else 1


if __name__ == "__main__":
    sys.exit(main())
