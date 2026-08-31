using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// SkadaHelper（创意工坊「皮皮军师」）社区统计的反射适配器（可行性分析 §8.2）。
///
/// 设计约束：
/// - **可选依赖**：不做编译期引用，全部走反射；未安装 / 内部结构改名 / 数据包未就绪一律静默失效，
///   调用方回退到既有的最左 / 最上策略——行为与本 mod 未接入统计时完全一致。
/// - **反射集中在本文件**：第三方结构变化只影响这里，业务侧只认 WakuuCardSignal / WakuuEventSignal。
/// - **只读消费**：仅读取内存中已公开的静态数据包，不改其状态、不重分发其数据，符合 AGENTS.md 约束。
/// - **用瓦库玩家自己的角色 id 查表**：不走它的 CurrentCharacterId（跟随前台，多控下语义漂移）。
///
/// 数据流：DataProvider.Data（BundleData）→ GetCharacter(charId)（CharacterBundle）
///       → GetCard(cardId)（CardStats）/ GetEvents(eventId)（List&lt;EventOptionStats&gt;）。
/// </summary>
internal static class WakuuSkadaAdapter
{
    private const string DataProviderTypeName = "SkadaHelper.Scripts.Lite.DataProvider";
    private const string DataPropertyName = "Data";

    private const string GetCharacterMethodName = "GetCharacter";
    private const string GetCardMethodName = "GetCard";
    private const string GetEventsMethodName = "GetEvents";

    /// <summary>
    /// 类型探测失败后的重试间隔。
    /// 实测（godot.log）本 mod 的初始化**早于** SkadaHelper 的程序集加载，启动探测必然失败，
    /// 因此必须允许后续重探。查表调用频率极低（一局仅卡牌奖励 + 事件选择，几十次量级），
    /// 间隔取小值即可：SkadaHelper 一加载完，最多延迟这么久就能用上。
    /// </summary>
    private const long ProbeRetryIntervalMs = 2_000;

    private static readonly object Sync = new();

    private static bool _typeProbed;
    private static bool _typeReady;
    private static long _nextProbeAtMs;

    private static MethodInfo? _dataGetter;
    private static MethodInfo? _getCharacter;
    private static MethodInfo? _getCard;
    private static MethodInfo? _getEvents;

