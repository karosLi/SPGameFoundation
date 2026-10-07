# 各玩法动画接入审计

日期：2026-10-07。范围为十个 runtime family：九个原有玩法加 Shooter；守点、飞剑、纵深动作是 Survivor / Brawler 的变体，不重复计数。本文件按实际模拟数据和当前 renderer 源码区分已接入、此提交新增与不适用能力。它不是“十个玩法都改成人形骨骼”的完成声明。

## 接入选择

| 玩法 | 真实状态 / 权威输入 | 当前表现后端 | 本次接入与边界 | 相关验证入口 |
| --- | --- | --- | --- | --- |
| Brawler / 横屏纵深动作 | `FighterState` Idle / Walk / Attack / Hit / KO；`AttackKind` 和动作时长；纵深模式 `Ground` / `PreviousGround` / `GroundVelocity`、独立跳跃高度；选配 `WeaponRuntime.View(alpha)` | 经典 `SkeletonPoseJob`；自然角色选配 `GameplayCharacterPresenter`，读取地面、速度、攻击阶段、握持目标 | 已有自然角色和武器接入。共享 motion profile / skill pose 合约承载进一步角色差异；具体人物 IK、角色 profile 和武器改动属于并行的自然角色提交，本 sprite 提交不改这些文件。不从画面推断伤害时点 | `BwNaturalGameplayTests`、`BwWeaponGameplayTests`、`BwBeltScrollerPlayTests` |
| Survivor / 守点 / 飞剑 | `HeroPrev` / `Hero`、敌人前后位置和 handle、受击 / 死亡、真实武器阶段；飞剑独立飞行 / 返回数据 | 普通怪物保留 sprite / smooth blob；预算内选中角色由 `GameplayCharacterPresenter` 绘制；飞剑有独立轨迹和投射物表现 | 已有两类角色后端共存。步态按角色实际速度分类；人形武器和技能覆盖由自然角色提交完善。不能对所有小怪无条件增加骨骼，也不把飞剑当四肢 | `SvNaturalGameplayTests`、`SvWeaponGameplayTests`、`SvFlyingSwordPlayTests`、`SvAllocationCaptureTests` |
| RPG | `Position` / `PrevPosition` / fixed step；hero analog intent、AI 巡逻 / chase 导致的实际速度；`ActionPhase` None / Windup / Recover / Cast / Channel / Dash / Stagger，dead flag，`PhaseSkill` | `RpgArt` atlas + `RpgWorldRenderer`，按模拟阶段采样 sprite，武器独立 overlay | **此提交新增** Idle / Walk / Run 速度滞回与 walk↔run 连续归一化脚步相位；各 humanoid 和 slime 独立 run strip；Projectile / Nova / Whirlwind / Warden Slam 使用不同 pose。Cast 进度读取模拟 `PhaseProgress`，Channel 读取 `PhaseTime`。技能 enum 没有 Heal；不虚构治疗施法状态。每帧手部 anchor 与对应 sprite 的作者姿态一致，保留弓、杖、矛、斧、锤 overlay；这不是完整人形 IK | `RpgSpriteStateTests`、`SpriteTierTests.GameplayWalkRunCastPauseAndRebind`，原 `RpgUITests` / bot / 两档 sprite 比较 |
| Platformer | `Motor.Velocity` / `Motor.Grounded`、`Riding`，`PlFlow.Dying`、walker `Dead` | `PlArt` atlas + `PlRenderer` | **此提交新增**独立 Walk strip，四个不同 Run 接触 / 过渡姿态，腿臂反向摆动；按实际横向速度滞回分类并保持步态相位。Jump / Fall 仍读取真实 grounded、riding 和竖直速度；死亡闪烁、walker squash 保留。不增加攻击 / 武器状态 | `PlSpriteStateTests`、`PlPlayTests.LocomotionTransitionsPauseAndRebind`；原 static tile / no-GC / night 测试不放宽 |
| Shooter | `HeroPrevious` / `Hero`、`Invulnerable`、`Wingman`、`BeamTargetId` / `BeamEnd`、敌人 flash 和投射物 velocity | 飞机 / drone sprite、飞行方向 / 轻微滚转，wingman 跟随、beam 和 engine strip quads | **审计，未改动**。当前飞机 bank 来自 hero 横向 tick 位移；引擎为条形 quad，并非人物 run clip。适用的是 bank / thrust / hit / weapon effect；如未来优化，bank 可按 tick 时长归一化并平滑。没有站立、走路、脚锁定或人手 IK 的意义 | `ShooterPlayTests`、`CombatVfxVisualTests`、`ShooterVfxIsolationTests` |
| Defense | 路径敌人 `PrevPosition` / `Position`、`Slow` / hp；tower `Kind` / `Aim` / `Level`、shot kind | 敌人 `SpriteClip.FrameAt(time + offset)`；炮台底座与旋转炮管独立绘制 | **审计，未改动**。适用的是敌人行进节奏、减速反馈、炮塔追踪和开火。当前敌人 strip cadence 仍按表现时间，未改成此提交的速度 clock；未来可按实际位移调速，炮台需要自身 recoil profile。没有可复用的人手握持骨架 | `TdPlayTests`、`TdHudTests` |
| Snake | `HeadPrev` / `HeadCurr`、`ArcPrev` / `ArcCurr`、trail points、保护 / 增长等 gameplay 数据 | `ChainRenderer` 节点 / 条带插值，GPU 或 fallback | **审计，未改动**。实际身体是一条连续轨迹，位置和弧长插值才是运动连贯性接入点。装饰性的保护闪烁与链体运动分开。Walk / Run strip 和 humanoid IK 不适用 | `RenderTierTests`、`VisualRegressionTests`、`SnakeReplayTests`、`RenderPerformanceTests` |
| Sling | `SlFlow.Aiming`、aim pull、真实 rigid body `Position` / `Angle`、damage / alive | `SlRenderer` 直接使用物理姿态，皮筋两条带与预测抛物线，命中 / 碎裂效果 | **审计，未改动**。投射、旋转、碰撞、断裂是相应动作。保留模拟作为物体朝向权威，不能用人物 gait 覆盖 rigid-body angle | `SlTests`、`SlPlayTests` |
| Puzzle（三消） | `BoardSerial` / `LogSerial`、`M3EventKind` Swap / SwapBack / Clear / MakeBomb / Fall / Spawn / Shuffle 以及 cascade `Step` | `M3Renderer.Play` 将事件序列转换为有限 tween：swap InOutQuad、clear 放大收缩、fall / spawn OutBounce | **审计，未改动**。适用的是移动事件顺序、落子时间和取消 / 重建，不是人形 action state。共享 `Tweens` 已满足后端需求；不能让实时步态 clock 驱动手动模拟的棋盘推进 | `M3Tests`、`M3PlayTests` |
| Story | `InStory` / `MiraShown` / `Face` / `Background`；真实 `Expression` | `StRenderer` 表情 portrait atlas、淡入 / 平移 / 轻微 bob、背景星光 | **审计，未改动**。当前角色是 portrait，没有行走或武器骨架。接入点是表达式与剧情节点之间的过渡；以后若需要可增加眼神 / 嘴型层，但当前没有这些权威数据，未宣称已实现 | `StTests`、`StPlayTests` |

