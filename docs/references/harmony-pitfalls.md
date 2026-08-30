> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# Harmony 补丁约定（本 mod 特有的两个大坑）

## 坑 1：`PatchAll` 只应用「类级带 `[HarmonyPatch]`」的类型

`Entry.Init` 的 `_harmony.PatchAll()` 处理的是**类声明上**带 `[HarmonyPatch]` 的类型；
`[HarmonyPatch]` 只写在方法上、类上没有类级属性时，整个类被**静默跳过**，无任何报错。

后果：补丁永不触发，日志上什么都看不到（不是「不生效」而是「根本没打上」）。

- 单目标类：类级 `[HarmonyPatch(typeof(X), nameof(X.M))]`，方法只留 `[HarmonyPrefix]`/`[HarmonyPostfix]`。

```csharp
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.SelectCards))]
internal static class NPlayerHandSelectCardsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(...) { ... }
}
```

- 多目标类（同一个文件里多个 `[HarmonyPatch(目标)]` 方法）：类上必须放一个**裸 `[HarmonyPatch]`** 标记，
  让 PatchAll 处理本类；方法级 `[HarmonyPatch(target)]` 仍给出各自目标。

```csharp
[HarmonyPatch]                                   // 关键：裸标记
internal static class MultiTargetPatch
{
    [HarmonyPatch(typeof(A), nameof(A.M1))]
    [HarmonyPrefix]
    private static void PrefixA(...) { }

    [HarmonyPatch(typeof(B), nameof(B.M2))]
    [HarmonyPostfix]
    private static void PostfixB(...) { }
}
```

- 排查法：看启动日志 `Harmony 补丁统计` 清单里有没有目标方法名（Entry.cs 只会列出
  SelectCards / FromHand / FromSimpleGrid / FromChooseACard / FromCombatPile / ShouldSelectLocalCard
  这几类，其它目标要自己加日志确认）；或 grep 历史日志里某个特征日志（如 `弹出背包`）出现次数，
  **一次都没有 = 补丁从没应用过**。

## 坑 2：拦截原实现不要用 `ref bool __runOriginal`

拦截（跳过原方法、替换返回值）用本代码库验证过的模式：

```csharp
[HarmonyPriority(Priority.High)]
[HarmonyPrefix]
private static bool Prefix(..., ref Task<T> __result)
{
    if (guards) return true;          // 放行原逻辑
    __result = MyWrapperAsync(...);   // 替换结果
    return false;                     // 跳过原逻辑
}
```

`ref bool __runOriginal` 在本游戏内置的 Harmony **2.4.2.0** 上对某些方法（如
`NPlayerHand.SelectCards`）会生成 `InvalidProgramException` → PatchAll 整体抛异常 → **整个 mod
初始化崩溃**（比不生效更糟）。全线补丁都别用 `__runOriginal`。

## 其它约定

- guards 惯例：`LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode`
  且 `RunManager.Instance.NetService is LocalLoopbackHostGameService`。
- 补丁命名：`PrefixXxx` / `PostfixXxx`，每个游戏类型/场景一个文件，放 `Scripts\Patch\`。
- 线程注意：`AsyncLocal` 沿异步链流动、天然区分交错调用；静态标志跨链共享会互相污染
  （r2 失败根因之一）。
- 改返回值/跳过原逻辑的补丁要**幂等**，重复触发不能破坏状态。
- 热路径补丁要轻量（不要每帧做重反射或分配）。

## 遗留（未启用，需用户决定）

`CardSelectManualConfirmationPatch`、`NEndTurnButtonLifecyclePatch` 两个作者遗留补丁也是
「仅方法级 `[HarmonyPatch]`」→ 从未生效。启用会改变 UX（如牌组选牌强制手动确认），先问用户。

> 注：`NEndTurnButtonLifecyclePatch` 与 `CardSelectManualConfirmationPatch` 已于 **2026-08-29（r28）**
> 补上类级裸 `[HarmonyPatch]` 并确认被 PatchAll 正常处理（覆盖清单工具实证，WARN-METHOD-ONLY 清零）。
> `NEndTurnButton.OnTurnStarted` / `NCombatUi.Activate` 探针实机确认会触发；`SetState` 是否触发待观察——
> 见 `local-multicontrol-pitfalls.md` 坑 A（2026-08-29 修正）。
