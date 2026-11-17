using System;
using System.Collections.Generic;

namespace DualRoleAdventure.Loader;

/// <summary>
/// 载荷（`lib\&lt;游戏版本&gt;\DualRoleAdventure.Payload.dll`）的**选择口径** —— 纯逻辑，无游戏类型依赖。
///
/// <para><b>为什么单独一层</b>：loader 是唯一在两个分支上都要跑的程序集，所以它的决策必须简单、
/// 可单测、且不引入任何"某个分支才有"的类型。</para>
///
/// <para><b>口径</b>（照抄生态里内容 mod 的做法，见 HextechRunes / Koishi 的 loader）：</para>
/// <list type="bullet">
/// <item>目录名就是一个版本号（`0.111.0`、`0.113.0`）——**只到 minor**，patch 级共用
///   （0.111.1 用 `0.111.0` 那份；游戏改 patch 时不必重新出包）；</item>
/// <item>只选 **&lt;= 宿主版本** 里最新的那份，**绝不**加载比自己新的载荷
///   （防止 0.111 上跑 0.113 的 dll 直接炸）；</item>
/// <item>宿主版本读不到 ⇒ 取最新的那份并打 WARN（与生态做法一致：宁可试，也不静默不加载）。</item>
/// </list>
/// </summary>
internal static class PayloadVersionPolicy
{
    /// <summary>从目录名解析版本（容忍 `v` 前缀与 `-beta`/`+build` 后缀）。</summary>
    internal static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        int suffix = trimmed.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            trimmed = trimmed.Substring(0, suffix).Trim();
        }

        if (trimmed.Length >= 2 && (trimmed[0] == 'v' || trimmed[0] == 'V') && char.IsDigit(trimmed[1]))
        {
            trimmed = trimmed.Substring(1);
        }

        if (Version.TryParse(trimmed, out Version? parsed) && parsed != null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    /// <summary>把目录名规范化成"输出目录名"（`v0.111.0` → `0.111.0`；解析失败返回 null）。</summary>
    internal static string? NormalizeDirectoryName(string? name)
    {
        return TryParseVersion(name, out Version version) ? ToDirectoryName(version) : null;
    }

    /// <summary>`Version(0,111,1)` → `0.111.0`（**截到 minor**：patch 级共用一份载荷）。</summary>
    internal static string ToDirectoryName(Version version)
    {
        return $"{version.Major}.{version.Minor}.0";
    }

    /// <summary>
    /// 从候选目录里挑一个载荷。
    /// </summary>
    /// <param name="candidates">`(目录名, 版本)`，由调用方从 `lib\*` 枚举（版本已解析成功）。</param>
    /// <param name="host">宿主游戏版本；null/未知时按"取最新"处理。</param>
    /// <param name="chosen">选中的目录名（无可用时为 null）。</param>
    /// <param name="reason">人类可读的判定说明（写日志用）。</param>
    internal static bool Select(
        IReadOnlyList<(string Directory, Version Version)> candidates,
        Version? host,
        out string? chosen,
        out string reason)
    {
        chosen = null;
        if (candidates.Count == 0)
        {
            reason = "lib 目录下没有任何可识别的版本目录";
            return false;
        }

        string newest = candidates[0].Directory;
        Version newestVersion = candidates[0].Version;
        foreach ((string directory, Version version) in candidates)
        {
            if (version > newestVersion)
            {
                newest = directory;
                newestVersion = version;
            }
        }

        if (host == null)
        {
            chosen = newest;
            reason = $"宿主版本未知 ⇒ 取最新载荷 {newest}（可能不匹配，日志里会再报一次）";
            return true;
        }

        string? bestDirectory = null;
        Version? bestVersion = null;
        foreach ((string directory, Version version) in candidates)
        {
            if (version > host)
            {
                continue;
            }

            if (bestVersion == null || version > bestVersion)
            {
                bestDirectory = directory;
                bestVersion = version;
            }
        }

        if (bestDirectory == null)
        {
            reason = $"宿主 v{host} 比所有载荷都旧（最低 {newest}）⇒ 不加载任何载荷";
            return false;
        }

        chosen = bestDirectory;
        reason = bestVersion == host
            ? $"宿主 v{host} 精确命中载荷 {bestDirectory}"
            : $"宿主 v{host} ⇒ 取不高于它的最新载荷 {bestDirectory}（v{bestVersion}）";
        return true;
    }
}
