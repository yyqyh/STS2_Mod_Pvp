# 分离模式（C2）· 设计与实施

> 适用 `pvp_duel` 0.3.0+ ｜ 最后整理：2026-09-26
> 姊妹文档：[代码架构](ARCHITECTURE.md) ｜ [实现方案](IMPLEMENTATION.md)

## 0. 这是什么

目标玩法：**两人各自跑自己的单机局**（同种子 → 地图一致），地图上只同步对方的**位置图标**；
走到同一个节点（"相遇"）时，两边合流进**一局临时联机局**打 PVP；打完输家结束、赢家载回自己的局继续。

这就是 C2：**分离期不联机，相遇时才建联机会话**。它绕开了"同一条联机会话里跑两局"那条死路——
本体一局只有一个 `CurrentMapCoord`、一个房间栈、一套 run 级同步器（`RunManager.InitializeShared` 里建了 15 个），
两端状态不一致会被 `ChecksumTracker` 判分叉直接踢人（`ChecksumTracker.cs:151`）。

### 三条铁律

1. 分离期**不存在联机局**——两人各跑各的单机局（`RunManager.SetUpNewSingleplayer`）。
2. 跨机通道只跑**坐标 + 信令**，游戏状态一律不走它。
3. 相遇时建立的是**一局新局**（只装 PVP 内容），不是"把两局合并"。

## 1. 分层

| 层 | 名字 | 职责 | 关键类型 |
|---|---|---|---|
| **L0** | 基础层 | 连接保活与消息原语、存档读写、设置开关、日志 | `SplitLink`、`SplitLinkPump`、`SplitLinkPatches` |
| **L1** | 分离层 | 本机单机局生命周期 + 位置采集 + 地图图标 + 相遇判定 | `SoloStage`、`PeerPresence`、`MapIconOverlay`、`MeetDetector` |
| **L2** | 相遇层 | 握手、冻结、取快照、合成 PVP 存档、切会话、失败回滚 | `MeetingStage`、`DuelSaveBuilder`、`SessionSwitch` |
| **L3** | 决斗层 | 载入即决斗：战斗改造 / 轮流行动 / 敌我视角 / 平衡 / 结算 | **复用现有 `Core/Duel/**`，一行不改** |
| **L4** | 赛后层 | 结果广播、输家结束、赢家载回自己的快照、回到 L1 | `AfterStage`、`RunRestore` |

依赖方向单向：**L4 → L3 → L2 → L1 → L0**；L3 不认识 L1/L2/L4。

## 2. 状态机

```
Off ──(开启分离模式)──▶ SoloRun（各自单机局）
                          │  坐标变化 → 广播 Presence
                          │  判定：同幕 + 同坐标 + 双方空闲
                          ▼
                       Meeting（握手 / 取快照 / 合成 DuelSave）
                          │  本地退局 → 主机 LoadRunLobby → 客户端 load-join
                          ▼
                       DuelRun（临时联机局，只有 PVP 幕）
                          │  分出胜负
                          ▼
                       AfterRun（输家结束 / 赢家载回自己的快照）
                          └──────────▶ SoloRun
```

失败路径：任何一步超时 / 失败 → 各自 `SetUpSavedSingleplayer(自己的快照)` 回滚 → 回到 `SoloRun`，并给相遇加冷却。

## 3. 消息契约（L0 的通道之上）

| 通道 | 消息 | 内容 | 可靠性 |
|---|---|---|---|
| Presence | `PosMsg` | `actIndex`、`coord`、活动状态（地图/房间/战斗/已结束）、血量%、seq | 可丢（下一次覆盖） |
| Presence | `PingMsg` | 心跳、在线状态、mod 清单哈希 | 可丢 |
| Control | `MeetPropose` / `MeetAccept` / `MeetReject` | 幕、坐标、快照哈希、reason | 可靠有序 |
| Control | `SnapshotMsg` | 客户端自己的 `SerializablePlayer` | 可靠（可能多包） |
| Control | `DuelPlanMsg` | P0/P1 的 netId、PVP 幕 id | 可靠 |
| Control | `DuelResultMsg` | 谁赢、是否要恢复 | 可靠 |
| Control | `AbortMsg` | 中断原因（对方离线、载入失败） | 可靠 |

