# 移动端共享能力与双玩法检查点（2026-10-06）

## 本轮交付

- 新增 ShooterFoundation，完整竖屏射击流程：拖动/WASD、自动开火、僚机、最近目标持续射线、波次、拾取、三选一升级、胜负/重开/回菜单。
- 在 Survivor 原模拟上增加可选守点规则、真实固定 tick 双环伤害、信标目标、终局流程；经典配置及旧快照保持兼容。
- 共享 Beam/Annulus 数学、导入/超采样平滑 atlas、独立挤出边距、BlobShadow 与 CPU 骨矩阵参考。两种示例的质量预算只减少表现，不改变权威模拟。
- 修复共享 BufferText 缺少 CanvasRenderer 导致数字 HUD 不呈现的问题；加入真实组件和截图字形回归。
- 最小新玩法接入合约见 [MobileGameplayIntegration](MobileGameplayIntegration.md#8-新玩法的最小接入清单)。

当前美术为原创程序占位图。旧 CPU/Burst cutout 骨架和 sprite instancing 不被当作 weighted mesh GPU 蒙皮；后者在独立下一阶段实现。

## 已执行的全量验证

测试源码提交：`3ac28ccc9fa2f4fe1d43607b286eaf2a325a534c`。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| .NET 8 harness，全 69 个生成程序集项目 | 374 通过，2 Explicit 未执行，0 失败；0 编译警告/错误 | FinalStage1/harness-build.log、harness-tests.log、22 个 TRX |
| Unity EditMode | 382 通过，3 Explicit 跳过，0 失败 | FinalStage1/editmode.xml，16:49:03–16:49:19 UTC |
| Unity PlayMode，包括原九玩法和两个新接入 | 67 通过，1 Explicit 跳过，0 失败 | FinalStage1/playmode.xml，16:49:30–16:56:06 UTC |

环境为 Unity 2022.3.62f2 / Linux x86_64 / OpenGLCore Mesa llvmpipe 软件渲染。音频设备未成功初始化，FMOD 使用 nosound 输出；反馈逻辑保持启用。这里验证真实 Unity/Burst/图形路径，但没有 Android/iOS 真机、硬件 GPU 帧时、触屏取消事件的设备传递、发热或电池数据。

### 性能证据的范围

- Shooter 两档渲染分别预热 150 帧，再测 180 帧：此次均 0 分配帧、0 B；既有 ≤2 分配帧预算未改。不能由短窗口推出所有流程零分配。
- Shooter 1,024 目标 × 4,096 扫掠，实际窄相候选 4,290，对比笛卡尔积 4,194,304 为 0.102%；3,036 命中。10 次预热 +30 次采样，真实 Unity 本次 p50 0.421 ms、p95 0.527 ms。仅含网格查询与相对运动精确扫掠，不含网格重建、绘制/上传，也不是整帧或真机加速比。
- 固定 tick 模拟零分配断言、容量耗尽、确定性、快照续算、质量不影响模拟、射击高速穿透与环带边界均有回归。
- 可见数字 HUD 的正常 Survivor 六窗口捕获单独记录于 [分配调查](SurvivorAllocationInvestigation.md)，不使用上述射击短窗口代替。

### 可见 HUD 的独立诊断

正常 Survivor 两档各 3×600 帧捕获 1/1 通过，3,600 帧全部唯一对应，六段 gen0 完成回收增量均为 0。五段预热窗口各 24,244 B，其中 24,000 B 的每帧分配调用栈属于 Unity Test Runner；另 204 B 是升级 UI 字体注册，40 B 是 EventSystem 根 Raycaster 查询。首段额外 Canvas.PreRender 样本部分地址未解析，不全部归为引擎。旧全局 164 B 残余没有被冒充完整解释。

## 截图与呈现检查

- shooter-playing / shooter-upgrade：两档 540×960；测试布置明确设置三个可见敌人、修复与金币拾取。正常模拟仍须击杀并完成胜负流程。检测真实文字像素和射线区域，并人工查看两档四张 PNG。
- guard-portrait / low-quality / loss / victory / menu：两档 720×1280；压力布置 70 个高生命静止目标，检查环带、血条、阴影、画质下保持可读、重开与菜单。
- 最后增加守点专用、raycast=false 的深色统计底板；2026-10-06 17:00:14–17:00:17 UTC 两档针对性 PlayMode 2/2 通过，新增真实字形与背景对比度断言。此增量不改经典 Survivor 或模拟。
- 不把测试布置中的高血量、静止目标和缩短波次当作生产默认难度。

## 已知边界与后续顺序

1. 当前经典 Survivor/RPG 的跨 tick LastHit row、Brawler 的 row-bitmask 存在 swap-back 后身份错误。独立正常模拟复现已确认。下一阶段用有界稳定 handle 历史和版本化接入修复；本检查点没有隐瞒为已解决，也不破坏旧快照来换表面通过。
2. 新共享 ActionTimeline / hit policy 应至少由 Survivor 与 Brawler 各一种配置使用。旧 Brawler 仍是横轴格斗，不宣称已支持纵深动作 RPG。
3. 实际 weighted BAT/GPU IK 与 CPU 回退独立推进；后续再做姿态阴影图集与设备分档。现有 blob 只表示接触，不处理遮挡。
4. Android/iOS 长时热态、低端 GLES 回退和 native 资源峰值仍需要真机验收。

本地提交与恢复包不涉及远程推送或发布。
