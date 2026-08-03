---
name: "unity-scene-layout-generator"
description: "Generates 3-5 differentiated Unity 2D scene layout schemes by only modifying object Transforms (position/rotation/scale) without adding/deleting objects. Invoke when user needs to quickly generate multiple level layouts from an existing scene, or asks for scene rearrangement, level layout variants, or difficulty-adjusted stage designs."
---

# Unity 2D 场景布局生成器

本Skill用于基于现有Unity 2D场景，快速生成多款差异化关卡布局方案，**严格遵守「只改Transform、不增删物体」** 的硬性约束。

---

## 一、硬性约束（MUST READ - 禁止违反）

### 1.1 物体操作约束
- **禁止新增**任何GameObject（禁止Instantiate预制体、禁止新建空物体）
- **禁止删除**任何GameObject
- **禁止替换**资源（禁止改Mesh、Sprite、Material、Prefab引用等）
- **仅允许修改Transform组件的3个属性**：
  - `position`（x, y, z；2D场景通常只改x,y，z保持原值）
  - `rotation`（eulerAngles，2D通常只改z轴旋转）
  - `scale`（x, y, z；2D通常只改x,y，z保持1）

### 1.2 固定物体约束
标记为【固定物体】的对象，其Transform**完全不变**，包括：
- 玩家出生点（Player / PlayerSpawn / StartPoint）
- 场景边界墙体（Boundary / Wall_Left / Wall_Right / Wall_Top / Wall_Bottom / Ground主地面）
- 终点/目标点（Goal / EndPoint / FinishFlag / Exit）
- FailDetector / 死亡检测区
- MainCamera（通常固定，除非用户明确允许）
- 用户输入中明确标记为 `isFixed: true` 的任何物体

### 1.3 碰撞规避约束
- **任意两个物体的AABB包围盒（或碰撞体边界）禁止重叠**，最小间隙 >= 2像素（或0.02 Unity单位）
- 平台类物体下方/上方需留有足够玩家通过空间（至少1.5倍玩家高度）
- 禁止出现：陷阱嵌入平台、敌人卡入墙体、收集物埋入地面

### 1.4 地面贴合约束（PHYSICS_PUZZLE 专用，CRITICAL）
- **所有可移动的platform类物体必须贴近地面或堆叠在其他平台上方**，禁止悬空漂浮
- 物体底部距离地面（或支撑平台顶部）的间隙不得超过 0.3 Unity单位
- 若物体rotation.z != 0，需计算旋转后的最低接触点，确保仍贴近地面/支撑物
- **禁止出现**：物体底部悬空超过0.5单位无支撑、物体漂浮在半空中不接触地面/其他平台
- 验证方法：对每个platform物体计算 `bottomY = pos.y - bounds.sizeY*scale.y/2`，若无其他物体的topY（`pos.y + bounds.sizeY*scale.y/2`）与之接近（差值<0.3），则该物体必须直接放在地面上（bottomY ≈ groundY）

### 1.5 可支撑性约束
- 每个非装饰类物体（platform/enemy/collectible/trap）**必须有支撑**：要么直接放在地面上，要么放在另一个platform物体顶部
- 收集物/敌人放在平台上时，其AABB底部与平台AABB顶部的间隙 < 0.2单位
- 违反示例：星星悬空在空中、敌人漂浮在无平台支撑的位置

---

## 二、输入信息规范

执行本Skill时，从用户处收集以下三类输入：

### 2.1 输入①：场景物体清单（JSON数组）
```json
[
  {
    "name": "Platform_01",
    "hierarchyPath": "Level1/GameBlocks/Platform_01",
    "category": "platform | enemy | collectible | trap | decoration | fixed",
    "isFixed": false,
    "bounds": { "sizeX": 2.0, "sizeY": 0.5, "sizeZ": 1.0 },
    "currentTransform": {
      "position": { "x": 0, "y": 0, "z": 0 },
      "rotation": { "x": 0, "y": 0, "z": 0 },
      "scale":    { "x": 1, "y": 1, "z": 1 }
    },
    "colliderType": "BoxCollider2D | CircleCollider2D | PolygonCollider2D | None"
  }
]
```
**category枚举**：
- `platform`：平台（玩家可站立/跳跃的方块、长条、斜面）
- `enemy`：敌人（巡逻怪、飞行怪、固定炮台等威胁）
- `collectible`：收集物（金币、星星、宝石、钥匙、道具）
- `trap`：陷阱（尖刺、熔岩、电锯、毒气、凹陷坑）
- `decoration`：装饰（纯视觉，无碰撞：云朵、草、蘑菇、背景树）
- `fixed`：固定物体（归到1.2节的任何一类，isFixed强制为true）

