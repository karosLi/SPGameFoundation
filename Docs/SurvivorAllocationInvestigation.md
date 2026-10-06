# Survivor 分配与回收调查（2026-10-06）

历史基线为 `97a2b34`；后续实际可见数字 HUD 的复测源码为 `3ac28cc`。本轮没有放宽任何原有 GC 预算，没有为了通过测量关闭正常玩法的 HUD、音效反馈、机器人或升级选择，也没有把分配字节数叫作垃圾回收次数。

## 后续可见 HUD 校验（重要范围修正）

后续两玩法截图检查发现，共享 `BufferText` 原本缺少 `CanvasRenderer`。因此本文下述 97a2b34 捕获虽然启用了 HUD 脚本和正常升级 UI，**数字 HUD 的网格当时没有实际呈现**；不能把这些数据称为完整可见数字 HUD 的成本。直接调用网格重建的 ABBA probe 也不能替代 Canvas 实际呈现。

玩法阶段已通过 `[RequireComponent(typeof(CanvasRenderer))]` 修复，并新增组件回归、真实截图字形检查。诊断现在在捕获前强制验证 CanvasRenderer 与非空 glyph mesh，随后重新执行同样六窗口。以下首先列新结果，旧数据保留为范围明确的历史基线。

## 最新：可见数字 HUD 的六窗口复测

2026-10-06 16:56:18–16:57:59 UTC，Explicit 诊断测试 1/1 通过。每段捕获前均验证 `bufferTextRenderer=True`、124 个 HUD glyph mesh 顶点；同一修复已有真实截图字形像素断言。自动游玩、正常 HUD/反馈与原始生命值保留。

| 窗口 | 唯一匹配帧 | 分配样本 / 字节 | gen0 完成回收增量 |
| --- | --- | --- | --- |
| GpuDriven 1 | 600/600 | 616 / 52,328 B | 0 |
| GpuDriven 2 | 600/600 | 603 / 24,244 B | 0 |
| GpuDriven 3 | 600/600 | 603 / 24,244 B | 0 |
| DataTexture 1 | 600/600 | 603 / 24,244 B | 0 |
| DataTexture 2 | 600/600 | 603 / 24,244 B | 0 |
| DataTexture 3 | 600/600 | 603 / 24,244 B | 0 |

3,600 帧全部唯一匹配，无无效 metadata、无丢失/歧义/碰撞，观察者 marker 作用域分配 0 B，没有缺失调用栈样本。每段约 10 秒，级别均 1→2，598 个 Playing stamp 和 2 个 LevelUp stamp。这里是有限正常游玩窗口，并非所有武器/升级/菜单路径或长时存活测试。独立 CSV 审计合计 60.0282 秒、173,548 B、3,631 样本，逐帧、逐栈和 summary 均一致。

五段预热窗口的 603 个样本归因：

- 600 ×40 B =24,000 B：栈明确为 Unity Test Runner `LogScope.EvaluateLogScope` / `CheckFailingLogs`，每帧测试框架成本。
- 2 个样本合计 204 B：`SvHud.Update → Text.OnEnable → FontUpdateTracker.TrackText`，正常升级 UI 的字体注册。
- 1 个样本 40 B：`EventSystem.RaycastAll → BaseRaycaster.rootRaycaster`。

首段比其余多 28,084 B：其中 27,904 B /8 样本位于 `CanvasUpdate.PreRender`，内部地址未完整解析；52 B 为 `SpriteFont.DrawNumber` 的 Mono.JIT；其余 128 B 位于 EventSystem/JIT/ArraySortHelper 初始化路径。现象符合首次使用成本，但不能把未解析地址全部判为引擎内部、业务代码或缓冲扩容。首段这 27,904 B 位于 ordinal 414、Playing、tickStart 397、游戏约 13.23 秒，晚于本段升级事件；不能称其为已证明的升级成本。原始地址保留在 CSV。`unresolvedAddresses` 字段统计地址出现次数而非不同地址数：预热窗口 614 次/11 个唯一地址，首段 756 次/47 个唯一地址；也不是未归因分配的样本数。

本次选定 PlayerLoop/all-recorded-thread 样本中，除了有明确栈的每帧测试框架分配，未发现持续高频业务托管分配；没有观察到完成的 gen0 回收增长。这不表示所有分配为 0，也不测量增量 GC 切片/暂停时长。DataTexture 第 1 与第 2 窗口之间，绝对 gen0 计数由 173 增至 174：零增量仅适用于所捕获的六段，不能扩为整个诊断进程。区间外包含捕获导出/清理/重新创建工作，该次回收未做调用栈关联。Profiler callstack 记录会增加观测成本，不能用本次帧时宣称生产性能。

