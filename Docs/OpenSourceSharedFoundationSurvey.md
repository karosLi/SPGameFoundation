# 开源共享游戏基座调研与 SPGameFoundation 取舍

调研日期：2026-10-07（UTC）。本仓库对照源码：`7e9419155827a40dfe9a1cea19546e053f9f7c47`。外部源码固定到下文提交；维护状态来自当日 GitHub API。本文是研究与建议，不代表引入依赖、修改运行时或完成新的性能验收。

## 结论

有值得研究的相似项目，但本次核查的七组项目分别解决不同层面的问题。没有找到一个已经验证、可直接替换当前 Unity 2022.3 移动端多玩法基座，同时覆盖固定 Tick、稳定身份、共享战斗、技能 HUD、武器动作与低端渲染回退的现成仓库。

最值得读的组合：

1. **GameFramework / UnityGameFramework**：应用模块、异步资源与视图实例生命周期。
2. **QFramework**：小型组合根、命令与查询、事件订阅的退出责任。
3. **Morpeh**：组件池、稳定实体身份、系统分组及明确的 Jobs 同步边界。
4. **Latios Framework**：可选功能模块、world/scene 生命周期、DOTS 数据与表现配合。
5. **Unity ECS Samples / Megacity Metro**：从具体代码学习配置烘焙、输入事件转模拟、实例化与回收桥接，而非把示例整游当通用框架。

**建议继续使用现有 SoA + Jobs/Burst + Module/Table/System/SessionHost。优先吸收生命周期、配置预检和诊断的做法，不改换整套 ECS 或引擎版本。** 当前动作自然度、密集画面可读性和 Android/iOS 真机证据的缺口，需要继续用本项目的实际场景验证，换框架不能替代这部分工作。

## 比较口径

“共享基座”至少要区分以下五种，避免按功能清单或 stars 排名：

| 类别 | 实际复用对象 | 本次代表 | 与本仓库的关系 |
| --- | --- | --- | --- |
| 应用与业务框架 | 启动流程、UI、资源、音频、事件、状态组织 | GameFramework、QFramework | 补强 Shell / Game / 资源管理，不替代模拟内核 |
| 数据与执行内核 | 实体身份、组件存储、筛选、系统调度 | Morpeh | 可比较容器与安全边界，但不等于技能、战斗或移动端完整方案 |
| 数据导向功能基座 | DOTS 扩展、场景、动画、物理、渲染等可选模块 | Latios | 目标接近共享能力层，但版本与技术栈耦合显著 |
| 示例与整游参考 | 一个具体功能或一款游戏的接线和验证 | ECS Samples、Megacity Metro | 适合提炼局部模式；不能直接推导跨品类复用成本 |
| 在线游戏基础设施 | 客户端与服务端、实体、消息、场景/调度 | ET | 当前单机需求与许可均不支持作为直接依赖推荐 |

取证采用官方 README、许可证、package/manifest、实际源码和仓库 API；未运行这些外部项目，也未作同机 A/B。维护日期只说明观察到的活动，不说明质量或商业支持承诺。引擎最低版本声明不等于已经在本项目 Unity 2022.3、IL2CPP、ARM64 和各图形后端通过编译与性能测试。

## 本仓库已经具备的能力

对照 [架构](Architecture.md)、[新玩法接入配方](NewGameplayIntegrationRecipe.md)及源码，可以确认以下机制已经存在，不应因调研再造一套：

- [IGameplayModule](../Assets/SinglePlayerFoundation/Runtime/Composition/IGameplayModule.cs) 分开声明数据与注册系统；[WorldComposer](../Assets/SinglePlayerFoundation/Runtime/Composition/WorldComposer.cs) 检查空模块和重复 Id。
- [WorldLayout](../Assets/SinglePlayerFoundation/Runtime/World/WorldLayout.cs) 已有容量声明、扩展列、world-owned IDisposable 资源、LevelScoped 表及可重置资源。
- [SimSession](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs) 已有固定步长、手动 Tick、快照恢复、重开与 TimelineRevision；[SessionHost](../Assets/SinglePlayerFoundation/Runtime/Session/SessionHost.cs) 区分显式暂停和失焦/后台挂起，恢复时不补跑后台时间。
- [EntityHandle](../Assets/SinglePlayerFoundation/Contracts/EntityHandle.cs) 已采用 index + generation；不能把行号当持久身份。无 handle 的 pooled 表仍须采用其既定身份契约。
- [EventQueue](../Assets/SinglePlayerFoundation/Runtime/World/EventQueue.cs) 已有固定容量、溢出计数及是否参与快照的区分；表现事件不应恢复重播。并行写入顺序仍需按项目的确定性契约处理。
- 共享战斗、武器、HUD 和表现适配已经有多个消费者；新玩法以规则、配置、适配器接入，而非复制游戏循环。[移动接入](MobileGameplayIntegration.md)与[权威武器合约](AuthoritativeWeapons.md)是继续演进的约束。

