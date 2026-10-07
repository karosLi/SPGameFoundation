# 共享基座语义与扩展优化计划

日期：2026-10-07。面向 SPGameFoundation 的后续演进，基于 `9a95654247f066ee9ff9164381bdd30ade4c9309` 源码和[开源共享基座调研](OpenSourceSharedFoundationSurvey.md)。本文最初交付为计划；下列实施状态记录后续独立提交，不把拟议能力当作已完成，也不产生未经测量的性能或设备结论。当前实现与验证状态继续以[能力清单](RequestedCapabilityChecklist.md)和[精确提交验证记录](MobileFoundationFollowupValidation.md)为准。

实施状态：计划发布时的两项试验候选精度失败现已在精确 `e86ee87` 原生闭环解决（1,084 EditMode、154 graphics PlayMode、1,050 .NET 通过）。用户已授权在该闭环后执行本计划；P0 兼容/语义基线现已完成；P1 首批已实现有界[组合回滚](CompositionRollbackValidation.md)及[Session/View 生命周期修复](validation/SessionViewLifecycle-20261007.md)，本次集成的原生执行状态待独立闭环。可选[描述清单](OptionalCompositionPreflightValidation.md)和[调度异常恢复](TickFailureOwnershipValidation.md)已各自完成红测试与本地验证，集成/原生验收按[阶段记录](FoundationStageBAndCValidation.md)推进；后续阶段继续按门槛实施。见[证据与剩余设备门槛](MobileFoundationFollowupValidation.md)。 D 的[有界保存 envelope](VersionedSaveEnvelope.md)与 F 的[后端契约夹具](StageFBackendContracts.md)已完成[最终本地组合验证](FoundationStageDFValidation.md)；E 的[两游戏规则组合](ComposedAbilityRules.md)与 G 的[共享验收/移动预算](SharedAcceptanceAndMobileBudgets.md)也已完成独立实现和本地验证，现与原生生命周期探针修复一起做最终组合检查。A–G 的代码/文档已齐备，但原生精确提交与真机门槛未因此完成。

## 1 推荐方向与完成标准

**保留 SoA、Jobs/Burst、Module/Table/System/SessionHost，把扩展点建立在稳定语义和可检查契约上。先补组合、身份、生命周期、配置与保存的边界，再按两个真实消费者的需要提炼能力。**

“高扩展”应能用接入成本证明：

- 新玩法主要增加自己的 Runtime、Presentation、Game 三个程序集及配置；公共内核、公共玩法枚举、旧玩法源码的必要修改数目标为 **0**。应用启动入口可以显式选择新模块，不能要求“整个仓库零修改”。
- 已有行为的新技能、新武器只加本玩法内容定义和映射；全新机制允许新增独立规则系统。真正缺少的通用机制先有需求与实验，再增加一个有界公共能力，不承诺任意行为都无需写代码。
- 新渲染后端消费相同只读数据，经过同一套生命周期、像素和质量隔离测试；模拟状态、伤害、冷却、掉落与胜负不变。
- 新模块可以单独测试、明确拒绝不兼容配置、退出后回到资源基线；关闭可选功能时，经典模式的状态与存档 fixture 不变。
- 稳态热路径仍预分配、有上限、无新增托管分配；扩展成本包含初始化、内存、调度、上传、回收和真机持续帧时，不能只看一个函数的速度。

不以新增接口数量、继承层数或支持的框架名单衡量扩展性。暂不切换 ECS、升级 Unity、建设通用服务定位器、热插拔插件市场或任意技能图编辑器。

## 2 从开源项目吸收哪些概念

以下是对源码模式的提炼及本项目的设计建议，不表示这些项目已经实现同一套契约。版本、许可和完整边界见[调研报告](OpenSourceSharedFoundationSurvey.md)。

| 来源 | 借鉴的语义 | 在本项目的落点 | 保留的边界 |
| --- | --- | --- | --- |
| GameFramework / UnityGameFramework | 服务、资源、表现实例分别拥有生命周期；加载完成也可能已经失效 | 模块退出与资源归属；换装、重开后的迟到回调 | 不把其 EntityLogic 当 SoA 实体；不照搬全局定位器或托管事件热路径 |
| QFramework | 命令表达意图，查询取得信息；订阅由生命周期负责退出 | 输入命令、HUD 读模型、表现绑定的退出契约 | SPF 命令仍在固定 Tick 授权，不能照搬立即 Execute |
| Morpeh | 身份与所属 World 的生命周期相关；存储与结构提交分离 | Session 归属加 EntityHandle；安全提交窗口与接入测试 | 不改用第二套 stash/filter；不把 row 或进程内编号当持久身份 |
| Latios | 显式安装可选能力；执行依赖和 Native 数据所有权关联 | 组合预检、能力清单、Job 读写与释放责任 | 保留自有 SoA；不因概念接近引入 Unity 6/Entities 依赖 |
| Unity ECS Samples | Authoring、编译后数据、运行状态分别处理 | 配置校验与冻结、稳定内容 ID、运行清单 | 可以采用烘焙概念，无需采用 Entities Baker |
| Megacity Metro | 输入、权威模拟、表现桥接分离；启用、解绑、回收不是同一动作 | View 绑定状态机、后端适配与退出矩阵 | 案例规模和移动支持不等于本项目性能证据 |