### 2.2 输入②：游戏配置
```json
{
  "gameType": "platformer | puzzle | physics_puzzle | roguelite | top_down_shooter | runner",
  "coreGameplay": "简短描述核心玩法，如：玩家控制弹弓发射方块到终点，途中需要撞击星星收集并避开尖刺",
  "difficulty": "easy | normal | hard",
  "playerSize": { "sizeX": 0.5, "sizeY": 0.8 },
  "playerJumpHeight": 3.0,
  "playerMoveSpeed": 5.0
}
```

### 2.3 输入③：活动范围边界
```json
{
  "boundary": {
    "minX": -10.0, "maxX": 10.0,
    "minY": 0.0,   "maxY": 8.0
  },
  "groundY": 0.5
}
```
说明：所有可移动物体的包围盒中心点必须落在boundary内，且不与边界墙体穿插。

---

## 三、布局设计核心算法

### 3.1 第一步：划分固定参考系
1. 提取所有 `isFixed=true` 的物体，构成「固定锚点集」
2. 计算玩家出生点 `Spawn` 坐标 → 终点 `Goal` 坐标的主路径向量
3. 将活动区域沿主路径方向划分为 **Start段（前25%）→ Mid段（中间50%）→ End段（后25%）**
4. **关键：读取原始场景物体的y值范围**，确定物体贴地分布的y坐标基准区间

### 3.2 第二步：平台布局策略（按gameType）

#### Platformer（横版跳跃）：
- **梯度难度原则**：平台高度差从Start段到End段逐步增大
- **跳跃可达校验**：相邻平台水平间距 <= `playerJumpHeight * 0.8`，垂直高度差 <= `playerJumpHeight * 0.6`
- 平台排列模式：
  - 楼梯式（简单）：等距阶梯上升/下降
  - 锯齿式（普通）：左右错开跳跃
  - 浮动群岛式（困难）：分散孤立平台，间隙大
  - 螺旋塔式（困难）：向上盘旋，容错极低

#### Physics_Puzzle（物理谜题，如弹弓/AngryBirds类）—— ★重点优化：
**核心原则：所有平台必须贴地堆叠，构成障碍墙，禁止悬空**

- **障碍墙构建**：
  - 平台从地面（groundY）向上堆叠，构成1-3层的障碍物群
  - 障碍物高度不超过 `3 * 单个平台高度`（避免完全封死弹道）
  - 障碍物宽度方向需有间隙，允许小球从间隙穿过或从顶部越过
  - 多个障碍物之间形成"通道"或"迷宫"，引导小球弹道

- **排列模式**（按难度）：
  - **简单**：稀疏矮墙（1层），障碍物间距大，留有明显直通路径
  - **普通**：交错高墙（2层），障碍物左右交错，需反弹1-2次才能通过
  - **困难**：密集迷宫墙（3层），障碍物紧密排列，需多次反弹才能找到通路

- **关键布局规则**：
  1. 所有platform物体底部必须接触地面或另一platform顶部
  2. 同一列平台可以垂直堆叠（下层topY ≈ 上层bottomY）
  3. 不同列平台之间水平间距 >= `playerSize.sizeX * 3`（允许小球穿过）
  4. 障碍物群的总宽度不超过活动范围的60%（留出可达终点的空间）
  5. 终点旗帜附近必须有可落脚的平台（小球能到达的地面区域）

