#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CLR / PE / Assembly 兼容性检查器（clr_compat_check）

补上此前完全没有门禁的一层：把「这个 DLL 到底是不是一个能被当前游戏 CLR 加载的合法
.NET 程序集」变成构建期可判定的结论，而不是等游戏启动报 BadImageFormatException。

检查内容（结构层，纯标准库解析 PE，不依赖 .NET / 第三方包）：

  1. 文件级   存在 / 非空 / 大小 / SHA256（顺带确认「检查的就是你认为的那个文件」）
  2. PE 结构  MZ → PE\\0\\0 → COFF header → Optional header → Section table
  3. 架构     Machine（AMD64 / x86 / ARM64）+ Optional magic（PE32 / PE32+）
              + COMIMAGE_FLAGS（ILONLY / 32BITREQUIRED），与游戏 sts2.dll 比对
  4. CLR      CLI header（DataDirectory[14]）存在、
              RuntimeVersion、MetaData root "BSJB" + 版本串（如 v4.0.30319）
  5. TFM      #US 堆中的 TargetFrameworkAttribute 串（如 .NETCoreApp,Version=v9.0）
  6. ABI      sts2.dll 的名称/版本/SHA256 作为基线输出，供 Assembly 层比对
              （精确的 Assembly 身份与引用 ABI 由 NUnit AssemblyCompatibilityTests 负责）
  7. 版本锚点 mod marker 里的 "game vX.Y.Z" vs 游戏 release_info.json（默认 WARN，
              --strict-game-version 变 FAIL）

游戏目录解析优先级（避免散落硬编码）：
  --game-dir  >  环境变量 STS2_DIR  >  LocalMultiControl.csproj 的 <Sts2Dir>  >  默认路径

用法:
  python clr_compat_check.py                                  # 检查仓库根 DualRoleAdventure.dll
  python clr_compat_check.py --mod-dll <path> --game-dir <dir>
  python clr_compat_check.py --extra-dll <other.dll>          # 附加检查（可多次，如各 fix 仓库产物）
  python clr_compat_check.py --deployed                       # 检查部署槽位 DLL
  python clr_compat_check.py --json
  python clr_compat_check.py --strict-game-version            # 游戏版本不一致即失败

退出码: 0 = PASS（允许 WARN）; 1 = 有 FAIL; 2 = 用法 / 环境错误。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import struct
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
DEFAULT_MOD_DLL = REPO_ROOT / "DualRoleAdventure.dll"
DEFAULT_CSPROJ = REPO_ROOT / "LocalMultiControl.csproj"
DEFAULT_GAME_DIR = r"D:\SteamLibrary\steamapps\common\Slay the Spire 2"
DEFAULT_SLOT_DLL = Path(DEFAULT_GAME_DIR) / "mods" / "DualRoleAdventure" / "DualRoleAdventurefixed.dll"

# 期望 TFM 的单一来源：默认 net9.0（与 LocalMultiControl.csproj 的 <TargetFramework> 一致），
# 升级游戏运行时时只改这里 / 用 --expect-tfm 覆盖，不要在多个文件里各写一份。
DEFAULT_EXPECT_TFM = "net9.0"
# 最低要求的元数据版本（.NET Framework 4.x 起固定）
EXPECTED_METADATA_VERSION_PREFIX = "v4.0"

MACHINE_NAMES = {
    0x0000: "UNKNOWN",
    0x014C: "I386",
    0x0162: "R3000",
    0x01A2: "SH3",
    0x01C0: "ARM",
    0x01C2: "THUMB",
    0x01C4: "ARMNT",
    0x0200: "IA64",
    0x0266: "MIPS16",
    0x0EBC: "EBC",
    0x5032: "RISCV32",
    0x5064: "RISCV64",
    0x8664: "AMD64",
    0xAA64: "ARM64",
}

# COMIMAGE_FLAGS（CLI header Flags）
FLAG_ILONLY = 0x00000001
FLAG_32BITREQUIRED = 0x00000002
FLAG_32BITPREFERRED = 0x00020000

MAGIC_PE32 = 0x10B
MAGIC_PE32PLUS = 0x20B


class PEError(Exception):
    pass


