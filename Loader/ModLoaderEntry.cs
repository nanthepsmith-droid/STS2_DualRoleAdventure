using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace DualRoleAdventure.Loader;

/// <summary>
/// 双版本**薄 loader**：按宿主游戏版本从 `lib\&lt;版本&gt;\` 里挑一份完整载荷并转发初始化。
///
/// <para><b>为什么这样做</b>：正式版（0.111.x）与 beta（0.113.x）的原版 API 差异不小
/// （选择器栈、商店库存、商店房间节点、愚者/恐惧/闪电战…）。生态里内容 mod 的通行做法是
/// **每个分支各编一份完整 dll**，运行期只加载匹配的那份 —— 这样每份载荷都对着"自己的原版 dll"编译，
/// 既不需要反射兜底，也不会在 JIT 时撞上缺失类型。</para>
///
/// <para><b>目录形态</b>（与 HextechRunes / Koishi 一致）：</para>
/// <code>
/// mods\DualRoleAdventure\
///   DualRoleAdventure.json                    ← manifest（min_game_version = 0.111.0）
///   DualRoleAdventure.dll                     ← 本 loader（游戏按 manifest id 加载的就是它）
///   lib\0.111.0\DualRoleAdventure.Payload.dll ← 由 stable/0.111 代码线构建
///   lib\0.113.0\DualRoleAdventure.Payload.dll ← 由 master（0.113 线）构建
/// </code>
///
/// <para><b>只允许用两个分支都有的原版 API</b>：<c>Log</c>、<c>ModManager.AssociateAssemblyWithMod</c>、
/// <c>ReleaseInfoManager</c>、<c>ModInitializerAttribute</c>（均已在 0.111.1 与 0.113.1 的反编译源码里核对）。</para>
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModLoaderEntry
{
    private const string LogPrefix = "[DualRoleAdventure.Loader]";
    private const string PayloadFolderName = "lib";

    /// <summary>载荷文件名里必须带的标记（用来在 `lib\&lt;版本&gt;\` 里认出"我们的"载荷）。</summary>
    private const string PayloadFileNameMarker = ".Payload";

    public static void Init()
    {
        try
        {
            LoadPayload();
        }
        catch (Exception exception)
        {
            // loader 绝不把异常冒穿 mod 加载链（那会让游戏显示"MOD_ERROR"）。
            Log.Error($"{LogPrefix} 载荷加载失败，本 mod 未启用: {exception}");
        }
    }

    private static void LoadPayload()
    {
        string? loaderDirectory = Path.GetDirectoryName(typeof(ModLoaderEntry).Assembly.Location);
        if (string.IsNullOrEmpty(loaderDirectory))
        {
            Log.Error($"{LogPrefix} 无法定位 loader 所在目录，本 mod 未启用。");
            return;
        }

        string libRoot = Path.Combine(loaderDirectory, PayloadFolderName);
        if (!Directory.Exists(libRoot))
        {
            Log.Error($"{LogPrefix} 缺少载荷目录（安装包不完整？）: {libRoot}");
            return;
        }

        var candidates = new List<(string Directory, Version Version)>();
        foreach (string subDirectory in Directory.GetDirectories(libRoot))
        {
            string name = Path.GetFileName(subDirectory);
            if (PayloadVersionPolicy.TryParseVersion(name, out Version version))
            {
                candidates.Add((name, version));
            }
            else
            {
                Log.Warn($"{LogPrefix} 忽略无法识别为版本号的载荷目录: {name}");
            }
        }

        string hostLabel = ReleaseInfoManager.Instance?.ReleaseInfo?.Version ?? "<unknown>";
        Version? host = PayloadVersionPolicy.TryParseVersion(hostLabel, out Version parsedHost) ? parsedHost : null;

        if (!PayloadVersionPolicy.Select(candidates, host, out string? chosen, out string reason))
        {
            Log.Error(
                $"{LogPrefix} 未选择任何载荷：{reason}。宿主={hostLabel}, "
                + $"可用载荷=[{string.Join(", ", candidates.Select(candidate => candidate.Directory))}]");
            return;
        }

        Log.Info($"{LogPrefix} {reason}（宿主={hostLabel}）");

        string? payloadPath = FindPayloadFile(Path.Combine(libRoot, chosen!));
        if (payloadPath == null)
        {
            Log.Error($"{LogPrefix} 选中的载荷目录里没有 *{PayloadFileNameMarker}*.dll: {Path.Combine(libRoot, chosen!)}");
            return;
        }

        string modId = ResolveModId(loaderDirectory);

        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(typeof(ModLoaderEntry).Assembly)
            ?? AssemblyLoadContext.Default;
        Assembly payload = context.LoadFromAssemblyPath(payloadPath);

        // 关键一步：把载荷登记到本 mod 名下 —— 游戏的模型扫描（自定义卡/遗物/状态）与联机类型映射
        // 都按 "mod → assemblies" 找类型，不关联就会出现"补丁打了但自定义内容没进 ModDb"。
        ModManager.AssociateAssemblyWithMod(modId, payload);
        Log.Info($"{LogPrefix} 载荷已加载并关联到 mod {modId}: {payloadPath}");

        InvokePayloadInitializer(payload);
    }

    /// <summary>在选中的版本目录里找载荷 dll（文件名带 `.Payload` 标记；不写死全名，便于槽位改名）。</summary>
    private static string? FindPayloadFile(string variantDirectory)
    {
        return Directory.GetFiles(variantDirectory, "*" + PayloadFileNameMarker + "*.dll")
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// 解析本槽位真正的 mod id。
    ///
    /// <para>不能写死：本机槽位把 manifest 改名叫 `DualRoleAdventurefixed.json`（id `DualRoleAdventurefixed`）
    /// 以避免与工坊订阅版重复加载，而工坊包里 id 是 `DualRoleAdventure`；
    /// dll 文件名按规则必须等于槽位 id，但**程序集名**在构建时是固定的 ⇒ 只能读槽位 manifest。</para>
    /// </summary>
    private static string ResolveModId(string loaderDirectory)
    {
        string assemblyName = typeof(ModLoaderEntry).Assembly.GetName().Name ?? "DualRoleAdventure";
        string? fallback = null;

        foreach (string jsonPath in Directory.GetFiles(loaderDirectory, "*.json"))
        {
            try
            {
                using FileStream stream = File.OpenRead(jsonPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
                {
                    continue;
                }

                string? id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (string.Equals(id, assemblyName, StringComparison.Ordinal))
                {
                    return id!;
                }

                fallback ??= id;
            }
            catch (Exception exception)
            {
                Log.Warn($"{LogPrefix} 解析 manifest 失败（忽略）: {jsonPath} ⇒ {exception.GetType().Name}");
            }
        }

        return fallback ?? assemblyName;
    }

    /// <summary>转发到载荷里带 <c>[ModInitializer]</c> 的那个类型（= 我们真正的 <c>Entry.Init</c>）。</summary>
    private static void InvokePayloadInitializer(Assembly payload)
    {
        Type attributeType = typeof(ModInitializerAttribute);
        PropertyInfo? methodProperty = attributeType.GetProperty("initializerMethod");
        FieldInfo? methodField = attributeType.GetField("initializerMethod");

        foreach (Type type in GetLoadableTypes(payload))
        {
            object? attribute = type.GetCustomAttribute(attributeType);
            if (attribute == null)
            {
                continue;
            }

            string? methodName = methodProperty?.GetValue(attribute) as string
                ?? methodField?.GetValue(attribute) as string;
            if (string.IsNullOrEmpty(methodName))
            {
                Log.Error($"{LogPrefix} 载荷里的 {type.FullName} 标注了 [ModInitializer] 但取不到方法名。");
                return;
            }

            MethodInfo? initializer = type.GetMethod(
                methodName!,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (initializer == null)
            {
                Log.Error($"{LogPrefix} 载荷初始化器 {type.FullName}.{methodName} 不存在。");
                return;
            }

            Log.Info($"{LogPrefix} 调用载荷初始化器: {type.FullName}.{methodName}");
            initializer.Invoke(null, null);
            return;
        }

        Log.Error($"{LogPrefix} 载荷里没有找到任何 [ModInitializer]，本 mod 未启用: {payload.FullName}");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            Log.Warn($"{LogPrefix} 载荷里有类型加载失败，已跳过那些类型继续找初始化器。");
            return exception.Types.Where(type => type != null).Select(type => type!);
        }
    }
}
