# 新玩法接入配方：先复用基座，再增加规则

> 所有新增玩法交付面向移动端，先遵守仓库根目录 [AGENTS.md](../AGENTS.md)。需要新的基座能力时，先比较官方/原始技术来源中的业界方案，记录移动端成本与适用边界，再用本项目实测决定。完整交付包括可玩闭环、原创资源、协调动画、武器/命中特效、能力回退及精确提交的测试/真实影像；不把桌面结果替代真机验收。

这份指南的目标是让下一个玩法更容易接入、容量和性能更可控、后续功能更容易扩展。Shooter、Guard、Flying Sword Horde、Belt Scroller 是四种不同压力的验证样本，价值在于证明同一套能力可以复用，不在于继续增加演示数量。

本文按 `62c5b7f`（2026-10-07）的实际源码核对入口和调用；其代码树与已恢复的远端 `e86ee87a7691282ae7e96713c315dbec5998c414` 相同。[精确验证清单](validation/NativePrecisionClosure-20261007.json)记录 1,050 .NET、1,084 原生 EditMode、154 graphics PlayMode 通过，另有 5/1 项原生跳过；不是物理 Android/iOS 验收。本文的文档检查也不等于重新运行这些原生测试。

[架构与真实 API](Architecture.md)、[九个经典玩法和四个移动示例兼容矩阵](FoundationCompatibilityMatrix.md)说明现状。本文下方的历史接入示例仍可使用；后续已实现的[可选 manifest 预检](OptionalCompositionPreflightValidation.md)、[安装回滚](CompositionRollbackValidation.md)、[视图时间线生命周期](validation/SessionViewLifecycle-20261007.md)和[保存 envelope](VersionedSaveEnvelope.md)各有独立接口与验证范围。通用异步资产租约仍是潜在需求，不能将其当作现有 API。新的执行计划、访问窗口和配置隔离进度见 [Latios 实施记录](LatiosImplementationProgress.md)。

- 只想跑通最小模块：看第 1 节
- 新建独立玩法：按第 2–4 节接入
- 只想增加武器、技能或配置：看第 5 节，不必复制另一个游戏
- 接人物、阴影、特效：看第 8–9 节
- 准备验收：按第 10–12 节检查

## 1. 用现有 DriftSmoke 跑通最小闭环

先确认 `IGameplayModule → WorldComposer → SimSession → SessionHost → 只读视图` 能跑，再加入战斗和 UI。

### 不写新代码的烟雾测试

