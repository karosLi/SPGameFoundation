# SinglePlayerFoundation 架构与语义合约

本文说明**当前源码怎样工作**，并保留蛇身、空间查询和渲染的工程取舍。源码核对基线为 `62c5b7f`（2026-10-07）；[共享基座分层与语义扩展计划](SharedFoundationSemanticExtensionPlan.md)中的后续安装事务、manifest、保存 envelope、异步租约等仍是提案，本文没有实现它们。

运行时验收与文档校正分开：已恢复的精确远端 `e86ee87a7691282ae7e96713c315dbec5998c414` 为 **1,050 .NET、1,084 原生 EditMode、154 graphics PlayMode 通过**。原生另有 5/1 项跳过；[精确清单](validation/NativePrecisionClosure-20261007.json)记录本地/远端树等价。范围与历史失败见[本轮验证记录](MobileFoundationFollowupValidation.md)。这些结果不等于 Android/iOS 物理设备通过，也不自动覆盖以后的源码提交。九个经典玩法和四个移动示例的差异见[兼容性矩阵](FoundationCompatibilityMatrix.md)。

## 0. 设计目标与证据边界

目标是移动端 Unity 2022.3 单机基座：固定 Tick、SoA 表、Jobs/Burst、有界状态、共享能力和只读表现。经典 Snake 的 7500 × 7500 大地图、3750 × 3750 小地图、每区 150 AI 是该游戏的默认设计规模，**不是所有玩法的统一容量或手机吞吐承诺**。

| 原始 Snake 预算 | 用法与限制 |
| --- | --- |
| 中端 60 FPS、低端 30 FPS | 产品目标；需按设备、图形 API、热身和持续时间验收 |
| 可见节点约 30k、活跃食物约 10k、飞行物 500 | 负载预算；实际容量读模式配置，不是无限增长许可 |
| 主线程 ≤ 3 ms、工作线程总计 ≤ 6 ms / tick | 初始分项预算；工作线程累计时间与墙钟 Tick/SyncWait 分开记录 |
| Tier A 上传 ≤ 50 KB、Tier B ≤ 1 MB / tick；玩法绘制 ≤ 12、总计 ≤ 40 | 早期目标；后端、可见页、材质及 UI 会改变实际调用与上传量 |
| 模拟数据 ≤ 32 MB；稳态热路径无新增托管分配 | 需分别测加载峰值、托管、Native、纹理/GPU 和 driver；固定容量不等于零 Native 分配 |

新玩法应声明自己的横竖屏、安全区、目标设备、输入、容量和 CPU/GPU/内存预算。没有本项目实测支持时，不把“ECS”“GPU 驱动”“固定 Tick”或别人的案例规模当成性能结论。

核心约束：

1. 模拟拥有权威位置、动作、伤害、冷却、随机、掉落与胜负；表现不反向结算。
2. Job 使用固定容量 Native 数据；结构变更由主线程在安全窗口执行。
3. 访问声明、阶段顺序、稳定身份、满容量行为、恢复规则都是合约，不只看类型签名。
4. 新游戏主要增加规则、配置和适配器；公共内核不认识游戏名称。已有能力无法表达的新机制仍需写代码。
5. 新增质量档只减少表现成本。经典 Snake 的历史 AI 质量参数是显式兼容例外，见 §8.7，不把它隐去或在本次文档修改中迁移。

## 1. 五层责任与真实程序集

### 1.1 五层是逻辑责任，不是五个现成 asmdef

| 逻辑层 | 当前主要落点 | 边界 |
| --- | --- | --- |
| 计算与存储内核 | Contracts、L1Simulation、Runtime.Core | 句柄、SoA、几何/空间、固定 Tick、Job 依赖；不实现具体胜负或场景 UI |
| 共享玩法能力 | L2Gameplay | 动作时间线、命中历史、技能槽、武器、统计/成长、决策选择；不增加按游戏命名的全局 enum |
| 游戏规则模块 | 各 Foundation.Runtime | 本游戏配置、资格、结算、关卡和进度；复用 World/Session |
| 表现与平台适配器 | 公共 Presentation、Shell、各游戏 Presentation 与 Game 中的适配代码 | 输入传意图，HUD/渲染读结果；资源与设备能力归适配侧 |
| 应用组合根 | WorldComposer、SessionHost、游戏 Bootstrap | 选择模块、创建 Session、连接输入/View、退出；SimSession 承担执行编排 |

组合根创建规则与适配器；游戏规则可以使用内核和共享能力，并不要求每层只能调用紧邻下一层。配置、保存和诊断是跨层协议，不能成为任意访问状态的全局服务定位器。

### 1.2 物理依赖由现有 asmdef 决定

以下列出公共 **SPF 引用**；完整 Unity 包/引擎引用以各文件为准。

| 程序集与实际目录 | SPF 引用 |
| --- | --- |
| [SPF.Contracts](../Assets/SinglePlayerFoundation/Contracts/SPF.Contracts.asmdef) | 无其他 SPF；有 Collections、Mathematics、Burst 引用，且未禁用 UnityEngine 引用 |
| [SPF.L1Simulation](../Assets/SinglePlayerFoundation/L1Simulation/SPF.L1Simulation.asmdef) | Contracts |
| [SPF.Runtime.Core](../Assets/SinglePlayerFoundation/Runtime/World/SPF.Runtime.Core.asmdef) | Contracts；World 下的 asmdef 与 Scheduling 下的 [asmref](../Assets/SinglePlayerFoundation/Runtime/Scheduling/SPF.Runtime.Core.asmref) 属于同一程序集 |
| [SPF.L2Gameplay](../Assets/SinglePlayerFoundation/L2Gameplay/SPF.L2Gameplay.asmdef) | Contracts、L1Simulation、Runtime.Core |
| [SPF.Presentation](../Assets/SinglePlayerFoundation/Presentation/SPF.Presentation.asmdef) | Contracts、L1Simulation、Runtime.Core |
| [SPF.Runtime](../Assets/SinglePlayerFoundation/Runtime/SPF.Runtime.asmdef) | Contracts、L1Simulation、Runtime.Core、L2Gameplay、Presentation；含 Composition、Session、Persistence、Diagnostics |
| [SPF.Shell](../Assets/SinglePlayerFoundation/Shell/SPF.Shell.asmdef) | Contracts、Runtime.Core、Runtime、L2Gameplay；另含 UI/URP 适配 |
| 各游戏 Runtime / Presentation / Game | 按实际使用显式引用公共层；Game 连接本游戏 Runtime / Presentation；公共层不反向引用游戏 |

`IGameplayModule` 与 `GameplayModuleAsset` 在 SPF.Runtime，`ISimSystem` 在 Runtime.Core。游戏 Runtime 引用 SPF.Runtime 是当前声明契约的实际需要，不授权规则调用游戏 Bootstrap。Shell 和某些适配器引用 Runtime 也不等于它们拥有模拟结算权。

现有 asmdef 能限制程序集反向引用，但不能自动强制 NativeArray 只读、一个程序集内部的五层职责、所有资源所有权或热路径无分配。本阶段不拆程序集、不声称 Contracts 完全无 Unity 依赖。

