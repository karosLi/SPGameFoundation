# A2：显式列权限与结构变更窗口

2026-10-07。本次独立实现蓝图 §6.2，不引入 Latios/Entities，不改变 Phase → Order → 注册序、依赖图、保存布局或旧工厂。来源基线为 `9fc4e2b7e20a17e5586a68f756e92f8102f99339`；精确受测源文件 SHA-256、命令和原始结果在[证据清单](validation/AccessStructureWindows-20261007/evidence.json)。集成提交还需完整兼容回归与原生 Jobs/Burst safety 验证，下面的 .NET 和 API 编译不替代这些门槛。

## 1. 新入口和兼容边界

SimContext、SimWorld、SimTable 均增加同名入口：

- `ReadColumn<T>(ColumnKey<T>)` 返回 `NativeArray<T>.ReadOnly`。普通 C# API 没有 index setter，不复制数据；Unity 原生 view 保留容器安全信息。
- `WriteColumn<T>(ColumnKey<T>)` 返回可写 `NativeArray<T>`。已声明系统在获取时必须声明 `Write`，`Read` 不足；`Write` 同时允许读取。
- 两者都是完整容量 view，只有 `[0, Count)` 为有效行。容器为借用，不能释放或跨 World 生命周期长期缓存。
- 获取 view 不完成任何 Job。主线程索引之前仍需完成相应依赖；系统调度把收到的依赖传给 Schedule，主线程外部访问使用 Session.Sync。
- 空声明 barrier 在框架成功完成全部前序 Job 后可以读取和写入任何列。OnCreate、Tick 间访问没有正在执行的系统权限声明，因此不施加声明权限限制。

旧 `Column` 保留可写 NativeArray 和“Read 或 Write 声明存在即可获取”的兼容行为。旧 Handles、DeadFlags、Resource、Registry 入口没有变成新的只读协议。资源引用本身仍可变，IJobData 获取检查仍只验证声明存在。此前缓存的 NativeArray、unsafe 指针、直接 Registry 操作、资源内部方法均可能绕过这些新检查。本次不是完整内存安全证明，也不声称 `Read` 声明会追踪随后每次索引写入。

试点仅迁移 Snake MovementSystem 的 Control/Radius 为真实只读 view、其余列为显式 WriteColumn，以及 DriftMoveSystem 的两个写列。Job 算法、列类型/顺序、输入、RNG、容量和保存字节均不改变。

## 2. 窗口依据实际交接所有权，不依据阶段名

每个 World 的 AccessGuard 独立记录 Pipeline 已接收的新返回句柄，成功显式完成后才归还。默认句柄或原样返回传入 dependency 不引入新工作。后者很重要：DependencyTracker 会保留已经由 barrier 完成的旧句柄，不能仅因这些句柄非默认便误判为新的在途工作。

- 返回新非默认句柄后立即登记，早于依赖登记或串行 profiling 可能失败的位置。
- 成功完成 barrier 的全部前序工作后，打开窗口。
- SerialProfiling 成功完成当前新返回工作后归还这份所有权；之前不相关而未确认完成的工作仍保守保留。
- EndTick/Session.Sync 在 unrecorded 与 pending 句柄都成功完成后打开窗口，再进行资源 OnSync。
- Complete 抛错不打开窗口、不清除仍持有的句柄。恢复成功后可以继续编辑；OnSync 通知失败发生在工作已完成之后，不重新借出存储。
- HasPendingTick 本身不是是否有 Job 的证据。完全同步的已声明系统以及已经完成所有工作但还未 EndTick 的 barrier 之后都允许结构修改。
- `JobHandle.IsCompleted` 不是主线程已取回 NativeContainer 所有权的证明，本实现不会凭它开放窗口，也不会在 getter 或结构检查中悄悄 Complete。

当前检查故意采用 **World 级保守窗口**：即使 Job 使用另一张表，也先拒绝该 World 的结构变更；没有增加细粒度 table/registry/resource lease。直接由用户代码调度、尚未从 OnTick 返回的私有 Job 不可见，调用者仍须负责其同步，尤其在 OnTick/OnCreate 抛错时。一个 Session 的唯一 Pipeline 管理自己的 World，不提供多个同时运行 Pipeline 共用一个 World 的所有权协议。

## 3. 操作类别

| 操作 | 为什么需要完成窗口 | 检查位置 |
| --- | --- | --- |
| CreateEntity | 分配 registry 身份、改变 Count、初始化每列 | 所有计数和 registry 改动之前；表 Add 再检查 |
| Spawn / SpawnRange | pooled Count 与列初始化；批量清零 | 容量失败计数、Count 和清零之前 |
| DestroyEntity / PlaybackDestroys | swap-back 改列/身份行映射，队列排序/清理 | registry 解析/释放和队列读写之前 |
| CompactPools | survivor 顺序压缩、移动所有列 | 取得 unsafe 指针、临时容器和 Run 之前 |
| SortRows | 行重排与 registry 行映射 | 排序临时分配、读取 sort keys 及重排之前 |
| ClearLevel | 清表、释放身份、重置资源、LevelVersion | 第一个可见副作用之前 |
| Reset / ReadSnapshot | 替换权威存储及重置资源 | 第一个副作用或 reader 消费之前 |
| World/Table.Dispose | 释放存储与资源 | disposed 标记和任何释放之前 |
| WriteSnapshot | 读取存储，且规范化待销毁队列顺序 | writer 写入及队列变化之前 |

