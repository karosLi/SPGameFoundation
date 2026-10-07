# SPGameFoundation

> 当前工作分支正在验证新增玩法与自然角色；完成项、实测范围及外部设备阻塞见 [能力验收清单](Docs/RequestedCapabilityChecklist.md)。`dot/mobile-foundation-validated` 只在对应提交的远程 CI 通过后前进，历史冻结记录见 [CurrentWipStatus](Docs/CurrentWipStatus.md)。

Unity 2022.3 2D 单机小游戏基座（面向移动端），保留贪吃蛇、ARPG、幸存者、平台跳跃、塔防、三消、弹弓、格斗、剧情九个验证玩法，新增非像素竖屏射击、守点怪潮、飞剑怪潮与横屏纵深动作变体，用于验证共享能力接入。

- 数据导向模拟：自研 SoA 表 + C# Jobs + Burst，固定步长（默认 30 Hz，动作示例 60 Hz，按模式配置），确定性（同种子同输入 → 同结果）
- 两级空间网格碰撞；7500×7500 大地图 + 3750×3750 小地图（传送门衔接，非活动地图冻结）
- GPU 驱动渲染（Compute 展开蛇身 / 剔除食物 + Indirect Draw），GLES 3.0 自动降级到数据纹理路径
- 节点 / 条带两种蛇身，不透明 / 半透明（同蛇等深度，不叠色）/ 叠加三种混合
- 固定容量、预热与零分配热路径测试；分配/回收的实际范围见调查记录；AI（效用决策 + 上下文避让）、Buff、道具、技能飞行物、加速掉落、回放

设计文档：[Docs/Architecture.md](Docs/Architecture.md) · 品类覆盖与验证玩法：[Docs/GenreCoverage.md](Docs/GenreCoverage.md) · 本轮移动端加固：[Docs/MobileFoundationBatch1.md](Docs/MobileFoundationBatch1.md) · 分配/回收调查：[Docs/SurvivorAllocationInvestigation.md](Docs/SurvivorAllocationInvestigation.md)

**测量说明：** [Unity线程分配API的校准更正](Docs/AllocationMeasurementCalibration.md)。旧同步0B读数尚不能证明Unity零分配；独立Profiler/全帧与逻辑、图形结果分别保留。

## 快速开始

1. 用 Unity **2022.3 LTS** 打开仓库根目录（`Packages/manifest.json` 已列出依赖）。
2. 菜单 **SPF → Snake → Create Or Update Scene**，生成 `Assets/SnakeFoundation/Scenes/Snake.unity`。
3. 打开场景，Play。
   - 桌面：鼠标方向转向，左键 / 空格加速，右键 / E 发射；WASD 也可以
   - 触屏：左半屏虚拟摇杆，右下 BOOST / FIRE 按钮
   - F1：性能面板（各阶段耗时、实体数、GC、Draw Call）

也可以只放一个挂了 `SnakeGameBootstrap` 的空物体，其余（模拟、相机、渲染、UI、输入、自适应画质）运行时自动组装。

## 新玩法接入从这里开始

[中文接入配方](Docs/NewGameplayIntegrationRecipe.md) 从现有 DriftSmoke 最小模块开始，说明数据/系统/表现三层、配置新武器、技能 HUD、稳定命中、容量与保存合约；包含按实际源码编译核对的片段。无需修改公共玩法枚举或复制另一套游戏循环。

## 共享战斗与角色后端

- [共享稳定命中、动作时间线与实测结果](Docs/MobileSharedCombatAndBatCheckpoint.md)
- `BwGameBootstrap.CreateSharedCombat()` 与 `SvGameBootstrap.CreateCrossedBladeExample()` 是两个选配接入，不修改经典保存结构。
- **SPF → Characters → Create Or Update Weighted BAT Scene**：真实 weighted mesh、GPU 矩阵纹理动画/限定两骨 IK 与 CPU 回退；这是独立的三骨加权后端验证场景，支持选配 compute palette（G）与强制 CPU（C），不是任意骨架导入器。

## 新增移动端示例

- **SPF → Weapons → Create Landscape Weapon Brawler Scene / Create Portrait Weapon Horde Scene**：刀、剑、法杖、弓箭四种真实可切换武器；固定 Tick 攻击/拉弓/换装、自然握持 IK、GPU 粒子与 CPU/Burst 回退，保留摇杆和独立技能。Q / SWITCH 换装；[共享武器合约与验证范围](Docs/AuthoritativeWeapons.md)。

