# BotRandomizer 分支说明

本目录为 [ed0ard/CS2-Bot-Randomizer](https://github.com/ed0ard/CS2-Bot-Randomizer) 1.3.4 的衍生版本，在原插件基础上增加真人白名单、音乐盒写入限制与外部权重文件。

- 许可：GNU Affero General Public License v3.0（见仓库根目录 `LICENSE`）
- 已修改：以下三节列出的改动均已实施

## 与上游的差异

### 1. 真人白名单

上游仅对 bot 应用伪造的经济物品。本版本增加 `self_steamids.txt`，文件内列出的 SteamID64 与 bot 使用同一套随机外观。

改动位于 `BotRandomizer.cs`：

- 新增 `_selfSteamIds`、`IsCosmeticTarget`、`LoadSelfSteamIds`
- 原 9 处 `player.IsBot` 判定改为「bot 或白名单内真人」
- `GetPlayerFromItemServices` 由静态方法改为实例方法，否则 `GiveNamedItem` 前置钩子无法取得真人玩家实体

### 2. 音乐盒仅写入 bot

真人客户端的音乐盒由本地库存解析，并在出生与回合 MVP 时覆盖服务端写入，因此对真人写入不产生效果。`ApplyMusicKit` 在入口处直接返回，`OnRoundMvp` 对非 bot 玩家不再处理。

### 3. 权重外部化

新增 `Cosmetics/CosmeticWeights.cs`，从插件目录下的可选文件 `weights.json` 读取权重与阈值。`Cosmetics/CosmeticRoller.cs` 中原有的硬编码数值改为从 `CosmeticWeights` 读取；文件缺失时回退至内置默认值，行为与上游一致。

## 配置

### 文件清单

| 文件 | 说明 |
| --- | --- |
| `cosmetic_catalog.json` | 物品目录，取自上游 1.3.4 发行包 |
| `charm_placements.json` | 挂件挂点表，取自上游 1.3.4 发行包 |
| `weights.json` | 权重配置，缺失时使用内置默认值 |
| `self_steamids.txt` | 真人白名单，仓库内仅提供 `self_steamids.example.txt`，部署时复制并改名 |
| `BotRandomizer.deps.json` | 由构建生成，随 `dotnet publish` 输出 |

### self_steamids.txt

每行一个 SteamID64，`//` 之后为注释，`#` 开头的行为注释行。

```
# 仅列出需要生效的玩家
76561198000000001   // 示例
```

该文件为空或缺失时，插件仅对 bot 生效。

### weights.json

所有字段均可省略，缺省时使用内置默认值；值必须为正整数。主要内容：

| 分区 | 含义 |
| --- | --- |
| `knives` | 各刀型相对权重 |
| `knifeFinish` | 刀皮偏好权重 |
| `gloves` | 手套相对权重 |
| `weaponRarity` | 枪皮稀有度权重 |
| `stickerFinish` | 印花 finish 权重 |
| `stickerCraft` | 印花张数档位阈值，须递增 |
| `keychain` | 挂件出现概率，`chanceNumerator` 不大于 `chanceDenominator` |

## 构建

```
dotnet publish -c Release -o dist
```

产物 `dist/BotRandomizer.dll` 与同目录下的 `cosmetic_catalog.json`、`charm_placements.json` 需一同部署至：

```
game/csgo/addons/counterstrikesharp/plugins/BotRandomizer/
```

## 运行前提

- 服务端或客户端须以 `-insecure` 或 `-dedicated` 启动，否则 Metamod 与 CounterStrikeSharp 不会加载
- `addons/counterstrikesharp/configs/core.json` 中 `FollowCS2ServerGuidelines` 须为 `false`
- 仅适用于自建服务器与本地人机局
