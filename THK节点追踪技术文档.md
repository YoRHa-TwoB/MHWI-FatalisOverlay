# MHW ProcessTHKSegment 节点追踪技术文档

> 目标版本：MHW Iceborne v421810 (Steam)
> 函数地址：`MonsterHunterWorld.exe + 0x133A9D0`

## 一、概述

怪物 AI 的每次决策都经过 `ProcessTHKSegment`，签名：

```cpp
int ProcessTHKSegment(cThinkMgr* thisptr, int* out, THK_Segment* segment);
```

- `thisptr`：怪物 AI 管理器
- `segment`：当前执行的行为树片段，函数直接传入，无需指针链
- 每帧可能调用数百次（每个 AI 决策节点一次）

---

## 二、结构体定义

### 2.1 THK_Segment（0x80 字节，pack(1)）

字段名以 Leviathon 反编译器源码 (`common/thk.py`) 为准。

| 偏移 | 字段 | 类型 | 说明 |
|------|------|------|------|
| +0x00 | `endRandom` | uint8 | 0x01=结束节点, 0/0x40/0x80/0xC0=概率分支 |
| +0x01 | `flowControl` | uint8 | 0x04=repeat, 0x08=return, 0x80=reset |
| +0x02 | `branchingControl` | uint8 | 0x02=if, 0x04=elif/else, 0x08=endif, 0x10=conclude |
| +0x08 | `functionType` | int32 | 条件/函数ID（核心字段） |
| +0x0C | `parameter1` | uint32 | 条件参数1 |
| +0x20 | `parameter2` | uint32 | 条件参数2 |
| +0x24 | `nodeEndingData` | uint32 | endRandom=1 时 = 10000 + nodeIndex |
| +0x28 | `extRefThkID` | uint32 | 外部调用 THK ID（55=Global），0xFFFFFFFF 表示未使用 |
| +0x2C | `extRefNodeID` | uint32 | 外部调用的 Node ID（`node.id` 字段），0xFFFFFFFF 表示未使用 |
| +0x30 | `localRefNodeID` | int64 | 本 THK 内跳转的 Binary Index，-1 表示未使用 |
| +0x4C | `actionID` | uint32 | 招式 ID（0=非招式片段，核心字段） |

### 2.2 THK_NodeInfo 与 THK_Header

```cpp
#pragma pack(push, 1)
struct THK_NodeInfo {
    THK_Segment* mNode;          // +0x00: Segment 数组指针
    uint32_t     mSegmentCount;  // +0x08: 本节点 Segment 数量
    uint32_t     mNodeExternalID;// +0x0C: 节点的二进制 ID（Leviathon: node.id）
};

struct THK_Header {
    char          mType[4];      // +0x00: "THK\0"
    uint16_t      mVersion;      // +0x04: 固定 40
    uint16_t      mNodeCount;    // +0x06: 节点总数（含空 padding 节点）
    uint32_t      mThkType;      // +0x08: THK 类型哈希
    uint32_t      mNull;         // +0x0C: 是否 Palico
    uint32_t      mMonsterID;    // +0x10: 怪物 ID（64 位，但低 32 位足够）
    uint32_t      mNull2;        // +0x14
    THK_NodeInfo* mNodeInfoList; // +0x18: Node 信息数组
};
#pragma pack(pop)
```

### 2.3 三套节点编号体系

同一节点在不同上下文中使用不同编号：

| 编号体系 | 来源 | 用途 |
|---------|------|------|
| **Binary Index** | `nodeList` 中从 0 开始的位置序号，**含空节点** | `GetIndices()` 返回值、`localRefNodeID` |
| **Binary ID** | `THK_NodeInfo.mNodeExternalID`，Capcom 分配的自定义 ID | `extRefNodeID`（跨 THK 引用） |
| **.nack 编号** | 过滤空节点后从 0 开始顺序重命名 | `.nack` 文件中的 `def node_000` |

**空节点（Padding Node）**：Capcom 在编译 THK 时插入的空节点，满足 `mNodeExternalID == 0`、仅 1 个 segment、该 segment 所有字段为默认值（`-1` 用于 ref 字段，`0` 用于其他字段）。Leviathon 反编译器过滤空节点后重新编号。

以 Fatalis（em013）为例：Combat_Main 二进制有 223 个节点（`nc=223`），过滤空节点后剩 193 个有效节点。Global 二进制有 155 个节点，过滤后剩 143 个。