1. 用仓库要求的 Unity 2022.3 LTS 打开项目。具体修订版看 [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt)，测试方式看 [README](../README.md#测试)。
2. 在 Project 窗口通过 **Assets → Create → SPF → Samples → Drift Smoke Module** 新建模块资源。将 Count 调成便于观察的值，例如 1000。
3. 通过 **Assets → Create → SPF → Mode Definition** 新建模式资源，把模块加入 Modules。默认模拟频率为 30 Hz。
4. 场景空物体挂 `SessionHost`，设置 Mode；另挂 `DriftGizmoView` 并把 Host 指向前者。
5. Play 后打开 Scene 视图并开启 Gizmos，可以看到移动、反弹和回收的点。它只画编辑器 Gizmos，没有 Game 视图渲染、可玩的 HUD 或打包后的画面，不是成品游戏场景。

现成代码：[DriftSmokeModule.cs](../Assets/SinglePlayerFoundation/Samples/DriftSmoke/DriftSmokeModule.cs)、[DriftGizmoView.cs](../Assets/SinglePlayerFoundation/Samples/DriftSmoke/DriftGizmoView.cs)。它们演示了：

- `DeclareData`：固定容量 Drifter 表，Position / Velocity 两列，三缓冲呈现快照
- `ApplyCommands`：主线程创建实体
- `Move`：Burst 并行移动
- `Resolve`：Job 通过销毁队列申请删除
- `Snapshot`：写入只读视图所需的数据；Sync 后轮换缓冲

### 在测试中直接创建同一个模块

下面是方法体片段，使用真实 API；所需命名空间为 `SPF.Runtime.Composition`、`SPF.Runtime.Session`、`SPF.Samples.DriftSmoke` 和 `UnityEngine`。它不是新框架，也没有替代 SessionHost 的第二套正式游戏循环。

```csharp
var module = DriftSmokeModule.CreateRuntime(1000, 40f, 4f, 0.002f);
try
{
    using var session = new SimSession(
        new IGameplayModule[] { module }, SessionSettings.Default, seed: 12345);
    session.Start();
    for (int i = 0; i < 60; i++) session.Step(); // 测试 / 回放入口
    session.Sync();
    var current = session.World.Resource(DriftKeys.Snapshot).Current;
    Debug.Log(current.Length);
}
finally
{
    UnityEngine.Object.DestroyImmediate(module); // 测试中释放临时资源
}
```

正式场景用 `ModeDefinition.Create(new[] { module }, settings)` 和 `host.Initialize(mode, seed)`。`SessionHost` 负责帧调度、Sync、后台挂起和销毁；不要再从自己的 Update 同时调用 `session.Update`。`WorldComposer` 已由 `SimSession` 调用，无须为每个游戏重写。

## 2. 独立玩法通常新增三个运行时程序集

例如新增 `Assets/MyGame/`。以下名字是建议的项目文件名，不是基座已经提供的新 API。

| 目录 / 程序集 | 最少放什么 | 不应放什么 |
| --- | --- | --- |
| `Runtime / MyGame.Runtime` | `MyKeys`、POD 列、配置、状态资源、`MyModule`、固定 Tick 系统 | Canvas、Camera、SpriteBatch、输入设备、视觉质量开关 |
| `Presentation / MyGame.Presentation` | `MyRenderer`、图集、只读人物适配、特效池与视觉预算 | 伤害、掉落、技能冷却、胜负、模拟随机数 |
| `Game / MyGame.Game` | `MyGameBootstrap`、`MyHud`、输入源到命令的适配 | 第二套碰撞或实体生命周期实现 |

直接参考已经运行的三个 [Shooter Runtime](../Assets/ShooterFoundation/Runtime/ShooterFoundation.Runtime.asmdef)、[Presentation](../Assets/ShooterFoundation/Presentation/ShooterFoundation.Presentation.asmdef)、[Game](../Assets/ShooterFoundation/Game/ShooterFoundation.Game.asmdef) asmdef 的引用方式，改名后删掉不需要的引用。最小模块需 Contracts、Runtime.Core、Runtime，以及所用的 Collections / Mathematics / Burst；使用空间查询或技能时再引用 L1 / L2。继承 `GameplayModuleAsset` 必须引用 `SPF.Runtime`，只引用 Runtime.Core 不够。

依赖方向保持为：Game → 自己的 Presentation / Runtime；Presentation → 自己的 Runtime + 公共 Presentation；Runtime → 所需公共 Contracts / L1 / L2 / Runtime.Core / Runtime。公共层不能反向引用 MyGame。Editor 菜单、EditMode、PlayMode 可以各有独立程序集，不计入这三个。

这是现有项目的接入组织方式，不是已经拆好的“五层五程序集”。五层责任是内核、共享能力、游戏规则、适配器、应用组合根；目前 SPF.Runtime 同时放模块声明契约与 Session/组合/诊断，Game 也同时放 Bootstrap 和输入/HUD 适配。引用 SPF.Runtime 不授权规则反向调用全局 Bootstrap；只读职责也不会由 asmdef 自动强制。完整物理引用见[架构 §1](Architecture.md#1-五层责任与真实程序集)。

### 第一轮实际要做的文件

1. 复制 DriftSmoke 的数据声明和四个小系统到自己的 Runtime，统一替换 namespace、Keys、模块名与资源名。不要让新游戏继续注册同一套 DriftKeys。
2. 先保留 Position、Velocity 和一个固定容量表。跑过自己的移动 / 重开测试后，再添加 HP、阵营、武器等列。
3. 将容量、速度、Tick 规则放入自己的配置，在创建 Session 时校验并冻结。不要在 Job 中直接读可变 ScriptableObject；Snake Capacity 和 RPG 的 section/技能槽现已[隔离 authoring 来源](RuntimeConfigSourceIsolation.md)；这不表示公开的 runtime 对象不可修改，也不自动覆盖其他玩法的 Bake 路径。对新配置仍应独立验证共享来源、两个 Session、非法输入和失败释放。
4. `MyGameBootstrap` 创建模块、Mode、Host，绑定 Renderer 和 HUD。创建时拥有的临时配置/Mode/Module，销毁时也由它释放；Host 释放 Session，不替它释放这些 SO。外部传入的共享资源保留原 owner；失败、重复绑定、disable 和 destroy 路径分别检查。
5. Renderer 先画简单 sprite，不必一开始接自然人物或 BAT。HUD 先实现状态、开始 / 暂停 / 重开，再按第 4 节接共享技能控件。

完整组装参考 [ShooterGameBootstrap](../Assets/ShooterFoundation/Game/ShooterGameBootstrap.cs)；有共享技能和自然人物时参考 [SvGameBootstrap](../Assets/SurvivorFoundation/Game/SvGameBootstrap.cs) 或 [BwGameBootstrap](../Assets/BrawlerFoundation/Game/BwGameBootstrap.cs)。不要复制它们的整套规则再换皮。

## 3. Module、Table、System 各负责一件事

模块实现 [IGameplayModule](../Assets/SinglePlayerFoundation/Runtime/Composition/IGameplayModule.cs) 的 `Id`、`DeclareData(WorldLayout)` 和 `RegisterSystems(SystemRegistry)`；`GameplayModuleAsset` 默认 Id 为类型名。当前没有通用 BakeConfig/RegisterRules/RegisterPresentation 钩子。以下是已有 DriftSmoke 的调用方式，省略的是现成文件中的系统实现，不是可单独编译的新模块：

```csharp
public override void DeclareData(WorldLayout layout)
{
    layout.Table(DriftKeys.Drifter, m_Count)
        .Column(DriftKeys.Position)
        .Column(DriftKeys.Velocity);
    layout.Resource(DriftKeys.Snapshot, new SnapshotBuffer<float2>(m_Count));
}

public override void RegisterSystems(SystemRegistry registry)
{
    registry.Add(new DriftSpawnSystem(m_Count, m_WorldSize, m_MaxSpeed))
        .Add(new DriftMoveSystem(m_WorldSize))
        .Add(new DriftRecycleSystem(m_RecycleChancePerTick))
        .Add(new DriftSnapshotSystem());
}
```

### 数据选择

- 跨 Tick 被锁定、跟踪或记录命中的对象，使用普通实体表和完整 `EntityHandle(Index, Generation)`，没有 Kind 字段。Index 是 Registry 槽位，不是 row；用所属 World.Registry.TryResolve 获取表和当前 row。普通表 swap-back / 排序后 row 会变；释放后旧句柄失活，再分配递增 generation。
- 短命且不需要稳定引用的弹丸、碎片或拾取物，可评估 `.Pooled()`。这类表没有实体 handle，使用 `Spawn`，不可直接把 row 当持久身份。Shooter 的敌人也采用 pooled 表，并自行维护 `ShooterEnemy.Id`；不能假设所有游戏表都能读取有意义的 handles。
- `.LevelScoped()` 用于 ClearLevel 时清除的表；对应资源指定 `levelScoped: true` 并实现 `IResettableResource`。ClearLevel 保留 session-scoped 数据，不替所有 system 调用 OnReset。所有 world-owned `IDisposable` 资源由 world 释放，不把同一个独占对象交给两个 World。
- 表扩展复用同一个 TableKey，调用 `layout.Table(key, capacity).Column(columnKey)`；同表重复声明取最大容量，同列 key 合并，资源 key 重复拒绝。相同字符串重新构造的 key 不是同一 key，新增列仍可能改变保存布局。
- 新功能尽量增加独立资源或 opt-in 扩展列。不要为一个可选武器修改所有旧角色结构和旧存档字节布局。

### 身份不只是一对整数

- Session/World 归属：两个 Session 的 handle 数值可以相同；View、缓存与迟到回调不能只比较 handle。
- 运行实体与 View 实例：部分英雄状态在 resource，渲染适配会用合成 key（如 `EntityHandle(-1, 1)`）；它不是 Registry 实体，不能拿来销毁/解析权威 row。不要因参数类型叫 EntityHandle 就假设所有 actor 都注册在表中。
- TimelineRevision：Restart / 成功恢复递增，恢复到相同 Tick 也变。它不保存，是表现失效标记。
- LevelVersion：只在 ClearLevel 递增，未进入存档，Restart 不靠它标识；不能替代时间线版本。
- ContentId / VisualId / schema 版本：分别说明内容、表现和保存字节含义；AccessKey.Id 只是进程内依赖编号。
- 异步请求版本：仅真实引入异步资源后再建立，旧回调即便指向还活着的实体也可能已失效；当前没有通用 AssetLease/RequestToken。

接入时先写明哪些版本变化清掉插值、输入锁存、命中提示、粒子和订阅。普通数组借用不能跨释放或缓冲轮换长期缓存。

### 调度选择

固定阶段为 `ApplyCommands → Input → Decide → Move → Body → SpatialBuild → Collision → Resolve → Spawn → Snapshot`。同阶段先按 `Order`，再按注册顺序。BeginTick 在所有系统之前播放有序销毁队列并压缩 pooled 表；阶段先后是调用顺序，不是自动完成全部前阶段 Job 的屏障。

1. 在 `Declare` 中声明实际读写的列、表和 Job 资源，Job 返回包含全部调度工作的 `JobHandle`，把收到的 dependency 传入 Schedule。Read 等最后 writer；Write 隐含 Read，等最后 writer 和之后的 readers。TableKey 不自动涵盖所有 ColumnKey。
2. 访问声明建立依赖，不会自动使后续主线程读取安全。系统内同步读取先前 Job 所写 NativeArray 时，必须完成覆盖这些数据的 dependency；空声明系统会成为 barrier，等待之前全部工作。EndTick 统一汇合，但不是每 Tick 只允许/只发生一次 Complete。
3. 结构性创建 / 销毁 / 排序统一放到安全的主线程结构变更窗口，必须已完成这个 World 所有交接的 Job，包括 OnCreate、tick 之间及已完成前序 Job 的 barrier；ApplyCommands 本身不保证安全。Job 申请销毁用 `SimWorld.DestroyQueueKey` 的 writer。不要因为存在名为 Spawn 的阶段，就假设可与前面的所有 Job 无条件并发改表。
4. 新增 Job 资源标记 `IJobData`，让开发期获取资源的检查覆盖它。新列入口 ReadColumn 返回原生只读 view，WriteColumn 在开发态要求 Write；旧 Column 和 Resource 仍只检查声明存在，不扫描所有字段/Job 指针，详见[读写/结构窗口](AccessAndStructuralWindows.md)；现有 SnapshotBuffer 未带此标记，写它仍必须声明 ResourceKey。配置和普通主线程 flow 不因此自动线程安全。
5. 热路径不增加 LINQ、闭包、每对象 List、每击 Instantiate / Destroy 或无上限循环。先预分配，再明确满容量的行为。

**组合失败合约：** 基座在分配前校验 SessionSettings/时钟，负责回收已成功交接到布局/World 的资源及已成功初始化的系统。新模块仍须清理尚未登记的构造中间值，失败的 OnCreate 自行完成私有工作并释放部分分配；不能假定会收到正常 OnDestroy。资源登记被拒绝时仍由调用者拥有。清理抛错不阻断其他 owner，保留原始异常与附加诊断；直接借用 World 建 Pipeline 不撤销任意状态写入。见[实际回滚与失败测试](CompositionRollbackValidation.md)。可选 ModuleManifest 已在两个武器模式接入，[预检记录](OptionalCompositionPreflightValidation.md)只描述实际声明的能力/布局切片，不是全量配置冻结或保存 schema。

详细实现：[WorldComposer](../Assets/SinglePlayerFoundation/Runtime/Composition/WorldComposer.cs)、[WorldLayout](../Assets/SinglePlayerFoundation/Runtime/World/WorldLayout.cs)、[TickPipeline](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs)、[SimWorld](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs)。

## 4. 输入与 HUD：控件发命令，Tick 决定能否释放

复用 [InputRouter](../Assets/SinglePlayerFoundation/Shell/Input/InputFrame.cs)、[MobileCombatHud](../Assets/SinglePlayerFoundation/Shell/UI/MobileCombatHud.cs)、[SkillSlots](../Assets/SinglePlayerFoundation/L2Gameplay/Skills/SkillSlots.cs)。最小步骤：

1. Runtime 注册自己的 `ResourceKey<SkillSlots>` 和 1–4 个 `SkillSlotDefinition`。槽位下标是命令 bit，技能 Id / IconId 是稳定配置标识，不是共享伤害枚举。
2. 固定 Tick 的输入系统调用一次 `AdvanceTick(playing)`。在请求存在且角色可行动、资源足够时调用 `TryActivate(slot, eligible)`；成功后才产生动作或效果。
3. 在适当的 Input / Move / Collision / Resolve 阶段执行真实逻辑。读取 `Activated` 的系统要在同一 Tick 消费，不把它当永久事件。
4. Game 层实现 `IMobileCombatHudSource` 的 `SlotCount`、`TickRate`、`Playing`、`ReadSlot`、`SlotLabel`。`ReadSlot` 返回 `GetSnapshot(slot, eligible)`，不自己算冷却。
5. 创建 Canvas，调用 `hud.Build(canvas.transform, source, preferLandscape)`，将 `hud.Input` 加入 InputRouter；图标需要时在 Build 前设置 `IconResolver`。
6. Sink 把输入锁存到自己的模拟命令；有一个拖动瞄准技能时用 `SkillInput.Latch(stored, next, aimSlot)`。Tick 消费 Pressed 后清除一次性 bit，Held 按规则保留。
7. `hud.Interrupted` 清空整份锁存命令；`hud.SkillCanceled` 调用 `SkillInput.CancelSlot`，只清该槽，保留别的手指和移动。

现成最短配置就是 [SvMobileSkills.Create](../Assets/SurvivorFoundation/Runtime/SvMobileSkills.cs)（所需命名空间为 `SPF.Contracts` 和 `SPF.L2.Skills`）：

```csharp
new SkillSlots(
    new SkillSlotDefinition(11, 2, SkillActivation.Tap, 90, 2),
    new SkillSlotDefinition(12, 3, SkillActivation.AimRelease, 120, 2));
```

在 Survivor 的 30 Hz 中是 3 秒 / 4 秒的顺序充能；Brawler 为 60 Hz，不能照抄 tick 数后仍宣称相同秒数。每槽每 tick 只能成功激活一次，最多 16 充能；重复花费不会重置已开始的充能。

真实适配：[SvHud](../Assets/SurvivorFoundation/Game/SvHud.cs)、[SvGameBootstrap](../Assets/SurvivorFoundation/Game/SvGameBootstrap.cs)、[BwGameBootstrap.MobileSource](../Assets/BrawlerFoundation/Game/BwGameBootstrap.cs)。新游戏需要自己的 eligibility、伤害和目标选择；共享 HUD 不提供通用技能执行器。

例如 Brawler 的真实接线如下。它是 `BwGameBootstrap.BuildUi` 内的片段，`root`、`State` 和 `MobileSource` 属于该游戏；移植时写自己的同等适配，不能原样塞进没有这些成员的新类：

```csharp
MobileHud = gameObject.AddComponent<MobileCombatHud>();
MobileHud.Build(root, new MobileSource(this), preferLandscape: true);
MobileHud.Interrupted = () => { if (State != null) State.Input = default; };
MobileHud.SkillCanceled = slot =>
{
    if (State != null) State.Input = SkillInput.CancelSlot(State.Input, slot);
};
InputRouter.AddSource(MobileHud.Input);
```

### 多指与中断不能留到最后补

- 摇杆与每个技能控件各拥有自己的 pointer；别的手指不能偷取、拖动或松开当前手势。
- 多个渲染帧可能没有模拟 Tick，短按和瞄准释放必须锁存，不能只依赖一帧的 `GetKeyDown`。
- 本地取消只取消对应槽；失焦、后台、整个 HUD 禁用、布局变更、菜单 / 死亡 / 重开，以及 Scripted/人工输入所有权切换，需要清空全部旧命令。
- `SessionHost` 的生命周期暂停和游戏主动 Pause 是独立原因。恢复焦点不应自动解除菜单暂停，也不应补跑后台积累的几十秒。
- 当前 `InputFrame` 只有一个 Aim。两个独立同时瞄准的技能需要显式扩展并版本化命令合约，不能悄悄共用方向。
- Belt 的 WaveClear 是“允许移动收集、禁用四项技能”的特殊阶段。参考当前适配，避免隐藏整个 HUD 后连摇杆一起失效。

更多规则和真实 UI 用例见 [MobileSkillHud](MobileSkillHud.md)。

## 5. 加新武器 / 技能，不改公共玩法枚举

### 只改已有武器参数：配置足够

例如“更多但更慢的飞剑”仍是已有机制，可在创建 Session 之前修改游戏自己的配置。下面使用现有工厂和字段，不新增公共枚举：

```csharp
var config = SvConfig.CreateFlyingSwordExample();
var settings = config.Settings;
var swords = settings.FlyingSwords;
swords.BaseCount = 14;
swords.Speed = 18f;
swords.Damage = 10f;
swords.Validate();
settings.FlyingSwords = swords;
config.Settings = settings;
var game = SvGameBootstrap.CreateFlyingSwordExample(config, seed: 7);
```

所需命名空间为 `SurvivorFoundation` 和 `SurvivorFoundation.Game`。调用者保留对传入 config 的所有权；场景释放后再销毁它。不要在已运行的 Session 中改这份配置并期待已烘焙状态同步变更。Id 相同但参数不同的 Session 也不应混用存档。

### 换一个手动技能：槽配置 + 小型消费者

在新游戏自己的配置中定义一个稳定正整数 Id、IconId、激活方式、冷却 tick 和充能数，例如 `new SkillSlotDefinition(101, 21, SkillActivation.Tap, 60, 1)`。自己的输入系统决定 101 对应什么规则；`IconResolver(21)` 找美术。基础库无需增加 `Fireball`、`Laser`、`Sword` 等全局枚举。

- 改范围、频率、伤害、数量：扩展本游戏配置，在初始化时校验
- 新命中形状：先组合已有 `CombatShapes` / `CombatSweep` / `GroundCombatQueries`
- 新持续状态：增加本游戏的 versioned resource / POD 列和 system，声明容量、重置与保存
- 新可复用几何或时间规则：经两个不同消费者验证后再下沉到 L1/L2，而不是先造庞大继承树

例如 [SvFlyingSwords](../Assets/SurvivorFoundation/Runtime/SvFlyingSwords.cs) 在原 Survivor 中增加独立资源、碰撞系统和只读呈现；[BwBeltScroller](../Assets/BrawlerFoundation/Runtime/BwBeltScroller.cs) 在原 Brawler 表上增加 ground / previous-ground / motion 列。它们复用原来的 Session、HUD、实体和反馈管线。

边界：当前示例的升级列表、标签和部分行为仍有游戏内的固定映射；“不修改公共枚举”不等于已经实现任意武器配置的自动注册、编辑器生成或通用技能图。完全不同的行为仍需要写游戏内消费者。

## 6. 碰撞、身份、历史和溢出先写清楚

### 先写队列语义，再选容器

Command 是意图，Event 是结算后的事实，Query 是不改变模拟的读取，presentation cue 是可有预算损失的提示。`EventQueue<T>` 能运输申请、候选或事实，名称不保证语义；先列生产/消费阶段、排序键、容量、overflow、保存和清空 owner。

- 当前队列是固定 `ParallelQueue<T>`，不是 NativeStream；并行 producer 使用 AsWriter().TryAdd，EventQueue.TryAdd/Raw.TryAdd 是单线程路径。多 writer 入队顺序不稳定，依赖完成后才读/排序/清空。
- `EventQueue(saved: true)` 原样保存队列顺序，不自动排序；`saved: false` 不写 payload，恢复清掉待播项。关键规则队列不能照搬视觉丢弃政策。
- 并行过量入队时，接纳子集也可能取决于调度；只排序接纳项不能保证同一结果，需容量证明或显式确定性接纳策略。
- DestroyQueue 的播放和保存会按完整 handle 规范化；过期/重复销毁被忽略。`Request` 是 void；需要知道是否接纳时用 writer.TryAdd。
- Clear 的消费者拥有队列清空责任；多个 View 需独立 cursor/只读批次，不能由第一个 View 清掉所有人的公共事件。


### Broad phase 后仍要 narrow phase

- 网格只给候选。用权威位置、半径和高度做窄相检测，不能以“进了同一个格”直接命中。
- 网格存目标中心时，形状 bounds 要扩最大目标半径。高速移动目标还要加入该 tick 的保守位移范围。
- 快速弹丸用 previous → current 的扫掠；双方都移动时用 `CombatSweep.Circles` 相对运动。飞剑代码每 tick 先做一次 O(敌人数) 的最大位移汇总，再局部查询。
- `BeamHitsCircle` 是有限圆头 capsule，halfWidth 是半宽；环带包含内外边界，内半径为 0 可表示圆盘。
- Belt 使用 `(x, groundDepth)` 和独立高度区间做碰撞。显示 Y = groundDepth 投影 + height，屏幕 Y 不能代替真实地面纵深。
- 选择最近或最早命中时，比较距离 / TOI，平局比较稳定 Id 或完整 handle。不能用 row、网格访问顺序或并行写入顺序决胜。

源文件：[CombatShapes](../Assets/SinglePlayerFoundation/L2Gameplay/Combat/CombatShapes.cs)、[CombatSweep](../Assets/SinglePlayerFoundation/L2Gameplay/Combat/CombatSweep.cs)、[GroundCombatQueries](../Assets/SinglePlayerFoundation/L2Gameplay/Combat/GroundCombatQueries.cs)、[SvFlyingSwords](../Assets/SurvivorFoundation/Runtime/SvFlyingSwords.cs)、[BwBeltSystems](../Assets/BrawlerFoundation/Runtime/Systems/BwBeltSystems.cs)。

### 一次攻击只命中一次，用完整 handle 和有界 history

[HitHistory](../Assets/SinglePlayerFoundation/L2Gameplay/Combat/HitHistory.cs) 保存 `(Index, Generation)`；同一次挥击的多段形状或整次飞剑出击共用一个 scope。新动作调用 `ActionTimeline.Begin()`，再以非零 PulseId 开始 scope。死亡、换关、源复用时释放或重置。

队列伤害必须遵守 **Check → 成功入队 → TryRecord**，且同一 scope 只允许一个 writer。队列满了不能提前花掉 history 或穿透次数；history 满了拒绝新伤害，不驱逐旧目标，不动态扩容。使用当前表查候选或通过 Registry 验证句柄，HitHistory 本身不负责存活检查。

`ActionTimeline` 提供 tick 时间窗、动作 pulse 和中断规则，`TickInputBuffer` 提供有截止期的一条缓冲命令；它们不自动重建跨过的动画姿势。近战权威 probe 应在固定 Tick 的动作姿态中采样。

### 为每个有限缓冲明确 policy

| 对象 | 接入时必须明确的满容量行为 |
| --- | --- |
| 实体 / pooled row | `CreateEntity` 返回 Null 或 `Spawn` 返回 -1；拒绝生成并记录，不写非法 row |
| 命中 / 销毁队列 | 检查 `TryAdd`；按玩法决定拒绝、保留待处理状态或延后重试，不能静默丢关键终态 |
| HitHistory / scope | 无扩容、无替换；新增伤害拒绝，重复目标仍判 Duplicate |
| 掉落 | Belt 当前拒绝最新掉落，已有奖励不被挤掉；记录 RejectedDrops |
| 视觉反馈 / FX / 数字 | 可按预算合并、丢弃或驱逐低优先级，不改变已结算的伤害和奖励 |

基座没有替所有游戏选好所有 overflow policy。配置要校验有限值、容量、乘法和波次数量溢出；热路径拒绝计数宜饱和累加。用“恰好满 / 超一项 / 重复项 / 恢复后仍满”测试政策，而不只测试正常容量。

## 7. 存档不是只存位置：同配置合约与生命周期

需要完整续算的模式使用 [SimSession.WriteSnapshot / ReadSnapshot](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs)。权威资源实现 `ISnapshotResource`，跨 Tick 有状态的 system 实现 `ISnapshotSystem`；重开同时实现对应 reset。

- 保存动作时间线、pulse、命中历史、完整目标 handle、冷却/充能、飞行阶段、已缓冲输入、玩法时钟，以及影响以后规则的计数
- 网格和排序 scratch 可以不保存，但必须在恢复后的首次读取前从权威数据重建
- 特效、拖尾、飘字和视觉 IK 状态一般不属于模拟存档；恢复 / 重开 / session rebind 时清掉旧视觉身份
- `SnapshotBuffer<T>` 是呈现三缓冲，**不是**持久存档；它按 row 匹配，若要在排序/删除后稳定插值，记录并匹配身份
- 相同模块布局、容量、seed、Tick 规则和不可变配置才是续算前提。现有 World format 1 检查表/资源顺序、容量及 raw 元素大小等，没有保存列 key/type 的语义身份；Pipeline 使用系统类型短名。等大小换义/换列或同名系统可能逃过校验，不能仅凭文件能读完就认为兼容
- SnapshotGaps 只拒绝缺少 ISnapshotResource 的 IJobData；这是诊断启发式，不能证明全部可变状态已保存。Signals 就是未标 IJobData 的 Job Native mailbox，普通主线程权威状态也不会自动检测。新增功能的 schema magic/version、内容指纹、reset/rebuild/drop 策略要逐项写清
- 飞剑 V1 指纹包含全部飞剑规则和宿主 `SvVariant`；相同飞剑参数不能跨 Classic / Guard / FlyingSwordHorde 恢复。Belt V1 校验其容量和波次配置；SkillSlots 校验完整槽定义
- 当前 WeaponRuntime 指纹包含 VisualId 和握持/socket 参数；规则与视觉在新设计中区分，不等于旧存档已经分离这些版本。
- 这些 opt-in 检查没有替所有历史配置提供完整指纹，也没有提供通用旧存档迁移器。继续保留已存在的 Classic fixture 和布局隔离测试；不要把同一版本内生成的 A/B 快照比较称为历史 fixture 兼容
- Snake 的 GameState、RegionPopulations、ReplayBuffer、SnakeQuality、Signals 未实现完整保存资源合约，相关 system 也无 ISnapshotSystem；已有输入回放测试不能证明完整中途 Session 存档。其他模式的证据范围逐项见[兼容矩阵](FoundationCompatibilityMatrix.md)
- `SimSession.ReadSnapshot` 遇到非法数据会重启 session 后抛异常，不保留恢复前的进行中对局；直接调用底层 world 的恢复不能假设事务回滚，应遵守其 reset-before-use 合约
- 新模式可使用[有界 envelope 与显式完整布局](VersionedSaveEnvelope.md)，先检查规则/schema/runtime 身份和长度/完整性，再进入旧 raw 恢复；预检拒绝不动当前对局，已通过 framing 的非法 raw 内容仍按明确的重启失败语义处理。新增权威资源须给自己的版本与完整新 recipe，不能挪用两个旧武器模式的身份。
- raw Native 快照是同构检查点，不自动成为跨版本/跨平台长期格式。保存 envelope、稳定 schema ID 和迁移入口属于后续计划，不在旧 writer 前随意插入字段

验收时从“空闲”与“飞剑在途 / 空中 / 连招缓冲 / 充能中”分别抓快照；在同配置新 Session 续跑同输入，比较完整快照。随后逐项修改容量、技能定义、伤害、速度、宿主模式，确认该功能明确保护的配置差异会被拒绝。

参考 [SharedCombatStage2](SharedCombatStage2.md)、[FlyingSwordHorde](FlyingSwordHorde.md)、[LandscapeBeltScroller](LandscapeBeltScroller.md)。

## 8. 只读呈现、横竖屏与角色后端要分开选择

### View 的硬边界

[SessionHost](../Assets/SinglePlayerFoundation/Runtime/Session/SessionHost.cs) 默认让下一个 Tick 与渲染重叠，并在 LateUpdate 开头完成当前工作。常规 renderer 应在它之后读取；从 Update、HUD getter、测试或直接 Render 方法读取 NativeArray 时先 `Session.Sync()`，不能仅凭“平时在 LateUpdate 调用”就省掉独立入口的保护。

视图只读取完成的数据、插值和生成批次。新增质量开关不得改变模拟输入、AI、碰撞、伤害、掉落或随机序列。经典 Snake 的 AdaptiveQualityController 会改变 AI 决策间隔并写入 ReplayFrame，是明确保留的历史模拟输入，不能把它描述为纯视觉档或直接推广给新游戏。直接读取飞剑状态的参考是 [SvSwordPresentation](../Assets/SurvivorFoundation/Presentation/SvSwordPresentation.cs)。

### 方向和安全区属于具体游戏

- Shooter、Guard、Flying Sword Horde 可优先竖屏；Belt 优先横屏。玩法菜单名不等于全局强制方向。
- HUD 用实际 viewport / `Screen.safeArea` 布局；至少测 720×1280、1280×720、狭长屏和合成刘海 / Home inset，连同真实 raycast 命中区一起测。
- `MobileCombatHud.Build(..., preferLandscape)` 是布局意图；实际大小按屏幕宽高重排，不设置 `Screen.orientation`。
- 只有明确选择 **SPF → Shooter → Apply Portrait Mobile Settings** 才修改共享项目的 `PlayerSettings.defaultInterfaceOrientation`。它会影响同一构建中的其他玩法，不要在每个入口自动调用。
- 角色展示场景的横屏 letterbox 适配不等于竖屏游戏 UX 已完成。

### 三条人物路径，不混淆名称

| 路径 | 实际工作 | 当前边界 |
| --- | --- | --- |
| 普通 SpriteBatch | sprite 实例通过 GPU-driven 或 DataTexture 绘制 | 没有顶点权重蒙皮；两种 sprite tier 也不是两种 BAT 后端 |
| 自然人物 CPU/Burst cutout | `NaturalCharacterRig` / `NaturalPoseJob` / `GameplayCharacterPresenter` 求 14 骨姿态、脚底固定和视觉 IK，再输出分件 sprite | 已接 Brawler / Survivor 自然角色；每 attachment 是四边形，不是 GPU weighted skinning |
| Weighted BAT | 骨骼矩阵纹理 + 真实两权重顶点；vertex 路径逐顶点采样，可选 compute 路径每角色先算 palette，再由 vertex 蒙皮；有真实 CPU weighted mesh fallback | 独立 3 骨静态父链、两骨 IK、最多 2 clips / 每 clip 60 帧 / 128 vertices / 256 instances 的有界验证资产；不是任意 rig 导入器 |

自然人物的最小接法是沿用 [GameplayCharacterPresenter](../Assets/SinglePlayerFoundation/Presentation/Animation/GameplayCharacterPresenter.cs)，由游戏 renderer 提供稳定身份、ground root、朝向、移动/受击/动作状态，保留低成本 sprite fallback。Survivor 的自然人物选择有上限，其他可见怪物仍画普通 sprite；减少精细姿态数量不能让怪物消失。Belt 阴影跟地面，身体跟 height，不能一起抬走。

Weighted BAT 入口是 **SPF → Characters → Create Or Update Weighted BAT Scene**，不是上述自然人物开关。`new BatCharacterBatch(asset, capacity, preferCompute: true)` 只是请求 compute；实际能力 gate 允许才选择，失败按独立支持条件回到 vertex，再到 CPU。`forceCpu: true` 优先。GLES 当前走 CPU weighted fallback；缺图形设备不绘制。要记录 `ActiveBackend`，不能把“请求 GPU”当成“实际用了 GPU”。

加权资产的矩阵插值有收缩 / 剪切限制，GPU IK 是视觉限定链；权威命中仍留在模拟。没有测目标设备前，不能断言 compute 比 vertex 或 CPU 更快。精度、纹理格式、缓冲数、shader 和相机/缩放范围都是独立门槛。

详细边界：[GameplayNaturalCharacters](GameplayNaturalCharacters.md)、[NaturalMotionValidation](NaturalMotionValidation.md)、[BatCharacterValidation](BatCharacterValidation.md)、[BatComputePaletteValidation](BatComputePaletteValidation.md)。

### 阴影档位也不能夸大

[PoseSilhouetteShadow](../Assets/SinglePlayerFoundation/Presentation/Animation/PoseSilhouetteShadow.cs) 在加载时用真实 cutout alpha 烘焙固定光向、平面地面的姿态轮廓；最多 32 个样本。只在 kind / facing / 拓扑匹配且所有 attachment 角点误差不超过 0.12 模型单位时选样本。任意动态 IK 或不支持的姿态必须降到 Blob；最低档可为 None。

它不是动态 shadow map，不投到墙面或别的人物上。当前实际自然人物游戏采用接触 Blob；支持姿态轮廓 atlas 的示例仍是独立展示。不要把已有 atlas 当成全姿态实时阴影系统。

## 9. 反馈先结算，再按视觉预算呈现

复用 [CombatVfxPool](../Assets/SinglePlayerFoundation/Presentation/Combat/CombatVfxPool.cs) 与 [VfxProfile / VfxBudget](../Assets/SinglePlayerFoundation/Presentation/Combat/VfxProfile.cs)：

1. 模拟先产生真实命中 / 伤害 / 死亡反馈；视图读取后决定是否生成可见效果。
2. 初始化时建 atlas、pool、batch 和上传缓存，预热到允许峰值。Emit / Draw 热路径不创建每击 GameObject、材质、Text 或字符串。
3. 固定 pool 容量，同时限制每帧接纳、活跃效果、sprite 数和透明面积代理；重要角色反馈有保留通道，次要 spark / glow 先降级。
4. event key / visual seed 来自已发生的事件身份；视觉去重与合并不得消耗模拟 RNG。
5. 长期 telegraph（Guard 双环、Shooter 射线）有独立固定预算，不能随命中特效洪峰消失。
6. 飞剑拖尾和飘字参考 [SvSwordPresentation](../Assets/SurvivorFoundation/Presentation/SvSwordPresentation.cs)：固定采样和数字池、每帧有限接纳，断点清拖尾，不把瞬移画成伤害光束。
7. 每个 renderer 分开报告整个场景的 `SpritesDrawn` / `BytesUploaded` 和局部 FX 指标。32 B × sprite 数是有效 payload 估算；DataTexture 按真实页 / prefix 纹理大小上传，不等同于硬件带宽。

替换正式美术时沿用 [SmoothSpriteArt / SpriteAtlas](../Assets/SinglePlayerFoundation/Presentation/Sprites/SmoothSpriteArt.cs) 的加载阶段处理、透明 RGB 和 gutter 约定；它们没有提供自动商业资产下载或通用离线导入器。颜色 atlas、可读 CPU 副本、源像素、临时超采样大图和 native / mesh / driver 成本分别计量。

详见 [MobileVisualPolish](MobileVisualPolish.md)。

## 10. 用四类玩法检查扩展是否真正有效

| 验证类型与真实入口菜单 | 主要复用 / 验证什么 | 先读的文件 |
| --- | --- | --- |
| Shooter：**SPF → Shooter → Create Or Update Scene** | 高速飞行物、扫掠、射线、波次、升级、竖屏输入、FX 和两档 sprite | [Module](../Assets/ShooterFoundation/Runtime/ShooterModule.cs)、[TickSystem](../Assets/ShooterFoundation/Runtime/ShooterTickSystem.cs)、[Renderer](../Assets/ShooterFoundation/Presentation/ShooterRenderer.cs)、[测试报告](ShooterValidation.md) |
| Guard：**SPF → Survivor → Create Guard Example Scene** | 同一 Survivor 模块切守点规则，双环去重、信标接触、怪群和血条 | [Config](../Assets/SurvivorFoundation/Runtime/SvConfig.cs)、[AnnularSkill](../Assets/SurvivorFoundation/Runtime/SvAnnularSkill.cs)、[Systems](../Assets/SurvivorFoundation/Runtime/Systems/SvSystems.cs)、[测试报告](HordeGuardValidation.md) |
| Flying Sword Horde：**SPF → Survivor → Create Flying Sword Horde Scene** | 在途持续武器、稳定目标、相对扫掠、单出击历史、技能 HUD、自然人物/廉价 fallback | [剑规则和系统](../Assets/SurvivorFoundation/Runtime/SvFlyingSwords.cs)、[只读剑呈现](../Assets/SurvivorFoundation/Presentation/SvSwordPresentation.cs)、[验证合约](FlyingSwordHorde.md) |
| Belt：**SPF → Mobile Gameplay → Create Landscape Belt Scroller Scene** | 地面双轴 + 独立高度、近战 probe、连招输入缓冲、四槽 HUD、追击、掉落、波次收集 | [配置/资源](../Assets/BrawlerFoundation/Runtime/BwBeltScroller.cs)、[Systems](../Assets/BrawlerFoundation/Runtime/Systems/BwBeltSystems.cs)、[Game/HUD](../Assets/BrawlerFoundation/Game/BwGameBootstrap.cs)、[验证合约](LandscapeBeltScroller.md) |

补充入口：**SPF → Mobile Skill HUD → Create Landscape Brawler Scene**、**SPF → Mobile Skill HUD → Create Portrait Horde Scene** 用于隔离测试共享 HUD；**SPF → Characters → Create Natural Motion Showcase** 用于姿态、固定脚点和支持姿态阴影。它们与完整 gameplay 验证各有用途。

新增第五种玩法时，优先问“要增加哪一项基座能力，现有消费者还能否使用”，而不是“再复制哪一个场景”。小规模 Brawler shared-combat 仍有有界线性/成对处理；Belt 增加了局部网格，但极端全重叠仍会访问很多邻居。容量上限和正确性支持都不是无限扩展或真机吞吐保证。

## 11. 最小验收清单：逻辑、Unity、图形、设备分开

### A. 源码与逻辑

- [ ] Runtime / Presentation / Game 的实际依赖无反向引用；五层职责、模块 Id、key 名和 schema 不冲突
- [ ] 双 Session 同值 handle 不串绑；同 Tick 恢复、ClearLevel、重开、禁用/重绑、共享资源退出各有 owner 和失效断言
- [ ] 相同配置、种子、输入序列的续算一致；不要由单平台结果推导所有 CPU/Burst 架构逐位一致
- [ ] 开始 / 进行 / 暂停 / 胜负 / 重开 / 回菜单均有断言
- [ ] 命中跨 row sort、swap-back、generation 回收不串目标；精确相切、零长、高速与移动目标覆盖
- [ ] queue / history / entity / loot / FX 全满时符合书面 policy
- [ ] 进行中的快照恢复、已声明配置不匹配拒绝、原 Classic fixture 和 opt-in 隔离保持
- [ ] 视觉档位、FX 开关、角色后端不改变模拟快照

运行仓库脚本：

```sh
Tools/DotnetHarness/run.sh
```

它生成与 asmdef 对应的 .NET 工程并运行 EditMode 逻辑测试，但 Unity / Jobs / Burst / 渲染 API 是桩。桩构建成功可以检查 C# 和程序集边界；不能证明真实 Burst 编译、Job 安全、原生分配、触摸调度、shader 或画面。

本轮文档验证对 `62c5b7f` 的真实源文件生成 .NET 桩程序集，独立编译 DriftSmoke、SkillSlots、飞剑配置和架构扩展列片段，并核对接口及带原类上下文的模块/HUD 摘录。具体命令、执行/仅编译边界和结果见[文档验证记录](validation/FoundationSemanticDocs-20261007.json)。本文没有把完整新游戏加入 Assets，也没有在本次文档检查中启动 Unity；不能称作“新游戏原生运行通过”。

### B. 真实 Unity 与图形

- [ ] 实际 Unity 2022.3 EditMode，启用真实 Jobs/Burst 与安全检查
- [ ] 实际 Canvas raycast、摇杆 + 技能双指、瞄准释放/取消、输入所有权切换
- [ ] 后台 / 失焦 / disable / 布局改变中断，恢复后无旧按键或追帧爆发
- [ ] GPU-driven 与 DataTexture 都有真实相机 + HUD 截图；读像素并人工检查
- [ ] 自然角色、近战权威 tip、落地/阴影、廉价 fallback 与遮挡排序分别检查
- [ ] Weighted BAT vertex / compute / CPU 记录实际 backend、数值/像素差和跳过原因
- [ ] 多次创建 / 释放 / 重开，资源与池无累积；捕获和编码不混入测量窗口

使用 Unity Test Runner，或按环境选择 [local-unity-tests.sh](../Tools/ci/local-unity-tests.sh) / [unity-tests.sh](../Tools/ci/unity-tests.sh)。图形测试不能以 `-nographics` 运行。安装 API 的 DLL 编译也只是 compile-only；capability skip 明确是未验证，不是通过。

重点用例：

- [SessionHostTests](../Assets/SinglePlayerFoundation/Tests/EditMode/SessionHostTests.cs)、[MobileSkillControlTests](../Assets/SinglePlayerFoundation/Tests/EditMode/MobileSkillControlTests.cs)、[SharedCombatTests](../Assets/SinglePlayerFoundation/Tests/EditMode/SharedCombatTests.cs)
- [SvFlyingSwordTests](../Assets/SurvivorFoundation/Tests/EditMode/SvFlyingSwordTests.cs)、[SvFlyingSwordPlayTests](../Assets/SurvivorFoundation/Tests/PlayMode/SvFlyingSwordPlayTests.cs)
- [BwBeltScrollerTests](../Assets/BrawlerFoundation/Tests/EditMode/BwBeltScrollerTests.cs)、[BwBeltScrollerPlayTests](../Assets/BrawlerFoundation/Tests/PlayMode/BwBeltScrollerPlayTests.cs)
- [GameplayCharacterTests](../Assets/SinglePlayerFoundation/Tests/EditMode/GameplayCharacterTests.cs)、[BatComputeTests](../Assets/SinglePlayerFoundation/Tests/EditMode/Characters/BatComputeTests.cs)

### C. 分配测量与性能证据

使用测试专用 [ManagedAllocationProbe](../Assets/SinglePlayerFoundation/Testing/ManagedAllocationProbe.cs)：创建 probe、绑定 delegate 和预热均在窗口外；测量前后都运行 retained-array 正对照与 empty 对照。正对照必须看得见分配、空窗口必须为零，否则测量不可用，不能记 0。

- .NET 返回 `ManagedBytes`；Unity 当前返回 `AllocationSamples`，即 GC.Alloc 事件数量
- `Collections` 是完成的 gen-0 collection 次数增量，和事件数量 / 字节数不是一回事，也不能直接归因到被测对象
- 同步当前线程零样本不证明全帧、工作线程、native、GPU 或 driver 零分配
- 数值格式化、截图、Readback、PNG/视频编码、日志与断言字符串都应放在窗口之外
- 对真实 frame-time、GC、native/texture 常驻与加载峰值分别采集；不要调大阈值来掩盖回归

更正原因见 [AllocationMeasurementCalibration](AllocationMeasurementCalibration.md)。最新已记录检查点见 [MobilePresentationAndHudCheckpoint](MobilePresentationAndHudCheckpoint.md)；自然人物、飞剑、Belt、compute BAT 的后续结果分别见前述专题报告。最终密集负载和完整回归仍以中央实际运行的提交、环境、窗口、结果文件为准，本文不填尚未产出的数字。

### D. 设备门槛：当前外部阻塞

物理 Android / iOS 的触摸、安全区、Vulkan / Metal / GLES fallback、持续帧时、发热和功耗验证仍需可用真机及其构建/签名环境，当前没有据此完成的设备验收。桌面软件图形可以帮助验证实际 Unity 画面正确性，不能代替手机 GPU/CPU 性能。这里没有“所有机型 60 FPS”“任意密度零 GC”或“任意骨架导入即用”的承诺。

## 12. 提交一个新玩法时，交付这七样

1. 按需要组织 Runtime / Presentation / Game 程序集，列出实际引用与五层责任边界
2. 一个最小可玩入口；若有 Editor 菜单，记录其准确名称和生成场景路径
3. 配置、容量、溢出策略、权威状态与呈现状态的清单
4. 同输入 / 快照 / 满容量 / 生命周期 / 多指的测试
5. 两档实际游戏画面和可核对的后端信息
6. 带正负对照、单位和测量范围的性能结果；未测和阻塞单列
7. 对公共层改动的理由与至少两个消费者；如果只是新规则，公共层应尽量不动

做到这些，新增玩法才真正证明这套基座更通用、更可维护，而不只是仓库里多了一个 Demo。

## 可选的规则组合接入（Stage E）

[两种真实玩法的规则组合](ComposedAbilityRules.md)展示 Survivor 范围脉冲的数据变体/推开规则，以及 Brawler 踢击/治疗的数据变体/命中赚取治疗额度。显式新工厂复用现有移动 HUD、动作和武器；旧工厂保持控制组。新状态有独立完整保存配方，不借用旧武器配方冒称覆盖。原生连续影像和真机状态以该文档链接的精确验证记录为准。

## Stage G 公共验收夹具与移动预算

[公共验收与预算报告](SharedAcceptanceAndMobileBudgets.md)提供复用既有 NUnit/SPF.Testing 的真实工厂夹具、Shooter/Platformer 两个消费者和独立的测试模块。测试模块只证明外部规则接入，不是成品可玩 demo。报告区分配置目标、软件实测和 Unknown/null；保留分配阳性/空对照与现有门槛，Android/iOS 真机验收仍为 Pending。