- **弹道可达性校验**：
  - 从弹弓位置向终点方向，模拟小球抛物线弹道
  - 至少存在一条弹道路径：小球发射后能撞到障碍墙上反弹，最终到达终点
  - 障碍墙高度必须允许小球以合理角度越过（发射角30°~60°时可达顶部）
  - 若弹道路径被完全封死 → 降低障碍高度或增大间距

#### Puzzle（解谜）：
- 平台/开关/门形成逻辑链路
- 收集物往往需要按特定顺序触发才能获得

### 3.3 第三步：收集物布置策略（Physics Puzzle专用）
- 收集物**不允许悬空**，必须放在：
  1. 障碍物顶部（通过反弹弹上去才能拿到）
  2. 障碍物后方/夹缝中（小球撞墙反弹时顺带拿到）
  3. 地面上障碍物之间的间隙中（直接滚过去拿到）
- 收集物bottomY ≈ 支撑平台的topY（差值 < 0.15）
- Easy：50%在地面间隙，30%在1层障碍顶，20%在2层障碍顶
- Normal：30%在地面间隙，40%在1-2层障碍顶，30%在反弹路径死角
- Hard：仅20%在地面，40%在障碍顶，40%藏在反弹路径末端死角

### 3.4 第四步：敌人布置策略
- **Easy**：敌人数量 × 0.6，仅在Mid-End段各放1-2只巡逻怪，巡逻范围小，不堵路口
- **Normal**：敌人数量不变，Start段0，Mid段2-3只巡逻，End段1只守终点附近
- **Hard**：敌人数量 × 1.3，Start段门口即有威胁，Mid段巡逻+飞行混编，End段堵关键路径
- 敌人必须放在平台顶部（不允许悬空），敌人bottomY ≈ 平台topY
- 相邻敌人视野不重叠（Easy）/ 部分重叠（Normal）/ 完全覆盖（Hard）

### 3.5 第五步：陷阱布置策略
- **密度系数**：Easy=0.5x, Normal=1.0x, Hard=1.8x
- 陷阱必须布置在「玩家必然经过的判定点」：平台边缘、跳跃落点、狭长通道两端
- Easy：陷阱稀疏，周围留有≥1个安全绕行路线
- Normal：陷阱中等密度，绕行路线存在但需要判断
- Hard：陷阱密集，几乎无安全路，需要精准操作时机通过
- **绝对禁止**：陷阱堵死出生点到终点的**所有**通路（必须至少有一条通路）
- **Physics Puzzle专项**：陷阱放在障碍墙间隙的地面上，逼迫小球精准反弹

### 3.6 第六步：装饰物布置
- 装饰物（decoration）无碰撞，允许放置在任何位置
- 用于遮挡空白、丰富视觉层次
- 放在平台边缘（草/蘑菇）、背景层（云/树）、终点附近（花/旗）
- 装饰物禁止遮挡关键UI/HUD区域
- **装饰特判**：category为decoration的物体（如grass）不受地面贴合约束，可放在任意位置

---

## 四、碰撞检测与可行性校验（必须执行）

### 4.1 AABB重叠检测
对每对物体 (A, B) 计算：
```
Ax1 = A.pos.x - A.bounds.sizeX*A.scale.x/2
Ax2 = A.pos.x + A.bounds.sizeX*A.scale.x/2
Ay1 = A.pos.y - A.bounds.sizeY*A.scale.y/2
Ay2 = A.pos.y + A.bounds.sizeY*A.scale.y/2
Bx1, Bx2, By1, By2 = 同理
重叠 = (Ax1 < Bx2 - gap) && (Ax2 > Bx1 + gap) && (Ay1 < By2 - gap) && (Ay2 > By1 + gap)
其中 gap = 0.02 (2像素安全距)
```
若任意一对非装饰物重叠 → **必须重新调整位置直到无重叠**

### 4.2 旋转AABB检测
对rotation.z != 0的物体，计算旋转后的AABB：
```
对8个顶点分别绕中心旋转rotation.z角度，取旋转后坐标的min/max
```
旋转后AABB用于精确碰撞检测，避免误判