class PEInfo:
    """解析出来的最小可用 PE/CLR 信息。"""

    def __init__(self, path: Path):
        self.path = path
        self.size = path.stat().st_size
        self.sha256 = _sha256(path)
        with open(path, "rb") as handle:
            self.data = handle.read()
        self.machine = None
        self.machine_name = None
        self.magic = None
        self.magic_name = None
        self.characteristics = None
        self.subsystem = None
        self.cli_rva = None
        self.cli_size = None
        self.runtime_major = None
        self.runtime_minor = None
        self.metadata_version = None
        self.cli_flags = None
        self.target_framework = None
        self.marker_game_version = None
        self._parse()

    # ---------------------------------------------------------------- 解析
    def _u16(self, offset: int) -> int:
        return struct.unpack_from("<H", self.data, offset)[0]

    def _u32(self, offset: int) -> int:
        return struct.unpack_from("<I", self.data, offset)[0]

    def _parse(self) -> None:
        data = self.data
        if len(data) < 0x40:
            raise PEError("文件过小，不是合法的 PE（<64 字节）")
        if data[:2] != b"MZ":
            raise PEError("缺少 MZ 签名（可能不是 PE，或文件已截断/损坏）")

        e_lfanew = self._u32(0x3C)
        if e_lfanew + 24 > len(data):
            raise PEError("e_lfanew 越界，文件异常")
        if data[e_lfanew:e_lfanew + 4] != b"PE\0\0":
            raise PEError("缺少 PE\\0\\0 签名（可能是 native DLL 或文件损坏）")

        coff = e_lfanew + 4
        self.machine = self._u16(coff)
        self.machine_name = MACHINE_NAMES.get(self.machine, "0x%04X" % self.machine)
        number_of_sections = self._u16(coff + 2)
        size_of_optional = self._u16(coff + 16)
        self.characteristics = self._u16(coff + 18)

        opt = coff + 20
        if size_of_optional == 0:
            raise PEError("SizeOfOptionalHeader=0，不是托管可执行文件")
        self.magic = self._u16(opt)
        if self.magic == MAGIC_PE32:
            self.magic_name = "PE32"
            dir_offset = opt + 96
        elif self.magic == MAGIC_PE32PLUS:
            self.magic_name = "PE32+"
            dir_offset = opt + 112
        else:
            raise PEError("未知 Optional header magic=0x%X" % self.magic)
        self.subsystem = self._u16(opt + 68)

        # Section table 紧跟 Optional header
        sections = []
        sec_off = opt + size_of_optional
        for i in range(number_of_sections):
            base = sec_off + i * 40
            if base + 40 > len(data):
                raise PEError("节表越界")
            virtual_size = self._u32(base + 8)
            virtual_address = self._u32(base + 12)
            raw_size = self._u32(base + 16)
            raw_pointer = self._u32(base + 20)
            sections.append((virtual_address, virtual_size, raw_pointer, raw_size))

        # DataDirectory[14] = CLI header
        cli_dir = dir_offset + 14 * 8
        if cli_dir + 8 > len(data):
            raise PEError("无法定位 DataDirectory[14]（CLI header）")
        self.cli_rva = self._u32(cli_dir)
        self.cli_size = self._u32(cli_dir + 4)
        if self.cli_rva == 0 or self.cli_size == 0:
            raise PEError("CLI header 为空：这是一个 native（非 .NET）模块")

        cli_off = self._rva_to_offset(sections, self.cli_rva)
        if cli_off is None:
            raise PEError("CLI header RVA 无法映射到文件偏移")
        self.runtime_major = self._u16(cli_off + 4)
        self.runtime_minor = self._u16(cli_off + 6)
        metadata_rva = self._u32(cli_off + 8)
        metadata_size = self._u32(cli_off + 12)
        self.cli_flags = self._u32(cli_off + 16)

        metadata_off = self._rva_to_offset(sections, metadata_rva)
        if metadata_off is None:
            raise PEError("MetaData RVA 无法映射到文件偏移")
        if metadata_off + 16 > len(data):
            raise PEError("MetaData root 越界（文件被截断）")
        if data[metadata_off:metadata_off + 4] != b"BSJB":
            raise PEError("缺少 BSJB 元数据签名（元数据区损坏）")
        version_length = self._u32(metadata_off + 12)
        if version_length <= 0 or version_length > 255:
            raise PEError("MetaData 版本长度异常=%d" % version_length)
        raw_version = data[metadata_off + 16:metadata_off + 16 + version_length]
        self.metadata_version = raw_version.split(b"\0")[0].decode("utf-8", errors="replace")
        self.metadata_size = metadata_size

        # TFM：TargetFrameworkAttribute 的构造参数是 #US 堆里的 UTF-16 字符串
        self.target_framework = _find_target_framework(data)
        self.marker_game_version = _find_marker_game_version(data)

    @staticmethod
    def _rva_to_offset(sections, rva: int):
        for virtual_address, virtual_size, raw_pointer, raw_size in sections:
            span = max(virtual_size, raw_size)
            if virtual_address <= rva < virtual_address + span:
                return raw_pointer + (rva - virtual_address)
        return None

    # ---------------------------------------------------------------- 展示
    @property
    def runtime_version(self) -> str:
        return "v%d.%d" % (self.runtime_major, self.runtime_minor)

    @property
    def is_64bit(self) -> bool:
        return self.machine in (0x8664, 0xAA64, 0x5064, 0x0200)

    @property
    def ilonly(self) -> bool:
        return bool(self.cli_flags & FLAG_ILONLY)

    @property
    def requires32bit(self) -> bool:
        return bool(self.cli_flags & FLAG_32BITREQUIRED)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _u16_texts(data: bytes) -> tuple[str, str]:
    """.NET #US 堆的字符串起始偏移可能是奇数（长度前缀 1/2/4 字节），
    所以必须同时按偶对齐与奇对齐各解一次，否则会整串错位导致假阴性。"""
    return (data.decode("utf-16-le", errors="replace"),
            data[1:].decode("utf-16-le", errors="replace"))


