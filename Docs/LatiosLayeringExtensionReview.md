# Latios 对 SPGameFoundation 分层与扩展性的增量启发

研究日期：2026-10-07。本文是源码研究与后续提案，没有引入 Latios、修改运行时、升级 Unity 或执行新基准。它补充[开源共享基座调研](OpenSourceSharedFoundationSurvey.md)，不重开已经完成的[语义扩展 A–G](SharedFoundationSemanticExtensionPlan.md)。

## 1. 结论与基线

**保留当前 SoA + Jobs/Burst + Module/Table/System/SessionHost。下一步优先让扩展者能看清执行计划、正确声明访问和提交冻结配置；只有真实需求出现后，才扩展结构命令。** Latios 的主要价值是把引擎能力做成可组合的工具，并把执行上下文和资源责任纳入 API。这个方向能借鉴，不要求变成 Entities 项目。

本轮推荐四项，详见 §5：

1. **P1：可解释的管线计划。** 在已有 preflight 之外，导出实际系统顺序、读写冲突和 barrier 的来源；先不更改调度。
2. **P1：渐进的访问与结构变更检查。** 基于现有 AccessGuard，区分借用的读/写权限，检查危险结构操作的执行窗口；不另造自动依赖引擎。
3. **P2：两消费者的配置冻结编译。** 先给 Snake/RPG 的 authoring 别名补反例测试，再统一最小编译协议；不重复建设已存在的 save envelope。
4. **P3：需求驱动的有界类型化结构命令。** 若新玩法确需 Job 生成/迁移实体，先做一个模块自有队列；证明第二消费者需求后才共享化。

### 1.1 可复核的版本

