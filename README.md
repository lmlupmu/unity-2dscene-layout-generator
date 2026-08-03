# Unity 2D 场景布局生成器 (Unity Scene Layout Generator)

一个基于 Trae Skill 的 Unity 2D 场景布局自动生成工具,通过修改现有场景物体的 Transform(位置/旋转/缩放),快速生成多套差异化关卡布局方案,**不新增、不删除、不替换任何物体资源**。

## 核心特性

- **零资源破坏**:仅修改 Transform 属性,不增删 GameObject,不替换资源
- **多套方案**:一次生成 3-5 套差异化布局,覆盖不同难度
- **物理弹弓优化**:专为 Physics Puzzle 游戏设计,自动构建贴地障碍墙
- **智能校验**:自动检测碰撞重叠、地面贴合、可支撑性、通关可行性
- **Unity 集成**:提供 Editor 工具一键导出/应用布局

## 目录结构

```
.
├── .trae/
│   └── skills/
│       └── unity-scene-layout-generator/
│           └── SKILL.md                  # Skill 定义文件(核心规则)
├── Editor/
│   ├── SceneObjectExporter.cs            # 场景物体导出器(Unity Editor 工具)
│   └── SceneLayoutApplier.cs             # 布局方案应用器(Unity Editor 工具)
├── Level1_objects.json                  # 示例:Level1 场景物体清单
├── README.md                            # 项目说明
├── LICENSE                              # MIT 开源协议
└── .gitignore                           # Git 忽略规则
```

## 快速开始

### 1. 安装 Unity Editor 工具

将 `Editor/` 文件夹下的两个 C# 脚本复制到你的 Unity 项目 `Assets/Editor/` 目录下:

- [SceneObjectExporter.cs](Editor/SceneObjectExporter.cs):导出当前场景物体清单
- [SceneLayoutApplier.cs](Editor/SceneLayoutApplier.cs):应用生成的布局方案

### 2. 安装 Skill

将 `.trae/skills/unity-scene-layout-generator/` 整个文件夹复制到你的 Trae 工作区 `.trae/skills/` 目录下。

### 3. 使用流程

```
[Unity] Tools → Scene Layout → Export Scene Objects to JSON
    ↓ 生成 Level1_objects.json
[Trae] 调用 unity-scene-layout-generator Skill
    ↓ 输入:物体清单 + 游戏配置 + 边界
[Trae] AI 生成布局方案 JSON
    ↓ 输出 3-5 套方案
[Unity] Tools → Scene Layout → Apply Layout Scheme from JSON
    ↓ 粘贴 JSON,解析方案
[Unity] 校验贴地/支撑 → 应用到当前场景 或 另存为新关卡
```

## 支持的游戏类型

| 游戏类型 | 描述 | 布局策略 |
|---|---|---|
| Platformer | 横版跳跃 | 阶梯/锯齿/浮岛/螺旋塔 |
| Physics Puzzle | 物理弹弓 | 贴地障碍墙堆叠 |
| Puzzle | 解谜 | 多分支逻辑链路 |
| Top-Down | 俯视角 | 入口-出口迷宫 |
| Runner | 跑酷 | 单向强制滚动障碍 |

## 核心约束

### 硬性约束
1. **只改 Transform**:position / rotation / scale
2. **禁增删物体**:不新增、不删除、不替换资源
3. **固定物体不变**:Player、Slingshot、Ground、Camera 等

### Physics Puzzle 专用约束
1. **地面贴合**:所有 platform 物体必须贴地或堆叠在另一 platform 上
2. **可支撑性**:每个非装饰物体必须有支撑(地面或平台顶部)
3. **弹道可达**:至少存在 3 组(角度,力度)组合能通关

## 输出格式

生成纯净 JSON,包含:

```json
{
  "generatedAt": "ISO 时间戳",
  "inputSummary": { "gameType": "...", "difficulty": "...", ... },
  "schemes": [
    {
      "schemeName": "方案A-XXX式",
      "difficultyTag": "easy|normal|hard",
      "designRationale": "布局思路说明",
      "objects": [
        {
          "name": "Platform_01",
          "hierarchyPath": "...",
          "isFixed": false,
          "newTransform": {
            "position": { "x": 0, "y": 0, "z": 0 },
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
    "notes": "..."
  }
}
```

## 调用示例

在 Trae 中向 AI 提问:

```
请调用 unity-scene-layout-generator Skill,基于我的 Level1 场景生成 4 套布局方案:

① 物体清单 JSON:(粘贴 Level1_objects.json 内容)

② 游戏配置:
{
  "gameType": "physics_puzzle",
  "coreGameplay": "玩家通过弹弓发射小球,通过障碍墙反弹收集星星并到达终点旗帜",
  "difficulty": "normal",
  "playerSize": { "sizeX": 0.4, "sizeY": 0.4 },
  "playerJumpHeight": 0.0,
  "playerMoveSpeed": 0.0
}

③ 活动范围边界:
{
  "boundary": { "minX": -12, "maxX": 12, "minY": -5.15, "maxY": 7 },
  "groundY": -5.15
}

请严格按第六章格式输出纯净 JSON,不要任何多余文字。
所有 platform 物体必须贴地堆叠构成障碍墙,禁止悬空。
```

## 技术栈

- **Trae Skill**:AI 工作流定义
- **Unity 2D**:游戏引擎
- **C#**:Editor 工具开发
- **JSON**:数据交换格式

## 适用场景

- 独立游戏开发者快速生成关卡布局
- 游戏关卡设计师迭代设计方案
- Unity 2D 物理弹弓类游戏开发
- 关卡难度梯度设计参考

## 许可证

[MIT License](LICENSE) - 可自由使用、修改、分发

## 贡献

欢迎提 Issue 和 PR 改进 Skill 规则或 Unity 工具。