- **SPF → Shooter → Create Or Update Scene**：拖动飞行 / WASD、自动射击、僚机、最近目标射线、波次与三选一升级。生成场景后 Play；移动端竖屏设置通过单独的 **Apply Portrait Mobile Settings** 菜单显式应用。
- **SPF → Survivor → Create Guard Example Scene**：守护信标、双环伤害、密集血条、接触阴影与失败/胜利/重开。原 Survivor 默认模式保留。
- **SPF → Survivor → Create Flying Sword Horde Scene**：竖屏优先，环绕/发射/追踪/返回的飞剑、升级与有限波次、共享脉冲/闪现 HUD、受预算约束的轨迹和伤害数字。
- **SPF → Mobile Gameplay → Create Landscape Belt Scroller Scene**：横屏优先，独立地面纵深/跳跃高度、连击/踢击/击飞/受击、拾取与治疗、四技能 HUD。人物与怪物使用同一个自然 cutout 表现适配器。
- [飞剑接入](Docs/FlyingSwordHorde.md) · [纵深动作接入](Docs/LandscapeBeltScroller.md) · [实际玩法自然角色](Docs/GameplayNaturalCharacters.md) · [可选 compute 骨骼后端](Docs/BatComputePaletteValidation.md)
- [本轮验证结果](Docs/MobileGameplayCheckpoint.md) · [共用能力与新玩法接入](Docs/MobileGameplayIntegration.md) · [射击验证](Docs/ShooterValidation.md) · [守点验证](Docs/HordeGuardValidation.md)

两档渲染均有真实 Unity 测试与截图；美术是原创程序占位图。桌面软件渲染证据不代表 Android/iOS 真机性能。

## 共享移动技能 HUD（可选示例）

- **SPF → Mobile Skill HUD → Create Landscape Brawler Scene**：可见摇杆、按住连拳、双充能踢击，沿用真实骨骼攻击与命中。
- **SPF → Mobile Skill HUD → Create Portrait Horde Scene**：可见摇杆、范围脉冲、拖动瞄准/松手闪现与取消，冷却和充能由固定 Tick 技能状态驱动。
- 两例共用安全区域布局和输入控件，适配实际横竖屏，不自动修改全局移动端方向设置。旧玩法入口与快照保持隔离。
- [接入合约与验证边界](Docs/MobileSkillHud.md)

## 目录与程序集

| 目录 | 程序集 | 内容 |
| --- | --- | --- |
| `SinglePlayerFoundation/Contracts` | `SPF.Contracts` | 句柄、访问键、阶段、随机、并行队列、资源钩子 |
| `SinglePlayerFoundation/L1Simulation` | `SPF.L1Simulation` | 轨迹 / 蛇身（Slab 池）、转向、两级网格、分块 |
| `SinglePlayerFoundation/L2Gameplay` | `SPF.L2Gameplay` | Buff、成长曲线、头对头规则、分块种群、技能 / 道具定义 |
| `SinglePlayerFoundation/Runtime/World`+`Scheduling` | `SPF.Runtime.Core` | SimWorld、SoA 表、注册表、事件队列、快照、Tick 管线、依赖追踪 |
| `SinglePlayerFoundation/Runtime` | `SPF.Runtime` | 模块组装、Session、SessionHost、PerfHud |
| `SinglePlayerFoundation/Presentation` | `SPF.Presentation` | 两档渲染：CircleBatch、PointCloud、ChainRenderer、Shader / Compute |
| `SnakeFoundation/Runtime` | `SnakeFoundation.Runtime` | 贪吃蛇模块：配置、14 个系统、AI、地图衔接、回放 |
| `SnakeFoundation/Presentation` | `SnakeFoundation.Presentation` | 相机、世界渲染器 |
| `SnakeFoundation/Game` | `SnakeFoundation.Game` | 启动引导、UI、输入、自适应画质 |
| `SnakeFoundation/Editor` | `SnakeFoundation.Editor` | 一键建场景、移动端 Player 设置、CI 入口 |
| `*/Tests/EditMode`、`Tests/PlayMode` | 测试程序集 | 逻辑测试、UI 自动化 |

