# 刀的换向轨迹修正（2026-10-08）

本次基线为本地 `278989217313a1565ecf0b4fb625e49008c8b58b`，源码树 `b2d5e2fc751baede4496c0a722db2685f4ed7bb9`，与远端 `5d1db3e` 同树。范围仅为已装备 `Slash / 1001` 刀的表现换向和相关取消衔接。既有接触前刹停、接触后直接回到 guard 的曲线不变；剑刺、法杖、弓、权威时间线、接触几何、冷却、移动规则、存档、美术及渲染流契约不变。

## 实际反例及原因

旧版真实近景里，GPU `blade-grip-isolated-gpu/frame-054..057.png` 和 fallback `blade-grip-isolated-fallback/frame-053..055.png` 都出现向左换向时刀经过腿边再抬起。柄仍在手里；错误是整个腕/刀的轨迹，不能用握持误差为零代表视觉通过。原图、两档局部放大和追踪摘录保留在 `integration-validation/resume-20261008/blade-closeup-review-5d1db3e/`。完整原生范围由独立的 [近景审查](BladeCloseupNativeReview.md) 记录。

旧采集观察器可能在读取 prepared 图形之后通过 Sync 推进一个 Tick，因此原 `authority-and-camera.csv` 不能作为图形帧精确相位的 oracle。上述像素仍是实际渲染反例；本次根因和红例用独立的公开 `WeaponRuntime.Step / View / GameplayCharacterPresenter` 输入链重现，不依赖 CSV 的单 Tick 相位对齐。

原因链为：

1. `atan2(sin(delta), cos(delta))` 在水平 180° 的等长弧处由 float 的 `sin(pi)` 符号决定下半圆。运行中动作仍拥有旧朝向，下一次 held 动作接纳新朝向时才暴露。
2. 平滑瞄准还在转动，抬刀角却立刻乘新的离散 Facing，导致原本向上的 guard 被翻到下侧。
3. 固定 IK bend 随 Facing 立即镜像，最终肘和腕会换支。单独把瞄准改成上半圆不足以解决最终骨骼的翻支。

基线 `BladeTurnTrajectoryTests.HeldBladeReversalNeverDetoursBelowContact` 在 30/60/120 Hz 和不规则步长均为红。测试完整经过 idle samples 0..3、右侧 held、sample 38 改左、旧动作恢复、下一脉冲及整个恢复段，双向、静止/移动。基线记录的最低刃尖模型高度约 `0.072–0.109`，接触基准为 `1.25`；不是仅检查开始或取消的同一时刻。

## 有界修正

- 仅刀的水平 180° 歧义明确选上侧弧，其他角度仍按原最短弧。
- 刀 guard 的左右归属随既有 `motion.Turn` 连续变化，当前动作的 `aimWeight` 在接触时收敛到权威朝向。没有新增振荡器、自由时间轴或速度调参。
- 同一 Turn 参数拥有主臂镜像。用当前相位的 incoming guard 作参考，肘在身体中线经过紧凑折叠，两个真实骨段长度保持不变；不会用瞬间反转固定 IK 分支来转身。+1 时退回原求解。肩滑只消费当前动作的 guard 参考，不会把虚拟瞄准转弧误当成需要额外肩滑的抬高手。
- 腕仍受原 `±0.85 rad` 限制，武器的位置和方向仍取最终 Hand。接触时 `aimWeight=1`、`ArmBend=1`，原 IK 与标准 socket 路径生效，接触时刻/位置不改。
- 取消沿用原有 0.2 s Hermite 衰减窗口，同时保存 arm reference 和 turn ownership。新增 4 个 float2 与 4 个 float 值字段（每个预分配表现槽增加48字节字段数据，按Capacity保留而非当前可见人数；实际结构对齐/分配量另测），容量固定，不进入权威存档。若换向与取消都发生于两个绘制帧之间，先转换参考的镜像坐标再继承，覆盖真实 30 Hz 情况。
- 未持武器时 turn ownership 为中立 1、速度为 0；新增折叠还受 WeaponWeight 控制，装备本身不产生假转身。刀取消时残余通用 punch lean 使用同一 EquipAge 淡入，避免肩部瞬跳；真实 Kick 不走这条修正。
- 两种 IK 路径互斥执行，无新增数组、池、临时集合、逐帧托管分配或迭代求解。未修改相机、拖尾、阶段跳过或性能阈值。

## 验证与边界

证据目录：`integration-validation/resume-20261008/blade-turn-trajectory/`。

- `red.log / red.trx`：基线完整换向红例，原始低绕保留。
- `frozen-focused.log / frozen-focused.trx`：最终 focused 168/168 通过，0 失败，覆盖武器动作/运行时、刀剑编舞、握持美术、恢复/接触 C1、全身动作、角色流/分配及新换向测试。
- 新换向用例包含 3 个动作角色、双向、静/动、30/60/120 Hz 与不规则步长；Kind=1 实际触发低画质 15 Hz key 缓存，和高画质每帧最终骨骼/刀结果一致。实测最大腕速 14.8224、肘速 6.5242 模型单位/s、刀角速 33.5937 rad/s。原阈值：腕速 <15、肘速 <20 模型单位/s，刀角速 <35 rad/s；手/刃尖不得低于接触高度。另完整采样在新向动作第 1/2/4/7 Tick 取消、等待、重入之后的尾段，并保留原 dt=0 的严格开始/取消/重入连续性测试。
- 未持武器进入新增用例使用同一 armed 身体修正为控制组，只改变转向归属；不把既有 equipped 躯干接入行为误算为本次折叠回归。检查零权重无额外折叠、未转身装备无假折叠、腕限幅和最终刚性握持；零 dt 卸下与重入也不得继承已移除手臂的转向速度。
- `other-weapons-equality.json`：对每种剑/杖/弓的 10,080 个样本逐字节比较所有 attachment、骨骼和 packed sprite。每种 8,547,840 字节，与基线相同。样本包含 30/60/120 Hz、三个角色、左右、移动/静止、guard/完整动作/取消/装备。
- `native-domain/compile-frozen.log`：官方 Unity NUnit 引用及非 harness 条件域编译审计；这是编译审计，不是 Unity 原生执行。

本分支没有运行新的 Unity、aggregate 或 push。候选的两档真实 1× 近景、逐帧骨骼/刀轨迹和原生完整性能门槛仍需集成分支验证；上述逻辑绿灯不能代替用户对动作自然度的接受。Android/iOS 物理设备门槛仍保留。

## 集成软件检查点

运行时代码与独立的观察窗口修正合并后，78个程序集编译通过，0警告/错误；一次完整aggregate为 **1,681通过、0失败**，保留原六项Explicit诊断。12个非空测试程序集和15个空PlayMode harness结果分别记录，空结果不提供原生覆盖。新旧motion与observer条件域均用官方Unity NUnit/UTF审计，0错误、各一条既有host版本引用警告；engine仍为桩。[精确源、哈希及范围](validation/BladeTurnIntegration-20261008.json)。

观察器现在处于renderer500与下一Tick launcher32000之间的1000窗口，进入时必须没有pending tick，且不能推进时钟；原武器ID、快照和sprite身份断言均保留。原5d两次失败、完整近景反例和存储恢复后的22项复验结果见[原生审查](BladeCloseupNativeReview.md)。只读磁盘诊断分支没有合入产品候选。

此次生产代码已变化，必须在新精确head上重跑完整native并取得新的普通和近景两档录像；不得把旧影片当作修正后结果。待新实际像素、正常1×/明确慢放和用户判断完成后，才能扩大视觉结论。
