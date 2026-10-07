# SPGameFoundation 与 Latios 的 Unity 2022 整合方案

日期：2026-10-07。对象：继续使用 **Unity 2022.3.62f2**、面向 Android/iOS 的 SPGameFoundation（下称 SPF）。本文把[Latios 分层与扩展性分析](LatiosLayeringExtensionReview.md)和[Unity 2022 回迁评估](LatiosUnity2022BackportAssessment.md)合成一份可决策、可分阶段实施、可撤回的方案。

**推荐保留 SPF 的权威 SoA 模拟，分两条线推进：一条完善现有扩展契约；另一条用 Latios 0.11.5 建立独立的 Unity 2022 兼容基线，再回迁确有需求的新能力。先证明 Core/Psyshock，随后决定是否接成 SPF 的可选后端。Kinemation、Myri 和完整 ECS 替换分别立项评估。**

本文交付的是方案。没有安装包、创建 Unity 实验工程、修改运行时代码、改变项目依赖、升级 Editor 或启动新原生测试。下文拟议目录、接口名、开关和阶段均未实现，也不表示后续实施已获授权。

## 1 决策摘要

### 1.1 这次整合要得到什么

1. **更容易扩展 SPF：** 新模块能查看真实执行顺序、声明并检查读写权限、冻结自己的运行配置，沿用已经存在的组合、生命周期、保存和验收契约。
2. **真正验证 Latios 的 2022 路线：** 独立项目证明历史版本能够在精确 Editor 上导入、运行和构建；需要新版功能时，交付明确范围的兼容补丁与回归证据。吸收设计与库回迁是两种成果，分别验收。
3. **以收益决定接入：** Psyshock 先承担有明确输入/输出的查询或候选对计算，SPF 保留命中、伤害、技能、时间、身份与保存权威。只有完整成本与语义测试过关，才成为 opt-in 后端。
4. **保持可退出：** 默认网格、Sprite/BAT、SoundPlayer 和现有存档继续可用。关闭实验选项、撤回适配包或回到上一依赖锁都有明确路径；不能以禁用安全检查或删减移动回退换取“兼容”。

### 1.2 三种部署形态与选择门槛

| 形态 | 实际改动范围 | 适用条件 | 本方案位置 |
| --- | --- | --- | --- |
| 独立研究工程与兼容分支 | 精确版本的 Latios、测试内容、兼容补丁；不进入 SPF 产品工程 | 先回答“2022 能否运行、哪些新版功能值得回迁” | **第一步**；即使后来不接入 SPF，仍有独立成果 |
| SPF 可选算法或表现后端 | 增加适配程序集、依赖锁和 opt-in 组合；仍由 SPF 模拟裁决 | 相同玩法结果，完整成本可接受，设备与维护条件满足 | **优先的产品接入形态**；先 Psyshock，其他模块逐项批准 |
| 全面迁移到 Entities/LatiosWorld | 改写存储、查询、系统注册、时钟、结构提交、baking、保存与工具链 | 有当前架构难以满足的产品需求，且迁移收益覆盖长期成本 | **本轮不选择**；不能作为局部适配失败后的隐性升级路线 |