def _u16_find(data: bytes, pattern: str) -> str | None:
    for text in _u16_texts(data):
        match = re.search(pattern, text)
        if match:
            return match
    return None


def _find_target_framework(data: bytes):
    """TargetFrameworkAttribute 形如 ".NETCoreApp,Version=v9.0"（#US 堆，UTF-16）。"""
    match = _u16_find(data, r"\.NET(?:CoreApp|Framework),Version=v([\d.]+)")
    return match.group(0) if match else None


def _find_marker_game_version(data: bytes):
    match = _u16_find(data, r"game v(\d+\.\d+\.\d+)")
    return match.group(1) if match else None


# ---------------------------------------------------------------- 环境解析
def resolve_game_dir(explicit: str | None) -> tuple[Path, str]:
    """返回 (游戏目录, 来源说明)。优先级：显式参数 > 环境变量 > csproj > 默认路径。"""
    if explicit:
        return Path(explicit), "--game-dir"
    env = os.environ.get("STS2_DIR")
    if env:
        return Path(env), "环境变量 STS2_DIR"
    if DEFAULT_CSPROJ.is_file():
        try:
            text = DEFAULT_CSPROJ.read_text(encoding="utf-8", errors="replace")
            match = re.search(r"<Sts2Dir>(.*?)</Sts2Dir>", text)
            if match and match.group(1).strip():
                return Path(match.group(1).strip()), "LocalMultiControl.csproj <Sts2Dir>"
        except OSError:
            pass
    return Path(DEFAULT_GAME_DIR), "内置默认路径"


def read_game_version(game_dir: Path):
    release_info = game_dir / "release_info.json"
    if not release_info.is_file():
        return None, str(release_info)
    try:
        raw = release_info.read_text(encoding="utf-8-sig")
    except OSError:
        return None, str(release_info)
    match = re.search(r'"version"\s*:\s*"([^"]+)"', raw)
    if not match:
        return None, str(release_info)
    return match.group(1).lstrip("vV"), str(release_info)


# ---------------------------------------------------------------- 检查
class Results:
    def __init__(self):
        self.rows: list[tuple[bool, str, str]] = []  # (pass, level, message)

    def add(self, ok: bool, message: str, level: str = "FAIL") -> None:
        self.rows.append((ok, "PASS" if ok else level, message))

    def check(self, ok: bool, message: str) -> None:
        self.add(ok, message)

    def warn(self, ok: bool, message: str) -> None:
        self.add(ok, message, level="WARN")

    @property
    def failed(self) -> list[str]:
        return [m for ok, level, m in self.rows if not ok and level == "FAIL"]

    @property
    def warned(self) -> list[str]:
        return [m for ok, level, m in self.rows if not ok and level == "WARN"]


