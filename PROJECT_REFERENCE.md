# MHWI Fatalis Overlay — 项目参考手册

## 一、项目概述

C# WPF 程序，针对 Monster Hunter World: Iceborne 黑龙（Fatalis, ID=101）战斗。
双窗口架构：设置窗口 + 透明悬浮 Overlay。.NET 10.0，框架依赖发布。

## 二、外部参考来源

### 2.1 mhw-radar（Rust 项目）
- 仓库：https://github.com/stellarling/mhw-radar
- 用途：内存偏移参考、黑龙招式名称对应表
- 关键文件：`engine/src/reader.rs`（内存读取）、`engine/src/data/monster_101.json`（招式映射）
- 招式映射文件已复制到本项目：`action_names.json`（206条黑龙招式，可编辑）

### 2.2 HunterPie v2（C# 项目）
- 仓库：https://github.com/Haato3o/HunterPie-v2
- 用途：结构体定义参考（部位、异常、HUD、武器等 C# struct 偏移）
- 关键文件：
  - `HunterPie.Integrations/Datasources/MonsterHunterWorld/Definitions/` — 所有结构体定义
  - `HunterPie.Integrations/Datasources/MonsterHunterWorld/Entity/Enemy/MHWMonster.cs` — 怪物数据读取
  - `HunterPie.Integrations/Datasources/MonsterHunterWorld/Entity/Player/` — 玩家数据读取
  - `HunterPie/Game/World/Data/MonsterData.xml` — 怪物部位/异常定义

### 2.3 内存地址（MHW v421810）
所有地址基于 `MonsterHunterWorld.exe` 模块基址（`gameBase`）。

| 名称 | 偏移 | 说明 |
|------|------|------|
| PLAYER_BASE | 0x050139A0 | 玩家/装备基址 |
| QUEST_BASE | 0x0500ED30 | 任务数据基址 |
| MONSTER_ARRAY_BASE | 0x0500CF40 | 怪物数组（128个） |
| COUNTERATTACK_BASE | 0x05013C50 | 黑龙下压值/AI决策基址 |

### 2.4 关键指针链

**黑龙下压值：**
```
[BASE + 0x05013C50] → +0xE58 → [ptr] → +0x18388 → float
P1/P2 阈值 1500，P3（动作179后）阈值 2143（=1500/0.7）
```

**AI决策距离/角度：**
```
[BASE + 0x05013C50] → +0xE58 → [ptr] → +0x1008 → [ptr]
  → +0x5FC: float AiDist
  → +0x604: float AiAngle
```

**玩家血量（MHWHudStructure, LayoutKind.Explicit）：**
```
[BASE + 0x050139A0] → +0x50 → [ptr] → +0x7630 → [ptr]
  → +0x60: float MaxHealth
  → +0x64: float Health
```

**太刀练气刃等级（MHWLongSwordStructure, LayoutKind.Explicit）：**
```
[BASE + 0x050139A0] → +0x50 → [ptr] → +0x76B0 → [ptr]
  → +0x2368: float BuildUp (0-1, ×100=百分比)
  → +0x2370: int SpiritLevel (0=无 1=白 2=黄 3=红)
  → +0x2374: float SpiritLevelTimer
```

**怪物血量：**
```
monsterPtr + 0x7670 → [ptr] → +0x60: MaxHealth, +0x64: Health
```

**怪物部位（MHWMonsterPartStructure, LayoutKind.Sequential, stride=0x1F8）：**
```
monsterPtr + 0x1D058 → [ptr]
  普通部位: +0x40 + slot×0x1F8
  → +0x0C: float MaxHealth
  → +0x10: float Health
  → +0x18: int Counter (硬直次数)
  可切断部位: +0x1FC8, stride=0x78
```

**黑龙部位定义（来自 MonsterData.xml）：**
按内存槽位顺序排列（排除 Id=0 可切断）：
| 槽位 | Id | 名称 | 破坏阈值 |
|------|-----|------|---------|
| 0 | 1 | 头 | 3, 6 |
| 1 | 2 | 脖子 | - |
| 2 | 3 | 胸 | 2 |
| 3 | 4 | 身体 | - |
| 4 | 5 | 左手 | - |
| 5 | 6 | 右手 | - |
| 6 | 7 | 左腿 | - |
| 7 | 8 | 右腿 | - |
| 8 | 9 | 左翼 | 1 |
| 9 | 10 | 右翼 | 1 |
| 10 | 11 | 尾 | - |

**怪物异常（MHWMonsterAilmentStructure, LayoutKind.Sequential）：**
```
monsterPtr + 0x1BC40 → 遍历指针链表
  ailmentPtr + 0x148:
    +0x08: int IsActive
    +0x10: int Id
    +0x14: float MaxDuration
    +0x30: float Buildup (积蓄值)
    +0x40: float MaxBuildup (阈值)
    +0x70: float Duration (持续时间)
    +0x78: int Counter (触发次数)
```

