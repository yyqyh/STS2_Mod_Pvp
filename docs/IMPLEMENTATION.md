# PVP 决斗 · 实现方案

> 适用于 `pvp_duel` 0.2.0 ｜ 最后整理：2026-09-26
> 配套文档：[代码架构](ARCHITECTURE.md)

这份文档回答「每个机制是怎么落地的、为什么这么做、踩过什么坑」。文件级职责见 ARCHITECTURE.md 第 2 节。

## 1. 进战斗

**做法**：事件选项的回调里调

```csharp
EnterCombatWithoutExitingEvent(ModelDb.Encounter<CorpseSlugsWeak>(), [], shouldResumeAfterCombat: false);
```

那个遭遇只是**占位**：它的怪会在 `CombatState.AddCreature` 处被全部拦下，实际打的是两人轮流行动。

**为什么不用 `CanonicalEncounter`**：覆写它会让本体走「事件战斗」那条更早的初始化路径，
实测那条路上事件还没绑好 `Owner`，`EventOption` 构造时会在 `CharacterModel.AddDetailsTo` 里 NRE。

**为什么补录像快照**（`DuelMode.EnsureReplaySnapshot`）：本体只在「走地图点」和「按房间类型直接进房」两条路上
调 `CombatReplayWriter.RecordInitialState`，事件里开打不记。快照为空时 `CombatReplayWriter` 的每个钩子都会抛
`RecordInitialState must be called first`，回合开始的第一个 checksum 正好踩中它 —— 表现是**战斗卡住不动**。

## 2. 敌我关系

决斗里**两人都留在我方侧**，对手只是「看起来在对面」。早先试过把 `Players[1].Creature.Side` 真改成敌方，
代价是同时踩中本体五处「怪物专属」路径（`TakeTurn` / `AfterAddedToRoom` / `RollMove` / `PrepareForNextTurn` /
`PerformIntent`）并且要接管整个敌方回合。所以改成只改「看法」，一共三个地方：

1. **自动效果的口径**（`DuelRules.OpponentsOf` + `TargetPatches.DuelOpponentsOfPatch`）：
   本体的 `GetOpponentsOf` / `GetTeammatesOf` 按 `Creature.Side` 分侧，决斗里「对面」是空的、「队友」里混进了对手。
   重算成「另一位玩家 + 他名下的召唤物」。
   **雷球就是踩这条**：`LightningOrb.ApplyLightningDamage` 从 `GetOpponentsOf` 取候选，取到空表直接 `return`，一点伤害都不打。
   同一入口还有 AOE（`AttackCommand`）、毒、若干遗物自动效果。这条只看「谁问的」，不看本机视角 → 两端一致。
2. **规则层的合法目标**（`DuelRules.OpponentTargetOverride` + `TargetPatches` 里的卡牌/药水两个补丁）：
   `CardModel.IsValidTarget` 与 `PotionModel.IsValidTarget` 都看 `Creature.Side`，不改会出现「对面被判成不合法」。
   `AnyEnemy` 放行、`AnyAlly` 否掉（1v1 里对面不是队友，否则「给友方上 buff」会变成给对方上 buff）。
   **药水是另一套校验**，必须一起改，否则 UI 让点、点完用不出去。
3. **选中 UI**（`DuelTargetManagerOpponentPatch`）：`NTargetManager.AllowedToTargetCreature` 也写死在阵营上，
   不放行的话鼠标悬停上去立刻 `return`，`HoveredNode` 保持 null，松手只会取消 —— 表现就是**点不到人**。

另外 `HittableEnemies` 是**本机视角**的一份（`DuelHittableOpponentPatch`）：它同时被规则层（攻击牌、随机目标）
和 UI 层（拖牌时的可选目标）读，所以按本机玩家重算并把自己剔掉。目标选择本来就是本地 UI 行为，不涉及跨端一致性。

站位上，立绘的 `Hitbox` 原本可能被关成 `MouseFilter.Ignore`，这里会补回 `Stop`，否则同样点不到。

## 3. 轮流行动

**做法**：借本体的「额外回合」机制。每个玩家回合开始时，把这一回合的参与者名单
（`CombatTurnState.PlayersTakingExtraTurn`，通过 `DuelTurnState` 反射读写）**限定成当前行动者**。