### 2.4 Binary Index → .nack 映射

Leviathon 从原始 `.thk` 文件生成映射。以 Combat_Main 为例：

| Binary Index | .nack 编号 | 说明 |
|:---:|:---:|------|
| 0 | 0 | node_000 |
| 1 | — | 空节点（跳过） |
| 2 | 1 | node_001 |
| 77 | 65 | 空节点累积 12 个偏移 |
| 104 | 89 | 此段偏移变为 15 |

完整映射可通过 Leviathon 导出，或直接解析原始 `.thk` 文件按空节点规则过滤后生成。

---

## 三、定位 THK Header

### 从 Segment 地址往回搜索魔数

```cpp
uintptr_t segAddr = (uintptr_t)segment;

// 从 segment 地址往前，每 16 字节检查一次，最多回退 2MB
for (uintptr_t probe = (segAddr & ~0xF); probe > segAddr - 0x200000; probe -= 0x10) {
    if (!IsValidPtr(probe)) continue;

    uint32_t magic = ReadU32(probe);
    if (magic == 0x004B4854) {  // "THK\0" 小端表示
        uint16_t ver = ReadU16(probe + 4);
        uint16_t nc  = ReadU16(probe + 6);
        uintptr_t nodeList = ReadPtr(probe + 0x18);

        if (ver > 0 && ver <= 100 && nc > 0 && nc < 10000
            && IsValidPtr(nodeList)) {
            THK_Header* header = (THK_Header*)probe;
            break;
        }
    }
}
```

### 缓存策略

Combat_Main 和 Global 是不同的 THK 文件，在内存中可能相距不到 2MB。缓存会错误地将 Global segment 匹配到 Combat_Main header，导致 `GetIndices` 返回 -1。

**正确做法**：先尝试缓存；如果 `GetIndices` 返回 -1，强制重新扫描找到正确的 header。

---

## 四、获取 Node 编号：GetIndices

```cpp
void GetIndices(THK_Segment* seg, THK_Header* header,
                int* outNode, int* outSeg) {
    *outNode = -1; *outSeg = -1;

    uint16_t nodeCount = header->mNodeCount;
    THK_NodeInfo* nodeList = header->mNodeInfoList;

    for (uint16_t i = 0; i < nodeCount; i++) {
        THK_Segment* nodeStart = nodeList[i].mNode;
        uint32_t segCount = nodeList[i].mSegmentCount;

        for (uint32_t j = 0; j < segCount; j++) {
            if (&nodeStart[j] == seg) {
                *outNode = i;   // Binary Index（节点在列表中的位置，含空节点）
                *outSeg  = j;   // Segment Index（在此 Node 内的位置）
                return;
            }
        }
    }
}
```

**返回 -1**：segment 属于外部 THK（如 Global），在当前已缓存的 header 中找不到，需重新扫描 header。

**注意**：返回值是 Binary Index（含空节点），不是 `.nack` 编号。要对应 `.nack` 文件需要做空节点过滤映射。

---

## 五、.fand 文件（THK 索引，由 Leviathon 生成）

```
Combat_Main    = em013_00.nack @ 97793049
Combat_Enter   = em013_01.nack @ 53ACD122
Search         = em013_02.nack @ 0A3FC6CC
Discover       = em013_03.nack @ B45B36F5
Rage_Enter     = em013_07.nack @ 0A3FC6CC
Mount          = em013_11.nack @ 536F468B
Concealment    = em013_13.nack @ D5C2636C
Non_Combat_Main= em013_15.nack @ 0A3FC6CC
Non_Combat_Hit = em013_18.nack @ CDE4D8D3
Target_Lost    = em013_29.nack @ E68EC1AC
Global         = em013_55.nack @ 299E388A
```

`@` 后的十六进制数是 THK Header 中 `mThkType` 字段的值，用作版本标识，不是文件偏移。

### .nack 文件示例

```nack
def node_000
    >> Global.node_142          ← Seg 0: extRefThkID=55, extRefNodeID=142（Node ID）
    >> node_001                 ← Seg 1: localRefNodeID=node_001的BinaryIndex
    >> node_027                 ← Seg 2: localRefNodeID=node_027的BinaryIndex
    if self.target(4)           ← Seg 7: functionType=46, branchingControl=0x2, P1=4
        >> node_006             ← Seg 8: True 分支
    elif self.target(3)         ← Seg 9: functionType=46, branchingControl=0x4, P1=3
        >> node_006
    elif self.target(55)        ← Seg 11
        >> Global.node_004      ← Seg 12: extRef=55:4
    elif self.flying()          ← Seg 13: functionType=112
        >> node_008             ← Seg 14
    else                        ← Seg 15: functionType=2, branchingControl=0x4
        >> node_009             ← Seg 16
    endif
    reset                       ← Seg 17: flowControl=0x80
endf
```

