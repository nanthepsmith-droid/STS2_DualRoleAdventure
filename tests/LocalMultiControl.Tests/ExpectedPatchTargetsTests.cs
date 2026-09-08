using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using LocalMultiControl.Scripts.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 启动自检期望补丁清单（Entry.CriticalPatchTargets / OptionalPatchTargets）的门禁测试。
///
/// 背景：这份清单是「补丁是否被 PatchAll 静默跳过」的唯一判据——写错一个字符，实机就会
/// 误报 Critical 缺失 → INIT_FAILED → mod 在主菜单报红（AGENTS.md §9 门禁 8）。
/// 且清单原本是 "Type.Method" 简写，同名类型 / 重载无法区分。
///
/// 本测试做三件事：
///   1. 格式合规：必须是完整类型名（含命名空间，≥2 个 '.'），可选 "/参数个数"；
///   2. 可解析：每条都要能在 sts2.dll 元数据里找到对应方法与参数个数（游戏更新 / 手误立即变红）；
///   3. 无重复项。
///
/// 取数走 System.Reflection.Metadata（只读元数据，不加载游戏程序集，避免 Godot 原生依赖），
/// 与 AssemblyCompatibilityTests 同一套路。缺少游戏安装时：本地开发 = Ignore，
/// 部署门禁（-p:RequireGameInstall=true）= Fail。
/// </summary>
[TestFixture]
public class ExpectedPatchTargetsTests
{
    private static readonly Regex TargetPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+(/\d+)?$", RegexOptions.Compiled);

    private static IEnumerable<(string Kind, string Target)> AllTargets()
    {
        foreach (string target in Entry.CriticalPatchTargets)
        {
            yield return ("Critical", target);
        }

        foreach (string target in Entry.OptionalPatchTargets)
        {
            yield return ("Optional", target);
        }
    }

    private static string? GetMetadata(string key)
    {
        return typeof(ExpectedPatchTargetsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
    }

    private static bool RequireGameInstall =>
        string.Equals(GetMetadata("RequireGameInstall"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? FindGameAssembly()
    {
        // 测试项目以 Private=true 引用 sts2.dll，构建时会复制到输出目录
        string local = Path.Combine(AppContext.BaseDirectory, "sts2.dll");
        if (File.Exists(local))
        {
            return local;
        }

        string? dataDir = GetMetadata("Sts2DataDir");
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            string candidate = Path.Combine(dataDir, "sts2.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? env = Environment.GetEnvironmentVariable("STS2_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string candidate = Path.Combine(env, "data_sts2_windows_x86_64", "sts2.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string RequireGameAssembly()
    {
        string? path = FindGameAssembly();
        if (path != null)
        {
            return path;
        }

        if (RequireGameInstall)
        {
            Assert.Fail("门禁模式（RequireGameInstall=true）下未找到游戏程序集 sts2.dll，判为失败。");
        }

        Assert.Ignore("未找到游戏程序集 sts2.dll，跳过期望补丁清单核对（本地开发默认跳过）。");
        return string.Empty;
    }

    private static string TypeFullName(MetadataReader reader, TypeDefinition type)
    {
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (declaring.IsNil)
        {
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        // 嵌套类型：运行期 FullName 用 '+' 连接（Outer+Inner）
        return TypeFullName(reader, reader.GetTypeDefinition(declaring)) + "+" + name;
    }

    /// <summary>
    /// 读取游戏程序集元数据，返回 { "Namespace.Type.Method": [参数个数...] }。
    /// </summary>
    private static Dictionary<string, List<int>> LoadGameMethods(string assemblyPath)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            string typeFullName = TypeFullName(reader, type);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                // 跳过编译器生成成员（属性访问器等对补丁清单无意义）
                if (methodName.StartsWith("get_", StringComparison.Ordinal)
                    || methodName.StartsWith("set_", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = typeFullName + "." + methodName;
                if (!result.TryGetValue(key, out List<int>? counts))
                {
                    counts = new List<int>();
                    result[key] = counts;
                }

                int argCount = method.GetParameters().Count;
                if (!counts.Contains(argCount))
                {
                    counts.Add(argCount);
                }
            }
        }

        return result;
    }

    [Test]
    public void 期望补丁清单_格式合规且无重复()
    {
        List<(string Kind, string Target)> all = AllTargets().ToList();
        Assert.That(all.Count, Is.GreaterThan(0), "期望补丁清单不应为空。");

        foreach ((string kind, string target) in all)
        {
            Assert.That(target, Is.Not.Null.And.Not.Empty, $"{kind} 清单存在空项。");
            Assert.That(TargetPattern.IsMatch(target), Is.True,
                $"{kind} 目标格式不合规（要求 Namespace.Type.Method 或 Namespace.Type.Method/参数个数）: {target}");
            Assert.That(target.Count(character => character == '.'), Is.GreaterThanOrEqualTo(2),
                $"{kind} 目标必须写完整类型名（含命名空间），不接受 'Type.Method' 简写: {target}");
        }

        List<string> duplicates = all
            .GroupBy(item => item.Target, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.That(duplicates, Is.Empty, "期望补丁清单存在重复项: " + string.Join(", ", duplicates));
    }

    [Test]
    public void 期望补丁清单_全部能在游戏程序集中解析()
    {
        string assemblyPath = RequireGameAssembly();
        Dictionary<string, List<int>> methods = LoadGameMethods(assemblyPath);
        Assert.That(methods.Count, Is.GreaterThan(0), "未从 sts2.dll 读到任何方法，元数据读取异常。");

        var failures = new List<string>();
        foreach ((string kind, string target) in AllTargets())
        {
            // 拆分可选的 "/参数个数"
            string name = target;
            int? expectedArgc = null;
            int slash = target.LastIndexOf('/');
            if (slash >= 0)
            {
                name = target.Substring(0, slash);
                expectedArgc = int.Parse(target.Substring(slash + 1));
            }

            if (!methods.TryGetValue(name, out List<int>? overloads))
            {
                failures.Add($"{kind} {target} → 游戏程序集中找不到该方法（类型或方法名已变？）");
                continue;
            }

            if (expectedArgc.HasValue && !overloads.Contains(expectedArgc.Value))
            {
                failures.Add($"{kind} {target} → 参数个数不匹配，实际为 {string.Join("/", overloads)}");
                continue;
            }

            // 未写参数个数但存在多个重载：不算失败，但打印出来提示可以钉死到签名级
            if (!expectedArgc.HasValue && overloads.Count > 1)
            {
                TestContext.WriteLine($"[AMBIGUOUS] {kind} {target} 有 {overloads.Count} 个重载（参数个数 {string.Join("/", overloads)}），可写 '/n' 钉死");
            }
        }

        Assert.That(failures, Is.Empty,
            "期望补丁清单存在无法在游戏程序集中解析的目标（游戏更新或清单写错，实机会误报 Critical 缺失）:\n  "
            + string.Join("\n  ", failures));
    }
}
