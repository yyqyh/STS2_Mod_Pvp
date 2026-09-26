# PVP 决斗 · 代码架构

> 适用于 `pvp_duel` 0.4.0 ｜ 最后整理：2026-09-26
> 配套文档：[实现方案](IMPLEMENTATION.md) ｜ [分离模式（C2）设计](SPLIT_MODE.md)

## 1. 一句话概览

联机的两名玩家互相开打：没有怪物，一人一回合轮流行动，谁先倒下谁输；手牌对对手隐藏。
入口是第三幕之后追加的一幕里的「对决邀请」事件（另有调试入口：本局第一个问号房直接进决斗）。

## 2. 目录结构

```
Main.cs                          装配：初始化设置 + 扫描应用全部 Harmony 补丁（无业务逻辑）
pvp_duel.json                    清单（id / version / 依赖）
pvp_duel.csproj                  构建 + 版本号闸门 + 自动部署
localization/ pvp_duel/          本地化与图片资源

Core/
  ModInfo.cs                     身份与日志出口（ModId / Version / Info / Warn / Error / LogOnce）
  ModuleSwitches.cs              模块级开关总表（所有模块的唯一入口判断）
  Settings/                      基础模块：设置（持久化 + 设置页 UI）
    PvpSettings.cs               持久化字段与默认值
    PvpSettingsStore.cs          RitsuLib 数据存储注册与读取
    PvpModSettingsPage.cs        设置界面（主开关 / 模块开关 / 平衡数值）
  Interop/                       基础模块：跨 mod 存根
    TogetherInterop.cs           together 的 ModInterop 存根（Unbind）
  Diagnostics/                   基础模块：诊断
    StartCombatDiagPatch.cs      把 StartCombatInternal 吞掉的异常完整打出来

  Duel/                          【模块】决斗本体（开关：Core / Balance）
    DuelConfig.cs                生效参数（设置页 ⊕ 外部覆盖）的唯一来源
    Rules/DuelRules.cs           纯判定：能不能决斗、谁是敌人、谁是行动者、超时怎么算
    Internals/DuelTurnState.cs   反射读本体 internal 的 CombatTurnState
    Runtime/DuelSession.cs       一场决斗的状态与生命周期（标记、轮流、结算）
    Runtime/DuelMode.cs          战斗改造：清怪 + 补录像快照
    Runtime/DuelBalance.cs       平衡账本：出牌数、同卡数、额外回合数、后手补偿
    Hud/DuelHud.cs               右上角「限制小抄」（纯本机表现）
    Patches/CombatPatches.cs     战斗状态级：进战斗、拦怪、判负、不结束战斗、可选敌人
    Patches/LayoutPatches.cs     表现级：站位 / 朝向 / 宠物 / 奥斯提偏移
    Patches/TargetPatches.cs     敌我关系：GetOpponentsOf / GetTeammatesOf / 卡牌 / 药水 / 选中 UI
    Patches/TurnPatches.cs       轮流行动与回合数
    Patches/BalancePatches.cs    出牌上限 / 同卡上限 / 后手补偿
    Patches/BannerPatches.cs     回合横幅文案

  Act/                           【模块】额外幕（开关：ExtraAct）
    PvpDuelAct.cs                第 4 幕的 ActModel（美术借第三幕，无先古之民）
    DuelActMap.cs                这一幕的地图：只有初始节点 + BOSS 节点
    Patches/ActPatches.cs        追加幕 / 换地图 / 换房间 / 双 boss 归还 / 成就兜底 / 血量缩放夹取
    Patches/ActMapUiPatch.cs     BOSS 节点在地图上收拢等表现

  Entry/                         【模块】入口（开关：Invitation / DebugEntry）
    DuelInvitationEvent.cs       「对决邀请」事件（进决斗的唯一正式入口）
    Patches/EntryPatches.cs      调试入口（第一个问号房）/ 事件房替换 / 事件选项异常兜底

  SplitMode/                     【模块】分离模式（开关：SplitMode / SplitKeepAlive / SplitStatusHud）
    SplitStage.cs                阶段枚举与状态（SoloRun / Meeting / DuelRun / AfterRun）
    Link/SplitLink.cs            保活链路：捕获联机传输 / 拦退出时的断开 / 每帧泵 / 状态统计
    Link/SplitLinkPump.cs        常驻 Node：离开对局后接着泵那条传输
    Link/SplitLinkPatches.cs     三个挂点（捕获、拦断窗口、断连守卫）
    Presence/SplitPresenceChannel.cs  Presence 通道：消息类型 + Sidecar 收发
    Presence/PresenceLedger.cs        坐标台账（本机 + 对端）与相遇判定
    Presence/PresencePublisher.cs     采样与节流（250ms 一发坐标）
    Hud/SplitStatusHud.cs        左上角状态行（纯本机观测）
    Hud/MapPeerMarker.cs         地图上给对端贴标记（◆ 对端 83% / ⚔ 相遇）

  Api/
    DuelApi.cs                   对外接口：改限制数值、接管或取消我们的规则、读用量
```

