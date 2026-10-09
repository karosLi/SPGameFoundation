# Roadmap：统一待办与验收门槛

更新：2026-10-09 UTC。范围：当前 `dot/continuous-blade-slash` 的第一方源码、README、Docs、现有用户要求，以及下文指明的已发布 Latios / GC 分支。本页统一跟踪已知 TODO；原始设计、失败证据和阶段记录保留，不把历史 pending 自动解释为当前缺失，也不授权实施所有可选方案。

状态：**待推进**＝尚未开始/恢复；**待验收**＝实现已有、证据或用户接受未齐；**条件项**＝需求触发后才立项；**已完成**＝仅在所述范围闭环。优先级 P0 为数据/证据可靠性，P1 为当前产品质量与下一阶段门槛，P2 为发布准备，P3 为条件性扩展。没有运行证据时不得写“正在运行”。

## 现在完成了什么、正在做什么

- 本次正在做的是统一路线图及待办核对，不是新运行时代码开发。下列 CI 已结束；没有因为仍有 TODO 就把它们标为运行中。
- **连续大幅刀斩自动化里程碑已完成**：运行时代码 `3f9e6f75d1e91b94782112b19bbdc675a8749e27`，最终完整验收提交 `7a1ac69f3409501b81425acc1048435baf8a697b`。二者 Assets 源码相同，后者补文档、移除临时窄过滤并修复旧 macOS Bash 的空数组展开。
  - [完整 Unity 原生运行 37882531425](https://github.com/karosLi/SPGameFoundation/actions/runs/37882531425)：EditMode 1,742 通过、0 失败、5 跳过；PlayMode 173 通过、0 失败、3 跳过。跳过为显式诊断/可选录制及无图形 EditMode GPU 检查，不能计作通过。
  - [完整 harness 37882531444](https://github.com/karosLi/SPGameFoundation/actions/runs/37882531444)：1,702 执行并通过、0 失败；TRX 另有 6 个 NotExecuted 诊断/调参条目。不是原生 Unity 或手机证据。
  - 真实采集时间编码的 GPU / DataTexture 1× 片段已交付；逐帧检查支持抬肘、完整手臂和稳定握持。**用户尚未接受当前自然度**；不能用绿灯代替接受。详见 [动作记录](BladeContinuousAuthoredArc.md)。
- **GC 诊断分支发布已完成**：`diagnostic/shooter-native-allocation-20261008` 的 `3382fd4a98ffb3994d32f61a966db26ec532984d`。这只完成库存检查修正和发布，不代表 41 B 问题已定位或修好。
- **Latios 已完成一个打补丁 Local 包的 Editor 实验**，不是完成 S1a 或产品集成，见 L1。

## P0：可靠性与证据

| ID / 状态 | 待办与下一步 | 完成标准 / 阻塞 | 来源 |
| --- | --- | --- | --- |
| G1 / 待推进 | 修复/验证 Shooter 平台对照的帧计数器校准，先建立原生样本世代、读值阶段和实际帧归属，再调查间歇 41 B | 空/保留数组阳性对照都有效，阶段/帧映射唯一；原窗口、阈值、Profiler OFF 和资源上限不变。无效计数器不得当零分配；不要扩大成无边界采样 | [诊断分支说明](https://github.com/karosLi/SPGameFoundation/blob/3382fd4a98ffb3994d32f61a966db26ec532984d/Docs/ShooterAllocationPlatformControl.md)、[原生运行 37878866320](https://github.com/karosLi/SPGameFoundation/actions/runs/37878866320) |
| P1 / 待推进 | 核对 ProfileStore 的磁盘持久化承诺，补故障注入、替换中断与损坏/超大输入覆盖 | 现注释称原子替换，但源码先 Delete 再 Move，存在失去旧槽的窗口；先复现并明确平台安全替换策略。Load 还需独立 payload 上限/剩余长度约束评估；不得宣称已复现线上丢档 | [ProfileStore.cs](../Assets/SinglePlayerFoundation/Runtime/Persistence/ProfileStore.cs)、[品类覆盖与持久化](GenreCoverage.md) |
| E1 / 持续维护 | 精确提交、原始失败、有效分配控制、原生/桩/设备证据分层；新改动后跑对应回归 | 保持 SoA / Burst、CPU/GPU 等价、权威命中/存档/旧玩法语义和原阈值；不能以静态 pose、编译或无效 0B 替代真实验证 | [AGENTS](../AGENTS.md)、[证据恢复](CiEvidence.md)、[分配口径更正](AllocationMeasurementCalibration.md) |

G1 最新事实：3382fd4 的 A/B 各有 204 条观察、180 帧窗口，observationsValid=true，但 calibrationValid=false。A 的阳性对照候选数为 3/3/2，存在歧义和载荷不足；B 三次保留阳性载荷已发出，24 次校准读数却全部为零。SourceFrame 是观察协程时间戳，不是已证实的原生样本帧；direct/cache 同一 handle 的重复读取不能独立证明新鲜度。因此未见 41 B 不能排除任何来源。[同提交 harness](https://github.com/karosLi/SPGameFoundation/actions/runs/37878866335) 的 1,690 通过也不能补足原生校准。后续可评估仅在现有 24 次校准 yield 中预分配 CurrentValue/LastValue 阶段采样，但这只是提案，尚未实施。

7a1ac69 的普通 Shooter 两后端各 180 帧记录全部为 0B，是单次有效产品测试未复现；不能抹去历史失败或替代 G1 的校准调查。

## P1：人物、怪物、技能和战斗表现持续优化

这是持续产品质量项，不是已通过一次测试就永久关闭的功能。每轮都保留“角色 × 状态 × 武器 × 技能 × 后端”覆盖清单，只对实际支持状态验收，不凭表现虚构玩法。

| ID / 状态 | 待办与下一步 | 完成标准 / 阻塞 | 来源 |
| --- | --- | --- | --- |
| A1 / 待用户视觉验收 | 当前连续大幅刀斩继续按实际 1× 录像反馈调整 | 保留已接受剑刺；刀斩不是通用抬手姿势。肩、上臂、肘、腕、躯干及重心共同运动，有加速/减速、随挥和逐渐收势；不以缩小弧线/放慢录像换绿灯 | [当前动作证据](BladeContinuousAuthoredArc.md)、[武器表现](WeaponMotionPresentation.md) |
| A2 / 持续优化 | 英雄、敏捷怪、重型怪的 Idle / Walk / Run、起停、转向、受击、死亡、恢复和各自技能 | 步态支撑与重心自然、角色节奏可辨；肩肘躯干协作。每个技能有独立前摇、接触/释放、随动、恢复；不是一套循环换颜色 | [自然角色](GameplayNaturalCharacters.md)、[按品类接入](AnimationIntegrationByGenre.md)、[步态](GroundedLocomotionValidation.md) |
| A3 / 持续优化 | 移动攻击、连续按住/缓冲连击、取消、打断、受击、换装、暂停恢复及重建的交接 | 从当前最终姿态/速度衔接，不在相位边界重置目标；继续混合实时移动，不压掉步态；30/60/120 Hz 与不规则步长覆盖、保持命中时钟 | [移动攻击](BeltAttackMobility.md)、[连续动作](BladeContinuousAuthoredArc.md)、[权威武器](AuthoritativeWeapons.md) |
| A4 / 持续验收 | 固定手柄/手掌 socket、主副手语义与有限 IK 修正 | 握持不漂移、不抖；刀接触后不意外下坠；肘提示稳定，IK 不把已设计的抬肘压回肋边。同时检查最终骨骼、完整刀刃、脚/骨盆及镜像朝向 | [刀握持](BladeGripCloseupDiagnostic.md)、[武器协调](BladeSwordChoreographyValidation.md) |
| V1 / 持续优化 | 武器、技能粒子、刀光/拖尾、弹体、蓄力、释放和命中特效的精致度与可读性 | 真实权威事件驱动时间/位置/朝向；落空不假命中，换装/取消不重播旧事件；固定池/容量、合并、优先级和低档回退，移动 CPU/GPU、上传、内存及过绘制有预算 | [粒子](BoundedWeaponParticles.md)、[移动表现](MobileVisualPolish.md)、[美术规范](SanctuaryArtDirection.md) |
| V2 / 待持续视觉验收 | 密集战斗的血条、伤害数字、武器/HUD 遮挡和小屏可读性 | 已有有界数字/血条避让不重做为“未实现”；在真实横竖屏、长屏、安全区及两后端复核，正常/暴击和核心命中反馈清楚。局部剩余问题依据最新录像再立具体修复 | [有界伤害字](BoundedDamageNumbers.md)、[血条避让](HordeWeaponHealthClearance.md)、[HUD](CompactMobileHudValidation.md) |

所有 A/V 项：正常速度连续攻击/移动/中断录像与自动化分开验收；保留实际时间戳，不插帧、不伪造帧率。当前助手做了源帧/接触表审阅，未完成连续实时播放的主观接受。保持热路径预分配、无新增托管 GC；CPU/GPU/Burst 等价和旧武器回归是必要条件，不是自然度证明。

## P1：Latios 隔离实验的剩余门槛

| ID / 状态 | 待办与下一步 | 完成标准 / 阻塞 | 来源 |
| --- | --- | --- | --- |
| L1 / 待推进 | patched Local 0.11.5 的重复 clean import / fresh-cache 对照 | 保留 pristine、补丁、实际 Local package 来源、锁图、独立 Library、严格销毁/释放/重入 oracle，重复结果一致；不得把补丁成功改称原版 Git 成功 | [已发布核验报告](https://github.com/karosLi/SPGameFoundation/blob/98efc16e488b46a9dba6d2f755785d087b1cd731/Latios2022Lab/Docs/Validation/20261008-patched-local-editor/verification-report.md) |
| L2 / 待推进 | 桌面 IL2CPP Player 构建及实际运行 | 验证真实 Player 工具链、构建日志、运行测试/释放生命周期；Editor Standalone-target PlayMode 不算 Player build | 同上；[综合方案](LatiosUnity2022IntegrationBlueprint.md) |
| L3 / 受门槛约束 | S1a 收尾，再评估 S1b / S2 的有界局部回迁/查询适配 | 前置 clean-repeat、Player 和所需设备证据完整；明确两个消费者、性能收益、兼容/回退和不采用条件后决策。尚未产品接入，不自动替换 SPF | [实施记录](LatiosImplementationProgress.md)、[分层审视](LatiosLayeringExtensionReview.md) |

已完成范围：源 b6ef271 的 [run 37813671446](https://github.com/karosLi/SPGameFoundation/actions/runs/37813671446) 完成一个 patched Local Editor 实验：Burst on/off 各 49/49 EditMode、各 1/1 PlayMode，0 失败/跳过，归档完整核验。原 Git 与匹配未补丁 Local 的 4 个失败仍保留。该实验不证明通用形状/solver、访问顺序、性能、所有可选模块、所有 Job 的 Burst 执行或全面泄漏自由；S1a 仍未完成。

## P1/P2：设备与发布验收

| ID / 状态 | 待办与下一步 | 完成标准 / 阻塞 | 来源 |
| --- | --- | --- | --- |
| M1 / 外部设备门槛 | 物理 Android/iOS 的真实构建和持续性能 | 固定设备/OS/API/负载，实际 IL2CPP/Burst、GPU 与回退；CPU/GPU p50/p95/最坏、内存/native buffers/上传/过绘制、热、电量、后台恢复。现预算中的内存/上传/过绘制 null 值需按目标设备填实，CPU/GPU/FPS 目标也需实测校准。桌面通过不替代 | [共享移动预算](SharedAcceptanceAndMobileBudgets.md)、[能力清单](RequestedCapabilityChecklist.md) |
| M2 / 外部设备门槛 | 真实多指触控、摇杆/技能、横竖屏/安全区和完整一局 | 短按/按住/拖瞄/取消、死亡/重开、失焦/后台/中断、不同尺寸和布局变化，输入拥有权正确，HUD 不遮挡关键目标 | [技能 HUD](MobileSkillHud.md)、[玩法接入](MobileGameplayIntegration.md) |
| S1 / 待试听及设备验收 | 原创音效/BGM 的正常速度实际试听、混音与路由 | 真机扬声器/耳机、系统中断/路由、压缩后资源、循环/重复切歌/交叉淡入淡出、延迟与解码 native 内存实测。PCM/AudioSource 信号测试已存在，不等于人耳音质验收 | [音频系统](MobileAudioSystem.md)、[原创资源](OriginalAudioAssets.md)、[已闭环原生音频](NativeFeedbackAudioClosure.md) |
| R1 / 发布前待办 | 内置精简、许可明确的 CJK 字体，减少对系统字体回退依赖 | 字体许可、字形覆盖/缺字回退、包体/图集/预热/运行分配、实际设备文本可读性与本地化检查 | [品类覆盖限制](GenreCoverage.md)、[移动基础](MobileFoundationBatch1.md) |
| R2 / 按产品范围推进 | 各玩法保存/暂停恢复的产品化策略与版本迁移 | 保留已有 ProfileStore、SaveEnvelope/SnapshotSchema 与 opt-in adapters；按玩法/配置登记存档支持、损坏拒绝、迁移和平台存储生命周期，不宣称所有旧模式已迁移 | [兼容矩阵](FoundationCompatibilityMatrix.md)、[保存 envelope](VersionedSaveEnvelope.md) |

### 保存与兼容的具体子项（R2）

- **待补验证**：Story 的保存测试目前检查文字/信任值/Choice，尚不足以证明完整场景状态、byte-exact restore 和后续 continuation。补对应反例与固定输入继续运行对照。[StTests.cs](../Assets/StoryFoundation/Tests/EditMode/StTests.cs)
- **待补历史覆盖**：除特定 Survivor 旧 fixture 外，其他模式的固定旧构建 golden 不足；从固定旧源码独立采集，不能拿同次运行的自比较代替历史兼容。[兼容矩阵 §8–9](FoundationCompatibilityMatrix.md)
- **条件性完整保存**：Snake 的 SnakeGameState、RegionPopulations、ReplayBuffer、SnakeQuality、Signals 与系统私态未构成完整中途 Session 保存；已有 replay 不等于保存缺失全部从零开始。
- **条件性 adapter rollout**：RPG / Story 的 SessionSnapshotSave 仍包装 raw session；现有 opt-in Sv/Bw envelope、composed/damage adapters 已实现。FlyingSword / CrossedBlade 等新布局需完整 schema 后再承诺，不自动套用 Classic / Guard 配方。不承诺自动旧格式探测、跨版本或跨平台 ABI 迁移。[保存 envelope](VersionedSaveEnvelope.md)

## P3：条件性扩展，不是当前必须实现的缺失

| ID | 触发条件与验收 | 来源 |
| --- | --- | --- |
| C1 | A4 通用命令、更多访问声明/旧可写列迁移：有新增语义/第二消费者才扩展；当前固定队列、A1–A3 已实现，冷诊断不宣称完整内存安全 | [Latios 实施](LatiosImplementationProgress.md)、[访问与结构窗口](AccessAndStructuralWindows.md) |
| C2 | 并行分离、树/查询后端、pruning/AI 策略或完整 BT：配对全 Tick/构建查询等待测量、确定性/顺序/溢出等价且有设备收益再采用；目前保留 grid/direct 默认 | [碰撞基准](CollisionBroadphaseBenchmarks.md)、[AI](AiDecisionValidation.md)、[后端契约](StageFBackendContracts.md) |
| C3 | 经典 projectile row-memory / 旧存档布局迁移：单独版本化、兼容策略和 fixture；不以统一命名为理由重写 | [分层计划 §8](SharedFoundationSemanticExtensionPlan.md)、[兼容矩阵](FoundationCompatibilityMatrix.md) |
| C4 | 任意骨架/蒙皮导入、更大 BAT、GPU 动画/音频/渲染后端及 Latios S3/S4：有实际资产/产品需求才立项，能力/精度/资源/许可证和 CPU 回退都验证；现三骨两影响 BAT 与 cutout 不是通用导入器 | [BAT](BatCharacterValidation.md)、[compute palette](BatComputePaletteValidation.md)、[综合方案](LatiosUnity2022IntegrationBlueprint.md) |
| C5 | 护送玩法、减速投射技能、新后端等扩展示范及未来玩法 adapters：仅为接入例子，用户选定需求后立项；复用模块/共享接口，完整可玩闭环和两消费者验证 | [分层计划 §7](SharedFoundationSemanticExtensionPlan.md)、[新玩法配方](NewGameplayIntegrationRecipe.md) |
| C6 | 全面 Entities/Latios 迁移、Unity 升级、运行时模块热插拔、网络/预测回滚/MMO、无限行为图、跨平台位级确定性 | 明确延期/非当前范围；不得当本次用户要求自动开工。[分层计划 §8](SharedFoundationSemanticExtensionPlan.md) |

### 其他已识别的条件项与风险验证

- **并行满容量接纳语义**：ParallelQueue 的 atomic arrival order 决定被接纳集合，事后排序不能让溢出时的集合天然确定。先表征 schedule/batch/overflow；若新需求要求确定接纳，再设计并版本化，当前未证明生产失败。[ParallelQueue.cs](../Assets/SinglePlayerFoundation/Contracts/Collections/ParallelQueue.cs)
- **访问检查边界**：旧可写 NativeArray、缓存别名、raw registry/resource 和未交接的私有 Job 仍不受完整检查；按实际消费者迁移/审计，不重复制造已完成的 A2。[AccessGuard.cs](../Assets/SinglePlayerFoundation/Runtime/World/AccessGuard.cs)
- **异步资产与租约**：真实引入异步加载才实现取消、迟到结果回收、多 owner 共享租约；更细程序集拆分、通用多消费者事件也需两个具体消费者，而非为抽象扩框架。[分层计划](SharedFoundationSemanticExtensionPlan.md)
- **品类条件项**：Defense 敌人 strip 随真实位移与炮塔 recoil profile、大规模刚体按岛并行，按实际产品需求及性能证据立项；不把示范未覆盖所有品类动作当成支持 API 坏掉。[动画接入](AnimationIntegrationByGenre.md)、[品类覆盖](GenreCoverage.md)
- **AI cadence**：昂贵感知错峰/事件失效策略如改变已有 cadence，须版本化回放/保存并做全 Tick/设备 A/B，不能宣称改成树即更快。[AI 架构](AiDecisionArchitecture.md)

## 已完成或被新证据覆盖的历史 TODO

- 共享 HUD、横竖屏/模拟安全区、真实 gameplay 人物、粒子、伤害数字、原始音频、九经典玩法及新增变体都已有实现和记录；本页只保留后续质量/设备/产品范围门槛，不把它们列成从零实现。
- A–G 分层语义能力、A1–A3 的源码/集成检查和既有原生音频已进入后续已验证产品源；历史冻结文档中“尚未运行”只属于当时提交。不能用新通过结果倒写旧运行成功，也不能用旧文本否定当前完整结果。
- 早期刀斩 10 项转向失败、3 项移动速度失败、完整运行的 macOS Bash 启动失败均保留历史；当前 7a1ac69 自动化已通过，不再是运行中缺陷。
- 血条/武器避让已有修正和原生像素证据；剩余密集场景观感归 V2。软件通过不等于用户视觉接受。
- GC 库存 DebugUpdater 修正已发布；校准仍无效是 G1，不能合称“GC 修复完成”。Latios patched Local 单次通过与 S1a 完成同样分开。

## 覆盖与维护规则

- 本次扫描当前提交 1,068 个 tracked 文件的 whole-word `TODO/FIXME/HACK/NotImplementedException`：0 命中；其中 670 个 code/tool/shader 文件、96,703 行（619 C#），覆盖 Assets、Tools、Lab 和 validation 工具。`ToDot` 等名字不算 TODO。零标记不代表零待办。文档中的“待验收/未运行/限制/延期”、用户当前要求和相关已发布分支一起核对；生成目录和外部上游源码不当作本项目承诺。
- 路线图是已知范围清单，不保证发现每个潜在缺陷，也不承诺未提供的参考附件或任意新玩法。原始上游 TODO、第三方所有算法和未经要求的重构不在自动实施范围。
- 每项后续更新记录：状态、精确 source commit、测试/失败/跳过、可打开证据、剩余门槛及用户是否接受；有新缺陷先复现，不放宽阈值。每个重大已授权改动单独提交/push，不擅自 merge/force push。
- 本轮仅文档；以 `[skip ci]` 避免重复消耗 Mac runner，不声称新文档提交重新跑过 Unity。运行时验证仍准确归属 7a1ac69，后续代码变更必须重新验证。
