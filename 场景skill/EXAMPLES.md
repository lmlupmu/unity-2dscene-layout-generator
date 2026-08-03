# 使用示例 (Examples)

本目录收录 unity-scene-layout-generator Skill 的典型使用示例,帮助你快速上手。

---

## 示例 1:Physics Puzzle 物理弹弓游戏

### 输入

**游戏配置**:
```json
{
  "gameType": "physics_puzzle",
  "coreGameplay": "玩家通过Slingshot弹弓发射Player小球,通过障碍墙反弹和物理惯性,收集所有星星(collectible),最终到达终点Flag。",
  "difficulty": "normal",
  "playerSize": { "sizeX": 0.4, "sizeY": 0.4 },
  "playerJumpHeight": 0.0,
  "playerMoveSpeed": 0.0
}
```

**活动边界**:
```json
{
  "boundary": { "minX": -12, "maxX": 12, "minY": -5.15, "maxY": 7 },
  "groundY": -5.15
}
```

**物体清单**:见 [Level1_objects.json](../Level1_objects.json)

### 输出方案概览

AI 会生成 4 套差异化方案:

| 方案 | 难度 | 风格 | 障碍墙层数 | 弹道特点 |
|---|---|---|---|---|
| 方案A | easy | 稀疏矮墙式 | 1层 | 直射可达 |
| 方案B | normal | 交错高墙式 | 2层 | 1-2次反弹 |
| 方案C | normal | 横向通道式 | 2层平行 | 穿越通道 |
| 方案D | hard | 密集迷宫墙 | 3层 | 多次反弹 |

### 方案 A 物体坐标示例(部分)

```json
{
  "name": "elm_01",
  "hierarchyPath": "GameBlocks/elm_01",
  "isFixed": false,
  "newTransform": {
    "position": { "x": -5, "y": -4.45, "z": 0 },
    "rotation": { "x": 0, "y": 0, "z": 0 },
    "scale":    { "x": 1, "y": 1, "z": 1 }
  }
}
```

**说明**:`y: -4.45` 是贴地坐标(groundY=-5.15 + sizeY/2=0.7 = -4.45),确保物体底部接触地面。

---

## 示例 2:Platformer 横版跳跃游戏

### 输入

```json
{
  "gameType": "platformer",
  "coreGameplay": "玩家控制角色左右移动+跳跃,收集金币并到达终点旗帜,途中避开尖刺陷阱和巡逻敌人",
  "difficulty": "normal",
  "playerSize": { "sizeX": 0.5, "sizeY": 0.8 },
  "playerJumpHeight": 3.0,
  "playerMoveSpeed": 5.0
}
```

**活动边界**:
```json
{
  "boundary": { "minX": -15, "maxX": 15, "minY": 0, "maxY": 10 },
  "groundY": 0.5
}
```

### 预期输出方案

| 方案 | 难度 | 风格 | 平台排列 |
|---|---|---|---|
| 方案1 | easy | 阶梯引导式 | 等距阶梯上升 |
| 方案2 | normal | 锯齿切换式 | 左右错开跳跃 |
| 方案3 | hard | 浮岛分散式 | 间隙大,容错低 |

---

## 示例 3:Top-Down 俯视角迷宫

### 输入

```json
{
  "gameType": "top_down_shooter",
  "coreGameplay": "玩家从入口进入迷宫,击败敌人收集钥匙,找到出口逃离",
  "difficulty": "hard",
  "playerSize": { "sizeX": 0.6, "sizeY": 0.6 },
  "playerJumpHeight": 0.0,
  "playerMoveSpeed": 4.0
}
```

### 输出特点

- 平台物体构成迷宫墙体
- 陷阱布置在拐角和必经路径
- 敌人分布在迷宫关键节点
- 收集物(钥匙)藏在死胡同

---

## 使用步骤详解

### Step 1: 导出场景物体清单

在 Unity 中打开目标场景,执行:

```
菜单栏 → Tools → Scene Layout → Export Scene Objects to JSON
```

保存为 `LevelX_objects.json`。

### Step 2: 调用 Skill 生成布局

在 Trae 中向 AI 发送请求(参考 [示例 1](#示例-1physics-puzzle-物理弹弓游戏)),附上:
- 物体清单 JSON
- 游戏配置
- 活动边界

### Step 3: 应用布局方案

在 Unity 中执行:

```
菜单栏 → Tools → Scene Layout → Apply Layout Scheme from JSON
```

操作流程:
1. 粘贴 AI 返回的纯净 JSON 到文本框
2. 点击「▶ 解析方案」
3. 下拉选择目标方案
4. (Physics Puzzle) 设置 groundY,点击「🔍 校验贴地/支撑」
5. 点击「🔄 应用到当前场景」 或 「💾 另存为新场景 LevelX」

### Step 4: 测试与微调

- 运行游戏测试通关路径
- 检查物体是否悬空(校验报告会提示)
- 必要时手动微调个别物体坐标

---

## 常见问题

### Q1: 生成的物体悬空怎么办?

**A**: 检查 `validationReport.groundAdherenceCheckPassed` 是否为 `true`。若为 `false`,说明方案存在悬空物体,需重新生成或在 Unity 中手动调整。

### Q2: 弹道被障碍墙完全封死无法通关?

**A**: 这是 Skill 明令禁止的极端布局(见 SKILL.md §4.6)。请重新生成方案,或在请求中强调「降低障碍高度或增大间距」。

### Q3: 固定物体被移动了?

**A**: 检查 JSON 中 `isFixed: true` 的物体,其 `newTransform` 必须与输入的 `currentTransform` 完全一致。若不一致,说明 Skill 违反了硬性约束。

### Q4: 如何为新关卡生成布局?

**A**: 打开新关卡场景 → 导出物体清单 → 调用 Skill 生成方案 → 另存为新场景。

---

## 进阶用法

### 自定义难度映射

修改 SKILL.md §5.2 的难度映射规则,例如:

```
若用户选 difficulty: normal → 4套方案:Easy-A, Normal-B, Normal-C, Hard-D
```

### 添加新游戏类型

在 SKILL.md §3.2 添加新 gameType 的布局策略,例如:

```
#### Tower_Defense(塔防):
- 路径从入口蜿蜒到基地
- 平台沿路径两侧布置炮台位
- 陷阱布置在路径关键拐点
```

### 扩展校验逻辑

在 SKILL.md §4 添加新校验项,例如:

```
### 4.7 性能校验
- 同屏可见物体数不超过 50
- 重叠渲染层不超过 3 层
```