设置 pooled DeadFlags 和往 DestroyQueue writer 入队是 **延迟请求**，不是当场移行；它们仍可在正确声明/依赖保护下从 Job 写入，真实压缩/销毁在下一次 BeginTick 的完成窗口执行。本次不增加通用结构命令队列。

OnCreate、已同步 Tick 之间、任何 Phase 的已完成 barrier 均合法。ApplyCommands/Spawn/Resolve 的名字不能免除窗口检查，也不会新增逐 Phase Complete。内部表原语也检查，避免从内部调用或公开 Table.Dispose 漏过 World 入口。

## 4. 开发检查和成本

`AccessGuard.Enabled` 在 UNITY_EDITOR、DEVELOPMENT_BUILD、SPF_DOTNET_HARNESS 默认 true，普通 release 默认 false。所有权记录仍运行，防止运行中切换 Enabled 丢失状态；本次没有声称检查代码被编译移除。

- 开发检查开启时，`Guard.Throw=false` 仅把列/资源权限违规改为计数。**结构违规仍始终抛错**，绝不在日志模式下继续释放或改表。
- Enabled=false 关闭这些开发检查，并不自动使违反依赖/所有权契约的操作合法。原生 safety 和调用者生命周期责任仍在。
- 每个非 barrier 系统冷构建多一个 Writable bool[]，长度 `access.MaxId + 1`；热路径不建权限集合、不格式化成功路径字符串、不分配 view。

Linux x86_64 / .NET SDK 8.0.425 / Debug harness，5×10,000 次预热；每个保留窗口 10,000 次迭代。每次包括三层入口共 6 次读/写获取、相应索引、已完成句柄副本的所有权登记/归还以及 CreateEntity/DestroyEntity。原生分配、worker 线程与完整 Tick 不在该窗口内。

| 配置 | 当前线程 managed bytes | gen0 | 前/后 retained-array / empty 控制 | 31 个交替配对窗口 p50 / p95 / worst |
| --- | --- | --- | --- | --- |
| Enabled=false | 0 | 0 | 两次均 33536 / 0 | 5.804 / 10.078 / 10.789 ms |
| Enabled=true | 0 | 0 | 两次均 33536 / 0 | 5.860 / 9.804 / 11.144 ms |

这是同一新实现的两个检查配置，不是旧基线与新实现的性能 A/B，也不是 release player 或手机结果。原始 31 个样本保留在 TRX/JSON；环境抖动明显，不据此宣称固定开销、加速或移动帧时预算通过。没有改变任何现有阈值。

## 5. 红测试、验证和待验项

- 修改 runtime 前，行为红测试 **14 失败 / 3 通过 / 17 总计**。为了能在没有新成员的基线编译，测试私有扩展方法把 WriteColumn 暂接旧 Column；缺少只读 API 通过 reflection 断言失败。最终测试已移除 fallback，全部调用真实新成员。用于重放红测试的 [BaselineTests.cs.txt](validation/AccessStructureWindows-20261007/BaselineTests.cs.txt) 放在 Docs，不进入程序集；配合最终 opt-in harness ownership adapter 在基线重放。
- 首轮实现后额外发现“completed barrier 后原样返回旧 dependency”误关窗口，先记录 **1 失败 / 1 总计** 的红反例，再修正新返回工作的判定。
- 最终 SPF 精选 **65/65 通过**：包含 30 个 A2 用例、原有 AccessGuard、TickFailureOwnership、SessionTests。覆盖三层入口 R/W、漏声明、只读 Job 参数、全部结构类别、无副作用拒绝、同步恢复、异常/失败重试、双 World/Session、OnCreate/barrier/serial/无 Job Tick 以及校准分配。
- Snake Movement 试点原有 replay/world 测试 **12/12 通过**；Drift 试点已由上述 SessionTests 覆盖。
- 实际 Unity 2022.3.62f2 的 96 份 engine/package 引用、202 个真实源文件 API 编译 **0 错误**，1 项原有 DriftGizmoView 未赋值字段警告。这只证明真实签名兼容，未执行 Unity/Burst/native worker。

.NET Job stubs 仍 eager 执行。仅本测试 fixture 打开 ThreadStatic 的“已返回未归还”句柄 token 模式，Complete 清 token，Combine 保留存在性；它不模拟 DAG、线程并发、原生安全句柄或原生 Complete 失败。普通 harness 默认行为保持 eager/default；原有失败注入仍明确是 **Complete 之前** 的模拟拒绝。

**未在本独立改动执行：** 19 组合全量回归、精确集成提交 Unity EditMode/Burst/graphics CI、Android/iOS 实机及 release player。它们由统一集成验证处理，不能把本地绿色结果提升为这些门槛已通过。