### 4.3 地面贴合检测（PHYSICS_PUZZLE 强制）
对每个platform物体：
```
bottomY = pos.y - bounds.sizeY * scale.y / 2
检查：bottomY ≈ groundY (±0.3) 
   或 bottomY ≈ 某其他platform的topY (±0.3)
若均不满足 → 该物体悬空，必须调整位置
```

### 4.4 可支撑性检测（强制）
对每个非decoration物体：
```
检查其底部下方是否存在支撑物（地面或另一platform）
支撑条件：支撑物topY ≈ 物体bottomY (±0.3) 且 X轴范围有重叠
若无支撑 → 必须调整
```

### 4.5 通关可行性校验（CRITICAL）
#### Platformer类型：
1. 从Spawn出发，以玩家尺寸+跳跃能力，能否通过平台跳跃/移动到达Goal
2. 是否存在至少一条路径：路径上每个跳跃步的间距≤跳跃可达阈值
3. 是否路径上每一步都不会被陷阱100%覆盖（即站在落点不会立即死亡，Hard可留「需要走位躲开」）
4. 若任何校验失败 → 调整平台/陷阱位置，重新校验

#### Physics Puzzle类型（弹道模拟）：
1. 模拟小球从弹弓发射（角度30°~60°，力度范围），计算抛物线弹道
2. 检查弹道是否能到达终点附近（允许±2单位误差）
3. 检查障碍物是否能引导反弹到终点（至少一条反弹路径）
4. 检查所有收集物是否在弹道经过的区域（允许反弹后到达）
5. 若弹道被完全封死 → 降低障碍高度或增大间隙
6. 模拟公式（简化）：
   ```
   v₀ = 发射初速度（估算值）
   θ = 发射角度（30°~60°）
   g = 重力加速度（估算值）
   x(t) = v₀ * cos(θ) * t
   y(t) = v₀ * sin(θ) * t - 0.5 * g * t²
   ```
7. 至少存在3组不同(角度,力度)组合能通关

### 4.6 极端布局排除项（禁止出现）
- ❌ 出生点悬空或被陷阱/平台卡死
- ❌ 终点周围无落脚点、被四面墙封死
- ❌ 唯一通路中间有不可跳过的缺口（缺口>跳跃可达*1.2）
- ❌ 陷阱覆盖100%的必经平台表面
- ❌ 敌人堵死100%的通路宽度且无绕过可能
- ❌ 【Physics Puzzle专用】障碍墙完全封死弹弓到终点的所有弹道
- ❌ 【Physics Puzzle专用】所有收集物都被放在不可达位置
- ❌ 【通用】平台/物体悬空无支撑

---

## 五、输出方案要求

### 5.1 方案数量与差异化
- 一次输出 **3～5套** 布局方案
- 每套方案必须至少有以下维度的差异化：
  - 主路径走向不同（S型 / Z型 / 直线阶梯 / 螺旋上攀 / 分支迷宫）
  - 平台组合模式不同（长条主平台 / 碎小浮岛 / 斜面反弹阵 / 高低错落 / 障碍墙堆叠）
  - 陷阱/敌人/收集物的分布热点不同

### 5.2 难度映射
- 若用户选 `difficulty: easy` → 3套方案：Easy-A, Easy-B, Easy-C（均为简单，策略不同）
- 若用户选 `difficulty: normal` → 方案1(Easy预览), 方案2(Normal核心), 方案3(Hard挑战)
- 若用户选 `difficulty: hard` → 方案1(Normal过渡), 方案2(Hard核心), 方案3(Lunatic噩梦)

### 5.3 Physics Puzzle难度梯度
- **Easy**：障碍物1层高度，间距大，收集物多在地面，弹道简单
- **Normal**：障碍物2层高度，交错排列，收集物需1次反弹，弹道中等
- **Hard**：障碍物3层高度，密集排列，收集物需多次反弹，弹道复杂

---

## 六、输出格式（严格JSON，禁废话）

**必须**按以下结构输出，禁止markdown、禁止前后缀说明文字、禁止闲聊，只返回纯净可解析JSON：