**为什么不是「跳过另一个人的回合开场」**：那条只挡住 `SetupPlayerTurn`（抽牌/回能），
挡不住 `Creature.AfterTurnStart` —— 那才是清格挡的地方，它遍历的是这一侧这一回合的参与者，
于是两人每个回合开头都被清一次格挡。

现在每个回合都由本体按「额外回合」的规格跑一遍：只有参与者跑 `AfterTurnStart` 与 `SetupPlayerTurn`，
没参与的玩家被本体自动置成「已结束回合」，回合结束的钩子也只对参与者跑。**对手的手牌会留到他自己回合。**

本体的额外回合（佩尔之眼那类效果）通过名单非空来识别：不换行动者、不加人，但按设置里的「连续额外回合上限」卡一刀
（超了就把名单清空，落到正常轮转）。

## 4. 回合数账

本体在「进入我方侧」时对**所有**玩家 `IncrementTurnNumber`，因为本体每个回合大家都动。
决斗里每回合只有一个人动，照旧全体 +1 的话第二位玩家的首次回合会变成「第 2 回合」，
`SetupPlayerTurn` 里「`TurnNumber == 1` 时把固有牌（Innate）挪到牌堆顶」那段就永远不跑。

所以 `DuelTurnNumberPatch` 只让**当前行动者**自增（本体的额外回合照旧 +1）。
后手补偿也据此判定：自己的第一次回合 = `TurnsByPlayer[actor] == 1`，不读本体的 `TurnNumber`。

## 5. 胜负与结算

- **拦怪**：`CombatState.AddCreature` 是怪物进战斗的唯一入口，在这里拦下比「先放进来再摘掉」干净。
  但**玩家自己的宠物 / 召唤物要放行**（`PetOwner != null`）—— 它们随后走 `CombatManager.AddCreature`，
  而那个方法要求 `CombatState` 里已经有这个 creature，被拦掉会在 `BeforeCombatStart` 抛
  `InvalidOperationException`，整个回合循环死掉（表现：战斗卡死）。实例：缚魂瓶开战召唤奥斯提。
- **不让战斗「敌人全灭」结束**：本体 `IsCombatEnding` 的判据是「敌方侧还有活着的 PrimaryEnemy」，
  决斗一开打敌方侧就是空的 —— 不压住这个结果，开局就会弹结算。压胜负判定很重，条件必须带「正好两人」。
- **倒下即结束**：本体的失败判据是「所有玩家都死了」，而对手还活着，这个条件永远不成立，战斗会卡住。
  所以在 `CreatureCmd.Kill` 后判断「倒下的那位是不是本机那位」；`force`（主动放弃本局）走原本流程，不算分出胜负。
- **结算**：决斗一局定胜负，直接 `RunManager.OnEnded` + 结束画面（`DuelSession.Finish`，`IsFinished` 防重复）。
- **回合上限**：每个回合开头问一次（`StartTurn` 是最稳的心跳，与快捷键、开关无关）；
  打满 `MaxRounds` 就按**剩余血量百分比**判定，血多的一方赢、打平算我方失败。
  用百分比而不是绝对值，是因为两人可以选不同角色、最大生命可能差很多。

## 6. 站位与宠物

- P1 固定左、P2 固定右（`±480`，正好是本体摆单个敌人的槽位），右侧立绘镜像 `Scale.X`（判定用的 `Hitbox` 不受影响）。
- 右侧那位会 reparent 到敌方容器：两个容器位置相同，所以这只影响**绘制层级**（敌方容器画在后面 = 更靠前）。
- 摆位时机：**只在 P2 进场时**摆一次（那时两个人的节点都已建好），并且延后一帧，等本体统一摆完再覆盖。
- 宠物是**后来**才建节点的（缚魂瓶在 `BeforeCombatStart` 才召唤奥斯提），所以它们各自进场时再摆一次；
  主人的一侧由 `DuelRules.IsRightSidePlayer` 决定，宠物跟着镜像。
- 奥斯提的位置来自本体 `NCreature.GetOstyOffsetFromPlayer`，写死朝右；右侧玩家的宠物要镜像到左边，
  这里打 `Postfix` 改 `ref Vector2 __result` 而不是只在摆位时改 —— 本体还有别的路径会按那个偏移重摆
  （例如缩放动画结束后），从 `Postfix` 改才能每条路径都一致。

