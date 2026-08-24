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

    public static string ConfigFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            ConfigFileName);

    /// <summary>
    /// 设置界面专用：更新单个开关，立即刷新内存生效值并把完整配置写回 json。
    /// key 取值与 json 字段一致：useVakuuForm / playAllCards / backgroundMode / suppressVanillaEarring。
    /// 返回 false 表示 key 未知或写盘失败（内存值也不会变）。
    /// </summary>
    public static bool TrySetAndSave(string key, bool value)
    {
        lock (_ioLock)
        {
            try
            {
                // 以磁盘上的现有内容为底稿改单键，避免覆盖玩家手改的其他字段。
                ConfigData data = ReadConfigDataOrThrow();
                switch (key)
                {
                    case nameof(ConfigData.useVakuuForm): data.useVakuuForm = value; break;
                    case nameof(ConfigData.playAllCards): data.playAllCards = value; break;
                    case nameof(ConfigData.backgroundMode): data.backgroundMode = value; break;
                    case nameof(ConfigData.suppressVanillaEarring): data.suppressVanillaEarring = value; break;
                    default:
                        LocalMultiControlLogger.Warn($"瓦库托管配置写入失败：未知开关名 {key}");
                        return false;
                }

                string? directory = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    ConfigFilePath,
                    JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
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

    /// <summary>读取磁盘配置；文件缺失/损坏时返回默认值底稿（默认=原版低语耳环行为）。</summary>
    private static ConfigData ReadConfigDataOrThrow()
    {
        string path = ConfigFilePath;
        if (!File.Exists(path))
        {
            return new ConfigData();
        }

        ConfigData? data = JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(path), JsonOptions);
        return data ?? new ConfigData();
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
                    Apply(new ConfigData(), logChanges: true);
                    return;
                }

                string json = File.ReadAllText(path);
                ConfigData? data;
                try
                {
                    data = JsonSerializer.Deserialize<ConfigData>(json, JsonOptions);
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

    private static void Apply(ConfigData data, bool logChanges)
    {
        if (logChanges)
        {
            LocalMultiControlLogger.Info(
                $"瓦库托管生效配置: useVakuuForm={data.useVakuuForm}, playAllCards={data.playAllCards}, "
                + $"backgroundMode={data.backgroundMode}, suppressVanillaEarring={data.suppressVanillaEarring}");
        }

        UseVakuuForm = data.useVakuuForm;
        PlayAllCards = data.playAllCards;
        BackgroundMode = data.backgroundMode;
        SuppressVanillaEarring = data.suppressVanillaEarring;
    }

    private static void WriteDefault(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(new ConfigData(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class ConfigData
    {
        public bool useVakuuForm { get; set; }

        public bool playAllCards { get; set; } = true;

        public bool backgroundMode { get; set; } = true;

        public bool suppressVanillaEarring { get; set; } = true;
    }
}