依赖方向由 asmdef 强制：Contracts ← L1 ← L2 ← 玩法；Presentation 只读模拟数据。

## 渲染管线说明

Shader 为无光照 CGPROGRAM，内置管线与 URP 均可用（URP 下走 SRPDefaultUnlit）。若使用 URP，请保持 Renderer 的 **Depth Priming 关闭**（这些 Shader 没有 DepthOnly Pass，强制 Depth Priming 会导致不透明物体被深度测试剔除）。

## 测试

| 方式 | 需要 | 覆盖 |
| --- | --- | --- |
| `Tools/DotnetHarness/run.sh` | .NET 8 | 按 asmdef 生成的工程 + Unity API 桩，编译全部程序集（分层与 Unity 一致），运行 EditMode 逻辑测试（当前数量见移动端加固验证记录） |
| Unity Test Runner（EditMode + PlayMode） | Unity 2022.3 | 以上 + 真实 Job / Burst + PlayMode UI 自动化（菜单、摇杆、加速、技能、死亡、重开、回菜单、换皮肤、传送门、1 分钟浸泡、两档渲染） |
| GitHub Actions | 仓库 Secrets `UNITY_LICENSE` 或 `UNITY_EMAIL`+`UNITY_PASSWORD` | `harness.yml` 每次推送都跑；`unity.yml`（GameCI Docker）手动触发 |
| 自托管 Runner（`unity-self-hosted.yml`） | 本机 Unity 2022.3.62f2 + 标签 `unity` 的 Runner，仓库变量 `UNITY_SELF_HOSTED=true` | 每次推送在本机跑 EditMode + PlayMode，结果/日志作为 artifact 上传；运行各玩法的真实 Unity 回归；当前基线与本轮结果见移动端加固验证记录 |

.NET 测试工程只验证逻辑、确定性和我们自己代码路径的 GC；Burst 编译、Job 安全检查、Shader 编译与真实渲染需要在 Unity 中验证。
测试宿主使用非并发 GC，避免后台回收导致线程分配字节计数误报；零分配阈值与校准保持不变，不能据此推断生产环境的回收频率或停顿。见 [计数调查与复现](Docs/HarnessAllocationAccounting.md)。
测试桩里的 `IJobParallelFor` 每次调度都以不同的排列顺序执行下标，用来暴露依赖并行写入顺序的代码（真实 Worker 线程顺序不确定）。

自托管 Runner 注意事项（`Tools/ci/local-unity-tests.sh` 已自动处理）：
- Apple Silicon 上若装的是 x64 Runner，它运行在 Rosetta 下，子进程 Unity 也会以 x86_64 运行，Burst JIT 代码会被 macOS 以 “Code Signature Invalid” 杀掉。脚本用 `arch -arm64` 原生启动 Unity；更推荐直接安装 `osx-arm64` 版 Runner。
- Unity 被杀后残留的子进程会占住项目锁，脚本在每次运行前清理，并清空 `Library/BurstCache`。
- 若 Unity 仍被信号杀掉，会以 `--burst-disable-compilation` 重跑一次，并打印崩溃原因与失败用例的消息/堆栈。

## 写一个新玩法模块

```csharp
public sealed class MyModule : GameplayModuleAsset
{
    public override void DeclareData(WorldLayout layout) =>
        layout.Table(MyKeys.Thing, 4096).Column(MyKeys.Position);

    public override void RegisterSystems(SystemRegistry registry) =>
        registry.Add(new MoveThingsSystem());
}

sealed class MoveThingsSystem : SimSystemBase
{
    public override SimPhase Phase => SimPhase.Move;
    public override void Declare(AccessDeclaration access) => access.Write(MyKeys.Position);
    public override JobHandle OnTick(in SimContext ctx, JobHandle dependency) =>
        new MoveJob { Positions = ctx.Column(MyKeys.Position), Dt = ctx.Time.DeltaTime }
            .Schedule(ctx.Count(MyKeys.Thing), 256, dependency);
}
```

规则：在 `Declare` 中声明读写 → 调度器自动串联 / 并行 Job；结构变更（创建 / 销毁）只在 `ApplyCommands` 阶段；Job 中销毁用 `SimWorld.DestroyQueueKey` 的 Writer；主线程在 tick 进行中访问数据前先 `session.Sync()`。
