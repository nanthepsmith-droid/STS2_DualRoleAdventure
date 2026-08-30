# 历史记录筛选与导出 Mod 可行性分析

## 概述

分析两个小型 mod 的可行性：
1. **历史记录筛选 mod** - 筛选历史记录（胜负、多人/单人、角色、遗物、卡牌等）
2. **历史记录导出到 Loadout mod** - 将历史记录中的卡组和遗物导出到 Loadout mod 的配置

---

## 1. 历史记录筛选 Mod

### 1.1 数据结构分析

**历史记录存储位置**：
```
%APPDATA%/SlayTheSpire2/steam/{userId}/profile{id}/saves/history/{timestamp}.run
```

**RunHistory 数据结构** (`RunHistory.cs:11-60`)：
```json
{
  "win": true/false,
  "game_mode": "standard"/"custom"/"daily",
  "was_abandoned": true/false,
  "players": [
    {
      "id": 76561198422527326,
      "character": "CHARACTER.IRONCLAD",
      "deck": [
        { "id": "CARD.STRIKE_RED", "floor_added_to_deck": 1 }
      ],
      "relics": [
        { "id": "RELIC.BURNING_BLOOD", "floor_added_to_deck": 1 }
      ],
      "potions": [...]
    }
  ],
  "ascension": 0,
  "killed_by_encounter": "ENCOUNTER.XXX",
  "start_time": 1787889404,
  "run_time": 72
}
```

**可筛选字段**：
| 字段 | 路径 | 筛选类型 |
|------|------|----------|
| 胜负 | `win` | 布尔 |
| 弃局 | `was_abandoned` | 布尔 |
| 游戏模式 | `game_mode` | 枚举 |
| 角色 | `players[].character` | 枚举 |
| 遗物 | `players[].relics[].id` | 字符串匹配 |
| 卡牌 | `players[].deck[].id` | 字符串匹配 |
| 升阶 | `ascension` | 数值范围 |
| 死因 | `killed_by_encounter` | 字符串匹配 |

### 1.2 实现方案

**方案 A：拦截原版 UI（推荐）**

通过 Harmony 补丁拦截 `NRunHistory` 类，注入筛选逻辑：

```
NRunHistory.OnSubmenuOpened()
    ↓ (postfix)
拦截 _runNames 列表，根据筛选条件过滤
    ↓
只显示符合条件的历史记录
```

**关键补丁点**：
- `NRunHistory.OnSubmenuOpened` - 拦截加载历史记录列表
- `NRunHistory.RefreshAndSelectRun` - 拦截加载单条记录

**优点**：
- 复用原版 UI，无需新建界面
- 实现简单，只需过滤逻辑
- 与原版兼容性好

**缺点**：
- 筛选条件 UI 需要额外添加（可复用原版按钮样式）

**方案 B：独立筛选界面**

新建一个筛选面板，显示筛选结果列表。

**优点**：
- 可以显示更多信息（如胜率统计）
- 界面更灵活

**缺点**：
- 工作量大
- 需要处理大量 UI 细节

### 1.3 可行性结论

**可行性：高**

- 数据结构清晰，字段完整
- 原版 UI 有明确的拦截点
- 筛选逻辑简单（主要是列表过滤）
- 预估工作量：2-3 天

---

## 2. 历史记录导出到 Loadout Mod

> **2026-08-29 更新**：确认 Loadout 2 有两种"卡组"相关功能，需要区分：
> 1. **自定义开局预设（Custom Runs）** - 存 `custom_runs.json`，创建新的自定义对局
> 2. **卡组+遗物预设（Deck Loadout）** - 存 `profile_loadouts.json`，**战斗中点开卡组就能应用**
>
> 用户目标更贴近**方案 2（卡组+遗物预设）**——把历史记录的一整套卡组/遗物存成预设，随时在战斗中一键应用。此方案数据结构更简单，下面重点分析。

### 2.0 卡组+遗物预设（Loadout Deck Preset）—— 首选方案

**功能入口**：`NDeckLoadoutPanel`，战斗/开菜单点开卡组界面看到的下拉选择器。

**存储文件**（反编译确认的路径常量）：
```
ProfilePath = "loadout/services/loadouts/profile_loadouts.json"
```
实际落到所属 profile 目录：
`%APPDATA%/SlayTheSpire2/steam/{userId}/modded/profile{id}/loadout/services/loadouts/profile_loadouts.json`

