using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 给**自定义休息区选项**（<c>RestSiteOption</c> 子类）准备图标 —— 瓦库四功能共用基建
/// （净化 / 炼化都要往休息区注入选项）。
///
/// 背景：`RestSiteOption` 基类把图标路径**硬拼**成 `ui/rest_site/option_&lt;OptionId 小写&gt;.png`
/// （`RestSiteOption.cs:31`），而 dll-only mod 无法把这个 png 补进游戏 pck。缺失的图标会连着炸两处：
/// <list type="number">
/// <item>房间预加载按 <c>AssetPaths</c> 去加载它失败 ⇒ <c>AssetCache</c> 把它记进 `_failedAssets` ⇒
///   之后 <c>RestSiteOption.Icon</c> **抛 `AssetLoadException`**，而 <c>NRestSiteButton.Reload</c>
///   是「先设图标、再设按钮名字」⇒ 异常中断后**按钮名字永远不会被赋值**（实机显示场景占位文字 "Dig"）；</item>
/// <item>就算让 `Icon` 只返 null，<c>NRestSiteCharacter.RefreshThoughtBubbleVfx</c> 里的
///   <c>NThoughtBubbleVfx.SetTexture(null)</c> 仍会**抛 `NotImplementedException`**
///   （"Can't set texture unless thought bubble was initialized with a texture"）—— 它冒穿
///   <c>RestSiteSynchronizer.ChooseOption</c> ⇒ **整次「点选项」被中断**（实机表现：净化要点**两下**
///   才弹界面，第二下靠"同一选项已进过气泡"的提前 return 才通过）。</item>
/// </list>
///
/// 所以本工具做两件事：
/// <list type="number">
/// <item>把**一个游戏自带的同语义图标**注册到我们的路径下（自持副本，见下），让
///   <c>GetTexture2D(IconPath)</c> 命中缓存、**不去真的加载**那个不存在的文件；</item>
/// <item>在房间预加载**之前**调用（注入时调用即可）—— 理由见下面的差集语义。</item>
/// </list>
///
/// ⚠ **调用方切记不要覆写 `AssetPaths`**（基类实现就是返回 `IconPath`，保持原样即可）：
/// <c>PreloadManager.LoadAssetSets</c> 是这样算卸载集的 ——
/// <code>assetsToUnloadSet = Cache.GetLoadedCacheAssets().Except(本房间需求集)</code>
/// ⇒ 一旦把我们的路径从需求集里摘掉，注册好的图标会被**当成"本房间不需要"卸载并 Dispose**
/// （r210 实测：`休息区选项图标已就绪` 之后 5 行就被卸，`Icon` 又变 null，点击仍被空图标异常打断）。
/// 反过来，路径**留在**需求集里 ⇒ `needLoaded = 需求集 − 已缓存` 因我们已登记而跳过它
/// ⇒ 既不会被卸、也不会去加载那个不存在的文件。**这两件事必须成对出现**。
///
/// ⚠ **必须自持纹理副本**：直接借用 `GetTexture2D(原版路径)` 拿到的实例**不行** —— 它是
/// <c>AssetCache</c> 的「missed cache 资产」，进房时 <c>UnloadMissedCacheAssets</c> 会
/// <c>Dispose</c> 它，我们留在缓存里的别名随即悬空（`IsInstanceValid == false`）⇒ `GetAsset` 转而去
/// `ResourceLoader.Load` 我们那个不存在的路径 ⇒ 又回到上面第 2 条（r209 实测：进房后 18 条
/// `NotImplementedException`、每次点选项都要两下）。所以这里用
/// <c>GetImage()</c> + <c>ImageTexture.CreateFromImage</c> 造一份**归我们所有**的实例，
/// 它不在 AssetCache 的卸载台账里，谁也不回收。
/// </summary>
internal static class LocalRestSiteOptionIcon
{
    /// <summary>已成功注册过的选项 id（幂等；失败不置位，留给下次注入重试）。</summary>
    private static readonly HashSet<string> Registered = new(StringComparer.Ordinal);

    /// <summary>选项 id → 基类硬拼出来的图标路径。</summary>
    internal static string IconPathFor(string optionId)
    {
        return ImageHelper.GetImagePath($"ui/rest_site/option_{optionId.ToLowerInvariant()}.png");
    }

    /// <summary>
    /// 确保 <paramref name="optionId"/> 的图标可用。幂等；失败只记 WARN。
    /// </summary>
    /// <param name="optionId">休息区选项的 <c>OptionId</c>。</param>
    /// <param name="borrowedInnerPaths">
    /// 可借用的原版图标（按优先级；形如 <c>ui/rest_site/option_cook.png</c>）。
    /// 建议按语义接近度排序，例如"删牌类"借【烹饪 Cook】。
    /// </param>
    internal static void EnsureRegistered(string optionId, IReadOnlyList<string> borrowedInnerPaths)
    {
        string iconPath = IconPathFor(optionId);

        if (Registered.Contains(optionId) && PreloadManager.Cache.ContainsKey(iconPath))
        {
            return;
        }

        try
        {
            if (PreloadManager.Cache.ContainsKey(iconPath))
            {
                // 已经有人注册过（或上次成功过）：只补登记标记。
                Registered.Add(optionId);
                return;
            }

            Tuple<Texture2D, string>? owned = CreateOwnedTexture(borrowedInnerPaths);
            if (owned == null)
            {
                LocalMultiControlLogger.Warn(
                    $"休息区选项图标注册失败：原版备用图标都取不到，选项将没有图标（按钮名不受影响）。option={optionId}");
                return;
            }

            PreloadManager.Cache.SetAsset(iconPath, owned.Item1);
            Registered.Add(optionId);
            LocalMultiControlLogger.Info(
                $"休息区选项图标已就绪（自持纹理副本，不受缓存卸载影响）: {owned.Item2} -> {iconPath}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注册休息区选项图标异常: option={optionId}, error={exception.Message}");
        }
    }

    /// <summary>借原版图标造一份**我们自己持有**的纹理实例；全部不可用时返回 null。</summary>
    private static Tuple<Texture2D, string>? CreateOwnedTexture(IReadOnlyList<string> borrowedInnerPaths)
    {
        foreach (string innerPath in borrowedInnerPaths)
        {
            Texture2D? borrowed = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath(innerPath));
            if (borrowed == null)
            {
                continue;
            }

            // ① 首选：取像素重建一份 ImageTexture（完全归我们所有，与借用实例彻底解耦）。
            try
            {
                Image? image = borrowed.GetImage();
                if (image != null)
                {
                    return new Tuple<Texture2D, string>(ImageTexture.CreateFromImage(image), innerPath);
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"休息区选项图标取像素失败，改试复制实例: {innerPath}, error={exception.Message}");
            }

            // ② 退而求其次：复制一份独立 Resource。
            if (borrowed.Duplicate() is Texture2D duplicated)
            {
                return new Tuple<Texture2D, string>(duplicated, innerPath);
            }

            // ③ 最后才用借用实例本身（可能随缓存卸载失效，但至少本次非 null）。
            return new Tuple<Texture2D, string>(borrowed, innerPath);
        }

        return null;
    }
}