## 4. 本轮已实现（最小地基）

`Core/SplitMode/`（模块开关：`ModuleSwitches.SplitMode` / `SplitKeepAlive` / `SplitStatusHud`）

| 文件 | 作用 |
|---|---|
| `SplitStage.cs` | `SplitStage` 枚举 + `SplitRuntime`（阶段状态与日志） |
| `Link/SplitLink.cs` | 保活链路：捕获联机传输、拦"退出对局"那一次断开、每帧泵、状态与统计 |
| `Link/SplitLinkPump.cs` | 常驻 `Node`（挂场景树根）：离开对局后本体不再泵传输，由它接着泵 |
| `Link/SplitLinkPatches.cs` | ① `RunManager.InitializeShared` 捕获传输 ② `RunManager.CleanUp` 开/关拦断窗口 ③ 客户端/主机的 `Disconnect` 守卫 |
| `Hud/SplitStatusHud.cs` | 左上角状态行（阶段 / 链路 / netId / 对端），纯本机观测 |
| `Presence/SplitPresenceChannel.cs` | Presence 通道：消息类型 + RitsuLib Sidecar 收发（客户端→主机；主机→广播） |
| `Presence/PresenceLedger.cs` | 坐标台账：本机位置 + 每个对端的最近位置/状态/时间戳、相遇判定 |
| `Presence/PresencePublisher.cs` | 采样与节流（250ms 一发）：读本机 `MapLocation` + 活动状态 + 血量% |
| `Hud/MapPeerMarker.cs` | 地图标记：按坐标找到 `NMapPoint`，把 `◆ 对端 83%` / `⚔ 相遇` 挂成它的子节点 |
| `Solo/SplitSoloRun.cs` | 分离期入口：进联机局 6 秒后 `CleanUp()`（拦断连）+ `NGame.StartNewSingleplayerRun` 起自己的单机局（同种子） |
| `Solo/SplitSoloRun.cs` | 分离期入口：**在大厅按下「开始」时拦下** `NGame.StartNewMultiplayerRun`，改成各自 `StartNewSingleplayerRun`（同种子），并先把大厅传输抓进 `SplitLink` |
| `Solo/SplitSoloPatches.cs` | 那个拦截的 Harmony 前缀（返回 `false` 跳过本体的联机局；调用方随后自己 `CleanUpLobby(disconnectSession:false)`，连接不断） |
| `Meeting/SplitSnapshotChannel.cs` | 相遇快照的通道：把本机 `SerializablePlayer` 的 JSON 发给对端 |
| `Meeting/SplitMeeting.cs` | 相遇逻辑：同节点时各发一次快照；收到对端快照后打日志（合流合成下一步做） |
| `Meeting/DuelSaveBuilder.cs` | 合流第二步：主机把两张快照合成「决斗局存档」（PVP 幕 + 两个 player，netId 写成真实 id） |

关键实现点：

- **为什么必须自己泵**：联机传输平时靠别人每帧 `Update()` —— 大厅期间是角色选择界面（`NCharacterSelectScreen.cs:412`），
  对局期间是 `NRun`（`NRun.cs:98`）。离开对局后没人泵，Steam P2P 会静默超时。
- **泵就是心跳**：本体的 `NetQualityTracker` 挂在**连接**上（`NetClientGameService` / `NetHostGameService`
  构造时注册 heartbeat 收发），只要有人 `Update()`，它每 200ms 发一次 `HeartbeatRequestMessage`、
  对面自动回响应 → 天然的双向流量。**不需要自己造心跳包**。
- **别信 `IsConnected`**：实测（2026-09-26 第一轮）主机侧哪怕对面已经不应答了，`IsConnected` 仍是 `true`。
  判断对方死活要看心跳统计：`PingMsec` 是最后一次成功应答时的**旧值**，`PacketLoss` 才是"最近还答不答"
  （`ConnectionStats`：收到一次向 0 插值、丢一次向 1 插值，权重 0.2；200ms 一发 ⇒ 0.9998 ≈ 连续 8 秒无应答）。
