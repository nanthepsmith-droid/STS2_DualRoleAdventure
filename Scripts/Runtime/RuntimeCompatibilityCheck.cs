using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Logging;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 运行期 CLR / PE / Assembly 兼容性自检（AGENTS.md §9 门禁 4、5、8 的运行期部分）。
///
/// 覆盖此前完全没有门禁的一层：
///   - 进程架构（AMD64 期望）与映像运行时版本（v4.0.30319）
///   - 必需依赖加载：<see cref="BadImageFormatException"/> / FileNotFound / FileLoad
///   - 游戏程序集 ABI：编译期引用的 sts2 版本 vs 运行时实际加载的 sts2 版本
///
/// 依赖分类原则（重要）：
///   <see cref="RequiredAssemblies"/> 失败 = 致命；已知可选第三方（Koishi / SkadaHelper 等）失败 = WARN；
///   **未分类的 AssemblyRef 一律按可选处理**（静态引用不等于运行期必须加载），
///   避免把「某条代码路径才用到」的引用误判成致命而打坏现有架构。
/// </summary>
internal static class RuntimeCompatibilityCheck
{
    /// <summary>必需依赖：加载失败 = 致命（mod 无法工作）。</summary>
    private static readonly string[] RequiredAssemblies = { "sts2", "0Harmony", "GodotSharp" };

    /// <summary>已知可选第三方依赖：加载失败只 WARN（Koishi 本我修复 / SkadaHelper 社区统计等）。</summary>
    private static readonly string[] OptionalAssemblyPrefixes = { "Skada", "Koishi" };

    /// <summary>.NET 程序集映像的 CLR 运行时版本（.NET Framework 4.x 起固定为此值）。</summary>
    private const string ExpectedImageRuntimeVersion = "v4.0.30319";

    /// <summary>
    /// 执行兼容性自检。致命项写入 <paramref name="fatalFailures"/>（格式 "[CODE] 描述"）。
    /// 结束时打印 COMPAT_RESULT PASS|FAIL 供日志解析。
    /// </summary>
    internal static void Run(ICollection<string> fatalFailures)
    {
        int fatalBefore = fatalFailures.Count;

        Assembly self = typeof(RuntimeCompatibilityCheck).Assembly;
        LocalMultiControlLogger.Info(
            $"GAME_ID sts2={SafeVersion(GetGameAssembly())} clr={Environment.Version} " +
            $"arch={RuntimeInformation.ProcessArchitecture} self={self.GetName().Name} {SafeVersion(self)}");

        CheckProcessArchitecture(fatalFailures);
        CheckImageRuntimeVersion(self, fatalFailures);
        CheckGameAssemblyAbi(fatalFailures);
        CheckReferencedAssemblies(self, fatalFailures);

        int fatalCount = fatalFailures.Count - fatalBefore;
        if (fatalCount > 0)
        {
            LocalMultiControlLogger.Info($"COMPAT_RESULT FAIL fatal={fatalCount}");
        }
        else
        {
            LocalMultiControlLogger.Info("COMPAT_RESULT PASS");
        }
    }

