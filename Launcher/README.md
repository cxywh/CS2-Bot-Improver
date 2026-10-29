# Launcher

本地人机局配置前端，C# / WinForms，目标框架 .NET 10（`net10.0-windows`）。用于替代发行包中的 `Panel` 图形界面，配合 `BotRandomizer` 插件使用。

## 功能

| 页面 | 内容 | 写入目标 |
| --- | --- | --- |
| 概览 | 十项环境检查：CS2 目录、插件、启动方式、`cs2.exe`、`core.json`、白名单、权重文件、模式、难度、掉落刀绑定 | 无（只读） |
| 模式与启动 | Online 与 WithBots 模式切换、启动 CS2、显示 Steam 启动项 | `game/csgo/gameinfo.gi` |
| 难度 | Low / Medium / High 三档 botprofile | `game/csgo/overrides/botprofile.vpk` |
| 掉落刀具 | 捕获绑定键、选择刀型、写入 `bind` 行 | `game/csgo/cfg/my_bot_*.cfg` |
| 真人白名单 | 读取 Steam 账号、增删 SteamID、保存白名单 | `self_steamids.txt` |
| 随机权重 | 七组滑块，实时显示组内占比，一次保存全部组 | `weights.json` |

写入前对目标文件生成一次性备份（`.gameinfo.bak`、`.botprofile.bak`、`.editbak`），已存在备份时不覆盖。

## 构建

框架依赖（体积小，需 .NET 10 桌面运行时）：

```
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o dist
```

自包含（无需安装运行时，约 49 MB）：

```
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true ^
  -p:SelfContained=true -p:EnableCompressionInSingleFile=true -o dist
```

## 插件安装

程序在与 `BotLauncher.exe` 同级的 `payload` 目录中读取插件文件，通过「概览 → 安装 / 更新插件」写入：

```
game/csgo/addons/counterstrikesharp/plugins/BotRandomizer/
```

`weights.json` 与 `self_steamids.txt` 仅在目标目录不存在时写入，不覆盖已有配置。被替换的文件备份至 `game/csgo/backup/plugin-<时间戳>/`。

`payload` 目录内容：

```
BotRandomizer.dll
BotRandomizer.deps.json
cosmetic_catalog.json
charm_placements.json
weights.json
self_steamids.txt
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--page=N` | 启动时打开第 N 页（0 起） |
| `--launch` | 不显示界面，直接以 `-insecure` 启动 `cs2.exe` |
| `--selftest` | 无界面自检，覆盖目录探测、模式与难度切换、掉落刀读写、权重往返、白名单写入、插件安装 |
| `--audit` | 按当前 DPI 遍历各页控件，报告文字尺寸超过控件边界的项目 |
| `--measure` | 输出样本文本的实测宽度与渲染墨迹宽度，用于校核排版 |
| `--dump-layout` | 输出各控件实际坐标至 `%TEMP%\botlauncher-layout.txt` |

`--selftest` 与 `--audit` 均以退出码表示结果：0 为通过。

## 运行前提

- Windows 10 1809 或更高版本
- 以 `-insecure` 启动 CS2，Metamod 与 CounterStrikeSharp 方可加载
- 需要预先安装 Metamod:Source 与 CounterStrikeSharp

## 实现约定

- 控件尺寸由 `TextRenderer.MeasureText` 实测文本决定，不使用固定像素值，以保证在 100% 至 200% 缩放下文字不被裁切
- 权重滑块仅在拖动、方向键与双击输入时改变数值，鼠标滚轮用于滚动列表
- 模式与难度切换、掉落刀写入在 CS2 运行期间拒绝执行，避免退出游戏时被重写

## 许可

本目录代码随本仓库以 GNU Affero General Public License v3.0 发布。