- **通道用 Sidecar，但不能用它的"默认取 NetService"入口**：本体的 `INetMessage` 子类型要走编译期生成的 ID 表
  （`INetMessageSubtypes.cs`），模组加不进去——这正是 RitsuLib Sidecar 存在的理由（它用自己的信封借原版传输发原始包）。
  注意 `SendToHost(RunManager,…)` 那种便捷写法会去取 `RunManager.NetService`，而分离期本机跑单机局时那是
  `NetSingleplayerGameService`（`SendMessage` 是空实现）。所以我们一律传**自己捕获的那条传输**：
  `SendToHost(transport, …)` / `Broadcast(transport, …)`。
- **为什么拦断连要限定窗口**：只拦 `RunManager.CleanUp` 里那一次 `Disconnect(NetError.Quit)`
  （`RunManager.cs:1606`），所以在角色选择界面按"离开联机"照旧能正常断开——否则开关一开就退不出房间。

## 5. 本轮验证步骤（保活实验）

先只验一件事：**离开对局之后，那条连接能不能活下来。**

1. 两端都装 0.3.0+，设置里打开：`分离模式 → 接管联机链路`、`退出对局不断线`（两端都要开）。
2. 正常开一局联机局（现成流程即可，不需要 PVP 相关开关）。
3. 开局后确认日志出现：`[SplitMode] 已捕获联机传输：Host/Client(本机 netId=…, 连接=True)` 与 `保活已开启`。
4. 两端都**放弃本局 / 退出对局**，回到主菜单后**不要退出游戏**，等 60 秒。
5. 看两边日志与左上角状态行：

| 观测点 | 期望 |
|---|---|
| 每 10 秒一行 | `[SplitMode] 链路状态：… 链路=存活 …（已保活 N 秒）` |
| 对端那一段 | `对端=<id>(ping …ms, 丢包 …%, Ns 前收到)`，其中 **`Ns 前收到` 一直很小（< 1 秒）** 才算对面还活着 |
| 警告 | 不该出现 `[SplitMode] 对端 … 已经 N 秒没回包了` |
| 有没有断线弹窗 / `[SplitMode] 联机传输断开` | 没有 |

- ✅ 60 秒后两端仍然 `存活` **且对端一直是"1 秒内收到"** → 地基成立，可以进第 6 节第 1 步（自建通道）。
- ❌ 变成 `已断`，或对端长时间"没回包" → 说明连接只在本机侧挂着、对端其实已经走了（或 P2 进程没了）。

> ⚠ 环境选择：第一轮用的是 `LocalCoopClone`（同机双开），它的克隆进程在回主菜单时会被回收
> （日志里的 `LocalCoopClone … Cleaned clone session data`），**P2 的 log 也在那一刻被删掉**。
> 要拿两边证据，请用**真双机**；若坚持同机双开，请在回主菜单**之前**把
> `%TEMP%\LocalCoopClone\<session>\Roaming\SlayTheSpire2\logs\godot.log` 复制出来。

> 想彻底退出联机时：先把"退出对局不断线"关掉再退，或者直接关游戏。

## 5.1 Presence 通道验证（0.4.0 起，不需要真机也能先跑）

Presence 只是"把坐标发过去"，**不需要先做成分离局**就能验：两人正常开一局联机局，各自发坐标即可。

1. 两端都装 0.4.0+，设置里打开：`分离模式 → 接管联机链路` + `同步对方坐标（Presence）`。
2. 开一局联机局，开局后看日志：`[SplitMode] Presence 通道就绪：opcode=<数字>`（两端同号）。
3. 走一两个节点，看左上角状态行的第二行 `[坐标]`：

