# P1 组合与生命周期集成记录

2026-10-07。落实[分层语义计划](SharedFoundationSemanticExtensionPlan.md)的 B/C 软件边界。P0 的 19 个真实组合基线保持原文件，后续改动不覆盖历史证据。

## 已发布的第一批：回滚与 View 生命周期

- 远端 `b75d4234a4b6547f30ba4e44e3aaf9d7bf7e4c43`，对应本地 `867583ea413f0d36b7dbd98a6016c5d6d641f7e2`，完全相同 tree `f7f372826e4ac2d74648adc579b0c00085cd7fb1`。
- 最终本地 76 项目构建零错误，11 个逻辑测试程序集 **1,132/1,132**；503 份 Assets C# 对真实 Unity 2022.3 API 编译，95 依赖、0 错误、7 条原有字段警告。编译不是原生执行。
- 精确提交的 [.NET CI](https://github.com/karosLi/SPGameFoundation/actions/runs/37600770860)已成功，1,132 执行/通过；两个原有显式工具用例不执行。[Unity CI](https://github.com/karosLi/SPGameFoundation/actions/runs/37600770862)本记录时仍运行，不能套用旧原生绿色结果。
- 冷探针两次输出相同，19 个组合输出 SHA-256 仍为 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`。固定系统/资源/列顺序、容量与旧 raw 保存字节的既有测试未改阈值。
- 本地最终测试日志 SHA-256 `41f11a20dac7c140016253ba1fbf8aee0d88a6b2b6139f4f3da42a4422f36258`；构建日志 `27fd4a3914afe29d24fd8e80a5a753e7ed6d8bf595c45d6514679da51b5f8f89`。

实现和红测试分别见[组合回滚](CompositionRollbackValidation.md)、[View 生命周期](validation/SessionViewLifecycle-20261007.md)、[Host 拥有关系与失败绑定](validation/SessionHostRetirement-20261007.md)。清理异常继续尝试其他 owner，原始原因不被掩盖；禁用 HUD 不重新激活输入，两个真实表现适配器丢弃隐藏期间的旧武器 cue，同 Tick 恢复使缓存失效。

Host 先撤销对外 Session 再清理，保留一个私有待清理引用直到真正完成，阻止清理/绑定中的重入替换。公开 Disposed 状态和已完成全部退出不是同一件事。任意失败初始化器仍须收回尚未移交的私有分配与未返回 Job；引擎无法完成 Job 时不能强制释放正在使用的 World。终局销毁后无可用回调时不承诺自动恢复。

## 第二批：可选预检与异常 Tick 拥有关系

[可选预检](OptionalCompositionPreflightValidation.md)在实际分配前检查明确声明的 capability/schema/scope/owner/capacity/save-hook 切片；Survivor/Brawler 武器模式均接入。声明不等于完整配置或完整存档证明。经典空清单仍有冷路径成本，已用阳性/空控制报告；不声称零启动开销或移动端收益。

[异常 Tick](TickFailureOwnershipValidation.md)保留已返回 Job 的拥有关系和最初调度异常，安全恢复失败可重试；保留正常串行分析和 barrier 的原生 completed-handle 值。失败 Tick 不回滚模拟写入，OnSync 可能已执行部分通知。模拟 pre-completion 拒绝仅存在于 .NET harness，不能冒称真实引擎故障注入。

两项独立源分别通过 1,154 / 1,146 个 .NET 用例、真实 API 编译和相同冷19探针；这两个数字不是组合后的总数。组合后的验收单独记录，不以这些分支结果替代最终源码测试。

## 后续与外部门槛

新保存 envelope/显式 schema 与内容标识按 D 阶段单独实现，旧 raw API 保留。异步资产租约仍以真实异步消费者为前提；不为当前同步加载增加空框架。移动端触摸、IL2CPP/Burst、图形回退、内存、温控、电量和持续帧时仍需 Android/iOS 真机，桌面 CI 不能签署这些指标。

复现：使用仓库 .NET harness 全量构建/测试；`Docs/validation/GenerateCompositionApiCompile.py --all-assets` 能保留全部原生测试源码的真实 API 编译；[冷探针](validation/FoundationCompatibilityProbe-20261007.md)比较语义和源文件改动分别报告。原生 XML、图像、连续 1× 视频及分片哈希恢复按 [CI 证据流程](CiEvidence.md)保存。