> 每张 `.cs` 旁边有个 `.uid`：那是 Godot 给脚本资源记的稳定 id，跟着文件一起搬，别单独删。

## 3. 分层与依赖方向

自上而下单向依赖，下层不认识上层：

| 层 | 在哪 | 职责 | 可以依赖 |
|---|---|---|---|
| 装配层 | `Main.cs` | 初始化设置、扫出带 `[HarmonyPatch]` 的类型逐类应用 | 全部 |
| 对外层 | `Core/Api/` | 别的 mod 的入口（不参与本 mod 自身逻辑） | 模块层 |
| 适配层 | 各模块的 `Patches/`、`Duel/Hud/` | 只判断「本体走到这一步该通知谁」，转发给模块 | 模块层、基础层 |
| 模块层 | `Core/Duel/`、`Core/Act/`、`Core/Entry/` | 业务与状态 | 基础层 |
| 基础层 | `Core/Settings/`、`Core/Interop/`、`Core/Diagnostics/`、`ModInfo`、`ModuleSwitches` | 设置、跨 mod 存根、日志、开关 | 只依赖游戏 API |

几条硬规矩：

1. **适配层不放业务逻辑**：补丁体里只允许「判断 + 转发」，超过两行的算法都要落到模块层（`DuelRules` / `DuelSession` / `DuelBalance` …）。
2. **基础层不认识模块**：只有 `ModuleSwitches` 例外 —— 它读设置、给模块用，本身不含任何模块逻辑。
3. **模块之间**：`Act` 与 `Entry` 互相认识 —— 这是「事件属于幕」的直接结果（幕的事件池里挂着那个事件，事件又用 `[RegisterActEvent(typeof(PvpDuelAct))]` 声明自己属于哪一幕），这个环只有这两个类型，是刻意保留的；`Duel` 只从 `Entry` 取一个事件 id（见下）。
4. **命名空间跟着模块走**：`PvpDuel`（入口）/ `PvpDuel.Core`（基础设施）/ `PvpDuel.Core.Duel`、`.Act`、`.Entry`、`.Settings`、`.Interop`、`.Diagnostics` / `PvpDuel.Api`。模块内部的子目录（`Rules/`、`Runtime/`、`Patches/` …）只做文件分组，不另开命名空间。

## 4. 模块表（模块 = 目录 + 一个开关 + 它的补丁与设置项）

| 模块 | 目录 | 开关（`PvpSettings` → `ModuleSwitches`） | 关掉会发生什么 | 两端必须一致 |
|---|---|---|---|---|
| 决斗本体 | `Core/Duel/` | `Enabled` → `Core` | 全部退化成原版战斗 | ✅ |
| 平衡规则 | `Core/Duel/`（Balance 相关） | `ModuleBalance` → `Balance`（还受 `DuelApi.BuiltInRulesEnabled` 影响） | 不卡出牌/同卡/额外回合，不发后手补偿 | ✅ |
| 入口事件 | `Core/Entry/` | `ModuleInvitation` → `Invitation` | 没有任何入口 | ✅ |
| 额外幕 | `Core/Act/` | `ModuleExtraAct` → `ExtraAct`（依赖 `Invitation`） | 不追加第 4 幕 | ✅ |
| 调试入口 | `Core/Entry/` | `ModuleDebugEntry` → `DebugEntry`（依赖 `Invitation`） | 第一个问号房照常随机 | ✅ |
| 互操作（解绑 together） | `Core/Interop/` | `ModuleTogetherUnbind` → `TogetherUnbind` | 不解绑共享，两人仍共享卡组 | ✅ |
| 回合横幅 | `Core/Duel/Patches/BannerPatches.cs` | `ModuleBanners` → `Banners` | 文案回到本体的「额外回合 / 敌方回合」 | ❌ 纯本机 |
| 限制小抄 | `Core/Duel/Hud/DuelHud.cs` | `ModuleLimitHud` → `LimitHud` | 不显示右上角那几行 | ❌ 纯本机 |
| 诊断 | `Core/Diagnostics/` | `ModuleDiagnostics` → `Diagnostics` | 不打过程日志与完整异常栈 | ❌ 纯本机 |
| 分离模式（C2，实验） | `Core/SplitMode/` | `ModuleSplitMode` → `SplitMode`；子开关 `SplitKeepAlive`（保活）/ `SplitPresence`（坐标同步）/ `SplitStatusHud`（状态行） | 完全不碰联机链路（默认就是关的） | ✅ 开的时候 |