### 映射规律（基于 Leviathon nackCall.py）

| 反编译语法 | branchingControl | functionType | 存储字段 |
|-----------|:---:|:---:|---------|
| `>> Global.node_NNN` | 0 | 2 | `extRefThkID=55`, `extRefNodeID=NNN`（**Node ID**）|
| `>> node_NNN` | 0 | 2 | `localRefNodeID=目标BinaryIndex` |
| `if function#XX(...)` | 0x2 | XX | P1/P2=参数 |
| `elif function#XX(...)` | 0x4 | XX | |
| `else` | 0x4 | 2 | |
| `endif` | 0x8 | — | |
| `endf` | 0 | — | `endRandom=1`, `nodeEndingData=10000+nodeIndex` |
| `conclude` | 0x10 | — | |
| `random (N)` | 0 | 0 | P1=N, branchingControl=0x1（内部） |
| `reset` | — | — | `flowControl=0x80` |
| `return` | — | — | `flowControl=0x08` |
| `repeat` | — | — | `flowControl=0x04` |
| `[RegisterVarN ++/--]` | — | 0x80+N | |
| `[RegisterVarN >= P1]` | 0x2 | 0x80+N | 寄存器比较作为 if 条件 |
| `-> fatalis.xxx(...)` | — | 2 | `actionID=xxx`（仅在 Global 中出现）|

**关键区别**：
- **`>> node_NNN`**（内部调用）：`.nack` 中的 `NNN` 不直接写入二进制。编译器将其解析为节点名 → 找对应节点 → 取 **Binary Index** → 写入 `localRefNodeID`。
- **`>> Global.node_NNN`**（外部调用）：编译器取目标节点的 **Binary ID**（`node.id`）→ 写入 `extRefNodeID`。

---

## 六、functionType 完整对照表

> 来源：Leviathon `default.fexty` + `em013_00.nack` 反编译

### 6.1 已命名函数

#### 距离检测

| functionType | .nack 写法 | P1 | P2 |
|:---:|------|----|----|
| 6 | `self.distance_3d_to_target().leq(P1)` | 阈值 | - |
| 7 | `self.distance_3d_to_target().gt(P1)` | 阈值 | - |
| 8 | `self.distance_2d_to_target().leq(P1)` | 阈值 | - |
| 9 | `self.distance_2d_to_target().gt(P1)` | 阈值 | - |
| 10 | `self.distance_3d_recalc_to_target().leq(P1)` | 阈值 | - |
| 11 | `self.distance_3d_recalc_to_target().gt(P1)` | 阈值 | - |
| 12 | `self.distance_2d_recalc_to_target().leq(P1)` | 阈值 | - |
| 13 | `self.distance_2d_recalc_to_target().gt(P1)` | 阈值 | - |
| 14 | `self.vertical_distance_to_target().leq(P1)` | 阈值 | - |
| 15 | `self.vertical_distance_to_target().gt(P1)` | 阈值 | - |
| 16 | `self.above_target()` / `self.below_target()` | 0=上 1=下 | - |
| 19 | `self.above_area()` / `self.below_area()` | 0=上 1=下 | - |

#### 角度检测

| functionType | .nack 写法 | P1 | P2 |
|:---:|------|----|----|
| 20 | `self.angle_2d_ccw_between(P1,P2)` | 最小 | 最大 |
| 21 | `self.angular_15(P1,P2)` | — | — |
| 22 | `self.angle_2d_cw_between(P1,P2)` | 最小 | 最大 |
| 23~27 | `self.angular_17~1B(P1,P2)` | — | — |

#### 目标选择

| functionType | .nack 写法 | P1 |
|:---:|------|----|
| 3 | `self.targetEnemy(P1)` | 1=random_player_or_cat, 2=current_ai_target, 13=last_attacker, 29=any_monster, 41=nearest_monster, 43=nearest_entity, 48=nearest_entity2, 49=unq_target, 50=random_player_or_cat2, 66=last_target, 75=nearest_entity3 |
| 4 | `self.targetUnknown(P1,P2)` | — |
| 5 | `self.targetArea(P1)` | 10=nearest_entrance, 12=global_center, 25=area_center, 26=area_aerial_center, 31=nearest_monster_area, 59=next_exit |