## 2. 数据、身份与 Tick 执行

### 2.1 SoA 选型

当前采用自研固定布局 SoA + C# Jobs/Burst，未引入 Unity Entities。每张 [SimTable](../Assets/SinglePlayerFoundation/Runtime/World/SimTable.cs) 有固定容量和类型化 NativeArray 列；活跃范围为 `[0, Count)`。这是现有负载的数据组织选择，不能据此宣称比所有 ECS 更快、结构变更零成本，或以后只替换 Runtime.Core 就能迁移 Entities。

[SimWorld](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs) 拥有表、EntityRegistry、DestroyQueue 和由模块注册的资源；空间网格、身体池、配置、游戏状态及事件队列都由模块显式登记，World 不内置一套固定 Snake/Food 表或通用 ConfigDatabase。

### 2.2 EntityHandle、row、key 与持久身份

真实 [EntityHandle](../Assets/SinglePlayerFoundation/Contracts/EntityHandle.cs) 只有 `Index`、`Generation` 两个 int，没有 Kind。`Generation == 0` 表示 Null；还必须在所属 World 的 Registry 中解析，才能判断存活并取得 `(tableIndex, row)`。

- `Index` 是 Registry 槽位，不是 row。普通表删除采用 swap-back；`SortRows` 也会重排，Registry 同步更新映射，完整 handle 不因换行失效。
- `Release` 将槽位置为不存活；下次 `Allocate` 增加 generation。`Registry.Clear` 使旧活跃句柄失效。不要只存 Index，也不要用 row 做跨 Tick 锁定目标。
- 屏幕上的 actor 不一定是 Registry 实体：部分英雄在游戏 state resource，表现可使用 `EntityHandle(-1, 1)` 等合成 key。此类 view-instance 身份不能交给 Registry/DestroyQueue 当运行实体；接入协议要注明身份来源。
- handle 的有效域是所属 World/Session。两个 Session 可以有完全相同的数值；新 Session、重开与快照恢复都要求外部缓存重新核对归属和时间线。handle 不是跨 World 或永久存档对象 ID。
- `.Pooled()` 表没有 Registry 实体身份，`Handles` 为 Null。用 `Spawn/SpawnRange` 创建，Job 写自己行的 `DeadFlags`，下一 Tick 起始稳定压缩保留幸存行顺序；row 仍可能改变。需要跨 Tick 身份时采用普通表，或明确实现并验证独立 ID。
- 容量满时 `CreateEntity` 返回 Null 且 row 为 -1，`Spawn` 返回 -1；`SpawnRange` 可能部分成功，检查 added。失败计数是诊断，不是重试/补偿政策。
- [AccessKey.Id](../Assets/SinglePlayerFoundation/Contracts/AccessKey.cs) 是按创建顺序分配的进程内密集编号，用于调度。key 通常为 `static readonly`；名字用于诊断及部分保存标记。相同字符串创建的新 key 仍是不同 key；Id 不能充当跨进程 schema/content ID。
- 模块默认 Id 是类型名；模块、内容、视觉、schema 版本和运行实体分别命名。长期稳定 ID/旧名迁移属于显式协议，尚无统一目录自动管理。

### 2.3 Tick 管线与访问声明

[SimPhase](../Assets/SinglePlayerFoundation/Contracts/SimPhase.cs) 的顺序为：

```text
BeginTick: PlaybackDestroys → CompactPools
ApplyCommands → Input → Decide → Move → Body → SpatialBuild
              → Collision → Resolve → Spawn → Snapshot
EndTick: 完成所有待执行 Job → 各 ISyncResource.OnSync
```

Phase 内按 Order，再按注册次序稳定排序。Phase 是**调用顺序**，不是自动完成所有前阶段 Job 的屏障；互不冲突的访问集可以重叠。`Spawn` 名称也不自动授予安全改表权限。

真实系统签名如下（声明摘录，定义位于 [ISimSystem.cs](../Assets/SinglePlayerFoundation/Runtime/Scheduling/ISimSystem.cs)，不是要在游戏里重新声明一个同名接口）：

```csharp
public interface ISimSystem
{
    SimPhase Phase { get; }
    int Order { get; }
    void Declare(AccessDeclaration access);
    void OnCreate(SimWorld world);
    JobHandle OnTick(in SimContext context, JobHandle dependency);
    void OnDestroy(SimWorld world);
}
```

[AccessDeclaration](../Assets/SinglePlayerFoundation/Runtime/Scheduling/AccessDeclaration.cs) 与 [DependencyTracker](../Assets/SinglePlayerFoundation/Runtime/Scheduling/DependencyTracker.cs) 的规则：

1. Read 等最后一个 writer；Write 隐含 Read，等最后 writer 及之后的 readers。列、表和资源 key 是独立项；声明表不自动覆盖它的全部列，TableKey 用于 handles/pooled dead flags 等结构数据的约定。
2. 把输入 dependency 传给 Schedule，并返回包含所有新工作的 handle。漏传、漏返或漏声明都会破坏依赖图。
3. 主线程在 OnTick 中读写前序 Job 数据时，要先 `dependency.Complete()`，前提是访问集已覆盖这些数据；拿到依赖不等于它已经完成。
4. 空声明被 [TickPipeline](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs) 视作 barrier：先完成之前全部工作，后续依赖它。已有主线程 flow/生成系统使用该方式；它是正确性选择，也可能减少并行。
5. [AccessGuard](../Assets/SinglePlayerFoundation/Runtime/World/AccessGuard.cs) 在开发期 OnTick 获取列和 `IJobData` 资源时检查是否声明；它不区分读取还是写入，也不扫描 Job 内所有内存访问、直接缓存的 NativeArray、Handles 或普通托管对象。不是完整静态/运行时权限系统。
6. `SnapshotBuffer<T>` 当前未实现 IJobData，但写它的系统仍须显式声明其 ResourceKey；不要把“guard 没报错”当成“不必声明”。

创建、销毁、ClearLevel、排序、Slab resize 和释放只能在没有相关在途 Job 的主线程安全窗口进行；一般放在 ApplyCommands 或已 Sync 的 Tick 之间。入口没有统一 phase 锁来替调用者强制这一点。Job 删除普通实体通过 `SimWorld.DestroyQueueKey` 的 writer 申请，下一 Tick 起始播放；尚无通用结构命令缓冲自动涵盖创建/迁移/所有规则命令。

`EndTick` 是统一汇合点，但不能说整个 Tick 只 Complete 一次：barrier、主线程访问、诊断 `SerialProfiling` 都可能提前完成。`BeginTick` 调度异常会汇合已登记工作再报告；这不是完整 Session 安装事务，见 §6.1。

### 2.4 Session 时钟与读取边界

[SessionSettings.Default](../Assets/SinglePlayerFoundation/Runtime/Composition/ModeDefinition.cs) 为 30 Hz、每帧最多 3 tick、DestroyQueue 4096；模式可使用其他配置。`ManualClock` 模式只消费 `RequestTicks`，适合回合/事件驱动游戏；`Step()` 是忽略运行时钟的单步测试/回放入口。

