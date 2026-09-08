using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using LocalMultiControl.Scripts.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 程序集兼容性测试（AGENTS.md §9 门禁 2、4、5）。
///
/// 用 System.Reflection.Metadata（net9.0 内置，无需额外 NuGet 包）在元数据层面校验：
///   - mod DLL 是合法的托管映像（结构层，截断/拿错文件会在这里现形）
///   - 目标运行时（TargetFrameworkAttribute，缺失时退回 System.Runtime 引用主版本）
///   - 反编译游戏源码没有被编译进 mod（源码隔离，门禁 2）
///   - 游戏程序集 ABI：编译期引用的 sts2 版本 vs 游戏安装目录里实际的那份
///
/// 游戏目录解析优先级：MSBuild AssemblyMetadata（Sts2DataDir）
///   &gt; 环境变量 STS2_DIR &gt; 不可用。
/// 语义区分：本地开发（默认）缺游戏 = Skip；部署门禁（-p:RequireGameInstall=true）缺游戏 = Fail。
/// </summary>
[TestFixture]
public class AssemblyCompatibilityTests
{
    private const string ExpectedTargetFramework = "net9.0";
    private const int ExpectedRuntimeMajor = 9;

    private static string ModDllPath => typeof(Entry).Assembly.Location;

    private static string? GetMetadata(string key)
    {
        return typeof(AssemblyCompatibilityTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
    }

    private static bool RequireGameInstall =>
        string.Equals(GetMetadata("RequireGameInstall"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetGameDataDir(out string dataDir)
    {
        dataDir = string.Empty;

        string? configured = GetMetadata("Sts2DataDir");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            dataDir = configured;
            return true;
        }

        string? env = Environment.GetEnvironmentVariable("STS2_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string candidate = Path.Combine(env, "data_sts2_windows_x86_64");
            if (Directory.Exists(candidate))
            {
                dataDir = candidate;
                return true;
            }
        }

        return false;
    }

    private static void RequireGameOrIgnore()
    {
        if (TryGetGameDataDir(out _))
        {
            return;
        }

        if (RequireGameInstall)
        {
            Assert.Fail("门禁模式（RequireGameInstall=true）下未找到游戏安装目录，判为失败。" +
                        "请设置环境变量 STS2_DIR 或传 -p:Sts2Dir=<游戏目录>。");
        }

        Assert.Ignore("未找到游戏安装目录（可设环境变量 STS2_DIR，或传 -p:Sts2Dir=<游戏目录>）；" +
                      "本地开发跳过，部署门禁会判失败。");
    }

    private static (PEReader Reader, MetadataReader Metadata) Open(string path)
    {
        var reader = new PEReader(File.OpenRead(path));
        return (reader, reader.GetMetadataReader());
    }

    private static string ReadAssemblyName(MetadataReader metadata)
    {
        AssemblyDefinition definition = metadata.GetAssemblyDefinition();
        return metadata.GetString(definition.Name);
    }

    private static Version ReadAssemblyVersion(MetadataReader metadata)
    {
        return metadata.GetAssemblyDefinition().Version;
    }

    /// <summary>读取 TargetFrameworkAttribute 的值（Godot SDK 产物可能没有，返回 null 时由 System.Runtime 版本兜底）。</summary>
    private static string? ReadTargetFramework(MetadataReader metadata)
    {
        AssemblyDefinition definition = metadata.GetAssemblyDefinition();
        foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
        {
            try
            {
                CustomAttribute attribute = metadata.GetCustomAttribute(handle);
                BlobReader blob = metadata.GetBlobReader(attribute.Value);
                if (blob.RemainingBytes < 2)
                {
                    continue;
                }

                blob.ReadUInt16(); // prolog = 0x0001
                if (blob.RemainingBytes < 1)
                {
                    continue;
                }

                string? value = blob.ReadSerializedString();
                if (!string.IsNullOrEmpty(value) && value.StartsWith(".NET", StringComparison.Ordinal))
                {
                    return value;
                }
            }
            catch (BadImageFormatException)
            {
                // 单个 attribute blob 解析失败不影响其他项
            }
        }

        return null;
    }

    private static int ReadRuntimeReferenceMajor(MetadataReader metadata)
    {
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(handle);
            if (string.Equals(metadata.GetString(reference.Name), "System.Runtime", StringComparison.Ordinal))
            {
                return reference.Version.Major;
            }
        }

        return 0;
    }

    [Test]
    public void Mod程序集是合法的托管映像()
    {
        Assert.That(File.Exists(ModDllPath), Is.True, "找不到 mod 程序集: " + ModDllPath);

        using FileStream stream = File.OpenRead(ModDllPath);
        using var reader = new PEReader(stream);
        Assert.That(reader.HasMetadata, Is.True, "mod DLL 没有 CLI 元数据（拿错文件或文件损坏）");

        PEHeaders headers = reader.PEHeaders;
        Assert.That(headers.IsCoffOnly, Is.False, "mod DLL 只有 COFF 头，不是托管程序集");
        Assert.That(headers.CorHeader, Is.Not.Null, "mod DLL 缺少 CLI header（可能是 native DLL）");
        Assert.That(reader.GetMetadataReader().MetadataVersion.StartsWith("v4.0", StringComparison.Ordinal),
            Is.True, "元数据版本异常: " + reader.GetMetadataReader().MetadataVersion);
    }

    [Test]
    public void Mod程序集目标运行时与项目一致()
    {
        var (reader, metadata) = Open(ModDllPath);
        using (reader)
        {
            string? targetFramework = ReadTargetFramework(metadata);
            if (targetFramework != null)
            {
                string expected = ".NETCoreApp,Version=v" + ExpectedTargetFramework.Replace("net", string.Empty);
                Assert.That(targetFramework, Is.EqualTo(expected),
                    "TargetFramework 与项目不一致（游戏运行时升级后需同步 --expect-tfm 与本项目）");
                return;
            }

            // Godot SDK 产物常常不写 TargetFrameworkAttribute：退回用 System.Runtime 引用主版本判断
            int runtimeMajor = ReadRuntimeReferenceMajor(metadata);
            Assert.That(runtimeMajor, Is.EqualTo(ExpectedRuntimeMajor),
                $"未找到 TargetFrameworkAttribute，且 System.Runtime 引用主版本为 {runtimeMajor}（期望 {ExpectedRuntimeMajor}）");
        }
    }

    [Test]
    public void Mod程序集未混入反编译游戏源码()
    {
        var (reader, metadata) = Open(ModDllPath);
        using (reader)
        {
            var leaked = new List<string>();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition definition = metadata.GetTypeDefinition(handle);
                string? ns = metadata.GetString(definition.Namespace);
                if (!string.IsNullOrEmpty(ns) && ns.StartsWith("MegaCrit.Sts2", StringComparison.Ordinal))
                {
                    leaked.Add(ns + "." + metadata.GetString(definition.Name));
                }
            }

            Assert.That(leaked, Is.Empty,
                "mod 程序集中出现了游戏命名空间的类型，说明反编译源码被编译进来了（AGENTS.md §1 源码隔离）。" +
                "示例: " + string.Join(", ", leaked.Take(5)));
        }
    }

