# 2026-10-08 原生与伤害反馈检查点

精确运行源为 [23d509f7](https://github.com/karosLi/SPGameFoundation/commit/23d509f728ee21b6239d96575e5c14adf9b18b05)，tree `ad7cdb4fbd2f6223c5a52c56554d259f67986386`。本页闭环 A1–A3 的桌面原生正确性、快照初始化和有界伤害字改动；后续人形抬臂、步频及独立 Latios 实验不包含在该结果中。

## 已完成的验证

- [官方 .NET 37712952736](https://github.com/karosLi/SPGameFoundation/actions/runs/37712952736)：12 个逻辑程序集 **1,567 通过、0 失败**，构建 0 警告/错误。本地同一代码树的全量结果相同；空 PlayMode 桩程序集不计原生通过。
- [Unity 37712952704 / attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37712952704/attempts/1)：**EditMode 1,606 通过、0 失败、5 个原有显式跳过；graphics PlayMode 169 通过、0 失败、1 个原有显式跳过**。两个 Editor exit 0，没有崩溃重试或关闭 Burst 的后备运行。
- 相对 35af6dfa，EditMode 新增 13 个用例、删除 0；唯一旧用例结果变化是原 Snake 配置隔离测试从失败恢复为通过。PlayMode 用例身份和结果完全相同。重叠的 .NET/原生用例不能相加作为独立覆盖数。
- 19 个真实冷组合在两个独立进程逐字节一致，仍为原 SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`。6,588,656-byte Snake 的原 .NET raw 摘要也不变。
- Burst direct-managed / Run / Schedule 为 **0 / 1 / 1**；物理 **60/60 预热、300/300 测量**均为原生，mean 0.418 ms、worst 0.594 ms。只说明本次桌面 Editor 数据，不称为加速或手机预算。
- 音频 27 个观察完成，三个控制阶段各保留 8 个有帧/游标推进的样本；正弦能量约 0.238–0.256，静音均为 0。原门槛未改；它不替代实际试听或手机音频路由。

完整证据为 **22 片、1,559 个文件、359,418,148 bytes**，归档 SHA-256 `069cd551e0e19c2b168cba1f919f10facf9daa6b8faec0edd68529feaa904be1`。分片、每个文件及 1,210 个提前读取成员均已与完整恢复核对。见[机器记录](validation/NativeFeedbackRecovery-20261008.json)与[恢复方法](CiEvidence.md)。

## 修复与原失败的关系

1. Unity 的定制 NUnit 没有新增夹具无条件引用的 `NonParallelizable`；保留 .NET 属性并按实际 Unity runner 串行执行方式处理，夹具方法与断言不变。原编译失败仍见 [37708237734](https://github.com/karosLi/SPGameFoundation/actions/runs/37708237734)。
2. [35af6dfa 的原生失败](https://github.com/karosLi/SPGameFoundation/actions/runs/37708607373)暴露了完整快照包含从未初始化的网格尾部。仅在创建时初始化五个完整保存的缓冲，原 Snake 全快照相等断言不变；带非零旧尾部的恢复、重写和继续执行仍通过。[诊断和兼容对照](validation/CellListGridInitialization-20261008.md)
3. 实际 Belt 影像中，同一普通 18 会在邻近 !36 缩小时下落约 25 px。现在保留已经接纳的角色/标签避让总偏移，受原 25% 视口上限约束；拒绝的摆放不提交偏移。两后端的新真实连续帧均未再出现该匹配事件的向下释放。[有界伤害字](BoundedDamageNumbers.md)
4. 超过 1 MiB 的结果 XML 曾被排到视频之后，延误失败诊断。本次完整结果 XML 已在首片核验，容量、哈希、完整文件与全量恢复要求不变。[证据排序](CiEvidence.md)

## 真实画面审视与保留限制

已顺序审视 20 张玩法静帧、4 张隔离字形图及四段共 360 个真实源帧。可见数字保持普通/暴击区别，并避开实际头、血条和 HUD。四段预览各保留全部 90 个源帧和实测 PTS，编码仅增加已说明的末帧停留；无插帧或重定时。采集约 20.29–22.97 Hz，含同步读回，不能转换成手机帧率结论。审视方式为逐帧像素/轨迹，不宣称已完成连续实时主观观看。

预算仍有可见代价：

- Belt 首组普通 22 在两档各有约 50 ms 的完整隐藏后重现，`reserved_drops` 为 1→2→1，事实与活跃标签数未变；对应背景未发生相机位移。当前采集没有细分拒绝原因，不能把它宣称为普遍无闪烁。
- 新障碍或关键标签仍可引起上移；Horde 观察到约 64 px 的向上重排及完整标签抑制。总偏移上限正确生效后，部分密集/低档画面显示的标签比旧版少。
- 没有放宽容量、优先级、接触或 GC 门槛，也不承诺每次伤害都一定显示文字。实际 HP 结算与显示抑制分开。

Android/iOS 触摸、持续帧时、内存峰值、温度、电量、图形回退、音频路由及听感仍待物理设备验证。用户新提出的人形攻击抬臂、肩肘腕协调和更快视觉步频，需在独立动作提交用同镜头/同速度 1× 片段继续验收；本页不把当前动作自然度作最终认可。
