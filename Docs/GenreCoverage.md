# 品类覆盖：基座能力缺口与验证玩法

目标：让 SPF 基座支撑尽量多的 2D 单机品类。原则不变：面向移动端，计算要 CPU 缓存友好（SoA + Jobs/Burst），渲染要省 GPU 带宽，对局中 0 GC。

做法是每补一类能力，就写一个最小但完整的玩法去"用"它。每个玩法都有三样东西：
- EditMode 规则测试；
- PlayMode UI / 渲染测试，GPU 驱动和数据纹理两种 Tier 各跑一次，并截图；
- 必要时加基准，在 CI 的 Mac 上实测。

## 1. 品类 → 所需能力 → 验证玩法

| 品类 | 关键能力 | 原基座情况 | 本轮补充 | 验证玩法 |
| --- | --- | --- | --- | --- |
| 大量实体动作（贪吃蛇 .io） | SoA 表、两级空间网格、GPU 驱动渲染 | ✅ | — | SnakeFoundation |
| 俯视角 ARPG | 精灵动画、技能 / Buff、受击、伤害数字、存档 | ✅（上一轮） | — | RpgFoundation |
| 弹幕 / 割草（幸存者类） | 每帧上万个短命实体的生灭；弹幕发射器；超大批量精灵 | ❌ 实体创建走句柄注册表，太重 | **池化表**、**弹幕发射器**、**32 字节打包精灵** | SurvivorFoundation |
| 平台跳跃 | 瓦片 AABB 扫掠、单向平台、土狼时间 / 跳跃缓冲、相机死区 | ❌ | **TileMap.MoveBox**、**PlatformerMotor**、**FollowCamera2D 死区 / 边界** | PlatformerFoundation |
| 塔防 / 策略 | 网格寻路、放塔后可达性校验、波次表、拖拽 / 双指缩放 | 只有流场 | **GridAStar** + 批量 Job、**WaveSchedule**、**GestureTracker** | DefenseFoundation |
| 消除 / 回合制解谜 | 按需推进的回合时钟、撤销 / 重做、补间动画、滑动手势 | ❌ 只有固定步长 | **ManualClock**、**SnapshotHistory**、**TweenPlayer**、手势滑动 | PuzzleFoundation |
| 所有品类 | 发热降频、省电、GC 监控 | Snake 有私有实现 | **FrameGovernor**（基座通用） | 接入全部新玩法 |

还没覆盖的品类以 §5 为准。

## 2. 新增基座能力（按层）

### Runtime / World
- **池化表** `TableSpec.Pooled()`：没有句柄，也不走注册表。
  - 生成：`SimWorld.Spawn` / `SpawnRange` 直接在表尾追加行。
  - 回收：Job 里写隐藏的 `DeadFlags` 字节列；下一 Tick 开始时由 Burst `CompactJob` 一次性压实（尾部搬移，保持连续）。
  - 子弹、粒子类实体从此不必付出句柄和随机访问的代价。
- **行重排** `SimWorld.SortRows` + `GatherJob`，配合 `Morton.Encode` 按空间顺序排表，让邻近实体在内存里相邻。实测结果见 §3，这里不先下结论。
- `IColumn.Pointer` / `ElementSize`：列的裸指针，给压实、重排等 memcpy 类 Job 使用。

### Runtime / Session
- **`SimSession.ManualClock` + `RequestTicks(n)`**：回合制玩法不按固定频率空转。只在玩家落子时推进 Tick，没有操作时 0 Tick、0 CPU。
- **`SnapshotHistory`**：基于已有的快照二进制做撤销 / 重做环形历史，每回合记录一次。

### L1 Simulation
- **`TileMapView.MoveBox`**：按轴分离扫掠，支持单向平台；另有 `BoxTouches` / `BoxGrounded`。
- **`GridAStar`**：
  - 寻路草稿区用时间戳复用，无需每次清零；二叉堆 + 八方向启发；禁止切角；`Smooth` 做路径拉直；`Reachable` 做放塔可达性校验。
  - `AStarBatchJob` 让多个单位并行寻路。
- **`Morton`**：2D Z-order 编码。

### L2 Gameplay
- **`PatternEmitter` + `IBulletSink`**：弹幕模式（环形、螺旋、瞄准扇形，多臂 + 旋转组合），可序列化配置。
- **`PlatformerMotor` / `PlatformerTuning`**：加速度、可变跳高（松键截断）、土狼时间、跳跃缓冲、蹬墙滑 / 蹬墙跳。
- **`WaveSchedule` / `WaveGroup`**：波次表（时间轴 + 组 + 间隔）。

### Shell
- **`GestureTracker` / `GestureInput`**：点击、拖拽、双指缩放、滑动方向，统一处理鼠标和触屏。
- **`HoldButton.ConsumePress`**：只消费一次的按下沿，用于跳跃这类按下动作。
- **`FollowCamera2D` 死区 / 边界 / Clamp**。
- **`FrameGovernor`**（`SPF.Shell.Performance`），见 §4。

### Presentation
- **`PackedSprite`（32 字节 / 实例）**：同时用于 GPU 驱动和数据纹理两种 Tier。
  - 结构是两个 `uint4`：位置用 float；尺寸和旋转用 half；UV 用 unorm16；颜色用 RGBA8（rgb 取值 0..2，可以过曝做受击闪白）；另有 8 位闪白。
  - 原格式是 64 字节，这样实例上传带宽减半。
  - 数据纹理层改用 RGBA8 纹理，每个精灵占 8 个 texel。原因：Metal 上 RGBA32UI 的采样支持不可靠；RGBA8 在 GLES3 上普遍可用。