    [Test]
    public void Mod引用的游戏程序集版本与编译期一致()
    {
        // 编译期引用 = 测试输出目录里随测试复制的 sts2.dll，与 HintPath 指向的是同一份
        string sts2Path = Path.Combine(TestContext.CurrentContext.TestDirectory, "sts2.dll");
        Assert.That(File.Exists(sts2Path), Is.True, "测试输出目录缺少 sts2.dll: " + sts2Path);

        var (modReader, modMetadata) = Open(ModDllPath);
        using (modReader)
        {
            var (gameReader, gameMetadata) = Open(sts2Path);
            using (gameReader)
            {
                Version gameVersion = ReadAssemblyVersion(gameMetadata);
                Assert.That(ReadAssemblyName(gameMetadata), Is.EqualTo("sts2"), "基线文件不是 sts2.dll");

                AssemblyName? reference = typeof(Entry).Assembly.GetReferencedAssemblies()
                    .FirstOrDefault(name => name.Name == "sts2");
                Assert.That(reference, Is.Not.Null, "mod 未引用 sts2");
                Assert.That(reference!.Version, Is.EqualTo(gameVersion),
                    $"mod 编译期引用的 sts2 版本 {reference.Version} 与基线 {gameVersion} 不一致");
            }
        }
    }

    [Test]
    public void 游戏安装目录的游戏程序集未发生ABI漂移()
    {
        RequireGameOrIgnore();
        TryGetGameDataDir(out string dataDir);

        string installed = Path.Combine(dataDir, "sts2.dll");
        Assert.That(File.Exists(installed), Is.True, "游戏目录缺少 sts2.dll: " + installed);

        string baseline = Path.Combine(TestContext.CurrentContext.TestDirectory, "sts2.dll");
        var (installedReader, installedMetadata) = Open(installed);
        using (installedReader)
        {
            var (baselineReader, baselineMetadata) = Open(baseline);
            using (baselineReader)
            {
                Version installedVersion = ReadAssemblyVersion(installedMetadata);
                Version baselineVersion = ReadAssemblyVersion(baselineMetadata);

                Assert.That(ReadAssemblyName(installedMetadata), Is.EqualTo("sts2"), "游戏目录里的不是 sts2.dll");
                Assert.That(installedVersion.Major, Is.EqualTo(baselineVersion.Major),
                    $"sts2 主版本漂移：编译时 {baselineVersion} → 游戏目录 {installedVersion}，需针对当前游戏重新编译");
                Assert.That(installedVersion.Minor, Is.EqualTo(baselineVersion.Minor),
                    $"sts2 次版本漂移：编译时 {baselineVersion} → 游戏目录 {installedVersion}，需针对当前游戏重新编译");
            }
        }
    }

    [Test]
    public void 游戏程序集架构可承载Mod程序集()
    {
        RequireGameOrIgnore();
        TryGetGameDataDir(out string dataDir);

        string installed = Path.Combine(dataDir, "sts2.dll");
        Assert.That(File.Exists(installed), Is.True, "游戏目录缺少 sts2.dll: " + installed);

        using FileStream gameStream = File.OpenRead(installed);
        using var gameReader = new PEReader(gameStream);
        using FileStream modStream = File.OpenRead(ModDllPath);
        using var modReader = new PEReader(modStream);

        CorFlags modFlags = modReader.PEHeaders.CorHeader!.Flags;
        bool modIsAnyCpu = (modFlags & CorFlags.ILOnly) != 0 && (modFlags & CorFlags.Requires32Bit) == 0;

        if (modIsAnyCpu)
        {
            // AnyCPU 由 CLR 决定位数，天然兼容游戏进程
            Assert.Pass("mod 为 AnyCPU 托管程序集，可加载进 " + gameReader.PEHeaders.CoffHeader.Machine + " 游戏进程");
        }

        Assert.That(modReader.PEHeaders.CoffHeader.Machine,
            Is.EqualTo(gameReader.PEHeaders.CoffHeader.Machine),
            "mod 非 AnyCPU，Machine 必须与游戏程序集一致");
    }
}
