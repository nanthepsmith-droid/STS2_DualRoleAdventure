using System;
using System.IO;
using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管（瓦库形态）功能总开关配置。
/// 默认全部关闭 = 与 v1.35 原版瓦库（永久低语耳环）行为完全一致；
/// 想体验新托管的玩家把 useVakuuForm 改为 true 即可。
/// 配置文件：%APPDATA%\SlayTheSpire2\vakuu_autopilot.json，每次开局（RunManager.Launch）时重新加载。
/// </summary>
internal static class LocalWakuuAutopilotConfig
{
    private const string ConfigFileName = "vakuu_autopilot.json";

    private static readonly object _ioLock = new();

    /// <summary>总开关：true 时瓦库角色改发【瓦库形态】遗物并启用新托管行为；false 时保持原低语耳环路径。</summary>
    public static bool UseVakuuForm { get; private set; }

    /// <summary>瓦库形态：打光所有手牌（false = 沿用原版每回合最多 13 张的上限）。</summary>
    public static bool PlayAllCards { get; private set; } = true;

    /// <summary>瓦库形态：后台托管——不再为瓦库角色自动切换前台。</summary>
    public static bool BackgroundMode { get; private set; } = true;

    /// <summary>瓦库形态：压制原版低语耳环的自动出牌钩子（保留其 +1 能量）。</summary>
    public static bool SuppressVanillaEarring { get; private set; } = true;

    /// <summary>
    /// 防止瓦库托管遗物被第三方效果移除（r83，默认开）：拦截 Player.RemoveRelicInternal 保住
    /// 【瓦库形态】/【永久低语耳环】，并让托管判据在遗物缺失时按瓦库名单兜底 + 补发。
    /// 关闭 = 回到 r82 行为（遗物可被引擎/第三方正常移除，移除后瓦库停止自动操作）。
    /// </summary>
    public static bool KeepWakuuFormRelic { get; private set; } = true;

    /// <summary>瓦库形态：战后卡牌奖励自动领最左（仅瓦库角色自己的奖励）。</summary>
    public static bool AutoClaimCards { get; private set; } = true;

    /// <summary>瓦库形态：金币与遗物奖励自动领取。</summary>
    public static bool AutoClaimGoldRelics { get; private set; } = true;

    /// <summary>
    /// 瓦库形态：药水奖励自动领取（2026-08-25 追加拍板）。
    /// 有空位直接领；满栏时若栏内有鲜血药水先喝掉腾位；否则奖励稀有度高于栏内最低稀有度
    /// 才丢弃栏内最低者领取，等价或更低则不领。
    /// </summary>
    public static bool AutoClaimPotions { get; private set; } = true;

    /// <summary>瓦库形态：非共享事件自动选最上（复杂/进战斗选项即停，交还真人）。</summary>
    public static bool AutoChooseEvents { get; private set; } = true;

    /// <summary>瓦库形态：火堆自动选择（低血睡觉；高血按策略升级牌或用遗物选项；帐篷多选全拿）。</summary>
    public static bool AutoRestChoice { get; private set; } = true;

    /// <summary>
    /// 瓦库形态：战斗中自动用药水（Phase 2.5 保守版，默认关，已拍板）。
    /// 血液/再生低血自用；果汁到手立刻喝；增益/攻击/卡牌授予类精英 Boss 战首回合用；
    /// mod 药水普通战斗随机回合消耗；未分类原版药水保守跳过。
    /// </summary>
    public static bool AutoUsePotions { get; private set; }

    /// <summary>涅奥（NEOW）开局奖励是否也自动选（默认关，已拍板 #3）。</summary>
    public static bool NeowAutoChoose { get; private set; }

    /// <summary>
    /// 社区统计辅助（可行性分析 §8.2，默认关）：开启后卡牌奖励与事件选项优先参考
    /// SkadaHelper 的社区统计（卡牌 PickRate + 因果增益、事件选项胜率）；
    /// 未安装、查表 miss、样本量不足一律静默回退到与关闭时完全一致的行为。
    /// </summary>
    public static bool SkadaAssist { get; private set; }

    /// <summary>
    /// 智能选牌优先级（可行性分析 §9.1/9.2，默认关）：开启后事件里的牌库删除/变化选牌
    /// 按数据驱动优先级表选取；关闭或场景未知时维持既有 cardPickMode 策略。
    /// </summary>
    public static bool SmartPick { get; private set; }