选择旧版本控制组，是为了先区分“历史支持能否重现”与“新版能力的回迁成本”。直接把 0.16.1 的最低 Editor 改成 2022，会同时混入 Entities 内部桥接、BRG、音频、生成器和内容差异，难以定位失败。旧版本也有代价：上游不维护旧发行线，需要自己筛选相关修复；旧版通过不能称为“0.16.1 全功能已兼容”。[上游维护政策](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/README.md#L271-L281)

## 2 当前起点与不可混用的证据

### 2.1 已经完成的 SPF 能力

运行时代码基线为远端 [fc1c10df57e179b7da5d5ae2b457cb0de2719738](https://github.com/karosLi/SPGameFoundation/commit/fc1c10df57e179b7da5d5ae2b457cb0de2719738)。两篇前置研究发布后，运行时与 Packages 未因此改变。A–G 的代码已实现，不再当作新方案的待开发工作：

| 已有能力 | 本方案怎样复用 |
| --- | --- |
| 19 个冷组合与既有 raw fixture | 作为新增适配器的兼容基线，不重写基线遮蔽变化 |
| WorldComposer 安装回滚、可选 manifest/能力与数据预检 | 声明新后端的能力、容量、资源 owner；新增信息只补不足 |
| Session/Level 生命周期、View 重绑与时间线失效 | 桥接缓存、视图、声音和迟到结果按同一时间线失效 |
| 调度异常与已返回 JobHandle 的所有权 | 桥接系统返回完整 JobHandle；失败不能提前释放在途缓冲 |
| 同运行域 save envelope 与明确配方 | 使用已有 schema/content/runtime 身份，不直接保存 ECS Entity 或 CollisionLayer |
| 两游戏的能力准入与规则组合 | Latios 不另建一套技能、冷却或伤害裁决 |
| Stage F 后端契约、Stage G 共享验收与移动预算记录 | 新后端加入同一组适用夹具，不另设较宽松的“Latios 专用通过线” |

依据：[架构](Architecture.md)、[组合回滚](CompositionRollbackValidation.md)、[预检](OptionalCompositionPreflightValidation.md)、[调度所有权](TickFailureOwnershipValidation.md)、[保存](VersionedSaveEnvelope.md)、[规则组合](ComposedAbilityRules.md)、[后端契约](StageFBackendContracts.md)、[共享验收](SharedAcceptanceAndMobileBudgets.md)。

截至本方案：**1,432 项 .NET 逻辑通过、真实 Unity API/netstandard2.1 编译、19 冷组合和 64 项 Python 检查通过**；精确最终源的完整 Unity/Burst/graphics 验证仍待闭环，Android/iOS 物理设备仍待验收。历史 `e86ee87` 的原生绿灯不覆盖后来改动。[当前组合记录](MobileFeedbackCoordinationValidation.md)

### 2.2 尚未证明的事

- Latios 0.11.5 在 **2022.3.62f2** 的精确导入、原生运行与 IL2CPP 构建。
- 任意 Latios 模块比 SPF 现有后端更快、更省电或更省内存。
- 任意平台之间浮点逐位确定、旧 raw 数据跨架构可用。
- 新旧 Kinemation 的资源、shader、原生插件在全部目标设备可用；Myri 后端在 2022 的原生音频行为可用。
- SPF 的通用异步资产 lease、运行中模块热插拔、任意历史存档语义迁移。这些仍是潜在独立需求。

## 3 五层架构与依赖方向

五层表达责任；它们不是五个必须串行调用的类，也不是五个已经存在的一一对应 asmdef。

```text
                    L5 应用组合根
          Bootstrap / WorldComposer / SessionHost
              选择规则、后端、配置、生命周期
                    /                  \
                   v                    v
          L3 游戏规则模块          L4 表现与平台适配
         Snake / RPG / Sv / Bw      输入、HUD、渲染、音频
             |       \                 |        |
             v        \                |        | 只读已完成状态/事实
        L2 共享玩法能力 <----命令意图----+        |
        技能/动作/命中/成长                       |
             |                                  |
             v                                  v
          L1 计算与存储内核 <---------------------+
       Contracts / SoA / Jobs / 固定 Tick / 算法

可选 Latios 适配器位于算法/表现边界：
SPF 数据视图 -> 专用桥接 -> Latios 计算 -> 规范化结果 -> SPF 规则
SPF 已结算事实 -> 专用表现桥接 -> 可选 Kinemation/Myri
```

图中的命令与结果箭头是运行时数据流，不表示 `L2` 程序集引用 `L4`。游戏规则可以直接用内核；组合根可以同时创建规则和适配器；配置、保存、诊断有跨层协议。**表现只读是一条语义约束，不能仅靠程序集引用图自动保证。**

实际公共依赖以[当前 asmdef 清单](Architecture.md#12-物理依赖由现有-asmdef-决定)为准：`L1Simulation -> Contracts`；`Runtime.Core -> Contracts`；`L2Gameplay -> Contracts + L1Simulation + Runtime.Core`；`Presentation -> Contracts + L1Simulation + Runtime.Core`；`Runtime -> Contracts + L1Simulation + Runtime.Core + L2Gameplay + Presentation`。`Shell` 还依赖 Runtime/L2Gameplay。不得把它简写成一条严格的 `L5 -> L4 -> L3 -> L2 -> L1` 编译依赖链。

### 3.1 每层的增量边界

| 层 | 保留的所有权 | 本方案允许的增量 | 不应进入这一层 |
| --- | --- | --- | --- |
| L1 内核 | 存储、句柄、固定容量、依赖和算法数据 | 可解释执行计划、开发态读写/安全窗口检查、窄数据视图 | LatiosWorld 全局入口、具体游戏胜负、UI 对象 |
| L2 共享能力 | 通用动作/技能/命中语义 | 有真实复用证据的查询契约或有界辅助类型 | 为接 Latios 改技能枚举、第二套伤害系统 |
| L3 游戏规则 | 配置、准入、结算、关卡、随机消费 | 显式选择适用后端，定义排序/溢出/容差 | 从渲染实体或音频时钟反推权威结果 |
| L4 适配器 | 输入转换、只读显示、平台资源 | Psyshock 桥接及后续独立表现后端、能力回退 | 修改权威表、保存 Entities Entity、热卸载模拟模块 |
| L5 组合根 | 构造顺序、失败回滚、退出与开关 | 冷启动选择后端；不满足依赖时明确拒绝或选已验证回退 | 隐式自动升级包、按每帧性能随机切换物理后端 |

这里将算法桥接归入“适配责任”，不要求它放入现有 `SPF.Presentation`。物理接入应有独立适配程序集，避免公共内核反向依赖第三方包。

## 4 先统一语义再接 API

| 术语 | SPF 语义 | Latios 对应与整合规则 |
| --- | --- | --- |
| World | `SimWorld` 拥有表、registry、队列和资源 | `LatiosWorld` 继承 `Unity.Entities.World`；二者不是同类型，也不能共享实体编号 |
| Session | `SimSession` 拥有一局时钟、管线、恢复时间线与结束过程 | 可选辅助 ECS World 由一个明确 Session/适配 owner 管理；不得自行重复推进战斗 |
| Entity 与 handle | 完整 `EntityHandle` 只在所属 Session/World 内有效；row 会移动，部分 pooled 表使用另有定义的稳定 spawn 身份 | Entities Entity 的 index/version 只属于其 ECS World；必须显式映射，不 reinterpret，也不把 row 持久化 |
| Command | 尚待固定 Tick 规则接纳的意图；结构请求有确定生效点 | Latios command buffer 只服务其 World；不能直接越过 SPF 准入与容量政策 |
| Event 或 cue | 已发生的权威事实；多个消费者有自己的游标 | 音频、HUD、粒子只消费各自视图；不能通过“读走事件”使另一消费者遗漏 |
| Config 与 baking | authoring 校验、来源隔离后形成运行定义；当前不是 Entities baking | SmartBaker/Blob/SubScene 是另一内容管线；必须明确输入、编译器版本、依赖、产物与释放者 |
| Collection dependencies | SPF Declare/DependencyTracker 加返回 JobHandle | Latios collection 的读写获取、依赖登记、dispose 构成另一套协议；跨边界需显式传递，不自动互相识别 |
| Install module | 冷路径声明数据、注册系统、组合预检与构造回滚 | Latios installer 可能禁用/替换 Unity 系统；可选安装不等于安全热卸载或事务回滚 |
| Sync | `SimSession.Sync`/管线 EndTick 完成在途工作 | `SimWorld.Sync` 只是资源同步回调；Latios sync point 也不是 SPF 保存/恢复的替代物 |
| Restore 与 reset | 同 Tick restore 也更换 TimelineRevision；ClearLevel 有 LevelVersion | 重建适配缓存、清理旧 cue，按完整版本绑定；不能只比较 Tick 数字 |

这些定义应进入适配包的 README、测试命名和错误消息。特别是“世界”“实体”“同步”不统一语义，即使方法签名相似也不能视作可替换。

## 5 版本锁与模块范围

### 5.1 三套版本分别记录

| 项目 | SPF 产品基线 | Latios 2022 控制组 | 新版功能参考基线 |
| --- | --- | --- | --- |
| Editor | 2022.3.62f2 | 精确 2022.3.62f2；历史最低为 2022.3.36f1 | 6000.3.8f1 |
| Latios | 无 | v0.11.5 / `381a77dbf774ff603014d5695ef6c06abaa25d96` | v0.16.1 / `ae2262afd5c80ac4850fef23c9e7d4a971fabf53` |
| Entities | 无 | 1.3.5 | 1.4.8 |
| Entities Graphics | 无 | 1.4.2 | 1.4.21 |
| Burst 声明 | 1.8.27 | 1.8.18 | 1.8.30 |
| Collections / Mathematics | 2.1.4 / 1.2.6 | 2.5.1 / 1.3.2 的传递依赖证据；以新实验真实 lock 为准 | 本方案未确认完整解析锁 |
| 音频附加包 | 现有 Unity 音频 | DSPGraph 0.1.0-preview.22 属于旧整包依赖 | 新版 Myri 已转为 Unity 6.3 音频 API |
| SRP | manifest 为 URP 14.0.11 | 单独验证目标 URP14；不沿用 Unity6 样例的 URP17 lock | 按新版参考工程单独记录 |

来源：[SPF manifest](../Packages/manifest.json)、[Editor](../ProjectSettings/ProjectVersion.txt)、[0.11.5 package](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/package.json)、[0.16.1 package](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/package.json)、[作者样例固定 lock](https://github.com/Dreaming381/LatiosFrameworkMiniDemos/blob/f46281b4cbdbbe1bceacc75284500204735a6607/LatiosComparison/Packages/packages-lock.json)。该样例使用 Unity6/URP17，它只支撑传递依赖判断，不证明 2022+URP14 通过。

SPF 当前没有提交 `packages-lock.json`；manifest 不是已解析环境的完整快照。S1 必须保存实验实际 lock、Git 包 commit、Editor/目标平台/API、defines、编译后端与构建日志。先重现旧版配套依赖，再用另一个受控配置测试接近 SPF 的 Burst 1.8.27；不能因为版本更高就声称必兼容，也不必无理由降级产品项目。Collections/Math 的变化则要单独验证现有 Native 容器和 API。

0.11.5 按[同期入门说明](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/bc3be50530180ad6ee8dd10fa7388a7854131f6b/Core/Getting%20Started.md)使用 `ENTITY_STORE_V1` 与 `UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS`。这些是版本相关设置，不复制成所有未来版本的永久默认。v0.12.0 移除 2022 支持，0.16 的 Core 已改变 ENTITY_STORE_V1 要求。[版本变更](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/CHANGELOG.md)、[Core 固定 changelog](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Core/CHANGELOG.md)

### 5.2 模块依赖和回迁策略

| 模块 | 要核对的依赖闭包与变化 | 首批范围 | 后续采用门槛 |
| --- | --- | --- | --- |
| Core | Entities、内部 `EntitiesExposed` 桥接、容器、生成器、资源/Job 生命周期 | **S1 必测**；保留旧版内部桥接作为起点 | 新能力逐个列出依赖与行为测试；不重写 Entities 内部布局 |
| QVVS Transforms / 数学工具 | 旧版 Psyshock 依赖 Transforms；Calci 是新版依赖，0.11.5 的 Psyshock asmdef 没有它 | 随所选版本的必需路径验证；仅具体回迁能力需要时引入 Calci | 明确 SPF 二维/纵深坐标、单位、尺度、旋转；不能只拷算法单文件 |
| Psyshock | 旧版 Core/Transforms/Entities 等；新版另含 Calci。ColliderBody 含 Entity/QVVS；还有查询容器和 blob | **优先验证** CollisionLayer、候选对、射线/距离 | oracle、结果语义、容量、完整成本通过后才桥接 |
| Kinemation | Entities Graphics、BRG、baking、shader、变形/动画、ACL 原生插件 | 初期只记录整包编译，不安装替换 renderer | 有真实动画/渲染需求；旧 BRG 路线、URP、iOS/Android 插件与回退逐项验证 |
| Myri | 旧版 DSPGraph；新版 RootOutput/control/realtime API 与 Audio ECS | 初期不替换 SPF 音频 | 明确选择旧后端+能力回迁或新 2022 后端，独立原生音频验收 |
| Unika | 0.11.5 不含；新版 Core、生成器、Burst 函数指针、buffer、引用重映射/AOT | 不作为首批前提 | 两消费者确有脚本需求；验证生成器、stripping、序列化与重映射 |
| Calligraphics、Mimic、LifeFX、Aux ECS | 版本间存在新增/调整及各自依赖；不是首批查询所需能力 | 范围外；在整包导入清单中如实记录 | 新需求单独批准；不从 Core 成功推导这些模块可用 |

**运行时只安装 Core/Psyshock，不会消除整包的编译和 UPM 依赖。** 首次实验保持原包可重现性，区分“哪些程序集编译”与“哪些系统真实运行”。若后续需要最小发行包，再单独裁剪 asmdef、资源、生成器、插件和 package 依赖，验证完整闭包；仅用条件宏包住 SPF 调用不能让不兼容的第三方程序集停止编译。

### 5.3 三个高成本边界

- **Core 内部 API：** `EntitiesExposed` 借 asmref 编入 Entities，接触内部存储和安全句柄。它是版本耦合审查点；并非已经证明所有新接口在 1.3 中都不存在。[源码](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/EntitiesExposed/EntityManagerExposed.cs)
- **Kinemation：** 当前版使用新 BRG 创建/finished-culling 流程；2022 的回调和数据契约不同。旧版已有条件分支可参考，但构造签名修完后仍有 culling、dispatch、LOD/shader 和资源时序工作。[当前创建](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Systems/LatiosEntitiesGraphicsSystem.CreateDestroy.cs)、[旧版 renderer](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Kinemation/Systems/LatiosEntitiesGraphicsSystem.cs)、[精确 Editor BRG](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Runtime/Export/Rendering/BatchRendererGroup.bindings.cs)
- **Myri：** 当前 `RootOutputInstance` 后端使用 Unity6.3 音频接口，2022 无同一套 API。恢复旧 DSPGraph 路径是实质后端工作，不能只换命名空间。Unika 的 AOT/引用风险则应实验验证，当前没有与 BRG/Myri 同等级的已核实硬阻塞。[新版音频输出](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/MyriAudio/AudioECS/Runtime/AudioEcsRootOutput.cs)、[旧版 Myri asmdef](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/MyriAudio/Latios.Myri.asmdef)

## 6 轨道一 优先完成 SPF 扩展契约

这些增量可以独立于 Latios 回迁进行；不需要先引入 Entities。所有实现仍待批准，按小提交、红测试、回归和精确源原生门槛推进。

### 6.1 可解释的执行计划

在现有 `TickPipeline`、注册结果和 AccessDeclaration 上生成冷路径报告，至少包含：系统/来源模块、Phase、Order、注册序、read/write key、冲突依赖来源、barrier 与其完成原因。未知来源显示 unknown，不能从 namespace 猜 owner。

报告应画清“调用顺序”“Job 依赖”“框架可见的 Complete”三者。后者覆盖 barrier、EndTick 和 SerialProfiling；系统内部同步不能仅由 Declare 推断。例如现有 SV SpawnBullets 内部也会 Complete，冷路径报告应对未声明的内部同步标记未知，或使用显式注解/独立测量，不能宣称静态图捕获所有等待。保持 Phase → Order → 注册序；每个 Phase 不自动 Complete，不额外拓扑排序，不改变 RNG 消费、碰撞平局和 snapshot 系统次序。沿用已有 `GetSystem/GetAccessNames` 扩展必要信息，从管线构建时已保存的声明读取，不能为了导出图再次调用可能有副作用的 Declare。可靠模块来源在组合注册时补冷路径元数据，避免第二份手写图漂移。[现有 TickPipeline](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs)

完成标准：19 组合计划可重复，测试 R→W、W→R、R→R、空声明 barrier、相同 Order；增删测试模块只出现预期差异；旧 raw/hash 不变；只在初始化或明确请求时格式化，不进入稳态 Tick。

### 6.2 读写权限与结构安全窗口

现有 AccessDeclaration 有 R/W，但 `SystemEntry.Allowed` 合并成 bool，AccessGuard 检查的是“声明过没有”，无法区分只读声明后拿到可写 NativeArray。[AccessGuard](../Assets/SinglePlayerFoundation/Runtime/World/AccessGuard.cs)

分两步做：先给新扩展提供真正受限的只读获取与显式写获取，开发态检查访问模式；再检查创建/销毁/压缩/重排/清关的合法窗口。窗口依据相关在途 Job 已完成及操作类别，不能只检查 Phase 名称。保留 OnCreate、Tick 间编辑、已完成前序 Job 的 barrier 等合法路径。不得在每次 getter 中暗中 Complete。

最小反例：Read 声明取写入口失败、Write 可读写、漏声明失败、在途结构变更失败、同步后通过、异常恢复后窗口正确、双 Session 不串上下文。原生 Jobs safety 是并发验收，.NET harness 只验证检查逻辑。旧入口可保留兼容，但注明早先缓存的 NativeArray/unsafe 指针仍可能绕过检查；这不是一个完整的内存安全证明。

### 6.3 运行配置的来源隔离

先处理已知别名：Snake 的 Capacity；RPG 的 Dungeon/Loot/Capacity 与 HeroSkillSlots。它们已有大量值拷贝和 Bake，缺的是部分运行读取仍引用 authoring 对象。[SnakeRuntimeConfig](../Assets/SnakeFoundation/Runtime/SnakeRuntimeConfig.cs)、[RpgRuntimeConfig](../Assets/RpgFoundation/Runtime/RpgRuntimeConfig.cs)

先写“创建 Session 后修改源 SO/嵌套数组”的差分测试，再仅复制需要冻结的运行字段。相同定义产生稳定内容身份，NaN、无效引用、容量乘法溢出在分配前拒绝，失败释放已创建资源。两个消费者稳定后，提炼最小 Validate → Freeze → Build 约定。这里“冻结”首先表示与来源隔离，不声称 public runtime 字段不可修改。

已有 D/E 的 weapon/ability 冻结与保存指纹继续用；不重做 envelope。旧玩法若有依赖实时 authoring 修改的行为，先记录再独立版本化，不能静默改为固定。记录冷启动峰值与常驻内存，不用“只在冷路径”掩盖移动内存成本。

### 6.4 有需求才增加结构命令

当前 DestroyQueue 已有稳定播放，SV 也已有真实 Job → 模块自有 `BulletSpawns` 队列 → 下一 Tick ApplyCommands → SpawnRange → Burst 填充的路径。[现有 SV 系统](../Assets/SurvivorFoundation/Runtime/Systems/SvSystems.cs) 因此后续先审计现有队列的 admission、平局、容量和保存语义，而不是首次建设 Job spawn。过载时原子抢槽可能接纳不同集合，**事后排序不能修复已丢弃请求的差异**。先压力测试界定旧政策，不因方案直接改变旧结果。[现有并行队列](../Assets/SinglePlayerFoundation/Contracts/Collections/ParallelQueue.cs)

若新增召唤/分裂玩法的语义超出现有协议，再扩展模块自有的类型化定长请求。明确请求/生效 Tick、完整 owner 与时间线、稳定生产者/序号、同键处理、admission、容量耗尽、失效与保存政策。可选按生产者预留槽等受测的确定性接纳方式；不能用 object/delegate 万能载荷替代设计。第二消费者证明复用后才提炼共享 helper。无新增需求就保留现有模块队列与安全窗口，不先做通用命令总线。

### 6.5 用真实扩展证明契约有用

选一个产品确需的区域影响或召唤模块，在自己的 Runtime/Presentation/Game 中实现，沿用共享验收。目标是公共内核、全局玩法 enum 和旧游戏代码必要修改数为 0；应用组合根显式选模块的修改允许存在。记录新增文件、数据/访问计划、容量、初始化/常驻成本和回归结果。

现有 External Courier 已证明一部分外部规则接入，不能把新提案描述成首次拥有扩展能力，也不能用替身通过冒充一个自然、可玩、完整的移动 demo。[Stage G 当前覆盖](SharedAcceptanceAndMobileBudgets.md)

## 7 轨道二 建立真实的 Latios 2022 兼容线

### 7.1 独立实验工程的最小结构

建议先用与 SPF 分开的工程目录、Library、manifest/lock 和缓存，不把它建在现有原生验收工作树中。初期不要求公开 fork 或新建远端仓库；本地受控兼容分支足够。

```text
Latios2022Lab/                         独立 Unity 工程
  ProjectSettings/ProjectVersion.txt   精确 2022.3.62f2
  Packages/manifest.json
  Packages/packages-lock.json          实际解析结果
  Assets/Latios2022Tests/              Core/Psyshock 测试与固定输入
  Docs/CompatibilityMatrix.md          模块/功能/平台状态
  Docs/BackportLedger.md               来源提交、依赖、补丁与验证
```

拟议分支分别保存：未改上游的 0.11.5 控制组；仅兼容修复；单个新增能力；SPF 接入候选。第一次导入就同时改 renderer、音频、生成器和 package 拆分，会失去控制组，不采用这种顺序。

### 7.2 首轮只证明可以复现

- 包按固定 commit 导入，记录 UPM 完整解析与所有程序集编译结果。
- 仅运行必要 Core/Transforms/Psyshock 系统。Core 覆盖 World 创建/销毁、collection 写 Job→读 Job→释放、异常、PlayMode 重入/域重载。
- Psyshock 用小数组和已知几何验证：空集、单物体、重叠/分离、边界、退化输入；候选对与暴力 AABB oracle 比较，射线/距离单独验证。分别记录 Burst 开/关与不同 batch。
- 干净导入可重复，开启安全检查，输出原生测试结果和实际运行模块；Editor 成功后做所选目标的 player/IL2CPP smoke，不把 API 编译当 AOT 执行。

优先使用旧版数组入口；它绕开 EntityQuery 数据提取，不能消除编译依赖。[0.11.5 BuildCollisionLayer](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Builders/Physics.BuildCollisionLayer.cs#L154-L181)

### 7.3 然后回迁一个明确的新能力

从实际需求选一个 Core 小功能或 Psyshock 查询修复，提交前写明：为什么需要、精确上游提交、旧版差异、依赖闭包、目标 API/行为、oracle、平台和停止条件。优先保留旧引擎适配层，把算法或上层能力接进来；不是追求所有 0.16 类型名称相同。

每个补丁保留旧控制组回归和新增反例。若必须回迁新容器、生成器或内部 Entities 改动，重新评估闭包后再做。需要“完整 current API”时，要独立列出逐模块、逐功能兼容矩阵，包含无旧版对应的 Unika/LifeFX/Aux ECS 等；不能以一个成功查询宣布整库回迁完成。

## 8 Psyshock 接入 SPF 的唯一权威设计

这是 S2 的候选设计，不是已经存在的后端接口。

### 8.1 数据路径

```text
固定输入 -> SPF Tick 权威状态
                |
          只读形状/位置/稳定身份
                v
      Bridge 输入快照或受控借用视图
                v
      Psyshock Build/Query/FindPairs
                v
       结果规范化 + 身份/版本验证
                v
   SPF 现有窄相/命中优先级/技能/伤害规则
                v
     SPF 状态 + 保存 + 多消费者事实
                v
       既有 HUD / 动画 / 音效 / 粒子
```

Psyshock `FindPairs` 给 AABB 候选，不等于一次有效命中或完整物理求解。第一版尽量仅替换 broadphase/查询，以 SPF 现有窄相和规则作为语义控制组；若连 narrowphase 都换，必须另立精度/接触时刻/顺序的迁移任务。不会并行运行两套战斗权威，也不添加一个 PhysX 世界再次解算同一对象。

### 8.2 六项必须写进契约的内容

1. **身份映射。** 输入序号对应 Session 身份、TimelineRevision、LevelVersion 与完整 SPF handle；pooled 表仅在确有稳定 spawn 身份时使用它，例如 ShooterEnemy.Id；不是每张 pooled 表都有这种字段。无稳定身份的行只能在同 Tick、禁止结构移动的借用窗口内用临时 source-index 映射，消费前验证窗口；跨 Tick 使用必须新增明确身份协议或排除该用途。Entities Entity 不能强转为 SPF handle。优先使用算法返回的 body/source index 回查专用表；若 API 必需 Entity 字段，先核对用途，不能制造一个会被误当成真实 ECS 对象的 ID。
2. **时钟与权威。** 所有查询输入属于一个明确固定 Tick。若数组路径不需要实际 ECS World，就不为对齐概念新增 World；若模块确需 World，由桥接 owner 明确更新一次，禁用其独立战斗/输入时钟。后端不能改变 MaxTicksPerFrame、输入延迟或 RNG 消费。
3. **Job 依赖。** 输入读取等待 SPF 对应写者；构建等待输入准备；查询等待构建；消费等待查询。只返回完整链的 JobHandle，不能让 Latios 自动追踪和 SPF Declare 各自以为对方负责。复制、排序、dispose 也是链的一部分；同步 API 明确计入等待。
4. **结果和顺序。** 过滤自身/友方、碰撞类别、纵深/高度、最早接触、平局、穿透、去重仍由现有规则决定。结果集合相同不保证顺序、有限候选或浮点累加相同；必须复现原后端的必要顺序，或把变化作为新玩法版本处理。
5. **容量与过载。** 输入、候选、结果、映射、临时缓冲都声明上限。不能随机截断 broadphase 造成漏命中；满容量时按批准契约使用正确且已测的整次查询回退，或明确失败并停止该 Session，不能跳过碰撞继续模拟。查询回退仅允许在尚未向 SPF 发布任何部分结果时进行，先完成/丢弃旧查询输出，再原子式发布完整后备结果；它是同一适配器内部的受测路径，不是运行中切换后端 owner。若无法满足这个条件，就停止并报告，不假设当前系统已有任意 Tick 自动回滚/重试。内存足够容纳所有结果时才谈排序恢复确定性。
6. **释放和失效。** 桥接拥有 CollisionLayer/映射/临时缓冲；SPF 表由 SPF owner 释放。重置、ClearLevel、同 Tick restore、Session 销毁必须先解决在途 Job，再失效结果和释放/重建缓存；不能捕获旧数组继续写新一局。

### 8.3 不增加第二份持久模拟状态

碰撞层、候选集、ECS 映射和 GPU buffer 的计算内容原则上按可重建缓存处理，restore 后由权威状态重建，而不是序列化其原始内存。但这必须对接已有保存协议：World 中的 `IJobData` 资源若不实现 `ISnapshotResource`，`SnapshotGaps` 会拒绝保存。接入设计需提供明确的 snapshot/reset/restore hook 和完整 save descriptor，必要时在新 opt-in 配方中保存版本/重建所需最小状态，读取时使缓存失效；即使 payload 很小，新增资源也会影响配方与字节布局。若缓存由管线外 owner 管理，也必须有明确的同步与恢复生命周期协议。不能去掉 `IJobData` 标记绕过检查或新增一条无条件“缓存免保存”的豁免。[当前 SnapshotGaps](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs)

若新后端产生 warm-start 等影响未来结果的状态，必须选择“显式加入新版本保存契约”或“每次确定性重建且证明结果等价”，不能称它缓存后不管。

纯后端替换也不自动取得旧存档兼容资格：若顺序、容量、规则或结果有变化，更新相应 schema/content/rule 身份，保留旧读取配方。禁止把 Native/Blob 原始内存、Entity ID 或 Library 缓存当作跨版本存档。

### 8.4 可选包与开关

接入候选可使用单独的 `SPF.LatiosBridge` 及测试/Editor 程序集；公共 Contracts、Runtime.Core、L1/L2 不直接引用 Latios。应用组合根显式添加这个包并选择后端；普通 SPF 工程仍能在未装 Latios 时编译和运行。

拟议编译开关与运行选择是两回事：编译开关只约束桥接程序集；第三方包本身的 UPM/asmdef 闭包仍要解决。后端选择仅在冷启动，默认旧网格。第一次交付不支持运行中交换碰撞 owner、按帧自动切换或热卸载模块。

## 9 资源 内容与表现的并行接入规则

### 9.1 内容编译和产物兼容

SPF authoring/运行配置与 Latios Baker/Blob/SubScene 内容分开管理，桥接提供有限的形状、过滤、坐标、材质或动画映射。每个产物标记源内容 hash、编译器/包版本、目标平台、渲染管线、布局版本和兼容域。

Editor/Baker 类型留在 Editor/authoring 程序集；运行程序集只依赖运行数据。升级包后在独立工程重烘焙，保留原始资源和旧产物，不复用不同包/Editor 生成的 Library 缓存。关闭新后端必须仍能加载旧 Sprite/BAT/音频资源；不可把已有资产原地转换为只有 Latios 能读的格式。

### 9.2 生命周期与所有权矩阵

| 资源或状态 | 唯一 owner | 消费者权限 | 结束或失败时的动作 |
| --- | --- | --- | --- |
| 权威表、RNG、技能、伤害与存档 | SPF Session/World/游戏规则 | 适配器只读已授权视图 | 现有 Session 同步、保存/重置/释放流程 |
| CollisionLayer、映射、查询 scratch | 专用桥接 owner | 受依赖保护的 Job 借用 | 先完成/归还句柄，再清理；源时间线变更后失效 |
| 辅助 LatiosWorld（确有需要时） | 一个明确桥接 owner | 限定模块，不拥有战斗规则 | 停止更新、完成在途工作、销毁；不能销毁 SPF 存储 |
| Mesh/Material/Texture/AudioClip/Blob | 明确资产 owner 或约定共享资源管理者 | View/runner 借用 | 持有者全部退出后释放；实例不得释放仍共享的资源 |
| HUD/动画/声音/粒子游标与缓存 | 每个 View 或 runner | 独立消费权威事实 | Disable/restore/rebind 清理，避免旧 cue 重播 |
| 迟到异步加载结果（若未来引入） | 独立、待实现的加载契约 | 接受前校验 Session/时间线/owner generation | 已失效则由请求 owner 回收；当前方案不假设已有通用 lease |

Latios collection 依赖协议不会自动覆盖跨库借用；托管 struct 内装引用也不表示所有阶段无 GC。资源替换必须明确旧值是否释放，不能把 Latios 某个 setter 当作 SPF 的通用换资源协议。

### 9.3 Kinemation 与 Myri 后置且独立决定

Kinemation 可能替换 Unity rendering/skinning/LOD 系统，不是现有 Sprite/BAT 的无侵入按钮。[安装入口](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Utilities/KinemationBootstrap.cs) 若选择它，必须复核真实 bootstrap、剔除、阴影、多相机、LOD crossfade、变形、材质/shader、baking 与资源释放；已有权威动画 socket、根运动与攻击几何不能被视觉骨骼反向覆盖。

Myri 只消费已有声音事实，DSP 时钟不推进技能。先验证旧 DSPGraph 整体链或另行批准新后端，再测停止/重建、buffer ownership、采样率、后台、路由中断、爆音、延迟与持续内存。保留现有 SoundPlayer 作为产品默认和回退，不能把 PCM 数值检查冒充实际听感。

HUD、技能准入、伤害数字和存档无需跟着更换。两个后端可在不同测试组合中并存；同一实际视图/声部的 owner 与消费者游标唯一，不能重复绘制、重复播音或重复结算。

### 9.4 迁移范围与必须显式处理的破坏性变化

| 选择 | 应保持兼容的面 | 不能承诺原样兼容的面 |
| --- | --- | --- |
| 仅独立实现扩展契约 | 旧系统注册顺序、raw fixture、默认工厂与结果 | 收紧检查可能暴露过去可调用但违反契约的访问；先在开发态试点并给出迁移提示 |
| 可选 Psyshock 后端 | 游戏规则、输入与HUD原则上不变；未选新后端的旧保存布局不变 | 包升级、候选顺序、浮点接触或容量政策如实际变化，需明确版本/回放域和旧后端共存，不声称自动兼容 |
| Kinemation/Myri 后端 | 命中、技能、事实日志与现有产品回退 | authoring/baking、材质shader、动画/音频资源、插件与构建设置需要新的适配和产物版本 |
| 全部转为 Entities/Latios | 产品玩法目标可保留，但靠完整差异测试证明 | 存储访问、handle/query、系统生命周期、结构命令、保存表示、baking及调试工具均涉及重写/转换，不能通过替换一个Runtime.Core程序集完成 |

进入最后一种路线前，必须单独确定哪些旧存档/回放需要读取、用何种字段级转换、哪些行为保持、哪些有意变化，以及双版本支持的截止条件。当前同运行域 envelope 只能执行已声明的兼容判断，不能自动完成上述迁移。没有迁移程序时，正确行为是明确拒绝不兼容数据并保留原文件。

## 10 分阶段实施与退出门槛

阶段编号与前置回迁报告对应：S1a 对应历史基线 Gate A，S1b 对应增量回迁 Gate B，S2 对应 SPF 接入 Gate C。轨道一的独立契约工作称 A1–A4，避免与原 A–G 已完成阶段混淆。

| 阶段 | 做什么 | 交付及通过条件 | 不通过怎样处理 |
| --- | --- | --- | --- |
| P0 冻结与证据闭环 | 记录当前运行时、依赖和19组合；继续精确源原生 CI | 精确 commit/tree、harness、API、Burst/native/graphics 与剩余设备状态分开；修复不得放宽阈值 | 保留失败证据；影响兼容基线的问题解决前不合并依赖或适配器 |
| A1–A3 SPF 增量 | 执行计划；读写/结构反例；两消费者配置别名隔离 | 独立小提交，旧 fixture/hash、冷组合不变，受影响原生 safety 通过 | 无实际收益则停在诊断/试点；不扩成新框架 |
| A4 条件性命令 | 先审计既有SV队列；仅新增语义需求触发扩展 | admission/顺序/容量/保存契约；两个消费者后再共享化 | 没需求不实施；过载不确定不能用排序掩盖 |
| S1a 历史兼容控制组 | 独立2022工程固定0.11.5，Core/Psyshock原生与所选player构建 | 真实lock、最小场景、oracle、安全/释放/重入/构建证据；编译模块和运行模块分列 | 分类修复一个已知问题；需升级Editor/改内部布局/关safety则停下重定范围 |
| S1b 选定新能力回迁 | 一个有需求的上游修复/能力，保持旧引擎后端 | 来源ledger、依赖闭包、新反例、S1a回归及所需IL2CPP验证 | 成本扩散到音频/渲染/生成器重写时重新决策 |
| S2 碰撞桥接候选 | opt-in适配；相同游戏输入/回放；完整成本 A/B | 语义、生命周期、保存隔离、容量和成本通过；至少两个适用消费者或一消费者加明确专用范围 | 无收益或超预算则留独立实验；旧后端不动 |
| S3 条件性渲染或音频 | 每次只选有产品价值的后端 | 独立内容/平台矩阵、真实像素/连续视频或音频、AOT/插件证据 | 缺平台支持则不进入对应产品档；不移除现有回退 |
| S4 产品采用 | 对批准的设备/玩法/包锁开 opt-in | 设备持续测试、安装/升级/恢复/撤回演练、维护owner确认 | 关闭冷启动开关并回到已验证包锁与兼容资产 |

P0 期间可以做文档、源码差异与隔离工程准备；新的原生执行需协调现有 Editor/runner 队列。不能让 Latios 实验改变正在验收的 SPF 工程 Library、Burst 设置或依赖。P0 未闭环时，实验即使通过也不改变 SPF 主线“待验收”的状态。

### 10.1 每个阶段的证据包

至少包含：源码 commit/tree、Editor/包锁/defines、实际目标设备或执行环境、测试命令、原始结果/日志、内容与构建 hash、实际后端、通过/失败/跳过/未运行清单，以及对照组。证据可重复恢复并核对哈希。测试代码、文档和兼容补丁分开提交；不重命名历史绿灯为当前结果。

### 10.2 允许回退的时机

- **实验失败：** 回到未改上游控制组或上一已测补丁；保留失败用例，SPF 不受影响。
- **适配器失败：** 下一次 Session 创建选旧后端；对在途 Session 先正常停止并完成 Job，再销毁。不能失败途中同时激活两套碰撞输出。
- **依赖变更失败：** 一并还原 manifest、lock、defines、适配程序集和匹配内容产物，用独立 Library 重导入；只撤销 C# 文件不足以回到旧环境。
- **产物或存档不兼容：** 保留旧资产、旧读取配方和原始保存；新兼容身份拒绝未知输入。已经引入新权威状态时，旧版本可能不能读新存档，必须在采用前说明和测试迁移/拒绝策略。

撤回变更以普通 revert/独立提交完成，不删除坏结果或强推覆盖历史。生产采用前演练一次完整冷启动、读档、重开、退出和回到旧后端。

## 11 验收矩阵与性能实验

### 11.1 语义与原生正确性优先

| 范围 | 必测边界 | 通过意味着什么 |
| --- | --- | --- |
| 构建/包 | clean import、重复构建、实际lock、生成器、asmdef、IL2CPP/stripping | 只覆盖指定Editor/目标配置，不代表全部模块/平台 |
| 调度/资源 | 真正在途Job、读写顺序、异常/释放、双Session、重入 | 指定访问链与owner受验证；未使用unsafe变体绕开检查 |
| 几何/碰撞 | 空/退化、负坐标、相邻浮点边界、大目标、纵深、枪口重叠、高速相向、最早接触与平局 | broadphase不漏必要候选，最终命中符合既有规则 |
| 确定性/兼容 | seed+input、batch变化、接纳集合、完整Tick/hash/raw、同Tick restore | 指定运行域/配置内成立；不承诺跨硬件逐位浮点 |
| 生命周期 | ClearLevel、重开、pause/background、destroy/rebind、句柄复用、迟到结果 | 旧世界/时间线输出不能污染新状态，不泄漏资源 |
| 表现/可玩性 | HUD触摸、四武器/技能、死亡重开、连续动作、密集反馈、低档可读性 | 真正接入游戏循环；静态demo和单张截图不能替代 |

Bridge 使用 Burst/Jobs 不等于 GPU 物理；本方案首批是 CPU 算法/调度验证。只有后续真实 compute/indirect 后端才需要对应执行、读回/像素见证，不能用“GPU”名称覆盖全部数据路径。

### 11.2 拟议负载而非已有成绩

沿用[现有碰撞基准方法](CollisionBroadphaseBenchmarks.md)和[共享移动预算](SharedAcceptanceAndMobileBudgets.md)，按相同 seed、位置、尺寸、运动、查询、容量与边界比较旧网格和候选后端。建议负载：

- 均匀分布与真实游戏默认负载。
- **1,024 个物体的密集聚集/极端全重叠**。全重叠可产生 523,776 个无序不同物体对；这是测试规模计算，未经本项目实测。检查候选容量、最坏时间与正确回退，不能默默截断。
- 稀疏大地图、混合大/小碰撞体、负坐标与网格边界。
- 高速扫掠、移动目标、连续生成销毁、接近容量与过载。
- 同帧多查询、两个独立 Session、clear/restore 后缓存重建。

分别报告输入导出/拷贝、build/update、query、结果排序/过滤、Job schedule/Complete、完整 Tick 与常驻/峰值内存。配对交错 A/B、相同预热/重置，保留原始样本，报告 p50/p95/最坏值及平台。纯 kernel 更快但桥接总成本更高，应判接入无收益。

预先以具体设备/玩法批准性能与内存预算，沿用既有 correctness/GC 阈值，不在结果出来后放宽。无新测量时不写虚构的加速比、毫秒或人天；平台未知的预算保持未知。

### 11.3 移动端最小矩阵

| 目标 | 算法/构建要求 | 若引入渲染或音频的额外要求 |
| --- | --- | --- |
| Android Vulkan | ARM64/IL2CPP/Burst、Job安全、实际内存与持续帧时 | 实际URP/shader/BRG路径，粒子/动画回退，暂停与音频路由 |
| Android OpenGL ES 回退档 | 精确区分现有GLES3.0档与新包所需能力 | 不能推定Kinemation覆盖GLES3.0；不支持时仍可选择已有DataTexture/CPU表现 |
| iOS Metal | ARM64/IL2CPP、代码生成/stripping、恢复/触摸/持续负载 | ACL等原生插件架构和导入设置、Metal shader、系统音频中断/路由 |
| 桌面控制组 | 精确Editor与CPU架构、Burst见证、原生测试 | 为开发与差异定位服务，不替代手机签署 |

包的兼容指南是研究依据，不是设备成绩。Entities Graphics 文档页面现在展示 1.4.21，其 Android GLES3.1+/URP 条件不能直接当成历史1.4.2的实测承诺；2022 BRG 可用也不能推出完整 Kinemation 可用。复核目标包内文档、实际 renderer 设置和设备。[Entities Graphics 要求](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4/manual/requirements-and-compatibility.html)、[2022 BRG 要求](https://docs.unity3d.com/2022.3/Documentation/Manual/batch-renderer-group-getting-started.html)

手机验收还包括横竖屏、安全区、多指、后台/恢复、热状态、电池、内存峰值、真实渲染帧时和音频听感。先选机型、OS、分辨率、运行时长与预算；没有设备就保留 Pending。短 .NET Tick 采样不能换成“移动60FPS已通过”。

## 12 风险 成本与维护责任

### 12.1 风险登记

| 风险 | 早期信号 | 控制与停止条件 |
| --- | --- | --- |
| 原生基线未闭环被实验掩盖 | 用旧native/桌面截图称新集成成功 | 精确源证据分列；基线问题未解决不合并依赖 |
| 包解析改变现有容器/API | Collections/Math被提升、生成器冲突 | 独立lock先测，再完整SPF回归；禁止主线试错装包 |
| Entities内部ABI漂移 | 安全句柄/Chunk/内部字段相关编译或运行错误 | 固定Entities，保留旧桥接；需重写核心布局则重定范围 |
| 双重权威与时钟 | 同一实体被两World移动/伤害两次 | SPF唯一裁决；辅助World只执行批准工作 |
| 队列过载不确定或漏碰撞 | batch变化造成接纳集合/命中变化 | 测admission；明确正确回退或拒绝，不只排序 |
| 接入总成本抵消kernel收益 | 转换、排序、等待或双份状态占主导 | 以完整Tick/内存决策；保留独立库成果，不强行合入 |
| 内容格式与渲染档绑定 | 新bake后旧后端打不开、GLES档消失 | 产物版本化、源文件/旧资源保留；不破坏回退 |
| AOT/原生插件/音频失败 | Editor可用但IL2CPP缺注册、ACL加载或音频回调失败 | player/真机专门门槛；对应平台不通过不宣称支持 |
| 维护范围失控 | 一个查询要求整套新renderer/audio/sourcegen | 单能力ledger与审批；超过批准闭包暂停 |
| 许可/来源错误 | 把整包当MIT、漏第三方二进制notices | 按文件和实际发行内容核对，保留来源/版权/许可 |

### 12.2 相对投入与必要角色

执行计划报告的初始范围较小；读写权限/安全窗口和配置隔离中等，成本主要在兼容与原生反例。Core/Psyshock 控制组的工作量有明确边界但尚未实测。正式桥接含数据转换、正确性、性能和设备维护；Kinemation/Myri 的后端与内容验证面显著更大。全量Entities迁移最大，且触及存档与工具链。这里不估未经试验的人天。

进入 S1 前确定兼容分支负责人；进入 S2 前确定 SPF 接口/玩法验收负责人；进入 S3/S4 前确定平台构建与资源/音频负责人。同一人可兼任，但不能让“由开源上游维护”代替责任。记录谁批准目标功能、谁审核补丁、谁保管依赖锁和回归证据、谁决定停止。

维护兼容线的最低工作包括：固定上游tag/commit；把兼容补丁、功能回迁和缺陷修复分开；定期按实际使用功能审核上游修复；维护“已回迁/不适用/待处理”清单；每次更新检查内部API、生成代码、Native插件和产物格式。不能自动追踪main，也不能对外称这是上游官方2022 LTS分支。

### 12.3 许可与发布边界

Latios 根[LICENSE](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/LICENSE.md)指向 **Unity Companion License**，不是整个包统一 MIT；[第三方 notices](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/THIRD%20PARTY%20NOTICES.md)另有不同来源。实现前核对[旧基线 LICENSE](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/LICENSE.md)、[旧基线 notices](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/THIRD%20PARTY%20NOTICES.md)和每次新复制文件的实际许可，保留修改来源、版权和适用声明。新旧 notices 的内容并不完全相同，不能把新版清单套给未采用的旧版文件；资源、shader、生成器与原生二进制分别检查。[UCL 原文](https://unity.com/legal/licenses/unity-companion-license)

独立吸收概念与复制源码要区分；工程可修改不代表任意再许可或非Unity平台使用。公开fork、对外分发、上游PR以及具体商业许可安排不在当前方案默认范围，届时按实际内容和目的确认。此处不替具体发布安排作法律结论。

## 13 建议批准的下一批范围

如果决定进入实施，建议一次只批准下面这个有界批次：

1. **保持当前精确源原生闭环优先。** 不让新实验覆盖已有失败、待验图像/音频或手机门槛。
2. **SPF 先做执行计划与反例测试。** 读写/安全窗口在少量真实系统试点；Snake/RPG 配置来源隔离单独提交。先复核既有SV生成队列，无新需求不建设通用结构命令。
3. **独立 2022.3.62f2 工程固定 Latios 0.11.5。** 只证明 Core/Psyshock 原生行为、资源生命周期、完整包编译与所选player构建，交付真实版本锁和证据。
4. **通过后再选一个新版能力回迁。** 说明实际用途、源提交和闭包；同时提出是否做SPF桥接的收益假设。
5. **S2 通过后才讨论默认采用、渲染、音频或完整迁移。** 每种结果都可以是继续、缩小范围或保留独立实验；不把“必须用上Latios”设成验收目标。

最终选择标准是：**扩展者更少碰内核，旧玩法与保存语义受保护，新的能力在精确2022环境有真实证据，完整成本和维护责任可接受。** 本文中的所有后续实现、安装与测试安排仍待明确启动；当前完成的是这份整合设计。

## 14 依据与进一步阅读

- 两篇输入报告的发布快照：[分层与扩展性分析](https://github.com/karosLi/SPGameFoundation/blob/a94d394af0db633543c67ccd32a9ae66a6093b27/Docs/LatiosLayeringExtensionReview.md)、[Unity2022回迁评估](https://github.com/karosLi/SPGameFoundation/blob/63646035ce9d1f69ba252fe951a7e9554b70c1bd/Docs/LatiosUnity2022BackportAssessment.md)。本文整合其决策，不替代逐调用点源码核对。
- SPF：[当前架构](Architecture.md)、[新玩法配方](NewGameplayIntegrationRecipe.md)、[兼容矩阵](FoundationCompatibilityMatrix.md)、[Stage F](StageFBackendContracts.md)、[Stage G](SharedAcceptanceAndMobileBudgets.md)、[精确软件与剩余原生证据](MobileFeedbackCoordinationValidation.md)。
- Latios 历史：[0.11.5源码](https://github.com/Dreaming381/Latios-Framework/tree/381a77dbf774ff603014d5695ef6c06abaa25d96)、[同期文档](https://github.com/Dreaming381/Latios-Framework-Documentation/tree/bc3be50530180ad6ee8dd10fa7388a7854131f6b)；新版参考：[0.16.1源码](https://github.com/Dreaming381/Latios-Framework/tree/ae2262afd5c80ac4850fef23c9e7d4a971fabf53)、[固定文档](https://github.com/Dreaming381/Latios-Framework-Documentation/tree/626d1a37f432cd31e4a3faf44fc3cb07c928cc17)。版本支持、源码推断、API编译、原生执行和物理设备验收保持分列。
