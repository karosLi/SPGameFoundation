# 紧凑移动 HUD：3f6362f 原生验收

## 结论与精确范围

本轮具体的“竖屏状态卡过高、引擎计数挤占战斗画面”问题已通过原生测试和实际图片审视。实现为 `eef185f`，集成提交为 `5dfb807`，本次验收源码为 **`3f6362f659cc5bc72955e019cca14010744784e2`**，对应 [Unity CI 37581211270](https://github.com/karosLi/SPGameFoundation/actions/runs/37581211270)。

这不是所有动作、美术或移动设备性能的最终签收：完整 EditMode 仍有两个独立的 separation candidate 精确位模式测试失败；早期 Horde 录像中的重型怪仍有部分被 UI 遮住，不能把“在相机视锥内”写成“全身均可观察”。

## 已验证的改动

- 默认隐藏 `Enemies / Bullets` 诊断行，时间、等级、击杀、目标及武器信息仍可见。
- 守点 / 飞剑普通两行卡为 **110 参考像素**；装备武器三行为 **134 参考像素**，替代旧的 204 像素卡。两行场景减少约 46% 的面板覆盖。
- 生命标签、生命 / 信标条、状态文字使用安全区顶端的固定偏移。普通 720×1280、模拟刘海以及 720×1600 长屏中，文字均在卡片内，未越入右上菜单点击区。
- 诊断开关能显式恢复计数并再次收起，快照逐字节相同；不改模拟和输入状态。
- 两个后端均保留摇杆、技能、充能 / 冷却、瞄准、取消和菜单的真实点击目标，未隐藏 HUD 或放宽原像素阈值。
- CANCEL 继续以象牙白文字标识，图标和环保留珊瑚警示色。

## 原生结果

- graphics PlayMode：**154 passed / 0 failed / 1 explicit skip**。
- EditMode：**1077 passed / 2 failed / 5 skipped**。两项失败是测试候选 `BwBeltSeparationCandidateTests.ImmutableGridParallelCandidateMatchesOrderedReferenceAndReportsCompletedCost(32,True)` 和 `(128,True)` 的 managed/Burst 浮点精确匹配，与本次 HUD 改动无关；不能将整个提交称为全绿或可直接合并。
- `CapturePortraitChargesAimCancelAndSyntheticSafeArea` 的 GPU / DataTexture 两例均通过，包括新卡片高度、文字边界、状态行数、诊断开关、长屏和快照断言。
- `FourWeaponsDrivePortraitHordeAndKeepSkillHud` 两档通过，原生 `CancelKeepsHighContrastTextWhileWarningArtUsesCoral` 和延迟固定 Tick 的 RPG 相机回归也通过。
- 本分支修改后的完整 .NET harness 为 849 passed / 0 failed；集成分支的其他新增测试数量以其综合报告为准。

## 实际图片审视

图片位于恢复的 `Artifacts/Screenshots/MobileHud/`；逐张查看了以下原始 PNG，而非源 sprite 或概念图：

1. `survivor-portrait-compact-restored-{gpu,datatex}.png`：720×1280，诊断开关关闭后的两行状态卡、血条、目标和技能均清楚可读。
2. `survivor-portrait-compact-tall-{gpu,datatex}.png`：720×1600，模拟安全区中卡片、标签、血条和文字没有因屏幕变长而拉散。
3. `survivor-sword-horde-portrait-gpu.png`：160 个敌人的密集场面中，卡片明显比 54477736 的前态矮，保留时间 / 等级 / 剑数 / 波次信息，主角和剑环中心仍可辨。
4. `collision-horde-debug-projectile-gpu.png`：验证武器三行卡完整显示，菜单及四技能分离。此图含开发碰撞标记，只作为开发验收，不作为无调试游戏宣传画面。

干净的 GPU 两行卡原图 SHA-256：`2d6314676e3d1d27b01073a33ff8670da44adb8aa40b1f1d2b130f9fbf643f3f`，1,443,243 字节。原始图片未拼接、补绘或更改内容。

完整归档已取回并验证：**1655 文件 / 19 分片 / 303,898,026 ZIP 字节**，archive SHA-256：`fbbc6d5e19b6aff3104647b34db67758dab42e116e9098aff6533228ed3c45f2`。各文件及分片均与清单匹配。

## 保留的边界

- 密集世界血条仍可能互相覆盖；这次没有隐藏生命信息来制造“干净画面”。
- Horde 重型怪早期录像约 frame 0–32 的 UI 遮挡属于镜头 / 采集布置与可观察性问题，不因本次卡片变小就宣称解决。
- 本次证明的是原生 Unity 的 HUD 布局、渲染和合成触摸 / 点击行为；物理 Android/iOS 的手指覆盖、DPI、发热、电量、持续帧时和内存压力仍待设备验证。
- 动作连续性、弹丸 / 命中与 AI 候选的结论分别看对应精确提交报告。总状态参见 [移动基座后续验收](MobileFoundationFollowupValidation.md)，资源规范参见 [原创美术与移动预算](SanctuaryArtDirection.md)。