关键原始证据：[GF 模块退出](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/GameFrameworkEntry.cs#L25-L49)、[GF 迟到加载处理](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Entity/EntityManager.cs#L1244-L1265)、[QFramework Command/Query](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L173-L205)、[Morpeh Entity 身份](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Entities/Entity.cs#L15-L45)、[Latios 显式启动](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Editor/ScriptTemplates/StandardExplicitBootstrap.txt#L20-L69)、[ECS 配置依赖](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/EntitiesSamples/Assets/Baking/BakingDependencies/ImageGeneratorAuthoring.cs)、[Megacity 实例桥接](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Utils/Pooling/Systems/DynamicInstanceLinkSystem.cs)。ET 仅保留概念审计参考，当前许可下不复制其实现。

## 3 先统一概念 再决定是否增加类型

下表是语义词典，不是要求把每一行都做成新 interface 或 Manager。

| 概念 | 精确定义 | 当前承载与建议 |
| --- | --- | --- |
| Session 对局 | 一次可暂停、重开、恢复的执行实例，拥有时钟、World 和 Pipeline | 已有 SimSession；SessionHost 只负责 Unity 生命周期接线 |
| World 模拟世界 | 一个 Session 中的表、实体注册表、资源与结构变更队列 | 已有 SimWorld；不是全局服务容器，也不是场景 GameObject 树 |
| Scope 生命周期范围 | 规定何时失效、谁清理；范围不同不意味着一定要新建 World | 已有 LevelScoped、LevelVersion；补齐 View/加载请求的退出约定 |
| Entity 实体 | 所属 registry 内有稳定身份的模拟对象 | 已有 EntityHandle(Index, Generation)；句柄必须与所属 Session 一起使用 |
| Row 行 | 当前存储位置，可因压缩、排序和 swap-back 改变 | 已有 SimTable；只在约定访问窗口有效，不能跨 Tick 持有为身份 |
| Instance 实例 | 某次表现实例、池槽占用或运行内容实例 | 与 Entity 不一一对应；用绑定代际或 SpawnTick 区分复用 |
| Definition 定义 | 稳定内容 ID 指向的不可变规则与参数 | SkillSlotDefinition、WeaponProfile 等已有；补统一校验和指纹口径 |
| Asset 资源 | 贴图、材质、音频等加载成果 | 属于资源提供者；借用者持租约，View 只拥有自己的实例 |
| Capability 能力 | 模块向其他模块提供的可依赖契约，如有界空间查询、技能槽或武器动作 | 先用启动清单描述；不新增按游戏名排列的全局 enum |
| Module 模块 | 安装一组数据、系统和所需契约的组合单元 | 已有 IGameplayModule；模块本身不等于每 Tick 执行器 |
| System 系统 | 在一个固定阶段内执行变换，声明真实读写依赖 | 已有 ISimSystem；行为相关跨 Tick 状态要归入可重置/可保存状态 |
| Policy 策略 | 在同一机制内决定规则选择，如最早命中、目标平局、AI 选择 | 已有若干静态规则与 DecisionTree；用 POD 配置/纯函数表达，按需提炼 |
| Backend 后端 | 在不改变约定结果的前提下，执行同一查询或绘制契约 | 空间、骨骼、粒子、精灵可各自选择；不是全局万能后端 |

### 3.1 四类身份不能混用

1. **运行身份**：EntityHandle 只在所属 World/Session 中解释。A、B 两个 Session 的 `(Index, Generation)` 相同是允许的；跨 Session 的缓存必须同时核对归属。
2. **时间线身份**：TimelineRevision 在重开和恢复后变更；LevelVersion 用于换关；WeaponRuntime.Revision 等局部 revision 只使相关缓存失效。它们不是内容版本，也不应回写进旧存档来恢复旧表现。
3. **内容身份**：技能/武器/图标 ID 属于显式内容命名空间；ID 对应规则含义不能随注册顺序漂移。内容 ID 和 VisualId 分离，换图不能偷偷换伤害。
4. **请求身份**：一次异步加载、换装或重新绑定的 request revision。即使实体还活着，旧请求也可能不再被需要。

[AccessKey](../Assets/SinglePlayerFoundation/Contracts/AccessKey.cs) 的 Id 是进程内按创建顺序分配的密集编号，适合依赖跟踪；它不适合存档、资源地址或跨进程协议。持久 schema ID 另行定义，不改变其热路径用途。默认 `GameplayModuleAsset.Id` 来自类型名，也不能直接承诺为长期内容标识；只有进入持久协议的模块才需要稳定显式 ID 和旧名映射。

### 3.2 Command Event Query 分清发生了什么

- **Command 意图**：请求攻击、移动、换装、重开。可能被拒绝、取消或到期；由明确阶段消费。UI 短按先锁存，Tick 再决定是否成功。
- **Event 事实**：技能已接受、弹丸已释放、伤害已结算、目标已死亡。生产者先完成相应状态变更，再发事实；消息名字不能把“伤害申请”误称为“已造成伤害”。
- **Query 查询**：读取槽位、目标候选或当前状态，不消费模拟 RNG，不推进计时器，不修改实体。空间查询返回候选不等于已命中。
- **Presentation cue 表现提示**：从事实派生，可有预算丢弃、合并和过期规则；不驱动权威伤害，不随保存恢复重播。

`EventQueue<T>` 是有界运输容器，不保证其中每种 T 都具有事实语义。每类队列要写清生产阶段、消费阶段、排序键、最大长度、溢出、保存与清空责任。模拟需要稳定次序时显式规范化；不能拿并行入队顺序当确定顺序。多 View 消费需要各自 cursor 或只读批次，不能由第一个 View 清空公共流。

### 3.3 两种 Snapshot 分开命名

- **呈现快照/Read model**：供当前画面读取的投影，可能双/三缓冲；有效期至约定交换点。可以缺少模拟内部数据，不用于续玩。
- **保存检查点/Save checkpoint**：恢复完整权威状态，包括 RNG、计时、输入缓冲、待提交命令和必要系统缓存，并验证布局和规则兼容性。

已有 `SnapshotBuffer<T>` 与 `SimSession.WriteSnapshot` 分别承担这两个方向。先在文档和适配参数中说明含义，不为了改词批量重命名公开类型或存档字段。

## 4 不可破坏的依赖 时间与所有权约束

### 4.1 五层逻辑架构与依赖图

这五层是责任边界，不是每层只能调用紧邻下一层的线性堆叠。组合根位于依赖图外侧，负责接线；适配器通过明确协议接入规则。

| 层 | 负责什么 | 不负责什么 | 当前主要落点 |
| --- | --- | --- | --- |
| 1 计算与存储内核 | 身份、SoA 表、几何/空间基础、Job 调度、固定 Tick、安全提交 | UI、资产加载、具体玩法胜负 | Contracts、L1Simulation、Runtime.Core |
| 2 共享玩法能力 | 战斗形状、命中历史、动作时间线、技能充能、武器机制、通用决策选择 | 为每个游戏命名的内容 enum、场景装配 | L2Gameplay |
| 3 游戏规则模块 | 本游戏配置、资格/属性/结算、关卡规则、进度、胜负 | 第二套实体/时钟/渲染内核 | 各 Foundation.Runtime 的 Module、State、Systems |
| 4 表现与平台适配器 | 触摸/设备输入、HUD、角色/武器表现、渲染后端、资产加载和平台接线 | 权威伤害、冷却、掉落、模拟 RNG | 公共 Presentation、Shell，各游戏 Presentation 和 Game 中的适配代码 |
| 5 应用组合根 | 选择模式/能力/后端，创建 Session，连接输入与 View，管理创建/退出责任 | 每 Tick 替游戏规则做决定 | WorldComposer、SessionHost、各游戏 Bootstrap；SimSession 负责执行编排 |

```text
                        应用组合根
                 创建并连接下面所有部分
                          │
         ┌────────────────┼────────────────┐
         ▼                ▼                ▼
     游戏规则模块      表现/平台适配器     Session 执行编排
         │                │                │
         ▼                │                │
     共享玩法能力         │                │
         │                │                │
         └───────────┬────┴────────────────┘
                     ▼
               计算与存储内核

输入适配器 ── Command 意图 ──→ 游戏 Tick 接受或拒绝
游戏状态   ── Query/Read model/Event 事实 ──→ HUD 与表现
```

上半图表示代码/创建依赖，下半图表示数据流；适配器依赖内核时只能使用约定的读取/传输入口，不能直接改权威状态。公共适配器依赖公共契约，游戏专属适配器可以依赖本游戏的窄协议。输入进入命令入口，输出进入只读呈现入口，组合根负责把两侧连接起来。

| 依赖方 → 被依赖方 | 内核 | 共享能力 | 游戏规则 | 适配器 | 组合根 |
| --- | --- | --- | --- | --- | --- |
| 内核 | 内部无环 | 禁止 | 禁止 | 禁止 | 禁止 |
| 共享能力 | 允许 | 内部无环 | 禁止 | 禁止 | 禁止 |
| 游戏规则 | 允许 | 允许 | 本域 | 禁止 | 仅现有模块声明契约* |
| 适配器 | 窄读取/传输 | 只读契约 | 仅本游戏窄协议 | 明确依赖 | 仅宿主生命周期接线* |
| 组合根 | 允许 | 允许 | 允许 | 允许 | 明确 owner |

*当前 IGameplayModule/GameplayModuleAsset 与 SessionHost 所在 SPF.Runtime 还承载多个责任，因此具体游戏和部分适配器在物理程序集上会引用 SPF.Runtime。这个引用不授权它们反向调用全局 Bootstrap。先以语义规则和测试限制，再依据真实循环/包复用压力决定是否拆出更窄的装配契约程序集；本计划不伪称现有 asmdef 已完全隔离五层。

配置、保存、诊断是贯穿上述责任的协议，不新增一个“什么都能访问”的第六层：配置在组合时冻结，权威配置由规则读取；保存由 Session 在同步窗口协调各 owner；诊断读取各层显式提供的计数；资源加载留在适配侧。内核允许 Unity.Mathematics、Native 容器、Jobs/Burst，禁止 UI/Camera/场景表现依赖，不把它描述为完全无 Unity 依赖的纯 C#。

### 4.2 程序集边界

维持现有 asmdef，先用检查保证方向正确，不立即拆出十几个新包：

- `SPF.Contracts`：身份、POD、基础协议，不引用游戏、Shell 或表现实现。
- `SPF.L1Simulation`：数学、几何、空间与纯计算；`SPF.Runtime.Core`：存储、调度、生命周期基础。两者继续按当前实际引用保持无环。
- `SPF.L2Gameplay` → Contracts / L1 / Runtime.Core：可复用玩法机制，不引用任何具体 Game 或 Presentation。
- `SPF.Presentation` → Contracts / L1 / Runtime.Core：只读投影与渲染实现。引用 Runtime.Core 不代表获得修改权威状态的语义许可。
- `SPF.Runtime`：组合与 Session 接线；当前 asmdef 还引用 Presentation。先审计必要性，只有确认无使用且编译/序列化无回归后才移除多余依赖，不把文档目标写成已实现事实。
- `SPF.Shell`：输入、HUD、应用级性能接线。具体 `MyGame.Game` 组合自己的 Runtime、Presentation 与 Shell；公共层不得反向引用 MyGame。

`SimWorld.Column<T>` 返回 NativeArray，现有 AccessGuard 主要检查“是否声明了 key”，不是完整的只读类型系统。因此先让新增 View 消费窄 POD/read model，配合静态检查和模拟 hash 测试；若两个真实适配器需要共同只读 API，再抽取小接口。不能宣称当前编译器已阻止所有表现写入。

### 4.3 时间和调度

保持 `ApplyCommands → Input → Decide → Move → Body → SpatialBuild → Collision → Resolve → Spawn → Snapshot`。同阶段以 Order、注册顺序稳定排序；改注册顺序也是可能改变行为或存档的修改，不能被“自动依赖排序”悄悄替换。

- Session 驱动固定 Tick 或 ManualClock，Render 只插值。暂停原因、后台时间丢弃和多渲染帧无 Tick 的输入锁存保持不变。
- `AccessDeclaration` 表示依赖，实际 Job 仍必须传递传入 dependency 并返回完整 handle。主线程读取前完成相关依赖；空声明是串行 barrier，不是“没有访问”。
- 创建、销毁、重排、清关和 Native 释放在安全窗口发生。外部 API 若不能证明安全，就经 Session 同步入口进入；不能仅凭阶段叫 Spawn 就并发改表。
- 固定 Tick 不自动保证跨平台位级确定。继续声明当前配置、平台和算法的回放范围；并行归约、浮点模式、候选顺序、RNG 消费必须另测。
- 画质治理只影响表现。降低 AI 更新率若会改变决定或命中，就属于版本化模拟策略，不能作为透明视觉降档。

### 4.4 所有权与退出顺序

| 所有者 | 拥有 | 借用 | 结束时责任 |
| --- | --- | --- | --- |
| Bootstrap / 应用组合根 | 自己创建的临时 Mode、Module、Host | 外部传入配置、共享服务 | 先停止/解绑消费者；只销毁自己创建的 Unity 对象 |
| SimSession | World、Pipeline、Clock | 冻结后的模式信息 | 停止推进、完成 Jobs、退出系统、释放 World |
| SimWorld | 表、Registry、注册的 world-owned 资源 | 不借用会被其他 Session 随意销毁的 Native 状态 | 重置/清关按声明执行；最终释放一次 |
| View 绑定范围 | 订阅、cursor、私有实例、实例缓冲 | Session 的只读投影、共享资产租约 | Disable/重绑先解绑并清旧输入/缓存，最终归还实例与租约 |
| 资源提供者 | 共享资产与引用计数 | 加载设施 | 最后一个合法租约归还后释放；迟到结果也要归还 |

当前 Pipeline 正常退出是系统逆序 `OnDestroy`，World 资源则按注册顺序释放。不能笼统宣称已实现依赖逆序卸载。第一选择是资源互不在 Dispose 中访问同级资源，或由一个明确组合 owner 管理相互依赖；确需排序时才加入局部依赖释放，并验证旧行为。

生命周期标准流程为“校验 → 声明/分配 → 系统初始化 → 绑定表现 → 运行 → 停止入口 → Sync → 解绑/取消 → 系统退出 → 数据与私有资产释放”。这不要求支持运行中卸载模拟模块；首期安装只发生在创建 Session，卸载只发生在 Session 结束。

## 5 源码核对后的现状和真实缺口

**已有**表示应复用；**补契约**表示机制已存在但接入规范/公共验证不足；**补能力**表示源码中缺少可用的公共入口；**条件性**表示需求出现才做。

| 主题 | 分类 | 源码事实与最小优化 |
| --- | --- | --- |
| 模块和数据组合 | 已有 + 补契约 | WorldComposer 已拒绝空/重复模块；WorldLayout 已支持扩展列、重复资源检查与 LevelScoped。缺的是能力需求、提供者、内容兼容和容量汇总的启动前统一报告 |
| 安装失败回滚 | 补能力，先写失败测试 | DeclareData 可直接创建资源；BuildWorld 尚无覆盖整段声明/分配的事务清理。Pipeline 构造按顺序 OnCreate，尚无记录成功初始化系统并逆序回滚的通用路径。SimSession 的 catch 会释放已建 World，但之后 FixedStepClock 构造不在该 catch 内，非法 TickRate/MaxTicksPerFrame 仍可能在资源建好后抛错；先通过注入失败确认所有者与具体泄漏路径 |
| 稳定身份与绑定 | 已有 + 补契约 | EntityHandle、TimelineRevision、LevelVersion、武器 Revision 已有。SvRenderer/BwRenderer 已检查 Session 与版本；抽出共享测试，不能先断言它们跨 Session 有 bug |
| 保存布局身份 | 补能力 | World 保存表名/资源名及顺序，Pipeline 保存系统类型名；SimTable 保存容量、列数和逐列原始数据。NativeIO 校验元素大小，但等尺寸列调序或结构含义改变仍缺显式 schema 身份 |
| 配置兼容 | 局部已有 + 补能力 | WeaponRuntime 已有内容指纹，SkillSlots 校验定义；World 配置兼容总体仍由调用者保证。补 opt-in 的统一内容/规则指纹，不能称现有存档完全不校验 |
| 只读表现 | 已有设计 + 补契约 | POD ViewState、角色流、粒子桥接已有；部分 View 可访问 World。新增窄读取入口和负向测试，按需收窄访问 |
| 技能/武器 | 已有 + 条件性扩展 | SkillSlots 是激活/充能门，ActionTimeline 是动作时间线，WeaponRuntime 是单 owner 武器运行库。它们不是任意多角色能力系统；旧 SkillDefinition 是特定弹丸规则，也不是所有技能的统一定义 |
| 异步资产租约 | 条件性 | 现有同步/内置资源路径不必强制重构。真实引入异步加载后，最小化补请求取消、失效结果归还、共享所有权 |
| 空间/AI 后端 | 已有 + 补接入合同 | 已有网格、比较样例、有限 DecisionTree。以查询/决策语义和实测选择后端，不替换为一棵“大一统树” |
| 诊断/画质 | 已有 + 补聚合 | PerfHud、PipelineStats、FrameGovernor、VfxBudget、粒子预算和校准分配探针已经存在。统一报告内容和来源，不新增平行监控栈 |

关键核对入口：[WorldComposer](../Assets/SinglePlayerFoundation/Runtime/Composition/WorldComposer.cs)、[WorldLayout](../Assets/SinglePlayerFoundation/Runtime/World/WorldLayout.cs)、[TickPipeline](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs)、[SimSession](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs)、[SimWorld](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs)、[SimTable](../Assets/SinglePlayerFoundation/Runtime/World/SimTable.cs)、[NativeIO](../Assets/SinglePlayerFoundation/Contracts/Snapshot.cs)、[AccessGuard](../Assets/SinglePlayerFoundation/Runtime/World/AccessGuard.cs)、[WeaponRuntime](../Assets/SinglePlayerFoundation/L2Gameplay/Weapons/WeaponRuntime.cs)。上述缺口来自接口/实现结构核对，本次未新增失败复现，不把待测风险写成已经发生的线上故障。

## 6 分阶段实施

每一阶段独立提交、独立验收。下面的新文件名是拟议落点，不是仓库中已经存在的 API；最终名称可在实施时随两个消费者收敛。优先级代表依赖和风险，不是人日估算。

### P0 阶段 A 固定术语和兼容基线

**目标**：下一次扩展能明确判断哪些行为不允许改变。

1. 在本计划基础上补齐 Architecture 与 NewGameplayIntegrationRecipe 的实际 API 对照。特别更正旧设计中的 handle 字段、访问声明、事件容器、跨平台确定性和历史性能数字口径，不批量重命名运行代码。
2. 给当前九个经典玩法和四个新增变体建立兼容矩阵：Mode、模块顺序、表/列、容量、资源、系统顺序、规则配置、保存支持、渲染后端、输入/时钟方式。
3. 保留经典状态/hash/二进制 fixture，并为有意演进的 opt-in 模式单列版本。不把旧红项抹成绿项。
4. 为后续工作建立需求卡：具体扩展场景、两个消费者、权威/表现边界、容量与回退、主来源、预期收益和不采用条件。

**落点**：Docs/Architecture.md、Docs/NewGameplayIntegrationRecipe.md、拟新增 Docs/FoundationCompatibilityMatrix.md；复用 FoundationRulesTests、WorldSnapshotTests 和各玩法现有 fixture。

**验收**：矩阵每项都可定位到实际工厂/模块与测试；未实现、待原生验证、待真机验证分别可见。当前阶段不产生运行时差异。

**回滚/依赖**：纯文档与测试基线，可独立撤回；不阻塞正在进行的动作/视觉验收。B、C 可在 A 后并行。

### P1 阶段 B 组合预检和安装事务

**目标**：模块接入出错时在启动阶段给出明确错误，并完整退出。

1. 在现有 ModeDefinition/WorldComposer 旁加可选模块描述记录，例如拟议 ModuleManifest：稳定模块 ID、schema 版本、Provides/Requires、表和资源 owner、容量来源、保存能力。普通内容差异留在内容定义，不变成 Capability。
2. 在任何资源分配前先校验 SessionSettings（TickRate、MaxTicksPerFrame、队列容量等），冷路径再验证缺少提供者、重复提供者、能力版本不符、安装依赖环、同名不同 schema、错误 scope、容量冲突和不支持的保存配置。已有检查直接复用。错误报告包含模块与 key，不每 Tick 扫描。
3. 默认仍保持显式模块顺序和现有 Phase/Order；需求图用于检查和解释。确需自动排序时作为新模式的显式选择，输出最终顺序并纳入指纹，不能重排旧模式。
4. 补覆盖整个 Session 创建的安装事务，包括 Pipeline 完成后的时钟初始化失败；宿主绑定失败由外层组合 owner 清理。记录本次接管的资源和成功 OnCreate 的系统，后续失败时 Sync 已调度工作，按安全逆序退出已初始化项，释放本次拥有资源且不碰外部资产。正在失败的构造器/DeclareData/OnCreate 也可能已分配一部分资源，必须自带异常清理，或每次分配后立即登记临时 owner，成功后再转交；仅回滚成功系统不足以覆盖它。清理一个 owner 抛错时继续清其他 owner，保留原始失败和清理失败诊断，不对未初始化对象盲调普通退出逻辑。DeclareData 的旧路径先用兼容适配覆盖；只有需要无分配预检的模块再改成声明后分配。
5. 表扩展沿用 WorldLayout.Table；明确哪个模块是 owner、哪个只加列。当前“容量取最大值”保留为旧模式政策；新模式报告实际合并量，不能由第三方模块无提示撑大预算。

**落点**：Runtime/Composition/WorldComposer.cs、IGameplayModule.cs、ModeDefinition.cs、Runtime/World/WorldLayout.cs、Runtime/Scheduling/TickPipeline.cs；拟新增 Composition/ModuleManifest.cs、Tests/EditMode/CompositionContractTests.cs。

**首批消费者**：DriftSmoke 验证最小独立模块；Survivor + Brawler 验证真实可选武器/技能资源组合。

**验收**：缺依赖/冲突均在运行前失败；覆盖非法 SessionSettings、Pipeline 完成后的失败，并在声明、分配、首个/中间/最后 OnCreate 以及单个初始化器部分分配后注入失败，验证清理抛错不会阻止其他 owner 清理，资源/订阅回到基线；重试创建成功；同配置的旧系统顺序和 fixture 不变；启动报告能解释每份资源归属。

**回滚/依赖**：清单 opt-in，旧 IGameplayModule 接口继续可用；不支持热卸载。失败清理和描述清单分成可独立审查的提交，不以关闭安全检查作为回滚。

### P1 阶段 C Session 与 View 生命周期契约

**目标**：复用 View、换装、重开、恢复、双 Session 时无串绑、旧事件或泄漏。

1. 先对 SvRenderer/BwRenderer 已有检查提炼测试。绑定有效性至少由 Session 归属、时间线/关卡版本、实体 generation 以及实际需要的资源/request revision 决定；不要求把所有字段装进每个热路径实体。
2. 若两个适配器确有重复，提炼一个冷路径绑定 helper 或显式 stamp。绑定与解绑幂等；新 Session 即使同 tick、同 handle 也使旧 cursor 失效。
3. 制定 Disable、Enable、Destroy、换关、重开、同 tick 恢复、换装、后台/前台、资源重建的退出矩阵。HUD disabled 时取消输入和订阅，Enable 后只绑定一次；持有资源与临时禁用分别处理。
4. 多 View 独立读取相同 cue sequence；丢帧后 backlog 有界；重开/恢复清旧 cue，池槽复用以 SpawnTick/代际阻断旧拖尾。
5. 只在真实引入异步资产时加拟议 AssetLease/RequestToken 适配：取消请求不等于加载器一定停止；完成回调再次校验所有者和请求版本，失效则归还租约，不实例化旧视图。先接武器视觉与另一真实资产消费者，再判断是否公共化。

**落点**：SvRenderer.cs、BwRenderer.cs、Presentation/Particles/WeaponParticlePresenter.cs、Presentation/Rendering/MonotonicInterpolation.cs、SessionHost.cs 及 Shell/UI 接线；优先扩展现有 WeaponParticleTransitionTests、MonotonicInterpolationTests、SessionHostTests、MobileInputTests。

**验收**：A/B Session 相同 handle 不串绑；同 tick 恢复不重播；A 请求晚于 B 完成不覆盖 B；共享资产销毁一个消费者不影响另一个；循环启停/重开后订阅、池占用和资源数回到基线。纯同步路径将异步项标为不适用，不造虚假缺陷。

**回滚/依赖**：不改变 EntityHandle 布局和旧保存字节；先迁移两个 View，其余按矩阵逐项接入。异步资产子项可独立延期。

### P2 阶段 D 内容编译和保存兼容协议

**目标**：扩展内容和版本时，兼容则安全继续，不兼容则明确拒绝。

1. 明确三层数据：编辑用 Authoring（可有 ScriptableObject/资产引用）→ 校验并冻结的 Definition/Catalog（稳定 ID、POD、指纹）→ RuntimeState（冷却、HP、动作、RNG 等）。已有 Clone/Validate/fingerprint 复用；不强制所有配置继承一个大基类。
2. 建立显式模块/表/列/资源/系统 schema 标识和版本。契约版本说明可调用能力，保存 schema 版本说明字节含义，内容指纹说明本次规则参数，三者分别管理。内容指纹包括权威参数、TickRate、必要容量与排序规则；视觉指纹单列。当前 WeaponRuntime 含 VisualId/握持参数的指纹继续保留，拆分只在新协议中显式迁移。
3. 新保存入口增加外层 envelope，先验证 format、mode/module manifest、schema、规则内容指纹、长度上限与完整性，再交给现有恢复逻辑；损坏数据不能触发无上限分配。旧 raw snapshot API 保持；不要直接在旧 writer 头部塞字段破坏所有 fixture。
4. 等尺寸列重排、等大小 struct 字段换义、系统改名、同 ID 参数变化也应被识别。资源是否参与保存用明确策略描述：权威状态保存、派生缓存恢复后重建、表现状态丢弃；主线程权威状态不能因不带 IJobData 而漏掉。
5. 外部长期存档与同构进程检查点区分：原始 Native 内存快照不自动成为跨平台/跨版本持久格式。确需长期版本迁移时，为指定模块做显式字段序列化与 vN→vN+1 转换，不承诺任意历史版本自动恢复。
6. 旧存档由已知 legacy mode/layout reader 读取后，转入新格式；无法确认旧布局则提示不兼容，保留原文件。迁移先在临时 session/buffer 验证，成功后才替换目标；不要让失败迁移破坏正在玩的对局。

**落点**：Contracts/Snapshot.cs、Runtime/World/SimWorld.cs、SimTable.cs、Runtime/Scheduling/TickPipeline.cs、SimSession.cs；拟新增 Runtime/Session/SaveEnvelope.cs；各模式配置工厂、WeaponRuntime/SkillSlots 的既有保存测试。

**验收**：旧经典 fixture 原样可读；已声明支持的迁移前后固定输入重放结果一致；截断、未知版本、内容不符、等尺寸列调序均拒绝；失败恢复保持既定安全语义；恢复到同 tick 仍使 View 缓存失效。指纹只用于兼容判定，不能冒称安全认证。

**回滚/依赖**：依赖 A 的兼容矩阵和 B 的清单语义。先提供新 API 与双读路径，默认旧模式仍写旧格式；新格式切换单独提交并保留导出/回退方案。

### P2 阶段 E 把战斗扩展点整理成可组合规则

**目标**：新技能与武器可局部扩展，而不是不断给万能 Skill 或 Weapon enum 加分支。

建议的语义链为：

`输入意图 → 资格/消耗门 → 动作时间线 → 目标查询/轨迹 → 命中筛选/去重 → 效果申请 → 结算事实 → 表现提示`

1. **Ability 能力**表达玩家/AI 可以做的动作；**SkillSlot** 只是控制入口与充能门；**Weapon** 是装备和相应动作/发射方式；**Action** 是一次具体执行。相同 Ability 可来自武器、角色或道具，技能按钮不拥有伤害规则。
2. 先映射已有 SkillSlots、ActionTimeline、TickInputBuffer、WeaponRuntime、CombatShapes/Sweep、HitHistory、各游戏 damage resolver；共享纯机制，保留各游戏资格、属性、掉落和胜负决策。
3. **Gameplay effect**（伤害、治疗、位移、状态）与 **visual effect**（粒子、拖尾、震屏）分开。效果记录采用有界 POD，请求与结算事实分别命名。不要用一个带任意 object payload 的 EventBus 抹平区别。
4. 以 Survivor 的范围技能和 Brawler 的踢击/治疗验证公共资格与执行结果结构；以两个现有武器适配验证时间线、socket、命中去重。确认重复后才提炼新的纯函数或小数据结构。
5. 保留现有单 owner WeaponRuntime 与有限动作 family。新内容沿用已有 family；全新“持续引导/链式弹射”等行为先在独立模块实现。出现第二个消费者后，才考虑可编译的执行策略或独立 SoA action 表，不能直接把当前武器库宣传为多角色通用 GAS。
6. 权威接触仍使用固定 Tick 的标准 socket/轨迹。当前近战是一拍提交接触，不因新视觉拖尾自动改成多 Tick 连续切割；新切割机制需要新规则版本、完整扫掠和历史上限。

**落点**：L2Gameplay/Skills、Combat、Weapons；SvMobileSkills/SvFlyingSwords、BwMobileSkills/BwBeltSystems 和游戏 damage resolver；维持 Contracts/Weapons/WeaponViewState 的只读方向。

**验收**：两种玩法各增加一个数据变体和一个独立规则扩展，公共玩法/武器内容 enum 无新增项；队列满遵守 Check → 成功入队 → TryRecord，并保持每个 hit-history scope 单 writer、检查与记录间无其他 writer；该顺序本身不是并发事务。取消、打断、死亡、暂停、换装和恢复不重复消耗/释放；同一动作不重复命中；1× 连续视频和 socket/伤害断言同时通过。

**回滚/依赖**：先在 opt-in 新模式接入，经典规则不动；D 提供新状态的版本策略。不要把旧 projectile row-memory 的历史迁移捆绑进本阶段，另立兼容任务。

### P2 阶段 F 查询和表现后端的可替换契约

**目标**：同一语义可以换实现，换实现必须证明等价或显式声明差异。

**空间/AI：**

- 空间查询固定边界、空间坐标/高度、过滤、稳定平局、输出容量和溢出语义。Broad phase 与 narrow phase 分开；遍历顺序变化会影响浮点归约和有限候选，不能只比结果集合。
- AI 的感知事实、决策选择、承诺动作、移动执行分开。现有 DecisionTree 只负责有界 reactive selection，不拥有持续任务状态，也不是完整 BehaviorTree。
- 冷路径选择具体实现，热路径使用具体 Burst 兼容数据与函数；可用泛型策略/显式系统注册，但不引入逐实体接口调用、反射或托管委托。
- 网格、直接 AI 继续默认。四叉树或树策略只有在同输入完整成本更好且规则一致时才切换；试验后端可单独移除。

**表现：**

- 角色/武器/投射物输入保持只读 POD；按需要分开 batch、骨骼、粒子后端，不造一个承载所有渲染对象的万能 Renderer。
- 用现有 RenderCapabilities/RenderTier 选择设备能力；新增后端通过显式 factory/注册入口安装，probe 失败能回退。当前 RenderTier 有既定 GPU/DataTexture 路径，第三后端的首次接入可能需要一次工厂边界提炼；提炼后再以第二个可替换实现证明无需修改模拟。
- 区分设备能力、用户视觉偏好和运行时质量档。能力检测不能只看 bool；compute/indirect 路径需实际执行和读回/像素证据。
- 统一骨骼、武器、箭矢、拖尾的插值时刻；渲染可以少画装饰，不能改变权威轨迹、角色移动、危险提示含义。

**落点**：L1Simulation/Spatial、L2Gameplay/AI、Presentation/Rendering/RenderCapabilities.cs、Sprites/SpriteBatch.cs、Animation/GameplayCharacterPresenter.cs、Particles/ParticleRenderer.cs 与对应 game adapters。

**验收**：两个后端同一输入/种子/配置下模拟检查点相同；GPU 与回退结果满足已声明的数值/像素容差；缺 shader、不支持精度、重建和质量切换可恢复；空间基准覆盖均匀/聚集/稀疏/混合尺寸/高速及满容量，分别报告 build、query、Sync 和完整 Tick。

**回滚/依赖**：可与 E 并行；保持当前默认后端。性能无收益则保留契约测试、撤销候选实现，不为了“可扩展”增加每帧抽象开销。

### P3 阶段 G 公共接入测试和移动预算报告

**目标**：下一个模块能运行同一组契约测试，且扩展成本可量化。

1. 在现有 SPF.Testing 和测试程序集内提炼夹具，优先组合既有测试逻辑，不再造独立测试框架。模块只提供工厂、固定输入、状态读取和可选保存能力。
2. 夹具覆盖创建/满容量、句柄复用、结构提交、重开/清关、存档恢复、输入中断、双 Session、多 View、随机顺序和质量隔离；不适用项必须声明原因。
3. 启动清单汇总容量和预算，运行时诊断按需采样：模块/Session、实际 backend、Job 调度与等待、队列拒绝/溢出、Native/Unity 资源、上传 payload/API 字节、池高水位、输入拒绝原因。沿用 PerfHud/PipelineStats，不在 release 热路径格式化大量字符串。
4. 每个模式配置自己的移动预算档：目标设备/系统/图形 API、方向/安全区、目标帧率、CPU 主/工作线程、GPU、托管/Native/GPU 内存、上传和过绘制。低中高档明确保留核心命中/危险反馈，减少装饰而不减少权威容量。
5. 分配探针保留空操作和保留数组阳性对照，记录线程、测量窗口、分配字节/样本/帧与 GC collection；观察器失效必须失败或标注不可验证。初始化允许的分配与稳态零分配分别验收。
6. 实际 Android/iOS 包验证 IL2CPP/Burst、触摸、中断、图形回退、持续帧时 p50/p95/最坏值、温控、内存和电量。负载、热身、持续窗口按设备/模式预先固定；不能以短时桌面平均值代替。

**落点**：SPF.Testing、Tests/EditMode、Tests/PlayMode、Runtime/Diagnostics/PerfHud.cs、Shell/Performance/FrameGovernor.cs、Testing/ManagedAllocationProbe.cs；Docs/NewGameplayIntegrationRecipe.md 与 CI 产物清单。

**验收**：一个不修改核心源码的外部新游戏模块通过公共夹具；全部九个经典玩法和四个变体通过各自适用回归；精确 commit/tree、测试范围、失败/跳过与原始证据齐全。无物理设备时明确停在“软件门槛通过，真机待验”，不能签署移动性能达标。

**回滚/依赖**：夹具从 A 开始逐阶段积累，不必等到 G 才测试；性能报告和工具可独立交付。新预算不能覆盖/放宽现有 GC、物理和动作门槛。

## 7 三个具体扩展示范

这些是后续验证场景，不是本次新增功能承诺。

### 新增一个俯视角护送玩法

1. 增加 `EscortFoundation.Runtime`：自己的 Keys、Config、EscortModule、护送目标/进度资源、规则系统。复用移动、空间查询、共享战斗与 SkillSlots。
2. 增加 `EscortFoundation.Presentation`：读目标/角色快照，复用 SpriteBatch、人物和有界 FX。
3. 增加 `EscortFoundation.Game`：Bootstrap、HUD source、输入适配；以 ModeDefinition 组合。Editor 入口和 Tests 单列。
4. 清单声明护送目标的身份、Level scope、保存策略、容量和最低反馈档；跑接入夹具、完整可玩闭环与移动预算。

**成功判据**：不修改 SPF 的公共玩法 enum、World 表结构或调度循环；只在应用组合入口选择该模式。如果护送规则迫使 Core 认识 Escort，边界设计失败，应先检查规则层是否越界。

### 新增一个带减速效果的投射技能

1. 技能槽提供稳定 ID、激活方式、冷却/充能；内容定义包含弹体参数与减速规则。
2. Tick 通过资格门后启动 action/pulse；权威投射轨迹查询命中，使用稳定身份去重；接受效果申请后由游戏 resolver 结算。
3. 减速是 gameplay effect；蓝色拖尾、命中特效是 cue。减速状态放游戏自己的 versioned resource/列，表现不反推减速时长。
4. 先用现有机制组合；若现有范围/生命周期足够，公共核心修改数为 0。若需要新的状态叠加规则，先作为本游戏小策略，第二个不同消费者出现后才下沉。

**成功判据**：加入新 ContentId 不增公共技能枚举；禁止出现“UI 已扣充能但 Tick 没激活”或“特效播了才造成伤害”；满容量、打断和保存恢复有明确结果。

### 新增一个角色渲染后端

1. 消费已有角色 stream 与武器 ViewState，安装在表现 factory 边界；向能力报告声明输入、精度、容量、实际后端与回退。
2. 资产与实例分别拥有，后端销毁/重建不动 World；角色身份用 Session 归属与 generation，池槽再带 binding revision。
3. 同一模拟录制分别交给原后端和新后端，检查 socket、插值、关键像素、连续动作与预算；禁用新后端仍完整可玩。

**成功判据**：模拟与 L2 规则无改动；初始化选择后端一次，热循环无反射/动态容器；质量切换前后权威检查点相同。任意外部模型/骨架导入不在本例承诺内。

## 8 兼容 迁移与明确延期

### 九个经典玩法和四个变体

经典 Snake、RPG、Survivor、Platformer、Defense、Puzzle、Sling、Brawler、Story 按各自时钟/保存契约保留基线；新增 Shooter、Guard、Flying Sword Horde、Belt Scroller 继续作为不同压力的消费者。武器/技能等 opt-in 组合单列配置版本，不因它们共用某个基础玩法就把保存布局视为相同。

迁移顺序为：最小 DriftSmoke → 两个已有真实消费者 → 其余模式逐项登记和验收。先通过兼容适配保留旧构造入口、字段与事件语义，再逐步弃用；弃用必须给替代方式、版本范围和自动检查，不能以“大重构”一次删旧路。

### 本轮明确延期

- 全面迁移 Unity Entities/Latios 或升级 Unity；除非出现不能由当前内核满足的明确需求和有收益的有界原型。
- 通用 Entity/Actor 对象继承树、全局 ServiceLocator、热路径反射注册、任意 object 消息、无限容量技能/行为图。
- 运行中热插拔模拟模块、多 World 网络同步、预测回滚、MMO/热更基础设施。
- 为统一命名重写所有 EntityHandle、经典状态 struct 和历史存档；旧 projectile row-memory 迁移另行定义范围。
- 承诺任意骨架/动画资源、任意手机或跨平台位级确定；这些需要各自的资产、执行和设备证据。

## 9 建议的第一批工作

先交付三个可以独立审查的结果：

1. **兼容矩阵与语义文档校准**：固定现状，纠正旧架构文档与实际 API 的差异。
2. **组合失败测试与最小回滚修复**：从真实安装失败路径切入，避免先造复杂插件系统。
3. **两个现有 View 的生命周期契约测试**：覆盖双 Session、同 tick 恢复、重绑和多实例资源退出，再判断是否抽 helper。

随后做 opt-in 保存 envelope 和内容指纹，最后按真实新需求推进技能策略与后端扩展。这样每一批都增加可证明的安全扩展能力，并且可以暂停、回滚或独立发布；无需等一场全面架构替换才得到价值。

## 10 本计划的证据范围

本段记录最初计划发布时的证据：检查了组合、布局、调度、Session、句柄、保存、共享武器/技能、两个玩法表现绑定、粒子、AI、预算和程序集引用的实际代码，并对照固定版本的开源源码与现有调研。原始调研没有修改运行时、引入外部包、运行外部框架或重写存档。后续独立实施以文首状态和各阶段验证记录为准；尚未实施的提案不得套用历史测试或桌面性能。

## 阶段 A 完成记录

[真实组合矩阵](FoundationCompatibilityMatrix.md)记录19个组合、154个固定源码哈希和65个已有测试方法，并保留可重复冷启动探针。[Architecture](Architecture.md)和[接入配方](NewGameplayIntegrationRecipe.md)已按实际 API 校准，未新增运行时接口或改旧快照。静态验证覆盖150个相对链接引用、11个菜单和83行表格；6个代码示例编译通过，DriftSmoke 60 tick、技能槽和扩展列示例执行通过，完整 .NET 回归1,050通过。集成后再次静态检查、编译/执行示例均通过。

[Stage A 验证](FoundationStageAValidation.md)说明源码与原生基线、冷探针局限和兼容例外。下一阶段先复现真实组合失败与生命周期问题，不把这些文档提案直接当作已实现能力。