#### 怪物自身状态

| functionType | .nack 写法 | 说明 |
|:---:|------|------|
| 28 | `self.in_combat()` | 战斗中 |
| 29 | `self.alert_out_of_combat()` | 警戒（非战斗） |
| 30 | `self.enraged()` | 发怒中 |
| 31 | `self.fatigued()` | 疲劳中 |
| 32 | `self.poisoned()` | 中毒 |
| 33 | `self.defense_downed()` | 防御下降 |
| 34 | `self.miasmaed()` | 瘴气 |
| 35 | `self.hookable()` | 可钩爪 |
| 36 | `self.target_on_part(P1)` | 目标在指定部位 |
| 37 | `self.mounted()` | 被骑乘 |
| 38 | `self.mount_finisher_ready()` | 骑乘终结技就绪 |
| 39 | `self.mount_stabbed()` | 骑乘刺击 |
| 40 | `self.mount_staggered_twice()` | 骑乘两次硬直 |
| 42 | `self.target.pinned()` | 目标被钉住 |
| 44 | `self.hp_percent().leq(P2)` | HP 百分比 ≤P2 |
| 46 | `self.target(P1)` | 目标状态（5=helpless, 18=poisoned, 28=paralyzed, 29=stunned, 30=sleeping, 32~36=blighted, 43=miasmaed） |
| 47 | `self.target_is(P1)` | 目标怪物 ID |
| 112 | `self.flying()` | 飞行中 |
| 118 | `self.part(P1).is_broken(P2)` | 部位破坏检测 |

#### 其他已命名函数

| functionType | .nack 写法 | 说明 |
|:---:|------|------|
| 43 | `self.enrage_time_left().leq(P2)` / `self.fatigue_time_left().leq(P2)` | P1=0怒 1疲劳 |
| 55 | `self.quest_id(P1,P2)` | P1: 0=等号 1=≥ 2=> 3=≤ 4=< 5=≠ |
| 94 | `self.clearTarget()` | 清除目标 |
| 174 | `self.force_area_change()` | 强制换区 |
| 177 | `self.force_area_change2()` | 强制换区2 |
| 184 | `self.current_quest().is_rank(P1)` | 0=LR 1=HR 2=MR 3=AT |
| 191 | `self.in_map(P1).in_area(P2)` | 地图+区域判断 |
| 12288 | `self.heal(P2)` / `self.damage(P2)` | P1: 0=heal 1=heal_perdecmil 2=damage 3=damage_perdecmil |
| 12289 | `self.stamina.increase(P2)` 等 | — |
| 12290 | `self.enrage()` / `self.refresh_enrage()` | 0=怒 1=刷新 |
| 12291 | `self.corpseDuration(P2)` 等 | — |

### 6.2 未命名函数

| functionType | .nack 写法 |
|:---:|-----------|
| 45 | `function#2D(P1)` |
| 93 | `function#5D()` |
| 257 | `function#101()` / `function#101(1)` |
| 258 | `function#102(P1)` |
| 259 | `function#103()` / `function#103(1)` |
| 260 | `function#104()` / `function#104(1)` |
| 262 | `function#106()` / `function#106(1)` |
| 264 | `function#108()` |
| 268 | `function#10C()` |
| 269 | `function#10D()` |
| 272 | `function#110(P1)` |
| 273 | `function#111()` |
| 274 | `function#112(P1,P2)` |
| 275 | `function#113(P1,P2)` |

### 6.3 特殊值

| functionType | 含义 |
|:---:|------|
| 0 | 随机分支 / 寄存器操作 / 未归类 |
| 2 | `else` 分支 / 无条件跳转（默认 functionType） |
| 0x80 ~ 0xAB | 寄存器操作 |

### 6.4 寄存器操作

寄存器编号 = `functionType - 0x80`。

| 操作 | .nack 语法 |
|------|----------|
| 递增 | `[RegisterVarN ++]` |
| 递减 | `[RegisterVarN --]` |
| 赋值 | `[RegisterVarN := P1]` |
| 比较 | `if [RegisterVarN >= P1]` |

黑龙 Combat_Main 中声明了 15 个寄存器（RegisterVar0 ~ RegisterVar14），运行时值存储在 `cThinkEm + 0x640 + N*4`。

---

## 七、AoB 扫描

