# A1 执行计划诊断

日期：2026-10-07。落实 [Unity 2022 整合方案 §6.1](LatiosUnity2022IntegrationBlueprint.md#61-可解释的执行计划)。本阶段仅增加 SPF 冷路径诊断，不引入 Latios/Entities，不改 Phase → Order → 注册序，不对现有 Job 图做拓扑重排。

## 使用与边界

`session.Pipeline.GetExecutionPlan()` 返回只读模型；`ToText()` 导出文本，`ToDot()` 导出 Graphviz DOT。只在明确请求时构建边和格式化。DOT 的灰色虚线表示调用顺序，实线表示声明推导出的依赖输入，橙色 note 表示框架可见的 Complete 调用点。两种格式都包含系统完整类型、来源模块 ID/类型、Phase/Order/注册序、读写 key。

模型刻意分开三件事：

- 调用顺序：已经排定的系统数组，Phase 变化本身不增加 Complete。
- 依赖输入：R→W、W→R、W→W、上一个 barrier、barrier 等待之前所有已登记句柄。R→R 无数据冲突边。与 `DependencyTracker` 一样保留跨 barrier 的 writer/reader 记录，不把图化简成另一种调度算法。
- Complete 调用点：空声明系统执行之前、启用 SerialProfiling 时各系统返回之后、EndTick 及调度异常恢复。Reset/保存/恢复/Dispose 经 EndTick 完成；失败完成仍保留在途所有权供重试。

边表示框架根据声明传入的符号依赖，**不证明对应 JobHandle 非空、存在实际等待或经过测量**。系统可以返回传入句柄、default 或新任务；SerialProfiling 也会提前完成句柄。每个系统内部 Complete 都明确显示 `unknown`，包括已知源码内会同步的 SV SpawnBullets；这里没有靠声明猜内部同步，也没有宣称覆盖所有等待。运行时修改 SerialProfiling 后，需要重新取计划才能反映该模式。

声明仅在原有构造流程调用一次。`AccessDeclaration` 保存精确 ID→name 关系，报告复制构造时读写集合，不解析显示字符串，也不再次调用 Declare。调用方必须遵守固定管线契约，不在 Declare 返回后继续修改声明；诊断快照不是对这种不受支持行为的验证或安全封装，现有 tracker 仍持有原声明。本阶段没有悄悄冻结/收紧旧入口。

`WorldComposer` 在每次真实 `RegisterSystems` 周围记录当时模块 ID/完整类型。直接构造的旧两参数 `TickPipeline`、手动裸注册表保持 `unknown`；不会从命名空间猜 owner。需要手动来源时可用新增三参数重载传一项对应一个注册项的 `SystemRegistrationSource`，数量不符在 Declare 前拒绝。两参数 API 保留。

## 兼容与实现范围

- `BeginTick`、`ScheduleSystems`、`CompletePendingTick`、`Complete` 和失败句柄恢复路径没有逻辑改动。
- 不改变表/资源/系统 snapshot 布局，默认工厂、RNG 消费和碰撞平局行为不变。
- `SystemEntry.Writable` 是与 A2 约定的冷构造字段，为读写权限检查提供基础；A1 自身不启用新运行时访问限制。
- 旧冷组合探针现在直接读计划中保存的注册序/barrier 元数据，移除第二次 RegisterSystems/Declare。默认 JSON 形状和内容保持原样。可加 `--execution-plans OUTPUT_DIRECTORY` 输出 19 组合的文本与 DOT，同时内部检查重复导出一致及组合来源已知。

## 冷路径与移动端成本

这不是零成本诊断。即使不导出，每个系统也保留一份构造时类型/Phase/Order/注册序/模块来源、只读 key 数组及包装；每份声明另外持有 ID→name 字典。字符串引用复用，不克隆系统/模块实例。附带的 A2 Writable 数组也在构造时分配。稳态 Tick 不调用导出器，不新增格式化、遍历图或字符串创建。

按 N 个系统、K 条声明计，新增长期元数据规模为 O(N+K)，另加原有按最大 key ID 分配的访问数组（本阶段增加同规模 Writable）。`GetExecutionPlan()` 临时构建 writer/reader 字典、边和完成原因；边数最坏 O(N²)，例如许多空声明 barrier。格式化成本与输出长度相关。计划不是每帧 UI 数据源；开发工具应按需取一次、展示后释放。

[冷成本探针](validation/ExecutionPlanColdCostProbe.cs) 使用项目 `ManagedAllocationProbe`，前后保留数组阳性/空操作对照、5 次预热、20 次样本；构造测试不含 World/系统对象创建，也不含 Dispose。单独计未格式化计划创建。数据和原始样本见 [验证清单](validation/ExecutionPlanDiagnostics-20261007.json)。这些是 .NET 8 当前同步线程分配字节，不是 Unity 原生堆、长期驻留内存、峰值或 Android/iOS 表现；没有把分配数当成 GC 次数。

本轮受控数据每系统固定 4 读 + 4 写；1 / 20 / 64 个系统的构造分配中位数分别从基线 1,568 / 19,728 / 61,792 bytes 变为 3,008 / 47,920 / 151,936 bytes，增量为 1,440 / 28,192 / 90,144 bytes。对应一次未格式化计划创建为 1,536 / 11,344 / 24,264 bytes。64 系统新构造窗口有 1 次 Gen0 collection、最高样本 152,224 bytes；没有删去该样本。前后阳性对照均为 33,536 bytes、空操作均为 0。数值包括测试用同名固定 key 拓扑，不代表每种实际组合占用；长期驻留与原生移动分配仍待测。

## 验证与复现

测试范围：`ExecutionPlanTests` 的冲突/读读/屏障/稳定排序、来源与 unknown、write 升级、只读模型、无二次 Declare、导出不完成 pending Tick/改变 raw pipeline snapshot、SerialProfiling/失败完成原因、增删模块、构造元数据隔离、来源数量拒绝、转义/区域设置；同时运行原 Pipeline/AccessGuard/Composition/TickFailureOwnership 回归。

红阶段在原始运行时代码上编译新增测试，4 个缺失执行计划 API 错误；实施后重新生成并构建受影响测试程序集，通过 49/49（15 项新测试、34 项相关回归），0 跳过。初次绿测试曾因一个单系统 DOT 用例错误要求实际调用顺序边而失败；修正测试为在双系统用例检查虚线，在单系统用例检查 SerialProfiling note，最终结果重新执行。

19 组合的 `.txt` / `.dot` 在每个 Session 重复导出一致，再用第二个独立进程全部复跑字节一致；Graphviz 能解析全部 19 个 DOT。原冷组合 JSON SHA-256 仍为 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`。这证明该冷清单相同，不等于所有玩法 raw/hash 和原生 Jobs safety 已验收；完整 aggregate、精确提交原生 EditMode/PlayMode/Burst 与物理移动设备门槛由整合验收另外执行，不引用旧绿灯代替。

基础命令（先设置可写 DOTNET_CLI_HOME / NuGet 缓存并准备项目现有 Mathematics 依赖）：

```sh
python3 Tools/DotnetHarness/generate.py
dotnet build Tools/DotnetHarness/.gen/SPF.Tests.EditMode/SPF.Tests.EditMode.csproj -m:1 -nologo -v q
dotnet test Tools/DotnetHarness/.gen/SPF.Tests.EditMode/SPF.Tests.EditMode.csproj --no-build -m:1 -nologo -v q \
  --filter 'FullyQualifiedName~ExecutionPlanTests|FullyQualifiedName~PipelineTests|FullyQualifiedName~TickFailureOwnershipTests|FullyQualifiedName~CompositionTests|FullyQualifiedName~AccessGuardTests'
```

冷组合探针沿用 [现有 runner 项目构造方法](validation/FoundationCompatibilityProbe-20261007.md)，运行已构建的 `Probe.dll --execution-plans OUTPUT_DIRECTORY`，stdout 仍是原 JSON。成本探针用同样的 net8 console runner 引用 `SPF.Runtime.Core` 与 `SPF.Testing`，只编译 `ExecutionPlanColdCostProbe.cs`，设 `ConcurrentGarbageCollection=false`；在原始基线和本提交各执行一次，保存原始 JSON。未格式化导出测量只在新 API 存在时进行。