[SessionHost](../Assets/SinglePlayerFoundation/Runtime/Session/SessionHost.cs) 默认帧末启动下一 Tick，在下一帧 LateUpdate 开头 Sync。后续 renderer 可读取完成的数据；Update、HUD getter、测试、独立 Render 入口访问 Native 数据前仍应显式 `Session.Sync()`。不要把同一 Session 同时交给 Host 和另一套 Update 循环推进。

只有同种子、配置、输入、消费次序和受测构建的回归证据支持可复现声明。当前不少 Job 使用 FloatMode.Fast；固定 Tick、换 FloatMode 或使用 Burst 都不自动给出跨 CPU/平台位级确定性，本文不提供“切 Deterministic 即可、代价 5–15%”的承诺。

## 3. 空间查询与 Snake 大 / 小地图

### 3.1 通用机制与玩法配置分开

共享 L1 提供几何、[SpatialGrid](../Assets/SinglePlayerFoundation/L1Simulation/Spatial/SpatialGrid.cs)、[CellListGrid](../Assets/SinglePlayerFoundation/L1Simulation/Spatial/CellListGrid.cs) 等有界存储/查询。网格给候选，窄相才决定接触。形状 bounds 要包含目标半径与必要移动范围；切线、负坐标、零长扫掠、相向高速、最大半径、越界和容量满都需测试。

后端等价至少包括集合、边界、稳定平局、容量与访问次序敏感计算。查询集合相同不代表浮点分离力、有限候选或最早命中结果相同；密集全重叠也不能保证近似线性。四叉树与 AI 候选的范围见[碰撞基准](CollisionBroadphaseBenchmarks.md)，不因数据结构名称更换默认。

### 3.2 当前 Snake 网格

[SnakeGameModule](../Assets/SnakeFoundation/Runtime/SnakeGameModule.cs) 实际安装：

- BodyGrid：活跃窗口内的增量 CellListGrid，节点锚定轨迹点；ItemGrid：食物/道具增量 CellListGrid，使用独立 ChangeLog。
- HeadGrid：覆盖最大 Region 的 SpatialGrid，粗格按 ChunkSize；不是所有网格每 Tick 计数排序重建。
- 默认 ChunkSize 125，大地图 60 × 60 Chunk、小地图 30 × 30。细窗口范围和 body/item cell 尺寸由 Capacity 配置，不能由早期设计图写死。
- 原地列写入不会自动增加 SimTable.Version；需要 `MarkChanged` 或消费者自己的更新契约。各消费者独立清理自己的 ChangeLog。
- 窗口变化到重建之间，读者使用网格实际内容的 BuiltOrigin，不能只取尚未应用的新 Origin。

只让主动体发起适当查询；身体/食物作为候选数据，依照玩法过滤自己、阵营与保护状态。统一的所有玩法 CollisionMatrix/ResponseId 注册器尚不存在；Snake 在 ContactSystem/ResolveSystem 中执行自己的规则。

### 3.3 当前地图衔接

[RegionSystem](../Assets/SnakeFoundation/Runtime/Systems/RegionSystem.cs) 在 ApplyCommands 消费 PortalRequest，平移玩家头部与整条轨迹、更新 Region/保护时间；[WindowSystem](../Assets/SnakeFoundation/Runtime/Systems/WindowSystem.cs) 按活动区域流入/流出食物和道具。

- 非活动 Region 的蛇保留在同一组表与 BodyStore 中，按 Region 标记冻结；食物/道具以 ChunkPopulation 保留统计，再按窗口展开。
- 7500 × 7500 与 3750 × 3750 使用同类机制，后者面积是前者 1/4；不是小房间特例。
- 活跃窗口外蛇的低频/简化行为由具体系统决定。不能把全部 Dormant 蛇描述为已经聚合成统计人口。
- `WorldGraph/EdgeLink` 无缝连接、异步预加载、通用 Migrate 命令、RegionSnapshot 压缩/离线推进是早期备选设计，**不是当前公共 API**。未来需求应独立验证，不能照图直接调用。

## 4. 规则、配置与共享能力

### 4.1 Authoring、冻结配置、运行状态

具体游戏采用 Bake、克隆、值结构或固定定义，不能假设所有配置都已深冻结。Snake 使用 [SnakeConfig.Bake](../Assets/SnakeFoundation/Runtime/SnakeConfig.cs) 创建 [SnakeRuntimeConfig](../Assets/SnakeFoundation/Runtime/SnakeRuntimeConfig.cs)，包含按值传给 Job 的 SnakeSettings 和 NativeArray；其他模式可采用克隆或值结构。SnakeRuntimeConfig.Capacity 仍引用 source section，RpgRuntimeConfig 也保留若干源引用；运行字段可变性要逐项审计。它们不是统一自研 ConfigBlob 数据库，也没有类型层面统一强制不可变。

- Authoring 可以是 ScriptableObject 和资源引用；配置 owner 负责校验有限值、容量及整数乘法溢出。
- 编译/冻结后的规则定义由模拟读取；不要在 Job 内读取可变 ScriptableObject，也不要假设修改原 SO 会更新已启动的 Session。
- HP、冷却、动作、命中历史、计数器和 RNG 状态属于运行状态；不能当常量配置遗漏保存/重置。
- 内容 ID 与 VisualId 各有命名空间；AccessKey.Id 和 row 不是稳定内容标识。
- 通用 CSV/Excel 导入、Default→Mode→Region→Difficulty 合并、配置热重载到下一 Tick、全模式完整内容指纹都未作为公共协议实现。

### 4.2 AI 与规则的实际分工

Snake 的 [AISystem](../Assets/SnakeFoundation/Runtime/Systems/AISystem.cs) 位于游戏 Runtime，使用低频效用选择与每 Tick 12 方向上下文转向，读取上一 Tick 网格；不是所有 AI 都在 L2Gameplay。共享 [DecisionTree](../Assets/SinglePlayerFoundation/L2Gameplay/AI/DecisionTree.cs) 是有界 reactive selection，不是管理长时任务的完整 BehaviorTree。

树/评分选择不能接管攻击前摇、承诺、打断、恢复和移动状态的所有权。增加共享 AI 时保留惰性随机消费、稳定平局和失效时机，比较包括感知、构建、查询、等待与完整 Tick 的成本。

### 4.3 已有能力与内容边界

| 当前能力 | 实际落点 / 接入边界 |
| --- | --- |
| 成长、Buff、道具、基本技能参数 | L2 的 GrowthCurve、BuffSet、PropDefinition、SkillDefinition；具体拾取/伤害/冷却由游戏消费 |
| 动作与输入窗口 | ActionTimeline、TickInputBuffer、ActionPoseClock；不自动执行动画或任意技能图 |
| 稳定命中与形状 | HitHistory、CombatShapes、CombatSweep、GroundCombatQueries；资格/伤害/目标排序由游戏决定 |
| 移动技能门 | SkillSlots；槽位是输入/充能门，不拥有全部技能规则 |
| 权威武器 | WeaponProfile/WeaponRuntime 与只读 WeaponViewState；当前动作 family 与单 owner 等边界见[武器合约](AuthoritativeWeapons.md) |
| 其他品类共用 | PlatformerMotor、StatSheet、Progression、WaveSchedule、Dialogue/Localization；按真实消费者使用 |

