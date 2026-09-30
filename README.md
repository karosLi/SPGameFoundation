# SPGameFoundation

Unity 2022.3 2D 单机小游戏基座（面向移动端）：数据导向模拟（SoA + Jobs + Burst）、网格碰撞与大 / 小地图衔接、GPU 驱动渲染、可插拔玩法模块。首个接入玩法：贪吃蛇。

- 架构设计：[Docs/Architecture.md](Docs/Architecture.md)
- 当前进度：**M1 基座骨架**

## 安装

把 `Assets/SinglePlayerFoundation` 放进 Unity 2022.3 工程，并在 Package Manager 安装：

| 包 | 版本 |
| --- | --- |
| `com.unity.burst` | 1.8.x |
| `com.unity.collections` | 2.1.x |
| `com.unity.mathematics` | 1.2.x |
| `com.unity.test-framework` | 1.1.33+（运行测试） |

Player Settings 中开启 `Allow 'unsafe' Code` 不是必须的（各 asmdef 已单独开启）。

## 目录与程序集

| 目录 | 程序集 | 内容 |
| --- | --- | --- |
| `Contracts/` | `SPF.Contracts` | `EntityHandle`、`TableKey` / `ColumnKey<T>` / `ResourceKey<T>`、`SimPhase`、`TickTime`、`SimRandom`、`ParallelQueue<T>` |
| `Runtime/World/` + `Runtime/Scheduling/`（asmref） | `SPF.Runtime.Core` | `SimWorld`、`SimTable`、`EntityRegistry`、`DestroyQueue`、`SnapshotBuffer<T>`、`ISimSystem`、`TickPipeline`、`FixedStepClock` |
| `L1Simulation/` `L2Gameplay/` `Presentation/` | `SPF.L1Simulation` 等 | 程序集已建立，内容在后续里程碑 |
| `Runtime/Composition` `Session` `Diagnostics` | `SPF.Runtime` | `IGameplayModule`、`ModeDefinition`、`SimSession`、`SessionHost`、`PerfHud` |
| `Samples/DriftSmoke/` | `SPF.Samples.DriftSmoke` | M1 冒烟模块 |
| `Tests/EditMode/` | `SPF.Tests.EditMode` | EditMode 单元测试 |

> 若工程中 `SinglePlayerFoundation/` 根目录已有 `SnakeFoundation.Runtime.asmdef`，请移走：各层现在都有自己的 asmdef，贪吃蛇玩法将放在独立的 `SnakeFoundation.Runtime` 程序集中（M4）。

## 跑起来看看（M1 冒烟场景）

1. `Create → SPF → Samples → Drift Smoke Module`，数量设为 10000。
2. `Create → SPF → Mode Definition`，把上一步的模块拖进 Modules。
3. 场景中新建空物体，挂 `SessionHost`（指定 Mode Definition）、`PerfHud`、`DriftGizmoView`（指定 Host）。
4. Play：HUD 显示 tick 频率、各 Phase 主线程耗时、同步等待时间、实体数量、每帧 GC 与 Draw Call；Scene 视图可看到点在移动。

## 写一个玩法模块

```csharp
public static class FoodKeys
{
    public static readonly TableKey Food = new TableKey("Food");
    public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Food, "Position");
}

public sealed class FoodModule : GameplayModuleAsset
{
    public override void DeclareData(WorldLayout layout) =>
        layout.Table(FoodKeys.Food, capacity: 16384).Column(FoodKeys.Position);

    public override void RegisterSystems(SystemRegistry registry) =>
        registry.Add(new FoodDriftSystem());
}

sealed class FoodDriftSystem : SimSystemBase
{
    public override SimPhase Phase => SimPhase.Move;
    public override void Declare(AccessDeclaration access) => access.Write(FoodKeys.Position);

    public override JobHandle OnTick(in SimContext ctx, JobHandle dependency) =>
        new DriftJob { Positions = ctx.Column(FoodKeys.Position), Dt = ctx.Time.DeltaTime }
            .Schedule(ctx.Count(FoodKeys.Food), 256, dependency);
}
```

规则：

- **声明即依赖**：`Declare` 中声明读写的 Key，调度器据此自动串联 / 并行 Job；不声明任何 Key 的系统是屏障（等待之前全部工作）。
- **结构变更只在 `ApplyCommands`**：创建实体用 `world.CreateEntity`；Job 中销毁实体写 `SimWorld.DestroyQueueKey` 的 `Writer`，下个 tick 开头统一执行（重复 / 过期句柄自动忽略，队列满时计数不崩溃）。
- **主线程系统**（非 ApplyCommands 阶段）访问数据前需先 `dependency.Complete()`。
- **表现只读快照**：`SnapshotBuffer<T>` 在 Snapshot 阶段写入，tick 结束时轮换，表现层在 `Previous` / `Current` 间按 `session.InterpolationAlpha` 插值。
- **一次同步**：`SessionHost` 在 `Update` 中调度 tick、在 `LateUpdate` 中 `Complete`，模拟 Job 与其他脚本的 Update 并行；表现脚本应在更晚的执行顺序里读取快照。

## 测试

Unity：`Window → General → Test Runner → EditMode → Run All`。覆盖：

- 实体注册表（分配、回收代数、满载、清空）
- 表的 swap-back 删除后句柄与列数据一致、销毁队列去重 / 溢出
- 并行队列超容量安全丢弃
- 系统按 Phase / Order 排序、声明式依赖下 Job 结果正确、快照轮换
- 固定步长时钟（累积、丢帧）
- 会话：种群稳定、同种子确定性、重开复用内存、Update/Sync 流程
- **稳态 300 tick 零 GC 分配**（`Is.Not.AllocatingGCMemory`）