    /// <summary>
    /// 附魔智能选牌（默认开）：开启后附魔选牌按 WakuuEnchantRules 规则表挑牌
    /// （来源：原版附魔一览表.md 用户填写）；关闭或该附魔填"维持现状"时维持既有 cardPickMode。
    /// </summary>
    public static bool SmartEnchant { get; private set; } = true;

    /// <summary>
    /// 跨角色卡组（默认关）：每个角色战后奖励追加一组从「其他角色」卡池抽取的 3 选 1 卡牌奖励
    /// （原作者未完成的 v1.30 设计，上游 GuyGinat 69c7d99 实现）。
    /// </summary>
    public static bool ExtraCrossCharacterCardReward { get; private set; }

    /// <summary>
    /// 个人偏好记录器（默认开）：记录真人点选的卡牌奖励/事件选项（三级决策链第①级数据源，
    /// 可行性分析 §8.4.1）。只记真人决策，瓦库自动不记。
    /// </summary>
    public static bool PersonalRecorder { get; private set; } = true;

    /// <summary>
    /// 个人统计决策辅助（默认关）：瓦库选牌/选事件优先参考本机个人统计，样本不足回退社区统计。
    /// </summary>
    public static bool PersonalAssist { get; private set; }

    /// <summary>
    /// 个人统计偏好档位（默认角色优先 = v1 现状，行为零变化）。
    /// characterFirst 角色优先 / volumeFirst 总量优先 / characterOnly 只看角色，
    /// 语义见 WakuuPersonalQuery.DecisionTiers 注释。同时影响瓦库卡牌奖励与事件选项两条决策链。
    /// </summary>
    public static string PersonalTier { get; private set; } = CharacterFirstTier;

    /// <summary>
    /// 商店自动化（Phase 4，默认关）：瓦库商店界面打开时自动买卡（社区统计胜率阈值 + 金币保底）。
    /// </summary>
    public static bool ShopAssist { get; private set; }

    /// <summary>
    /// 商店自动买卡补充项（默认关）：查不到社区统计胜率的卡（多为 mod 卡）也按金币保底买入。
    /// </summary>
    public static bool ShopAssistBuyNoData { get; private set; }

    /// <summary>
    /// 自有统计角标（默认关）：奖励选牌卡/商店卡/事件选项按钮的右下角个人统计角标 + 悬停弹窗。
    /// 只显示本地个人记录器数据，与皮皮军师/SkadaHelper 社区统计 UI 分开。
    /// </summary>
    public static bool StatBadge { get; private set; }

    /// <summary>
    /// 自有统计角标位置档位（默认左下：避开皮皮军师/SkadaHelper 画在卡右侧的社区统计标签）。
    /// </summary>
    public static string StatBadgeCorner { get; private set; } = WakuuStatBadgeCorner.BottomLeft;

    /// <summary>
    /// 统计角标数据来源档位（默认仅个人）：personalOnly / personalThenCommunity / blended。
    /// </summary>
    public static string StatBadgeSource { get; private set; } = WakuuStatBadgeSource.PersonalOnly;

    /// <summary>
    /// 事件自动选择的策略：first=第一个（最上）/ last=最后一个 / random=随机。
    /// 很多事件一直选第一个会死，可切到 last 或 random 规避。
    /// </summary>
    public static string EventChoiceMode { get; private set; } = FirstChoiceMode;

    /// <summary>
    /// 战斗内效果选牌策略（酒狐合成二选一、开局遗物二选一、"从手牌选 N 张"、
    /// 事件中的附魔/升级/变化选牌等）：first=最前 / last=最后 / random=随机 / rare=稀有度最高。
    /// 默认 last，避免合成永远拿到排在最前的牌。卡牌奖励不受此影响（始终领最左）。
    /// </summary>
    public static string CardPickMode { get; private set; } = LastChoiceMode;

    /// <summary>
    /// 战斗决策大脑（任务 2.2，默认启发式）：heuristic=现有出牌逻辑原样（行为零变化）；
    /// auto=自动探测可用求解器（当前未探测到一律回退启发式，行为与 heuristic 相同）。
    /// </summary>
    public static string BrainMode { get; private set; } = HeuristicBrainMode;

    public const string FirstChoiceMode = WakuuChoiceModes.First;
    public const string LastChoiceMode = WakuuChoiceModes.Last;
    public const string RandomChoiceMode = WakuuChoiceModes.Random;
    public const string RareChoiceMode = WakuuChoiceModes.Rare;
    public const string HeuristicBrainMode = WakuuBrainModes.Heuristic;
    public const string AutoBrainMode = WakuuBrainModes.Auto;
    public const string CharacterFirstTier = WakuuPersonalQuery.PersonalTierCharacterFirst;
    public const string VolumeFirstTier = WakuuPersonalQuery.PersonalTierVolumeFirst;
    public const string CharacterOnlyTier = WakuuPersonalQuery.PersonalTierCharacterOnly;

