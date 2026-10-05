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
| 物理解谜（愤怒的小鸟类） | 旋转刚体、堆叠稳定、关节、防穿透、冲击判定 | ❌ 只有 AABB / 圆形 | **PhysicsWorld2D**（第二轮，§5） | SlingFoundation |
| 横版氛围 / 夜间关卡 | 法线贴图、点光源 | ❌ 精灵不受光 | **受光精灵**（第二轮） | PlatformerFoundation 第 3 关 |
| 动作 / 格斗 | 骨骼动画、动画混合、骨骼挂载的命中判定 | ❌ 只有帧动画 | **骨骼剪纸动画**（第二轮） | BrawlerFoundation |
| 视觉小说 / 剧情 | 对话脚本、分支变量、本地化、打字机、存读档 | ❌ | **对话系统 + 本地化**（第二轮） | StoryFoundation |
| 关卡制作 | 关卡资源、可视化编辑 | ❌ 关卡写在代码里 | **TileLevelAsset + 编辑器**（第二轮） | PlatformerFoundation |

第二轮补充见 §5，剩余未覆盖的见 §6。

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
  - Mac 实测（1.3 万精灵压测，240 帧）：GPU 驱动 Tier 0 帧分配；数据纹理 Tier 只有 1 帧分配，共 122 字节（来自 HUD 文字）。
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

## 5. 第二轮：补齐缺口 + 对象池与减少动态分配

### 5.1 对象池与零分配文本
- **`ObjectPool<T>` / `ListPool<T>`**（Contracts）：预热后玩法中不再调用工厂，`Created` 计数可以在测试里断言"不再增长"。
- **`ComponentPool<T>`**（Shell）：场景对象池。回收时只是隐藏并挂到池节点下，玩法中不 Instantiate / Destroy。对话选项按钮就用它。
- **`TextBuilder`**：往字符缓冲里拼整数、定点小数和字符串，不分配。
- **`BufferText`**：直接从字符缓冲生成 UGUI 网格，**任何时候都不产生 string**。
  - 支持换行、自动折行（中日韩文字可逐字断行）、九种对齐，`MaxVisible` 用来做打字机效果。
  - 字体图集在每个字号下预热一次可打印 ASCII，防止游戏中途扩图集触发所有 Text 重建。
  - 图集重建的回调延后到 UGUI 重建循环之外处理。
- 所有玩法 HUD 中每帧变化的数字都已换成 `BufferText`。

**实测（Mac，PlayMode 稳态窗口，`perf-gc.txt`）**

| 场景 | 分配帧 |
| --- | --- |
| 格斗、塔防波次、平台跳跃、弹弓坍塌（两种渲染层） | 均为 0 帧 |
| 幸存者 | 游戏代码（模拟、渲染、HUD，含升级）稳态为 0 帧；开着音效回调时，180 帧里约有 1～4 帧各 164 B（编辑器内，见下文） |
| 故事打字机（第二句起） | 0 帧 |

排查中发现并修掉的分配点：
- HUD 每帧做字符串插值；
- 幸存者在每次捡宝石时重建隐藏的升级界面文字；
- 测试机器人用 `Array.IndexOf` 查枚举数组，在 Mono 下会装箱，每帧 164 B；
- Mono 在首次 JIT 时才创建 lambda 里的字符串字面量，导致"不分配"断言误报，测试改为先预热再测量。
- 升级选项文字改为一次性预生成（约 40 个字符串）；
- `BufferText` 改用独立的动态字体实例：字形被淘汰后图集重建时，不再通知所有 UGUI Text 重新生成网格（那条路径在 UGUI 内部会分配）。

**定位方法**：先在测试里逐帧记录分配字节数，再依次关掉反馈回调（音效）、HUD、测试机器人，分别测量。结论：

- 游戏代码本身（模拟、渲染、HUD）为 0；
- 剩下的少量分配只在开启音效回调时出现，而 C# 侧的播放与发声槽池都没有分配，所以指向编辑器里的 `AudioSource` 路径；
- 真机 Player 中是否也存在，需要在设备上复测。

### 5.2 发热与电量（不依赖 Adaptive Performance 包）
- `ThermalMonitor` 每 3 秒轮询一次：
  - iOS：通过一个 .mm 插件读取 `thermalState` 和低电量模式；
  - Android：通过 JNI 调用 `PowerManager` 读取 `getCurrentThermalStatus`（API 29 及以上）和省电模式；
  - 另外读取电量与充电状态。