```
[分离模式] 阶段=…｜链路=…｜Host(本机 netId=1, 连接=True)｜对端=<id>(ping …ms, 丢包 …%, 0.2s 前收到)
[坐标] 我 act0(2,3) 地图 78%｜对端 <id> act0(2,3) 地图 74%（0.3s 前）｜presence 发 40 / 收 39 / 失败 0
```

| 观测点 | 期望 |
|---|---|
| `[SplitMode] Presence 通道就绪：opcode=…` | 两端都有，且**数字相同** |
| `presence 发 N / 收 M / 失败 0` | `N` 随时间涨；`M` 也涨（说明对面在发、我们收得到） |
| 对端坐标 | 跟对端实际位置一致；两人在同一个坐标时 **`相遇判定` 会成立**（下一步才用它触发合流） |

- ✅ 两端都能看到对方的坐标 → 通道成立，可以进第 6 节第 2 步（地图图标）。
- ❌ `失败` 一直涨 / `收 0` → 通道没通：先看是不是只有一边开了开关、或两边 mod 版本不同
  （opcode 由 mod id + 消息键算出来，版本不同不影响，但**消息类型不同**就收不到）。

## 6. 后续路线（每一格都可单独验证）

1. ~~**自建 Presence 通道**（坐标台账）~~ —— 已在 0.4.0 落地（见 5.1），待验证。
2. ~~**地图图标（L1）**~~ —— 已在 0.5.0 落地（`Hud/MapPeerMarker.cs`），待验证。
3. **分离期入口（L1）** —— 已在 0.6.0 落地（`Solo/SplitSoloRun.cs`，默认关）：进联机局后各自切到自己的单机局（同种子）。
   验证：两端都开该开关 → 开一局联机 → 约 6 秒后日志出现 `分离期入口：切成自己的单机局` / `单机局已开始`，
   之后两人地图上互见对方的标记在**不同节点**。
   ⚠ 已知取舍：单人局用初始卡组/遗物（不继承当前进度）；切过去后各自独立，没法回到同一局；
   两端同时 `CleanUp` 存在竞态（若主机先清导致客户端掉线，下一步改成"主机广播一起切"）。
4. **相遇判定（L1→L2）**：同幕 + 同坐标 + 双方空闲 → 触发握手（带冷却与防抖）。
4. **快照与合成（L2）**：`ToSave()` 取快照、交换 `SerializablePlayer`、主机合成 `DuelSave`
   （PVP 幕 + 两个 player，netId 对齐，`LoadRunLobby` 就是按 netId 认人的）。
5. **切会话（L2）**：本地退局 → 主机 `LoadRunLobby` → 客户端 load-join（`RunSessionState.InLoadedLobby`）→ `SetUpSavedMultiplayer`。
   ⚠ 这条是最大的未知数：要在**已有连接**上驱动 load-join，而不是断线重连（退路：相遇时干脆重连一次）。
6. **决斗（L3）**：复用现有 `Core/Duel/**`，入口从"事件"改成"载入即进"。
7. **赛后（L4）**：输家结束、赢家 `SetUpSavedSingleplayer(自己的快照)` 载回。

## 7. 风险与已知坑

| 风险 | 说明 | 应对 |
|---|---|---|
| Steam P2P 空闲超时 | 保活期间没有业务流量 | 加心跳（第 6 节第 1 步） |
| 双泵 | 我们的泵与本体（`NRun` / 大厅界面）可能同时 `Update` | 只在"游戏当前用的传输不是我们这条"时泵 |
| 消息没有官方注册表 | `INetMessage` 子类型的 ID 表是编译期生成的，模组加不进去 | 走原始包 / 参考 RitsuLib Sidecar 的 raw envelope |
| 地图不一致 | 一方带了会重造地图的遗物（SpoilsMap / BigGameHunter）时坐标不可比 | 降级为"只显示对方在第 X 幕" |
| 载入失败 | 合成存档被拒（netId 不匹配 / 状态还原异常） | 各自载回自己的快照 + 相遇冷却 |
| 第三方 mod | 分离期本体不同步，`together` 之类的 mod 行为要单独确认 | 分离模式开关独立，默认关 |
