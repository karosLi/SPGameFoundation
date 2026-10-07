# AI 选择与队列确定性验证

## 实施检查点

本轮从 `1c9731a` 开始，在独立工作树验证。共享 core 提交 `5283867`；两个策略 pilot 和冻结参考执行器提交 `ec1624c`。队列确定性修复为独立提交 `3735705`；本文件与性能 workload/report 随后单独提交。

- .NET harness 使用真实 Mathematics、Unity API/Job 桩，不能证明 native Burst、图形或移动设备性能。
- 最终代码的 76 个程序集在 .NET harness 构建通过，0 warnings / 0 errors；完整运行 893 个发现用例，891 passed、0 failed、2 个原有显式工具用例未执行（Sling SearchShots、Snake ExportFrames）。原生 graphics PlayMode 用例不由桩环境执行，不能包含在通过数量里。
- 其中 RPG 完整套件 62/62、Brawler 89/89；core 的新增决策/快照用例包含在完整套件中。求值校准窗口为 0 current-thread ManagedBytes，前后 positive/empty 各为 33536/0。原生 Unity 仍须重新验证其实际 GC.Alloc sample 单位。
- 原生 Unity/Mac 精确最终提交验证尚待集中运行。之前的绿色 CI 不覆盖本轮。
- 自有云机工具链安装已恢复，但现有记录显示 Unity 登录/许可证阻塞；没有为本轮读取凭据或尝试改变登录/设备设置。

## 正确性门槛

- Core：全部事实组合、每个叶子、未知/矛盾掩码、非法 action/节点/边、不可达、最大 32 节点、访问预算和默认失败。三个批量大小的真实 Schedule + Complete 路径比较 action/status/leaf/visits；原生分支以 BurstDiscard probe 要求实际 Burst 执行。
- 无分配：预热后 4096 次求值，用 ManagedAllocationProbe 前后保留数组阳性/空对照；当前线程范围，字节与 allocation sample 不混用。
- RPG：全部 32 事实组合、精确范围/视线/远程退避阈值、技能覆盖普通攻击、同时 retreat+attack、安全 Hold；实际 Tick 与旧 AI 的冻结源码逐字节比较，含恢复。
- Belt：全部事实、x/y 距离、cooldown 边界；实际 Hit/KO/Attack 状态不能重开攻击；旧执行器与新执行器完整 Tick/恢复快照一致。
- RPG projectile：不同调度批次/重复运行、固定 capacity 和溢出接受集合、相同 OwnerId 的 payload/signed-zero tie、旧格式乱序 pending 队列恢复后的稳定生成。原生 probe 验证 gather 编译执行。
- DestroyQueue：不同调度入队顺序、重复写、重复读、reset 后恢复、重复/过时代数/null、后续实体回收/生成一致。快照写前等待全部生产者完成。

冻结参考实现只编译在 test assembly，通过 IGameplayModule 装配替换一个 AI/fighter 系统，保留 module Id、数据布局、注册顺序和所有其他系统。生产代码没有测试用 legacy 开关。两侧共用修复后的确定性队列管线，避免把 baseline 已有的队列缺陷当成树的差异。

## 可重复成本实验

- `RpgAiWorkloadTests.CompleteTickLegacyVersusTreeAbba`：32/256 怪、spread/clustered、90 Tick 预热，四个相同快照起点的 120 Tick 窗口，A/B/B/A；保留不同职业/射击/技能、真实感知、导航、碰撞/解析和完成的 jobs。预热逐 Tick 比较全部 actor 关键列与完整快照；每窗口末尾完整快照再次一致。
- `RpgAiWorkloadTests.ScheduledSelectorAndPerceptionCostsAreReportedSeparately`：4096 输入 ×16 次变化求值，每个输出摘要包含全部求值；旧手写分支与平坦树使用相同缓存 facts。另量真实 32×32 tile LOS/距离/条件捕获。12 组交错次序；计入 Schedule/Complete，不能把感知成本从完整 Tick 中随意相减。
- `BwBeltWorkloadTests.CompleteTickFrozenLegacyVersusDecisionTreeAbba`：32/128 fighter、spread/clustered、相同预热、密度和 120 Tick ×4 窗口。保留完整 ordered player/skill/weapon/movement、网格、分离、命中、掉落、feedback 和 sync。
- `BwBeltSeparationCandidateTests`：生产尚未采用的独立并行候选。相同不可变网格、相同访问顺序、逐行输出和候选数完全一致；20 次交错样本 ×30 pass，计入 Complete；不包含 build/copyback/其余 Tick，不能据此承诺整帧收益。