**为什么分「两端必须一致」**：带 ✅ 的开关会改变对局内容（章节列表、房间、共享状态、每回合谁动），
两端不一致会在某个 checksum 点上当场分叉（主机 `StateDivergence` 踢人）。纯本机的开关只影响自己屏幕上的东西。

诊断模块是唯一**不受主开关约束**的模块：主开关关掉时也要能看见「为什么不生效」。

## 5. 运行时生命周期

### 5.1 开局 → 进决斗

```
新一局 ─ RunState.CreateForNewRun
          └ Act.DuelActPlan.TryAppend      正好两人 + 额外幕开 → 把 PvpDuelAct 追加到 Acts 末尾
        RunManager.GenerateRooms           本体的 for(0..Acts.Count) 会连新幕一起生成房间
          └ Act.DuelActDoubleBossPatch     把双 boss 从决斗幕还给「本来最后一幕」

第三幕打完 ─ EnterNextAct（CurrentActIndex < Acts.Count-1）→ 自然进入 PvpDuelAct
        Act.DuelActCreateMapPatch          换用 DuelActMap（只有初始 + BOSS）
        Act.DuelActCreateRoomPatch         这一幕的每个房间都换成「对决邀请」

点「接受对决」─ Entry.DuelInvitationEvent.BeginDuel
        ModuleSwitches.TogetherUnbind → Interop.TogetherInterop.Unbind()
        DuelSession.Arm()
        EnterCombatWithoutExitingEvent(占位遭遇, [], shouldResumeAfterCombat:false)
```

### 5.2 一场决斗里

```
CombatState.AddPlayer            → DuelMode.EnsureApplied → DuelSession.Begin（消费标记、清账）
                                   → 清怪（拦在 AddCreature）+ 补录像快照
CombatManager.StartTurn          → DuelSession.TryFinishByRoundLimit（回合上限心跳）
                                   → DuelSession.BeginActorTurn + DuelBalance.BeginTurn（本回合只留行动者）
本回合只有行动者：抽牌 / 回能 / 清格挡都只对他跑；对手手牌留到他自己回合
打牌                             → DuelBalance.NoteCardPlayed（到上限就自动结束回合）
动作 / 抽牌 / 目标判定           → Patches/TargetPatches（对手 = 另一位玩家 + 他的召唤物）
有人倒下                         → DuelSession.Finish → RunManager.OnEnded + 结束画面
```

## 6. 跨端一致性（写代码前先问这一句）

决斗跑在本体联机上面，任何**两端都会执行到的判定**都必须由「两端看得到、算法相同」的输入算出来：

| 必须一致的 | 用的口径 |
|---|---|
| 谁是先手 / 谁是右边那位 | 一律 `Players[0]` / `Players[1]`（大厅顺序），**不用本机视角** |
| 本回合谁行动 | `DuelSession.BeginActorTurn` 的轮转（`_actorIndex % Players.Count`） |
| 后手补偿给谁 | 他自己的第一次回合 + 不是 `Players[0]` |
| 额外幕追不追加 | `players.Count == 2` + 两端都开额外幕 |
| 后手补偿的数值 | `DuelConfig`（两端设置页应当一致） |

反过来，**纯本机**的可以按本机来算：拖牌时的可选目标（`HittableEnemies` 那份）、立绘位置与朝向、横幅文案、HUD。

## 7. 扩展点

- **调数值**：设置页「平衡」一节；0 = 关掉那条规则。
- **加一条平衡规则**：`PvpSettings` 加字段（给默认值）→ `DuelConfig` 加只读属性（带夹取）→ 需要对外就加 `DuelApi` 的覆盖方法与读方法 → 账本落在 `DuelBalance` → 补丁挂在 `Duel/Patches/BalancePatches.cs`，入口先问 `ModuleSwitches.Balance`。
- **加一个模块**：新建目录 + `ModuleSwitches` 加开关 + `PvpSettings` 加字段 + 设置页加一行；补丁体的第一句永远是问开关。
- **别的 mod 接管规则**：`DuelApi.BuiltInRulesEnabled = false`，数值与查询照旧可用（见 IMPLEMENTATION.md 第 10 节）。

## 8. 已知限制

- 只在**正好两名玩家**的局里生效；单人 / 三人局下本 mod 什么都不做。
- 进场后两人都留在我方侧（对手只是「看起来在对面」），所以任何按 `Creature.Side` 分侧的**第三方 mod 逻辑**在决斗里都可能需要自己适配。
- 「p1 进问号、p2 进小怪」这类各走各的在原版联机模型下做不到（地图坐标、房间栈、战斗都是全局唯一一份），详见 `IMPLEMENTATION.md` 第 13 节。