    /// <summary>
    /// 启动探测：只打一次状态日志，任何失败都不抛、不影响后续加载（Mod 初始化不因第三方 mod 失败而中断）。
    /// </summary>
    public static void Probe()
    {
        try
        {
            if (!EnsureType(force: true))
            {
                LocalMultiControlLogger.Info(
                    "SkadaHelper 当前不可用：未安装，或其程序集晚于本 mod 加载（已验证属常见情况，"
                    + "将在首次查询时自动重新探测，无需重启；瓦库先按纯策略选择执行）。");
                return;
            }

            object? data = TryGetData();
            if (data == null)
            {
                LocalMultiControlLogger.Info(
                    "SkadaHelper 已加载但统计数据包尚未就绪（首次联网拉取或缓存加载中），将在首次查询时自动重试。");
                return;
            }

            LocalMultiControlLogger.Info(
                $"SkadaHelper 适配器就绪，社区统计辅助可用: {DescribeData(data)}；"
                + "是否启用由 skadaAssist 开关控制，未装/无数据时自动回退既有策略。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"SkadaHelper 启动探测异常，社区统计辅助不可用: {exception.Message}");
        }
    }

    /// <summary>
    /// 查卡牌社区统计。characterId / cardId 均为 ModelId.Entry（大写 slug，卡的裸 id 不含升级后缀）。
    /// 未安装、查表 miss、读取异常一律返回 null（调用方回退原策略）。
    /// </summary>
    public static WakuuCardSignal? TryGetCardSignal(string characterId, string cardId)
    {
        try
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(cardId) || !EnsureType(force: false))
            {
                return null;
            }

            object? bundle = TryGetCharacterBundle(characterId);
            object? stats = Invoke(_getCard, bundle, cardId);
            if (stats == null)
            {
                return null;
            }

            return new WakuuCardSignal(
                cardId,
                WakuuSignalPicking.NormalizeRate(ReadDouble(stats, "PickRate")),
                WakuuSignalPicking.NormalizeRate(ReadDouble(stats, "WinRateHeld")),
                WakuuSignalPicking.NormalizeRate(ReadDouble(stats, "WinRateSkipped")),
                ReadInt64(stats, "OfferCount"));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"SkadaHelper 卡牌统计查询异常，本次回退原策略: char={characterId}, card={cardId}, error={exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 查事件选项社区统计（eventId 大写，返回按数据集顺序的选项条目列表）。
    /// 未安装、该事件无数据、读取异常一律返回 null（调用方回退原策略）。
    /// </summary>
    public static List<WakuuEventSignal>? TryGetEventSignals(string characterId, string eventId)
    {
        try
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(eventId) || !EnsureType(force: false))
            {
                return null;
            }

            object? bundle = TryGetCharacterBundle(characterId);
            object? rawList = Invoke(_getEvents, bundle, eventId);
            if (rawList is not IEnumerable enumerable)
            {
                return null;
            }

            List<WakuuEventSignal> signals = new();
            foreach (object? entry in enumerable)
            {
                if (entry == null)
                {
                    continue;
                }

                signals.Add(new WakuuEventSignal(
                    ReadString(entry, "Text"),
                    WakuuSignalPicking.NormalizeRate(ReadDouble(entry, "WinRate")),
                    ReadInt64(entry, "Count")));
            }

            return signals.Count > 0 ? signals : null;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"SkadaHelper 事件统计查询异常，本次回退原策略: char={characterId}, event={eventId}, error={exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 适配器当前是否可用（类型已识别且数据包已就绪）。
    /// **仅用于日志诊断**——业务判定一律以查表返回值为准，不存在"半可用"状态。
    /// </summary>
    public static bool IsReady()
    {
        try
        {
            return EnsureType(force: false) && TryGetData() != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取当前生效的数据包实例；未就绪返回 null（下次查询会重新读取）。</summary>
    private static object? TryGetData()
    {
        return Invoke(_dataGetter, instance: null);
    }

    /// <summary>
    /// 读取数据包的诊断信息（生成时间 / 总局数 / 角色数），仅用于启动日志与排障；
    /// 任一项读不到就跳过，第三方结构变化不影响功能。
    /// </summary>
    private static string DescribeData(object data)
    {
        List<string> parts = new();

        string generatedAt = ReadString(data, "GeneratedAt");
        if (!string.IsNullOrEmpty(generatedAt))
        {
            parts.Add($"generatedAt={generatedAt}");
        }

        long totalRuns = ReadInt64(data, "TotalRuns");
        if (totalRuns > 0)
        {
            parts.Add($"totalRuns={totalRuns}");
        }

        long characterCount = TryCountCharacters(data);
        if (characterCount >= 0)
        {
            parts.Add($"characters={characterCount}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "（未能读取诊断信息）";
    }

    /// <summary>读 Characters 字典的元素个数；不可读返回 -1。</summary>
    private static long TryCountCharacters(object data)
    {
        if (!TryReadValue(data, "Characters", out object? raw) || raw is not IDictionary dictionary)
        {
            return -1;
        }

        return dictionary.Count;
    }

    private static object? TryGetCharacterBundle(string characterId)
    {
        return Invoke(_getCharacter, TryGetData(), characterId);
    }

    /// <summary>
    /// 探测并缓存第三方类型的反射成员。
    /// force=true 立即探测（仅启动探测使用）；force=false 为查表路径，受冷却间隔约束
    /// （TypeByName 需遍历全部已加载程序集）。
    /// 类型一旦识别成功即短路，后续查表不再走 TypeByName。
    /// </summary>
    private static bool EnsureType(bool force)
    {
        lock (Sync)
        {
            if (_typeReady)
            {
                return true;
            }

            long now = Environment.TickCount64;
            if (!force && _typeProbed && now < _nextProbeAtMs)
            {
                return false;
            }

            _typeProbed = true;
            _nextProbeAtMs = now + ProbeRetryIntervalMs;

            try
            {
                Type? providerType = AccessTools.TypeByName(DataProviderTypeName);
                PropertyInfo? dataProperty = providerType?.GetProperty(
                    DataPropertyName, BindingFlags.Public | BindingFlags.Static);
                if (providerType == null || dataProperty == null)
                {
                    return false;
                }

                _dataGetter = dataProperty.GetGetMethod(nonPublic: false);
                _getCharacter = AccessTools.Method(dataProperty.PropertyType, GetCharacterMethodName);
                Type? characterBundleType = _getCharacter?.ReturnType;
                _getCard = characterBundleType == null ? null : AccessTools.Method(characterBundleType, GetCardMethodName);
                _getEvents = characterBundleType == null ? null : AccessTools.Method(characterBundleType, GetEventsMethodName);

                _typeReady = _dataGetter != null && _getCharacter != null && _getCard != null && _getEvents != null;
                if (_typeReady)
                {
                    // 启动探测失败后由查表路径重新探到的情况：补一条日志说明是"迟到"而非"未安装"
                    LocalMultiControlLogger.Info(
                        "SkadaHelper 已在运行中完成探测（其程序集晚于本 mod 加载），社区统计辅助可用。");
                }

                return _typeReady;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"SkadaHelper 类型探测异常，社区统计辅助不可用: {exception.Message}");
                return false;
            }
        }
    }

    private static object? Invoke(MethodInfo? method, object? instance, params object?[] args)
    {
        if (method == null || (instance == null && !method.IsStatic))
        {
            return null;
        }

        try
        {
            return method.Invoke(instance, args);
        }
        catch (TargetInvocationException exception)
        {
            LocalMultiControlLogger.Warn(
                $"SkadaHelper 接口调用异常: {method.Name}, error={exception.InnerException?.Message ?? exception.Message}");
            return null;
        }
    }

    private static bool TryReadValue(object target, string memberName, out object? value)
    {
        value = null;
        try
        {
            Type type = target.GetType();
            FieldInfo? field = AccessTools.Field(type, memberName);
            if (field != null)
            {
                value = field.GetValue(target);
                return true;
            }

            PropertyInfo? property = AccessTools.Property(type, memberName);
            if (property == null)
            {
                return false;
            }

            value = property.GetValue(target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读数值型成员；缺失或不可转换返回 0（对选牌评分等价于"无信号"，不会误选）。</summary>
    private static double ReadDouble(object target, string memberName)
    {
        if (!TryReadValue(target, memberName, out object? raw) || raw == null)
        {
            return 0.0;
        }

        try
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>读整数型成员（样本量）；缺失返回 0 → 视为样本量不足、回退原策略。</summary>
    private static long ReadInt64(object target, string memberName)
    {
        if (!TryReadValue(target, memberName, out object? raw) || raw == null)
        {
            return 0L;
        }

        try
        {
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0L;
        }
    }

    private static string ReadString(object target, string memberName)
    {
        if (!TryReadValue(target, memberName, out object? raw) || raw == null)
        {
            return string.Empty;
        }

        return raw.ToString() ?? string.Empty;
    }
}
