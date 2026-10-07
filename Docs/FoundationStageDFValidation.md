# P2 D/F 组合验证

2026-10-07。D 的[显式保存协议](VersionedSaveEnvelope.md)与 F 的[可替换后端契约](StageFBackendContracts.md)在本地 `c3509e538f234deee74d404d884434bc01655441` 完成最终组合验证。它们保留九个经典玩法/四个移动变体的默认组合，不新增通用服务定位器、无限能力图或新默认后端。

## 实际交付

- 保存：完整显式表/列/资源/系统 schema、契约/内容/视觉/raw-reader/runtime 身份分别管理；有界外层 framing 与 SHA-256 完整性，拒绝不兼容后才交给旧 raw reader；两个实际武器模式接入，已知 legacy checkpoint 在新临时 Session 中确认后转换，原文件和 live Session 不被该导入修改。不是认证、任意字段迁移或跨平台持久格式。
- 原始 raw 入口、固定 fixture 与方法体的直接比较保留在 [D 验证](validation/SaveEnvelope-20261007.md)。校验通过后的非法 raw 内容继续使用明确的重启失败语义；内部后缀案例的两次时间线失效已说明。
- 后端：复用已有空间 visitor、有限 reactive selector、能力探测及各 renderer 的本地回退；共享 conformance 检查 traversal/prefix/overflow/过滤/稳定平局。遍历集合相等不冒充浮点归约相等，网格/直接 AI 的默认选择不变。
- 图形夹具增加两个真实游戏、两档后端、三个生命周期时点共 **12 张预期点截图**及实际 backend/tick/采集时间 sidecar。它们还未在本组合执行，不是连续转换视频的替代。

## 最终本地证据

- **1,259/1,259** .NET 逻辑用例通过，11 个 EditMode 程序集；76 项目构建零警告/错误。两个历史显式工具用例仍不执行，.NET PlayMode 空程序集不计作图形通过。
- 全部 **522 份 Assets C#** 对 95 份真实 Unity 2022.3/API 依赖编译，net8.0 与 **.NET Standard 2.1** 两种目标均零错误，各有7条原有字段警告。BCL 编译检查不等于 Mono/IL2CPP/Unity 执行。
- 冷19探针两次逐字节相同，保持P0哈希 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`；旧基线不覆盖。独立分支的1,245与1,210通过数不当作相加后的结果，组合后已重新执行上述完整用例。
- 日志/精确 tree 哈希见[机器记录](validation/FoundationStageDFIntegrated-20261007.json)。复现沿用正常 harness，真实API脚本可使用 `--all-assets --target-framework netstandard2.1`，以及[固定冷探针](validation/FoundationCompatibilityProbe-20261007.md)。

## 尚不能签署的门槛

本批尚未完成原生门槛；此前P1 `.NET` 已绿，但一次原生失败没有留下XML/产物。截至10:21 UTC，b75 的一次已授权重试已被 Mac runner 接受，测试步骤在10:15:24开始；2066211 仍排队。本批D/F尚未取得结果。此处不伪造新的 Unity/Burst/像素/视频结果，也不套用早先 e86 的绿色记录。运行恢复后按精确提交回收原生XML、图像、时间戳和分片哈希，特别检查新增12张点截图。

物理 Android/iOS 的触摸、中断、IL2CPP/Burst、实际回退、内存、持续帧时、发热与电量仍待设备验证。E 的两种游戏规则扩展示范与 G 的共享验收/移动预算报告继续独立实现；软件门槛和设备门槛分别保持状态。
