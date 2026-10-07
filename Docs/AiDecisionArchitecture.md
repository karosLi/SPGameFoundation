# 有界 AI 决策选择：复用策略，保留动作状态机

## 本轮目标和边界

在现有 RPG 和横屏 Belt 真实玩法中复用可验证的决策选择；不新增 demo，不改变操作、固定 Tick、武器/技能前摇、打断、恢复、移动/碰撞执行、表现或画质规则。RPG 仍是原有 30 Hz，Belt 仍是 60 Hz。容量沿用各模块配置，决策程序最多 32 节点/次访问。

**默认仍用原有直接策略，树是显式选项。** 测量没有证明收益，因此没有把解释树强制用于移动端：

- RPG：创建 Session 前设置 `RpgConfig.UseDecisionTree = true`，Bake 后固定该选择。
- Belt：创建模块前设置 `BwBeltConfig.UseDecisionTree = true`；Default 为 false。
- 真实系统的树路径在专项测试和 A/B 中明确打开，通常 bootstrap/旧配置仍走直接路径。程序数据每 Session 只初始化一份；没有按角色分配。
- 两个实现的决策结果等价；这个实现选择不作为新增权威存档字段。未来若加入会改变行为的 utility/cadence/hysteresis，必须另行版本化，不能沿用此理由忽略保存。

交付分为三件事，不能相互替代：

1. 一个小而可复用的确定性决策程序，以及两个实际系统接入。
2. 策略与原执行器的严格等价验证，包括完整快照、恢复和真实动作锁定。
3. 分开的选择/感知/完整 Tick 成本报告。**树本身不是速度优化；解释执行树可能比手写分支更慢。**

不支持可视化编辑器、运行时脚本、任意回调、黑板字典、可暂停 Sequence/Running 任务、规划器或跨平台位级浮点保证。没有改动 Snake 的随机消费和质量档回放，没有给 Puzzle/Sling/Story 强加 NPC 行为树。

## 接口与所有权

- `SPF.L2.AI.DecisionTree.Create(nodes, knownFacts)` 是仅初始化时调用的验证/分配入口。接受 1..32 个平坦节点，拒绝未知/矛盾事实掩码、空条件、非法 action、越界/回边、不可达节点。所有边向前，因而不能形成环。
- `DecisionNode.Branch` 检查 required/forbidden 事实位；`Leaf` 返回模块自己的整数 action ID。节点不是 GameObject，也不自己读取世界、查询空间、抽随机数或执行攻击。
- `Evaluate` 是迭代式、最大 32 次节点读取。返回 `Status / Action / Leaf / Visits`；非法程序和访问预算耗尽有不同状态，失败 action 是 -1。模块选择安全 Hold；调用者可保留 trace 定位叶子/失败，不需要分配字符串。
- 一个模块共享一份 NativeArray 程序。RPG 的配置、Belt 的状态资源在初始化时拥有它，在 Dispose 释放；只读使用，不按角色分配，程序不是存档字段。
- 感知事实一次捕获并共享给所有分支。现有 sight/grid/flow 查询仍由模块负责，没有改动决策频率，也没有引入耗时节点各自重新查询的模式。

当前 RPG 程序 13 个节点，最长有效路径 6 个节点；Belt 6 个节点，最长路径 4 个节点。节点数据的实际内存尺寸以当前运行时为准，不把数组负载大小称为全部 native 分配。

## 两个真实接入

### RPG

`RpgDecisions` 只选择 Chase/Attack 状态中的战术意图：优先技能、远程后退并攻击/仅后退、接近、攻击或等待。原 `MonsterAISystem` 继续拥有 Idle/Return/Wander、追逐进入、丢失视线计时、provoked、朝向、flow field、状态计时和随机数。

范围/视线/技能条件与原代码保持相同严格比较。原本可能“后退同时射击”的组合保留，技能优先级保留；不能把一个复合行动误简化成单选。主线程把只读程序和固定的选择开关传给既有 Burst `BrainJob`；默认分支保留原手写条件链。

### Belt

显式启用时，`BwBeltDecisions` 选择追逐/对齐、攻击或等待；默认使用原有直接比较。外层 fighter 状态机先判断可行动；Hit、KO 和已承诺攻击不被新决策打断。左右朝向、稳定 lane offset、技能槽、玩家输入、动作计时、纵深/高度、武器及 pose 更新仍按原顺序执行。