Snake 飞行物由游戏系统移动、扫掠和结算；飞剑、Belt 高度/纵深、Shooter 光束有各自的显式能力。不能把“统一 ProjectileTable 支持任意追踪/抛物/回旋”和通用 EffectOp 执行器当成已经实现。

## 5. Command、Event、Query 与有界队列

### 5.1 语义先于容器名称

- Command：移动、攻击、换装、重开等意图，可以拒绝、取消或到期；UI 锁存，Tick 授权。
- Event：已接受技能、已结算伤害、已死亡等事实，应在对应状态改变之后产生。
- Query：读取状态或候选，不推进计时/消费模拟 RNG。空间候选不等于命中。
- Presentation cue：从事实派生的提示，可按预算合并、丢弃或过期；不驱动权威结算。

[EventQueue<T>](../Assets/SinglePlayerFoundation/Runtime/World/EventQueue.cs) 只是运输容器，里面可以放命中申请、候选或事实；名字不自动赋予事实语义。当前使用固定容量 [ParallelQueue<T>](../Assets/SinglePlayerFoundation/Contracts/Collections/ParallelQueue.cs)，不是 NativeStream 或托管全局事件总线。

### 5.2 生产、规范化、消费、清空

每条队列需要明确生产/消费阶段、owner、容量、排序键、溢出政策、保存及清空责任：

- 多 writer 只能用 `AsWriter().TryAdd` 的原子索引路径；`EventQueue.TryAdd` 和 `Raw.TryAdd` 是单线程路径，不能混为并行 writer。容量满返回 false 并计数，不扩容。入队顺序依赖调度；容量不足时被接纳的子集也可能依赖竞争，事后排序不能补救这一点。
- 完成生产者依赖后才读 AsArray、排序或 Clear。`Raw` 供单消费者 Job 读写；它不是独立副本。
- EventQueue 不自动排序。需要确定性的游戏在消费/保存前自行规范化；`saved: true` 默认保存当前项/溢出，不替你选择排序键。
- `saved: false` 不写事件 payload；恢复时清空待播项，外层资源顺序/标记仍参与 World 保存。它不是可以免去清空和生命周期处理的永久总线。
- Clear 将本轮 overflow 累加至 TotalOverflow 再清队列；使用 Raw.Clear 的消费者需要自己理解计数责任。不要声称所有拒绝计数天然饱和、所有关键事件自动重试。
- DestroyQueue 是特例：播放和保存前按完整 handle 排序，重复/过期请求播放时忽略；`Request` 返回 void，不能当作已接纳确认，需要结果时用 writer.TryAdd。超容量的关键终态仍由玩法决定补偿策略。
- 多 View 不能轮流 Clear 同一公共流而期待各自收到事件；需要已有只读批次/序列游标，或明确单消费者转发。通用多订阅者事件系统仍未提供。

HitHistory 的写入顺序为 **Check → 成功入队 → TryRecord**，每 scope 单 writer；队列满不提前消耗 history/穿透，history 满拒绝新目标。该顺序不是多 writer 事务。

## 6. 模块接入、生命周期与所有权

### 6.1 真实模块与组合入口

[IGameplayModule](../Assets/SinglePlayerFoundation/Runtime/Composition/IGameplayModule.cs) 仅包含以下成员：

```csharp
public interface IGameplayModule
{
    string Id { get; }
    void DeclareData(WorldLayout layout);
    void RegisterSystems(SystemRegistry registry);
}
```

`ModeDefinition` 保存模块列表与 SessionSettings；地图、规则配置、表现选择由游戏模块/Bootstrap 管理。`WorldComposer.BuildWorld` 按列表 DeclareData，拒绝空模块和重复 Id，再分配 World；BuildPipeline 收集系统、排序并调用 OnCreate。它没有 BakeConfig、RegisterRules、RegisterPresentation、WorldLayoutBuilder 或能力依赖图接口。

已有校验包括非正表容量、列所属表、重复 resource key、levelScoped 资源的 reset 接口。重复声明同一 TableKey 会取容量最大值，同一 ColumnKey 合并。它不预检所有同名不同 key、冲突 schema、安装环或预算来源。

**失败边界：** SimSession 在 BuildPipeline 抛错时释放已完成创建的 World，但 DeclareData/构造器的部分分配、已 OnCreate 系统以及后续时钟创建的完整异常清理仍是阶段 B 的改进目标。当前不是已实现的“全事务安装、失败必回到基线”。

### 6.2 Session 与版本的含义

[SimSession](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs) 的状态只有 Created、Running、Paused、Disposed；加载、预热、结算、返回菜单是 Bootstrap/游戏 flow 的责任，尚无统一异步 Load/Warmup/Ending 状态机。

- Restart 先完成工作与 system reset，再 world reset、重置时钟，进入 Running；复用主要存储，不自动保证任意资源的无分配重置。
- Host 的 disable、失焦、后台暂停与游戏主动 Pause 是独立原因；恢复焦点不解除菜单暂停，恢复首帧丢弃后台墙钟积累。
- TimelineRevision 在 Restart 或成功 ReadSnapshot 后递增，包含恢复到同 Tick。它不保存、不作为内容版本；恢复失败触发 Restart，也会失效旧时间线。
- World.LevelVersion 只在 ClearLevel 后递增。ClearLevel 只清 level-scoped 表和 reset 对应资源，保留 session 数据；不会自动调用所有 system 的 OnReset。它未写进世界快照，World.Reset 也不增加它，不能单独用它检测所有重开/恢复。
- 表 Version 表示结构/显式 MarkChanged，SnapshotBuffer.Version 表示缓冲轮换；都不能替代 Session 归属、TimelineRevision 或 EntityHandle.Generation。

### 6.3 扩展列：使用同一 key 和布局阶段

下面是可用 API 的独立声明示例，定义自己的 key，不修改一个叫 SnakeTable 的基类：

```csharp
using SPF.Contracts;
using SPF.Runtime.World;
using Unity.Mathematics;

public static class ExampleKeys
{
    public static readonly TableKey Actor = new TableKey("Example.Actor");
    public static readonly ColumnKey<float2> Position =
        new ColumnKey<float2>(Actor, "Position");
    public static readonly ColumnKey<int> Streak =
        new ColumnKey<int>(Actor, "Streak");

    public static void Declare(WorldLayout layout)
    {
        layout.Table(Actor, 128).Column(Position);
        layout.Table(Actor, 128).Column(Streak); // 其他模块可复用同一个 Actor key
    }
}
```

列随普通表 swap-back、pooled 压缩和排序一起移动；读取用 `context.Column(ExampleKeys.Position)`。扩展在 World 创建前声明，不支持运行中随意增加 NativeArray 列。新增列会影响保存布局，不因叫“扩展”就自动兼容旧 fixture。

### 6.4 谁创建，谁负责退出

