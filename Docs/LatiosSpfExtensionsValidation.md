# Latios 方案首批 SPF 扩展集成验证

日期：2026-10-07。软件验证源为本地 `4f70cc2`，其后只补充本文和证据说明。完整源文件/日志 SHA-256、程序集计数及边界见[机器记录](validation/LatiosSpfExtensions-20261007.json)。这份结果覆盖根 SPF 工程的 A1–A3；独立 Latios 实验工程只有准备与静态验证。

## 已实现的边界

- [A1 执行计划](ExecutionPlanDiagnostics.md)：记录实际模块来源和构造时声明，冷路径导出 text/DOT；调用顺序、声明依赖、框架可见 Complete 分开。不重复 Declare、不排序新系统、不把静态边当成实际等待。
- [A2 访问与结构窗口](AccessAndStructuralWindows.md)：增量 ReadColumn/WriteColumn、两个实际消费者试点、World 级在途所有权检查。一个 World 同时只允许一个 active/pending Pipeline；失败完成保留所有权。旧可写引用和系统内部未观察到的 Complete 均有明确限制。
- [A3 配置来源隔离](RuntimeConfigSourceIsolation.md)：Snake Capacity、RPG Dungeon/Loot/Capacity/技能槽隔离；非法输入在配置分配前拒绝，构造失败释放已分配前缀。公共运行对象仍可变，更新 authoring 后新建 Session 才采用新定义。
- [S1a 独立控制组准备](Latios2022LabPreparation.md)：固定 Editor 2022.3.62f2 与 Latios 0.11.5，准备 Core/Psyshock 原生反例和真实包锁采集。当前没有安装到 SPF 根工程。

## 集成软件结果

| 检查 | 结果与限制 |
| --- | --- |
| Harness 78 个程序集项目构建 | 0 错误、0 警告 |
| 12 个含逻辑测试程序集 | **1,533 通过、0 失败**；增加 101 个非 Explicit 用例，未删除旧用例 |
| 显式诊断 | 2 个原有 + 4 个新的 config 冷分配/raw 记录用例不计入上述通过数；专项运行记录保留 |
| 真实 Unity API / BCL 编译 | 576 个根工程 C# 源、95 份实际引用；net8.0 与 netstandard2.1 均 0 错误，各有 7 项原有字段警告；不是 Unity 执行 |
| 19 个真实冷组合 | 两次运行及带计划导出运行完全一致，并与旧基线逐字节相同 |
| 执行计划 | 19 份 text/DOT，重复输出一致；19 份 DOT 均可解析 |
| Python | 原有 CI 30、共享验收 24、音频制作 10、独立 lab 静态 22，全部通过 |

冷组合聚合 SHA-256：`a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`。Snake / RPG 默认同运行域 raw 摘要也保持原专项比较值。旧保存的写入/读取体、默认工厂与根 Packages/ProjectSettings/workflow 在此检查点没有改变。

独立源码审阅修复了两个真实问题：一个空 Pipeline 曾能清除同 World 另一 Pipeline 的在途标记；新 RPG 预检曾拒绝原本有效的最小房间和边距。均先保留失败反例再修正，未放宽原有 GC/物理性能门槛。A1 专项 TRX 重新按类核对为 15 个新增用例 + 34 个相关回归，共 49，通过数未变。

## 成本与仍待验证事项

诊断与校验不是免费的性能优化。A1 增加构造时元数据；A3 的有限值检查、来源副本增加冷分配。A2 成功热路径的校准探针为 0 当前线程托管分配，但 Debug harness 的计时不代表 release player 或手机。各自原始成本和观察器控制见专项文档。

- 这个集成提交的 Unity EditMode/Burst/graphics 尚待远端执行，不能使用原始 d7 的结果代替。
- d7 的修正音频 PlayMode 已核验 169 通过、0 失败、1 原有跳过，三段音频控制使用原阈值；其 EditMode XML、上传终态与完整归档核验仍由独立 P0 证据记录维护。
- Lab 的 49 个 EditMode + 1 个 PlayMode 案例、实际 lock、clean-repeat import、IL2CPP 构建/运行都尚未执行。固定上游的 reactive cleanup 查询存在源码风险，严格反例保留，不能靠最终 World.Dispose 掩盖。
- Android/iOS 的持续 CPU/GPU、内存、温度、电量、音频路由和实际操作仍需要物理设备。
- 已交付 fc1 的真实连续片段；密集横屏血条/数字及少量竖屏 HUD 边缘遮挡仍列为局部可读性待办，不把静态数学或软件绿色结果升级为最终美术自然度认可。

集成分支为 `dot/latios-extension-a1-a3`。后续实验按[实施记录](LatiosImplementationProgress.md)和原方案门槛独立推进；S1a 未通过前不采用 S1b/S2，A4 没有新增语义需求时不扩展通用命令系统。