ProcessTHKSegment 定位：

```cpp
static const unsigned char kPat[] = {
    0x48, 0x89, 0x00, 0x00, 0x00, 0x56, 0x57, 0x41, 0x54, 0x48,
    0x00, 0x00, 0x00, 0x48, 0x8B, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x49, 0x8B, 0xF0, 0x48, 0x8B, 0xDA, 0x48, 0x8B, 0xF9, 0x41
};
static const unsigned char kMask[] = {
    0xFF, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
};

uintptr_t match = ScanAoB(kPat, kMask, sizeof(kPat));
uintptr_t fn = match - 5;  // 减去 5 得函数入口
// v421810 结果: gameBase + 0x133A9D0
```

---

## 八、注意事项

1. **Seg 编号从 0 开始**，对照 .nack 时空行和注释不算 segment
2. **三套编号体系**不要混淆：Binary Index（`GetIndices` 返回）≠ Binary ID（`extRefNodeID`）≠ .nack 编号
3. **`>> node_NNN`**（内部调用）在二进制中用 `localRefNodeID` 存目标节点的 **Binary Index**
4. **`>> Global.node_NNN`**（外部调用）在二进制中用 `extRefNodeID` 存目标节点的 **Binary ID**（`node.id`）
5. **空节点过滤**：Capcom 在 THK 中插入空 padding 节点，`.nack` 编号是不含空节点的顺序号。需用 Leviathon 的 `checkEmptyNode` 逻辑做映射
6. **THK header 缓存**：Combat_Main 和 Global 在内存中可能 < 2MB 间距，`GetIndices` 返回 -1 时需强制重新扫描
7. **`branchingControl=0x4`** 覆盖 elif 和 else，需结合 functionType 判断（functionType=2 且 branchingControl=0x4 是 else）
8. **`ExtRefThkID=55`** 指向 Global，`0xFFFFFFFF` 表示无外部引用

---

## 附录 A：空节点判定标准（Leviathon checkEmptyFields）

```cpp
bool IsEmptySegment(THK_Segment* seg) {
    if (seg->endRandom > 1)         return false;
    if (seg->flowControl != 0)      return false;
    if (seg->branchingControl != 0) return false;
    if (seg->functionType != 0)     return false;
    if (seg->parameter1 != 0)       return false;
    if (seg->parameter2 != 0)       return false;
    if (seg->nodeEndingData / 10000 > 1) return false;
    if (seg->extRefThkID != 0xFFFFFFFF)  return false;
    if (seg->extRefNodeID != 0xFFFFFFFF) return false;
    if (seg->localRefNodeID != -1)       return false;
    if (seg->actionID != 0)         return false;
    // +0x03~0x07, +0x10~0x1C, +0x38~0x48, +0x50~0x7C: all must be 0
    return true;
}

bool IsEmptyNode(THK_NodeInfo* info) {
    return info->mNodeExternalID == 0
        && info->mSegmentCount == 1
        && IsEmptySegment(info->mNode);
}
```

---

## 附录 B：Fatalis (em013) Binary Index/ID → .nack 完整映射

> 由 `export_nack_map.py` 从原始 `.thk` 文件生成。用于将 `GetIndices()` 返回的 Binary Index 或 segment 中的 Node ID 转换为 `.nack` 编号。

### B.1 Combat_Main（223→193，30 个空节点）

**Binary Index → .nack**（`cm_idx`）：