**反编译确认的数据结构**：
```csharp
LoadoutProfileSaveData          // 根
{
  schemaVersion: 1,
  loadouts: List<SavedLoadout>
}

SavedLoadout                    // 一条预设
{
  id: string,                    // Guid
  name: string,                  // 预设名
  kind: LoadoutKind,             // Cards(0) / Relics(1) / CardsAndRelics(2)
  specialPreset: LoadoutSpecialPreset,  // None(0) / StartingDeck(1)
  createdAt: long,
  updatedAt: long,
  cards: List<SavedCardLoadoutEntry>,   // 卡组
  relics: List<SavedRelicLoadoutEntry>   // 遗物
}

SavedCardLoadoutEntry { modelId: string, upgradeLevel: int, count: int, state: CardModificationSpec? }
SavedRelicLoadoutEntry { modelId: string, count: int, state: RelicModificationState? }
```

**关键 API（LoadoutStorageService，静态）**：
- `GetLoadouts()` → 读取所有预设
- `Upsert(SavedLoadout)` → 新增/更新预设
- `Import(SavedLoadout)` → 导入新预设（自动生成新 id）

**对比 Custom Runs，此处更合适的原因**：
- 数据模型就是"卡组 + 遗物 + 药水"，与历史记录高度一致
- 用户目标"点开卡组就能应用"，正是 `EnableDeckLoadoutScreen` 的功能
- 无需处理 Custom Run 的 roles/rules/variables/modifiers 等复杂规则系统
- 写入结构更简单（直接 append 一条 `SavedLoadout` 到 `loadouts` 数组）

### 2.1 Loadout Mod 分析（Custom Runs 备选）

**Loadout Mod 信息**：
- 作者：Jasonwqq
- 版本：v0.4.10
- 依赖：BaseLib 3.3.5+
- Workshop ID：3756859747

**配置文件位置**：
```
%APPDATA%/SlayTheSpire2/mod_configs/Loadout.cfg
%APPDATA%/SlayTheSpire2/loadout/services/custom_runs/custom_runs.json
```

**Loadout.cfg 格式**：
```json
{
  "EnableDeckLoadoutScreen": "True",
  "EnableCreatureManipulationPanel": "True",
  "EnableCustomRuns": "True",
  "PanelSkin": "Default",
  "PanelAnimation": "YellowGlowPulse",
  "Companion": "none",
  "CardCustomizationScope": "Global"
}
```

**custom_runs.json 完整格式**（已通过反编译 Loadout.dll 确认）：
```json
{
  "schemaVersion": 4,
  "definitions": [
    {
      "schemaVersion": 4,
      "id": "<32位hex GUID>",
      "name": "New Custom Run",
      "description": "",
      "createdAt": 1787889404,
      "updatedAt": 1787889404,
      "setup": {
        "character": { "kind": "Character", "selection": {...} },
        "startingLoadoutMode": "PerCharacter",
        "characterStartingLoadouts": [
          {
            "characterModelId": "CHARACTER.IRONCLAD",
            "startingDeck": { "kind": "Card", ... },
            "startingCardEntries": [
              { "modelId": "CARD.STRIKE", "upgradeLevel": 0, "count": 1, "state": null }
            ],
            "startingRelics": { "kind": "Relic", ... },
            "startingRelicEntries": [
              { "modelId": "RELIC.BURNING_BLOOD", "count": 1, "state": null }
            ],
            "startingPotions": { "kind": "Potion", ... }
          }
        ],
        "startingAscension": null,
        "potionSlots": null,
        "startingGold": null,
        "runSeed": null
      },
      "roleAssignmentMode": "PlayersChoose",
      "defaultRoleName": "Default Role",
      "roles": [],
      "playerChoices": [],
      "variables": [],
      "rules": [],
      "requiredModIds": []
    }
  ]
}
```

**关键字段反编译确认的类**：
```
CustomRunSaveData → schemaVersion + definitions: List<CustomRunDefinition>
CustomRunDefinition → id/name/description/createdAt/updatedAt/setup/roles/...
RunSetupDefinition → character/startingLoadoutMode/characterStartingLoadouts/...
CharacterStartingLoadoutDefinition → characterModelId/startingDeck/startingCardEntries/startingRelics/startingRelicEntries/startingPotions/...
SavedCardLoadoutEntry → modelId/upgradeLevel/count/state
SavedRelicLoadoutEntry → modelId/count/state
SelectionSpec → kind(Character/Card/Relic/Potion)/selection
```

### 2.2 数据转换分析

**历史记录数据** → **Loadout 格式**（映射关系已完全确认）：

| 历史记录字段 | Loadout 目标字段 |
|--------------|------------------|
| `players[].character` | `setup.character.characterModelId` |
| `players[].deck[].id` | `setup.characterStartingLoadouts[0].startingDeck` → `startingCardEntries[].modelId` |
| `players[].relics[].id` | `startingRelicEntries[].modelId` |
| `players[].potions[].id` | `startingPotions` |
| `ascension` | `setup.startingAscension` |
| `seed` | `setup.runSeed` |

