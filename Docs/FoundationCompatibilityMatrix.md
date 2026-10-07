# 共享基座兼容矩阵：Stage A 源码基线

日期：2026-10-07（UTC）。本表落实[分层语义扩展计划](SharedFoundationSemanticExtensionPlan.md)的 P0 / 阶段 A，记录实际已存在的组合与兼容边界，不引入新运行时协议，不修改旧玩法、默认配置或 fixture。

## 1. 基线、范围与证据分层

- 源码固定为本地 `62c5b7f5765a879aaf8ec9fa69bc7601ae3fa969`，树 `04435658d8b1c2e95e286b093405141b5fd6040b`。已发布的 `e86ee87a7691282ae7e96713c315dbec5998c414` 使用同一树；映射和既有 CI 结果见[闭环记录](validation/NativePrecisionClosure-20261007.json)与[验证报告](MobileFoundationFollowupValidation.md)。本分支随后仅接入 `de755e1` 的闭环文档，`Assets` / `Tools` 相对源码基线无差异。
- 覆盖九个经典玩法、四个新增移动变体，另列六个会改变组合的 shared-combat / mobile / weapon opt-in。下文的 `snake.classic` 等只是本次盘点标签，**不是运行时 ModeId、schema ID 或兼容性承诺**；现有 ModeDefinition 没有这些稳定模式标识。
- 容量为所列默认工厂的声明/分配容量，不是活跃实体数、推荐负载、设备预算或实际峰值。自定义 Config、Editor 资产、测试缩小容量均须另列配置；不能直接套用本表。
- 采用源码审查与临时只读 .NET 组合探针：调用真实 Mode 工厂和 `SimSession.Create`，不启动 Tick，读取世界/管线组成；19 种组合均成功创建与释放，两次独立进程输出一致。探针使用项目 Unity 桩，**没有验证原生 Job、GPU、触摸、内存峰值或真机性能**。探针只作为[可复现文档诊断](validation/FoundationCompatibilityProbe-20261007.md)保留，未加入运行时或 Unity Tests。
- [机器可读清单](validation/FoundationCompatibilityInventory-20261007.json)保留默认工厂、按序表/列/资源/系统、容量、源码 SHA-256 和 fixture 方法索引，便于下一阶段有界对照。它是盘点证据，不是新生成的游戏 golden snapshot，也没有把缺失的历史 fixture 补成“通过”。

## 2. 所有组合共同遵守的现有契约

1. 模块按 ModeDefinition.Modules 顺序 DeclareData，再按同顺序 RegisterSystems。模块 Id 默认是类型短名，只有 Snake 显式覆盖为 `Snake`。重复 Id 被拒绝；相同 TableKey 再声明只提升到最大容量、追加未重复列。[IGameplayModule.cs](../Assets/SinglePlayerFoundation/Runtime/Composition/IGameplayModule.cs)、[WorldComposer.cs](../Assets/SinglePlayerFoundation/Runtime/Composition/WorldComposer.cs)、[WorldLayout.cs](../Assets/SinglePlayerFoundation/Runtime/World/WorldLayout.cs)
2. AccessKey.Id 是进程内分配序号，不能作为持久内容 ID。实体是 `EntityHandle(Index, Generation)`，所属 World/Session 不在该值中；跨 Session 的相同 handle 不是同一个实体。H 表使用 registry handle 与 swap-back 行；P 表无 handle，依赖 dead flags 和下一 Tick 的稳定压缩；行号不是长期身份。[AccessKey.cs](../Assets/SinglePlayerFoundation/Contracts/AccessKey.cs)、[EntityHandle.cs](../Assets/SinglePlayerFoundation/Contracts/EntityHandle.cs)、[SimTable.cs](../Assets/SinglePlayerFoundation/Runtime/World/SimTable.cs)
3. 下文 S 表示未声明 LevelScoped，L 表示 ClearLevel 会清表/重置该资源；**S 不等于永久保存，L 不等于独立子 World**。所有资源属于 SimWorld，IDisposable 由其 Dispose；Restart 清全部表并调用已有 reset hooks。游戏自己的 Flow 也可能主动重置 S 资源。[SimWorld.cs](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs)
4. 每个 World 都先装 `World.DestroyQueue`（S、有保存和 reset hook）。Mode 设置默认容量 4096；Snake 取 `max(settings, EventQueue)`，RPG 取 `max(settings, Actors + Projectiles)`，Survivor **覆盖为 Enemies**。其余模块保持设置值。默认有效值在下表单列。
5. 管线按 `SimPhase → Order → 注册位置` 稳定排序；每 Tick 先 PlaybackDestroys、CompactPools，再执行系统。空访问声明是主线程 barrier。名字相同但命名空间不同的 `FlowSystem` 等不是同一系统。[SimPhase.cs](../Assets/SinglePlayerFoundation/Contracts/SimPhase.cs)、[TickPipeline.cs](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs)
6. 实时钟以 `1 / TickRate` 积分，每帧超过 MaxTicksPerFrame 的积压被丢弃；SessionHost 负责 Sync/帧重叠与失焦、后台、禁用暂停，恢复首帧丢弃后台时差。ManualClock 只消费 RequestTicks，仍受每帧上限；直接 Step 是手动一步入口。Puzzle/Story 的 **Mode 工厂并不设置 ManualClock，Bootstrap 才设置**。[ModeDefinition.cs](../Assets/SinglePlayerFoundation/Runtime/Composition/ModeDefinition.cs)、[FixedStepClock.cs](../Assets/SinglePlayerFoundation/Runtime/Scheduling/FixedStepClock.cs)、[SimSession.cs](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs)、[SessionHost.cs](../Assets/SinglePlayerFoundation/Runtime/Session/SessionHost.cs)

## 3. 工厂、模块顺序和时钟

默认设置中的 DestroyQueueCapacity 均为 4096；“销毁队列”列是模块覆盖后的实际分配值。方法参数中的 out 值是临时模块资产，拥有它们的 Bootstrap/测试还负责释放资产本身。

