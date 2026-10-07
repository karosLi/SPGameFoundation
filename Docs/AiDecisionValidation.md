# AI 选择与队列确定性验证

## 实施检查点

本轮从 `1c9731a` 开始，在独立工作树验证。共享 core 提交 `5283867`；两个策略 pilot 和冻结参考执行器提交 `ec1624c`。队列确定性修复为独立提交 `3735705`；本文件与性能 workload/report 随后单独提交。

- .NET harness 使用真实 Mathematics、Unity API/Job 桩，不能证明 native Burst、图形或移动设备性能。
- 最终代码的 76 个程序集在 .NET harness 构建通过，0 warnings / 0 errors；完整运行 895 个发现用例，893 passed、0 failed、2 个原有显式工具用例未执行（Sling SearchShots、Snake ExportFrames）。原生 graphics PlayMode 用例不由桩环境执行，不能包含在通过数量里。
- 其中 RPG 完整套件 63/63、Brawler 90/90；core 的新增决策/快照用例包含在完整套件中。求值校准窗口为 0 current-thread ManagedBytes，前后 positive/empty 各为 33536/0。原生 Unity 仍须重新验证其实际 GC.Alloc sample 单位。
- 原始工作树阶段的原生验证当时尚未运行；下方新增精确集成提交 `4af9b87` 的实测结果。此前绿色 CI 不覆盖后续修复。
- 自有云机工具链安装已恢复，但现有记录显示 Unity 登录/许可证阻塞；没有为本轮读取凭据或尝试改变登录/设备设置。

## 正确性门槛

- Core：全部事实组合、每个叶子、未知/矛盾掩码、非法 action/节点/边、不可达、最大 32 节点、访问预算和默认失败。三个批量大小的真实 Schedule + Complete 路径比较 action/status/leaf/visits；原生分支以 BurstDiscard probe 要求实际 Burst 执行。
- 无分配：预热后 4096 次求值，用 ManagedAllocationProbe 前后保留数组阳性/空对照；当前线程范围，字节与 allocation sample 不混用。
- RPG：全部 32 事实组合、精确范围/视线/远程退避阈值、技能覆盖普通攻击、同时 retreat+attack、安全 Hold；实际 Tick 与旧 AI 的冻结源码逐字节比较，含恢复。
- Belt：全部事实、x/y 距离、cooldown 边界；实际 Hit/KO/Attack 状态不能重开攻击；旧执行器与新执行器完整 Tick/恢复快照一致。
- RPG projectile：不同调度批次/重复运行、固定 capacity 和溢出接受集合、相同 OwnerId 的 payload/signed-zero tie、旧格式乱序 pending 队列恢复后的稳定生成。原生 probe 验证 gather 编译执行。
- DestroyQueue：不同调度入队顺序、重复写、重复读、reset 后恢复、重复/过时代数/null、后续实体回收/生成一致。快照写前等待全部生产者完成。

冻结参考实现只编译在 test assembly，通过 IGameplayModule 装配替换一个 AI/fighter 系统，保留 module Id、数据布局、注册顺序和所有其他系统。生产代码没有测试用 legacy 开关；有明确用户配置 `UseDecisionTree`，默认 false。专项树用例和 A/B 明确设为 true，普通 bootstrap/回归保留直接策略默认。两侧共用修复后的确定性队列管线，避免把 baseline 已有的队列缺陷当成树的差异。

## 可重复成本实验

- `RpgAiWorkloadTests.CompleteTickLegacyVersusTreeAbba`：32/256 怪、spread/clustered、90 Tick 预热，四个相同快照起点的 120 Tick 窗口，A/B/B/A；保留不同职业/射击/技能、真实感知、导航、碰撞/解析和完成的 jobs。预热逐 Tick 比较全部 actor 关键列与完整快照；每窗口末尾完整快照再次一致。
- `RpgAiWorkloadTests.ScheduledSelectorAndPerceptionCostsAreReportedSeparately`：4096 输入 ×16 次变化求值，每个输出摘要包含全部求值；旧手写分支与平坦树使用相同缓存 facts。另量真实 32×32 tile LOS/距离/条件捕获。12 组交错次序；计入 Schedule/Complete，不能把感知成本从完整 Tick 中随意相减。
- `BwBeltWorkloadTests.CompleteTickFrozenLegacyVersusDecisionTreeAbba`：32/128 fighter、spread/clustered、相同预热、密度和 120 Tick ×4 窗口。保留完整 ordered player/skill/weapon/movement、网格、分离、命中、掉落、feedback 和 sync。
- `BwBeltSeparationCandidateTests`：生产尚未采用的独立并行候选。相同不可变网格、相同访问顺序、逐行输出和候选数完全一致；20 次交错样本 ×30 pass，计入 Complete；不包含 build/copyback/其余 Tick，不能据此承诺整帧收益。