**异常 ID 映射（来自 MonsterData.xml Ailments 节）：**
| Id | 名称 |
|----|------|
| 1 | 毒 |
| 2 | 麻 |
| 3 | 眠 |
| 4 | 爆 |
| 5 | 骑乘 |
| 6 | 减气 |
| 7 | 晕 |
| 99 | 怒 |

**软化（MHWTenderizeInfoStructure, LayoutKind.Sequential, stride=0x40）：**
```
monsterPtr + 0x1C458, 共10个
  → +0x08: float Duration（已过时间，从0递增）
  → +0x0C: float MaxDuration
  → +0x30: uint PartId
软化 PartId 映射: 0,3=头 | 1,4=左手+右手+胸 | 2=左腿 | 5=右腿
IsTenderized = Duration < MaxDuration（未过期）
剩余比例 = (MaxDuration - Duration) / MaxDuration（满→空）
```

**发怒（MHWMonsterStatusStructure, LayoutKind.Sequential）：**
```
monsterPtr + 0x1BE30
  → +0x14: int IsActive（真正的进怒标记）
  → +0x18: float Buildup
  → +0x24: float Duration（已过时间，从0递增）
  → +0x28: float MaxDuration
  → +0x3C: float MaxBuildup
剩余时间 = MaxDuration - Duration
```

**任务计时器：**
```
[BASE + 0x0500ED30] = questPtr
questPtr + 0x4C: int QuestId
questPtr + 0x54: int QuestState (2=任务中)
questPtr + 0x13180: ulong timerRaw
timerRaw / 60.0 = 剩余秒数
```

## 三、项目文件结构

```
FatalisOverlay-Source/
├── FatalisOverlay.csproj      # .NET 10.0 WPF 项目
├── App.xaml / .cs             # 入口，启动 SettingsWindow
├── MainWindow.xaml / .cs      # Overlay 透明悬浮窗
├── MainViewModel.cs           # MVVM ViewModel + 后台轮询 (28ms)
├── SettingsWindow.xaml / .cs  # 设置窗口
├── LogViewerWindow.xaml / .cs # 日志查看器窗口
├── LogViewerViewModel.cs      # 日志查看器 ViewModel
├── Models/
│   ├── AppConfig.cs           # 所有配置项，JSON序列化，INotifyPropertyChanged
│   └── LogSession.cs          # 日志会话/条目模型，JSONL解析
├── Services/
│   ├── MemoryReader.cs        # P/Invoke kernel32 ReadProcessMemory
│   ├── GameDataService.cs     # 类ProcessService：所有游戏数据读取
│   ├── GameData.cs            # 数据模型：GameData, FatalisPart, AilmentInfo
│   └── BattleLogger.cs        # 战斗日志模块（JSONL输出）
├── Converters/
│   └── ValueConverters.cs     # WPF绑定转换器
├── action_names.json          # 黑龙招式ID→名称映射（可编辑）
└── app.manifest               # Windows 清单
```

## 四、主要功能

### 4.1 Overlay 悬浮窗
- 透明、置顶、不在任务栏、拖拽移动
- 黑龙任务中自动显示完整数据、非黑龙任务显示最小状态条
- 实时刷新（默认28ms，可在设置中调整10-200ms）

### 4.2 显示项目（均可独立开关）
- 🩸 **血量**：绿条 + 当前/最大 + 百分比，78%/50%处有红色转阶段标记
- 🔶 **下压值**：橙条，P1/P2上限1500，P3自动变为2143
- 🦴 **九部位**：头/胸/左手/右手/左腿/右腿/脖子/左翼/右翼，各有独立开关，显示血量+硬直次数+破坏星级(★/★★)
- 💎 **软化白条**：部位血量条上方细白条，随剩余时间缩短
- ☠ **异常状态**：毒/麻/眠/爆/晕/减气/骑乘/怒，各有开关，显示积蓄值+触发次数
- ⚡ **发怒**：积蓄进度 + 倒计时（使用IsActive标记而非Duration>0）
- ⚔ **太刀练气刃**：白/黄/红刃实时显示
- 🧠 **AI决策**：距离(m) + 角度(°)
- 📏 **玩家距离**：水平距离
- ⏱ **任务计时**：MM:SS格式

### 4.3 设置窗口
- 深色主题，所有开关实时生效
- 缩放滑块 (0.5-3.0x)、刷新率 (10-200ms)
- 自定义颜色：血量/下压值/部位/背景
- 配置自动保存到 `config.json`

### 4.4 战斗日志
- JSONL 格式，每局一个文件 `logs/fatalis_日期_时间.jsonl`
- 三种记录类型，用 `type` 字段区分：
  - `action`: 黑龙出招变化
  - `damage`: 黑龙血量下降
  - `enrage_start` / `enrage_end`: 进怒/消怒
- 变化检测：招式ID变化或血量下降 >1 即记录
- 重开检测：黑龙血量回升(>500) 且 玩家曾到达地面(Y<500)
- 开关：设置窗口 > 战斗日志复选框（默认关闭）