| BinIdx | Nack | | BinIdx | Nack | | BinIdx | Nack | | BinIdx | Nack |
|:--:|:--:|---|:--:|:--:|---|:--:|:--:|---|:--:|:--:|
| 0 | 0 | | 52 | 44 | | 108 | 93 | | 167 | 146 |
| 2 | 1 | | 53 | 45 | | 109 | 94 | | 169 | 147 |
| 3 | 2 | | 54 | 46 | | 110 | 95 | | 170 | 148 |
| 4 | 3 | | 55 | 47 | | 111 | 96 | | 171 | 149 |
| 5 | 4 | | 56 | 48 | | 112 | 97 | | 172 | 150 |
| 6 | 5 | | 57 | 49 | | 113 | 98 | | 173 | 151 |
| 8 | 6 | | 58 | 50 | | 115 | 99 | | 175 | 152 |
| 9 | 7 | | 60 | 51 | | 116 | 100 | | 176 | 153 |
| 10 | 8 | | 61 | 52 | | 117 | 101 | | 177 | 154 |
| 11 | 9 | | 62 | 53 | | 118 | 102 | | 178 | 155 |
| 13 | 10 | | 63 | 54 | | 119 | 103 | | 179 | 156 |
| 14 | 11 | | 65 | 55 | | 121 | 104 | | 181 | 157 |
| 15 | 12 | | 66 | 56 | | 122 | 105 | | 182 | 158 |
| 16 | 13 | | 67 | 57 | | 123 | 106 | | 183 | 159 |
| 17 | 14 | | 68 | 58 | | 124 | 107 | | 184 | 160 |
| 18 | 15 | | 70 | 59 | | 125 | 108 | | 185 | 161 |
| 19 | 16 | | 71 | 60 | | 127 | 109 | | 186 | 162 |
| 20 | 17 | | 72 | 61 | | 128 | 110 | | 188 | 163 |
| 21 | 18 | | 74 | 62 | | 129 | 111 | | 189 | 164 |
| 22 | 19 | | 75 | 63 | | 130 | 112 | | 190 | 165 |
| 23 | 20 | | 76 | 64 | | 131 | 113 | | 191 | 166 |
| 24 | 21 | | 77 | 65 | | 132 | 114 | | 192 | 167 |
| 26 | 22 | | 78 | 66 | | 133 | 115 | | 194 | 168 |
| 27 | 23 | | 79 | 67 | | 135 | 116 | | 195 | 169 |
| 28 | 24 | | 81 | 68 | | 136 | 117 | | 196 | 170 |
| 29 | 25 | | 82 | 69 | | 137 | 118 | | 197 | 171 |
| 30 | 26 | | 83 | 70 | | 138 | 119 | | 198 | 172 |
| 32 | 27 | | 84 | 71 | | 139 | 120 | | 200 | 173 |
| 33 | 28 | | 85 | 72 | | 140 | 121 | | 201 | 174 |
| 34 | 29 | | 86 | 73 | | 141 | 122 | | 202 | 175 |
| 35 | 30 | | 87 | 74 | | 142 | 123 | | 203 | 176 |
| 36 | 31 | | 88 | 75 | | 143 | 124 | | 204 | 177 |
| 37 | 32 | | 89 | 76 | | 145 | 125 | | 205 | 178 |
| 38 | 33 | | 91 | 77 | | 146 | 126 | | 206 | 179 |
| 39 | 34 | | 92 | 78 | | 147 | 127 | | 207 | 180 |
| 41 | 35 | | 93 | 79 | | 148 | 128 | | 208 | 181 |
| 42 | 36 | | 94 | 80 | | 149 | 129 | | 209 | 182 |
| 43 | 37 | | 95 | 81 | | 150 | 130 | | 211 | 183 |
| 45 | 38 | | 96 | 82 | | 151 | 131 | | 212 | 184 |
| 46 | 39 | | 97 | 83 | | 152 | 132 | | 213 | 185 |
| 47 | 40 | | 98 | 84 | | 153 | 133 | | 214 | 186 |
| 49 | 41 | | 99 | 85 | | 154 | 134 | | 215 | 187 |
| 50 | 42 | | 100 | 86 | | 155 | 135 | | 217 | 188 |
| 51 | 43 | | 101 | 87 | | 157 | 136 | | 218 | 189 |
|   |    | | 103 | 88 | | 158 | 137 | | 219 | 190 |
|   |    | | 104 | 89 | | 159 | 138 | | 220 | 191 |
|   |    | | 105 | 90 | | 160 | 139 | | 221 | 192 |
|   |    | | 106 | 91 | | 161 | 140 | | | |
|   |    | | 107 | 92 | | 162~166 | 141~145 | | | |

缺失索引（1,7,12,25,31,40,44,48,59,64,69,73,80,90,102,114,120,126,134,144,156,168,174,180,187,193,199,210,216）为空节点。

### B.2 Global（155→143，12 个空节点）

**Binary Index → .nack**（`global_idx`）：