def check_assembly(path: Path, label: str, baseline: PEInfo | None, expect_tfm: str,
                   results: Results) -> PEInfo | None:
    results.check(path.is_file(), "[%s] 文件存在: %s" % (label, path))
    if not path.is_file():
        return None
    try:
        info = PEInfo(path)
    except PEError as exc:
        results.check(False, "[%s] 是合法的 .NET 程序集: %s" % (label, exc))
        return None
    except OSError as exc:
        results.check(False, "[%s] 可读取: %s" % (label, exc))
        return None

    results.check(True, "[%s] PE 结构完整（MZ / PE / COFF / Optional / CLI / BSJB）" % label)
    results.check(
        info.metadata_version.startswith(EXPECTED_METADATA_VERSION_PREFIX),
        "[%s] MetaData 版本 %s（期望以 %s 开头）"
        % (label, info.metadata_version, EXPECTED_METADATA_VERSION_PREFIX),
    )
    expect_framework = ".NETCoreApp,Version=v%s" % expect_tfm.lstrip("net")
    if info.target_framework is None:
        # Godot.NET.Sdk 产物通常不把 TargetFrameworkAttribute 的值写进 #US 堆，
        # 这时 PE 层无法确认 TFM —— 交给 NUnit AssemblyCompatibilityTests（PEReader）精确校验。
        results.warn(False,
                     "[%s] #US 堆未找到 TargetFrameworkAttribute（Godot SDK 产物常见）；"
                     "TFM 由 NUnit AssemblyCompatibilityTests 校验" % label)
    else:
        results.check(
            info.target_framework == expect_framework,
            "[%s] TargetFramework %s（期望 %s）" % (label, info.target_framework, expect_framework),
        )

    if baseline is not None and baseline is not info:
        if info.ilonly and not info.requires32bit:
            # AnyCPU 托管程序集：Machine 恒为 I386/PE32，由 CLR 决定实际位数，
            # 拿它跟游戏的 AMD64/PE32+ 硬比会误报，这里只记录架构信息。
            results.check(True, "[%s] AnyCPU 托管程序集（ILONLY，无 32BITREQUIRED）：可加载进 %s 进程"
                          % (label, baseline.machine_name))
        else:
            results.check(
                info.machine == baseline.machine,
                "[%s] 非 AnyCPU，Machine %s 必须与游戏基线 %s 一致"
                % (label, info.machine_name, baseline.machine_name),
            )
            results.check(
                info.magic == baseline.magic,
                "[%s] 非 AnyCPU，Optional magic %s 必须与游戏基线 %s 一致"
                % (label, info.magic_name, baseline.magic_name),
            )
        results.check(
            not (info.requires32bit and baseline.is_64bit),
            "[%s] 未标记 32BITREQUIRED（游戏基线为 %s）" % (label, baseline.machine_name),
        )
    return info