### 4.5 日志查看器
- 设置窗口点击"查看战斗日志"打开
- 左侧：对局列表（新→旧，显示时间+时长+条目数）
- 右侧：详情，彩色左侧色条 + 进度条（头/胸/下压值）
- 筛选：招式/伤害/怒 分别勾选
- 操作：重命名、删除、刷新
- 进怒/消怒：独立彩色横幅样式

### 4.6 快捷键
| 键 | 功能 |
|----|------|
| 拖拽 | 移动 Overlay |
| 双击Overlay | 记事本打开config.json |
| Esc | 隐藏Overlay |
| F5 | 重载config.json |

## 五、配置说明 (config.json)

所有配置项自动序列化，首次运行自动生成。可直接编辑 JSON 文件或用设置窗口。

```json
{
  "ShowHealth": true,
  "ShowCounterattack": true,
  "ShowAiDecision": true,
  "ShowQuestTimer": true,
  "ShowDistance": true,
  "ShowBattleLog": false,
  "ShowPartHead": true,
  "ShowPartChest": true,
  "ShowPartLArm": true,
  "ShowPartRArm": true,
  "ShowPartLLeg": true,
  "ShowPartRLeg": true,
  "ShowPartNeck": true,
  "ShowPartLWing": true,
  "ShowPartRWing": true,
  "ShowAilmentPoison": true,
  "ShowAilmentPara": true,
  "ShowAilmentSleep": true,
  "ShowAilmentBlast": true,
  "ShowAilmentStun": true,
  "ShowAilmentExhaust": true,
  "ShowAilmentMount": true,
  "ShowAilmentEnrage": true,
  "WindowX": 100,
  "WindowY": 100,
  "Scale": 1.0,
  "HealthColor": "#4CAF50",
  "CounterattackColor": "#FF9800",
  "PartColor": "#2196F3",
  "BackgroundColor": "#CC1A1A1A",
  "PollingRateMs": 28
}
```

## 六、action_names.json 格式

```json
[
  {"action_id": 1, "name": "初始动画"},
  {"action_id": 4, "name": "两足进怒吼"}
]
```
字段 `action_id`（JSON）对应 `ActionId`（C#）。程序启动加载，用户可编辑。

## 七、战斗日志 JSONL 格式

### 招式变化
```json
{"type":"action","ts":123.45,"aid":154,"aname":"扇形火","mhp":45000,"php":200,"slv":3,"h_hp":4500,"c_hp":6750,"ca":800,"dist":15.2,"ai_d":20.1,"ai_a":45.3}
```

### 血量下降
```json
{"type":"damage","ts":123.45,"mhp":44500,"delta":500,"php":200,"slv":3,"h_hp":4500,"c_hp":6750,"ca":800,"dist":15.2}
```

### 进怒/消怒
```json
{"type":"enrage_start","ts":456.78}
{"type":"enrage_end","ts":500.12}
```

| 字段 | 类型 | 含义 |
|------|------|------|
| ts | float | 任务已过秒数 |
| aid | int | 招式ID |
| aname | string | 招式中文名 |
| mhp | int | 黑龙当前血量 |
| delta | int | 本次血量下降量 |
| php | int | 玩家血量 |
| slv | int | 太刀练气刃 0-3 |
| h_hp | int | 头部部位血量 |
| c_hp | int | 胸部部位血量 |
| ca | int | 下压值 |
| dist | float | 玩家距离(m) |
| ai_d | float | AI决策距离(m) |
| ai_a | float | AI决策角度(°) |

## 八、如何修改

### 添加新的显示项
1. `GameData.cs` 添加字段
2. `GameDataService.cs` 添加读取方法（内存偏移+指针链）
3. `AppConfig.cs` 添加开关属性（private field + public property + OnPropertyChanged）
4. `MainWindow.xaml` 添加UI绑定
5. `SettingsWindow.xaml` 添加勾选框

### 修改招式映射
编辑 `action_names.json`，增减条目或修改名称。程序启动时加载。

### 修改内存偏移
所有偏移在 `GameDataService.cs` 顶部常量区。游戏版本更新时需要对照新地址映射文件修改。

### 添加新的异常类型
1. `GameDataService.cs` 中 `AilmentNames` 字典添加新 ID 和名称
2. `AppConfig.cs` 添加对应的 ShowAilmentXxx 开关
3. `GameData.cs` 中 `Ailments` 预填充字典添加新 ID
4. `SettingsWindow.xaml` 添加 CheckBox
5. `MainWindow.xaml` 添加对应的异常方块

## 九、编译与分发

### 编译
```bash
dotnet build -c Release
```

### 分发（框架依赖，需.NET 10.0 Desktop Runtime）
```bash
dotnet publish -c Release -r win-x64 --no-self-contained -o dist
```
分发文件：`FatalisOverlay.exe`、`.dll`、`.deps.json`、`.runtimeconfig.json`、`action_names.json`

### GitHub
- 仓库：https://github.com/YoRHa-TwoB/MHWI-FatalisOverlay
- License：MIT