| BinIdx | Nack | | BinIdx | Nack | | BinIdx | Nack | | BinIdx | Nack |
|:--:|:--:|---|:--:|:--:|---|:--:|:--:|---|:--:|:--:|
| 0 | 0 | | 42 | 36 | | 82 | 73 | | 120 | 111 |
| 2 | 1 | | 43 | 37 | | 83 | 74 | | 121 | 112 |
| 3 | 2 | | 44 | 38 | | 84 | 75 | | 122 | 113 |
| 4 | 3 | | 45 | 39 | | 85 | 76 | | 123 | 114 |
| 6 | 4 | | 46 | 40 | | 86 | 77 | | 124 | 115 |
| 7 | 5 | | 47 | 41 | | 87 | 78 | | 125 | 116 |
| 8 | 6 | | 48 | 42 | | 88 | 79 | | 126 | 117 |
| 9 | 7 | | 49 | 43 | | 89 | 80 | | 127 | 118 |
| 10 | 8 | | 50 | 44 | | 90 | 81 | | 128 | 119 |
| 11 | 9 | | 51 | 45 | | 91 | 82 | | 129 | 120 |
| 12 | 10 | | 53 | 46 | | 92 | 83 | | 130 | 121 |
| 14 | 11 | | 54 | 47 | | 93 | 84 | | 131 | 122 |
| 15 | 12 | | 55 | 48 | | 94 | 85 | | 132 | 123 |
| 16 | 13 | | 56 | 49 | | 95 | 86 | | 133 | 124 |
| 18 | 14 | | 58 | 50 | | 96 | 87 | | 134 | 125 |
| 19 | 15 | | 59 | 51 | | 97 | 88 | | 135 | 126 |
| 20 | 16 | | 60 | 52 | | 98 | 89 | | 136 | 127 |
| 21 | 17 | | 61 | 53 | | 99 | 90 | | 137 | 128 |
| 22 | 18 | | 62 | 54 | | 100 | 91 | | 138 | 129 |
| 23 | 19 | | 63 | 55 | | 101 | 92 | | 139 | 130 |
| 24 | 20 | | 64 | 56 | | 102 | 93 | | 140 | 131 |
| 25 | 21 | | 65 | 57 | | 103 | 94 | | 141 | 132 |
| 26 | 22 | | 66 | 58 | | 104 | 95 | | 143 | 133 |
| 28 | 23 | | 67 | 59 | | 105 | 96 | | 144 | 134 |
| 29 | 24 | | 68 | 60 | | 106 | 97 | | 145 | 135 |
| 30 | 25 | | 69 | 61 | | 107 | 98 | | 146 | 136 |
| 32 | 26 | | 71 | 62 | | 108 | 99 | | 147 | 137 |
| 33 | 27 | | 72 | 63 | | 109 | 100 | | 148 | 138 |
| 34 | 28 | | 73 | 64 | | 110 | 101 | | 150 | 139 |
| 35 | 29 | | 74 | 65 | | 111 | 102 | | 151 | 140 |
| 36 | 30 | | 75 | 66 | | 112 | 103 | | 152 | 141 |
| 37 | 31 | | 76 | 67 | | 113 | 104 | | 154 | 142 |
| 38 | 32 | | 77 | 68 | | 114 | 105 | | | |
| 39 | 33 | | 78 | 69 | | 115 | 106 | | | |
| 40 | 34 | | 79 | 70 | | 116 | 107 | | | |
| 41 | 35 | | 80~81 | 71~72 | | 117~119 | 108~110 | | | |

缺失索引（1,5,13,17,27,31,52,57,70,142,149,153）为空节点。

### B.3 Node ID → .nack

`extRefNodeID` 使用的是 **Node ID**（`mNodeExternalID` 字段），需通过以下映射转为 `.nack` 编号。

#### Combat_Main（`cm_id`）

| Node ID | .nack | | Node ID | .nack | | Node ID | .nack | | Node ID | .nack |
|:--:|:--:|---|:--:|:--:|---|:--:|:--:|---|:--:|:--:|
| 1 | 0 | | 228 | 54 | | 321~325 | 94~98 | | 263 | 156 |
| 162 | 1 | | 250 | 55 | | 235~239 | 99~103 | | 289~298 | 157~166 |
| 164 | 2 | | 168~170 | 56~58 | | 219~223 | 104~108 | | 299~309 | 167~177 |
| 320 | 3 | | 209~211 | 59~61 | | 186 | 109 | | 336~340 | 178~182 |
| 207 | 4 | | 251 | 62 | | 184 | 110 | | 310~319 | 183~192 |
| 208 | 5 | | 173~177 | 63~67 | | 187~191 | 111~115 | | | |
| 165 | 6 | | 230~234 | 68~72 | | 240~244 | 116~120 | | | |
| 163 | 7 | | 270~271 | 73~74 | | 279~282 | 121~124 | | | |
| 166 | 8 | | 274~275 | 75~76 | | 245~247 | 125~127 | | | |
| 167 | 9 | | 214~216 | 77~79 | | 268 | 128 | | | |
| 265 | 10 | | 267 | 80 | | 248~249 | 129~130 | | | |
| 183 | 11 | | 217~218 | 81~82 | | 283~287 | 131~135 | | | |
| 288 | 12 | | 272~273 | 83~84 | | 185 | 136 | | | |
| 264 | 13 | | 276~278 | 85~87 | | 192~196 | 137~141 | | | |
| 205~206 | 14~15 | | 252 | 88 | | 331~335 | 142~146 | | | |
| 266 | 16 | | 178~182 | 89~93 | | 254~262 | 147~155 | | | |
| 269 | 17 | | | | | | | | | |
| 356~364 | 18~26 | | | | | | | | | |
| 344~354 | 27~40 | | | | | | | | | |
| 197~229 | 41~51 | | | | | | | | | |
| 224~226 | 51~53 | | | | | | | | | |