## 7. 额外幕

**思路**：做成追加的一幕，而不是替换结局事件。`EnterNextAct` 看到 `CurrentActIndex < Acts.Count - 1` 就自然走 `EnterAct`，
于是三幕打完不再是结局，而是进入这一幕。

| 环节 | 做法 | 关键点 |
|---|---|---|
| 追加章节 | 挂 `RunState.CreateForNewRun` 的 Postfix 反射改 `Acts`（setter 是 private） | 调用方紧接着走 `RunManager.GenerateRooms`（`for (i in 0..Acts.Count)` 全覆盖），所以额外幕的房间会被正常生成，不需要自己调 |
| 美术 | `ContentAssetProfiles.FromVanillaActId("glory")` | 借本体第三幕的图，本 mod 不出任何关卡图 |
| 不参与随机 | `Index => -1` | 负数 = 永不进原版随机章节表，只由我们手动追加 |
| 地图 | `Act.DuelActCreateMapPatch`（`ActModel.CreateMap` 不是 virtual，只能换返回值） | 见下 |
| 房间 | `Act.DuelActCreateRoomPatch` 用 **Prefix 直接换结果** | 不跑本体那段建房逻辑，就不依赖幕里的先古 / 遭遇 / boss 池有没有东西（否则会在 `PullAncient` 抛 `RoomSet.Ancient not set!`） |
| 没有先古之民 | `AllAncients` / `GetUnlockedAncients` 给空 + 起始节点标 `Unknown` | `Rng.NextItem` 对空集返回 null 是安全的；起始节点标 `Unknown` 而不是 `Ancient`，万一哪天绕过了 `CreateRoom` 那层替换也不会走去 `PullAncient` |

### 7.1 只有初始节点和 BOSS 节点的地图（`DuelActMap`）

- **起始节点绝不能放进网格**：`NMapScreen.SetMap` 会先在网格循环里画一遍边、再另外拿起始节点画一遍
  （`DrawPaths(_startingPointNode, map.StartingMapPoint)`），放进网格就会把同一条边加两次，
  `_paths.Add` 当场抛 `An item with the same key has already been added`，**整幕进不去**。本体也是把起始点摆在网格外的 row 0。
- **网格里一行房间都不放**：地图上就只剩「初始 + BOSS」两个点。
- **行数只用来调地图长度**：本体尺寸写死 —— 起始节点 `y = 720`、BOSS 固定 `y = -1980`、每行间距 `_distY = 2325 / (行数 - 1)`。
  行数太少反而把地图拉长（2 行时 `_distY = 2325`）。取 14 行（本体一幕的行数）时 `_distY ≈ 179`，
  滚动距离 ≈ 2500px，正好是本体一幕的高度，也就是**不改 UI 的前提下的下限**（再短就得改 `NMapScreen` 里写死的坐标，属于表现层 hack）。
  下限 2 行是公式本身的要求（1 行会除以 0）。
- BOSS 的行号给 1（就摆在初始节点上面一格），再配合 `ActMapUiPatch` 把它的坐标挪到初始节点上方、
  把旧连线摘掉按新坐标重画（`_paths` 里的旧箭头必须先 `Remove`，否则同样重键报错）。
- `BaseNumberOfRooms = 7` 是给「会把地图整个重造的遗物」（SpoilsMap / BigGameHunter）的兜底：
  那些地图里有 `GetRowCount() - 7` 这类行号运算，行数不足 7 会越界。

### 7.2 双 boss 还给第三幕

本体的双 boss（进阶 10+）发给 `Acts.Count - 1` 那一幕，追加决斗幕后那就是决斗幕 —— 于是第三幕的双 boss 消失、
双 boss 反而落在一个只有 BOSS 节点的幕上。`ActDoubleBossPatch` 在房间生成后纠正：
决斗幕不要第二个 BOSS，双 boss 补给「本来最后一幕」。
挑 BOSS 用独立 `Rng(seed, "pvp_duel_last_real_act_second_boss")`，两端结果一致，也不扰动本体的 RNG 流。

### 7.3 两个兜底

- **成就名**：`ActModel.DefeatedAllEnemiesAchievement` 是 `Enum.Parse<Achievement>("Defeat" + Id + "Enemies")`，
  自定义幕的 id 不在本体的枚举里 → 谁读谁抛。今天那条链是空实现（潜在雷），这里兜底成第三幕的成就。
