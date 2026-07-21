# MHWI Fatalis Overlay

Monster Hunter World: Iceborne Fatalis (黑龙) 专用数据 Overlay。实时显示血量、下压值、部位破坏、异常状态、发怒倒计时、软化计时等战斗信息。

## v1.3 新增：THK 行为树路径追踪

实时显示黑龙每招的 AI 决策路径——经过了哪个 Combat_Main 节点、命中了什么条件、跳转到 Global 的哪个节点、出了哪招。

- 🧠 实时 Overlay 显示每招决策路径（`node_065 → Global.node_081 → 扇形火`）
- 🎲 显示最后一层概率分支权重（如 `[15%]`）
- 📋 战斗日志完整记录决策轨迹，日志查看器展示路径
- 🔧 THKLogger.dll 注入插件 (C++) — Hook `ProcessTHKSegment`，采集行为树段数据
- 🗺 精确节点编号映射 — 通过 Leviathon 从原始 `.thk` 生成的 `nack_map_full.json` 完成 Binary Index → .nack 转换，无需手工偏移量

## 功能

- 🩸 血量 + 转阶段标记 (78% / 50%)
- 🔶 下压值 (P1/P2/P3 自动适配)
- 🦴 九部位独立显示 (血量 + 硬直次数 + 破坏等级)
- 💎 软化白条 (头/胸+手/腿)
- ☠ 毒/麻/眠/爆/晕/减气/骑乘 分别勾选
- ⚡ 发怒积蓄 + 倒计时
- 🧠 AI 决策距离/角度 + THK 行为树路径
- 🏔 高台检测 (判断当前招式是对高台/非高台)
- ⏱ 任务计时
- ⚙ 实时生效的设置窗口

## 运行要求

- Windows 10/11 x64
- [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Monster Hunter: World (Iceborne)
- [Stracker's Loader](https://www.nexusmods.com/monsterhunterworld/mods/1982)（用于加载 THKLogger.dll）

## 使用方法

1. 下载 [最新 Release](https://github.com/YoRHa-TwoB/MHWI-FatalisOverlay/releases)
2. 解压到任意文件夹
3. 双击 `FatalisOverlay.exe`
4. 首次运行会自动将 `THKLogger.dll` 部署到 `<MHW目录>/nativePC/plugins/`
5. **重启 MHW**（Stracker's Loader 仅在启动时加载 DLL）
6. 在设置窗口勾选需要的显示项，进黑龙任务自动显示

## 编译

```bash
# C# Overlay
dotnet build -c Release

# C++ DLL (需要 Visual Studio 2022 + MSBuild)
cd THKLogger
msbuild THKLogger.vcxproj -p:Configuration=Release -p:Platform=x64
```

## 项目结构

```
├── FatalisOverlay/          # C# WPF Overlay (主程序)
│   ├── Models/              # 数据模型 (ThkPathData, LogSession, AppConfig)
│   ├── Services/            # 业务逻辑 (ThkPathReader, ThkCycleLogger, DllInjector)
│   ├── MainWindow.xaml      # Overlay 悬浮窗
│   ├── SettingsWindow.xaml  # 设置窗口
│   └── LogViewerWindow.xaml # 日志查看器
├── THKLogger/               # C++ DLL (注入插件)
│   ├── dllmain.cpp          # Hook + 周期追踪 + 共享内存
│   └── deps/                # SafetyHook, Zydis
├── export_nack_map.py       # 从 .thk 生成节点映射
├── nack_map_full.json       # Fatalis 完整节点编号映射
└── THK节点追踪技术文档.md    # THK 技术参考资料
```

## 致谢

- [mhw-radar](https://github.com/stellarling/mhw-radar) — 内存偏移参考
- [HunterPie](https://github.com/Haato3o/HunterPie-v2) — 结构体定义参考
- [Leviathon](https://github.com/AsteriskAmpersand/Leviathon) — THK 反编译/编译工具

## 免责声明

本项目为社区制作的免费开源工具，仅供学习交流使用。

- 本工具**不修改任何游戏文件**，THKLogger.dll 仅读取运行时内存数据
- Monster Hunter: World 及所有相关商标、角色、素材版权归 **CAPCOM Co., Ltd.** 所有
- 本工具与 CAPCOM 无任何关联，亦未获得 CAPCOM 认可或授权
- 使用本工具产生的一切后果由用户自行承担

## License

MIT