#### Global（`global_id`）

| Node ID | .nack | | Node ID | .nack | | Node ID | .nack | | Node ID | .nack |
|:--:|:--:|---|:--:|:--:|---|:--:|:--:|---|:--:|:--:|
| 1 | 0 | | 229 | 23 | | 204 | 31 | | 207 | 42 |
| 162 | 1 | | 235 | 24 | | 198~199 | 32~33 | | 203 | 43 |
| 163 | 2 | | 283 | 25 | | 273 | 34 | | 262~263 | 44~45 |
| 164 | 3 | | 195 | 26 | | 205 | 35 | | 257~260 | 46~49 |
| 165 | 4 | | 224 | 27 | | 201 | 36 | | 167 | 50 |
| 300~305 | 5~10 | | 196 | 28 | | 222 | 37 | | 208 | 51 |
| 166 | 11 | | 227~228 | 29~30 | | 206 | 38 | | 169 | 52 |
| 230~234 | 12~16 | | | | | 202 | 39 | | 289~291 | 53~55 |
| 253 | 17 | | | | | 272 | 40 | | 210 | 56 |
| 251~252 | 18~19 | | | | | 223 | 41 | | 168 | 57 |
| 254~256 | 20~22 | | | | | | | | 170~171 | 58~59 |
| 249 | 60 | | 175 | 69 | | 188 | 80 | | 248 | 122 |
| 295 | 61 | | 179 | 70 | | 193 | 81 | | 245 | 123 |
| 286 | 62 | | 220 | 71 | | 215 | 82 | | 296 | 124 |
| 299 | 63 | | 218 | 72 | | 211~212 | 83~84 | | 194 | 125 |
| 307 | 64 | | 181 | 73 | | 274~275 | 85~86 | | 225~226 | 126~127 |
| 172~173 | 65~66 | | 288 | 74 | | 282 | 87 | | 281 | 128 |
| 298 | 67 | | 184~186 | 75~77 | | 310 | 88 | | 261 | 129 |
| 308 | 68 | | 297 | 78 | | 209 | 89 | | 278~280 | 130~132 |
|   |    | | 309 | 79 | | 242 | 90 | | 270~271 | 133~134 |
|   |    | | | | | 174 | 91 | | 264 | 135 |
|   |    | | | | | 287 | 92 | | 266 | 136 |
|   |    | | | | | 177~178 | 93~94 | | 268 | 137 |
|   |    | | | | | 176 | 95 | | 267 | 138 |
|   |    | | | | | 180 | 96 | | 292~294 | 139~141 |
|   |    | | | | | 221 | 97 | | 306 | 142 |
|   |    | | | | | 219 | 98 | | | |
|   |    | | | | | 182 | 99 | | | |
|   |    | | | | | 189~192 | 100~103 | | | |
|   |    | | | | | 216 | 104 | | | |
|   |    | | | | | 213~214 | 105~106 | | | |
|   |    | | | | | 217 | 107 | | | |
|   |    | | | | | 284 | 108 | | | |
|   |    | | | | | 250 | 109 | | | |
|   |    | | | | | 247 | 110 | | | |
|   |    | | | | | 183 | 111 | | | |
|   |    | | | | | 285 | 112 | | | |
|   |    | | | | | 237 | 113 | | | |
|   |    | | | | | 276~277 | 114~115 | | | |
|   |    | | | | | 238~241 | 116~119 | | | |
|   |    | | | | | 243~244 | 120~121 | | | |
```