源码入口分别为 `Assets/<Family>Foundation/Presentation/` 下的 `BwRenderer.cs`、`SvRenderer.cs`、`RpgWorldRenderer.cs`、`PlRenderer.cs`、`ShooterRenderer.cs`、`TdRenderer.cs`、`SnakeWorldRenderer.cs`、`SlRenderer.cs`、`M3Renderer.cs`、`StRenderer.cs`。

## Sprite 接入合约

- `GameplayLocomotionClassifier` 只分类速度，不引用骨架。`SpriteLocomotionClock` 是一个小的表现侧 value type：同一 `Phase` 采样不同 Walk / Run strip，速度变化只调整推进速率。run enter / exit 为参考全速的 0.72 / 0.60，idle enter / exit 为 0.08 / 0.045 world units/s。不以“换动画”为由把相位归零。
- RPG 参考速度取 hero / monster 配置的基础全速；移动量取模拟前后位置除以真实 fixed step，而不是渲染位移除帧时长。碰墙 / 被减速不会以全速摆腿；AI 低速巡逻和追逐可落入不同视觉状态。动作段覆盖 locomotion，技能姿态不更改攻击判定、冷却、事件或存档布局。
- Platformer 横向速度来自 `Motor.Velocity.x`，最大参考速度来自 `Tuning.RunSpeed`。站在移动平台上时，由 `Riding >= 0` 保持 grounded，平台搬运不会误判成 hero 自己跑动。
- 两个 renderer 的表现 dt 均在 `SimSession.State != Running` 时变成 0，包含手动 pause 与 host 生命周期暂停；不用全局 `Time.timeScale`。RPG 的 death、环境 sprite、状态粒子和 dash ghost 同步冻结；Platformer 的 actor、coins、flag、torches 和 effect clock 同步冻结。其他六个非人形 renderer 的时钟未在本提交全局改写。
- RPG 每个 registry slot 的 generation 改变会清除旧步态与死亡时钟；floor rebuild / restore 清除 view history。两个 renderer 重新绑定 session 时重置时钟；Platformer 关卡重建重置 hero gait。表现状态不进入 gameplay snapshot。
- 角色帧选择与手部 anchor 使用同一组 pixel pose 参数，精灵镜像和 elite 尺寸同步应用到手部与武器。保留现有武器种类和按阶段旋转 / 矛柄滑动；像素锚点不是两骨 IK，也不证明所有姿态下武器已经经过视觉验收。