- **多人血量缩放**：`MultiplayerScalingModel.GetMultiplayerScaling` 是 `switch (actIndex) { 0/1/2 …; default: throw }`，
  而 `CombatState.CreateCreature` 建怪时就会问它 —— 决斗幕是第 4 幕（`actIndex = 3`），
  于是「接受对决」那一刻直接抛 `Invalid act index for HP scaling`。这一场本来没有怪物，
  缩取哪一档都无所谓，把越界的幕号折回最后一档即可。

## 8. 入口

### 8.1 正式入口：事件

`DuelInvitationEvent` 是 `IsShared = true`（决斗必须两个人的事一致），挂在额外幕的事件池里。
选择「接受对决」后：解绑 together（可关）→ `DuelSession.Arm()` → 进战斗。
事件挂进池子之后任何一局都可能抽到，所以开头先问「入口模块开 + 正好两人」，不满足就地收掉事件页。

文案的资源先借本体的图（`res://images/events/battleworn_dummy.png`）：RitsuLib 判存在性走 `ResourceLoader.Exists`，
mod 包里的 png 在那个索引里查不到。

### 8.2 调试入口：本局第一个问号房

本体决定「问号房变成什么」只有一处（`RunManager.RollRoomTypeFor` 里 `MapPointType.Unknown => …Roll(...)`），
改它的返回值就能 100% 控制。两段配合：

1. `RollRoomTypeFor` 的 Postfix：本局第一个问号 → 把结果改成 `RoomType.Event`，并在 `DebugEntryPlan` 里记一个 pending 标记；
2. `CreateRoom` 的 Postfix：这次 Event 是我们那个 → 换成 `new EventRoom(ModelDb.Event<DuelInvitationEvent>())`。

标记跟着 `IRunState` 走（换一局自动重置），只生效一次。
`EventRoom` 要的是 canonical 模型，给 Mutable 副本会被 `AssertCanonical` 拦下。

### 8.3 事件选项异常兜底

`EventOption` 构造时会调 `AddLocVars`：`eventModel.Owner?.Character.AddDetailsTo(Description)` 在我们的路径上会 NRE。
关键是 `Description` 在传参时已求值（文案已就绪），后面那句只是往里面塞一个 `{IsMultiplayer}` 变量，我们的文案不用它，
所以用 `HarmonyFinalizer` 只吞**我方事件**的这个异常，其它事件、其它异常一律原样抛回。

## 9. 平衡规则

「无限」有两条路：**回合内无限**（连击 / 循环）与**回合数无限**（额外回合被原样放行）。
前者用「出牌数上限 + 同卡上限」堵，后者用「连续额外回合上限」堵；数值全部来自设置，**0 = 不限制**。

| 规则 | 挂点 | 实现 |
|---|---|---|
| 单回合出牌上限 | `Hook.AfterCardPlayed` | 数到上限就 `RequestEnqueue(new EndPlayerTurnAction(...))`（和玩家点「结束回合」同一个入口，两端一致）。本体成就也是在这个钩子上数「单回合打了几张」 |
| 同卡上限 | `CardModel.CanPlay(out …, out …)` | 打够次数后 `__result = false`。**只补带 out 的那个重载就够**（无参的那个只是转调它） |
| 连击同卡计数 | `DuelBalance` | 按**牌实例**计数（同名卡的不同副本各自计数），只在正常回合清零 |
| 连续额外回合 | `CombatManager.StartTurn` | 本体的额外回合连着给同一个人的次数超上限就不再给（清空参与者名单，落到正常轮转） |
| 后手补偿 | `Hook.AfterPlayerTurnStart` | 这时本体的回合开场已经跑完（能量已重置、起手已抽），补能量不会被覆盖、补格挡不会被回合开始的清格挡吃掉 |
| 回合上限 | `CombatManager.StartTurn` | 见第 5 节 |

**后手补偿的判定**只看两件事：这位玩家自己的第一次回合 + 他不是 `Players[0]`。
先手方 = `Players[0]`（大厅顺序，两端一致），所以两端算出来一定一样。
补偿内容：额外能量 `PlayerCombatState.GainEnergy`、开局格挡 `CreatureCmd.GainBlock(..., (ValueProp)0, null)`
（0 = 既不吃格挡修正、也不算「被能力加成」，就是白给一坨格挡）、额外抽牌 `CardPileCmd.Draw(..., fromHandDraw: true)`。