| 盘点标签 / 入口 | 实际模块 Id（声明顺序） | TickRate / 每帧上限 / 驱动 | 销毁队列 |
| --- | --- | --- | --- |
| `snake.classic` · [Snake 经典](../Assets/SnakeFoundation/Runtime/SnakeGameModule.cs)<br>`SnakeGameModule.Create(SnakeConfig.CreateDefault()) + ModeDefinition.Create(..., SessionSettings.Default)` | `Snake` | 30 Hz / 3 / 固定实时 | 4096 |
| `rpg.classic` · [RPG 经典](../Assets/RpgFoundation/Runtime/RpgModules.cs)<br>`RpgMode.Create(RpgConfig.CreateDefault(), out modules)` | `RpgCoreModule` → `RpgDungeonModule` → `RpgActorModule` → `RpgRewardsModule` | 30 Hz / 3 / 固定实时 | 4096 |
| `survivor.classic` · [Survivor 经典](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateDefault(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 4096 |
| `platformer.classic` · [Platformer 经典](../Assets/PlatformerFoundation/Runtime/PlModule.cs)<br>`PlMode.Create(out module)` | `PlModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `defense.classic` · [Defense 经典](../Assets/DefenseFoundation/Runtime/TdModule.cs)<br>`TdMode.Create(out module)` | `TdModule` | 30 Hz / 3 / 固定实时 | 4096 |
| `puzzle.classic` · [Puzzle 经典](../Assets/PuzzleFoundation/Runtime/M3Module.cs)<br>`M3Mode.Create(out module)` | `M3Module` | 30 Hz / 3 / Bootstrap 手动 | 4096 |
| `sling.classic` · [Sling 经典](../Assets/SlingFoundation/Runtime/SlModule.cs)<br>`SlMode.Create(out module)` | `SlModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `brawler.classic` · [Brawler 经典](../Assets/BrawlerFoundation/Runtime/BwModule.cs)<br>`BwMode.Create(out module)` | `BwModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `story.classic` · [Story 经典](../Assets/StoryFoundation/Runtime/StModule.cs)<br>`StMode.Create(out module)` | `StModule` | 30 Hz / 3 / Bootstrap 手动 | 4096 |
| `shooter.default` · [Shooter](../Assets/ShooterFoundation/Runtime/ShooterModule.cs)<br>`ShooterMode.Create(ShooterConfig.CreateDefault(), out module)` | `ShooterModule` | 30 Hz / 3 / 固定实时 | 4096 |
| `survivor.guard` · [Guard](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateGuardExample(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 1024 |
| `survivor.flying_sword` · [Flying Sword Horde](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateFlyingSwordExample(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 1024 |
| `brawler.belt` · [Belt Scroller](../Assets/BrawlerFoundation/Runtime/BwModule.cs)<br>`BwMode.CreateBeltScroller(BwBeltConfig.Default, out module)` | `BwModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `survivor.crossed_blades` · [Crossed Blades（选配）](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateCrossedBladeExample(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 4096 |
| `survivor.mobile` · [Survivor Mobile（选配）](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateMobileCombatExample(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 1024 |
| `survivor.weapons` · [Survivor Weapons（选配）](../Assets/SurvivorFoundation/Runtime/SvModule.cs)<br>`SvMode.Create(SvConfig.CreateWeaponCombatExample(), out module)` | `SvModule` | 30 Hz / 3 / 固定实时 | 1024 |
| `brawler.shared_combat` · [Brawler Shared（选配）](../Assets/BrawlerFoundation/Runtime/BwModule.cs)<br>`BwMode.CreateSharedCombat(BwSharedCombatConfig.Default, out module)` | `BwModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `brawler.mobile` · [Brawler Mobile（选配）](../Assets/BrawlerFoundation/Runtime/BwModule.cs)<br>`BwMode.CreateMobileCombat(out module)` | `BwModule` | 60 Hz / 4 / 固定实时 | 4096 |
| `brawler.weapon_belt` · [Weapon Belt（选配）](../Assets/BrawlerFoundation/Runtime/BwModule.cs)<br>`BwMode.CreateWeaponBelt(BwBeltConfig.Default, out module)` | `BwModule` | 60 Hz / 4 / 固定实时 | 4096 |

种子与工厂分开：本次组成探针统一使用 seed 123；ModeDefinition 本身不保存 seed。默认 Bootstrap 中 Snake 未传 seed 时使用 Environment.TickCount，显式 seed 才关闭随机；RPG Session seed 默认 1，但 NewGame() 另生成 run seed，NewGame(uint) 才固定该种子。Puzzle/Shooter 默认 17，其余默认 1。兼容重放必须同时固定实际 session/run seed、配置与输入，不能把默认工厂视为固定挑战。

## 4. 表、列、容量和身份

表和列严格按声明顺序列出；完整类型名在机器清单。P 表额外存在隐含的首列 byte dead flag，以下用 `$dead` 表示，不能在旧保存布局中漏掉它。H 表的 handles 独立于显式列保存。资源持有的单英雄、棋盘、物理 body 或飞剑并不会自动变成 SimTable。

| 组合 / 表 | 容量 | 身份 / scope | 有序列（省略表名前缀） |
| --- | --- | --- | --- |
| Snake 经典 · `Snake` | 320 | H / S | `Head`, `PrevHead`, `Heading`, `Speed`, `Mass`, `Radius`, `Length`, `Trail`, `PrevArc`, `Bounds`, `BoundsVersion`, `Info`, `Control`, `Contact`, `AI`, `Buffs`, `Stats` |
| Snake 经典 · `Food` | 24000 | H / S / TrackChangedRows | `Position`, `Info` |
| Snake 经典 · `Prop` | 512 | H / S | `Position`, `Info` |
| Snake 经典 · `Projectile` | 512 | H / S | `Position`, `State` |
| RPG 经典 · `Rpg.Prop` | 256 | H / L | `Position`, `Info` |
| RPG 经典 · `Rpg.Actor` | 512 | H / L | `Position`, `PrevPosition`, `Facing`, `MoveIntent`, `Info`, `Health`, `Mana`, `Loadout`, `BaseStats`, `Stats`, `Mods`, `Combat`, `Brain`, `Status` |
| RPG 经典 · `Rpg.Projectile` | 256 | H / L | `Position`, `Prev`, `Info` |
| RPG 经典 · `Rpg.Item` | 512 | H / L | `Position`, `Info` |
| Survivor 经典 · `Sv.Enemy` | 4096 | H / L | `Position`, `PrevPosition`, `Info` |
| Survivor 经典 · `Sv.Bullet` | 32768 | P / L | `$dead`, `Position`, `Info` |
| Survivor 经典 · `Sv.Gem` | 8192 | P / L | `$dead`, `Position`, `Info` |
| Platformer 经典 · `Pl.Walker` | 128 | H / L | `Position`, `Prev`, `Info` |
| Platformer 经典 · `Pl.Platform` | 64 | H / L | `Position`, `Prev`, `Info` |
| Platformer 经典 · `Pl.Coin` | 512 | P / L | `$dead`, `Position` |
| Defense 经典 · `Td.Enemy` | 1024 | H / L | `Position`, `PrevPosition`, `Info` |
| Defense 经典 · `Td.Tower` | 256 | H / L | `Info` |
| Defense 经典 · `Td.Shot` | 2048 | P / L | `$dead`, `Position`, `Info` |
| Puzzle 经典 | 无 SimTable | 见资源状态 | — |
| Sling 经典 | 无 SimTable | 见资源状态 | — |
| Brawler 经典 · `Bw.Fighter` | 64 | H / L | `Position`, `Prev`, `Info`, `Anim` |
| Story 经典 | 无 SimTable | 见资源状态 | — |
| Shooter · `Shooter.Enemy` | 512 | P / L | `$dead`, `Position`, `Previous`, `Info` |
| Shooter · `Shooter.Bullet` | 4096 | P / L | `$dead`, `Position`, `Previous`, `Info` |
| Shooter · `Shooter.Pickup` | 512 | P / L | `$dead`, `Position`, `Info` |
| Guard · `Sv.Enemy` | 1024 | H / L | `Position`, `PrevPosition`, `Info` |
| Guard · `Sv.Bullet` | 4096 | P / L | `$dead`, `Position`, `Info` |
| Guard · `Sv.Gem` | 2048 | P / L | `$dead`, `Position`, `Info` |
| Flying Sword Horde · `Sv.Enemy` | 1024 | H / L | `Position`, `PrevPosition`, `Info` |
| Flying Sword Horde · `Sv.Bullet` | 1024 | P / L | `$dead`, `Position`, `Info` |
| Flying Sword Horde · `Sv.Gem` | 2048 | P / L | `$dead`, `Position`, `Info` |
| Belt Scroller · `Bw.Fighter` | 64 | H / L | `Position`, `Prev`, `Info`, `Anim`, `BeltGround.V1`, `BeltPreviousGround.V1`, `BeltMotion.V1` |

六个附加 opt-in 的表差异：Crossed Blades 与经典 Survivor 表相同；Survivor Mobile / Weapons 与 Guard 的默认表容量相同；Brawler Shared / Mobile 与经典 Fighter 表相同；Weapon Belt 与 Belt 的七列表相同。**表相同不代表资源、系统、规则或保存布局相同**。不同配置的容量仍按工厂实参计算。

列/类型定义：[SnakeKeys.cs](../Assets/SnakeFoundation/Runtime/SnakeKeys.cs), [SnakeData.cs](../Assets/SnakeFoundation/Runtime/SnakeData.cs), [RpgKeys.cs](../Assets/RpgFoundation/Runtime/RpgKeys.cs), [RpgData.cs](../Assets/RpgFoundation/Runtime/RpgData.cs), [SvKeys.cs](../Assets/SurvivorFoundation/Runtime/SvKeys.cs), [SvData.cs](../Assets/SurvivorFoundation/Runtime/SvData.cs), [PlData.cs](../Assets/PlatformerFoundation/Runtime/PlData.cs), [TdData.cs](../Assets/DefenseFoundation/Runtime/TdData.cs), [BwData.cs](../Assets/BrawlerFoundation/Runtime/BwData.cs), [ShooterData.cs](../Assets/ShooterFoundation/Runtime/ShooterData.cs)

Shooter 的 pooled enemy 使用 `ShooterEnemy.Id = ++NextEnemyId` 作游戏内目标编号；它不是 EntityHandle generation。Sling 的 body id 索引 PhysicsWorld2D 和 SlGameState 的 Kind/Hp 数组，也不是 World registry handle。Survivor 与 Platformer 的 hero 在 GameState，Shooter 的 hero 在 Run；Brawler 的 hero 则是 Fighter 表中 Team=0 的普通 handle 实体。SvWeapons.Owner 与 SvRenderer 的 hero 使用 EntityHandle(-1,1) 作为适配层 sentinel，不能交给 registry 解析，也不能单靠它跨 Session 复用 View。[ShooterModule.cs](../Assets/ShooterFoundation/Runtime/ShooterModule.cs)、[ShooterData.cs](../Assets/ShooterFoundation/Runtime/ShooterData.cs)、[SlData.cs](../Assets/SlingFoundation/Runtime/SlData.cs)

## 5. 资源顺序、容量与保存策略

各组按 DeclareData 顺序列出，均在 `World.DestroyQueue` 之后。`Q<T>(N)` 是固定容量 EventQueue。`F` 是 saved:false 反馈：内容不保存、恢复时清空，**但资源名字与 marker 仍在保存流中**。`N` 没有 ISnapshotResource；`SNP` 有该接口，并不保证其中所有字段被保存。空 hook 的派生/静态资源单独说明。所有 scope 以模块声明为准。[EventQueue.cs](../Assets/SinglePlayerFoundation/Runtime/World/EventQueue.cs)

### Snake

- S / N：`Snake.Config`（SnakeRuntimeConfig）、`Snake.Game`（SnakeGameState）、`Snake.Quality`、`Snake.Populations`。默认两 region；种群按 region/chunk 配置创建。
- S / SNP：`Snake.Bodies` 为 `320 × 768 = 245760` trail points，max slab 1024；`Snake.BodyGrid` 为 128×128、cell 8、65536 entries、246080 keys；`Snake.ItemGrid` 为 128×128、cell 8、24512 entries/keys；`Snake.HeadGrid` 为 60×60、cell 125、320 entries。
- S / SNP 队列依次：`Deaths Q<DeathEvent>(320)`、`Eats Q<EatCandidate>(16384)`、`Hits Q<ProjectileHit>(512)`、`FoodSpawns Q<FoodSpawnRequest>(4096)`、`ProjectileSpawns Q<ProjectileSpawnRequest>(512)`、`Feedback Q<FeedbackEvent>(4096), F`、`RemovedItems Q<int2>(4096)`。
- 最后 S / N：`Snake.Signals`（原生 flag/计数）、`Snake.Replay`（36000 ReplayFrame，满后停止记录）。这些未保存的可变资源与系统私有状态是完整 Session 保存覆盖的缺口，见第 8 节。
来源：[SnakeGameModule.cs](../Assets/SnakeFoundation/Runtime/SnakeGameModule.cs)、[SnakeConfig.cs](../Assets/SnakeFoundation/Runtime/SnakeConfig.cs)、[SnakeSpawner.cs](../Assets/SnakeFoundation/Runtime/SnakeSpawner.cs)、[BodyStore.cs](../Assets/SinglePlayerFoundation/L1Simulation/Body/BodyStore.cs)、[Replay.cs](../Assets/SnakeFoundation/Runtime/Replay.cs)

### RPG

顺序：`Rpg.Config` S/N；`Rpg.Game` S/SNP；`Rpg.Feedback` S/F，Q<FeedbackEvent>(2048)；`Rpg.Deaths` L/SNP，Q<DeathEvent>(512)；`Rpg.Map` S/SNP，64×64、tile 1；`Rpg.Flow` S/SNP，同尺寸 FlowField；`Rpg.ActorGrid` S/SNP，32×32、cell 2、512 entries；`Rpg.Hits` L/SNP，Q<HitEvent>(2048)；`Rpg.ProjectileRequests` L/SNP，Q<ProjectileRequest>(256)。地图/Flow/ActorGrid 不声明 LevelScoped，楼层 Flow 系统另行管理；不能因其服务一层就给它们标 L。默认烘焙内容含 6 monster、7 weapon-family、5 skill、37 gear 定义。
来源：[RpgModules.cs](../Assets/RpgFoundation/Runtime/RpgModules.cs)、[RpgConfig.cs](../Assets/RpgFoundation/Runtime/RpgConfig.cs)、[RpgRuntimeConfig.cs](../Assets/RpgFoundation/Runtime/RpgRuntimeConfig.cs)

### Survivor 经典、Guard、Flying Sword 与附加 opt-in

公共顺序：`Sv.Config` S/N；`Sv.Game` S/SNP；`Sv.EnemyGrid` L/SNP（ceil(96/GridCell)²，默认 48×48、cell 2，entries=E）；`Sv.Hits` L/SNP，Q<SvHit>(2V)；`Sv.Deaths` L/SNP，Q<SvDeath>(E)；`Sv.BulletSpawns` L/SNP，Q<BulletSpawn>(V)；`Sv.HeroDamage` L/SNP，Q<float>(V)；`Sv.Collected` L/SNP，Q<int>(G)；`Sv.Feedback` S/F，Q<SvFeedback>(V)。

| 工厂 | E / B / G / V（enemy / bullet / gem / event 容量） | 公共资源之后按序追加的 L/SNP 资源 |
| --- | --- | --- |
| Classic | 4096 / 32768 / 8192 / 16384 | 无 |
| Guard | 1024 / 4096 / 2048 / 4096 | 无；GameState 内启用扩展 payload |
| Flying Sword | 1024 / 1024 / 2048 / 4096 | `Sv.FlyingSwords.V1`：24 blades、288 history handles（24×12）、24 scopes、1024 contact scratch、6 counters；`Sv.WeaponSkillPose.V1`；`Sv.MobileSkills.V1`（2 slots） |
| Crossed Blades | 与 Classic 相同 | `Sv.CrossedBlades.V1`：256 targets、1 scope、2 rejection counters |
| Mobile | 与 Guard 相同 | `Sv.WeaponSkillPose.V1`；`Sv.MobileSkills.V1`（2 slots） |
| Weapons | 与 Guard 相同 | `Sv.WeaponSkillPose.V1`；`Sv.Weapons.V1`（30 Hz、4 profiles、32 projectile slots、33 scopes、4224 history handles=33×128、32 cues）；`Sv.MobileSkills.V1`（4 slots） |

`Sv.Game` 的扩展开关是 `Variant != Classic || AnnularSkill.Enabled || FlyingSwords.Enabled`，不是检查 MobileSkills / CrossedBlades / WeaponCombat。标准 Mobile / Weapons 工厂继承 Guard，所以开启扩展；自定义组合必须检查实际条件。剑 contact scratch 不保存，剩余核心数组/计数按自己的 schema 保存。
来源：[SvModule.cs](../Assets/SurvivorFoundation/Runtime/SvModule.cs)、[SvConfig.cs](../Assets/SurvivorFoundation/Runtime/SvConfig.cs)、[SvGameState.cs](../Assets/SurvivorFoundation/Runtime/SvGameState.cs)、[SvFlyingSwords.cs](../Assets/SurvivorFoundation/Runtime/SvFlyingSwords.cs)、[SvCrossedBlades.cs](../Assets/SurvivorFoundation/Runtime/SvCrossedBlades.cs)、[SvMobileSkills.cs](../Assets/SurvivorFoundation/Runtime/SvMobileSkills.cs)、[SvWeapons.cs](../Assets/SurvivorFoundation/Runtime/SvWeapons.cs)

### Platformer、Defense、Puzzle、Sling、Story、Shooter

| 模式 | 有序资源、scope / 保存 / 容量 |
| --- | --- |
| Platformer | `Pl.Game` S/SNP（单 hero/motor）；`Pl.Map`、`Pl.Hazards` 均 S/SNP、96×24、tile 1；`Pl.Feedback` S/F、256 |
| Defense | `Td.Game` S/SNP；`Td.Rules` S/N（3 tower / 3 enemy 定义）；`Td.Map` S/SNP、24×14、tile 1；`Td.Flow` S/SNP、同尺寸；`Td.Grid` L/SNP、24×14、cell 1、1024 entries；`Td.Hits` L/SNP、8192；`Td.ShotSpawns` L/SNP、512；`Td.Rewards`、`Td.Leaks` 均 L/SNP、1024；`Td.Feedback` S/F、4096 |
| Puzzle | `M3.Board` S/SNP：8×8 Color/Special 两数组、6 colors、主线程 Moves 队列与 Events 列表；Moves/Events 恢复时清空，不是保存的一部分；StartRequested 也不写入，ReadSnapshot 本身不清它；无 SimTable |
| Sling | `Sl.Physics` S/SNP：512 body、32 joint、2048 manifold、256 contact-event 容量；`Sl.Game` S/SNP：Kind/Hp 各 512，按 physics body id 索引；`Sl.Feedback` S/F、256 |
| Story | `St.State` S/SNP：DialogueRunner + command queue；runner vars 长度随编译的图，最多 8 choice / 16 event slots；无 SimTable；图由 StContent.Script 编译并进程内缓存 |
| Shooter | `Shooter.State` S/SNP（单 hero/run、16-entry command ring，满则拒绝计数）；`Shooter.Rules` S/N（validated settings 值）；`Shooter.Grid` L/SNP，16×24、cell 1、512 entries，origin=(-8,-12)；`Shooter.Scratch` S/SNP，4096 hit slots，每活跃 bullet 在读前覆盖，hook 空；`Shooter.Feedback` L/F、1024 |

来源：[PlModule.cs](../Assets/PlatformerFoundation/Runtime/PlModule.cs), [TdModule.cs](../Assets/DefenseFoundation/Runtime/TdModule.cs), [M3Module.cs](../Assets/PuzzleFoundation/Runtime/M3Module.cs), [SlModule.cs](../Assets/SlingFoundation/Runtime/SlModule.cs), [StModule.cs](../Assets/StoryFoundation/Runtime/StModule.cs), [ShooterModule.cs](../Assets/ShooterFoundation/Runtime/ShooterModule.cs)；[M3Board.cs](../Assets/PuzzleFoundation/Runtime/M3Board.cs)、[PhysicsWorld2D.cs](../Assets/SinglePlayerFoundation/L1Simulation/Physics/PhysicsWorld2D.cs)、[Dialogue.cs](../Assets/SinglePlayerFoundation/L2Gameplay/Narrative/Dialogue.cs)、[ShooterData.cs](../Assets/ShooterFoundation/Runtime/ShooterData.cs)

`Queue<T>` / `List<T>` 的存在不能被写成“所有玩法队列都有固定上限”。例如 Puzzle Moves/Events、Story Commands 和若干经典 GameState commands 是托管增长容器；部分 ReadSnapshot 对命令数有上限（Story 64，Survivor 256），这只是读档校验，**不等于运行时 enqueue 容量**。本阶段不改变这些语义。

### Brawler 经典与 opt-in

- Classic 顺序：`Bw.Rig` S/SNP（构造后固定的 rig/attack 定义，hook 空）；`Bw.Game` S/SNP；`Bw.Feedback` S/F、128。
- Shared：在 Classic 末尾追加 `Bw.SharedCombat.V1` L/SNP。默认 64 simultaneous fighter scopes、每 scope 64 targets，共 4096 EntityHandle；配置上限分别 128。
- Mobile：在 Classic 末尾依次追加 `Bw.MobileSkills.V1` L/SNP（2 slots）、`Bw.SharedCombat.V1` L/SNP。
- Belt 的**完整顺序**：`Bw.BeltScroller.V1` L/SNP → `Bw.WeaponSkillPose.V1` L/SNP → `Bw.Rig` S/SNP → `Bw.Game` S/SNP → `Bw.Feedback` S/F → `Bw.MobileSkills.V1` L/SNP（4 slots）→ `Bw.SharedCombat.V1` L/SNP。BeltState 默认 64 fighters、64 targets/attack、32 drop slots、3 waves、首波 4 enemies；内部 20×8、cell 1 grid 容量 64，SeparatedGround 64。Drops/权威计数保存；grid、decision program 和查询诊断为派生数据。
- Weapon Belt 在 Pose 与 Rig 之间插入 `Bw.Weapons.V1` L/SNP：60 Hz、4 profiles、32 projectile slots、33 scopes、2112 history handles=33×64、32 cues。它仍使用 Belt 的四 slots；不能将这个 schema 当经典 Brawler。
来源：[BwModule.cs](../Assets/BrawlerFoundation/Runtime/BwModule.cs)、[BwData.cs](../Assets/BrawlerFoundation/Runtime/BwData.cs)、[BwSharedCombat.cs](../Assets/BrawlerFoundation/Runtime/BwSharedCombat.cs)、[BwBeltScroller.cs](../Assets/BrawlerFoundation/Runtime/BwBeltScroller.cs)、[BwMobileSkills.cs](../Assets/BrawlerFoundation/Runtime/BwMobileSkills.cs)、[BwWeapons.cs](../Assets/BrawlerFoundation/Runtime/BwWeapons.cs)、[WeaponRuntime.cs](../Assets/SinglePlayerFoundation/L2Gameplay/Weapons/WeaponRuntime.cs)

## 6. 系统的实际执行顺序

下表按最终管线列出；`Phase/Order` 后同格从左到右执行。未显式 override 的 Order 为 0。机器清单另保留零起点 registrationIndex，避免把注册列表误当最终顺序。所有类型的完整命名空间和源码路径也在清单中。

| 组合 | 已排序执行序列（Phase/Order: 系统） |
| --- | --- |
| Snake 经典 | `ApplyCommands/-10: ReplaySystem` → `ApplyCommands/0: LifecycleSystem` → `ApplyCommands/5: RegionSystem` → `ApplyCommands/10: PopulationSystem` → `ApplyCommands/20: WindowSystem` → `ApplyCommands/30: ItemSpawnSystem` → `ApplyCommands/40: StatsSystem` → `Input/0: PlayerControlSystem` → `Decide/0: AISystem` → `Move/0: MovementSystem` → `Body/0: BodySystem` → `SpatialBuild/0: BodyGridSystem` → `SpatialBuild/0: ItemGridSystem` → `SpatialBuild/0: HeadGridSystem` → `Collision/0: ContactSystem` → `Resolve/0: ResolveSystem` |
| RPG 经典 | `ApplyCommands/0: FloorSystem` → `ApplyCommands/5: InventorySystem` → `ApplyCommands/10: RewardSystem` → `ApplyCommands/12: PropSystem` → `ApplyCommands/15: StairsSystem` → `ApplyCommands/20: ProjectileSpawnSystem` → `Input/0: HeroControlSystem` → `Input/10: FlowFieldSystem` → `Decide/0: MonsterAISystem` → `Move/0: MovementSystem` → `Body/0: StatusSystem` → `SpatialBuild/0: ActorGridSystem` → `Collision/0: CombatSystem` → `Resolve/0: ResolveSystem` |
| Survivor 经典 | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Resolve/0: ResolveSystem` |
| Platformer 经典 | `ApplyCommands/0: FlowSystem` → `Decide/0: WalkerSystem` → `Move/0: PlatformSystem` → `Collision/0: HeroSystem` |
| Defense 经典 | `ApplyCommands/0: CommandSystem` → `ApplyCommands/10: WaveSystem` → `Input/0: PathSystem` → `Move/0: EnemyMoveSystem` → `SpatialBuild/0: GridSystem` → `Collision/0: CombatSystem` → `Resolve/0: ResolveSystem` |
| Puzzle 经典 | `ApplyCommands/0: M3TurnSystem` |
| Sling 经典 | `ApplyCommands/0: FlowSystem` → `Move/0: PhysicsSystem` → `Resolve/0: DamageSystem` |
| Brawler 经典 | `ApplyCommands/0: FlowSystem` → `Move/0: FighterSystem` → `Resolve/0: CombatSystem` |
| Story 经典 | `ApplyCommands/0: StorySystem` |
| Shooter | `ApplyCommands/0: ShooterTickSystem` |
| Guard | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Resolve/0: ResolveSystem` |
| Flying Sword Horde | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/-10: MobileSkillInputSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Collision/7: FlyingSwordSystem` → `Collision/7: MobileSkillPulseSystem` → `Resolve/0: ResolveSystem` |
| Belt Scroller | `ApplyCommands/0: BeltFlowSystem` → `Move/0: BeltFighterSystem` → `Resolve/0: BeltCombatSystem` |
| Crossed Blades（选配） | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Collision/6: CrossedBladeSystem` → `Resolve/0: ResolveSystem` |
| Survivor Mobile（选配） | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/-10: MobileSkillInputSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Collision/7: MobileSkillPulseSystem` → `Resolve/0: ResolveSystem` |
| Survivor Weapons（选配） | `ApplyCommands/0: FlowSystem` → `ApplyCommands/5: RewardSystem` → `ApplyCommands/10: SpawnSystem` → `Input/-10: MobileSkillInputSystem` → `Input/0: HeroSystem` → `Decide/0: EnemySystem` → `Move/0: BulletSystem` → `SpatialBuild/0: EnemyGridSystem` → `Collision/0: CollideSystem` → `Collision/5: AnnularSkillSystem` → `Collision/7: MobileSkillPulseSystem` → `Collision/8: WeaponCombatSystem` → `Resolve/0: ResolveSystem` |
| Brawler Shared（选配） | `ApplyCommands/0: FlowSystem` → `Move/0: FighterSystem` → `Resolve/0: CombatSystem` |
| Brawler Mobile（选配） | `ApplyCommands/0: FlowSystem` → `Input/0: MobileSkillSystem` → `Move/0: FighterSystem` → `Resolve/0: CombatSystem` |
| Weapon Belt（选配） | `ApplyCommands/0: BeltFlowSystem` → `Move/0: BeltFighterSystem` → `Resolve/-10: BeltWeaponSystem` → `Resolve/0: BeltCombatSystem` |

默认组合中只有 RPG FlowFieldSystem 与 Defense PathSystem 实现 ISnapshotSystem；完整顺序保存在机器清单。其余系统没有该接口，不能由 World.SnapshotGaps 推导其私有状态已经保存。

关键差异：RPG 的奖励、库存、楼层等按 Order 重排；Platformer 的 Walker(Decide) 早于先注册的 Platform(Move)；FlyingSword 与 MobileSkillPulse 同为 Collision/7，依注册顺序先剑后脉冲；Weapon Belt 的 BeltWeapon(Resolve/-10) 早于 BeltCombat(Resolve/0)。Shooter 只有一个 ApplyCommands 系统，内部串联流程/移动/命中/结算，不能臆造分阶段系统。

## 7. 权威配置、输入和表现边界

`GpuDriven` / `DataTexture` 是现有两个 RenderTier，不是“模拟后端”。Detect 检查 compute、vertex buffer 输入数和 Null graphics；Override 允许测试强制选择。所有以下 renderer 都接入能力选择，但真正 GPU、fallback、资源重建和像素结果仍须看原生图形测试，不能从枚举或 .NET probe 推导。[RenderCapabilities.cs](../Assets/SinglePlayerFoundation/Presentation/Rendering/RenderCapabilities.cs)

| 玩法 | 权威配置 / 数据来源 | 输入与表现接线 |
| --- | --- | --- |
| Snake 经典 | SnakeConfig → SnakeRuntimeConfig：Movement/Body/Food/Collision/AI/Skill、region/portal/容量；烘焙值和 Native 定义并非通用不可变协议，Capacity 仍引用 source section | 专用 InputRouter + KeyboardMouseInput / touch joystick、boost/fire；SnakeWorldRenderer 用 ChainRenderer、CircleBatch，以及可用时的 GPU food PointCloud；[Bootstrap](../Assets/SnakeFoundation/Game/SnakeGameBootstrap.cs) |
| RPG 经典 | RpgConfig → RpgRuntimeConfig：dungeon/hero/monster/weapon/skill/loot；Dungeon/Loot/Capacity/HeroSkillSlots 保留 source 引用，非完整深冻结 | 共享 InputRouter，keyboard + touch skills；RpgWorldRenderer / SpriteBatch；RpgOptions 的音量/静音另存 options，不是战斗规则；[Bootstrap](../Assets/RpgFoundation/Game/RpgGameBootstrap.cs) |
| Survivor 经典 | SvConfig.Settings/Enemies/Capacity → SvRuntime；Settings 值与 enemy Native 定义复制，运行字段仍公开可变 | InputRouter + keyboard/touch；默认 Pixel SvArtStyle；自动武器攻击由 Tick，SvRenderer 只读呈现；[Bootstrap](../Assets/SurvivorFoundation/Game/SvGameBootstrap.cs) |
| Platformer 经典 | PlModule.MapSize、PlGameState.Tuning / PlatformerTuning、PlLevels 文本关卡与 Flow | InputRouter 锁存 jump；keyboard / touch；PlRenderer / SpriteBatch，动作与地图碰撞由模拟；[Bootstrap](../Assets/PlatformerFoundation/Game/PlGameBootstrap.cs) |
| Defense 经典 | TdRules.CreateDefault、MapSize、tower/enemy 定义与 flow/path规则 | GestureInput 点击建造/选择，拖移/缩放；命令入 GameState；TdRenderer / SpriteBatch；[Bootstrap](../Assets/DefenseFoundation/Game/TdGameBootstrap.cs) |
| Puzzle 经典 | M3Board 8×8 / 6 colors、M3Rules 的配对/级联/目标/随机规则 | GestureInput click/swipe → Moves + RequestTicks；动画忙时 InputLocked；M3Renderer / SpriteBatch 播放 Events，表现时长不改变已结算的回合；[Bootstrap](../Assets/PuzzleFoundation/Game/M3GameBootstrap.cs) |
| Sling 经典 | SlRules、SlLevels、PhysicsWorld2D.Settings；512 body 与 60 Hz 物理步长 | GestureInput drag/release → Launch(Pull)；SlRenderer / SpriteBatch；拉线不是另一个物理模拟；[Bootstrap](../Assets/SlingFoundation/Game/SlGameBootstrap.cs) |
| Brawler 经典 | BwRules / BwRig 固定 attack/animation 定义，FighterInfo/AnimatorState 参与权威战斗 | InputRouter 锁存 punch/kick；keyboard / joystick+tap；BwRenderer / SpriteBatch，经典 skeletal pose 与规则关联；[Bootstrap](../Assets/BrawlerFoundation/Game/BwGameBootstrap.cs) |
| Story 经典 | StContent.Script 编译成缓存 DialogueGraph；Runner 的 PC/vars/事件决定叙事状态 | DialogueBox tap/choose → StCommand + RequestTicks；StRenderer / SpriteBatch；EN/中文字符串和 typewriter 速度属表现；[Bootstrap](../Assets/StoryFoundation/Game/StGameBootstrap.cs) |
| Shooter | ShooterSettings.Default → validated ShooterRules readonly 值；波次、弹道、伤害、容量是权威 | keyboard movement / DragPad touch，自动开火；ShooterRenderer 可 ForcedTier；阴影/FX budget/QualityLevel 不降低权威容量；[Bootstrap](../Assets/ShooterFoundation/Game/ShooterGameBootstrap.cs) |
| Guard | GuardBeacon variant、beacon HP/radius、90×30 guard ticks、annular 两环伤害节奏；相同 SvModule 不等于 classic规则 | CreateGuardExample 选择 SmoothOutline；移动操作仍通过 Survivor 输入；光环显示不制造伤害；[Bootstrap](../Assets/SurvivorFoundation/Game/SvGameBootstrap.cs) |
| Flying Sword Horde | FlyingSwordHorde + SvFlyingSwords.Default（24/10 swords容量/起始数、history上限、轨迹/波次参数）+ MobileSkills | CreateFlyingSwordExample：portrait-first shared MobileCombatHud、pulse/aim-release blink；SmoothOutline + NaturalCharacters + 有界 sword trails；[Bootstrap](../Assets/SurvivorFoundation/Game/SvGameBootstrap.cs) |
| Belt Scroller | BwBeltConfig + BwBeltRules：独立 ground depth / height、combo、loot、waves、4 skill slots；UseDecisionTree 默认 false | CreateBeltScroller：landscape-first MobileCombatHud、移动/拳/踢/跳/治疗；NaturalCharacters；BwRenderer 投影 ground+height，不反推碰撞；[Bootstrap](../Assets/BrawlerFoundation/Game/BwGameBootstrap.cs) |

补充源码：[SnakeRuntimeConfig.cs](../Assets/SnakeFoundation/Runtime/SnakeRuntimeConfig.cs)、[RpgRuntimeConfig.cs](../Assets/RpgFoundation/Runtime/RpgRuntimeConfig.cs)、[RpgOptions.cs](../Assets/RpgFoundation/Game/RpgOptions.cs)、[PlData.cs](../Assets/PlatformerFoundation/Runtime/PlData.cs)、[PlLevels.cs](../Assets/PlatformerFoundation/Runtime/PlLevels.cs)、[TdData.cs](../Assets/DefenseFoundation/Runtime/TdData.cs)、[M3Rules.cs](../Assets/PuzzleFoundation/Runtime/M3Rules.cs)、[SlLevels.cs](../Assets/SlingFoundation/Runtime/SlLevels.cs)、[BwData.cs](../Assets/BrawlerFoundation/Runtime/BwData.cs)、[StContent.cs](../Assets/StoryFoundation/Runtime/StContent.cs)

所有实际 renderer：[SnakeWorldRenderer.cs](../Assets/SnakeFoundation/Presentation/SnakeWorldRenderer.cs), [RpgWorldRenderer.cs](../Assets/RpgFoundation/Presentation/RpgWorldRenderer.cs), [SvRenderer.cs](../Assets/SurvivorFoundation/Presentation/SvRenderer.cs), [PlRenderer.cs](../Assets/PlatformerFoundation/Presentation/PlRenderer.cs), [TdRenderer.cs](../Assets/DefenseFoundation/Presentation/TdRenderer.cs), [M3Renderer.cs](../Assets/PuzzleFoundation/Presentation/M3Renderer.cs), [SlRenderer.cs](../Assets/SlingFoundation/Presentation/SlRenderer.cs), [BwRenderer.cs](../Assets/BrawlerFoundation/Presentation/BwRenderer.cs), [StRenderer.cs](../Assets/StoryFoundation/Presentation/StRenderer.cs), [ShooterRenderer.cs](../Assets/ShooterFoundation/Presentation/ShooterRenderer.cs).

### 必须保留的两个旧边界

- **Snake quality 有权威例外。** AdaptiveQualityController 会更改 SnakeQuality.AIDecisionIntervalTicks，AISystem 读取它；ReplayFrame 记录/回放该值。DiscSegments、NodeStride 等视觉 knobs 不能与 AI cadence 混称“纯表现设置”。本阶段保留旧结果；若后来拆分/移除，必须另立规则与回放迁移任务。[SnakeQuality.cs](../Assets/SnakeFoundation/Runtime/SnakeQuality.cs)、[AdaptiveQualityController.cs](../Assets/SnakeFoundation/Game/AdaptiveQualityController.cs)、[AISystem.cs](../Assets/SnakeFoundation/Runtime/Systems/AISystem.cs)、[Replay.cs](../Assets/SnakeFoundation/Runtime/Replay.cs)
- **WeaponRuntime 指纹目前混合规则与视觉/握持定义。** 它会 hash VisualId、GripOffset / SecondaryGripOffset / MuzzleOffset 等，不能宣称改视觉 ID 一定不影响当前读档兼容判定。Profiles 在构造时 clone/validate；新拆分只能在显式新协议中做迁移。Sv weapon 与 Bw weapon 的 TickRate/history容量也不同。[WeaponRuntime.cs](../Assets/SinglePlayerFoundation/L2Gameplay/Weapons/WeaponRuntime.cs)

NaturalCharacters / artStyle 的纯表现选择不必添加模拟模块；但 MobileSkills、WeaponCombat、SharedCombat、Belt/FlyingSword 是权威组合开关，会增资源/列/系统，不能当成画质开关。CrossedBlade 工厂已有命中规则和普通敌人反馈，但不安装独立 blade-path 美术；独立 weighted BAT demo 不等于上述玩法已采用任意 weighted rig 导入。

## 8. 保存支持与现有 fixture

### 8.1 raw Session 快照的共同边界

- Session 保存 next tick / elapsed → World → Pipeline；不保存 TickRate、MaxTicksPerFrame、ManualClock、累积帧时、Running/Paused 状态或宿主暂停来源。同配置/时钟是调用者前提，不能把自定义 60 Hz SvMode 与工厂 30 Hz 自动判为相容。
- World format 1 检查 seed、有序表名、容量、列数量、有序 snapshot 资源名与 section marker。列按声明顺序写原始 unmanaged bytes（元素数与 sizeof(T)），**没有列名/type/schema ID**；同尺寸字段/列重排可能不被检测。资源的主线程权威状态若没实现接口会被省略；SnapshotGaps 仅筛 `IJobData && !ISnapshotResource`，19 组合的空列表不构成完整保存证明。
- Pipeline 只保存 ISnapshotSystem，按执行序使用短类型名；重命名/新增/重排这些系统可能破坏旧字节。一般配置不保存、未全局指纹化；不能跨配置、内容版本、平台/后端或 struct ABI 自动承诺字节兼容。
- Session 恢复失败会 Restart 并保留此前显式 running/paused 状态，再抛错；**不是保留旧 live timeline 的事务式回滚**。直接 World.ReadSnapshot 可能部分恢复，调用者必须 reset。成功恢复/重开增加非持久 TimelineRevision，且清 PendingTicks。
- saved:false EventQueue 的 payload 空，但 key/marker 仍参与顺序。DestroyQueue 写前按 handle index/generation 规范化；一般 EventQueue 没有自动稳定排序。队列顺序、接受/丢弃策略和 owner generation 仍是玩法契约。
来源：[Snapshot.cs](../Assets/SinglePlayerFoundation/Contracts/Snapshot.cs)、[Column.cs](../Assets/SinglePlayerFoundation/Runtime/World/Column.cs)、[EntityRegistry.cs](../Assets/SinglePlayerFoundation/Runtime/World/EntityRegistry.cs)、[SimWorld.cs](../Assets/SinglePlayerFoundation/Runtime/World/SimWorld.cs)、[TickPipeline.cs](../Assets/SinglePlayerFoundation/Runtime/Scheduling/TickPipeline.cs)、[SimSession.cs](../Assets/SinglePlayerFoundation/Runtime/Session/SimSession.cs)

ProfileStore format 1 包装 magic/version、ISaveData.Version、长度与 FNV-1a checksum；SessionSnapshotSave v1 只是 raw snapshot 适配，没有 mode/schema/content envelope 或通用迁移。当前覆盖流程是 temp 写完后先删除旧 slot，再 Move，**不证明崩溃原子替换**；长度也没有独立固定安全上限。它可检测已有测试中的破损，不是面向任意不可信存档的完备解析协议。[ProfileStore.cs](../Assets/SinglePlayerFoundation/Runtime/Persistence/ProfileStore.cs)、[SessionSnapshotSave.cs](../Assets/SinglePlayerFoundation/Runtime/Persistence/SessionSnapshotSave.cs)

### 8.2 九个经典玩法与四个变体

“同构”表示同一构建/执行后端/规则/容量内恢复。下面按玩法归档的测试多数使用专门缩小/静音的配置，并非第 3–5 节默认工厂整套配置的逐项认证；精确种子/容量/开关以各测试 helper 为准。测试名称是可定位的现有门槛，**其存在不等于这份文档另跑了全部原生测试**。二进制 fixture、同次运行生成的比较、语义断言分开标注。

| 组合 | 当前保存/操作接线 | 精确测试定位 | 证据种类 / 限制 |
| --- | --- | --- | --- |
| Snake 经典 | 输入 replay 已实现；没有发现专用 save/load UI、ProfileStore 或 SessionSnapshotSave 接线。通用 Session API 可调用但完整状态覆盖不足 | [SnakeReplayTests](../Assets/SnakeFoundation/Tests/EditMode/SnakeReplayTests.cs).`ARecordedRunReplaysExactly` | 同构录制/回放，比较部分 float aggregate（food count / heads / masses）和 player head；非完整快照 hash，非旧构建 fixture |
| RPG 经典 | 实际 SaveRun()/Continue() + SessionSnapshotSave；另有楼层起始 HeroProfile 保存，不能混同 | [RpgSnapshotTests](../Assets/RpgFoundation/Tests/EditMode/RpgSnapshotTests.cs).`MidFloorSnapshotContinuesIdentically` | 同次运行 mid-floor checkpoint：立即重写、fresh restore、原地 rewind 和继续 300 ticks 字节一致；另有 CorruptSnapshotRestartsTheSession |
| Survivor 经典 | 同构 raw-session 保存/重放测试；未发现专用磁盘保存 UI | [SvGameTests](../Assets/SurvivorFoundation/Tests/EditMode/SvGameTests.cs).`RunsAreDeterministicAndSnapshotsResumeExactly` | 同次运行精确继续；另有唯一已找到的旧构建二进制 fixture，见下节 |
| Platformer 经典 | 同构 raw-session 保存/重放测试；未发现专用磁盘保存 UI | [PlTests](../Assets/PlatformerFoundation/Tests/EditMode/PlTests.cs).`ReplaysAndSnapshotsAreExact` | 600 ticks 脚本、midpoint restore、最终字节相等；无旧构建字节 fixture |
| Defense 经典 | 同构 raw-session 保存/重放测试；未发现专用磁盘保存 UI | [TdTests](../Assets/DefenseFoundation/Tests/EditMode/TdTests.cs).`DeterministicWithSnapshots` | 900 ticks build/wave 场景和 midpoint restore；无旧构建字节 fixture |
| Puzzle 经典 | 实际 SnapshotHistory(50) undo；无专用磁盘保存接线 | [M3Tests](../Assets/PuzzleFoundation/Tests/EditMode/M3Tests.cs).`TurnsAreTicksAndUndoRestoresExactly` | exact undo bytes；SameSeedSameMovesSameGame 比较同种子/相同回合最终 bytes；不是旧构建 fixture |
| Sling 经典 | 同构 raw-session 物理恢复测试；未发现专用磁盘保存 UI | [SlTests](../Assets/SlingFoundation/Tests/EditMode/SlTests.cs).`ShotsAreDeterministicAndSnapshotsResume` | 300 ticks shot/collapse、midpoint restore；无旧构建字节 fixture |
| Brawler 经典 | 同构 raw-session 战斗恢复测试；未发现专用磁盘保存 UI | [BwTests](../Assets/BrawlerFoundation/Tests/EditMode/BwTests.cs).`FightsAreDeterministicAndSnapshotsResume` | 600 ticks 相同输入与恢复继续；无旧构建字节 fixture |
| Story 经典 | 实际 BACK（SnapshotHistory 128）、SAVE/LOAD（SessionSnapshotSave） | [StTests](../Assets/StoryFoundation/Tests/EditMode/StTests.cs).`SavesRestoreTheExactLineAndScene` | 恢复 Text/trust/Choice 状态的语义断言；该测试虽名带 Scene，但未断言 scene 字段，也不含完整 byte equality / 继续重放门槛 |
| Shooter | 同构 raw-session 保存/重放测试；未发现专用磁盘保存 UI | [ShooterTests](../Assets/ShooterFoundation/Tests/EditMode/ShooterTests.cs).`SameSeedAndInputsMatchAndSnapshotResumesExactly` | 360 ticks wave/upgrade、midpoint restore，最终 bytes 一致；无旧构建 fixture |
| Guard | SvGameState 启用 version 1 扩展；同构测试 | [SvGuardTests](../Assets/SurvivorFoundation/Tests/EditMode/SvGuardTests.cs).`GuardSnapshotsResumeAnnularCadenceAndDamageExactly` | checkpoint + 41 ticks，annular cadence/damage 与完整 bytes 一致；不是 classic schema |
| Flying Sword Horde | 默认有 sword-state v1 + GameState 扩展 + skills/pose；此 EditMode 用自定义 1-sword / 32-enemy、MobileSkills=false，不含 skills/pose | [SvFlyingSwordTests](../Assets/SurvivorFoundation/Tests/EditMode/SvFlyingSwordTests.cs).`SnapshotResumesInFlightWithHistoryAndExactDeterminism` | in-flight/history rewrite + 100-tick continuation；host variant / sword-rule 更改有拒绝测试 |
| Belt Scroller | 新增三列 + belt/skills/pose/shared resources；同构测试 | [BwBeltScrollerTests](../Assets/BrawlerFoundation/Tests/EditMode/BwBeltScrollerTests.cs).`MidActionAirborneAndLootSnapshotResumesExactly` | 60-tick airborne/loot继续；SameSeedAndInputsStayDeterministicThroughWaves；SchemaRejectsClassicAndChangedConfigWithoutChangingClassicLayout |

Snake 的明确缺口：`SnakeGameState`、`RegionPopulations`、`SnakeQuality`、`ReplayBuffer` 无 ISnapshotResource；`Signals` 是供 Jobs 使用的 native mailbox，却也没有 IJobData / ISnapshotResource 标记。Snake 系统未实现 ISnapshotSystem。BodyStore/grid 有 hook 不能补齐这些遗漏。因此保留“replay 已实现，完整 Session 保存未验证/覆盖不全”的状态，不因为通用 API 可调用就对玩家承诺恢复整局。[SnakeGameState.cs](../Assets/SnakeFoundation/Runtime/SnakeGameState.cs)、[SnakeSpawner.cs](../Assets/SnakeFoundation/Runtime/SnakeSpawner.cs)、[Replay.cs](../Assets/SnakeFoundation/Runtime/Replay.cs)、[SnakeQuality.cs](../Assets/SnakeFoundation/Runtime/SnakeQuality.cs)

RPG 原生 UI 中 SaveRun/Continue 的直接门槛是 [RpgUITests](../Assets/RpgFoundation/Tests/PlayMode/RpgUITests.cs).`PauseSaveAndQuitThenContinueResumesMidFloor`；楼层起点路径为 `StairsDescendAndContinueFromTheSave`。

RPG 真实接线还有 `RpgGameTests.ProfilesSaveAndContinueOnTheSameFloor`（HeroProfile 楼层起点），Story 实际 UI 用 `StPlayTests.TapThroughChooseSwitchLanguageUndoAndSave`；这些不能替代别的模式的磁盘保存门槛。[RpgGameTests.cs](../Assets/RpgFoundation/Tests/EditMode/RpgGameTests.cs)、[StPlayTests.cs](../Assets/StoryFoundation/Tests/PlayMode/StPlayTests.cs)

### 8.3 真正冻结的旧构建字节

[SvGuardTests](../Assets/SurvivorFoundation/Tests/EditMode/SvGuardTests.cs).`ClassicRestoresAndRewritesActual97a2b34SnapshotByteForByte` 使用 [SvLegacySnapshotFixtures](../Assets/SurvivorFoundation/Tests/EditMode/SvLegacySnapshotFixtures.cs) 内固定 gzip/base64 数据：

| Fixture | 来源与范围 | 解压后字节 / SHA-256 |
| --- | --- | --- |
| Checkpoint | 旧 97a2b34 assemblies 隔离采集；seed 71；E/B/G/V=16/64/32/128，具体规则见测试 | 10392 / `d9951f51cbe8279916a493867b53dce6f1eee24b2becfec51cc59fc9ebb6e0c6` |
| Continued | 相同旧构建继续 23 ticks | 10367 / `879e44cd7459e1229dc5f9303453780923a929316262b2f52954c58826b9dc32` |

本次独立解压并验证两份 fixture 长度/hash。原生与 .NET 都要求 restore/rewrite 精确；**旧 continuation byte equality 只在 SPF_DOTNET_HARNESS 编译分支启用**，Unity 继续结果比较 HP/position 容差。不能把该哈希推广为 Burst/ARM/任意配置的通用金标。其他八个经典玩法、四个新增变体及六个 opt-in 未找到已提交的旧构建二进制/字面量状态 hash；现有同次运行比较保留，但不能冻结上一个版本的规则。

### 8.4 opt-in schema 与拒绝门槛

| 选配 | 已有局部 schema 检查 | 精确现有测试 |
| --- | --- | --- |
| Brawler Shared | v1：scope/target容量、history/timeline关系；追加资源，和 classic 双向拒绝 | [BwSharedCombatTests](../Assets/BrawlerFoundation/Tests/EditMode/BwSharedCombatTests.cs)：`MidHitSnapshotRestoresHistoryBeforeCorpseSwapback`；`ClassicAndSharedSnapshotsHaveExplicitlyDifferentLayouts`；`SnapshotRejectsDifferentCapacityAndCorruptCount` |
| Crossed Blades | v1：target容量、pulse/history/timeline；只加自己的资源，不启用 GameState 扩展 | [SvCrossedBladeTests](../Assets/SurvivorFoundation/Tests/EditMode/SvCrossedBladeTests.cs)：`SnapshotWithAcceptedHistoryResumesThroughSortDestroyAndRecycle`；`ClassicAndBladeSnapshotsHaveExplicitlyDifferentResourceLayouts`；`SnapshotAfterLethalPulsePreservesPendingDestroy`；`SnapshotRejectsCapacityMismatchAndInvalidHistoryBounds` |
| Brawler Mobile | SkillSlots v1 + shared history，与 classic 分开 | [BwMobileSkillTests](../Assets/BrawlerFoundation/Tests/EditMode/BwMobileSkillTests.cs)：`MobileMidAttackAndRechargeSnapshotContinueExactly` |
| Survivor Mobile | 默认工厂是 SkillSlots v1 + pose + Guard 扩展；此测试是 Classic + MobileSkills 的自定义组合，证明其继续/隔离，不能替代默认 Guard-Mobile 全组合测试 | [SvMobileSkillTests](../Assets/SurvivorFoundation/Tests/EditMode/SvMobileSkillTests.cs)：`MobileSnapshotsContinueAndClassicSchemaStaysSeparate` |
| Flying Sword | v1：容量/history容量/剑规则 fingerprint（含 host variant）；不是整个 SvConfig 指纹 | [SvFlyingSwordTests](../Assets/SurvivorFoundation/Tests/EditMode/SvFlyingSwordTests.cs)：`ClassicResourceLayoutUnchangedAndMalformedSwordSnapshotRejected`；`DifferentHostVariantRejectsSnapshotBeforeTerminalFlowCanDiverge`；`AlteredSwordRulesRejectSnapshotAndClassicOptInPreservesClock` |
| Belt | v1：Fighters / TargetsPerAttack / Drops / Waves / FirstWaveEnemies；derived grid/query counters 重建 | [BwBeltScrollerTests](../Assets/BrawlerFoundation/Tests/EditMode/BwBeltScrollerTests.cs)：`SchemaRejectsClassicAndChangedConfigWithoutChangingClassicLayout` |
| Weapon Belt | WeaponRuntime v2，60 Hz、history/cue/projectile容量与profile指纹；无v1通用迁移 | [BwWeaponTests](../Assets/BrawlerFoundation/Tests/EditMode/BwWeaponTests.cs)：`ActualAdapterSnapshotAtReleaseAndRowReorderDoNotRepeatHits`；`SameTickPauseResumeCannotRewindBodyWeaponOrReleasedArrow` |
| Survivor Weapons | WeaponRuntime v2，30 Hz；此测试是 Classic + weapons/mobile 自定义组合，非默认 Guard-Weapons；恢复清旧 cue、递增 presentation revision | [SvWeaponTests](../Assets/SurvivorFoundation/Tests/EditMode/SvWeaponTests.cs)：`PulseAndBlinkExposeSeparateSavedPoseClocksAndPauseWithLevelUp`；`RestoreOfInFlightArrowMatchesUninterruptedActualDamageAndResetClearsPool` |
| 共享 slots | v1：count、Id/IconId/activation/cooldown/maxCharges及 recharge状态 | [SkillSlotTests](../Assets/SinglePlayerFoundation/Tests/EditMode/SkillSlotTests.cs)：`MidRechargeSnapshotContinuesAndResetRefills`；`SnapshotRejectsChangedDefinitionCapacityAndCorruptCharges` |
| 共享 weapons | profile字段指纹、TickRate及容量；geometry/history归属拒绝检查 | [WeaponRuntimeTests](../Assets/SinglePlayerFoundation/Tests/EditMode/WeaponRuntimeTests.cs)：`SnapshotBeforeAndAfterReleaseResumesWithoutDuplicateShotAndRejectsContentChanges`；`RestoreRejectsInvalidProjectileGeometryOrDetachedHitScope`；`RestoreRejectsContactHistoryDetachedFromActionPulse` |

局部 schema 编号没有组成全局 Mode schema；同一个 SvModule/BwModule 的多个入口也没有自动迁移关系。未启用 opt-in 时不得修改经典表、资源顺序、payload 或 fixture。BwBelt 的 UseDecisionTree 是默认关闭的执行策略；其 same-run frozen-reference parity 测试不是旧二进制 fixture。空间四叉树/分离 Burst 候选的研究或测试也不等于默认工厂切换了后端。

## 9. 共享回归索引与待补项

- [WorldSnapshotTests](../Assets/SinglePlayerFoundation/Tests/EditMode/WorldSnapshotTests.cs)：`RestoredWorldMatchesRowsHandlesAndFutureAllocations`；`SnapshotIsRejectedByADifferentWorld`；`JobResourcesWithoutSnapshotSupportBlockSaving`；`SpatialStructuresRoundTrip`
- [DestroyQueueSnapshotTests](../Assets/SinglePlayerFoundation/Tests/EditMode/DestroyQueueSnapshotTests.cs)：`ScheduledArrivalOrdersHaveCanonicalRepeatedSnapshotBytes`；`RestoreResetAndPlaybackKeepDuplicatesStaleHandlesAndRecyclingSemantics`
- [PooledTableTests](../Assets/SinglePlayerFoundation/Tests/EditMode/PooledTableTests.cs)：`PooledTablesSnapshotIncludingPendingRemovals`
- [PipelineTests](../Assets/SinglePlayerFoundation/Tests/EditMode/PipelineTests.cs)：`SystemsRunInPhaseThenOrderThenRegistrationOrder`；`DeclaredAccessChainsJobsAndSnapshotsRotateAtSync`；`ExceptionWhileSchedulingCompletesAlreadyScheduledJobs`
- [TurnToolsTests](../Assets/SinglePlayerFoundation/Tests/EditMode/TurnToolsTests.cs)：`RestoreDiscardsRequestsFromThePreviousTimelineAndKeepsManualPause`；`InvalidRestoreDiscardsRequestedTicksAndKeepsManualPause`；`RestartDiscardsRequestedTicksAndPreviousFrameStats`；`SnapshotHistoryUndoesAndRedoes`
- [FoundationRulesTests](../Assets/SinglePlayerFoundation/Tests/EditMode/FoundationRulesTests.cs)：`SaveFilesRoundTripAndRejectCorruption`
- [SessionHostTests](../Assets/SinglePlayerFoundation/Tests/EditMode/SessionHostTests.cs)：`SnapshotRestoreKeepsHostSuspensionWithoutCreatingAManualPause`

图形/输入和质量隔离的直接门槛（明确区分 EditMode 与原生 PlayMode，盘点不冒充新执行）：

- [ShooterVfxIsolationTests](../Assets/ShooterFoundation/Tests/EditMode/ShooterVfxIsolationTests.cs)：`VisualQualityOverflowAndVisualSeedsLeaveReplayBytesIdentical`
- [SvGuardPlayTests](../Assets/SurvivorFoundation/Tests/PlayMode/SvGuardPlayTests.cs)：`GuardPortraitRingsHealthQualityAndRestart`
- [SvFlyingSwordPlayTests](../Assets/SurvivorFoundation/Tests/PlayMode/SvFlyingSwordPlayTests.cs)：`CaptureDenseSwordSwarmWithRealPortraitHudAndQualityInvariant`
- [BwBeltScrollerPlayTests](../Assets/BrawlerFoundation/Tests/PlayMode/BwBeltScrollerPlayTests.cs)：`LandscapeBeltDepthJumpLootRestartAndBudgetIsolation`
- [SvMobileHudPlayTests](../Assets/SurvivorFoundation/Tests/PlayMode/SvMobileHudPlayTests.cs)：`PortraitHudDrivesPulseAimedBlinkCancelAndRestart`
- [BwMobileHudPlayTests](../Assets/BrawlerFoundation/Tests/PlayMode/BwMobileHudPlayTests.cs)：`SkillHudOwnsTwoFingersHitsAndFlushesOnPause`

待补边界保持可见：

1. **尚未实现**：全局 mode/module manifest、稳定表/列/系统 schema、全规则内容指纹、等尺寸语义变更检测、通用迁移和面向不可信 payload 的统一上限。按计划阶段 B/D 分批增加，不重写 legacy raw writer。
2. **未完整验证/覆盖不足**：Snake 整局保存；Story byte-exact continuation；除上述 Survivor 单配置外的历史字节/字面量 hash 金标。若新增 golden，必须从固定旧代码/后端独立采集，不能用被测新实现生成 expected。
3. **已有 native 证据、并非本次新增运行**：基线树的 .NET 1050 passed；原生 EditMode 1084 passed / 5 skipped；graphics PlayMode 154 passed / 1 skipped，见第 1 节精确闭环记录。跳过项保持原记录中的范围；没有因这次文档变更转成通过。
4. **真机待验**：物理 Android/iOS 的触摸、中断、IL2CPP/Burst、图形 API fallback、持续 p50/p95/最坏帧时、温控、电量及内存。表中的容量和桌面 CI 不能签署真机预算。

### 为下一阶段保留的有界需求卡

| 具体需求 / 两个消费者 | 权威与表现边界 | 容量 / fallback / 主来源 | 采用与不采用条件 |
| --- | --- | --- | --- |
| 安装失败后可重试：先 DriftSmoke，再 Survivor + Brawler opt-in | World/Pipeline 所有者负责释放；不改变既有系统顺序与旧字节 | 复用既有固定布局/资源；旧 IGameplayModule 保留；[阶段 B](SharedFoundationSemanticExtensionPlan.md#p1-阶段-b-组合预检和安装事务)与 WorldComposer/SimSession | 首个/中间/末尾初始化失败均回到资源基线才采用；不先造热插拔框架 |
| 同 tick restore / 双 Session 视图隔离：SvRenderer + BwRenderer | timeline/session/owner generation 校验属于绑定；命中仍在 Tick | 现有有界 cue/粒子池、原后端可回退；[阶段 C](SharedFoundationSemanticExtensionPlan.md#p1-阶段-c-session-与-view-生命周期契约)与 TimelineRevision / WeaponRuntime | 两个真实适配器有重复才抽 helper；同步资源路径不虚构异步加载缺陷 |
| 新 envelope 安全拒绝内容不符：RPG/Story 真实磁盘入口，再覆盖 Survivor/Belt opt-in | 规则/时间与视觉配置区分；旧 raw API 不变 | 明确长度上限、manifest/schema/规则指纹，旧 reader 可回退；[阶段 D](SharedFoundationSemanticExtensionPlan.md#p2-阶段-d-内容编译和保存兼容协议)与本表第 8 节 | 等尺寸列调序/内容变化可拒绝且失败语义明确才启用；不承诺未知旧布局自动迁移 |

## 10. 本次检查记录

- 19 个默认/opt-in 组合的真实工厂 + SimSession 创建/释放，按 .NET harness 桩读取表/列顺序、capacity、scope、资源接口和最终 pipeline；第二独立进程重复输出逐字节一致。没有执行游戏 Tick、改写快照、进行性能测量或原生验收。
- 编译临时探针成功；最初探针误用 internal AccessDeclaration.IsBarrier，改为诊断反射后重新编译通过。依赖恢复曾输出 NU1900 vulnerability-cache warning，不影响编译/盘点；这些步骤不代表安全扫描通过。
- 所有新增文档相对路径、机器清单源码路径与精确测试方法均检查存在；源文件 SHA-256 与固定基线核对；所有 Markdown 表列数和 git diff --check 检查。旧 fixture 两个 SHA-256 独立核对，不更新任何 expected。
- 本交付仅新增本文件、机器清单及其冷路径诊断源码/复现文档；没有 Runtime / Game / Presentation / Tests / AGENTS / README 改动。后续运行时提交必须重新执行适用门槛，不能引用本次创建探针替代。
