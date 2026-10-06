# 共享战斗与加权角色后端检查点（2026-10-06）

> 证据更正：本机Unity线程字节API未通过故意分配正对照，先前同步0B断言不能作为零分配证明。详见[校准记录](AllocationMeasurementCalibration.md)。逻辑、快照、图形与独立Profiler/全帧记录结果分别保留。

## 已实现的可复用能力

- `HitHistory`：有界 caller-owned NativeArray，使用完整实体 index/generation 保存跨 tick 命中身份；明确满容量、去重、重置、owner/pulse 和快照约定。
- `ActionTimeline` / `TickInputBuffer`：固定整数 tick 的有效窗、取消/中断、pulse 与单次消费缓冲。两个规则实例真实使用共享时间线和命中历史。
- `CombatSweep`：相对运动圆扫掠，稳定入口根、double 中间计算处理极小运动/相切；Shooter 独立保留旧算术，避免兼容性变化。
- Survivor 选配交叉刀路、Brawler 选配稳定攻击身份，均可通过 bootstrap 实际启动、命中和重开。经典快照保持原样；经典持久 row 缺陷没有被隐瞒成已修复。参见 [SharedCombatStage2](SharedCombatStage2.md)。
- 真正的两权重顶点角色、两段烘焙 clip、矩阵纹理 GPU 蒙皮、限定两骨顶点阶段 IK、CPU/Burst weighted-mesh 回退。显式 64 B instance /32 B palette ABI、能力检查、精度门槛与容量。参见 [BatCharacterValidation](BatCharacterValidation.md)。

这一步的角色几何是验证后端的原创简化模型。精致美术、自然多链步态、技能 HUD 和更丰富的反馈另在下一阶段验证，不用参考模型截图宣称已达到最终美术质量。

## 当前真实验证

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 完整 .NET harness，71 个程序集 | 532 通过 /2 Explicit 未执行，0 失败，编译0警告/错误 | Artifacts/Stage2/Final/harness-*.log，23 个 TRX |
| 完整真实 Unity EditMode（移除临时诊断后） | 540 通过 /3 Explicit 跳过，0 失败，17.44秒 | Artifacts/Stage2/Clean/editmode.xml |
| 针对性真实 PlayMode | 13/13 通过，0 跳过 | Artifacts/Stage2/Clean/playmode.xml |
| 完整真实 PlayMode | 80 通过 /1 Explicit 跳过，0 失败，398.36秒 | Artifacts/Stage2/Final/playmode-retry.xml |

13 个针对性用例包括9个BAT图形用例与4个两档共享战斗接入用例。验证了 Brawler 真实触控输入→一次攻击→重开，Survivor 两道交叉命中去重→重开，以及布局/画质不得修改模拟快照。

### 角色后端证据

- 实际 GPU 顶点回读最大世界坐标误差：Float `1.274202e-5`，Half `1.095919e-6`。CPU数学另与既有 Skeletal 源姿态及 inverse-bind 独立对照。
- 生产 CPU/GPU 材质图像：24,807 个有效像素，在允许的一像素边缘带外差异0。
- 真实场景 `Graphics.RenderMeshPrimitives` 与独立强制CPU `DrawMesh` 各验证64个角色、独立相位、双朝向、IK改变433个手臂像素，以及禁用清空和重建。CPU用例不依赖GPU可用。
- 两后端分别预热64次后测64次 `Prepare` 时，原线程字节API报告0；后续正对照证明该API在本机Unity无效。此项不能证明零分配，待经校准的记录器重新验证；也不是整帧/其他线程/native内存结论。
- 原始角色在4倍比例与256像素/单位的严格包络下，half误差约1.26像素，超过0.25像素预算，实际选择float。未降低阈值；小尺寸fixture单独覆盖half路径。
- 256个角色dirty上传：GPU instance payload 16,384 B；同一95顶点角色CPU动态位置/颜色payload 680,960 B。单次共享BAT float上传11,520 B；不是硬件总线流量，也不是已证明的手机帧率倍数。

环境仍为 Unity2022.3.62f2 / Linux / OpenGLCore / Mesa llvmpipe。真正GPU shader执行正确性可在此检验，硬件吞吐、GLES/Metal/Vulkan设备、功耗与热态仍须真机。

## 保留的异常与处理

一次完整 EditMode 在测试启动后无结果、进程等待超过6分钟；仅该测试进程被终止。临时测试开始/结束记录辅助复跑通过540例，移除记录后的干净复跑也通过；没有确认原停滞原因，不归咎于某个未证实的玩法或BAT代码。

首次完整 PlayMode 在测试执行前的 Unity Mono `PipeStream → SetCloseOnExec` 原生断言退出134，未产生XML。日志位置在脚本编译启动阶段；没有把这次启动失败计成测试通过。保存脱敏失败摘录并单独重试，不改GC预算/断言/玩法来绕过。之后完整 PlayMode 80例通过；一次重试成功不能证明原生断言的具体根因。

## 可运行入口

- Brawler `BwGameBootstrap.CreateSharedCombat()`，或在原 bootstrap Inspector 打开 Shared Combat；呈现容量仍64，不宣称纵深格斗或千人扩展。
- Survivor `SvGameBootstrap.CreateCrossedBladeExample()`；交叉刀路目前有实际模拟伤害，但此阶段没有专属挥刀视觉。
- 菜单 `SPF/Characters/Create Or Update Weighted BAT Scene`，Play；1/2/3切换1/64/256角色，C切换CPU，I切换首角色IK，按住指针改变目标。矩阵参考不参与玩法权威状态。

完整回归已于17:42:06 UTC完成。该检查点单独提交和推送；未完成的下一阶段不并入。
