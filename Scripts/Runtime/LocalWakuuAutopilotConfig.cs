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
    /// 事件自动选择的策略：first=第一个（最上）/ last=最后一个 / random=随机。
    /// 很多事件一直选第一个会死，可切到 last 或 random 规避。
    /// </summary>
    public static string EventChoiceMode { get; private set; } = FirstChoiceMode;

    /// <summary>
    /// 战斗内效果选牌策略（酒狐合成二选一、开局遗物二选一、"从手牌选 N 张"等）：
    /// first=最前 / last=最后 / random=随机。默认 last，避免合成永远拿到排在最前的牌。
    /// 卡牌奖励不受此影响（始终领最左）。
    /// </summary>
    public static string CardPickMode { get; private set; } = LastChoiceMode;

    public const string FirstChoiceMode = WakuuChoiceModes.First;
    public const string LastChoiceMode = WakuuChoiceModes.Last;
    public const string RandomChoiceMode = WakuuChoiceModes.Random;

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
                    case nameof(WakuuConfigData.autoClaimCards): data.autoClaimCards = value; break;
                    case nameof(WakuuConfigData.autoClaimGoldRelics): data.autoClaimGoldRelics = value; break;
                    case nameof(WakuuConfigData.autoClaimPotions): data.autoClaimPotions = value; break;
                    case nameof(WakuuConfigData.autoChooseEvents): data.autoChooseEvents = value; break;
                    case nameof(WakuuConfigData.autoRestChoice): data.autoRestChoice = value; break;
                    case nameof(WakuuConfigData.autoUsePotions): data.autoUsePotions = value; break;
                    case nameof(WakuuConfigData.neowAutoChoose): data.neowAutoChoose = value; break;
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
    /// 设置界面专用：更新单个字符串型配置（当前仅 eventChoiceMode：first/last/random）。
    /// 立即刷新内存生效值并写回 json；返回 false 表示 key 未知、值非法或写盘失败。
    /// </summary>
    public static bool TrySetAndSaveString(string key, string value)
    {
        lock (_ioLock)
        {
            try
            {
                if (key is nameof(WakuuConfigData.eventChoiceMode) or nameof(WakuuConfigData.cardPickMode))
                {
                    string? normalized = NormalizeChoiceMode(value);
                    if (normalized == null)
                    {
                        LocalMultiControlLogger.Warn($"瓦库托管配置写入失败：非法的策略取值 {value}（key={key}）");
                        return false;
                    }

                    WakuuConfigData data = ReadConfigDataOrThrow();
                    if (key == nameof(WakuuConfigData.eventChoiceMode))
                    {
                        data.eventChoiceMode = normalized;
                    }
                    else
                    {
                        data.cardPickMode = normalized;
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

    /// <summary>规范化事件选择策略取值；非法返回 null。</summary>
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
                + $"autoClaimCards={data.autoClaimCards}, autoClaimGoldRelics={data.autoClaimGoldRelics}, "
                + $"autoClaimPotions={data.autoClaimPotions}, "
                + $"autoChooseEvents={data.autoChooseEvents}, autoRestChoice={data.autoRestChoice}, "
                + $"autoUsePotions={data.autoUsePotions}, "
                + $"neowAutoChoose={data.neowAutoChoose}, "
                + $"eventChoiceMode={data.eventChoiceMode}, cardPickMode={data.cardPickMode}");
        }

        UseVakuuForm = data.useVakuuForm;
        PlayAllCards = data.playAllCards;
        BackgroundMode = data.backgroundMode;
        SuppressVanillaEarring = data.suppressVanillaEarring;
        AutoClaimCards = data.autoClaimCards;
        AutoClaimGoldRelics = data.autoClaimGoldRelics;
        AutoClaimPotions = data.autoClaimPotions;
        AutoChooseEvents = data.autoChooseEvents;
        AutoRestChoice = data.autoRestChoice;
        AutoUsePotions = data.autoUsePotions;
        NeowAutoChoose = data.neowAutoChoose;
        EventChoiceMode = NormalizeChoiceMode(data.eventChoiceMode) ?? FirstChoiceMode;
        CardPickMode = NormalizeChoiceMode(data.cardPickMode) ?? LastChoiceMode;
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