报告文件：`perf-rpg-ai-*`、`perf-belt-ai-abba-*`、`perf-belt-separation-candidate-*`。Unity 写到项目 Artifacts；harness 写到系统临时目录下 spf-artifacts。保留 runtime 标签。时间只报告 mean/p50/p95/worst，不设置任意设备阈值、不隐去更慢结果。

## 已观察结果与限制

2026-10-07 最终 opt-in 代码的 Linux/.NET 8 Debug 桩运行原始报告保存在 [Benchmarks/AiDecisions-dotnet](Benchmarks/AiDecisions-dotnet/)。这是本轮代码的实际结果，不是 native/Burst 或设备预算。

| 完成工作量 | 旧分支 mean / p95 ms | 树 mean / p95 ms |
| --- | ---: | ---: |
| 65,536 次变化战术选择，含 Schedule/Complete 与摘要 | 1.82994 / 2.81547 | 7.69263 / 9.59236 |
| RPG 32 spread 完整 Tick | 0.22870 / 0.40611 | 0.22807 / 0.33965 |
| RPG 32 clustered 完整 Tick | 0.26221 / 0.46766 | 0.26314 / 0.46346 |
| RPG 256 spread 完整 Tick | 3.49678 / 4.23442 | 3.20581 / 3.70004 |
| RPG 256 clustered 完整 Tick | 3.46220 / 3.98148 | 3.48928 / 4.01091 |
| Belt 32 spread 完整 Tick | 0.10728 / 0.14181 | 0.10927 / 0.14978 |
| Belt 32 clustered 完整 Tick | 0.14214 / 0.20310 | 0.14140 / 0.19098 |
| Belt 128 spread 完整 Tick | 0.46909 / 0.55936 | 0.46979 / 0.57450 |
| Belt 128 clustered 完整 Tick | 0.74726 / 1.69258 | 0.75478 / 1.76586 |

独立 tile LOS/距离/条件捕获 workload mean 98.97017 ms，p95 104.26882 ms（同为 65,536 次），不是每 Tick 的额外成本。单独树选择明显慢于原有几条手写分支；真实感知的独立工作量又高得多。Belt 完整 Tick A/B 没有建立稳定速度优势。这里交付的是可复用/有界/可检查的策略，以及修复的确定性契约，**不宣称树带来性能加速**。因此最后采用直接策略默认、树显式 opt-in；队列确定性修复独立启用。最终数值与通过总数以本提交之后的原始测试报告为准；原生 Burst 和设备数据未运行的部分不能填成通过。

现有九个旧玩法及新 mobile 变体仍须完整 harness/原生回归。没有修改原来的 GC/图形/物理门槛，没有减掉队列字节来让快照比较通过。物理 Android/iOS 的触摸、帧时、native memory、发热和电量仍是外部门槛。


## 复核命令和合并门槛

1. `Tools/DotnetHarness/run.sh`：全部分层构建和逻辑用例；本轮运行保存 `ai-optin-results.trx`。完整日志/报告在工作树的 `Artifacts/ai-optin-build.log`、`Artifacts/ai-optin-tests.log`、`Artifacts/ai-evidence/`。
2. `Tools/ci/local-unity-tests.sh`：由已授权且可用的原生 Unity 环境运行，保留 EditMode、graphics PlayMode 与 Burst backend 实际结果。不要从本地 .NET 数字推断 native 结果。
3. 检查原生的 `ScheduledBurstMatchesSynchronousAndChecksActualBackend`、`ScheduledPermutationsHaveIdenticalAcceptedRequestsAndOverflow` 与 belt candidate probe，不能将 managed fallback 标记为 Burst。
4. 原生 A/B 若显示不利成本，保留原始样本并评估是否值得采用解释器；不能只发布“AI 已优化”。并行 separation 候选仍未进入生产，需原生且完整 Tick 的新增证据才有采用理由。

## 原生集成结果：4af9b87

