# 分层计划与移动音频最终组合验证

2026-10-07。本次验证源为 `14652d5a77422d10fc89f8e800199243cacc9bd2`，覆盖 A–G 的实际实现、原生生命周期夹具修正和移动音频。伤害飘字与参考视频驱动的新动作调整仍是后续独立改动，不套用本记录。

## 已完成的代码与本地组合门槛

- A–G 已分别实现并独立提交：[兼容矩阵](FoundationCompatibilityMatrix.md)、[组合回滚](CompositionRollbackValidation.md)、[可选预检](OptionalCompositionPreflightValidation.md)、[调度异常所有权](TickFailureOwnershipValidation.md)、[保存 envelope](VersionedSaveEnvelope.md)、[两游戏技能规则](ComposedAbilityRules.md)、[后端契约](StageFBackendContracts.md)、[共享验收及移动预算](SharedAcceptanceAndMobileBudgets.md)。保留旧默认组合、raw fixture 与明确兼容例外。
- 音频扩展现有有界声部系统，接入 Brawler、Survivor 和移动 UI；包含 12 个原创音效、两首原创循环 BGM，明确并发、优先级、暂停/后台、恢复、独立事件游标和导入预算。见[移动音频](MobileAudioSystem.md)与[资源来源/客观检查](OriginalAudioAssets.md)。
- **1,368/1,368** .NET 逻辑用例通过，12 个实际测试程序集；78 个生成项目构建零警告/错误。历史显式工具用例和 .NET 空 PlayMode 程序集不计作原生图形通过。
- **551 份 Assets C# / 95 份真实 Unity API 引用**，net8.0 与 .NET Standard 2.1 两种编译均零错误，各有 7 条已有字段警告。编译不等于 Unity/Mono/IL2CPP 执行。
- 19 个冷组合连续两次逐字节一致，保持 P0 的 SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`，未重写基线。
- Python 检查全部通过：CI 30、验收报告 24、音频创作/资产 10，共 **64**。精确 tree、各程序集数量与日志哈希见[机器记录](validation/FoundationArchitectureAudioIntegrated-20261007.json)。

生命周期修正使用显式零表现时间检查“恢复前仍存在、同 Tick 恢复后清除”，另有正常老化对照；移除清理调用的变异实验会失败。没有放宽粒子寿命或现有断言。见[夹具时钟诊断](validation/LifecycleProbeClock-20261007.md)。

## 原生历史与当前待验证门槛

- `b75d423`：EditMode 1,164 通过、2 个已定位的生命周期前置条件失败、5 跳过；graphics PlayMode 154 通过、1 跳过。实际 Burst 控制 0/1/1，物理 60/60 预热与 300/300 测量执行，平均 0.398 ms。
- `2066211`：EditMode 1,204 通过、22 失败、5 跳过；graphics PlayMode 154 通过、1 跳过。22 项包含上述 2 项及 20 个禁用 Burst 的控制/性能失败；该运行实际 `BurstCompilation=False`、控制 0/0/0。60 个新增原生用例通过，不抵销失败。
- 两份完整 19 片证据分别恢复 1,674 / 1,670 个文件。2066211 archive SHA-256 为 `d0297d7b9f14beee729633e58468131530876498fde1090c8c2b097871376d2c`；b75 为 `a3fd4d9cae9f3bff34fae82dbb7c7a19624c724a2a7ed8506abaa33f056da63c`。完整日志排除了这两个有证据的运行调用 crash/noburst fallback；偏好写入者仍未知。
- 正在复用先前获准的窄范围 Mac Burst 设置作业，需新进程控制证明恢复。该设置作业的旧源码测试不代替本次源的完整原生门槛。已有失败和原限值保留。
- 本次精确源的 Unity/Burst、12 点生命周期截图、两游戏技能视频、音频导入/DSP 和实际听感仍待执行或复核。没有工具试听时，只报告 WAV 峰值、削波、DC、接缝等客观结果。
- 用户参考视频重新指出人物/怪物动作质量问题。当前接触轨迹通过不能据此宣布自然：2066211 的 Walk 显示高抬腿、上身参与不足。后续修正需同镜头、同速度 1× 连续视频及角色/武器/转换审视。
- Android/iOS 实机持续帧时、发热、电量、内存、触摸和音频路由/中断仍是外部设备门槛；桌面软件证据不替代。

复现使用标准 `Tools/DotnetHarness`、[真实 API 编译脚本](validation/GenerateCompositionApiCompile.py)、[冷探针步骤](validation/FoundationCompatibilityProbe-20261007.md)，以及三个 `Tools/ci`、`Tools/Acceptance`、`Tools/AudioAuthoring` unittest discovery 目录。原生证据获取遵循 [CiEvidence](CiEvidence.md)。