| 对象 | 本轮固定基线 | 含义 |
| --- | --- | --- |
| Latios 代码 | [ae2262afd5c80ac4850fef23c9e7d4a971fabf53](https://github.com/Dreaming381/Latios-Framework/commit/ae2262afd5c80ac4850fef23c9e7d4a971fabf53) | 2026-09-27，查询时默认分支最新提交；[v0.16.1 release](https://github.com/Dreaming381/Latios-Framework/releases/tag/v0.16.1) 同日发布 |
| Latios 官方文档 | [626d1a37f432cd31e4a3faf44fc3cb07c928cc17](https://github.com/Dreaming381/Latios-Framework-Documentation/commit/626d1a37f432cd31e4a3faf44fc3cb07c928cc17) | 2026-09-27，Update to 0.16.1；文档与代码分别固定，避免 main 漂移 |
| SPF 源码 | 本地 `c790b0b7045b868c449fbd41a1631bb023ae3dfc`，等价远端 `fc1c10df57e179b7da5d5ae2b457cb0de2719738`；tree `0c6917d4c16455e8e0c7135074225ae2c3e10e27` | 本文逐项对照此源码。A–G 已有实现；1,432 项 .NET 逻辑通过是软件证据，精确头原生执行和物理 Android/iOS 仍须分别报告；项目证据入口见[本轮组合记录](MobileFeedbackCoordinationValidation.md) |

Latios [package.json L4–13](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/package.json#L4-L13) 要求 Unity **6000.3.8f1**、Entities **1.4.8**、Entities Graphics **1.4.21**、Burst **1.8.30**。[兼容指南](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Installation%20and%20Compatibility%20Guide.md#L70-L74) 明确当前支持 Unity 6.3 LTS/Entities 1.4.x。对 SPF 的 Unity 2022.3 来说，这涉及引擎、包、数据模型、生命周期和工具链迁移，不是新增一个 asmdef 即可。

**后续兼容性核查补充：** 上段描述的是当前上游整包的直接接入成本，不表示 Unity 2022 无法使用或适配 Latios。v0.11.5 官方最低版本是 2022.3.36f1，存在可供 2022.3.62f2 验证的历史基线，也有 Kinemation 旧渲染路径可作为新版回迁参照。三条路线、真实 API 差异与最小验证计划见[Unity 2022 回迁评估](LatiosUnity2022BackportAssessment.md)；当前保留 SPF 架构是项目范围选择，不能替代对兼容 fork 的技术评估。

[LICENSE.md](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/LICENSE.md) 指向 Unity Companion License；[第三方 notices](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/THIRD%20PARTY%20NOTICES.md) 还列出不同来源的许可。它不是 MIT/Apache 式通用移植许可。[Unity 当前许可原文](https://unity.com/legal/licenses/unity-companion-license) 对 Unity 使用范围、作品衍生修改及第三方 notices 有约束。本文仅做概念分析，不复制实现；未来若摘取代码、shader、资源或原生插件，应按实际文件再次核对许可。这里不对具体商用/衍生归属作法律结论。

## 2. 先扣掉我们已经完成的能力

| Latios 方向 | SPF 当前已经具备 | 本轮真正剩余的问题 |
| --- | --- | --- |
| 显式 bootstrap/模块选择 | WorldComposer、ModeDefinition、可选 ModuleManifest、能力/数据所有者/容量预检 | 系统的最终执行图与同步原因尚未形成同一份可检查计划；无需再做模块注册器 |
| World/scene 生命周期与扩展状态 | WorldLayout 注册类型化 ResourceKey；Session/Level scope；安装回滚、系统退出、View 时间线与重绑 | 检查扩展者使用数据时的权限和安全窗口，区别于再做一次生命周期框架 |
| 固定 Tick、结构提交与恢复 | TickPipeline、DestroyQueue 稳定播放、异常 handle 所有权、snapshot/save envelope | 非 destroy 结构请求只在真实新需求下考虑；Latios 不替代已有保存协议 |
| 可组合玩法/脚本 | 已有两个实际消费者的 AbilityAdmission、各自权威规则与保存配方 | 没有证据支持再建通用脚本 VM、技能图或 vtable |
| 可替换算法/表现 | Stage F 空间/AI 夹具、真实表现生命周期与后端探针 | 使用既有契约继续验证新扩展，不把“再加公共接口”当作成果 |
| 接入验收 | 19 个冷组合基线、Stage G 公共场景适配与预算证据 | 新增扩展应接入这些夹具，不能另建较宽松验收 |

对应当前记录：[组合回滚](CompositionRollbackValidation.md)、[预检](OptionalCompositionPreflightValidation.md)、[调度异常所有权](TickFailureOwnershipValidation.md)、[保存 envelope](VersionedSaveEnvelope.md)、[规则组合](ComposedAbilityRules.md)、[后端契约](StageFBackendContracts.md)、[公共验收](SharedAcceptanceAndMobileBudgets.md)。架构总览应与这些后续实现记录一并阅读，实施状态以精确提交对应的源码和证据为准。

## 3. Latios 的关键机制，究竟能学什么

### 3.1 显式组合：安装一个能力，同时交代它改变什么

[CoreBootstrap](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/CoreBootstrap.cs#L12-L49) 把 scene manager、local ticking 分成显式安装入口。[SuperSystem](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/SuperSystem.cs#L44-L143) 允许父组控制是否运行、显式添加子系统；关闭排序时，添加次序就是执行次序。

值得借鉴的是**能力的执行位置可见、可审查**。但安装函数不是事务系统。其[官方说明](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Core/Customizing%20the%20Bootstraps.md#L21-L49) 明确 installer 也可能禁用被替换系统；不能把“模块可选”推导成可安全热卸载或自动回滚。

SPF 不需要换成 SuperSystem 继承树。现有 Phase → Order → 注册序已经明确，保留此规则，把扩展模块的最终顺序和依赖原因输出出来即可。以后如确需功能组，先做冷路径的注册/诊断分组，不让 group 静默重排 RNG、碰撞优先级或存档系统顺序。

### 3.2 Blackboard：扩展状态有世界归属和生命周期

[BlackboardEntity](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/BlackboardEntity.cs#L8-L43) 同时持有实体和所属 LatiosWorld 引用，而非单独一个裸实体编号。[LatiosWorld](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/LatiosWorld.cs#L138-L180) 区分世界 blackboard 与可重建的 scene blackboard；重建后还有新场景回调。

这相当于告诉扩展者：配置、共享运行状态和临时场景状态，应由明确的作用域拥有。SPF 的 [WorldLayout.Resource](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/World/WorldLayout.cs#L44-L68) 与 [SimWorld.ClearLevel](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs#L96-L119) 已能表达 Session/Level 分工，**无须再添加一套 Blackboard 类或全局服务定位器**。

区别必须保留：Latios scene blackboard 是实体重建；SPF LevelScoped 资源主要是原地 OnReset，并通过 LevelVersion 失效缓存。二者不能机械等同。SPF 的 EntityHandle 仍只在所属 Session/World 内有效，外部绑定继续使用现有 Session/时间线归属，不为“统一身份”改旧 snapshot 布局。

### 3.3 Pseudo-components：资源访问、依赖和释放是一个完整协议

Latios 的 [ICollectionComponent](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Components/IManagedStructComponent.cs#L20-L32) 定义带输入依赖的 TryDispose。[GetCollectionComponent](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/LatiosWorldUnmanaged.cs#L358-L434) 分 read/write，获取时合并依赖；[系统退出追踪](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/LatiosWorldUnmanaged.cs#L731-L796) 记录最终 JobHandle；[释放路径](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/LatiosWorldUnmanaged.cs#L799-L854) 选择合并或完成依赖。

这不是“自动就安全”。[官方错误说明](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Core/Automatic%20Dependency%20Management%20Errors.md#L8-L35) 说明系统边界外的访问仍需手动提交 JobHandle；嵌套更新和后创建系统也有上下文限制。

SPF 现有静态 Declare + DependencyTracker 更符合预分配、可检查和固定负载的目标，不应同时引入第二套隐式依赖图。可迁移的是更明确的读写借用和错误诊断，见 §5.2。当前资源不可随意热替换，World 释放前由 Session 完成 Job；没有必要为了模仿 SetCollectionComponent 建立运行时资源替换 API。

还要注意 [IManagedStructComponent.Dispose](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Components/IManagedStructComponent.cs#L6-L18) 只在移除时调用，设置新值不会替旧值释放。它不能直接被当成 SPF “每次换资源必释放”的所有权契约。“struct 内装托管引用”也不等于全部存储或初始化无 GC。

### 3.4 SyncPoint：延迟结构提交有价值，顺序必须由玩法定义

[SyncPointPlaybackSystem](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Systems/_Essentials/SyncPointPlaybackSystem.cs#L48-L89) 统一播放多种命令缓冲，缓冲之间按请求次序；[DestroyCommandBuffer 的 parallel writer](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Containers/CommandBuffers/DestroyCommandBuffer.cs#L650-L669) 要求 sortKey。

SPF 已在 [PlaybackDestroys](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs#L254-L272) 按完整 handle 规范化已接纳请求的销毁次序。这比盲目沿用线程入队顺序更重要。不过 [ParallelQueue.Writer](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Contracts/Collections/ParallelQueue.cs#L85-L103) 用原子抢槽，容量满时不同调度仍可能接纳不同集合；事后排序不修复 admission 差异。这里是源码可见的过载边界，未在本研究中执行原生压力复现。未来 spawn、transfer、custom command 的适用时机、平局和容量耗尽必须逐项定义，不能因为 Latios 有通用 command buffer，就引入 object/delegate 载荷的万能总线。

此外，Latios 的 [TickedLocalSuperSystem](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Systems/Ticking/TickedSuperSystems.cs#L90-L123) 明确分开 sync point、input、history、simulation；其 [Ticking 文档](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Core/Ticking.md#L65-L85) 说明超前模拟/丢弃重算的代价，并指出当前 local mode 不提供通用 snapshots/rollback。这里的重算不能替代 SPF 的保存、恢复与 TimelineRevision，亦不应自动改变现有输入延迟语义。

### 3.5 Authoring/Baking：扩展内容先编译成运行数据

[SmartBaker](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Authoring/SmartBaker.cs#L13-L78) 分初始采集和稍后处理；[SmartBlobber 请求](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Authoring/SmartBlobberAPI.cs#L121-L154) 有输入过滤、baking-only 中间状态和完成后解析的边界。

SPF 已有局部 Bake/Clone 与 D/E 的冻结内容指纹；仍可推进的是跨两个实际消费者的**完整冻结与校验责任**，而非复制 Entities Baker/BlobAssetReference。冷路径可以接受适量托管开销，运行时只消费冻结值和固定 Native 数据。编辑器热重载、增量缓存、跨进程内容仓库均不是本轮必要目标。

## 4. 其他模块：哪些是模式，哪些是生态依赖

| 模块与实际证据 | 可以迁移的设计 | 本项目当前不直接采用的原因 |
| --- | --- | --- |
| Psyshock：[数组入口](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Spatial/Builders/Physics.BuildCollisionLayer.cs#L243-L314)、[候选对 processor](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Spatial/Queries/Physics.FindPairs.cs#L12-L42) | 数据视图 → 算法 kernel → 结果，由调用方调度；L1 算法不反向拥有 SessionHost | FindPairs 是 AABB 候选阶段，不是完整命中规则；[asmdef](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Latios.Psyshock.asmdef#L4-L13) 仍依赖 Core/Transforms/Entities；数组 API 不代表包可独立摘用 |
| Kinemation：[安装替换](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Utilities/KinemationBootstrap.cs#L15-L65)、[culling 插入位置](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Systems/KinemationSuperSystems.cs#L239-L275) | 插入点写清前置数据、允许输出及阶段位置；新表现扩展继续只读模拟 | 会禁用/替换现有 Unity rendering/skinning/LOD 等系统；并非现有 Sprite/BAT 后端的一个无侵入开关 |
| Myri：[bootstrap/shutdown](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/MyriAudio/Utilities/MyriBootstrap.cs#L12-L63)、[runner 所有权](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/MyriAudio/AudioECS/AudioEcsInterfaces.cs#L13-L61) | 跨线程后端区分只读配置 owner 和动态 runner；停止生产、消费完成、释放的顺序明确 | 有 LatiosWorld/AuxEcs/Unity audio 耦合；现有移动音频先完成自身真实音频与设备验收，不因研究重写 |
| Unika：[运行分派](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Unika/Internal/SourceGenStaticAPI.cs#L236-L272)、[初始化反射](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Unika/Internal/AssemblyManager.cs#L84-L160) | 若注册/绑定重复达到真实痛点，可生成现有契约的胶水代码 | Burst function pointer 不等于全程无反射；DynamicBuffer、脚本 ID、引用重映射、AOT/调试均有成本。现有两消费者能力组合不足以支持引入通用脚本运行时 |

尤其不从名称推断性能。Psyshock [ScheduleParallelUnsafe](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Spatial/Queries/Physics.FindPairs.cs#L650-L708) 明确牺牲部分安全保证；“parallel”“ECS”“Burst”不构成可直接超越现有网格、直接 AI 或移动后端的证据。

## 5. 四个有边界的增量提案

以下名称是提案描述，不是已经存在的 API，也不意味着已获准开工。

### 5.1 P1：把实际管线变成可解释计划

**现有落点：** Runtime.Composition、Runtime.Scheduling 和 Editor/Testing。Manifest 已描述能力/数据；[TickPipeline](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs#L49-L77) 已构造稳定排序与访问声明，而且 [GetSystem/GetAccessNames](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs#L102-L104) 已暴露基本信息。无需重新声明一份容易漂移的“理想管线”。

**最小增量：** 从实际注册结果生成冷路径报告：系统稳定显示名、来源模块、Phase/Order/注册序、read/write key、因哪条 key 冲突形成依赖、显式/空声明 barrier。报告需区分调用顺序与完成屏障，不能画成每一 Phase 都等待完成。来源模块若暂时无法可靠归属，显示 unknown，不用类型命名空间猜测。

**收益：** 第三方新模块加入后，开发者能回答“为什么在这里执行”“为什么失去并行”“依赖了哪个模块”，避免靠 Order 魔数试错。仍沿用现有依赖求解与顺序；不自动拓扑重排。

**验收：** 以现有 19 组合记录计划；加入测试模块后只出现预期节点/边；两次冷启动计划一致；测试 R→W、W→R、R→R、barrier 与同 Order 注册平局。报告仅在初始化/按需生成，不在 Tick 拼接字符串；既有 raw bytes、玩法 hash、注册序不变。诊断开销与正式基准分开。

**成本/停止条件：** 首版是报告与契约测试，范围较小；若报告没有帮助发现真实接入歧义，不继续做可视化编辑器或依赖图框架。

### 5.2 P1：更精确的读写借用和结构安全窗口

**源码缺口：** [AccessDeclaration](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/Scheduling/AccessDeclaration.cs) 区分 R/W，但 [SystemEntry.Allowed](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs#L318-L337) 合并为 bool[]；[AccessGuard](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/World/AccessGuard.cs) 只能检查“是否声明”，不能拒绝“声明 Read 却获取可写容器”。[SimContext](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SinglePlayerFoundation/Runtime/Scheduling/ISimSystem.cs) 还暴露完整 World。CreateEntity、ClearLevel、SortRows 等入口要求安全窗口，但目前主要由调用者遵守。这是检查能力的边界，不是已证明现有游戏发生数据竞争。

**最小增量分两步：**

- 先给新扩展提供显式 read/write 获取入口和只读视图，开发态检查声明模式。按两个实际系统试接；保留旧入口兼容并标明它的检查局限。仅加一个 `Read()` 方法而仍返回任意可写对象，不算完成权限收窄。
- 再给创建、销毁、压缩、重排/清关等结构操作增加开发态合法窗口检查。窗口必须基于“相关在途工作已经完成”和操作类别，不能只看 Phase 名称；现有无声明 barrier 完成依赖后仍可做合法结构工作，OnCreate 与 Tick 间编辑也须保留。禁止通过在每次 getter 里偷偷 Complete 来消除错误。

**收益：** 新模块误用能在接入时失败；不要求扩展作者理解每个隐藏调用关系。资源依赖继续由原有 Declare/返回 JobHandle 管理；本轮不支持异步资源热替换。

**验收：** Read 后取写入口拒绝、Write 后读/写允许、漏声明拒绝、合法 barrier 允许、在途 Job 时结构变更拒绝、SimSession.Sync/Pipeline.EndTick 成功完成相关 Job 后允许、异常后窗口状态恢复、双 Session 不串上下文。原生 Jobs safety 下验证真实在途工作；harness 只能证明检查逻辑，不能替代并发验证。旧 raw fixture/19 组合不变，release 稳态无新增分配。

**成本/停止条件：** 这里的 Sync 特指 Session 完成管线工作；SimWorld.Sync 只是资源 OnSync 回调，并不自己完成 Job。不把它包装成完整内存安全系统。提前缓存的 NativeArray、unsafe 指针、外部 Unity 资源仍可能绕过入口；要明确可检测范围。若必须全库修改访问签名才能得到价值，缩回新模块试点再判断。

### 5.3 P2：补齐两个游戏运行配置的来源隔离

**源码缺口：** [SnakeRuntimeConfig](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/SnakeFoundation/Runtime/SnakeRuntimeConfig.cs#L190-L212) 的 Capacity 仍引用 authoring section；[RpgRuntimeConfig.Bake](https://github.com/karosLi/SPGameFoundation/blob/fc1c10df57e179b7da5d5ae2b457cb0de2719738/Assets/RpgFoundation/Runtime/RpgRuntimeConfig.cs#L82-L105) 对 Dungeon/Loot/Capacity 和 HeroSkillSlots 仍保留源对象/数组引用。大量数值已值拷贝、Native 表也已创建，不能说它们“完全没有 Bake”；但 `Bake` 这个名字本身不能保证所有运行读取都被冻结。

**最小增量：** 首阶段的“冻结”只承诺运行定义与 authoring 来源隔离，不承诺 runtime 对象在类型/内存层面完全不可写；现有 public 字段和 NativeArray 仍可被修改。先明确哪些字段是运行必需，做“创建 Session 后修改原配置”的差分测试。若设计要求新一局固定配置，则仅将对应 section/数组值复制为运行表示；列出稳定内容 ID、校验错误、容量估算和 owner。待两个消费者确有相同流程后，再提炼冷路径的 Validate → Freeze → Build 约定；不先发明通用配置数据库。

**与 D/E 的区别：** D/E 已为特定 weapon/ability 组合提供实际冻结值指纹和严格保存描述，继续复用。此项覆盖其外的配置别名与编译边界；不是重做 envelope，也不能把裸 Native bytes 变成跨平台格式。

**验收：** 改源 SO/嵌套数组不改变已开局结果；新 Session 接受新配置；相同输入有相同运行定义/内容标识；NaN、越界引用、容量乘法溢出在分配前拒绝；构建失败释放已创建资源。比较两个真实游戏、旧配置和旧 raw fixture；若某项旧行为确实依赖实时 authoring 修改，单独版本化/说明，不能悄悄改变。

**成本/停止条件：** 冷启动复制和占用增加，需量化峰值而非宣称零成本；Native backing 生命周期沿用 World owner。只做需要冻结的字段，不做热重载、磁盘缓存、全量代码生成或编辑器框架。

### 5.4 P3：真实需求出现后，再试非销毁结构命令

**现有边界：** DestroyQueue 已是有界并行请求和已接纳集合的稳定播放，过载 admission 的条件见 §3.4；创建/生成/清关仍用同步安全窗口。没有 Job 侧生成需求时，这个边界本身是合理选择。

**触发需求示例：** 一个新召唤/分裂玩法需要多个 Job 产生 spawn 意图，同时要求固定容量、相同 seed/输入得相同生成结果。先在该玩法模块持有类型化、预分配请求队列，由它注册的 ApplyCommands 系统消费，不改 World 的存储模型。

**必须先定义：** 请求发生/生效 Tick；Session/时间线和完整 owner handle；稳定键（例如 tick、明确生产者 ID、owner generation、局部序号）；同键策略；满容量拒绝政策；目标失效；部分生成失败；请求状态是否进入 snapshot。有限容量下，原子抢槽的“先到先得”本身可能非确定，即使事后排序也补不回丢失请求；可用按生产者预留槽或另一种受测确定性 admission，不能只加 Sort。

**验收：** 不同 Job batch/调度/输入顺序得到相同被接纳集合和播放结果；满容量、重复、过期 owner、同 Tick restore、ClearLevel、重开和销毁均按定义执行；计算包括排序、等待、队列内存和完整 Tick。回归现有 destroy 的 byte/顺序语义；对现有队列另用压力测试界定过载契约，不能在研究或通用化过程中静默改变旧丢弃策略。第二个真实消费者复用成功且节省接入成本后，再决定是否提炼共享 helper。

**成本/停止条件：** 语义和测试成本最高，排在前三项之后；无真实消费者就不做。不增加任意 object payload、运行时反射 dispatcher、热插拔模拟模块或统一世界事件总线。

## 6. 用一个外部扩展示例证明“无需修改内核”

下列是建议的验证任务，不是本次交付的新玩法。选择一个有真实产品需求的小模块，例如有界区域影响/光环；只在该模块的 Runtime、Presentation、Game/测试程序集里写代码。

1. **自有数据：** 注册自己的表/扩展列、固定配置和 Session/Level 资源。状态实现需要的 reset/snapshot/dispose；不能要求 SimWorld 加按玩法命名的字段。
2. **自有算法与系统：** L1/L2 适用 kernel 只收数据视图，规则系统声明依赖、返回所有 JobHandle。应用组合根显式选择模块；允许修改启动组合，不要求仓库完全零修改。
3. **自有表现适配：** 读既有只读结果/cue，复用 Sprite/粒子等后端；保持 Session、owner generation、时间线绑定。若需要真正的新渲染后端，沿 Stage F 的现有契约验证，不能用一个 mock 测试宣称 GPU 已接通。
4. **执行已存在的基座夹具：** 启动拒绝与回滚、同数值跨 Session handle 隔离、满容量、清关/重开、同 Tick 恢复、独立消费者、禁用/重绑、质量档隔离、退出资源基线。新增本模块独有语义断言即可，复用 A–G 夹具。
5. **记录接入成本：** 公共内核、全局玩法 enum、旧游戏源码必要修改数为 0；列出新文件、初始化/常驻/队列容量、真实访问图、提交与验证。达不到零改动时，先解释缺的是哪条具体契约，再决定公共扩展，不反过来为了“零改动”制造万能接口。

这比新增多少接口更能证明扩展性。一个替身模块通过只能证明协议，真实玩法、原生 Job/Burst/graphics 与设备持续帧时仍分别验收。

## 7. 采用边界与下一步决策

- **可直接吸收的概念：** 显式安装、实际执行计划、作用域状态、带权限的资源访问、编译后内容、算法与调度分离、窄阶段扩展点。用本项目代码独立实现，保持既有语义。
- **需要专项试验才考虑：** 异步资源 lease、跨线程新后端、生成式注册、非 destroy 命令、多 World 或新资源生命周期。当前限制不自动等于 bug。
- **当前不采纳：** Entities 迁移、Unity 6 升级、Latios PlayerLoop/World 替换、Unika 通用脚本、Kinemation 整包渲染替换、无证据的“ECS 更快”和“全平台确定”。
- **先行门槛：** 当前精确提交原生与移动设备验证仍按原路线推进；研究不会降低阈值，也不把桌面、API 编译或第三方样例当手机证据。优先先做 §5.1 报告与 §5.2 的反例测试，能发现实际问题才扩接口。

### 研究验证范围

已在线核对最新 release/commit/package/license，阅读固定版本 Core、bootstrap、blackboard、资源依赖/释放、sync point、ticking、baking 以及相关四模块实现，并与上述 SPF 基线的真实声明、执行、资源、配置和 A–G 测试文档对照。未安装 Latios、未运行其示例/性能测试、未进行 Unity 升级或移动端实测。所有性能收益、线程安全改进和接入成本改善均是待本项目验证的假设；源码证据只支持所描述的机制。