新的 24,244 B 与旧 24,864 B **不是已证明的优化收益**：可见呈现与 UI/升级路径不同，尚未做逐事件同轨迹的 A/B。全局 previous-frame counter 在本次 profiler 下仍出现 122/280 B 及其他值；它的采样边界不同，旧 117/164 B 来源依旧未完整解释。

证据目录：`Artifacts/GC/stage1-visible-hud-20261006T165618272Z`，含六组 frames / allocations / stacks CSV、summary、settings 和 raw 捕获。恢复包选入一段代表性 raw，其余 raw 保留在本地。两档完整新玩法回归另见 [MobileGameplayCheckpoint](MobileGameplayCheckpoint.md)。

## 历史基线：结论与边界（数字 HUD 未呈现）

在六段保留完整自动游玩的窗口中，共观察 3,600 帧、约 60 秒：

- `GC.CollectionCount(0)` 增量每段均为 **0**。Unity 此环境的 `GC.MaxGeneration` 是 0。这里是进程级完成回收次数的观测，不是暂停耗时或增量 GC 切片的测量。
- 每段 600 帧均通过自定义 profiler frame metadata 一一对应，没有丢帧、重复映射或观察者作用域内分配。
- 五段充分预热后的窗口均为 **24,864 B / 610 次 GC.Alloc**。其中 **24,000 B = 600 × 40 B** 的每帧分配，调用栈明确指向 Unity Test Runner 的 `LogScope.EvaluateLogScope` / `UnityLogCheckDelegatingCommand.CheckFailingLogs`。
- 剩余 **864 B / 10 次**事件位于升级面板的 UGUI 字体注册/注销，以及一次 EventSystem 根 Raycaster 查询。不是每帧业务分配。
- 首个 GPU 窗口为 **32,756 B**，比后五段多 7,892 B；其中 52 B 明确在 `SpriteFont.DrawNumber` 的 Mono.JIT 路径，多数其余额外样本位于 Canvas.PreRender，部分地址未解析。它们与首次使用有关的解释相符，但未全部归因，不能直接判定为持续业务泄漏。

因此，上述不包含实际数字 HUD 呈现的历史测试范围内没有发现正常模拟、碰撞、渲染循环的高频业务托管分配，也没有观察到完成的 GC 回收计数增长。不能由此推导整款游戏、所有交互、长时间运行或手机真机都为 0 GC。

## 原始窗口

| Tier / 重复 | 时间（秒） | 映射帧数 | 全部采样分配 | gen0 回收增量 |
| --- | --- | --- | --- | --- |
| GPU 1 | 10.0087 | 600/600 | 32,756 B | 0 |
| GPU 2 | 10.0000 | 600/600 | 24,864 B | 0 |
| GPU 3 | 10.0001 | 600/600 | 24,864 B | 0 |
| DataTexture 1 | 9.9999 | 600/600 | 24,864 B | 0 |
| DataTexture 2 | 10.0002 | 600/600 | 24,864 B | 0 |
| DataTexture 3 | 10.0000 | 600/600 | 24,864 B | 0 |

每段重新创建 seed=3 的游戏，SpawnPerSecond=8，其余默认玩法规则不变，正常自动选择升级。暖机至少 6 秒，再暖机观察路径 8 帧，之后采集 600 帧。记录真实 Tick/时间/Flow/Level，未宣称各次墙钟调度逐帧完全相同。

充分预热窗口中的非 Test Runner 栈：

- `SvHud.Update → GameObject.Activate → Text.OnEnable → FontUpdateTracker.TrackText`，合计 696 B
- `SvHud.Update → GameObject.Deactivate → Text.OnDisable → FontUpdateTracker.UntrackText`，128 B
- `EventSystem.Update → RaycastAll → BaseRaycaster.rootRaycaster`，40 B

这些是具体调用栈/采样 ancestry 的归因；仍未解析的地址原样保留，不笼统归为“引擎分配”。首次窗口含额外 PreRender 样本，不属于上述稳定重复值；未把其未解析的内部地址全部称为容量增长。

## 117 B / 164 B 的限制

旧测试读取的是全局 `GC Allocated In Frame` counter，然后用界面邻近帧作排除。它与本轮选取的 PlayerLoop 样本范围并不等价；开着 profiler 时，该 counter 还出现了 122/280 B 等值，不能根据大小相近就说是同一个来源。

