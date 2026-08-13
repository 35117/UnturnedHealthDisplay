# 血量显示 HealthDisplay（Unturned BepInEx）

作者：35117+Deepseek-v4-flash-0731

为未转变者（Unturned）3.26 开发的生物血量显示插件：在生物头顶显示血量（数字 / 血条 / 两者），支持黑白名单过滤、准星与角落显示模式，攻击时目标上方飘出伤害数字。

## 版本号规则

版本号格式为 `年.月.日.第几版`，例如 `26.8.13.1` 表示 2026 年 8 月 13 日当天上传的第 1 版。

## 安装

1. 安装 [BepInEx 5](https://docs.bepinex.dev/)（x64 版本）到游戏根目录。
2. 将 `BepInEx/Plugins/HealthDisplayMod.dll` 放入游戏根目录的 `BepInEx/Plugins/` 文件夹。
3. 启动游戏（单机或主机模式），配置自动生成在 `BepInEx/config/com.trae.healthdisplay.cfg`。

## 运行模式

| 模式 | 说明 |
|------|------|
| 单机 / 主机 | 完整功能：生物血量 + 伤害数字 + 受伤提示 |
| 客户端（连接他人服务器） | 降级：屏幕左下角显示自己血量、受到伤害时屏幕中央显示红色数字 |
| 专用服务器 | 无 UI，不生效 |

> 说明：僵尸/动物的实时血量与"造成伤害"的数值由服务器结算，客户端无法获取。因此完整功能需要本机为服务器（单机或自己开主机）。

## 功能

- 显示僵尸 / 动物 / 玩家血量（数字、血条、血条+数字），血条随距离缩放（近大远小，有上下限）
- 显示位置：生物头顶 / 准星下方（当前瞄准目标）/ 屏幕左下角（自己血量 HUD）
- 显示范围：所有 / 附近所有（可配距离）/ 视野内 / 准心附近（屏幕半径）/ 命中时（打过的目标）/ 关闭
- 隔墙检测：目标被墙壁/物体遮挡时不显示血条（默认开启，可关闭）
- 黑白名单：按僵尸类型（特殊类型枚举名）、动物资产、玩家 SteamID 过滤，支持类型通配
- 伤害数字：攻击命中时在目标上方飘出实际伤害值，爆头数字更大且为橙色
- 自己受到伤害时，屏幕中央显示红色伤害数字

## 配置

配置文件：`BepInEx/config/com.trae.healthdisplay.cfg`（游戏内可通过插件管理器修改，配置按分类导航分组）

| 分类 | 键 | 默认值 | 说明 |
|------|----|--------|------|
| 通用设置 | Enabled | true | 插件总开关 |
| 名单过滤 | ListMode | Black | 名单模式：Black=黑名单（名单中的不显示），White=白名单（只显示名单中的） |
| 名单过滤 | WhiteList | （空） | 白名单列表，每行一个条目 |
| 名单过滤 | BlackList | （空） | 黑名单列表，每行一个条目 |
| 名单过滤 | ShowZombies | true | 是否显示僵尸血量 |
| 名单过滤 | ShowAnimals | true | 是否显示动物血量 |
| 名单过滤 | ShowPlayers | false | 是否显示玩家血量（仅服务器端生效） |
| 显示设置 | DisplayMode | Both | 显示模式：Both=血条+数字，Bar=仅血条，Number=仅数字 |
| 显示设置 | ShowPercentage | false | 数字模式下额外显示百分比 |
| 显示设置 | DisplayPosition | Head | 显示位置：Head=头顶，Crosshair=准星下方，Corner=屏幕左下角 |
| 显示设置 | DisplayScope | All | 显示范围：All=距离内所有，Nearby=附近所有，View=视野内，Crosshair=准心附近，Hit=命中过的目标，Off=关闭生物血量 |
| 显示设置 | NearbyDistance | 10 | Nearby 模式的显示距离（米） |
| 显示设置 | CrosshairRadius | 100 | Crosshair 模式屏幕半径（像素） |
| 显示设置 | MaxDistance | 30 | 最大显示距离（米） |
| 显示设置 | ShowNames | true | 血条上方显示名称 |
| 显示设置 | NameFontSize | 13 | 名称字号（随距离缩放，实际=该值×倍率） |
| 显示设置 | BarScaleMin | 0.5 | 血条缩放下限（远处最小倍率，10 米为 1 倍） |
| 显示设置 | BarScaleMax | 1.8 | 血条缩放上限（近处最大倍率，防止过大） |
| 显示设置 | OcclusionCheck | true | 不隔墙显示：被遮挡时隐藏血条 |
| 伤害数字 | ShowDamageNumbers | true | 造成伤害时显示伤害数字 |
| 伤害数字 | DamageLifetime | 1.2 | 伤害数字持续时间（秒） |
| 伤害数字 | ShowIncomingDamage | true | 自己受伤时屏幕中央显示红色数字 |

### 名单条目格式

黑/白名单使用插件管理器的**生物选择器**（`Unturned.CreatureList` 标签），也可手动填写，每行一个条目（支持逗号、分号、换行分隔）：

| 条目 | 含义 |
|------|------|
| `Z:NORMAL` | 僵尸类型（EZombieSpeciality 枚举名：NORMAL/MEGA/SPRINTER/CRAWLER/ACID/BURNER 等） |
| `A:12` | 动物资产 ID 12（Asset ID） |
| `P:76561198000000000` | 玩家 SteamID |
| `Z:*` / `A:*` / `P:*` | 对应类型的全部 |

> 兼容旧版数字写法：`Z:0`（僵尸类型表索引）仍可识别。

示例（黑名单：不显示普通僵尸与类型 5 的僵尸）：
```
Z:NORMAL
Z:5
```

示例（白名单：只显示所有僵尸，动物玩家都不显示）：
```
Z:*
```

## 编译

环境要求：.NET Framework 4.x（`csc.exe`）、C# 5 语法。

运行 `HealthDisplay-Source/build.bat`，输出 `BepInEx/Plugins/HealthDisplayMod.dll`。

## 兼容性

- 插件配置项使用 `Unturned.Category` / `Unturned.Cycle` 标签，与插件管理器（PluginManager）兼容，可在游戏内管理。
- 版本号固定 4 段格式（`System.Version` 最多支持 4 段）。