## Atlas 与预算

默认六种 RPG 怪物的旧内容为 228 帧，新内容为 263 帧：七个角色各 4 帧 Run，加 hero 的 3 帧 Nova / 4 帧 Whirlwind。Warden 用其既有 3 帧 Cast 改作 Slam，未为每个不会施法的怪物复制全部技能 strip。

源码尺寸清单按 `SpriteAtlasBuilder` 的稳定高度排序 / shelf packing 复算：

| Atlas | 原帧数 → 新帧数 | 原纹理 → 新纹理 | RGBA32 纹理空间增量 |
| --- | --- | --- | --- |
| RPG（6 kinds） | 228 → 263 | 1024×256 → 1024×256 | 0；维持 1,048,576 bytes |
| Platformer | 31 → 35 | 512×64 → 512×64 | 0；维持 131,072 bytes；normal atlas 同尺寸 |

Platformer 明确用 512 像素 shelf 宽度，新增 Walk 放到第二行；继续默认 1024 宽会因跨过 power-of-two 宽度而无谓翻倍。新增帧仅增加 build 阶段画布 / 元数据；热路径仍复用固定 batch 与 value state，没有每帧生成 atlas、数组或动画对象。新增测试锁定纹理尺寸和帧数，原 no-GC、静态瓦片上传、帧预算断言保持原值。

## 验证与证据边界

新增 EditMode 测试覆盖：速度阈值滞回、Walk↔Run 相位连续、30 / 120 FPS 下相同推进量、air / disabled 时冻结步态、pause 时冻结时钟、generation 回收、动作优先级 / 技能映射、frame anchor 和 atlas 上限。真实 Unity 分支另外读取像素，要求每个 Run 帧互不重复，Platformer Walk / Run 八帧互异。

新增 PlayMode 测试在 GpuDriven / DataTexture 两档启动真实游戏，用输入驱动 Idle→Walk→Run，Platformer 再 Jump→Fall，RPG 再 projectile Cast；检查 paused snapshot 不变、时钟不动、session rebind 重置，并写出 `Artifacts/Screenshots/platformer-motion-<state>-<tier>.png`、`rpg-motion-<state>-<tier>.png`。截图时只暂停已进入的真实模拟状态，不制造独立陈列 pose。

本提交准备期间本地 .NET 测试启动遭遇 MSBuild `NamedPipeServerStream` 的 `SocketException (13): Permission denied`，未绕过权限重试。上述测试及真实 shader / Burst / 图像结果须由集中 Mac Unity CI 对最终集成 SHA 执行；源码 packing 复算不是运行时纹理验收，测试代码不是通过记录，静态截图也不是完整动作自然度视频证据。