**需要转换的数据**：
1. **卡组**：`players[].deck[]` 的 `id` → `startingCardEntries[].modelId`
2. **遗物**：`players[].relics[]` 的 `id` → `startingRelicEntries[].modelId`
3. **药水**：`players[].potions[]` 的 `id` → `startingPotions`
4. **角色**：`players[].character` → `characterModelId`

**潜在问题**：
1. **卡牌修饰符（CardModifier）** - 历史记录中有 `save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]`（卡牌上的 BaseLib 修饰符），而 Loadout 的 `state` 使用自己的 `CardModificationSpec` 格式，需要格式转换或丢弃
2. **遗物属性（props）** - 历史记录中部分遗物有 `props`（如 Yui 的充能次数），Loadout 的 `state` 用 `RelicModificationState`，需要确认兼容性
3. **Mod 卡牌** - 某些卡牌/遗物属于第三方 mod，`requiredModIds` 需要填充对应 mod 的 id

### 2.3 实现方案

**方案 A：直接写入配置文件（推荐）**

```
用户在历史记录界面点击"导出到 Loadout"
    ↓
读取当前历史记录的卡组/遗物/药水
    ↓
转换为 Loadout 的 custom_runs.json 格式
    ↓
写入到 %APPDATA%/SlayTheSpire2/loadout/services/custom_runs/custom_runs.json
    ↓
提示用户重启游戏生效
```

**优点**：
- 实现简单
- 不需要依赖 Loadout mod 的 API
- 配置文件格式已完全确认（通过反编译）

**缺点**：
- 需要手工维护 JSON 与 Loadout mod 的兼容性
- 如果 Loadout 更新 schema 版本可能失效

**方案 B：反射调用 Loadout Mod API（推荐进阶）**

Loadout.dll 已有 `CustomRunSerializationService` / `CustomRunSnapshotSerializationService` 等类，可尝试通过反射调用其持久化/加载接口，避免手工拼写 JSON。

**优点**：
- 更稳定，重用 Loadout 自身序列化逻辑
- 与 Loadout 内部格式始终一致

**缺点**：
- 需要反射定位 Loadout 的内部类，脆弱
- Loadout 更新可能改方法签名

### 2.4 关键挑战（已解决/剩余）

1. **✅ Loadout 配置格式**（已通过 ilspycmd 反编译确认）
   - `CustomRunSaveData`/`CustomRunDefinition`/`RunSetupDefinition`/`CharacterStartingLoadoutDefinition`/`SavedCardLoadoutEntry`/`SavedRelicLoadoutEntry` 全部确认
   - 字段名（JsonPropertyName）与类型完全掌握

2. **卡牌修饰符兼容性**
   - 历史记录的 `save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]`（BaseLib 修饰符）
   - Loadout 的 `state` 用 `CardModificationSpec`（Loadout 自己的格式）
   - 需要转换或丢弃修饰符（首版建议只迁移 `modelId`/`upgradeLevel`/`count`，丢弃 `state`）

3. **Mod 卡牌兼容性**
   - 历史记录中含第三方 mod 卡牌/遗物（如 `CARD.STS2_WINE_FOX_CARD_REGROUP`）
   - Loadout 通过 `requiredModIds` 声明依赖
   - 需要从卡牌 id 前缀推断所属 mod，填入 `requiredModIds`

4. **多人局如何导出**
   - 多人局 `players` 有多个角色，每个角色卡组/遗物不同
   - `roles` + 每个 role 的 `setup` 可对应不同玩家的 builds
   - 或者只导出本地玩家（`PlatformUtil.GetLocalPlayerId`）

### 2.5 可行性结论

**可行性：高**（方案 2：卡组+遗物预设）

- **配置格式已完全反编译确认**，存储路径明确（`profile_loadouts.json`）
- **数据模型与历史记录天然对齐**：`SavedLoadout.cards/relics` ↔ 历史 `players[].deck/relics`
- 写入方式简单：把历史记录卡组/遗物包成一个 `SavedLoadout` append 到 `loadouts` 数组即可
- 剩余工作：修饰符转换（或丢弃）、mod 卡牌识别、多人局多角色映射、写文件时保证不损坏已有预设（用 `LoadoutSerializationService.Normalize` 归一化或只读改写 JSON）
- 预估工作量：**2-3 天**

### 2.6 更新后的实施建议

**改为推荐方案 2（卡组+遗物预设）**，放下 Custom Runs 方案。

用户在历史记录界面点"保存为 Loadout 预设"：

