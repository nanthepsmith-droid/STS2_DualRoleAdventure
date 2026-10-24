# YuWanCardLocalCoopFix

让**猪猪 mod（YuWanCard）的遗物【队友帮付】在本地多控下可用**的独立补丁 mod
（不属于 `DualRoleAdventure` 主 mod，模板同 `HextechRunesLocalCoopFix` / `PreloadStallGuard`）。

## 现象（用户原话）

> 【队友帮付】遗物可以在商店让其它玩家付钱，但是我实际测试是不行的。

点商店里的「队友帮付」按钮 → 弹出「选择队友」→ 选一位队友 → **什么都没发生**
（不扣钱、不买到东西、也没有确认框）。

## 根因

猪猪这条链从头到尾都按**原版联机**写，全走**定向**网络消息
（反编译 `YuWanCard.Content.dll`，`YuWanCard.Patches.TeammatePayShopPatch`）：

```text
点按钮 → ShowTeammateSelection()
       → TeammatePayMessageHandler.QueryAllTeammatesGold()   // 发给每位队友，等 2s 回包
       → TeammatePayMessageHandler.SendRequest(request)      // 定向发给付款者  ← 断在这里
付款者 → HandleRequest → OnPayRequestReceived() → 弹确认框 → DeductGoldLocally()
       → SendResponse(accepted) → 发起者 ProcessPurchase() → entry.OnTryPurchaseWrapper(inv, true)
```

| 环节 | 本地多控下的实际行为 |
|---|---|
| 金币查询 | 主 mod 回环服务是单进程，定向 `SendMessage` **只打日志不派发** ⇒ 2s 超时；但猪猪弹层对缺失项回退 `player.Gold`（本机数据）⇒ 这一步**看着正常** |
| 请求 / 响应 | 同样是定向 `SendMessage`，**没有任何对端会收发** ⇒ 付款者永远收不到请求、永远不弹确认、购买永远不结算 |
| 就算消息通了 | `OnPayRequestReceived` / `DeductGoldLocally` 都用 `LocalContext.GetMe()` 判/扣「本机玩家」，而本地多控下 GetMe 只返回**当前前台**那一位 ⇒ 会判失败/扣错人 |

## 做法

只在本 mod 的**回环会话**（`NetService` 类型名 = `LocalLoopbackHostGameService`）里，
前缀接管猪猪的点击流程 `TeammatePayShopPatch.OnTeammatePayButtonPressed`：

1. 直接读本机其他席位的真实金币，复用猪猪自己的「选择队友」弹层（反射 `Create` + `WaitForSelection`，不重画 UI）；
2. 选定后在本机扣**那位席位**的钱（`PlayerCmd.LoseGold(cost, payer, GoldLossType.Lost)`，与猪猪一致）；
3. 再按猪猪原来的方式结算货物：`entry.OnTryPurchaseWrapper(inventory, ignoreCost: true)`
   （猪猪也是这么调的 —— 钱已在付款者那边扣过）；
4. 货物结算失败 ⇒ 金币退回付款者并记 WARN。

**全程不发任何网络消息。** 真实联机（非回环 NetService）一律 `return true` 交回原版。

不引用 `YuWanCard.dll` / `YuWanCard.Content.dll`：所有类型与方法是反射 + 按名字挂补丁，
猪猪缺席 / 改名的结果只是本补丁**退化为不干预**（`INIT_DEGRADED`），绝不连累启动。

## 验证锚点（日志）

```text
[YuWanCardLocalCoopFix] INIT_OK: patched=[TeammatePayShopPatch.OnTeammatePayButtonPressed], missing=[], source=...
[YuWanCardLocalCoopFix] 队友帮付（本地接管）: requester=…, cost=150, 候选=[…:200G, …]
[YuWanCardLocalCoopFix] 队友帮付已扣款: payer=…, cost=150, 剩余=50G
[YuWanCardLocalCoopFix] 队友帮付完成: payer=…, cost=150, entry=MerchantCardEntry, purchased=True
```

- 取消（不选人）⇒ 只有「玩家取消/超时，未结算」，**不扣钱**；
- 金币不足 ⇒ 「付款者金币不足，放弃」，**不扣钱**；
- 货物结算失败 ⇒ 「货物结算失败…金币退回」+「已退款」。

## 边界（刻意不做）

- **真实联机不接管**（只认回环 NetService）：原版的远端确认/网络扣款语义原样保留。
- 不动【我全都要】（卡牌奖励多选）：那是原版 `CardReward` 自身的多选钩子，与本地多控的自动抓牌
  不冲突（自动抓牌在同一 `OnSelect` 循环里逐张取，取空即结束），单独观察。
