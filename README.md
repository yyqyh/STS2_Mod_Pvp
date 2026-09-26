# PVP 决斗（`pvp_duel`）

《杀戮尖塔 2》联机下的玩家对战 mod：第三幕打完追加一幕「PVP 决斗」，接受对决后**没有怪物、两人轮流行动、谁先倒下谁输**，手牌对对手隐藏。

- 只在**正好两名玩家**的局里生效；单人 / 三人局下本 mod 什么都不做。
- 进决斗前会（可选）解绑 together 的共享；不依赖 together，不引入任何其它 mod。
- 调试用：设置里打开「调试入口」，本局第一个问号房直接换成对决邀请，不用跑完三幕。

## 文档

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) —— 分层、模块与开关、目录职责、运行时生命周期、跨端一致性约定
- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) —— 每个机制的实现方案、对外 API、构建方式、踩坑清单

## 构建

```powershell
# 只改 C#：编译并自动部署 DLL 与清单到 <游戏目录>\mods\pvp_duel\
dotnet build .\pvp_duel.csproj -c Debug -p:STS2_SKIP_PCK_EXPORT=1

# 改了 JSON / 图片 / 场景：还要重出 PCK
godot --headless --path . --export-pack BasicExport "<游戏目录>\mods\pvp_duel\pvp_duel.pck"
```

`Sts2Dir` / `GodotExe` 在 `local.props` 里配（缺了会走 `Sts2PathDiscovery.props` 自动探测）。
版本号以清单 `pvp_duel.json` 为准，必须与 `Core/ModInfo.cs` 的 `Version` 一致 —— 编译前有闸门会拦。