    private static Assembly? GetGameAssembly()
    {
        try
        {
            return typeof(Log).Assembly;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"无法取得游戏主程序集（sts2）: {exception.GetType().Name}");
            return null;
        }
    }

    private static string SafeVersion(Assembly? assembly)
    {
        return assembly?.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>进程架构：游戏是 x86_64 构建，非 X64 进程说明跑错了运行时。</summary>
    private static void CheckProcessArchitecture(ICollection<string> fatalFailures)
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture == Architecture.X64 || architecture == Architecture.Arm64)
        {
            return;
        }

        string message = $"进程架构不符: {architecture}（期望 X64）";
        LocalMultiControlLogger.Error($"[CLR001] {message}");
        fatalFailures.Add($"[CLR001] {message}");
    }

    /// <summary>映像运行时版本：不是 v4.0.30319 说明这压根不是一个标准的 .NET 程序集。</summary>
    private static void CheckImageRuntimeVersion(Assembly self, ICollection<string> fatalFailures)
    {
        string imageRuntimeVersion = self.ImageRuntimeVersion;
        if (string.Equals(imageRuntimeVersion, ExpectedImageRuntimeVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string message = $"映像 CLR 运行时版本不符: {imageRuntimeVersion}（期望 {ExpectedImageRuntimeVersion}）";
        LocalMultiControlLogger.Error($"[CLR001] {message}");
        fatalFailures.Add($"[CLR001] {message}");
    }

    /// <summary>
    /// 程序集 ABI：编译期引用的 sts2 版本 vs 运行时加载的 sts2 版本。
    /// major/minor 不同 = 致命（结构性变更）；build/revision 不同 = WARN（游戏小版本更新）。
    /// </summary>
    private static void CheckGameAssemblyAbi(ICollection<string> fatalFailures)
    {
        Assembly? gameAssembly = GetGameAssembly();
        if (gameAssembly == null)
        {
            return;
        }

        Version? runtimeVersion = gameAssembly.GetName().Version;
        if (runtimeVersion == null)
        {
            return;
        }

        AssemblyName? compileReference = typeof(RuntimeCompatibilityCheck).Assembly
            .GetReferencedAssemblies()
            .FirstOrDefault(name => string.Equals(name.Name, "sts2", StringComparison.OrdinalIgnoreCase));
        Version? compileVersion = compileReference?.Version;

        if (compileVersion == null)
        {
            LocalMultiControlLogger.Warn("未能读取编译期 sts2 引用版本，跳过 ABI 比对。");
            return;
        }

        if (compileVersion.Major != runtimeVersion.Major || compileVersion.Minor != runtimeVersion.Minor)
        {
            string message =
                $"sts2 ABI 主版本漂移: 编译时 {compileVersion} → 运行时 {runtimeVersion}（需针对当前游戏重新编译）";
            LocalMultiControlLogger.Error($"[ASM002] {message}");
            fatalFailures.Add($"[ASM002] {message}");
            return;
        }

        if (compileVersion != runtimeVersion)
        {
            LocalMultiControlLogger.Warn(
                $"sts2 小版本差异（不阻断）: 编译时 {compileVersion} → 运行时 {runtimeVersion}；" +
                "若出现 Patch 缺失，请重新生成反编译源码并重编译。");
        }
    }

    /// <summary>
    /// 逐个尝试加载静态引用：
    ///   Required 失败 → 致命；Optional / 未分类失败 → WARN。
    /// 重点捕获 <see cref="BadImageFormatException"/>（PE 架构或格式错误拿错文件时才会出现）。
    /// </summary>
    private static void CheckReferencedAssemblies(Assembly self, ICollection<string> fatalFailures)
    {
        AssemblyName[] references;
        try
        {
            references = self.GetReferencedAssemblies();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"无法枚举引用程序集: {exception.GetType().Name}");
            return;
        }

        foreach (AssemblyName reference in references)
        {
            string? name = reference.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            bool required = RequiredAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool optional = OptionalAssemblyPrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            try
            {
                Assembly.Load(reference);
            }
            catch (BadImageFormatException exception)
            {
                ReportLoadFailure(fatalFailures, name, "CLR002", "PE/CLR 映像格式错误（拿错平台或文件已损坏）",
                    required, optional, exception);
            }
            catch (FileNotFoundException exception)
            {
                ReportLoadFailure(fatalFailures, name, "DEP001", "依赖缺失", required, optional, exception);
            }
            catch (FileLoadException exception)
            {
                ReportLoadFailure(fatalFailures, name, "DEP002", "依赖加载失败", required, optional, exception);
            }
            catch (Exception exception)
            {
                ReportLoadFailure(fatalFailures, name, "DEP003", "依赖加载异常", required, optional, exception);
            }
        }
    }

    private static void ReportLoadFailure(
        ICollection<string> fatalFailures,
        string assemblyName,
        string code,
        string what,
        bool required,
        bool optional,
        Exception exception)
    {
        string kind = required ? "必需依赖" : (optional ? "可选依赖" : "未分类依赖");
        string message = $"{what}: {assemblyName} ({exception.GetType().Name}: {exception.Message})";
        if (required)
        {
            LocalMultiControlLogger.Error($"[{code}] {kind} {message}");
            fatalFailures.Add($"[{code}] {kind} {message}");
            return;
        }

        LocalMultiControlLogger.Warn($"[{code}] {kind} {message}（不阻断加载）");
    }
}
