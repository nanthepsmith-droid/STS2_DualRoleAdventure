using System.Runtime.CompilerServices;

// 单元测试需要访问 internal 成员（如 Entry 的期望补丁清单、纯逻辑规则表）。
// 说明：Godot.NET.Sdk 下 GenerateAssemblyInfo 不会被调度，csproj 的 <AssemblyAttribute> / AssemblyMetadata
// 会静默失效（见 LocalMultiControl.csproj 里 BuildIdentity 的 r89/r90 踩坑），因此这里用显式源文件声明。
[assembly: InternalsVisibleTo("LocalMultiControl.Tests")]