```json
{
  "generatedAt": "2026-08-03T09:22:00Z",
  "inputSummary": {
    "gameType": "platformer",
    "difficulty": "normal",
    "objectCount": 32,
    "fixedCount": 6
  },
  "schemes": [
    {
      "schemeName": "方案A-阶梯引导式",
      "difficultyTag": "easy | normal | hard",
      "designRationale": "一句话布局思路：前半段等距阶梯引导玩家熟悉跳跃，中段左右锯齿切换节奏，末段平直冲刺到终点。陷阱仅在平台边缘单处放置，留有充裕绕行空间。收集物沿阶梯主线分布，顺手可拿。",
      "objects": [
        {
          "name": "Platform_01",
          "hierarchyPath": "Level1/GameBlocks/Platform_01",
          "isFixed": false,
          "newTransform": {
            "position": { "x": -5.0, "y": 1.2, "z": 0 },
            "rotation": { "x": 0, "y": 0, "z": 0 },
            "scale":    { "x": 1.0, "y": 1.0, "z": 1 }
          }
        },
        {
          "name": "Player",
          "hierarchyPath": "Level1/Player",
          "isFixed": true,
          "newTransform": {
            "position": { "x": -8.0, "y": 0.5, "z": 0 },
            "rotation": { "x": 0, "y": 0, "z": 0 },
            "scale":    { "x": 1, "y": 1, "z": 1 }
          }
        }
      ]
    }
  ],
  "validationReport": {
    "collisionCheckPassed": true,
    "groundAdherenceCheckPassed": true,
    "supportCheckPassed": true,
    "solvabilityCheckPassed": true,
    "notes": "所有方案均通过AABB无重叠检测；所有platform物体均贴地或堆叠支撑；主路径跳跃可达性逻辑模拟通过，不存在绝对死局。"
  }
}
```

### 6.1 字段说明
| 字段 | 必要性 | 说明 |
|---|---|---|
| `schemes[*].schemeName` | 必填 | 中文方案名，格式：方案X-四字风格描述 |
| `schemes[*].difficultyTag` | 必填 | easy/normal/hard 三选一 |
| `schemes[*].designRationale` | 必填 | 50～200字中文，讲清楚布局梯度、路径形状、陷阱/敌人/收集物的布置策略意图 |
| `schemes[*].objects[*].name` | 必填 | 与输入清单name完全一致，用于匹配物体 |
| `schemes[*].objects[*].hierarchyPath` | 必填 | 与输入一致，用于Unity Scene层级定位 |
| `schemes[*].objects[*].isFixed` | 必填 | 固定物体保持true，其newTransform必须与输入currentTransform完全一致 |
| `schemes[*].objects[*].newTransform` | 必填 | position/rotation/scale完整写出，**所有数值精确到小数点后2位** |
| `validationReport` | 必填 | 碰撞+地面贴合+可支撑性+通关检测的总结报告 |

### 6.2 validationReport字段说明
| 字段 | 说明 |
|---|---|
| `collisionCheckPassed` | AABB重叠检测是否通过 |
| `groundAdherenceCheckPassed` | 地面贴合检测是否通过（PHYSICS_PUZZLE必填） |
| `supportCheckPassed` | 可支撑性检测是否通过（PHYSICS_PUZZLE必填） |
| `solvabilityCheckPassed` | 通关可行性检测是否通过 |
| `notes` | 检测详情说明 |

### 6.3 数值精度要求
- 所有坐标/旋转/缩放统一保留 **2位小数**，如 `x: 3.14`
- 旋转单位：**欧拉角（度）**，不是弧度
- 固定物体的newTransform必须与输入的currentTransform **逐字段完全相同**（不能有浮点差）

---

## 七、执行流程（AI操作步骤）

每次调用本Skill时，严格按以下步骤执行：

### 7.1 收集输入
1. 向用户索要 2.1物体清单、2.2游戏配置、2.3活动边界（若用户未提供，先询问，不要瞎编）
2. **读取原始场景物体的y值分布**，确定groundY附近的物体密集区域作为布局参考基准

### 7.2 提取固定物
- 将isFixed=true的物体先抄到每个方案的objects数组，坐标原封不动