- `SpriteBatch.Reserve` / `Trim`：Burst Job 直接往批次里写打包实例，主线程不逐个 Add。
- **`TweenPlayer` + `Easing`**：数组化的补间。每个对象有多个通道（位置、缩放），可以排队、加延迟，本身不产生 GC。

## 3. 实测数据（CI：Mac M5 Pro，Unity 2022.3 编辑器）

### 幸存者模拟：3000 敌人 + 约 2.8 万子弹（`perf-survivor.txt`）

| 项 | 数值 |
| --- | --- |
| 流水线 Tick 均值 | 0.39 ms |
| Tick p95 | 0.63 ms |
| 主线程调度 | 0.13 ms |
| 最重系统 EnemySystem（串行剖析） | 0.19 ms |
| 子弹系统 | 0.035 ms |
| 碰撞 | 0.056 ms |

如实记录：**敌人表 Morton 重排没有收益**，几次测量的加速比是 0.89×～0.98×。原因是这个负载的敌人每 Tick 都在移动，而网格构建本身已经是按格子的计数排序。重排的收益抵不过 gather 一遍的成本。

因此 Survivor 默认关闭重排（`ReorderInterval = 0`），基座保留这个能力。它适合"大量静态或慢速实体 + 随机空间查询"的场景，用之前先跑基准。

### 幸存者渲染压测：约 1.3 万精灵 / 帧（`perf-survivor-render.txt`）

| Tier | 帧时间 | 实例上传 |
| --- | --- | --- |
| GPU 驱动（StructuredBuffer） | 16.67 ms（锁 60 FPS，有余量） | ≈ 209 KiB / 帧 |
| 数据纹理（RGBA8，GLES3 路径） | 16.67 ms | ≈ 209 KiB / 帧 |

32 字节 × 1.3 万 ≈ 400 KiB 是理论满载上传量。屏幕外剔除后实际约 209 KiB。如果仍用 64 字节格式，同样场景要上传约 420 KiB / 帧。

### 0 GC

- 新增 EditMode 测试，验证稳态 Tick 不分配托管内存：
  - `SvGameTests.SteadyStateTicksDoNotAllocate`
  - `PlTests.SteadyStateTicksDoNotAllocate`
  - `TdTests.SteadyStateWaveTicksDoNotAllocate`
- Survivor 渲染压测的报告里会附上 `FrameGovernor` 统计的 GC 帧数。这一项包含 HUD 文字刷新，所以只记录，不断言。
- 三消是回合制，每步的少量托管分配不在热路径上，没有加这个约束。

## 4. FrameGovernor：移动端帧调控

`SPF.Shell.Performance.FrameGovernor` 从 Snake 私有的自适应画质里提炼出来，做成了基座组件。它有三部分：

1. **自适应画质（`FrameBudget`，纯逻辑，可单测）**
   - 平滑后的帧时间持续超预算 2 秒就降一级，余量充足 6 秒才升一级（滞回）。超过 250 ms 的卡顿（加载、切回前台）不计入。
   - 每一级对应一个 URP 渲染缩放（1.0 / 0.9 / 0.8 / 0.7），直接降低填充率和带宽。
   - 级别变化会通过 `LevelChanged` 事件广播，玩法可以自行削减表现开销。
   - 模拟 Tick 频率从不改变，保证确定性。
   - Snake 的 `AdaptiveQualityController` 已改为复用 `FrameBudget`。
2. **空闲降帧（`IdleThrottle`）**
   - 没有输入、玩法也没调用 `KeepAwake()` 时，1.5 秒后把帧率从 60 降到 30。
   - 三消：棋盘静止时降帧，动画播放期间保持 60。PlayMode 测试有断言覆盖。
   - 塔防：建造阶段降帧，波次进行中保持 60。
   - 这是回合制和策略类在手机上省电、降温最直接的手段。
3. **GC 监控**：读取 Profiler 的 "GC Allocated In Frame" 计数（编辑器和开发包），提供给测试和性能面板。

## 5. 仍未覆盖、建议后续补充的能力

| 能力 | 适用品类 | 说明 |
| --- | --- | --- |
| 刚体物理（关节、旋转碰撞） | 物理解谜、愤怒的小鸟类 | 目前只有 AABB / 圆形。建议接 Unity 2D 物理，或加一个确定性的简化刚体层，不在基座里自研完整物理。 |
| 光照 / 法线贴图精灵 | 氛围类横版 | 目前精灵着色是不受光的。可以在打包格式里给法线图集预留 UV，费用落在片元着色。 |
| 文本 / 剧情 / 对话系统 | 视觉小说、RPG 剧情 | 目前只有 UGUI Text，缺少剧情脚本和本地化表。 |
| 骨骼动画 | 动作、格斗 | 目前是帧动画。骨骼动画建议 GPU 蒙皮，数据可以复用数据纹理通道。 |
| 关卡编辑器 | 平台跳跃、解谜 | 关卡现在写在代码里（`PlLevels`），可以加一个 ScriptableObject 编辑器。 |
| Adaptive Performance（温度 API） | 全部 | 目前只靠帧时间推断发热。三星、iOS 可以接 Adaptive Performance 包拿到温度等级。 |

## 6. 运行与查看

- 每个玩法都可以在空场景里挂对应的 `*GameBootstrap` 直接运行：`SvGameBootstrap` / `PlGameBootstrap` / `TdGameBootstrap` / `M3GameBootstrap`。
- 截图和性能报告：CI 推送到 `ci-screenshots/<分支>`，包括：
  - 截图：`survivor-*.png`、`platformer-*.png`、`defense-*.png`、`puzzle-*.png`；
  - 报告：`perf-survivor*.txt`。
- 本地快速验证（无 Unity）：`Tools/DotnetHarness/run.sh`，会编译全部程序集并运行 EditMode 测试。