因此，后文“可借鉴”是审视现有实现或在真实需求出现时做小幅扩展，并不自动意味着某项能力缺失。

## 1 GameFramework 与 UnityGameFramework

**定位：最值得借鉴应用服务和视图生命周期的传统框架。** GameFramework 提供核心服务，UnityGameFramework 把服务接到 Unity 组件、资源、实体和 UI。其 EntityLogic 是 MonoBehaviour，不是本仓库的高密度 SoA 模拟实体。

### 维护与兼容性

- 两仓库均未归档，均为 MIT：[核心许可证](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/LICENSE.md)、[Unity 层许可证](https://github.com/EllanJiang/UnityGameFramework/blob/e7eb3ef11393df0ac504204c480cdb839752b05f/LICENSE.md)。
- 默认分支最新提交分别为 [d0c010b，2021-09-28](https://github.com/EllanJiang/GameFramework/commit/d0c010b05167c58e92350449d04864a91ca13fd2)与 [e7eb3ef，2021-10-28](https://github.com/EllanJiang/UnityGameFramework/commit/e7eb3ef11393df0ac504204c480cdb839752b05f)。API 的 `pushed_at` 分别为 2023-09-05 / 2023-05-21，不能拿它替代默认分支代码日期。
- 当日两个仓库的 releases 列表均为空。[Unity package](https://github.com/EllanJiang/UnityGameFramework/blob/e7eb3ef11393df0ac504204c480cdb839752b05f/package.json) 写的是包版本 2021.05.31、最低 Unity 2017.1；不是 Unity 2022.3 验证证明。
- 对当前项目的选择：读源码模式，若真要采用模块必须自行承担旧代码适配与回归成本。

### 三个可迁移模式

1. **模块顺序与逆序退出。** [GameFrameworkEntry](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/GameFrameworkEntry.cs#L25-L49) 顺序 Update、反向 Shutdown；[创建模块](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/GameFrameworkEntry.cs#L102-L130) 按 Priority 插入。可借鉴明确退出责任的原则：先退出消费者，再释放其依赖；Priority 逆序本身不保证依赖关系正确。SPF 继续使用显式组合与读写依赖，不照搬全局静态定位器、反射创建或“一个优先级解决全部依赖”。
2. **异步完成时先检查请求是否已失效。** [EntityManager.HideEntity](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Entity/EntityManager.cs#L672-L687) 把尚未完成的加载 serial 标记为待释放；[加载成功回调](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Entity/EntityManager.cs#L1244-L1265) 检查该 serial，失效就释放资源而不显示旧实体。这比“回调来了就 Instantiate”更适合重开、返回菜单和换装中断。SPF 若增加异步加载，应同时检查 session/表现生命周期、owner generation 和请求版本，不能只判断实体 index。
3. **显示、隐藏、回收、最终释放是不同阶段。** [EntityLogic](https://github.com/EllanJiang/UnityGameFramework/blob/e7eb3ef11393df0ac504204c480cdb839752b05f/Scripts/Runtime/Entity/EntityLogic.cs#L104-L150) 区分 OnInit/OnShow/OnHide/OnRecycle；[ObjectPool](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/ObjectPool/ObjectPoolManager.ObjectPool.cs) 区分 SpawnCount、锁定、可释放、过期等条件。可审视本项目的武器/VFX/HUD 缓存退出矩阵；不要把该池的 capacity 当 SPF 的硬容量上限，它允许先注册对象再尝试释放可回收项。

### 边界

[EventPool](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/EventPool/EventPool.cs#L218-L289) 区分排队 Fire 与立即 FireNow，处理后回收参数；对“参数只在回调期间有效”很有参考价值。但其 Queue/Dictionary/lock 和托管对象机制，不满足直接替换 Native 有界战斗队列的前提。也不能据线程安全推出确定性顺序。

## 2 QFramework

**定位：小型架构约定与 Unity 工具集。** 最适合学习界面、命令、数据与服务如何接线，不应把它当 Jobs/Burst 模拟或通用战斗框架。

### 维护与兼容性

- 未归档，根许可证 [MIT](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/LICENSE)；工具集中的第三方部分仍须分别检查许可证。
- 默认分支 [956cb1f，2026-09-24](https://github.com/liangxiegame/QFramework/commit/956cb1ff6f712dec138ee56cc090a6dc9ec913b9)；API latest release 为 [v1.0.246-Unity2018Compatible，2026-05-27](https://github.com/liangxiegame/QFramework/releases/tag/v1.0.246-Unity2018Compatible)。master 不等于该 release。
- 固定提交的 [README](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/README.md) 声明 Unity 2018.4 至 Unity 6。产品列表是项目方列举的使用案例，不是本项目的移动端压测数据。

### 三个可迁移模式

1. **每个功能聚合有一个可解释的注册入口。** [Architecture 初始化/销毁](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L70-L164) 先注册，再初始化 Model/System，退出时 Deinit。可把 SPF 新玩法接入文档做得同样直接：配置、数据、系统、表现、输入分别由谁装配。现有 ModeDefinition 已承担组合根，不需要再叠加一个全局 Architecture 单例。
2. **输入意图与查询分开。** [Command/Query 的执行入口](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L173-L205) 以及 [Controller/Model 能力接口](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L224-L285) 让接线方向清楚。但 QFramework 的 SendCommand 在调用处立即 Execute，不是固定 Tick 命令缓冲；Controller 仍可取得 Model，不能声称编译器强制所有数据只读。SPF 保留 HUD→锁存命令→Tick 授权→只读快照这条更严格的边界。
3. **订阅、资源与生命周期绑定。** [解绑触发器](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L504-L610) 支持 Destroy/Disable 时统一 UnRegister；[ResLoader](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.Unity2018%2B/Assets/QFramework/Toolkits/ResKit/Scripts/Framework/ResLoader/ResLoader.cs#L341-L408) 退出时清空待加载、释放引用、移除回调。适合检查多实例 HUD、菜单反复打开、资源共享与退出；必要时用本项目的小型 owner scope 实现，不必导入完整 Toolkit。

### 边界

核心使用托管字典、委托、列表和部分 LINQ；[EasyEvent.Register](https://github.com/liangxiegame/QFramework/blob/956cb1ff6f712dec138ee56cc090a6dc9ec913b9/QFramework.cs#L800-L841) 生成带闭包的解绑项。适合冷路径注册，不能由此承诺高频战斗零分配。普通事件也没有 SPF 的 tick、generation、快照版本和固定容量语义。

## 3 Morpeh

**定位：Unity / .NET 的 ECS 内核与开发工具。** 更适合比较存储、身份、调度和测试方式；不提供本项目已经形成的共享战斗、技能 HUD、武器 IK 和移动渲染完整链路。

### 维护与兼容性

- 未归档，[MIT](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/LICENSE.md)。默认 main 为 [7bcaf78，2025-07-28](https://github.com/scellecs/morpeh/commit/7bcaf7845e667e9aec397ad97911ba2c5a28a296)，最新 GitHub 正式版 [2024.1.1](https://github.com/scellecs/morpeh/releases/tag/2024.1.1) 实际发布于 2025-04-01。
- 不能据 main 日期断言停更：stage-2025.1 的 [a370913](https://github.com/scellecs/morpeh/commit/a37091308ffb0724600c9674ffc186f1ab5161bd) 提交于 2026-09-14，stage-2024.2 也在 2026-09-04 更新。以下源码分析固定 main，不混用开发分支 API。
- [package](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/package.json) 最低 Unity 2020.3，2022.3 在声明范围内；[README](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/README.md) 的 Inspector 工作流还要求 Tri Inspector。

### 三个可迁移模式

1. **身份包含 world 的生命周期。** [Entity](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Entities/Entity.cs#L15-L45) 包含 Id32、Generation16、WorldId8、WorldGeneration8；[有效性检查](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Worlds/WorldEntityExtensions.cs#L84-L110) 检查实体与 world 的代际。对 SPF 有价值的是审计“跨 Session 缓存”是否同时持有正确 Session 身份和 EntityHandle。现有 handle 在所属 registry 内已能防止复用；不能仅因它未编码 world 就判为 bug。也不建议照搬位数，其 [world generation 测试](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Tests/dotnet~/WorldTests.cs#L74-L87) 明确模 256 循环。
2. **World 拥有数据，结构变更有提交边界。** [World.GetStash](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Worlds/WorldStashExtensions.cs#L49-L67) 管理组件仓储；[Stash](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Stashes/Stash.cs#L19-L86) 使用 T[] 与 IntSlotMap，新增组件记录 transient change；[SystemsGroup](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Systems/SystemsGroupExtensions.cs#L59-L105) 在系统之间 Commit、每类更新末尾完成 Jobs。SPF 已有固定 SoA、扩展列、资源与安全窗口，可用此作对照检查；不要为名称统一引入第二套 filter/stash。
3. **核心测试与 Unity Job 测试分开。** [.NET 测试工程](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Tests/dotnet~/Tests.csproj)、[ID 回收时机测试](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Tests/dotnet~/EntityIdReuseTests.cs)、[NativeStash Job 测试](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Tests/unity/NativeStashTests.cs#L37-L64) 分别验证容器语义与原生执行。SPF 已采取 .NET harness / Unity EditMode / graphics PlayMode 分层；下一步应查覆盖缺口，不能把 .NET 绿灯当 Burst 或真机绿灯。

### 边界

[Jobs 文档](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/README.md#-unity-jobs-and-burst) 明确没有跨 SystemsGroup 的全局依赖图，NativeFilter/NativeStash 不得跨单次 tick/Commit 保存；[Commit 检查](https://github.com/scellecs/morpeh/blob/7bcaf7845e667e9aec397ad97911ba2c5a28a296/Scellecs.Morpeh/Core/Worlds/WorldExtensions.cs#L169-L199) 部分受调试宏限制。不能认为“换成熟 ECS”就自动消除 Job 生命周期错误。README 列举 Android/iOS 游戏是应用线索；本次没有取得可复现的目标手机端到端性能报告。

## 4 Latios Framework

**定位：最接近数据导向共享功能基座的候选，但当前版本迁移成本高。** 它扩展 Unity Entities，包含多个可选运行模块；与本项目独立 SoA 内核不是同一层的轻量替换件。

### 维护与兼容性

- 未归档，默认 master [ae2262a](https://github.com/Dreaming381/Latios-Framework/commit/ae2262afd5c80ac4850fef23c9e7d4a971fabf53) 与正式版 [v0.16.1](https://github.com/Dreaming381/Latios-Framework/releases/tag/v0.16.1) 均为 2026-09-27。
- [package](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/package.json#L1-L14) 要求 Unity 6000.3.8f1、Entities 1.4.8、Entities Graphics 1.4.21、Burst 1.8.30。[变更记录](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/CHANGELOG.md#L495-L516) 写明 0.12.0 已移除 Unity 2022 LTS 支持。
- [许可证](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/LICENSE.md) 为 Unity Companion License for Unity-dependent projects，并有第三方 notices，不是 MIT。

### 三个可迁移模式

1. **功能显式安装，运行顺序在组合根可见。** [StandardExplicitBootstrap](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Editor/ScriptTemplates/StandardExplicitBootstrap.txt#L20-L69) 创建 World、启用显式排序、按需安装 Transforms/Myri/Kinemation/LifeFX，再进入 PlayerLoop。SPF 可沿用 ModeDefinition，列明哪个玩法启用哪些共享能力和表现后端；不要为此新增万能玩法枚举。Latios 是一个包含多个模块的 UPM 包，按需运行不等于每个模块都是独立零依赖包。
2. **Native 数据所有权与依赖登记绑定。** [LatiosWorldUnmanaged](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Framework/LatiosWorldUnmanaged.cs#L335-L511) 区分只读/写入 collection component、登记系统 Dependency，外部调用需要显式更新依赖，移除/替换也处理释放依赖。可审计 SPF 的 IJobData 与访问声明是否覆盖共享查询、粒子与表现资源；继续维护自己的 Schedule 输入/输出依赖和统一 Sync/Dispose 责任。
3. **Tick 与结构提交、插值分阶段。** [TickedSuperSystems](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Systems/Ticking/TickedSuperSystems.cs#L86-L126) 组织 SyncPoint、修正、Input、History、Simulation，另设插值组；[Playback](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Core/Systems/Ticking/TickedSyncPointPlaybackSystem.cs#L177-L186) 先完成依赖，discardPreviousTick 时不播放旧命令。SPF 已有类似阶段约束，尤其应继续测试快照恢复到相同 tick、重开与旧表现事件清理，不需要引入网络回滚才能采用这种边界。

### 边界

引入当前版会涉及 Unity 6、Entities 数据布局/烘焙/调度、渲染和存档映射等一整套迁移。没有当前项目的收益证据，不建议实施。[README](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/README.md#L12-L32) 的桌面支持与其他平台 native plugin 说明，也不能视作本项目 Android/iOS 全模块认证。固定步长与 ECS 均不自动保证跨平台浮点或并行归约的位级确定性。

## 5 Unity EntityComponentSystemSamples

**定位：优先阅读的官方机制样例。** 它是一组独立的学习/功能工程，不是已经拼好的跨品类游戏框架。

- 未归档；master [6786a74，2026-03-06](https://github.com/Unity-Technologies/EntityComponentSystemSamples/commit/6786a741ee1f118ed14cecfa02beae8e926937b0)。[README](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/README.md) 要求 Unity 6.2 / DOTS 1.4；[Entities101](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/Dots101/Entities101/ProjectSettings/ProjectVersion.txt) 实际为 6000.2.10f1。
- [许可证](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/LICENSE.md) 为 Unity Companion License。

三个阅读点：

1. [ImageGeneratorAuthoring](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/EntitiesSamples/Assets/Baking/BakingDependencies/ImageGeneratorAuthoring.cs) 显式追踪 SO、贴图、Mesh、Material 依赖，并区分只用于烘焙的数据。SPF 可把现有配置冻结进一步做成明确的校验/编译入口：模拟保留只读值与稳定 Id，View 保留贴图/音频引用，相关资源变化触发重建。该建议不要求使用 Entities Baker。
2. [DotsUI.EventSystem](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/Dots101/OtherSamples/DotsUI/Assets/Scripts/Gameplay/EventSystem.cs) 清理上帧事件、播放 UI 命令缓冲，再创建新缓冲。可借鉴消费边界，但样例按渲染帧工作；SPF 已有适配多渲染帧/少模拟 tick 的短按锁存，应保留其 fixed-tick 语义。也不要照搬每帧创建临时缓冲来声称无分配。
3. [FixedRateSpawnerSystem](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/Dots101/Entities101/Assets/HelloCube/11.%20FixedTimestep/FixedRateSpawnerSystem.cs) 展示固定步长组。代码使用浮点时间/sin，适合作为时钟组织参考，不是确定性回放证明。

## 6 Megacity Metro

**定位：多人射击技术参考整游。** 可借鉴真实接线；预测、网络、UGS、3D 城市场景不是当前单机基座的必要前提。

- 未归档；master [07652ee，2025-10-23](https://github.com/Unity-Technologies/megacity-metro/commit/07652ee74a1f322c2c3e607020f07be720175680)。[README](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/README.md) 明确不接受 PR、GitHub review 或 issue 管理请求，不能按普通维护型依赖预期支持。
- [工程版本](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/ProjectSettings/ProjectVersion.txt) 为 Unity 6000.1.0f1；[manifest](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Packages/manifest.json) 有 Netcode 1.3.6、Physics 1.3.14、Entities Graphics 1.4.12、URP/VFX Graph 17.1.0。[许可证](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/LICENCE.md) 为 Unity Companion License。

三个阅读点：

1. [PlayerVehicleInputSystem](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Gameplay/Client/Player/PlayerVehicleInputSystem.cs) 归一触屏/桌面输入；[ShootingSystem](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Gameplay/Mix/Shooting/ShootingSystem.cs) 执行射击模拟；[LaserVisualJob](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Gameplay/Mix/Shooting/Jobs/LaserVisualJob.cs) 单独处理激光视觉位置/命中特效。与 SPF 权威命中、只读表现的方向相通；样例仍共享 VehicleLaser 数据，不能把它说成与 SPF 完全相同的快照隔离。
2. **最值得拿来做现有代码审查的是** [DynamicInstanceLinkSystem](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Utils/Pooling/Systems/DynamicInstanceLinkSystem.cs)：显式区分创建/启用、实体销毁、实体禁用、World 销毁，并处理表现实例回收与 Transform 同步。对 SPF，应检查 weapon/VFX/角色 View 在这些路径和重开/快照恢复时是否解绑，不需要为此让每个模拟实体创建 GameObject。
3. [QualitySettingsSelectorSystem](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Assets/Scripts/Utils/QualitySettingsSelector/Systems/QualitySettingsSelectorSystem.cs) 的条件编译分支每 60 帧抽一个单帧 FPS，低于 60 就降档。质量分层方向值得参考，算法不宜照搬。SPF 应继续依据持续帧时、分位数、温控、GPU/CPU 回退和核心反馈可读性定档。

[移动端前置条件](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/Documentation/prerequisites.md) 列出 Android 八核、4GB、Adreno 640，iOS 为 iPhone 12+。这比“支持移动端”具体，但仍不能证明本项目目标手机、2D 透明粒子/蛇身或多玩法负载达标；150 人规模也不是相同负载的性能比较。

## 7 ET

**定位：可阅读的网游/服务端基础设施，当前版本属受限许可，不列入宽松开源依赖候选。**

- 未归档；当前 ET10 master [743c635，2026-08-26](https://github.com/egametang/ET/commit/743c635dd5b0f6f6bab549c0093382c85f3f8307)。[README](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/README.md) 以分布式 MMO、Fiber/Actor、热更等为主；[工程版本](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/ProjectSettings/ProjectVersion.txt) 为 Unity 2022.3.62f3。
- **当前 [LICENSE](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/LICENSE) 明确商业项目上线前需要许可，列明费用 4999 人民币，限制修改后以开源项目再分发；第三方商业插件还需另行授权。** 不能沿用历史介绍或 GitHub 可见性，声称当前 ET10 是 MIT、免费商用或可任意复制。这里仅记录许可证原文要点，没有进行法律适用性审查。

有参考价值但本次不复制的三处：

1. [EntityRef](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/Packages/cn.etetet.core/Scripts/Core/Share/Entity/EntityRef.cs) 以 InstanceId 检查复用对象引用是否仍有效。可作为 generation handle 审计对照，不是 SoA 数据布局。
2. [BuffTickComponentSystem](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/Packages/cn.etetet.spell/Scripts/Hotfix/Share/BuffTickComponentSystem.cs) 销毁时撤销定时、异步和被覆盖行为，可用于武器/Buff 退出检查清单。
3. [CheckHashHandler](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/Packages/cn.etetet.lockstep/Scripts/Hotfix/Server/Room/C2Room_CheckHashHandler.cs) 按帧比较状态 hash、返回失配快照。可学习失配定位思路；有 hash 检查不等于整个框架跨平台确定。

[Spell 包依赖](https://github.com/egametang/ET/blob/743c635dd5b0f6f6bab549c0093382c85f3f8307/Packages/cn.etetet.spell/package.json) 连接 core/config/proto/unit/行为树/数值/地图/网络/路由/AOI/login/YooAssets/UI 等，不应把它当可直接摘出的轻量技能库。当前单机目标没有理由为此引入客户端/服务端与热更基础设施。

## 建议如何落到现有路线

以下是建议顺序，不是本次已实施的承诺。优先推进已有动作、可读性和真机验收；可并行做低风险审计。成本以影响面表示，不在未写原型前给不可靠的人日估计。

| 优先级 | 建议与已有落点 | 最小做法与验收 | 成本与不采用条件 |
| --- | --- | --- | --- |
| P0 | 保持现有验证路线 | 对最新精确提交重跑受影响原生测试，审视正常 1× 连续动作/密集画面；真机测试独立列门槛 | 不因调研暂停当前修复；外部框架样例不代替本项目结果 |
| P1 | 跨 Session 资源与表现身份审计；参考 GF、Morpeh、Megacity | 从 Survivor 与 Brawler 两个消费者检查 Session 实例、EntityHandle、TimelineRevision、请求版本的绑定/失效。覆盖暂停、禁用、换装、重开、同 tick 恢复、销毁后旧回调、双 Session 并存 | 先测试，查出真实缺口再改；不为了统一字段改变旧快照布局，不把普通所有权问题变成全局服务定位器 |
| P1 | 事件订阅与退出矩阵；参考 QFramework、Megacity | 给现有 HUD/角色/VFX 接线做注册→消费→取消→回收检查；反复打开/关闭和重开后，订阅数、实例数、资源引用回到基线，无旧事件重播 | 冷路径可以有小型 scope；热路径保留 Native 固定队列、溢出统计与 generation 语义 |
| P2 | 模块能力及配置预检；参考 Latios、ECS Samples | 沿用 ModeDefinition/WorldComposer；先输出每个玩法已启用的能力、表容量、资源所有者、所需表现后端和保存版本。按真实需求加启动期校验；模拟配置仍在开局前冻结 | 已有空/重复模块与资源校验不重复做；未出现复用冲突前，不先建设庞大依赖图、插件市场或反射注册系统 |
| P2 | 可复用接入契约测试；参考 Morpeh | 提炼现有 EntityRegistry/WorldSnapshot/LevelScope/Pipeline/SessionHost/MobileInput 测试的公共夹具；新玩法执行容量耗尽、稳定身份、重开、恢复、输入中断和质量切换不改模拟结果的相同断言 | 整理覆盖率而非追求测试数量；真实 Jobs/Burst、图形、IL2CPP/设备不能被 harness 替代 |
| P2 | 运行时诊断；参考 GF Debugger 与 Morpeh 工具方向 | 保留 PerfHud 已有的 tick、SyncWait、实体/表容量与创建失败指标，按需补充模块、Session 生命周期、队列溢出和实际渲染后端；开发构建按需采样 | 不把 debug observer 开销混入正式基准；不要在 release 热路径每帧拼接大字符串 |
| P3 | 异步资源加载抽象；参考 GF、QFramework | 仅在接入实际异步资源时，新增最小请求租约/取消/退出合约；用两个真实消费者证明共享需求，测试失效请求释放和共享资源最后引用释放 | 当前同步或内置资源路径无此需求时不造完整资源框架；先选一个必要适配器，不同时引入多套资源管理 |

### 最值得先补查的五个用例

这是审计清单，部分已经被现有测试覆盖；实现前先核对，避免重复测试或错误宣称缺陷。

1. **两个 Session 的实体 handle 数值相同。** A 的旧 View/异步回调不能命中 B；由 Session 归属或独立 binding 身份隔离，不必把所有 handle 格式重写。
2. **重开或恢复到相同 tick。** TimelineRevision 变化后，动作插值、粒子 backlog、技能提示不消费上一条时间线的缓存；既有武器/粒子测试应继续纳入回归。
3. **禁用与销毁不同。** HUD disable 清理输入/订阅，重新 enable 只绑定一次；模拟实体消失或 owner generation 更新时，表现对象按自己的所有权回收。
4. **异步请求发生重排。** 请求 A 发出后换装为 B，A 最后完成必须失效并释放；与“当前目标为空”是不同情况。若当前资源路径完全同步，则先保留为未来适配器契约，不虚构现存 bug。
5. **共享资源与私有实例退出。** 两个玩法实例共用图集/材质，销毁一个不能销毁另一个仍使用的资源；所有 owner 退出后回到基线。对 CPU/GPU 两条渲染路径分别检验。

## 本次不建议做的事

- 不为学习 Latios 或最新官方样例升级 Unity 6、迁移 Entities 或重写存档。若未来出现确切的新需求，另做有界原型，测迁移收益和兼容成本。
- 不把 GameFramework.Entity 的逐对象 MonoBehaviour 或 QFramework 的托管事件总线搬入密集模拟热路径。
- 不把“有池”“有 ECS”“固定 Update”当作固定内存、零分配、确定性或手机性能的证明。
- 不复制 ET10 当前受限许可代码；任何外部实现/资源进入仓库前，先固定版本并核对该文件、依赖和第三方资源的许可。
- 不因为别人的框架有树、网络、热更或复杂编辑器，就给当前单机基座增加同样的基础设施。碰撞网格与直接 AI 的选择继续依据[本项目实测](CollisionBroadphaseBenchmarks.md)。
- 本轮未深入核验 Entitas、LeoECS、其他 Cocos/Godot 框架，因此不对其当前维护、许可或移动性能作结论；它们不是本轮七项的隐含落选性能排名。

## 版本证据与复核方式

以下 GitHub API 在 2026-10-07 查询：仓库元数据用于 archived/default_branch；默认分支 commits 用于精确 sha 与提交日期；releases 用于发布状态。`updated_at` 可由 stars/issues 等非代码活动改变，`pushed_at` 也不等于默认分支最新提交时间。

| 仓库 | 元数据 | 默认分支提交 | 发布状态 |
| --- | --- | --- | --- |
| GameFramework | [API](https://api.github.com/repos/EllanJiang/GameFramework) | [API](https://api.github.com/repos/EllanJiang/GameFramework/commits?per_page=1) | [releases](https://api.github.com/repos/EllanJiang/GameFramework/releases?per_page=1) 为空 |
| UnityGameFramework | [API](https://api.github.com/repos/EllanJiang/UnityGameFramework) | [API](https://api.github.com/repos/EllanJiang/UnityGameFramework/commits?per_page=1) | [releases](https://api.github.com/repos/EllanJiang/UnityGameFramework/releases?per_page=1) 为空 |
| QFramework | [API](https://api.github.com/repos/liangxiegame/QFramework) | [API](https://api.github.com/repos/liangxiegame/QFramework/commits?per_page=1) | [latest](https://api.github.com/repos/liangxiegame/QFramework/releases/latest) v1.0.246-Unity2018Compatible |
| Morpeh | [API](https://api.github.com/repos/scellecs/morpeh) | [API](https://api.github.com/repos/scellecs/morpeh/commits?per_page=1) | [latest](https://api.github.com/repos/scellecs/morpeh/releases/latest) 2024.1.1；开发分支单列于正文 |
| Latios | [API](https://api.github.com/repos/Dreaming381/Latios-Framework) | [API](https://api.github.com/repos/Dreaming381/Latios-Framework/commits?per_page=1) | [latest](https://api.github.com/repos/Dreaming381/Latios-Framework/releases/latest) v0.16.1 |
| ECS Samples | [API](https://api.github.com/repos/Unity-Technologies/EntityComponentSystemSamples) | [API](https://api.github.com/repos/Unity-Technologies/EntityComponentSystemSamples/commits?per_page=1) | [releases](https://api.github.com/repos/Unity-Technologies/EntityComponentSystemSamples/releases?per_page=1) 为空 |
| Megacity Metro | [API](https://api.github.com/repos/Unity-Technologies/megacity-metro) | [API](https://api.github.com/repos/Unity-Technologies/megacity-metro/commits?per_page=1) | [releases](https://api.github.com/repos/Unity-Technologies/megacity-metro/releases?per_page=1) 为空 |
| ET | [API](https://api.github.com/repos/egametang/ET) | [API](https://api.github.com/repos/egametang/ET/commits?per_page=1) | [releases](https://api.github.com/repos/egametang/ET/releases?per_page=1) 为空 |

空 release 列表不代表没有 Git tag。API 链接随时间变化；正文源码、许可与版本说明链接固定到本次读取的提交。根许可证不覆盖一切第三方代码、字体、音频、模型或商业插件，实际采用时应对选定文件与依赖复核。

**证据边界：** 本次完成了源码和文档研究，没有导入外部包、执行其测试、复制运行时代码或改变本项目依赖。文档含 113 个链接，已核对 14 个相对链接和 63 条固定提交文件链接的路径存在性，并检查表格列数及空白格式；没有新增模拟或性能验证结果。后续任何落地都继续遵守 [AGENTS.md](../AGENTS.md)、[能力验收清单](RequestedCapabilityChecklist.md)及当前精确提交的测试/视觉/设备门槛。