报告文件：`perf-rpg-ai-*`、`perf-belt-ai-abba-*`、`perf-belt-separation-candidate-*`。Unity 写到项目 Artifacts；harness 写到系统临时目录下 spf-artifacts。保留 runtime 标签。时间只报告 mean/p50/p95/worst，不设置任意设备阈值、不隐去更慢结果。

## 已观察结果与限制

2026-10-07 的 Linux/.NET 8 Debug 桩运行原始报告保存在 [Benchmarks/AiDecisions-dotnet](Benchmarks/AiDecisions-dotnet/)。这是本轮代码的实际结果，不是 native/Burst 或设备预算。

| 完成工作量 | 旧分支 mean / p95 ms | 树 mean / p95 ms |
| --- | ---: | ---: |
| 65,536 次变化战术选择，含 Schedule/Complete 与摘要 | 1.80087 / 1.99927 | 7.93756 / 9.93550 |
| RPG 32 spread 完整 Tick | 0.22839 / 0.32158 | 0.24950 / 0.36538 |
| RPG 32 clustered 完整 Tick | 0.26523 / 0.46546 | 0.27189 / 0.45718 |
| RPG 256 spread 完整 Tick | 3.08886 / 3.48671 | 3.18068 / 3.67861 |
| RPG 256 clustered 完整 Tick | 3.44777 / 3.96577 | 3.40838 / 3.97935 |
| Belt 32 spread 完整 Tick | 0.09215 / 0.12702 | 0.09446 / 0.12058 |
| Belt 32 clustered 完整 Tick | 0.12759 / 0.18529 | 0.12951 / 0.17173 |
| Belt 128 spread 完整 Tick | 0.45012 / 0.57427 | 0.45627 / 0.55130 |
| Belt 128 clustered 完整 Tick | 0.72086 / 1.73191 | 0.74194 / 1.68468 |

独立 tile LOS/距离/条件捕获 workload mean 98.22599 ms，p95 105.65354 ms（同为 65,536 次），不是每 Tick 的额外成本。单独树选择明显慢于原有几条手写分支；真实感知的独立工作量又高得多。Belt 完整 Tick A/B 没有建立稳定速度优势。这里交付的是可复用/有界/可检查的策略，以及修复的确定性契约，**不宣称树带来性能加速**。最终数值与通过总数以本提交之后的原始测试报告为准；原生 Burst 和设备数据未运行的部分不能填成通过。

现有九个旧玩法及新 mobile 变体仍须完整 harness/原生回归。没有修改原来的 GC/图形/物理门槛，没有减掉队列字节来让快照比较通过。物理 Android/iOS 的触摸、帧时、native memory、发热和电量仍是外部门槛。


## 复核命令和合并门槛

1. `Tools/DotnetHarness/run.sh`：全部分层构建和逻辑用例；本轮运行保存 `ai-final-results.trx`。完整日志/报告在工作树的 `Artifacts/ai-all-build.log`、`Artifacts/ai-all-tests.log`、`Artifacts/ai-evidence/`。
2. `Tools/ci/local-unity-tests.sh`：由已授权且可用的原生 Unity 环境运行，保留 EditMode、graphics PlayMode 与 Burst backend 实际结果。不要从本地 .NET 数字推断 native 结果。
3. 检查原生的 `ScheduledBurstMatchesSynchronousAndChecksActualBackend`、`ScheduledPermutationsHaveIdenticalAcceptedRequestsAndOverflow` 与 belt candidate probe，不能将 managed fallback 标记为 Burst。
4. 原生 A/B 若显示不利成本，保留原始样本并评估是否值得采用解释器；不能只发布“AI 已优化”。并行 separation 候选仍未进入生产，需原生且完整 Tick 的新增证据才有采用理由。