| 对象 | 当前所有者和退出路径 |
| --- | --- |
| Session / World / Pipeline | Host 或直接创建它的调用者 Dispose Session；Session 先完成并销毁 Pipeline，再释放 World |
| 表、Registry、world-owned resource | SimWorld.Dispose；注册资源若为 IDisposable 会被释放。不要把同一个独占资源同时转交多个 World |
| System 私有 Native scratch | 创建它的 system 在 OnDestroy 释放；需要 reset/save 的跨 Tick 状态实现对应接口 |
| Bootstrap 创建的临时 Mode/Module/Config | Bootstrap 负责释放；外部传入共享资产保留外部所有权。Host 只管理 Session，不替调用者销毁全部 ScriptableObject |
| Renderer 的 material/mesh/buffer/私有 texture | Renderer 或创建者 Dispose/Destroy；Resources.Load 得到的共享资源与自己 new 的实例分开 |
| 事件订阅、输入源与 View cache | 绑定侧在 disable/rebind/destroy 的相应路径取消/重置；不得消费旧 Session 的输入、插值或 cue |

`WorldLayout.Resource` 的资源登记是 ownership transfer 约定，当前没有引用计数 AssetLease/通用异步 RequestToken。跨 Session 绑定至少核对 Session 对象、时间线/关卡版本与完整实体身份；有异步适配时还需请求版本，不能假设完整 handle 已包含这些信息。

## 7. 只读表现、渲染后端与保存

### 7.1 读取路径与身份

表现可读同步完成的表，也可读模块提供的投影；不是所有游戏都经过同一种 SnapshotBuffer。`NativeArray` 类型本身不阻止写入，“只读表现”是代码与测试合约。

[SnapshotBuffer<T>](../Assets/SinglePlayerFoundation/Runtime/World/SnapshotBuffer.cs) 是 Previous/Current/Write 三缓冲，每次 OnSync 轮换，只提供前后 row 对应。排序、压缩或重建后需要 writer 同时保存稳定 ID 并由读者核对，不能把相同行号插值成同一角色。借用的数组不能跨后续轮换/释放长期持有。

角色、武器、粒子应在同一表现时刻读权威状态；恢复/重开/换装清旧 backlog 和拖尾。已有 Sv/Bw renderer 的失效与武器 cursor 测试是接入参考，不等于所有历史 View 已采用统一 binding helper。

### 7.2 当前 Snake 渲染，而非早期理想管线

[RenderCapabilities.Detect](../Assets/SinglePlayerFoundation/Presentation/Rendering/RenderCapabilities.cs) 使用 Override 或 compute 支持、顶点 buffer 输入数 ≥ 4、非 Null 图形设备选择 GpuDriven/DataTexture。它没有内置全设备黑名单或证明每个 shader/indirect API 都成功；具体后端还需资源/精度门和实际执行验证。

| 部件 | 当前实现 |
| --- | --- |
| [ChainRenderer](../Assets/SinglePlayerFoundation/Presentation/Rendering/ChainRenderer.cs) GPU | CPU 组装有限链 header、轨迹增量并 SetData；compute 散写轨迹/展开节点，可压缩不透明节点，RenderMeshIndirect 绘节点；条带使用 GPU 轨迹与 RenderPrimitives |
| ChainRenderer DataTexture / mesh | Burst 在 CPU 展开节点，CircleBatch 数据纹理绘制；条带用 CPU 构建的 MeshData。不是纯 GPU 蛇身展开 |
| [PointCloudRenderer](../Assets/SinglePlayerFoundation/Presentation/Rendering/PointCloudRenderer.cs) | 常驻 GPU 实例池 + 行增量 scatter + compute 剔除 + indirect；初始化能力失败由调用者选择回退 |
| [CircleBatch](../Assets/SinglePlayerFoundation/Presentation/Rendering/CircleBatch.cs) | 32 B InstanceData；GPU SetData/indirect，DataTexture 最多 4096 实例一页，纹理/网格按使用创建并绘实际前缀 |
| [RenderAssets](../Assets/SinglePlayerFoundation/Presentation/Rendering/RenderAssets.cs) | Resources/Shader.Find 加载共享 shader，CreateMaterial 返回调用者拥有的新 material；直接 Dispatch/Graphics 绘制兼容当前管线接线 |

GPU 镜像不拥有模拟真相。轨迹 owner/Version/Start 变化要正确失效重传；非 Tick 帧可以复用 header 等准备结果，但插值/视口和实际绘制仍有成本。首次触达纹理页可能分配，正式测稳态前要预热到声明容量。

早期“全量合成一个 DeltaUploadBuf、每 Tick 一次 LockBufferForWrite、三上传缓冲、URP RendererFeature 内 3–4 次 Dispatch、所有剔除排序和参数都在 GPU、固定 Draw 数”的图是备选优化方向，**不是当前实现**。应逐个后端测实参、资源与真实调用，再决定是否值得改。

### 7.3 蛇身采样与半透明

[BodyStore](../Assets/SinglePlayerFoundation/L1Simulation/Body/BodyStore.cs) 在预分配池内按 2 的幂 Slab 管理 TrailState；Resize/Respace 可能搬移/重采样并增加 Version，容量不足返回 false 保留旧轨迹。写入成本与新增点有关，不能把整个身体维护描述为无条件 O(1)。

- 轨迹间距按半径量化，偏离阈值才重采样；视觉节点按弧长采样，不等同于每一个轨迹点。
- 身体碰撞节点锚定每第 k 个保留轨迹点；头端移动/新增、尾端删除走增量，窗口/owner/轨迹重写等变化仍需重建。
- 分块 TrailBounds 缓存减少重复扫描，空间索引和 GPU 镜像有各自失效/重建责任。
- 节点与条带共用轨迹，视觉 skin/LOD 不得改变命中几何。

半透明使用同蛇等深度、ZTest Less、ZWrite On 和预乘混合，使该蛇同像素只接纳首次覆盖；半透明蛇按后→前添加，同蛇节点按头→尾输出，不能无序压缩半透明节点。当前不透明压缩与有序透明路径分开。预算外使用不透明回退、减少装饰或降低分辨率，但透明像素/顶点成本仍要实测；不是“用了深度即无 overdraw”。

### 7.4 其他角色与特效后端

普通 SpriteBatch、CPU/Burst 自然 cutout 与 Weighted BAT 是不同能力；两种 sprite tier 不等于 BAT 的 vertex/compute/CPU 三后端。Weighted BAT 仍是有限资产/链条/顶点的验证路径，不是任意骨架导入器。粒子 GPU compute 需要真实执行和读回/像素证据，不能把 CPU 算完后上传叫 GPU 模拟。

详细接线保留在[新玩法配方](NewGameplayIntegrationRecipe.md)、[自然角色](GameplayNaturalCharacters.md)、[Weighted BAT](BatCharacterValidation.md)、[compute palette](BatComputePaletteValidation.md)、[有界武器粒子](BoundedWeaponParticles.md)。

### 7.5 保存检查点不是呈现快照

