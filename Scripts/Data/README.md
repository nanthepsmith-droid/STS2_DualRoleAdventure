# Scripts/Data —— 随 mod 分发的内置数据

目前只有一个文件：

## `hellwar_presets.json` —— 战灵召唤的**内置预设**

- **它是什么**：一份**和玩家自己那份同格式**的预设表（`%APPDATA%\SlayTheSpire2\hellwar_presets.json`）。
  这份会被**编译进 dll**（`EmbeddedResource`，见 `LocalMultiControl.csproj`），随工坊包一起走
  ⇒ 玩家装了 mod 就有这些预设，不用自己放文件。
- **怎么用**：把「战神构筑器」（`D:\Download\pain\WarGodForge\`）导出的 `hellwar_presets.json` 内容贴进来，
  再重新构建即可。**格式说明与字段语义**见 `decision-records`/`TODO.md` §预设构筑器 与
  `Scripts/Runtime/PureLogic/HellwarPresetData.cs` 的注释。
- **⚠ 不要写注释**：mod 侧的 JSON 解析**不跳过注释**，也不允许尾逗号 —— 写注释会让整份文件解析失败
  （日志 `内置预设资源解析失败`，然后只用玩家那份）。

### 与玩家文件的关系

| | 来源 | 优先级 |
|---|---|---|
| 玩家那份 | `%APPDATA%\SlayTheSpire2\hellwar_presets.json` | **高**（同 `id` 时整条覆盖内置那条） |
| 这份内置 | dll 里的嵌入资源 | 低 |

合并规则 = 玩家在前 + 内置在后，按 `id` 去重（详见 `Scripts/Runtime/PureLogic/HellwarPresetMerge.cs`）。
所以玩家可以"用自己的同名预设顶掉官方那套"，而不会看到重复条目。

### 引用了第三方 mod 内容怎么办

内置预设**可以**用第三方 mod 的角色 / 卡 / 遗物（编辑器里挑得到）。
**没装那些 mod 的玩家**拿到时会**跳过这条预设**（不进候选、不进随机池、不当默认），
并打一条 `[地狱战神] 预设已跳过（引用了本机没有的内容…）` 的 WARN，其余预设照常 ——
见 `Scripts/Runtime/HellwarPresetCompat.cs` 与 `PureLogic/HellwarPresetAvailability.cs`。

### 现状（2026-10-15 起）

这份**不再**是空模板 —— 里面是**定稿的 18 套预设**（用「战神构筑器」搓好、由 `--selftest` 校验过：

- 档位分布：**罕见 5 / 稀有 5 / 先古 8**（普通档不挂文件预设：那一档的候选由 **全部可玩角色** 自动组成，见 `HellwarPresetCatalog.CharacterPresets`）；
- 其中 **2 套**（`watchera15` / `common-watcher-watcher`）用 `WATCHER-*` 内容 = **Watcher mod** 的角色与卡 ⇒ 没装那个 mod 的玩家会被**整条跳过**（其余预设照常）；
- **没有任何一套写 `default: true`** ⇒ 各档「自动」回落成**该档第一条文件预设**（罕见 `ironclad1` / 稀有 `ironclad2` / 先古 `watchera15`）。
  想让某套当"自动默认"，在编辑器里勾上它的「默认」再覆盖本文件即可；
- 本机（玩家那份与这份**同 id**）合并后仍是 18 套 ⇒ **本地行为零变化**（`预设表已加载: 套数=18（玩家 18 条 / 内置 18 条）`）；
  想验内置通道真的通，把 `%APPDATA%\SlayTheSpire2\hellwar_presets.json` 临时改名再启动，看 `内置预设已读取: 条数=18`。

**怎么更新**：编辑器里改 → 「保存预设」/「导出」 → **覆盖本文件** → 重新构建（嵌入资源要重新编译才生效）。
`LocalMultiControl.Tests` 有一条测试会读这份数据做归一（有注释 / 尾逗号 / 写坏字段会直接 FAIL）。