    public static string ConfigFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            ConfigFileName);

    /// <summary>
    /// 设置界面专用：更新单个开关，立即刷新内存生效值并把完整配置写回 json。
    /// key 取值与 json 字段一致（useVakuuForm / playAllCards / backgroundMode / suppressVanillaEarring /
    /// autoClaimCards / autoClaimGoldRelics / autoChooseEvents / neowAutoChoose）。
    /// 返回 false 表示 key 未知或写盘失败（内存值也不会变）。
    /// </summary>
    public static bool TrySetAndSave(string key, bool value)
    {
        lock (_ioLock)
        {
            try
            {
                // 以磁盘上的现有内容为底稿改单键，避免覆盖玩家手改的其他字段。
                WakuuConfigData data = ReadConfigDataOrThrow();
                switch (key)
                {
                    case nameof(WakuuConfigData.useVakuuForm): data.useVakuuForm = value; break;
                    case nameof(WakuuConfigData.playAllCards): data.playAllCards = value; break;
                    case nameof(WakuuConfigData.backgroundMode): data.backgroundMode = value; break;
                    case nameof(WakuuConfigData.suppressVanillaEarring): data.suppressVanillaEarring = value; break;
                    case nameof(WakuuConfigData.keepWakuuFormRelic): data.keepWakuuFormRelic = value; break;
                    case nameof(WakuuConfigData.autoClaimCards): data.autoClaimCards = value; break;
                    case nameof(WakuuConfigData.autoClaimGoldRelics): data.autoClaimGoldRelics = value; break;
                    case nameof(WakuuConfigData.autoClaimPotions): data.autoClaimPotions = value; break;
                    case nameof(WakuuConfigData.autoChooseEvents): data.autoChooseEvents = value; break;
                    case nameof(WakuuConfigData.autoRestChoice): data.autoRestChoice = value; break;
                    case nameof(WakuuConfigData.autoUsePotions): data.autoUsePotions = value; break;
                    case nameof(WakuuConfigData.neowAutoChoose): data.neowAutoChoose = value; break;
                    case nameof(WakuuConfigData.skadaAssist): data.skadaAssist = value; break;
                    case nameof(WakuuConfigData.smartPick): data.smartPick = value; break;
                    case nameof(WakuuConfigData.smartEnchant): data.smartEnchant = value; break;
                    case nameof(WakuuConfigData.extraCrossCharacterCardReward): data.extraCrossCharacterCardReward = value; break;
                    case nameof(WakuuConfigData.personalRecorder): data.personalRecorder = value; break;
                    case nameof(WakuuConfigData.personalAssist): data.personalAssist = value; break;
                    case nameof(WakuuConfigData.shopAssist): data.shopAssist = value; break;
                    case nameof(WakuuConfigData.shopAssistBuyNoData): data.shopAssistBuyNoData = value; break;
                    case nameof(WakuuConfigData.statBadge): data.statBadge = value; break;
                    default:
                        LocalMultiControlLogger.Warn($"瓦库托管配置写入失败：未知开关名 {key}");
                        return false;
                }

                WriteConfigData(data);
                Apply(data, logChanges: true);
                return true;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"瓦库托管配置写入异常（key={key}, value={value}）: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 设置界面专用：更新单个字符串型配置（eventChoiceMode / cardPickMode / wakuuBrain / personalTier）。
    /// 立即刷新内存生效值并写回 json；返回 false 表示 key 未知、值非法或写盘失败。
    /// </summary>
    public static bool TrySetAndSaveString(string key, string value)
    {
        lock (_ioLock)
        {
            try
            {
                if (key is nameof(WakuuConfigData.eventChoiceMode) or nameof(WakuuConfigData.cardPickMode)
                    or nameof(WakuuConfigData.wakuuBrain) or nameof(WakuuConfigData.personalTier)
                    or nameof(WakuuConfigData.statBadgeCorner) or nameof(WakuuConfigData.statBadgeSource))
                {
                    string? normalized = key switch
                    {
                        nameof(WakuuConfigData.wakuuBrain) => NormalizeBrainMode(value),
                        nameof(WakuuConfigData.cardPickMode) => NormalizeCardPickMode(value),
                        nameof(WakuuConfigData.personalTier) => NormalizePersonalTier(value),
                        nameof(WakuuConfigData.statBadgeCorner) => WakuuStatBadgeCorner.Normalize(value),
                        nameof(WakuuConfigData.statBadgeSource) => WakuuStatBadgeSource.Normalize(value),
                        _ => NormalizeChoiceMode(value),
                    };
                    if (normalized == null)
                    {
                        LocalMultiControlLogger.Warn($"瓦库托管配置写入失败：非法的策略取值 {value}（key={key}）");
                        return false;
                    }

                    WakuuConfigData data = ReadConfigDataOrThrow();
                    // r83 修复：statBadgeCorner / statBadgeSource 此前落进 else 分支被写到了 wakuuBrain，
                    // 导致角标位置改不动（永远停在默认左下）、数据来源也改不动。
                    switch (key)
                    {
                        case nameof(WakuuConfigData.eventChoiceMode):
                            data.eventChoiceMode = normalized;
                            break;
                        case nameof(WakuuConfigData.cardPickMode):
                            data.cardPickMode = normalized;
                            break;
                        case nameof(WakuuConfigData.personalTier):
                            data.personalTier = normalized;
                            break;
                        case nameof(WakuuConfigData.statBadgeCorner):
                            data.statBadgeCorner = normalized;
                            break;
                        case nameof(WakuuConfigData.statBadgeSource):
                            data.statBadgeSource = normalized;
                            break;
                        default:
                            data.wakuuBrain = normalized;
                            break;
                    }

                    WriteConfigData(data);
                    Apply(data, logChanges: true);
                    return true;
                }

                LocalMultiControlLogger.Warn($"瓦库托管配置写入失败：未知字符串配置项 {key}");
                return false;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"瓦库托管配置写入异常（key={key}, value={value}）: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>规范化事件选择策略取值（first/last/random）；非法返回 null。</summary>
    public static string? NormalizeChoiceMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            FirstChoiceMode => FirstChoiceMode,
            LastChoiceMode => LastChoiceMode,
            RandomChoiceMode => RandomChoiceMode,
            _ => null,
        };
    }

    /// <summary>规范化卡牌选牌策略取值（first/last/random/rare）；非法返回 null。</summary>
    public static string? NormalizeCardPickMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            FirstChoiceMode => FirstChoiceMode,
            LastChoiceMode => LastChoiceMode,
            RandomChoiceMode => RandomChoiceMode,
            RareChoiceMode => RareChoiceMode,
            _ => null,
        };
    }

    /// <summary>规范化战斗决策大脑取值（heuristic/auto）；非法返回 null。</summary>
    public static string? NormalizeBrainMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            HeuristicBrainMode => HeuristicBrainMode,
            AutoBrainMode => AutoBrainMode,
            _ => null,
        };
    }

    /// <summary>规范化个人统计偏好档位取值（characterFirst/volumeFirst/characterOnly）；非法返回 null。</summary>
    public static string? NormalizePersonalTier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            CharacterFirstTier => CharacterFirstTier,
            VolumeFirstTier => VolumeFirstTier,
            CharacterOnlyTier => CharacterOnlyTier,
            _ => null,
        };
    }

    private static void WriteConfigData(WakuuConfigData data)
    {
        string? directory = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(ConfigFilePath, WakuuConfigJson.Serialize(data));
    }

    /// <summary>读取磁盘配置；文件缺失/损坏时返回默认值底稿（默认=原版低语耳环行为）。</summary>
    private static WakuuConfigData ReadConfigDataOrThrow()
    {
        string path = ConfigFilePath;
        if (!File.Exists(path))
        {
            return new WakuuConfigData();
        }

        return WakuuConfigJson.Parse(File.ReadAllText(path)) ?? new WakuuConfigData();
    }

    public static void Reload(string source)
    {
        lock (_ioLock)
        {
            try
            {
                string path = ConfigFilePath;
                if (!File.Exists(path))
                {
                    // 首次运行写一份带注释说明的默认配置（JSON 本身不支持注释，注释写在日志里）。
                    WriteDefault(path);
                    LocalMultiControlLogger.Info($"瓦库托管配置不存在，已写入默认配置（默认=原版低语耳环行为）: {path}, source={source}");
                    Apply(new WakuuConfigData(), logChanges: true);
                    return;
                }

                string json = File.ReadAllText(path);
                WakuuConfigData? data;
                try
                {
                    data = WakuuConfigJson.Parse(json);
                }
                catch (JsonException exception)
                {
                    LocalMultiControlLogger.Warn($"瓦库托管配置解析失败，沿用当前生效值: {exception.Message}");
                    return;
                }

                if (data == null)
                {
                    LocalMultiControlLogger.Warn("瓦库托管配置为空，沿用当前生效值。");
                    return;
                }

                Apply(data, logChanges: true);
                LocalMultiControlLogger.Info($"瓦库托管配置已加载: source={source}, path={path}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"瓦库托管配置加载异常，沿用当前生效值: {exception.Message}");
            }
        }
    }

    private static void Apply(WakuuConfigData data, bool logChanges)
    {
        if (logChanges)
        {
            LocalMultiControlLogger.Info(
                $"瓦库托管生效配置: useVakuuForm={data.useVakuuForm}, playAllCards={data.playAllCards}, "
                + $"backgroundMode={data.backgroundMode}, suppressVanillaEarring={data.suppressVanillaEarring}, "
                + $"keepWakuuFormRelic={data.keepWakuuFormRelic}, "
                + $"autoClaimCards={data.autoClaimCards}, autoClaimGoldRelics={data.autoClaimGoldRelics}, "
                + $"autoClaimPotions={data.autoClaimPotions}, "
                + $"autoChooseEvents={data.autoChooseEvents}, autoRestChoice={data.autoRestChoice}, "
                + $"autoUsePotions={data.autoUsePotions}, "
                + $"neowAutoChoose={data.neowAutoChoose}, skadaAssist={data.skadaAssist}, "
                + $"smartPick={data.smartPick}, smartEnchant={data.smartEnchant}, "
                + $"extraCrossCharacterCardReward={data.extraCrossCharacterCardReward}, "
                + $"personalRecorder={data.personalRecorder}, personalAssist={data.personalAssist}, "
                + $"shopAssist={data.shopAssist}, shopAssistBuyNoData={data.shopAssistBuyNoData}, statBadge={data.statBadge}, " + $"statBadgeCorner={WakuuStatBadgeCorner.Normalize(data.statBadgeCorner)}, statBadgeSource={WakuuStatBadgeSource.Normalize(data.statBadgeSource)}, "
                + $"personalTier={NormalizePersonalTier(data.personalTier) ?? CharacterFirstTier}, "
                + $"eventChoiceMode={data.eventChoiceMode}, cardPickMode={data.cardPickMode}, "
                + $"wakuuBrain={data.wakuuBrain}");
        }

        UseVakuuForm = data.useVakuuForm;
        PlayAllCards = data.playAllCards;
        BackgroundMode = data.backgroundMode;
        SuppressVanillaEarring = data.suppressVanillaEarring;
        KeepWakuuFormRelic = data.keepWakuuFormRelic;
        AutoClaimCards = data.autoClaimCards;
        AutoClaimGoldRelics = data.autoClaimGoldRelics;
        AutoClaimPotions = data.autoClaimPotions;
        AutoChooseEvents = data.autoChooseEvents;
        AutoRestChoice = data.autoRestChoice;
        AutoUsePotions = data.autoUsePotions;
        NeowAutoChoose = data.neowAutoChoose;
        SkadaAssist = data.skadaAssist;
        SmartPick = data.smartPick;
        SmartEnchant = data.smartEnchant;
        ExtraCrossCharacterCardReward = data.extraCrossCharacterCardReward;
        PersonalRecorder = data.personalRecorder;
        PersonalAssist = data.personalAssist;
        ShopAssist = data.shopAssist;
        ShopAssistBuyNoData = data.shopAssistBuyNoData;
        StatBadge = data.statBadge;
        StatBadgeCorner = WakuuStatBadgeCorner.Normalize(data.statBadgeCorner);
        StatBadgeSource = WakuuStatBadgeSource.Normalize(data.statBadgeSource);
        PersonalTier = NormalizePersonalTier(data.personalTier) ?? CharacterFirstTier;
        EventChoiceMode = NormalizeChoiceMode(data.eventChoiceMode) ?? FirstChoiceMode;
        CardPickMode = NormalizeCardPickMode(data.cardPickMode) ?? LastChoiceMode;
        BrainMode = NormalizeBrainMode(data.wakuuBrain) ?? HeuristicBrainMode;

        // 配置变化 → 大脑缓存失效（下次主循环用新开关值重新创建）。
        WakuuBrainFactory.Reset();
    }

    private static void WriteDefault(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, WakuuConfigJson.Serialize(new WakuuConfigData()));
    }
}