为了直接覆盖 EditorLoop，追加了 600 帧 / 256 MiB 与 120 帧 / 64 MiB 两次采集，两次 Editor 都以 **137** 退出，未产出有效窗口。日志与当前运行环境没有足够证据确定原因，**没有把它写成 OOM**。本轮没有完成旧 117/164 B 全局计数的全部来源解释。

后续应在独立 Development Player / 真机捕获，并记录增量 GC 标记和暂停时间。不能凭关掉某个子系统之后消失，就断言它来自音效、UI或编辑器。

## BufferText 排除实验

一个独立的真实 Unity EditMode probe，用预热后的同一字体、缓冲和 VertexHelper 进行单行/多行 ABBA 测量：每段 1,000 次改变数字并重建网格，另测 1,000 次单独换行字形请求。

五个窗口均为当前线程托管分配 0 B、字体纹理重建回调 0 次、gen0 回收增量 0。换行字形查询仍返回 false，因此可以确认该调用多余，但不能把它认定为之前的 GC 来源。本轮没有据此编造一个“修复 GC”的代码改动。这个直接调用实验不覆盖延迟 Canvas 工作、其他线程或 native 内存。

## 基座与测试改动

- `FrameGovernor.GcCollectionsSinceReset`：单独报告进程级 gen0 回收增量；`GcFramesSinceReset` 仍仅表示发生分配的采样帧。
- `ResetGcStats` 同时重新设置回收计数基线。
- `PerfHud` 把 “GC peak/frame” 改为 “Alloc peak/frame”，另列 Collections；该开发调试面板原有的 4 Hz 字符串分配没有被伪装成零分配。
- `GcReport` 可附带独立回收计数；修正 Survivor 旧测试中关于机器人、音效和 UI 来源的过度推断注释。
- 新增两个 Explicit 诊断入口，不混入每次 CI 的常规性能预算，也不替代旧断言。

## 运行

Unity 2022.3.62f2，Linux x86_64，Burst 1.8.27，OpenGLCore / Mesa llvmpipe。音频设备初始化失败，FMOD 使用 nosound 输出；反馈处理保持启用，但物理设备音频路径未验证。不是 Android/iOS 性能结论。采集打开 allocation callstacks，关闭 deep profiling；符号解析、CSV生成、汇总全部在停止采集后进行。

- PlayMode filter：`SurvivorFoundation.Tests.PlayMode.SvAllocationCaptureTests.NormalGameplayAllocationCallstacks`
- EditMode filter：`SPF.Tests.EditMode.BufferTextAllocationProbeTests.WarmSinglelineAndMultilineMeshes_ReportAllocations`
- 环境变量：`SPF_GC_LABEL`；可选 `SPF_GC_REPEATS=1..3`、`SPF_GC_FRAMES=60..600`、`SPF_GC_MEMORY_MIB=64..256`。默认两档各三段、600帧。
- `SPF_GC_INCLUDE_EDITOR=1` 是本环境中两次被终止的额外诊断，默认不启用。
- 正常退出会恢复 profiler 设置。若进程被外部杀死，finally 无法执行，可显式调用 `SvAllocationCaptureTests.DisableProfilerForBatchValidation` 关闭记录再做常规回归；这不是声称能还原所有已丢失的用户设置。

该基线完成后增加了“同一Profiler帧含多个不同ordinal”的拒绝保护；基线CSV已独立检查没有这种情况，但未人工构造该拒绝分支的测试。

CSV 保留完整 frame metadata、实际间隔、采样线程、分配字节、符号和未解析地址。回收计数的边界是每次 coroutine 相同阶段，Profiler bytes 是完整 stamped frame，两种边界在文件中分开标注。

## 回归与保留记录

- 完整 Unity EditMode 最终重跑：324通过、0失败、3个Explicit跳过。
- 受影响 Survivor PlayMode：5/5通过，现有预算不变。
- 在额外 profiler 捕获中断后，首次完整 EditMode 曾有一个未修改的塔防零分配测试失败。未修改该测试/阈值；显式关闭诊断记录后完整重跑通过。未证明首次失败的具体原因，失败XML保留作为证据。
- .NET harness：320通过、0失败、2个Explicit未执行；构建0警告/0错误。两个真实Unity诊断入口不伪装成桩环境测试。

参考API：[RawFrameDataView](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Profiling.RawFrameDataView.html)、[allocation callstacks](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Profiling.Profiler-enableAllocationCallstacks.html)。
