# MHWI Fatalis Overlay

Monster Hunter World: Iceborne Fatalis (黑龙) 专用数据 Overlay。实时显示血量、下压值、部位破坏、异常状态、发怒倒计时、软化计时等战斗信息。

## 功能

- 🩸 血量 + 转阶段标记 (78% / 50%)
- 🔶 下压值 (P1/P2/P3 自动适配)
- 🦴 九部位独立显示 (血量 + 硬直次数 + 破坏等级)
- 💎 软化白条 (头/胸+手/腿)
- ☠ 毒/麻/眠/爆/晕/减气/骑乘 分别勾选
- ⚡ 发怒积蓄 + 倒计时
- 🧠 AI 决策距离/角度
- ⏱ 任务计时
- ⚙ 实时生效的设置窗口

## 运行要求

- Windows 10/11 x64
- [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Monster Hunter: World (Iceborne)

## 使用方法

1. 下载 [最新 Release](https://github.com/jiyetwob/MHWI-FatalisOverlay/releases)
2. 解压到任意文件夹
3. 双击 `FatalisOverlay.exe`
4. 在设置窗口勾选需要的显示项
5. 打开 MHW，进黑龙任务自动显示

## 编译

```bash
dotnet build -c Release
```

## 致谢

- [mhw-radar](https://github.com/stellarling/mhw-radar) — 内存偏移参考、黑龙招式名称对应表
- [HunterPie](https://github.com/Haato3o/HunterPie-v2) — 结构体定义参考

## 免责声明

本项目为社区制作的免费开源工具，仅供学习交流使用。

- 本工具**不修改任何游戏文件**，仅读取游戏运行时内存数据
- Monster Hunter: World 及所有相关商标、角色、素材版权归 **CAPCOM Co., Ltd.** 所有
- 本工具与 CAPCOM 无任何关联，亦未获得 CAPCOM 认可或授权
- 使用本工具产生的一切后果由用户自行承担

## License

MIT