def main() -> int:
    parser = argparse.ArgumentParser(description="CLR/PE/Assembly 兼容性检查器")
    parser.add_argument("--mod-dll", help="要检查的 mod 程序集（默认仓库根 DualRoleAdventure.dll）")
    parser.add_argument("--extra-dll", action="append", default=[], metavar="PATH",
                        help="附加检查的程序集（可多次，如各 fix 仓库产物）")
    parser.add_argument("--deployed", action="store_true", help="改为检查部署槽位 DLL")
    parser.add_argument("--game-dir", help="游戏安装目录（含 release_info.json 与 data_sts2_windows_x86_64）")
    parser.add_argument("--expect-tfm", default=DEFAULT_EXPECT_TFM,
                        help="期望 TargetFramework（默认 %s）" % DEFAULT_EXPECT_TFM)
    parser.add_argument("--strict-game-version", action="store_true",
                        help="marker 中的 game 版本与 release_info.json 不一致时判 FAIL（默认 WARN）")
    parser.add_argument("--json", action="store_true", help="JSON 输出")
    args = parser.parse_args()

    if args.mod_dll:
        mod_dll = Path(args.mod_dll)
    elif args.deployed:
        mod_dll = DEFAULT_SLOT_DLL
    else:
        mod_dll = DEFAULT_MOD_DLL

    game_dir, game_dir_source = resolve_game_dir(args.game_dir)
    sts2_dll = game_dir / "data_sts2_windows_x86_64" / "sts2.dll"

    results = Results()

    print("=" * 64)
    print("CLR / PE / Assembly 兼容性检查")
    print("=" * 64)
    print("  游戏目录     = %s   (来源: %s)" % (game_dir, game_dir_source))
    print("  游戏主程序集 = %s" % sts2_dll)
    print("  Mod 程序集   = %s" % mod_dll)
    print("  期望 TFM     = %s" % args.expect_tfm)
    print("-" * 64)

    baseline: PEInfo | None = None
    if sts2_dll.is_file():
        try:
            baseline = PEInfo(sts2_dll)
            print("  游戏基线: Machine=%s  PE=%s  MetaData=%s  SHA256=%s..."
                  % (baseline.machine_name, baseline.magic_name, baseline.metadata_version, baseline.sha256[:16]))
        except (PEError, OSError) as exc:
            results.check(False, "[sts2] 游戏主程序集可解析: %s" % exc)
    else:
        results.check(False, "[sts2] 找到游戏主程序集（无法建立兼容性基线）: %s" % sts2_dll)

    targets = [("Mod", mod_dll)] + [("Extra%d" % (i + 1), Path(p)) for i, p in enumerate(args.extra_dll)]
    infos: dict[str, PEInfo] = {}
    for label, path in targets:
        info = check_assembly(path, label, baseline, args.expect_tfm, results)
        if info is not None:
            infos[label] = info

    # 游戏版本锚点：marker 里的 game vX.Y.Z vs release_info.json
    game_version, release_info_path = read_game_version(game_dir)
    print("-" * 64)
    print("  release_info = %s" % release_info_path)
    mod_info = infos.get("Mod")
    if game_version and mod_info is not None:
        if mod_info.marker_game_version is None:
            results.warn(False, "[version] mod 中未找到 game vX.Y.Z 锚点")
        else:
            same = mod_info.marker_game_version == game_version
            message = "[version] mod 构建锚点 game v%s vs 当前游戏 v%s" % (
                mod_info.marker_game_version, game_version)
            if args.strict_game_version:
                results.check(same, message)
            else:
                results.warn(same, message + "（游戏已更新建议重新构建，--strict-game-version 可判失败）")
    elif game_version is None:
        results.warn(False, "[version] 无法读取游戏版本（release_info.json 缺失）")

    if args.json:
        print(json.dumps({
            "game_dir": str(game_dir),
            "game_dir_source": game_dir_source,
            "expect_tfm": args.expect_tfm,
            "baseline": {
                "machine": baseline.machine_name if baseline else None,
                "pe": baseline.magic_name if baseline else None,
                "metadata_version": baseline.metadata_version if baseline else None,
                "sha256": baseline.sha256 if baseline else None,
            } if baseline else None,
            "assemblies": {
                label: {
                    "path": str(info.path),
                    "size": info.size,
                    "sha256": info.sha256,
                    "machine": info.machine_name,
                    "pe": info.magic_name,
                    "metadata_version": info.metadata_version,
                    "runtime_version": info.runtime_version,
                    "target_framework": info.target_framework,
                    "il_only": info.ilonly,
                    "requires32bit": info.requires32bit,
                } for label, info in infos.items()
            },
            "checks": [{"pass": ok, "level": level, "msg": msg} for ok, level, msg in results.rows],
            "all_pass": not results.failed,
        }, ensure_ascii=False, indent=2))
    else:
        for ok, level, msg in results.rows:
            print("[%-4s] %s" % (level, msg))
        print("-" * 64)
        for label, info in infos.items():
            print("  %-6s Machine=%-6s PE=%-6s MetaData=%-11s TFM=%-28s SHA256=%s..."
                  % (label, info.machine_name, info.magic_name, info.metadata_version,
                     info.target_framework or "unknown", info.sha256[:16]))
        print("-" * 64)
        print("  WARN = %d   FAIL = %d" % (len(results.warned), len(results.failed)))
        print("  RESULT = %s" % ("FAIL" if results.failed else "PASS"))

    return 1 if results.failed else 0


if __name__ == "__main__":
    sys.exit(main())