`SimSession.WriteSnapshot/ReadSnapshot` 在同步窗口协调时钟、World 和 ISnapshotSystem；`SnapshotBuffer<T>` 不实现 ISnapshotResource，不是存档。

- World 保存 live rows、Registry 和 ISnapshotResource；检查 world format、seed、表/资源顺序及相关容量/标记。Pipeline 按参与保存的系统类型名顺序读写。
- IJobData 没实现 ISnapshotResource 会被 SnapshotGaps 拒绝；这只是诊断启发式，**不会发现未标记/未保存的全部可变状态**；例如 Signals 是 Job 使用的 Native mailbox，却没有 IJobData。例如 SnakeGameState、RegionPopulations、ReplayBuffer、SnakeQuality 和 Signals 没有完整保存资源实现，相关 system 也未实现 ISnapshotSystem；Snake 输入回放通过不等于完整中途 Session 保存成立。各游戏必须审计自己的保存清单。
- 不可变配置通常不写入 raw snapshot；读取端必须有相同规则、布局、seed。已有 SkillSlots、飞剑/Belt/武器等选择性指纹不等于全库完整兼容校验。当前 WeaponRuntime 指纹还含 VisualId/握持定义，不能声称改视觉定义必然不影响读档兼容。
- World format 1 不保存列 key/type 的语义身份；Pipeline 采用系统类型短名。raw Native 数据、等大小字段或列换义、系统改名/同短名、配置同 ID 不同规则都需要显式 schema/指纹策略。通用长期跨版本/跨平台迁移与外层 envelope 仍为提案。
- 成功恢复增加 TimelineRevision 并清掉 pending ticks；非法数据时 Session Restart 后重抛，不能保证保留失败前的进行中对局。直接 World.ReadSnapshot 失败可能部分恢复，必须 Reset 后再用。
- grid/scratch 若派生自权威状态，恢复后先重建再读取；视觉 cue、IK、粒子等按失效协议清理，不能自动重播已发生事件。

## 8. 性能与证据

### 8.1 分开测什么

按模式/设备记录完整 Tick、主线程调度、Job 执行、SyncWait、渲染准备、上传、GPU、UI/输入和持续帧时 p50/p95/最坏值。`SerialProfiling` 会串行完成系统，适合归因，不可当正常流水线的无侵入总成本。

### 8.2 计算与内存约束

保留 SoA 冷热分离、有界队列、空间候选筛选、按需重建与增量更新。移动端 worker/batch 取值需实测；当前各 Job 的批次不同，不存在“少于 512 必然单线程更快”或“64 线程组适合全部 GPU”的统一定律。

固定容量控制峰值，不等于无任何 Native 临时分配：当前 SortRows/Permute、pooled Compact 等路径使用 TempJob scratch；要单独计量。原地列修改不会自动被所有 cache 观察到；为跳过工作增加 cache 时必须定义版本和恢复重建。

### 8.3 上传、常驻和过度绘制

payload 字节、纹理页/前缀填充、API 实际上传量和硬件显存流量分开。32 B × 实例数只是部分后端的有效数据量；还包括 header、delta、args、纹理及 mesh 等。ASTC/ETC2、图集尺寸、mipmap、MSAA/render scale 按真实资产与设备选择，不能把历史估算的 20 KB/tick 或 0.3 ms 当全部后端现值。

### 8.4 托管分配与 GC

热路径避免增长集合、LINQ、闭包/装箱、逐对象 GameObject/Animator、每击 Instantiate/Destroy 和字符串拼接；初始化/冷路径订阅委托是允许的。当前 SessionHost 等实际使用 C# event，不能写成“全库禁用 event”。

使用 [ManagedAllocationProbe](../Assets/SinglePlayerFoundation/Testing/ManagedAllocationProbe.cs) 保留数组阳性对照和 empty 对照；对照无效则测量不可用。`.NET ManagedBytes`、Unity 当前线程 `GC.Alloc` 样本、全帧分配、gen-0 collection 次数、Native/GPU 内存分别报告。观察器、截图/readback、编码和断言字符串放在窗口外。参见[校准更正](AllocationMeasurementCalibration.md)。本次不以手动 GC.Collect、关安全检查、缩短窗口或放宽阈值替代修复。

### 8.5 绘制与可读性

图集和共享 batch 减少逐实体提交；DataTexture 分页、混合类别、shader、UI 和可见内容仍可能增加 Draw Call。SRP Batcher 不代替实例化；避免无意读取 renderer.material 产生新材质。除了 Frame Debugger/计数，还要检查小屏密集画面的目标/弹道/危险提示可读性。

### 8.6 真实依赖与验证环境

[manifest](../Packages/manifest.json) 当前固定 Burst 1.8.27、Collections 2.1.4、Mathematics 1.2.6、URP 14.0.11；Unity 版本看 [ProjectVersion](../ProjectSettings/ProjectVersion.txt)。包存在不代表所有模式都启用对应渲染管线，Editor 设置菜单也不是已构建的 Android/iOS 包。

.NET harness 使用 Unity/Jobs/Burst/图形桩验证逻辑和 asmdef 编译方向；原生 EditMode、真实 graphics PlayMode、桌面 CPU/GPU、物理 Android/iOS 是不同证据。GLES/Metal/Vulkan 的能力回退和热态功耗需设备实测，当前外部设备门槛保留。

### 8.7 画质隔离与经典 Snake 例外

共享 [FrameGovernor](../Assets/SinglePlayerFoundation/Shell/Performance/FrameGovernor.cs) 不修改模拟 TickRate；新玩法质量档只能改变表现。**固定 TickRate 本身不保证玩法状态不变。**

经典 [AdaptiveQualityController](../Assets/SnakeFoundation/Game/AdaptiveQualityController.cs) 会修改 SnakeQuality.AIDecisionIntervalTicks，AISystem 使用该值；[ReplayFrame](../Assets/SnakeFoundation/Runtime/Replay.cs) 记录/回放它。这是会影响模拟调度的历史输入，不能称为纯视觉开关。本次保持其回放语义，后续若迁移必须单列规则版本与兼容验收；不将同样做法推广给新玩法。

### 8.8 历史实测与优化记录

以下保留早期 Snake 的六轮工程记录用于解释取舍。原段落没有完整绑定每个数字的精确 commit/tree、原始样本和当前配置，**不是本次新测结果、当前性能基线或移动端认证**。仅能按其明确的桌面环境阅读；后续优化必须建立对应提交的可恢复 A/B 证据。容量、网格默认值与渲染实现仍以当前源码为准。


基准：`SnakePerformanceTests.TickBenchmark`（150 AI + 默认食物，约 1.2 万食物 / 1.5 万身体点），自托管 Runner（Apple M5 Pro，Unity 2022.3.62f2，Burst 开启），报告写到 `Artifacts/perf-editmode.txt`。`TickPipeline.SerialProfiling` 让每个系统调度后立即完成，用来按系统拆分耗时。