```
历史记录界面 → 选中一条记录 → "保存为卡组+遗物预设"
    ↓
取当前记录的 players[].deck / relics（多人局选本地玩家）
    ↓
构造 SavedLoadout { name, kind=CardsAndRelics, cards=[...], relics=[...] }
    ↓
写入 profile_loadouts.json 的 loadouts 数组
    ↓
点开卡组界面的 Loadout 下拉即可看到并应用
```

---

## 3. 建议的实现顺序

### 第一阶段：历史记录筛选 Mod（优先）

1. 实现基础筛选功能（胜负、角色）
2. 添加高级筛选（多人/单人、遗物、卡牌）
3. 优化 UI 体验

### 第二阶段：历史记录导出 Mod（改为卡组+遗物预设方案 2）

1. ✅ 已反编译确认 `SavedLoadout` / `LoadoutProfileSaveData` 结构和 `profile_loadouts.json` 路径
2. 实现历史记录卡组/遗物 → `SavedLoadout` 转换
3. 在历史记录界面添加"保存为 Loadout 预设"按钮
4. 测试（确保不损坏已有预设、多人局取本地玩家）

---

## 4. 技术细节

### 4.1 历史记录筛选 Mod

**补丁点**：
```csharp
// NRunHistory.cs:181-187
public override void OnSubmenuOpened()
{
    _runNames.Clear();
    _runNames.AddRange(SaveManager.Instance.GetAllRunHistoryNames());
    _runNames.Reverse();
    // 在这里添加筛选逻辑
    TaskHelper.RunSafely(RefreshAndSelectRun(0));
}
```

**筛选逻辑伪代码**：
```csharp
// 在 OnSubmenuOpened 中过滤 _runNames
public override void OnSubmenuOpened()
{
    _runNames.Clear();
    var allNames = SaveManager.Instance.GetAllRunHistoryNames();
    
    foreach (var name in allNames)
    {
        var result = SaveManager.Instance.LoadRunHistory(name);
        if (result.Success && MatchesFilter(result.SaveData))
        {
            _runNames.Add(name);
        }
    }
    
    _runNames.Reverse();
    TaskHelper.RunSafely(RefreshAndSelectRun(0));
}
```

### 4.2 历史记录导出 Mod（方案 2：卡组+遗物预设）

**数据转换伪代码**（用反编译确认的 `SavedLoadout`，写 `profile_loadouts.json`）：
```csharp
public static SavedLoadout ConvertToLoadoutPreset(RunHistory history)
{
    // 取本地玩家（多人局匹配 PlatformUtil.GetLocalPlayerId）
    var player = history.Players.FirstOrDefault(p => p.Id == localPlayerId) ?? history.Players.First();

    return new SavedLoadout
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = $"History_{history.StartTime}",
        Kind = LoadoutKind.CardsAndRelics,   // 同时保存卡组+遗物
        SpecialPreset = LoadoutSpecialPreset.None,
        CreatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Cards = player.Deck.Select(c => new SavedCardLoadoutEntry
        {
            ModelId = c.Id,
            UpgradeLevel = 0,     // 历史记录无升级信息，默认0
            Count = 1,            // 历史记录每条是一个实体，默认1
            ModificationState = null   // 首版丢弃 BaseLib CardModifier
        }).ToList(),
        Relics = player.Relics.Select(r => new SavedRelicLoadoutEntry
        {
            ModelId = r.Id,
            Count = 1,
            ModificationState = null
        }).ToList()
    };
}

// 写入 profile_loadouts.json（loadouts 数组 append 一条）
// 路径: %APPDATA%/SlayTheSpire2/steam/{userId}/modded/profile{id}/loadout/services/loadouts/profile_loadouts.json
// 读取已有 JSON → Loadouts.Add(新预设) → 写回
```

---

## 5. 风险评估

| 风险项 | 影响 | 缓解措施 |
|--------|------|----------|
| Loadout 配置格式变化 | 中 | 监控 Loadout 更新，及时适配 |
| 第三方 mod 卡牌兼容性 | 低 | 只导出原版卡牌，跳过 mod 卡牌 |
| 历史记录数据损坏 | 低 | 添加数据校验逻辑 |
| 性能问题（大量历史记录） | 低 | 添加缓存机制 |

---

## 6. 总结

| Mod | 可行性 | 预估工时 | 优先级 |
|-----|--------|----------|--------|
| 历史记录筛选 | 高 | 2-3 天 | 高 |
| 历史记录导出为卡组+遗物预设 | **高**（格式已确认，路径 `profile_loadouts.json`） | 2-3 天 | 中 |

**建议**：两个功能都值得做。导出功能采用**卡组+遗物预设**方案（写 `profile_loadouts.json`）而非自定义开局预设，因为前者数据结构与历史记录天然一致，写入更简单，且正好匹配"点开卡组就能应用"的使用场景。