- `FrameGovernor` 据此设置**画质下限**和**帧率上限**：
  - 轻微发热：下限 1 级；严重发热：下限 2 级并锁 30 FPS；临界：最低画质；
  - 低电量（<15% 且未充电）或省电模式：下限 1 级，并锁 30 FPS。
- 原有的帧时间推断仍然保留，作为兜底。

### 5.3 确定性 2D 刚体物理（`SPF.L1.Physics`）
- **形状与求解**：
  - 圆形和旋转的盒子；
  - 盒子之间用 SAT 求接触，再用参考面裁剪得到两个接触点，并带特征 ID，用于暖启动；
  - 序列冲量求解，含摩擦、恢复系数和 Baumgarte 位置修正；
  - 求解时把速度拷到紧凑的 `float3` 数组里运算，缓存更友好。
- **防穿透**：推测性接触。接触提前在"本步相对位移"的距离内生成，求解时只允许物体合拢这段间隙。高速小球不会穿过薄墙，也不需要子步。
- **关节**：铰链、刚性杆、绳、弹簧（软约束）。
- **休眠**：用并查集划分岛屿，整个岛都静止足够久才一起休眠；有醒着的物体碰到就整岛唤醒。
- **工程特性**：
  - 宽相是沿 x 的排序扫描，跨步保持有序（插入排序，接近线性）。
  - 整步是**单个 Burst Job**，单线程是有意的：保证逐位确定。
  - 快照包含暖启动状态，恢复后的运行与原运行逐字节一致（有测试）。
  - 可报告冲击事件，用于破坏判定和音效。
  - 支持射线检测、点查询。

**实测（Mac，Burst）**
- 600 个刚体倒进箱子：每步 **0.39 ms**（最慢 0.52 ms），约 1150 个接触流形。
- 选项收益（`perf-physics-options.txt`，以下为 harness 实测）：
  - **暖启动**：12 个箱子叠起来，7 次迭代就能站稳；关掉暖启动，迭代到 30 次也站不住。
  - **岛屿休眠**：静止的 105 箱金字塔，单步 0.25 ms，关掉休眠是 3.39 ms，**约 13 倍**。
- 如实记录：把默认迭代从 10 降到 8 时，单一材质的堆叠还能稳住，但石头加木头的混合结构 4 秒内静不下来。所以默认值保持 10。

**验证玩法：SlingFoundation**（弹弓推塔）
- 拖拽松手发射，带抛物线预览；
- 方块按接触冲量掉耐久，碎裂时有碎屑；目标被打倒或掉出世界都算击倒；
- 三关，每关三只鸟。

### 5.4 受光精灵（两种渲染层通用）
- **`NormalMapBaker`**：从颜色图集烘焙法线图集。高度场由三部分组成：精灵轮廓的距离变换得到斜面边缘、帧边界、加上亮度细节；图集布局与颜色图集完全一致。实例数据不变，仍是 32 字节。
- **着色器**：加入 `SPF_LIT` 关键字，支持 8 个点光源加环境光。法线会跟随精灵的旋转和镜像，代价全部落在片元着色。
- **`SpriteLighting`**：全局光源表，用固定数组，每帧设置光源不分配。

**验证**：平台跳跃第 3 关改为夜间关卡。
- 有火把光源和主角提灯，每帧挑选离镜头最近的光源，不排序、不分配；
- PlayMode 截图校验：画面大部分是暗的，同时存在亮斑。

### 5.5 骨骼剪纸动画（`SPF.L1.Skeleton` + `SkeletonSpriteJob`）
- **动画核心**：
  - 骨骼资源可以用代码搭建：骨骼、片段、关键帧，关键帧的旋转和位移都相对绑定姿势；
  - `Animator2D` 支持淡入淡出；
  - 有正向运动学，带镜像；有双骨 IK。
- **两个 Burst 并行 Job**：
  - 姿态 Job：采样、混合，再转到世界空间；
  - 部件精灵 Job：直接写入打包精灵，并按皮肤调色板分别给皮肤、上衣、裤子上色。
- **命中判定**：模拟层用**同一份骨骼和片段**求出拳头、脚尖的位置，作为命中探针。看到的就是打到的。
- 有意选择剪纸式（每个部件一个精灵）而不是网格蒙皮：完全复用现有精灵管线，每个部件 32 字节，两种渲染层都能画。