| 指标 | 优化前 | 第一轮 | 第二轮（历史） |
| --- | --- | --- | --- |
| 流水线 tick 均值 / p95 | 0.434 / 0.475 ms | 0.270 / 0.307 ms | **0.229 / 0.259 ms**（−47%） |
| 网格构建（串行口径） | 0.274 ms（单系统） | Body 0.132 + Item 0.110 + Head 0.011 ms | Body 0.067 + Item 0.098 + Head 0.012 ms |
| AI | 0.042 ms | 0.028 ms | 0.030 ms |

`SessionHost.OverlapRendering` 默认把 tick 调度到帧末，允许与渲染及下一帧 Update 重叠；实际隐藏多少等待取决于负载，不能把 worker 时间视作消失，也不能把串行诊断时间与流水线时间相加。

已做的优化与修正：
- `GridEntry` 20 B → 16 B（Owner / Data 各 16 位），一条缓存行正好 4 个条目。
- 计数排序：包含式前缀和 + 逆序稳定散射，去掉逐条目 cell 缓存与“还原”遍历（格子遍历 3 → 2 次）。
- `GridSystem` 拆成 Body / Item / Head 三个系统，访问声明收窄，Item / Head 网格不再等待身体更新。
- 物品网格改用 8 m 格（128²，原 256²），覆盖范围不变。
- AI 避让按兴趣值降序探测，当剩余方向不可能超过当前最优分时停止（结果与全扫描一致），12 个方向用增量旋转生成；`TurnTowards` 去掉 `acos`。
- 数据纹理档：页面大小按批次容量分配，上传改为一次原生拷贝（`InstanceData` 恰好 2 个 RGBA32F 纹素）。
- 修正：迎头碰撞按“最小节点序号”判定（原先依赖格子扫描顺序，方向不同结果不同）；飞行物取扫掠最早命中；窗口边缘 12 m 内视为窗口外（避免穿过窗口外的身体）；回放记录自适应画质改变的 AI 决策间隔；食物容量 2.4 万；模拟 Job 同步编译。

第二轮已完成：
- **模拟与渲染重叠**：`SessionTickLauncher` 在所有 LateUpdate 之后调度下一 tick，下一帧 LateUpdate 开头完成；代价 1 帧显示延迟，输入反而在同一帧进入 tick。PlayMode UI 自动化在此模式下全部通过。
- **身体网格 8 m 格**（`Capacity.BodyGridCellSize`）：构建 0.131 → 0.067 ms，查询耗时不变；基准同时跑 4 m / 8 m 便于回归对比。
- **物品网格免重建**：`SimTable.Version` 记录结构变化，食物 / 道具与窗口原点都未变时跳过（实测约 12% 的 tick，被吃的频率很高）；测试对拍暴力查询。
- **数据纹理档条带**：每个轨迹点只采样一次（滑动窗口），采样次数从每段 6 次降到约每点 1 次；测试与原算法对拍。

第三轮（GPU 画面验证）：
- **GPU 像素 A/B 测试**（`VisualRegressionTests`，PlayMode，自托管 Runner 的 Metal 上运行）：冻结游戏（`timeScale = 0`、固定自适应画质、相机 Snap），相机渲染到 RenderTexture，同一帧在选项关 / 开下各截一次。先断言画面有内容、同一选项两次截图逐像素一致，再比较。
- **不透明节点可见性压缩**（`ExpandNodesCompact`）：只追加屏幕内的不透明节点（组内原子 + 每组一次全局原子写间接参数）；半透明 / 加色链保持节点顺序。测试场景放了三条横穿视口边缘的长蛇，压缩前后**0 像素差异**。
- **圆盘 8 段**（等面积八边形）作为自适应画质 2 级以上的选项：与 16 段相比，GPU 档和数据纹理档都只有约 0.04% 像素差异 > 24/255。
- 这一轮历史截图曾通过 `ci-screenshots/<分支>` 发布；当前证据交付与恢复以 [CiEvidence](CiEvidence.md) 为准，旧发布流程不是新任务的推送授权。

第四轮：
- **物品网格增量更新**：`CellListGrid`（每格双向链表 + 行 → 节点映射，O(1) 增删）。`ItemGridSystem` 用自己的 ChangeLog（`SimTable` 支持多个独立 ChangeLog），只重插变化的行和 swap-back 空出的行；窗口移动 / 重置 / 变化超过 1/4 时整体重建。实测平均每 tick 约 5 行变化，ItemGridSystem **0.098 → 0.006 ms**，tick 均值 0.227 → **0.210 ms**。
- **物品网格存储改为分块链表**（每格一串 4 条目块，64 B = 一条缓存行；除末块外都满，删除用末尾条目补洞）：增删仍 O(1)，遍历按块连续读。与旧的"每条目一个链表节点"在同一数据上对比（`ItemGridQueryBenchmarkWithDeathDrops`，含插入后乱序重排的 churn 场景）：M5 Pro 上两者差异在 ±10% 内、互有胜负——约 1.2–1.5 万条目的工作集（0.4–1.8 MB）完全在 M5 的大缓存里，链表的指针跳转几乎不产生缓存未命中。分块希望在工作集超出缓存时改善局部性；这是待真机验证的假设，不能由块大小直接推出实际 cache miss 次数或手机收益。代价是内存 0.7 → 约 1.8 MB（按最坏情况预留块）。旧实现保留为测试内的参照副本，便于以后在真机上跑同一对比。
- **大半径分层**（`SpatialGrid` 可选第二层粗网格）：实测无收益（0.210 vs 0.217 ms）——8 m 格下一次查询本来就只覆盖 1–2 格。保留在 `SpatialGrid` 中作为选项（身体网格改为增量更新后不再使用）。
- **非 tick 帧复用渲染数据**：没有新 tick 的帧跳过裁剪 / 排序 / 头部数据构建和上传，只更新插值 alpha、视口和头部 / 眼睛。像素测试两档都 0 差异；60 fps 下渲染器主线程耗时 GPU 档 0.061 → 0.054 ms、数据纹理档 0.146 → 0.130 ms（约 −11%）。
- 顺带修复：数据纹理档条带 Job 的顶点 / 索引数组被安全系统误判为别名（有条带皮肤蛇可见时抛异常）；`TickPipeline.BeginTick` 中途异常时会先完成已调度的 Job，避免一个异常连锁导致之后所有访问报错。

