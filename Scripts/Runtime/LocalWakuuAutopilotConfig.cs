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
