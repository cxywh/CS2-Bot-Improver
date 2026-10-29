# 自用人机换装与本地启动器

本文说明 `BotRandomizer` 白名单分支与 `Launcher` 的部署与使用方法。

## 适用范围

| 项目 | 说明 |
| --- | --- |
| 可用环境 | 自建专用服务器、本地人机局 |
| 不可用环境 | Valve 官方匹配、FACEIT、第三方公开社区服 |
| 启动要求 | 命令行须包含 `-insecure` 或 `-dedicated`，否则 Metamod 不加载 |
| 依赖 | Metamod:Source、CounterStrikeSharp（本仓库不包含） |

`BotRandomizer` 仅在服务端构造经济物品数据，不修改 Steam 库存、不修改客户端资源。真人白名单中的玩家获得与 bot 相同的随机外观，白名单为空时仅 bot 生效。

## 部署

1. 安装 Metamod:Source 与 CounterStrikeSharp，确认 `game/csgo/addons/metamod` 已加入 `gameinfo.gi`。
2. 将 `addons/counterstrikesharp/configs/core.json` 中的 `FollowCS2ServerGuidelines` 设为 `false`。
3. 启动 `Launcher`，在概览页确认 CS2 目录；如探测有误，使用「指定 CS2 目录」选择 `game/csgo`。
4. 点击「安装 / 更新插件」，将插件文件写入 `addons/counterstrikesharp/plugins/BotRandomizer/`。
5. 进入「真人白名单」页，添加 SteamID 后保存；仅需 bot 生效时跳过此步。
6. 进入「模式与启动」页，选择「本地人机 WithBots」并应用。
7. 点击「启动 CS2（-insecure）」。经 Steam 库直接启动不会附带该参数，游戏将报 `CAppSystemDict: Unable to create interface Source2ServerConfig001`。
8. 创建人机对局。外观在换图或下一回合生效。

## 配置项

`weights.json` 与 `self_steamids.txt` 位于插件目录，字段说明见 [`BotRandomizer/README.md`](../BotRandomizer/README.md)。权重保存前整体校验：印花档位须递增，挂件分子不得大于分母，任一项不合法则整份拒绝写入。

## 故障处理

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 启动时报 `Source2ServerConfig001` | 命令行缺少 `-insecure` | 使用启动器的「启动 CS2（-insecure）」，或在 Steam 启动项中加入 `-insecure` |
| 仅 bot 换装 | 白名单为空或不含当前账号 | 在游戏中执行 `status` 取得 SteamID64，填入白名单后保存并换图 |
| 已持有的武器没有皮肤 | 枪皮在武器生成时写入 | 丢弃后重新购买 |
| 真人音乐盒未改变 | 客户端以本地库存覆盖服务端字段 | 设计如此，音乐盒仅对 bot 写入 |
| 切换模式或难度被拒绝 | CS2 正在运行 | 退出游戏后重试；游戏退出时会重写 `gameinfo.gi` 与 `botprofile.vpk` |
| 权重保存被拒绝 | 数值未通过校验 | 按提示修正后重新保存 |

## 分发与许可

- `BotRandomizer` 为 [ed0ard/CS2-Bot-Randomizer](https://github.com/ed0ard/CS2-Bot-Randomizer) 1.3.4 的衍生版本，遵循 AGPL-3.0。分发二进制时须同时提供本仓库源码，并保留许可与修改说明。
- `Launcher` 随本仓库以 AGPL-3.0 发布。
- 发行包中原作者的 `Panel v1.4.7.exe` 采用 PolyForm Strict 1.0.0，禁止分发与衍生分发，本仓库不包含该文件。