**实测（Mac，Burst）**：1000 个角色 × 11 根骨骼 / 部件，姿态 + 部件精灵 Job 合计 **0.067 ms/帧**，产出 1.1 万个打包精灵（343 KiB）。

**验证玩法：BrawlerFoundation**（横版格斗）
- 直拳、后手拳连段，外加踢腿；踢腿的够击距离更远（由腿骨长度决定）；
- 有击退、受击、倒地，三波敌人；
- 敌人有三种外观，靠调色板区分。

### 5.6 对话脚本与本地化（`SPF.L2.Narrative`）
- **脚本语言**：逐行的轻量语言，支持节点、台词、选项（可带条件）、变量赋值、条件跳转和事件。
  - 编译后变成指令数组，运行器的全部状态就是一个程序计数器加一个 int 数组；
  - 运行**不分配**（有测试），可以快照。
- **本地化**：
  - 从 CSV 读入字符串表，支持带引号的逗号、双写引号和换行；
  - 缺翻译时回退到第一种语言；带引号的空字段 `""` 表示有意留空（例如旁白没有名字）；
  - `Format` 可以把数字填进模板而不分配；
  - `Missing()` 会列出脚本用到但没翻译的键，测试里对每种语言断言为空。
- **`DialogueBox`**：打字机效果基于 `BufferText.MaxVisible`；点一下补全当前句，再点进入下一句；选项按钮来自对象池。

**验证玩法：StoryFoundation**（视觉小说《守灯人》）
- 对话运行器作为模拟资源，跑在手动时钟上：每次点击或选择推进一个 Tick；
- 所以"回退"就是快照撤销，"存档 / 读档"就是会话快照；
- 支持中英文实时切换；
- 有信任值变量，一个靠信任值解锁的选项，两种结局；
- 立绘有五种表情，背景和灯笼由脚本事件驱动。

### 5.7 关卡资源与编辑器
- **`TileLevelAsset`**（ScriptableObject）：
  - 多层瓦片加标记点，通过图例把字符映射为"某层的某个值"或"某种标记"；
  - 支持文本导入导出，关卡可以 diff，代码里写的关卡也能直接粘贴进来；
  - 可以调整尺寸，可以拷贝到 `TileMap`。
- **编辑器**：
  - 菜单 **SPF → Tile Level Editor**：调色板、左键画右键擦、可撤销、缩放，可以复制或粘贴为文本；
  - 资源检视面板上有"在编辑器中打开"按钮。
- **平台跳跃接入**：
  - 关卡改为从资源加载，标记点按阅读顺序生成，保证不管怎么画都是确定的；
  - `PlLevels.Overrides` 可以换成设计师做的关卡；
  - 菜单 **SPF → Platformer → Export Levels To Assets** 可以把内置关卡导出成资源。

## 6. 仍未覆盖、建议后续补充的能力

| 能力 | 说明 |
| --- | --- |
| 网格变形蒙皮 | 目前骨骼动画是剪纸式，能覆盖大多数手游 2D 角色。需要布料、表情网格时，再加 GPU 蒙皮路径。 |
| 物理多线程 | 物理步进目前是单个 Job，为了确定性有意不并行。几千个刚体以上时，可以按岛屿并行求解。 |
| 真机验证 | iOS 插件和 Android JNI 代码只会在真机构建时编译，CI 跑的是 Mac 编辑器，还没有在真机上跑过。 |
| 中日韩字体 | `BufferText` 依赖动态字体回退到系统字体来显示中文。发布时应该内置一份精简的中文字体。 |

## 7. 运行与查看

- 每个玩法都可以在空场景里挂对应的 `*GameBootstrap` 直接运行：
  - `SvGameBootstrap` / `PlGameBootstrap` / `TdGameBootstrap` / `M3GameBootstrap`；
  - `SlGameBootstrap` / `BwGameBootstrap` / `StGameBootstrap`。
- 截图和性能报告：CI 推送到 `ci-screenshots/<分支>`，包括：
  - 截图：`survivor-*`、`platformer-*`、`platformer-night-*`、`defense-*`、`puzzle-*`、`sling-*`、`brawler-*`、`story-*`；
  - 报告：`perf-survivor*.txt`、`perf-physics*.txt`、`perf-skeleton.txt`、`perf-gc.txt`。
- 本地快速验证（无 Unity）：`Tools/DotnetHarness/run.sh`，会编译全部程序集并运行 EditMode 测试。
