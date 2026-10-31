using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ThirdPartyHotPathFix.Scripts.Patches;

/// <summary>
/// 放宽 `CombatState.IterateHookListeners` / `RunState.IterateHookListeners` 里
/// **listener 列表的初始容量**（原版写死 `new List&lt;AbstractModel&gt;(Players.Count * 50)`）。
///
/// 为什么（2026-10-31 实测）：`Players.Count * 50` 在 12 席时只有 600 槽，而真实 listener 数
/// （12 席 × 卡 + 附魔 + 遗物 + 药水 + 球 + 能力…）一旦越过 600，**每次分发**都会触发
/// `List.AddWithResize` 扩容（600→1200，分配新数组 + 复制），实测这一条在开了海克斯符文
/// （符文=遗物，每席位再多十几件）之后吃掉**主线程 28~30%**（`List<>.set_Capacity` 自身耗时），
/// 关掉海克斯时只有 0.15~0.29%。
///
/// 修法：把 IL 里那个乘数常量放大（默认 50 → 100，12 席 = 1200 槽）。
/// **只改分配大小，不改任何语义**：容量给大了只是多占几百字节，给不够就退回原来的扩容行为。
/// 可用环境变量 `HOT_PATH_FIX_LISTENER_CAPACITY` 覆盖（乘数，非槽位总数）。
///
/// 目标方法说明：`IterateHookListeners` 是 `yield` 迭代器 ⇒ 建列表的 IL 在编译器生成的
/// `<IterateHookListeners>d__NN.MoveNext()` 里，所以要补的是那个状态机（按名字前缀找，不写死 d__NN）。
/// </summary>
internal static class ListenerListCapacityPatch
{
    internal const int DefaultMultiplier = 100;

    /// <summary>覆盖乘数的环境变量名（日志里会打出来，便于实机调参）。</summary>
    internal const string EnvVarName = "HOT_PATH_FIX_LISTENER_CAPACITY";

    private const string EnvVar = EnvVarName;

    /// <summary>乘数（原版 50）。只在第一次读取时取环境变量。</summary>
    internal static int Multiplier { get; } = ReadMultiplier();

    private static int ReadMultiplier()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvVar);
        if (int.TryParse(raw, out int value) && value >= 50 && value <= 2000)
        {
            return value;
        }

        return DefaultMultiplier;
    }

    /// <summary>Harmony 转译器入口：报告改了几处（0 = 形态不符，由调用方记一条 WARN）。</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        int replaced = RaiseInPlace(list);
        TranspilerReport.LastReplaced = replaced;
        return list;
    }

    /// <summary>
    /// 把 `... Ldc_I4(_S) 50 ; Mul ; Newobj List&lt;AbstractModel&gt;::.ctor(int32)` 里的常量放大。
    /// 返回替换处数（纯逻辑，可单测）。
    /// </summary>
    internal static int RaiseInPlace(List<CodeInstruction> instructions)
    {
        int replaced = 0;
        for (int i = 0; i < instructions.Count; i++)
        {
            CodeInstruction ins = instructions[i];
            if (ins.opcode != OpCodes.Newobj)
            {
                continue;
            }

            if (ins.operand is not ConstructorInfo ctor || !IsListenerListCtor(ctor))
            {
                continue;
            }

            int constantIndex = FindMultiplierConstantIndex(instructions, i);
            if (constantIndex < 0)
            {
                continue;
            }

            if (!TryGetIntConstant(instructions[constantIndex], out int value) || value <= 0 || value > 1000)
            {
                continue;
            }

            instructions[constantIndex] = new CodeInstruction(OpCodes.Ldc_I4, Multiplier)
            {
                labels = instructions[constantIndex].labels,
                blocks = instructions[constantIndex].blocks,
            };
            replaced++;
            TranspilerReport.LastOriginalValue = value;
        }

        return replaced;
    }

    private static bool IsListenerListCtor(ConstructorInfo ctor)
    {
        if (ctor.DeclaringType != typeof(List<AbstractModel>))
        {
            return false;
        }

        ParameterInfo[] parameters = ctor.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
    }

    /// <summary>从 `Newobj` 往前找 `Mul`，再往前找乘数常量；形态不符返回 -1（宁可不动）。</summary>
    private static int FindMultiplierConstantIndex(List<CodeInstruction> instructions, int newobjIndex)
    {
        int mulIndex = -1;
        for (int j = newobjIndex - 1; j >= 0 && j >= newobjIndex - 4; j--)
        {
            OpCode opcode = instructions[j].opcode;
            if (opcode == OpCodes.Nop)
            {
                continue;
            }

            if (opcode == OpCodes.Mul)
            {
                mulIndex = j;
            }

            break;
        }

        if (mulIndex < 0)
        {
            return -1;
        }

        for (int j = mulIndex - 1; j >= 0 && j >= mulIndex - 3; j--)
        {
            if (instructions[j].opcode == OpCodes.Nop)
            {
                continue;
            }

            return TryGetIntConstant(instructions[j], out _) ? j : -1;
        }

        return -1;
    }

    private static bool TryGetIntConstant(CodeInstruction instruction, out int value)
    {
        OpCode opcode = instruction.opcode;
        if (opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte small)
        {
            value = small;
            return true;
        }

        if (opcode == OpCodes.Ldc_I4 && instruction.operand is int full)
        {
            value = full;
            return true;
        }

        if (opcode == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }
        if (opcode == OpCodes.Ldc_I4_M1) { value = -1; return true; }

        value = 0;
        return false;
    }
}

/// <summary>转译器最后一次运行的结果（只在打补丁时写一次，日志用）。</summary>
internal static class TranspilerReport
{
    internal static int LastReplaced;
    internal static int LastOriginalValue;
}