第五轮（蛇身容器，面向移动端）：
- **轨迹点间距随半径变化**：`TrailState.Spacing` 每条蛇独立，`TrailSpacingFor(r) = max(0.4, r × 0.25)`（5 cm 量化）。细蛇保持 0.4 m；粗蛇转弯半径大，用更稀的点描述同一路径，点数按半径反比下降（同长度身体的点数、上传量、采样量都减少）。理想间距偏离当前超过 20% 时由 `PopulationSystem` 调用 `BodyStore.Respace` 重采样（保持路径与身体长度，`Version` 递增让 GPU 镜像重传）。`TrailSpacingPerRadius = 0` 恢复固定间距。
- **身体节点锚定到轨迹点 + 身体网格增量更新**：身体网格从“每 tick 按头部距离重采样全部节点 + 计数排序重建”改为 `CellListGrid`。每条蛇在每第 k 个轨迹点（全局序号 g % k == 0，k ≈ 节点间距 / 轨迹间距，带迟滞）放一个节点，键 = 该点在 BodyStore 的槽位；另有一个头部条目（`HeadBit`）每 tick 移动。轨迹点写入后不再移动，所以每 tick 每条蛇只新增头端出现的节点、删除尾端过期的节点：O(速度) 而非 O(长度)。行变化（生成、swap-back 移动）、轨迹重写（`Version`）、k 变化、进出窗口 / 存活状态变化、半径超出存储区间时整条蛇重插；窗口移动或重置时整体重建。
  - 条目半径按 ×1.05 向上取整存储（半径缩小到 1/1.1 以下或超过存储值才重插），查询用 Radius 列做精确判定；无敌状态改为查询时读 Info，所以都不触发重插。
  - 迎头判定改为“范围内有其他蛇的头部条目即为迎头，否则为撞身体；同类取最小行号”，与扫描顺序无关。
  - 所有蛇的删除在插入之前完成：本 tick 释放的 slab 可能已分配给别的蛇，旧主人的清理不能删掉新主人的键。
  - `CellListGrid` 增加 `BuiltOrigin`：读者使用内容对应的原点，窗口移动后到下次更新之间运行的 Job（如用上一 tick 网格的 AI）不会错位（物品网格原先也有这个小问题，一并修正）。
  - 测试 `BodyGridHoldsExactlyTheAnchoredNodes` 在生成、死亡、成长、重采样的长时间运行中，逐条目对拍“每个活跃蛇一个头 + 每第 k 个保留轨迹点一个节点”。
- 实测（M5 Pro，Burst）：BodyGridSystem 0.063 → 0.017 ms，tick 均值 0.218 → 0.18 ms。

第六轮：
- **轨迹包围盒增量化**（`TrailBounds`）：每 16 个轨迹点的 AABB 存在 slab 旁（`BodyStore.BlockBounds`），块写满时算一次；蛇的包围盒 = 约 Count/16 个盒子的并 + 最新未满块的直接扫描。轨迹 `Version` 变化时整条重建（`BoundsVersion` 列）。BodySystem 不再随总轨迹点数线性增长。
- **主线程系统**：PopulationSystem 改为主线程 `Run()` 的 Burst 扫描（只挑出需要扩容 / 重采样的行），0.026 → 0.001 ms；`ChunkPopulation.SetWindow` 在 chunk 范围不变时直接返回，WindowSystem 0.022 → 0.016 ms。主线程调度耗时 0.077 → **0.042 ms**，tick 均值 0.18 → **0.152 ms**。
- **大蛇基准**（`BigSnakeBenchmark` / `BigSnakeRendererCpu`，60 条质量 800–3000、半径约 3.9 m 的蛇）：间距随半径（平均 0.96 m）对比固定 0.4 m——存活轨迹点 53.8k → 22.5k（−58%），slab 占用 61440 → 30720 点（480 → 240 KB），每 tick 新增点（= GPU 轨迹上传量）45 → 19；GPU 档渲染器主线程 0.089 → 0.078 ms（复用开启 0.077 → 0.071 ms）。模拟 tick 本身无差别（0.132 vs 0.136 ms）：包围盒和身体网格都已是增量的，不再随点数增长。数据纹理档无收益：它每帧按身体节点（间距 ≈ 0.55 × 半径）在 CPU 上采样，与轨迹点间距无关。
- **数据纹理档固定开销**：条带网格只在本帧或上一帧有内容时才分配 / 提交 MeshData（大多数画面没有条带皮肤）；圆盘页加前缀子网格（256 / 512 / … 个圆盘，各自一份索引，不重叠——Unity 不允许子网格共享部分索引缓冲），按已用数量画最小前缀，不再对整页（最多 4096 个零半径圆盘、约 7 万顶点）做顶点处理。代价是整页索引内存约 +90%（页按需创建）。像素 A/B 测试 0 差异。收益在 GPU 顶点负载上，CI 只测主线程时间，而且 PlayMode 渲染计时在不同 CI 运行间波动可达 2 倍（未改动的 GPU 档也从 0.06 变到 0.12 ms），需要在真机上用 GPU 计时确认。

## 9. 验证入口与文档契约

- 最小模块用 [DriftSmoke](../Assets/SinglePlayerFoundation/Samples/DriftSmoke/DriftSmokeModule.cs)，完整可玩接入用[新玩法配方](NewGameplayIntegrationRecipe.md)。
- 逻辑/分层运行 `Tools/DotnetHarness/run.sh`；它不能证明原生 Burst/Job 安全或像素。原生脚本见 [local-unity-tests.sh](../Tools/ci/local-unity-tests.sh)，图形测试不能用 nographics 代替。
- 关注 EntityRegistry、WorldSnapshot、LevelScope、Pipeline、SessionHost、MobileInput、武器/粒子时间线等已有测试；从双 Session、同 Tick 恢复、row 重排、generation 回收和满容量验证身份/生命周期。
- 本轮文档片段和相对链接的验证范围见[配方验证说明](NewGameplayIntegrationRecipe.md#11-最小验收清单逻辑unity图形设备分开)。源文件中的接口摘录不创建第二套公共 API。

## 10. 当前实现与下一阶段

| 项目 | 当前状态 |
| --- | --- |
| SoA、普通/pooled 表、模块组合、Tick/依赖、SessionHost | 已有实现；实际 API 与约束见 §1–6 |
| 空间查询、身体池、共享战斗/技能/武器、渲染回退 | 多个现有消费者；每种能力边界和模式差异见专题文档与兼容矩阵 |
| 保存、重开、清关、输入中断 | 已有机制与模式专属测试；不是所有模式/主线程资源都可直接 raw snapshot |
| Stage A 语义对齐 | 文档与兼容性基线；不改 Runtime/asmdef 或经典 fixture |
| 组合 manifest/完整安装事务、统一 View binding、保存 envelope | 后续计划；不得在新游戏中当现成 API 调用 |
| 物理移动端验收 | 外部门槛：触摸、图形 API、热态持续帧时、内存/电量仍需真实设备 |

后续分阶段目标、依赖和回滚策略以[共享基座扩展计划](SharedFoundationSemanticExtensionPlan.md)为准；开源概念依据及许可边界见[调研](OpenSourceSharedFoundationSurvey.md)。不为了实现全部提案一次重写存档、实体布局、渲染或游戏枚举。

## 11. 接入时优先守住的决定

1. 继续自有 SoA + Jobs/Burst，不为本阶段升级 Unity 或迁移 Entities。
2. 稳定实体引用需要所属 Session/World 与完整 handle；时间线、关卡、内容、请求版本各司其职。
3. 命令是意图、事件是事实、查询是读取、cue 是表现提示；有界队列不会替使用者决定语义/溢出。
4. 结构改表和资源释放在安全窗口，访问声明与 Schedule dependency 都要真实完整。
5. 模拟配置、运行状态、呈现投影和保存检查点分开；经典保存/回放差异明确保留。
6. 新功能先在两个真实消费者中证明复用价值，运行时收益与设备结论由精确证据支持。
