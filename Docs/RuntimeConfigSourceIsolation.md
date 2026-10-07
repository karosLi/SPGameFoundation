# Snake / RPG 运行配置来源隔离（A3）

日期：2026-10-07。对应 [Unity 2022 整合方案 §6.3](LatiosUnity2022IntegrationBlueprint.md#63-运行配置的来源隔离)。本阶段不引入 Entities，不改 Tick 调度、默认玩法、raw 保存布局或 Session 的默认保存身份。

## 合约与来源审计

`SnakeRuntimeConfig` 构造和 `RpgRuntimeConfig.Bake` 现在遵循同一个最小约定：

1. **Validate**：在配置 NativeArray 或容量决定的大缓冲分配前，验证所读来源、有限浮点数、索引引用和实际容量计算。
2. **Freeze**：运行定义与 authoring 来源隔离；保留原来已有的值拷贝，只补齐已确认的引用别名。
3. **Build**：配置拥有其 native 表，完整构造后交给 WorldLayout；失败释放已成功创建的 native 前缀。

这是 `source-snapshot.v1` 行为边界，**不是 public runtime 对象不可变**。`Settings`、公开 native 数组、RPG 的公开字段及复制后的 section 仍可被调用者修改；这样修改是否安全仍取决于同步、派生表和游戏自己的契约。本阶段不提供热重载接口或线程安全的 authoring 并发编辑。Bake 期间来源必须保持静止，更新 SO 后创建新 Session 才应用新定义。

| 来源 | 原来怎样读取 | 本次改动 |
| --- | --- | --- |
| Snake `Capacity` | Runtime 保留 SO 的 section 引用；模块分配网格/轨迹，RegionPopulations 读取 ChunkSize | 复制整个纯值 section；新 Session 各自拥有一份 |
| Snake movement/body/food/collision/AI/skill、buff/prop/region/portal、skin/name | 已经值拷贝或复制列表/原生表 | 保留已有输出、裁剪与空列表回退 |
| RPG `Dungeon` | FloorSystem、DungeonGenerator、RpgSpawner、PropSystem、RewardSystem 在后续楼层/奖励中继续读取 | 复制纯值 section |
| RPG `Loot` | RewardSystem、RpgShop、GearId/ArmourId 持续读取，Gear 数组却已在 Bake 时生成 | 复制纯值 section，避免修改来源导致 tier 与已烘焙 Gear 不一致 |
| RPG `Capacity` | Runtime 保留来源；各模块在初始化时分配 | 复制纯值 section；Core 队列读取其 runtime 副本 |
| RPG `Hero.SkillSlots` | HeroLoadout 在出生/升级时读取原数组 | 复制数组；null 仍表示空数组 |
| RPG monster/weapon/skill/gear/settings/names/AI decision program | 已值拷贝或新建 | 保持定义、顺序和现有派生规则 |

审计 Runtime、Game 和相关测试，没有发现项目内明确依赖“Session 创建后修改来源 SO”来改变这两个游戏的调用。原先 RPG 的别名确实允许来源编辑影响下一楼层、掉落或技能槽；这项意外可观察行为现已明确改为 Session 快照。编辑器或仓库外工具若依赖它，需要显式重建 Session，不能把本变更视为兼容的实时编辑通道。Snake 的运行画质/AI 调频例外没有改变。

复制的四个 section 当前只有 primitive/enum 字段，使用各类型自身的 `MemberwiseClone`。测试禁止以后直接加入其他字段类型而没有审视深拷贝；`HeroSkillSlots` 明确复制。字符串不可变，复制字符串引用不会把可变集合保留在 runtime 中。

## 预检边界与新拒绝的输入

正常默认值、现有工厂、有效定义顺序、重复 weapon-kind 的旧覆盖顺序、空表占位与既有 clamp 均保留。以下原先会晚失败、溢出、产生非有限状态或静默截断/忽略的输入，现在以 `ArgumentException`（null 根为 `ArgumentNullException`）明确拒绝：

- 缺少需要的 section/list，或 list 中有 null entry；Snake 没有 region
- 所验证 section/entry 的浮点字段为 NaN/Infinity，region/portal 坐标或 RPG 实际读取的 RGB 非有限
- 无法由 byte 身份表示的定义数量、越界 buff/portal/hero-skill/monster-skill 引用、越界 RPG weapon enum；空 skill 槽 0 仍有效
- 非正的表/队列容量，已有 16-bit grid owner 上限超出，Snake 非法 slab、非正分配所需间距
- Snake `Snakes × AverageTrailPoints`、`trailPoints + Snakes`、`EventQueue × 4`、item capacity、网格 cell 数、各 region chunk 数或**所有 region 分量最大尺寸组合后的 head-grid cell 数**无法由下游 int 容纳
- RPG `Actors + Projectiles`、`Width × Height`、actor-grid cells、`1 + (lootWeapons + 1) × GearTiers` 溢出或非法长度
- RPG 无法生成有效内部房间的 room 范围，rooms/prop/gold 随机上界的 `+1` 溢出，或没有有效 Gear tier

用 long 检查整数乘加，网格检查匹配原来的 float 除法、ceil 和分量最大值。没有把这些检查宣传为所有玩法算术的证明，也没有新增任意移动设备容量上限；很大但合法的容量仍可能因设备内存不足而失败。验证自身有小额冷路径托管分配。

现有兼容回退有专门测试：Snake 空 buff/prop/portal/skin/name、零/负值触发现有 spacing/drop/AI clamp、非正 BodyGridCellSize 回退；RPG null skill slots、空 monster/weapon/skill 列表仍可烘焙。需要空 skill 列表时，其余 skill 引用也必须为空或 0。

## 所有权与失败

两份 runtime 在其 native 分配范围外层使用 try/catch。发生异常时只释放已成功赋值的容器，再传播原异常。Snake 的 native 属性释放后归零，重复 Dispose 安全；RPG 使用可直接归零的 NativeArray 字段。

Snake 先向 layout 移交 config，再声明表；拒绝移交时自行 Dispose。RPG Core 同样处理拒绝注册。已接受资源仍由现有 WorldLayout/WorldComposer 回滚和 World 释放，资源保存次序不变。

测试通过私有、生产调用恒为 null 的分配后检查点，分别在第 1/2/3/4 个 native 分配成功后抛出，并检查所有 IsCreated 均已清空、重复释放安全、后续 Bake 成功。这证明配置所有者的前缀清理，**不是对真实 allocator OOM、Unity 内部部分分配或其他资源构造器的故障注入证明**。

## 可选稳定内容身份

两个 runtime 增加 `WriteContent(BinaryWriter)`。它按显式字段顺序写出版本标记、运行 settings、复制的 sections、实际烘焙表、计数、名字与外观值；没有反射、内存 padding 或进程内 AccessKey。使用既有路径：

```csharp
byte[] content = SaveCompatibilityDescriptor.Encode(runtime.WriteContent);
// 把 content 传给应用自己的既有 SaveCompatibilityDescriptor 构造参数。
```

相同运行定义得到相同编码和现有 descriptor 的 SHA-256 ContentFingerprint；源对象创建顺序不参与身份。该输入保守地包括名字/外观，当前不承诺视觉单独兼容；Encode 的原有 64 KiB 上限仍生效。对 runtime 本身修改后再编码会产生新身份，不是自动保存当初 Bake 的 hash。它没有安装 Snake/RPG save adapter、没有替换 envelope、没有改旧 raw 读写，也没有填补这些游戏原先未保存状态的缺口。

## 红测试、回归与精确 raw 对照

[机器可读证据](validation/RuntimeConfigIsolation-20261007.json)保存源码 SHA-256、测试范围、数值和测量限制。

- 红测试使用 `9fc4e2b7e20a17e5586a68f756e92f8102f99339` 原构造实现：Session 创建后改来源，Snake 读到 ChunkSize 1（应为 125）；RPG 读到 MonsterDensity 0（应为 0.028）。两个测试均失败。
- 最终定向 .NET：Snake **43/43**，RPG **58/58**。其中新增来源隔离/预检/释放 **22 + 21** 项，另覆盖现有 Snake gameplay/replay、RPG game/combat/AI/snapshot。无阈值调整。
- 两个 Session 共用同一 SO/module 的显式测试，验证各自 section/slot 对象独立，修改共享来源后两者完整内容编码均保持原值。另一组未编辑控制 Session 与来源已编辑 Session 的后续玩法 raw 字节完全相同。
- 受控旧/新构造 A/B 的 raw 摘要一致：Snake 6,588,656 B，SHA-256 `4d499319c7c956058da7b65e18e37b5ecfcfddd4b8d6d67fd2f41818a03829b5`；RPG 23,771 B，SHA-256 `7253b7d82c74b8c49a8d0d7d12f710a1abbef9f4a6d0b8c96009277b7c1f06c3`。
- Raw 场景：Snake 默认定义，只按 SnakeTestWorld 关闭 props、设置 AI=4/food=2、seed=42，StartPlayer 后 15 步；RPG RpgTestWorld 默认定义、seed=7/runSeed=99，开始后 20 步。这不是新增/替换历史 golden fixture，也不是所有组合的 raw 证明。

A/B 基线将两个 RuntimeConfig 实现恢复为上述 commit 的文本，仅加 partial 关键字以保留诊断扩展；其余 A3 支持代码存在但不被旧 Bake 调用。两端使用同一 harness、同一诊断、同一输入。没有把旧提交的历史绿色结果标成这次重跑结果。

复现定向范围：

```sh
python3 Tools/DotnetHarness/generate.py
# 每个游戏分别执行；需按 README 先准备 .NET 8 和 Mathematics 依赖。
dotnet test Tools/DotnetHarness/.gen/SnakeFoundation.Tests.EditMode/SnakeFoundation.Tests.EditMode.csproj \
  --filter 'FullyQualifiedName~ConfigIsolationTests|FullyQualifiedName~SnakeGameplayTests|FullyQualifiedName~SnakeReplayTests'
dotnet test Tools/DotnetHarness/.gen/RpgFoundation.Tests.EditMode/RpgFoundation.Tests.EditMode.csproj \
  --filter 'FullyQualifiedName~ConfigIsolationTests|FullyQualifiedName~RpgGameTests|FullyQualifiedName~RpgSnapshotTests|FullyQualifiedName~RpgCombatTests|FullyQualifiedName~RpgAiDecisionTests'
# 两个程序集各有下列显式 .NET 诊断；普通/native 测试不自动运行它们。
# --filter 'Name=RecordConfigColdAllocationAndRetainedPayload|Name=RecordDefaultRawSnapshotDigest'
```

## 冷路径与常驻内存成本

Linux x86_64、.NET SDK 8.0.425 / Unity 桩。单线程、每配置一次代码预热后，在一次窗口保留 128 份默认 runtime；source SO、observer、delegate 和 retained 容器预先创建。使用项目 ManagedAllocationProbe，在窗口两端保留数组阳性对照均为 33,536 B，空操作均为 0 B，窗口 gen0=0。之后强制 GC 观测托管 retained delta。这里只是一批观测，没有性能收益或设备峰值声明。

| 配置 | 旧/新 128 份托管累计分配 B | 旧/新 GC 前批次 live heap 增量 B | 旧/新 GC 后 retained 增量 B | 每份 native 有效载荷 B |
| --- | --- | --- | --- | --- |
| Snake | 103,424 / 392,192 | 115,136 / 402,976 | 103,112 / 112,640 | 208 / 208 |
| RPG | 244,968 / 567,528 | 254,944 / 583,904 | 180,224 / 212,992 | 1,460 / 1,460 |

来源隔离增加托管常驻副本；检查也增加冷分配。这里**没有速度或分配优化收益**。Native 有效载荷是 `Length × UnsafeUtility.SizeOf<T>()` 的精确表项和，不含 allocator 对齐/头部、安全句柄、源 SO、World 表/网格、表现对象、JIT、共享字符串或 GPU。GC 前 live heap 是批次末端采样，并非启动峰值；GC 后增量会有运行时观测噪声，不能用差值反推精确对象大小。

需要在集成提交上继续执行聚合 harness、精确 Editor 原生 EditMode/Jobs/Burst 回归。实际 Unity 冷启动峰值、完整 Session 常驻、Android/iOS IL2CPP 内存和设备持续负载仍是外部门槛。本工作未运行或声称通过这些原生/设备检查；不得用此表代替移动内存预算。

## 集成审阅的有效边界修正

独立审阅发现新预检对两个原来合法的房间边界过严：`RoomSizeMax == min(Width, Height) - 4` 允许生成固定起点 `NextInt(2,2)`；`2×2` 房间允许怪物使用固定内部位置 `NextInt(1,1)`。预检分别改为 `<=` 和最小尺寸 `2`，仍拒绝超出边距或尺寸 `1`。

两个成对反例各先得到 1 失败 / 1 通过，修正后四项全部通过。其中 `2×2` 用例以 MonsterDensity=1 创建真实 Session/楼层并要求实际产生怪物，避免默认密度舍入成 0 掩盖出生路径。第一处修正还运行完整 RPG harness，86 项通过；最终集成全量结果另列，不把该中间结果套用到之后的调度合并。见[审阅修正记录](validation/LatiosIntegrationReview-20261007/evidence.json)。默认值、raw 写入/读取体和旧比较哈希不改。