**计数只在正常回合清零**：额外回合不清，否则可以靠刷额外回合把出牌上限洗掉。

## 10. 限制小抄（HUD）

`DuelHud` 在战斗界面**右上角**显示五行：回合数 / 双方本回合出牌用量 / 同卡用量 / 双方血量百分比与差 / 后手补偿状态。
实现是一个 0.25 秒的 `Timer` + 一个 `Label`，位置每帧重算（窗口尺寸变了也跟着走）。
**纯本机表现，不参与任何判定**，所以两端可以不一样。位置想挪就改文件顶部那两个常量。

## 11. 对外 API（`PvpDuel.Api.DuelApi`）

别的 mod **不需要编译期引用**：用 RitsuLib 的
`[ModInterop("pvp_duel", "PvpDuel.Api.DuelApi")]` 写一个存根、成员签名逐字照抄即可。
本类刻意只用简单类型（`bool` / `int` / `string` 与游戏自带的 `Player`、`CardModel`）。

三类用法：

```csharp
// ① 只改数值（执行还是我们来做）
DuelApi.SetLimits(maxCardsPerTurn: 10, maxSameCardPerTurn: 2, extraTurnLimit: 1); // 每项传 -1 = 该项不改
DuelApi.SetCompensation(energy: 1, block: 5, cards: 0);
DuelApi.SetRoundLimit(20);

// ② 整体取消我们的规则（数值与查询照旧可用，方便调用方自己实现）
DuelApi.BuiltInRulesEnabled = false;

// ③ 读用量做 UI
var used = DuelApi.CardsPlayedThisTurn(player);
var same = DuelApi.SameCardPlaysThisTurn(card);
var turn = DuelApi.TurnsTaken(player);
var line = DuelApi.Describe();

// 交回控制权给设置页
DuelApi.ClearOverrides();
```

语义：

- 覆盖值 **`null` = 用设置页里的**；`0` = 不限制（回合上限除外，它最小是 1）。
- 设置页那些数值是给玩家调参用的，这里是给 mod 用的，两者最终都汇到 `DuelConfig` 这一份「生效值」，并由它统一做上下限夹取。
- `BuiltInRulesEnabled` 的读值是**外部自己的视角**（没设过就是 `true`），不是"当前是否真的在执行" ——
  真正是否执行还取决于玩家的「平衡」模块开关，用 `DuelApi.MaxCardsPerTurn()` 之类的读值看生效值即可。

## 12. 录像快照与 checksum 约定

- **不要自己调 `ChecksumTracker.GenerateChecksum`**：打点次数两端必须完全一致，多打一次会让后面所有 id 错位。
- 要打点走本体 / together 的既有链路（`Checkpoint()` / `[sync] chk=`）。
- 事件里进战斗必须补 `CombatReplayWriter.RecordInitialState`（见第 1 节），否则回合链第一个 checksum 就死。

## 13. 构建、部署、打包

```powershell
# 编译 + 自动部署 DLL（C# 改动只需要这一条）
dotnet build .\pvp_duel.csproj -c Debug -p:STS2_SKIP_PCK_EXPORT=1

# 改了 JSON / 图片 / 场景：重出 PCK
godot --headless --path . --export-pack BasicExport <游戏目录>\mods\pvp_duel\pvp_duel.pck
```