经典 `BwSystems.cs` 未改。主循环没有被贸然并行化。独立分离 pass 有一个**仅测试的** IJobParallelFor 候选，读已完成的同一网格、按原访问顺序逐行累加、写独立输出和候选计数，没有浮点原子。只有原生测量和完整 Tick 收益支持时才应采用；本轮没有将其替换进生产。

## 同时发现并修复的旧确定性缺陷

完整快照压力用例发现：冻结旧 AI 对冻结旧 AI 也会在同 Tick 多个远程单位发射时分歧。原 Combat ActionJob 按线程到达顺序写 `ProjectileRequests`，下一 Tick 的 `ProjectileSpawnSystem` 未排序直接生成实体；队列本身也保存进快照。

修复与决策选择独立：

- CombatSystem 私有预分配 scratch：每个 actor row 一份 `ProjectileRequest` 和 pending byte。现有动作阶段每角色每 Tick 至多释放一个 projectile。每次 Execute 先重写 pending，死亡/无请求也写零。
- 生产者完成后，单个 Gather job 压紧有效项，以稳定 OwnerId、随后完整 payload 位序排序，按这一顺序写入现有容量队列。满容量仍拒绝后续项并计数；稳定选择低排序项，避免“先丢再排序”保留不确定子集。
- scratch 的容量是 actor 表容量，负载是 `Actors × (sizeof(ProjectileRequest) + 1)`，创建/销毁由该系统拥有。它没有跨 Tick 的决策意义，所有可读取的 slot/flag 都在当前 Tick 产生，因此无需新增存档字段。
- 旧存档里的 pending request 仍可读；消费前也排序一次。新增的 gather 不增加随机调用。

第二处是通用 DestroyQueue：播放本来已按 index/generation 排序，但保存的是线程入队序。现在在同步后的 WriteSnapshot 中用**同一个 comparator**原地规范化。保留所有已接受项、重复/过时代数、overflow 计数和原字节布局；不提前销毁，也不新增无界缓存。通用 DestroyQueue 本身的溢出接受子集仍由原队列机制决定，本次排序不声称解决任意模块在队列溢出时的全局确定性。

兼容性：存档结构未改；DestroyQueue 的未来播放语义未改。RPG 历史上不确定的 projectile 到达/生成次序现在固定，可能改变旧运行的 projectile ID、由其影响的后续顺序/随机流和满容量时被接受的请求。不能承诺修复前后所有历史完整回放字节相同。A/B 策略基线在两侧使用相同的修复后队列管线，隔离验证“选择策略没有改变”。

## 行业依据与后续方向

- [Epic 行为树概述](https://dev.epicgames.com/documentation/en-us/unreal-engine/behavior-tree-in-unreal-engine---overview)：事件驱动和定期 services 可以减少重复求值。下一步应实测 sight/perception，并对重要变化失效，不是把每个 if 包成任务。
- [Epic StateTree](https://dev.epicgames.com/documentation/en-us/unreal-engine/overview-of-state-tree-in-unreal-engine)：选择器与状态/转移可以组合；本项目保留 action FSM 的所有权。
- [Game AI Pro，Utility 与行为树](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter10_Building_Utility_Decisions_into_Your_Existing_Behavior_Tree.pdf)：区分 evaluation/execution，昂贵评分应缓存或独立调度。这里没有复制其完整框架。
- [Game AI Pro，Utility Theory](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter09_An_Introduction_to_Utility_Theory.pdf)：惯性/冷却能抑制来回切换。Guard 的 target hysteresis 应作为显式新玩法变化另测，不能偷偷加入经典行为等价改造。
- [Unity Burst 类型约束](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html)：使用受支持值类型与 native 数据；实际编译以仓库 Burst 1.8.27 为准。

后续优化候选：按稳定身份错峰昂贵感知，死亡/受击/目标失效及时重算，移动和动作执行每 Tick 保持；如改变 cadence，必须带入回放/快照合约。局部邻居遍历仍需报告真实候选数，不能将“最多八个重叠”当成“最多八次候选检查”。