[原生运行 37574715913](https://github.com/karosLi/SPGameFoundation/actions/runs/37574715913)，Unity 2022.3.62f2 / Apple M5 Pro，精确 tree `c0173dec0f2cbda34c5734b496d0915f5bd38d1e`。[原始报告与逐文件哈希](Benchmarks/Native-4af9b87/source.json)。完整 EditMode 为 1,012 passed / 2 failed / 5 skipped，graphics PlayMode 为 148 passed / 1 explicit diagnostic skip。下列通过项不掩盖 dense separation candidate 的两项失败。

65,536 次变化选择、12 个交错样本、含 Schedule/Complete：直接策略 mean / p50 / p95 为 **0.11871 / 0.11920 / 0.12160 ms**；树为 **0.50258 / 0.49990 / 0.52560 ms**。树中位成本约为直接分支的 **4.19 倍**，因此继续保持直接策略默认。独立 LOS/距离/感知为 1.30186 / 1.26860 / 1.47260 ms，不能从完整 Tick 随意减去。样本数 12 时这里 p95 等于最大样本。全局 Burst 已实证，KernelJob 请求同步 Burst 编译，但此定时报告没有逐 kernel sentinel，不能把全局开关写成每个 kernel 的独立后端测量。

完整 Tick ABBA：90 tick 预热，各自从同一演化后快照恢复，4×120 tick 窗口；每侧 240 个计时 tick，输入与决策频率相同。8 个场景的完整窗口快照均逐字节一致。下表单位 ms：

| Game / population / clustered | Direct p50 / p95 | Tree p50 / p95 | Direct worst / Tree worst |
|---|---:|---:|---:|
| RPG / 256 / False | 0.13460 / 0.16810 | 0.13870 / 0.17160 | 0.28360 / 0.54730 |
| RPG / 256 / True | 0.14450 / 0.17550 | 0.14490 / 0.17110 | 0.23540 / 0.22400 |
| RPG / 32 / False | 0.04850 / 0.08920 | 0.04870 / 0.08230 | 0.11840 / 0.14460 |
| RPG / 32 / True | 0.04860 / 0.09140 | 0.04780 / 0.09740 | 0.58820 / 0.11800 |
| Belt / 128 / False | 0.43590 / 0.48970 | 0.45280 / 0.50860 | 0.51260 / 0.52520 |
| Belt / 128 / True | 0.55950 / 1.79360 | 0.57140 / 1.78400 | 4.55780 / 4.55470 |
| Belt / 32 / False | 0.09390 / 0.11100 | 0.09840 / 0.11520 | 0.11830 / 0.13520 |
| Belt / 32 / True | 0.12330 / 0.17540 | 0.12760 / 0.17690 | 0.34660 / 0.35390 |



Belt 树的 p50 在四个场景中慢 2.13–4.79%；RPG 中位数变化为 -1.65% 到 +3.05%，尾部结果混合。不能把单个 outlier 的变化宣传为稳定收益。RPG 包含原感知、导航、碰撞、动作、生命周期和已完成 jobs；Belt 包含原 ordered 逻辑、网格、分离、命中、掉落、同步。两者都不包含渲染、GPU 或设备发热/电量。

**独立 separation 候选仍不得启用。** spread 32 / 128 的 ordered p50 为 .05644 / .19818 ms，scheduled+Complete 为 .01434 / .01322 ms，但两个 dense 用例在精确浮点比较处失败，因而没有对应 timing 报告。这个候选比较还混合了主线程与原生编译/调度因素，排除了 build/copyback/其余 Tick。后续 source `5193305` 显式请求 Strict/High 精度，保留原精确相等断言和密集场景；本地通过不能替代新的原生结果，也尚不能确定具体指令根因。只有先修复精确语义，再做完整 Tick A/B，才有采用依据。

### 精确分离候选的算术定位（2026-10-07，原生复验待完成）

后续 `677611a` 的 Strict/High 仍出现原两项 dense 精度失败。现已用 Unity 自带 Mono 的 float32 / extended scalar 两种求值模式逐位复现两列结果，新增 test-only 显式舍入候选、独立未改生产 reference、原生逐贡献/累加/clamp 原始位轨迹与对抗用例；200,000 pair 和 160 dense row 在两种 managed 模式均匹配。完整原始精确断言保留，生产仍不采用候选。详见[诊断、指纹、复现命令和验收边界](SeparationArithmeticParity.md)；本地通过不代替这次补丁的真实 Burst 复验。

最新原生闭环：`e86ee87` 已通过1,084 EditMode、154 graphics PlayMode 和1,050 .NET 用例，两项 dense 精度失败均解决。Mac 实际选择 ExtendedScalar，候选 backend=1；原始精确断言、逐行/逐贡献比对均保留。生产 ordered 路径与直接 AI 默认不变，未把实验候选投入生产。详见[精度原因与修复](SeparationArithmeticParity.md)及[完整闭环](MobileFoundationFollowupValidation.md)。