- `Sts2Dir` / `GodotExe` 在 `local.props` 里配（没配就走 `Sts2PathDiscovery.props` 自动探测）。
- 构建结束会自动把 DLL 与清单拷到 `$(Sts2Dir)\mods\pvp_duel\`；**PCK 不含 DLL**。
- `RunPckExport` 默认 false：Godot 导出时自己也会跑一遍 `dotnet publish`，默认打开会变成「Godot 套 Godot」重复打包同一个 PCK。
- **版本号闸门**（`pvp_duel.csproj` 的 `CheckVersionConsistency`）：编译前比对清单 `version` 与 `Core/ModInfo.cs` 的 `Version`，
  不一致直接报错（清单才是游戏真正读的那份，两处必须一致；临时跳过：`-p:SkipVersionCheck=1`）。
- **依赖**：清单里只写 `STS2-RitsuLib`，**故意不写 together** —— 工坊版 together 还停在 0.2.0，
  写 `min_version: 0.3.0` 会让订阅者加载期直接失败；`TogetherInterop` 是 `ModInterop` 存根，
  目标 mod 没装时只是不转发（存根返回默认值，不抛异常）。

## 14. 踩坑清单

| 现象 | 根因 | 处理 |
|---|---|---|
| 补丁静默不生效，日志里没有报错 | `[HarmonyPatch(typeof(X), nameof(X.Method))]` 遇到**重载**会 Ambiguous match；out 形参的 byref 类型写不进特性实参（C# 只允许常量 / `typeof` / 数组） | 用 `TargetMethod()` + `MakeByRefType()` 在运行时解析（见 `BalancePatches.cs`） |
| 地图一进去就 `An item with the same key has already been added` | 起始节点被放进网格，`NMapScreen.SetMap` 会画两遍同一条边 | 起始节点留在网格外（本体也是这么摆的） |
| 换房间时抛 `RoomSet.Ancient not set!` | 本体取房间内容时就会抛，Postfix 来不及 | 用 **Prefix 直接换返回值**，根本不跑本体那段逻辑 |
| 事件里开打后战斗卡住不动 | 录像快照为空，`CombatReplayWriter` 第一个钩子就抛 | `DuelMode.EnsureReplaySnapshot` 补 `RecordInitialState` |
| 战斗刚开始就弹结算 | 敌方侧是空的，`IsCombatEnding` 判成敌人全灭 | `DuelKeepCombatAlivePatch` 压住这个结果（条件必须带「正好两人」） |
| 战斗卡死、log 里 `turn loop died … stuck` | 玩家的召唤物被拦在 `AddCreature` 外，`BeforeCombatStart` 抛 `InvalidOperationException` | 拦怪只拦「不属于本场的怪」，`PetOwner != null` 要放行 |
| 点不到对手、悬停没反应 | `IsValidTarget` 与 `NTargetManager.AllowedToTargetCreature` 都按阵营判断；立绘 `Hitbox` 被设成 `Ignore` | 三个闸门一起放开（见第 2 节） |
| 雷球/AOE 打不到人 | `GetOpponentsOf` 按 `Side` 分侧，决斗里敌方侧是空的 | 重算「对手 = 另一位玩家 + 他的召唤物」 |
| 每回合开头格挡被清两次 | 只挡 `SetupPlayerTurn` 挡不住 `Creature.AfterTurnStart` | 用本体「额外回合」的参与者名单，本回合只留行动者 |
| 固有牌（Innate）永远不生效 | 本体对所有玩家 `IncrementTurnNumber`，多出来的那位首次回合变成第 2 回合 | 只给当前行动者自增 |
| 第 4 幕建战斗时抛 `Invalid act index for HP scaling` | `GetMultiplayerScaling` 的 `switch` 只有 0/1/2 | 越界的幕号折回最后一档 |
| 读某个自定义幕的成就会抛枚举解析异常 | `"Defeat" + Id + "Enemies"` 拼出来的名字不在枚举里 | 兜底成第三幕的成就 |
| 读档 / 重连后决斗标记丢了 | `Armed` 是内存态 | `InDuel = Armed \|\| CombatRoom.ParentEventId == 事件 id`（那个字段跟存档走） |

## 15. 已知限制

- 只在**正好两名玩家**的局里生效。
- 两人都留在我方侧，所以按 `Creature.Side` 分侧的第三方 mod 逻辑在决斗里可能要自己适配。
- **「p1 进问号、p2 进小怪」这类各走各的做不到**：本体联机的地图界面只是投票
  （`MapSelectionSynchronizer` 每人一票，主机在票里**随机取一个**），`RunState.MapLocation` / 当前房间 / 房间栈 /
  地图历史都只有一份；而「两边各开一局」也不行 —— 回主菜单时 `RunManager.CleanUp()` 末尾就是
  `NetService.Disconnect(Quit)`，且各跑各的局会让每个动作的 checksum 分叉（主机 `StateDivergence` 踢人），
  相遇后更不可能合流（`RunState.Players` 开局由 lobby 定死，战斗必须同一个 `CombatState`）。
  想做出「各走各的」只能改成**轮流领路**之类的替代玩法（每节点只认当前领路人的票），目前未实现。
