using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private const string BuildMarker = "Revival v1.40.0 (game v0.111.0, marker=2026-09-10-r106)";

    private static Harmony? _harmony;

    /// <summary>
    /// 致命错误码（AGENTS.md §9）。日志 / 解析器 / issue 统一使用同一套 ID。
    /// </summary>
    private static class FatalCode
    {
        internal const string Config = "CONFIG001";      // 配置 / 记录器加载失败
        internal const string Model = "MODEL001";        // 遗物注册 / 本地化失败
        internal const string ClrRuntime = "CLR001";     // 进程架构 / 映像运行时版本不符
        internal const string ClrImage = "CLR002";       // 必需依赖 BadImageFormatException
        internal const string AsmAbi = "ASM002";         // 游戏程序集 ABI 版本漂移
        internal const string DepMissing = "DEP001";     // 必需依赖缺失
        internal const string DepLoad = "DEP002";        // 必需依赖加载失败
        internal const string PatchApply = "PATCH001";   // Harmony 应用中断
        internal const string PatchCritical = "PATCH002"; // Critical 补丁缺失
        internal const string PatchSelfTest = "PATCH003"; // 补丁自检无法执行
        internal const string Stage = "STG001";          // 阶段抛出未处理异常
    }

    /// <summary>
    /// 启动自检期望清单：这些目标必须被 Harmony 打上，否则说明被 PatchAll 静默跳过
    /// （本 mod 坑 1：类上缺类级 [HarmonyPatch] 时整个类被跳过且无任何报错）。
    ///
    /// 匹配口径（升级到完整名，消除同名类型 / 重载歧义）：
    ///   "Namespace.Type.Method"            完整类型名（清单标准写法，含命名空间）
    ///   "Namespace.Type.Method/2"          追加参数个数，用于区分重载（仅在确知补丁目标重载时写）
    /// 运行期会把「已打补丁方法」同时展开成 simple / full / simple+argc / full+argc 四种键，
    /// 所以写 full 一定能命中；写 full+argc 只在重载之间做精确区分。
    ///
    /// 门禁：tests/LocalMultiControl.Tests/ExpectedPatchTargetsTests.cs 会拿 sts2.dll 元数据
    /// 逐条核对（类型/方法存在、参数个数一致、格式合规）——写错在游戏更新或手误时**单测先红**，
    /// 不会等到实机才误报 Critical 缺失（那会 INIT_FAILED 让 mod 报红）。
    /// 维护口径：与 Scripts/Tools/patch_coverage.py 生成的 patch-coverage.md（pain/maintenance-docs/，无 git）一致。
    ///
    /// 【Critical】缺失 = 本地多控不可用 → 计入致命清单 → INIT_FAILED + 抛异常。
    /// </summary>
    internal static readonly string[] CriticalPatchTargets =
    {
        // ---- 选牌串行化 / 本地选牌判定（本地多控的核心，缺一个就会双角色同时选牌）----
        "MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.SelectCards",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHand",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHandForDiscard",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHandForUpgrade",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromChooseACardScreen",
        // FromCombatPile 有两个重载（参数 4 / 5），本 mod 两个都打了补丁 → 分别钉死签名，
        // 任意一个没打上就是真的漏了（r91 实机日志实证两条都在）。
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromCombatPile/4",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromCombatPile/5",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.ShouldSelectLocalCard",
        // ---- 回合流程 / 切前台 ----
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetupPlayerTurn",
        "MegaCrit.Sts2.Core.Combat.CombatManager.DoTurnEnd",
        "MegaCrit.Sts2.Core.Combat.CombatManager.FlushPlayerHand",
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetReadyToEndTurn",
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetReadyToBeginEnemyTurn",
        // ---- 击杀结算 ----
        "MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill",
        // ---- 事件 ----
        "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.BeginEvent",
        "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.ChooseLocalOption",
        // ---- 奖励归属（错归属 = 奖励给错角色）----
        "MegaCrit.Sts2.Core.Rewards.RewardsSet.Offer",
        "MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferCustom",
        "MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferForRoomEnd",
        "MegaCrit.Sts2.Core.Rooms.CombatRoom.OfferRoomEndRewards",
        "MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer.SelectLocalReward",
        // ---- 药水 / 动作队列 / 手牌变换 NetId 钉住 ----
        // TryToProcure 有 1 / 3 参数两个重载，补丁钉的是 3 参数那个（PotionModel, Player, int）
        "MegaCrit.Sts2.Core.Commands.PotionCmd.TryToProcure/3",
        "MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet.CombatEnded",
        "MegaCrit.Sts2.Core.Commands.CardCmd.Transform",
    };

    /// <summary>
    /// 【Optional】缺失只 WARN、不阻断加载：第三方联动、纯 UI 表现、瓦库自动化、个人偏好记录器。
    /// </summary>
    internal static readonly string[] OptionalPatchTargets =
    {
        // 第三方遗物联动
        "MegaCrit.Sts2.Core.Models.Relics.WhisperingEarring.AfterAutoPrePlayPhaseEnteredLate",
        // 纯 UI：结束回合按钮重评
        "MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton.CallReleaseLogic",
        // 瓦库：事件附魔自动作答（FromDeckForEnchantment 有三个重载，补丁钉的是
        // (IReadOnlyList<CardModel>, EnchantmentModel, int, CardSelectorPrefs) 那个）
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromDeckForEnchantment/4",
        // 个人记录器：整局胜负归因
        "MegaCrit.Sts2.Core.Runs.RunManager.OnEnded",
        // 个人记录器：真人事件点选
        "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.OptionButtonClicked",
        // 个人记录器：真人卡牌奖励点选
        "MegaCrit.Sts2.Core.Rewards.CardReward.OnSelect",
        // 个人记录器：事件网格选 N 入卡组
        "MegaCrit.Sts2.Core.Models.EventModel.SelectCardsToAddToDeckFromGrid",
        // 个人记录器：商店购买记录
        "MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry.OnTryPurchaseWrapper",
        // 个人记录器：真人删牌统计
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromDeckForRemoval",
    };

    /// <summary>
    /// 严格模式：致命失败时向游戏上报（抛异常 → 主菜单显示 MOD_ERROR.ASSEMBLY_LOAD）。
    /// 置 LMC_INIT_STRICT=0/off/false 可临时降级为「只打 INIT_FAILED 不抛」，用于救急排查。
    /// </summary>
    private static readonly bool StrictMode = ResolveStrictMode();

    public static void Init()
    {
        var fatalFailures = new List<string>();

        LocalMultiControlLogger.Info("INIT_BEGIN");
        LocalMultiControlLogger.Info($"BUILD_ID {BuildMarker}");
        LocalMultiControlLogger.Info($"BUILD_IDENTITY {DescribeBuildIdentity()}");
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");

        // 阶段 1：运行期兼容性（PE/CLR/Assembly/依赖）
        RunStage(fatalFailures, "RUNTIME_COMPAT_CHECK",
            () => RuntimeCompatibilityCheck.Run(fatalFailures));

        // 阶段 2：配置与记录器
        RunStage(fatalFailures, "SERVICE_INIT", () =>
        {
            SafeAction(fatalFailures, FatalCode.Config, "配置重载", () => LocalWakuuAutopilotConfig.Reload("entry-init"));
            SafeAction(fatalFailures, FatalCode.Config, "个人记录器重载", () => LocalPersonalRecorder.Reload("entry-init"));
        });

        // 阶段 3：模型注册（遗物入池 / 本地化 / 可选第三方探测）
        RunStage(fatalFailures, "MODEL_REGISTRATION", () =>
        {
            RegisterWakuuRelicsToPool();
            SafeAction(fatalFailures, FatalCode.Model, "瓦库遗物本地化", () => LocalWakuuRelicLocalization.Initialize());
            // 社区统计（SkadaHelper）为可选第三方依赖：探测失败只打日志，永不阻断
            WakuuSkadaAdapter.Probe();
        });

        // 阶段 4：应用 Harmony 补丁
        RunStage(fatalFailures, "PATCH_APPLY", () =>
        {
            _harmony = new Harmony("sts2.dualroleadventure");
            try
            {
                if (PatchDomainMap.UseGroupedPatchAll)
                {
                    ApplyAllPatchGroups();
                }
                else
                {
                    // 回滚预案（实施方案 2.3）：整体关闭分组容错，回到旧 PatchAll 直跑。
                    _harmony.PatchAll();
                }
            }
            catch (Exception patchException)
            {
                // r38 防御 + 2.3 分组：Core 组失败即停时会走到这里（后续补丁组未应用）。
                // 已应用的补丁保留；缺失会在下方启动自检中报出。
                LocalMultiControlLogger.Error($"Harmony 补丁初始化中断（请结合启动自检缺失清单定位具体补丁）: {patchException}");
                fatalFailures.Add($"[{FatalCode.PatchApply}] Harmony 补丁应用中断: {patchException.GetType().Name}");
            }
        });

        // 阶段 5：补丁自检（Critical 缺失 → 致命）
        RunStage(fatalFailures, "PATCH_SELF_TEST", () => ValidatePatches(fatalFailures));

        // 终态：INIT_OK / INIT_FAILED 二选一，互斥
        FinishInitialization(fatalFailures);
    }

    /// <summary>
    /// 启动自检：期望补丁清单 vs 实际已打补丁。
    /// Critical 缺失 → 致命（PATCH002）；Optional 缺失 → WARN。
    /// </summary>
    private static void ValidatePatches(ICollection<string> fatalFailures)
    {
        try
        {
            List<MethodBase> patchedList = _harmony!.GetPatchedMethods().ToList();
            LocalMultiControlLogger.Info($"Harmony 补丁统计: 已打补丁方法数={patchedList.Count}");

            // 同时构造简单名键与完整名键（含参数个数），清单可写 Type.Method 或 Namespace.Type.Method 或 Type.Method/argc
            var patchedKeys = new HashSet<string>(StringComparer.Ordinal);
            var signatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MethodBase method in patchedList)
            {
                if (method.DeclaringType == null)
                {
                    continue;
                }

                string simple = $"{method.DeclaringType.Name}.{method.Name}";
                string full = $"{method.DeclaringType.FullName}.{method.Name}";
                int argCount = method.GetParameters().Length;
                patchedKeys.Add(simple);
                patchedKeys.Add(full);
                patchedKeys.Add($"{simple}/{argCount}");
                patchedKeys.Add($"{full}/{argCount}");

                // 同名方法被多个重载/同名类型命中 → 提示升级到签名级写法
                string signature = $"{full}/{argCount}";
                signatureCounts[signature] = signatureCounts.TryGetValue(signature, out int count) ? count + 1 : 1;

                if (method.Name.Contains("SelectCards") || method.Name.Contains("FromHand")
                    || method.Name.Contains("FromSimpleGrid") || method.Name.Contains("FromChooseACard")
                    || method.Name.Contains("FromCombatPile") || method.Name.Contains("ShouldSelectLocalCard"))
                {
                    LocalMultiControlLogger.Info($"  已打补丁: {method.DeclaringType.FullName}.{method.Name}/{argCount}");
                }
            }

            foreach (KeyValuePair<string, int> entry in signatureCounts.Where(pair => pair.Value > 1))
            {
                LocalMultiControlLogger.Warn($"启动自检: 同一签名被多次打补丁（第三方 mod 也可能是这里）: {entry.Key} x{entry.Value}");
            }

            List<string> missingCritical = CriticalPatchTargets.Where(target => !patchedKeys.Contains(target)).ToList();
            List<string> missingOptional = OptionalPatchTargets.Where(target => !patchedKeys.Contains(target)).ToList();

            foreach (string target in missingCritical)
            {
                LocalMultiControlLogger.Error($"[{FatalCode.PatchCritical}] Critical 补丁缺失(可能被 PatchAll 静默跳过): {target}");
                fatalFailures.Add($"[{FatalCode.PatchCritical}] Critical 补丁缺失: {target}");
            }

            foreach (string target in missingOptional)
            {
                LocalMultiControlLogger.Warn($"启动自检: Optional 补丁缺失（不影响可用性）: {target}");
            }

            int criticalOk = CriticalPatchTargets.Length - missingCritical.Count;
            int optionalOk = OptionalPatchTargets.Length - missingOptional.Count;
            LocalMultiControlLogger.Info(
                $"PATCH_RESULT critical={criticalOk}/{CriticalPatchTargets.Length} " +
                $"optional={optionalOk}/{OptionalPatchTargets.Length} total_patched={patchedList.Count}");

            if (missingCritical.Count == 0)
            {
                LocalMultiControlLogger.Info($"启动自检: {CriticalPatchTargets.Length} 个 Critical 补丁全部生效。");
            }
            else
            {
                LocalMultiControlLogger.Error(
                    $"启动自检: {missingCritical.Count}/{CriticalPatchTargets.Length} 个 Critical 补丁缺失，" +
                    "请用 Scripts/Tools/patch_coverage.py 重新生成覆盖清单核对。");
            }

            LogKeyPatchOwners(patchedList);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"启动自检执行失败: {exception}");
            fatalFailures.Add($"[{FatalCode.PatchSelfTest}] 补丁自检执行失败: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Harmony owner 审计（backlog：第三方 Patch 冲突检查）。
    ///
    /// 对每个「命中 Critical/Optional 清单的关键方法」打印该方法上的全部补丁 owner 与种类，
    /// 一眼区分「我们的补丁没执行」与「第三方补丁也打在这个方法上」（Koishi 等 mod 冲突场景）。
    /// 仅输出到日志，不影响初始化成败。
    /// </summary>
    private static void LogKeyPatchOwners(List<MethodBase> patchedList)
    {
        try
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string target in CriticalPatchTargets.Concat(OptionalPatchTargets))
            {
                wanted.Add(target);
                // 签名级写法（.../参数个数）同时登记去掉后缀的写法，owner 审计按「方法」匹配
                int slash = target.LastIndexOf('/');
                if (slash > 0)
                {
                    wanted.Add(target.Substring(0, slash));
                }
            }

            int logged = 0;
            foreach (MethodBase method in patchedList)
            {
                if (method.DeclaringType == null)
                {
                    continue;
                }

                string simple = $"{method.DeclaringType.Name}.{method.Name}";
                string full = $"{method.DeclaringType.FullName}.{method.Name}";
                if (!wanted.Contains(simple) && !wanted.Contains(full))
                {
                    continue;
                }

                HarmonyLib.Patches? info = Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    continue;
                }

                var perOwner = new Dictionary<string, (int Prefix, int Postfix, int Transpiler, int Finalizer)>(StringComparer.Ordinal);
                void Count(IEnumerable<HarmonyLib.Patch> patches, string kind)
                {
                    foreach (HarmonyLib.Patch patch in patches)
                    {
                        string owner = string.IsNullOrEmpty(patch.owner) ? "(unknown)" : patch.owner;
                        if (!perOwner.TryGetValue(owner, out (int Prefix, int Postfix, int Transpiler, int Finalizer) tuple))
                        {
                            tuple = (0, 0, 0, 0);
                        }

                        switch (kind)
                        {
                            case "Prefix": tuple.Prefix++; break;
                            case "Postfix": tuple.Postfix++; break;
                            case "Transpiler": tuple.Transpiler++; break;
                            case "Finalizer": tuple.Finalizer++; break;
                        }
                        perOwner[owner] = tuple;
                    }
                }

                Count(info.Prefixes, "Prefix");
                Count(info.Postfixes, "Postfix");
                Count(info.Transpilers, "Transpiler");
                Count(info.Finalizers, "Finalizer");

                foreach (KeyValuePair<string, (int Prefix, int Postfix, int Transpiler, int Finalizer)> owner in perOwner)
                {
                    string counts =
                        $"P{owner.Value.Prefix}Po{owner.Value.Postfix}T{owner.Value.Transpiler}F{owner.Value.Finalizer}";
                    bool isSelf = owner.Key == "sts2.dualroleadventure";
                    if (isSelf)
                    {
                        LocalMultiControlLogger.Info($"  关键目标 {simple} — {owner.Key} [{counts}]");
                    }
                    else
                    {
                        LocalMultiControlLogger.Warn(
                            $"关键目标 {simple} 存在第三方补丁 owner「{owner.Key}」[{counts}]：若行为异常，" +
                            "先确认是不是它的补丁与我们冲突（Harmony 按优先级执行）。");
                    }
                }

                logged++;
            }

            if (logged > 0)
            {
                LocalMultiControlLogger.Info($"Harmony owner 审计完成: 关键目标 {logged} 个均已记录补丁归属。");
            }
        }
        catch (Exception exception)
        {
            // 审计是辅助诊断，绝不允许它反过来拖垮初始化
            LocalMultiControlLogger.Warn($"Harmony owner 审计失败（不影响加载）: {exception.Message}");
        }
    }

    /// <summary>
    /// 初始化终态收口：全绿 → INIT_OK；任一致命项 → INIT_FAILED，并在严格模式下抛异常上报游戏。
    /// 严格模式下抛出的异常会被 ModManager.CallModInitializer 捕获并写入 MOD_ERROR.ASSEMBLY_LOAD，
    /// 这是游戏侧唯一能表达「该 mod 加载失败」的机制（主菜单可见）。
    /// </summary>
    private static void FinishInitialization(List<string> fatalFailures)
    {
        if (fatalFailures.Count == 0)
        {
            LocalMultiControlLogger.Info("INIT_OK");
            LocalMultiControlLogger.Info("Mod 初始化完成（状态=OK）。");
            return;
        }

        LocalMultiControlLogger.Error($"INIT_FAILED fatal={fatalFailures.Count}");
        foreach (string failure in fatalFailures)
        {
            LocalMultiControlLogger.Error(failure);
        }

        LocalMultiControlLogger.Error(
            $"Mod 初始化失败（状态=FAILED）：{fatalFailures.Count} 项致命问题，本 mod 未处于可用状态。");

        if (!StrictMode)
        {
            LocalMultiControlLogger.Warn(
                "严格模式已关闭（LMC_INIT_STRICT=0）：仅记录 INIT_FAILED，未向游戏上报，mod 可能处于半损坏状态。");
            return;
        }

        throw new InvalidOperationException(
            $"LocalMultiControl 初始化失败（{fatalFailures.Count} 项致命问题）: {string.Join(" | ", fatalFailures)}");
    }

    private static void RunStage(ICollection<string> fatalFailures, string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"初始化阶段[{stage}] 抛出未处理异常: {exception}");
            fatalFailures.Add($"[{FatalCode.Stage}] 阶段 {stage} 未处理异常: {exception.GetType().Name}");
        }
    }

    private static void SafeAction(ICollection<string> fatalFailures, string code, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"[{code}] {what}失败: {exception}");
            fatalFailures.Add($"[{code}] {what}失败: {exception.GetType().Name}");
        }
    }

    private static bool ResolveStrictMode()
    {
        string? raw = Environment.GetEnvironmentVariable("LMC_INIT_STRICT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return !raw.Equals("0", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("false", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("off", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 结构化构建身份（BuildIdentity）：构建时由 csproj 生成 BuildIdentity.g.cs 编译进来。
    /// 与 BuildMarker（人工维护、可读）互为补充——这个回答「从哪个 commit、何时构建、工作区是否干净」。
    /// </summary>
    private static string DescribeBuildIdentity()
    {
        return $"commit={BuildIdentity.GitCommit} state={BuildIdentity.GitDirty} built={BuildIdentity.BuildTimeUtc}";
    }

    /// <summary>
    /// 分组应用全部 Harmony 补丁（维护性改进 2.3：PatchAll 分组隔离）。
    ///
    /// 按 <see cref="PatchDomainMap"/> 将补丁类归入 7 个域并逐组 try-catch：
    ///   - Core（本地多控运行基座）失败即停：异常上抛由 Init 兜底记录，不再应用后续补丁组；
    ///   - 其余组失败打 Error 并跳过，继续下一组；
    ///   - 未登记分组的补丁类打 Warn 并按「隔离组」兜底应用（应补登记到 PatchDomainMap）。
    /// </summary>
    private static void ApplyAllPatchGroups()
    {
        Assembly assembly = typeof(Entry).Assembly;
        // 与 Harmony PatchAll 的收集口径一致：不排除 abstract（静态补丁类编译为 abstract+sealed）。
        List<Type> allPatchTypes = assembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<HarmonyPatch>() != null)
            .ToList();

        var grouped = new Dictionary<PatchDomain, List<Type>>();
        var unregistered = new List<Type>();
        foreach (Type patchType in allPatchTypes)
        {
            PatchDomain? domain = PatchDomainMap.ResolveFor(patchType);
            if (domain == null)
            {
                unregistered.Add(patchType);
                continue;
            }

            if (!grouped.TryGetValue(domain.Value, out List<Type>? domainTypes))
            {
                domainTypes = new List<Type>();
                grouped[domain.Value] = domainTypes;
            }

            domainTypes.Add(patchType);
        }

        foreach (PatchDomain domain in PatchDomainMap.ApplyOrder)
        {
            if (grouped.TryGetValue(domain, out List<Type>? domainTypes))
            {
                ApplyPatchGroup(domain, domainTypes, failFast: domain == PatchDomain.Core);
            }
        }

        if (unregistered.Count > 0)
        {
            foreach (Type unregisteredType in unregistered)
            {
                LocalMultiControlLogger.Warn(
                    $"补丁类未登记分组（已按隔离组兜底应用，请登记到 PatchDomainMap）: {unregisteredType.FullName}");
            }

            ApplyPatchGroup(domain: null, unregistered, failFast: false);
        }
    }

    /// <summary>
    /// 应用单个补丁组。组内任一补丁失败时：
    ///   failFast=true（Core）→ 打 Error 并上抛，后续补丁组不再应用（错误基线上继续更危险）；
    ///   failFast=false → 打 Error 并跳过本组，继续其余组。
    /// </summary>
    private static void ApplyPatchGroup(PatchDomain? domain, IReadOnlyList<Type> patchTypes, bool failFast)
    {
        if (patchTypes.Count == 0)
        {
            return;
        }

        string groupName = domain?.ToString() ?? "Unregistered";
        try
        {
            foreach (Type patchType in patchTypes)
            {
                _harmony!.CreateClassProcessor(patchType).Patch();
            }

            LocalMultiControlLogger.Info($"补丁组[{groupName}] 应用完成：{patchTypes.Count} 类");
        }
        catch (Exception exception) when (failFast)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败（本组失败即停，后续补丁组不再应用）: {exception}");
            throw;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败，已跳过该组（其余组继续）: {exception}");
        }
    }

    /// <summary>
    /// 把瓦库托管遗物注册进事件遗物池（与原版低语耳环同池）。
    /// 不入池的遗物在 RelicModel.Pool 里会因 First() 找不到匹配而抛异常，
    /// 导致悬停/点开遗物描述时 UI 中断（表现为"未解锁"且无法退出描述页）。
    /// 事件遗物池没有任何随机奖励入口引用，注册后不会被随机抽到。
    /// </summary>
    private static void RegisterWakuuRelicsToPool()
    {
        try
        {
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuStarterRelic>();
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuFormRelic>();
            LocalMultiControlLogger.Info("已注册瓦库托管遗物到事件遗物池。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注册瓦库遗物到遗物池失败（遗物描述页可能异常）: {exception.Message}");
        }
    }
}