### 7.3 生成平台骨架（PHYSICS_PUZZLE专用流程）
1. **计算地面基准**：以groundY为基准，向上堆叠platform物体
2. **构建障碍墙**：
   - 按难度确定障碍墙层数（Easy:1层, Normal:2层, Hard:3层）
   - 每个障碍墙由1-3个platform垂直堆叠而成
   - 障碍墙之间留出间隙（>= playerSize * 3）
3. **确保贴地**：每个platform的bottomY必须等于groundY或下方platform的topY
4. **确保支撑**：每个platform必须有地面或下层platform支撑
5. **弹道预校验**：从弹弓位置模拟弹道，确保至少一条通路到终点

### 7.4 填入陷阱/敌人/收集物
- 按3.3-3.5的难度策略布置
- 每个物体必须有支撑（放在地面或平台上）
- 每加一类物体跑一次AABB碰撞检测 + 地面贴合检测 + 支撑检测

### 7.5 装饰物点缀
- 最后摆decoration（不受贴地约束）

### 7.6 六轮自检（PHYSICS_PUZZLE强制）
1. ① AABB无重叠（含旋转AABB）
2. ② 所有platform物体贴地或有支撑
3. ③ 所有非decoration物体有支撑
4. ④ 弹道可达性存在（至少3组角度力度组合）
5. ⑤ 收集物可达（在弹道经过区域）
6. ⑥ 无4.6极端布局

### 7.7 组装JSON
- 按第六章格式输出，3～5套方案，只输出纯JSON，**绝对不要加任何解释性文字、代码块标记、markdown符号**

---

## 八、常见游戏类型布局模板速查

| 游戏类型 | 主路径建议 | 平台占比 | 陷阱占比 | 收集物占比 | 敌人占比 |
|---|---|---|---|---|---|
| Platformer跳跃 | 从左到右 / 从下到上 | 45% | 15% | 25% | 15% |
| Physics Puzzle物理弹弓 | 从左下发射→右上目标 | 35%（障碍墙） | 20% | 30% | 15%（可破坏） |
| Puzzle解谜 | 多分支汇聚终点 | 40% | 10% | 20% | 10%（守卫） |
| Top-Down俯视角 | 入口→出口迷宫 | 30%（墙体） | 20% | 25% | 25% |
| Runner跑酷 | 单向强制滚动 | 50%（障碍） | 25% | 20% | 5% |

注：占比 = 该category物体数 / 所有可移动物体总数。Easy/Hard模式按3.3-3.5的密度系数修正。

### 8.1 Physics Puzzle物体位置速查表
| 物体类型 | y值范围（相对groundY） | x值范围 | 约束 |
|---|---|---|---|
| 底层platform | groundY ~ groundY+0.5 | -boundary.maxX*0.7 ~ boundary.maxX*0.7 | bottomY=groundY |
| 中层platform | groundY+0.5 ~ groundY+2.5 | 同上 | bottomY=下层topY |
| 顶层platform | groundY+2.5 ~ groundY+4.0 | 同上 | bottomY=中层topY |
| 收集物(地面) | groundY+0.1 ~ groundY+0.5 | 障碍物间隙中 | 有支撑 |
| 收集物(障碍顶) | 障碍顶+0.1 ~ 障碍顶+0.5 | 障碍物正上方 | 有支撑 |
| 旗帜/终点 | groundY ~ groundY+0.5 | boundary右侧 | 有支撑 |

---

## 九、示例调用提示词（供用户参考）

当用户不知道怎么触发本Skill时，可以引导他们按下面的模板提问：

> "请使用unity-scene-layout-generator Skill，基于我的Level1场景生成4套布局：
> ① 物体清单（附JSON），② 游戏类型：物理弹弓(Physics Puzzle)，核心玩法：弹弓发射小球收集星星并到达终点旗帜，目标难度：Normal，③ 活动范围 X∈[-12,12]、Y∈[0,10]，地面Y=0.5。请严格按JSON格式输出。"

---

**本SKILL到此结束。执行时，请严格遵守以上每一条约束，特别是：只改Transform、禁悬空、禁重叠、保支撑、保通关、输出纯JSON。**
