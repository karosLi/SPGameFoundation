# 移动端画面 / 战斗反馈打磨（Stage 3）

## 本轮边界

这是共享表现能力与两个示例的接入，不是复制广告素材。没有新外部素材依赖、粒子 GameObject、全屏 Bloom 或新后处理。没有改伤害、碰撞、AI、技能冷却、模拟随机数与存档布局。

- Shooter：枪口菱形亮芯 + 小面积柔光；细射线亮芯 / 柔边 / 接触点；命中短促火星与冲击环；机翼硬点、机身分色、机舱反光、重型机的引擎细节；击中变白改为保留颜色的短闪与约 3.5% 挤压。
- Guard / Survivor：平滑模式子弹采用透明混合，避免密集叠加变成白球；怪物保留材质色、击中闪白不超过 22%；约 4% 挤压；静止怪物不再强制两帧踏步；帧和摆动相位都来自稳定 handle + generation，不因 swap-removal 改变；轮廓收窄、敌人家族分色、布料/腰带/水晶细节；更暗的地面对比；细电环。
- 现有 Hit/Shoot 枚举不等于模拟已经发送事件。本轮从已有 Flash 状态只读收集命中表现，Guard 的 Burst job 每次至多写 48 个候选，不新建运行时事件或对象。事件路径仍支持 Hit、Death/Destroyed、Shot、Hurt，以及 opt-in 技能的 Nova。
- 肖像 Shooter 与竖屏 Guard 原有布局继续有效。横屏 Guard 的玩法/视口和技能 HUD 由独立模块处理；画质与布局不写模拟。

## 可复用接入

新增 `SPF.Presentation.Combat`：

1. 在已有 `SpriteAtlasBuilder` 中调用 `CombatVfxArt.AddTo`，把四张 64×64 原创解析遮罩打入同一张图集。已有 SpriteBatch / shader / 32 字节实例两档路径全部复用。
2. 加载期构造 `CombatVfxPool(96)`，预热效果 SpriteBatch；每帧先 `BeginFrame(dt, quality)`，暂停时传 0。
3. 由武器或技能自己的数据绑定一个 `VfxProfile`：自定 Id、颜色、优先级、寿命、核心尺寸、火星数、传播距离与合并半径。无需增加公共“武器枚举”。ShooterRenderer 的 ImpactProfile / ShotProfile / DeathProfile 和 SvRenderer 的 ImpactProfile / DeathProfile / PulseProfile 是实际示例绑定，可在 Inspector 配置。
4. `Emit(profile, position, nonzeroEventKey, scale, rotation)`。key 推荐高 32 位固定步编号 / 事件批号，低 32 位稳定来源编号；跨生命周期应包括 generation。0 明确关闭去重。适配旧数据时有意用三 tick 时间窗限制持续命中密度。
5. `Draw(batch, art.Resolve(sheet), actualWorldViewport)`。视口必须是实际视口，不能使用扩大的剔除边界。正常批次 Draw 完成渲染。
6. 重开 / 重绑 / 地图重置调用 Clear。去重历史只保证最近 2×capacity 个已接纳事件；这是一层表现防重，不是持久消息传输的 exactly-once 保证。

### 例：新冰系武器

```csharp
var frost = VfxProfile.Electric;
frost.Id = 201; // 仅是当前玩法内该 profile 的绑定 ID
frost.AccentColor = new float4(0.38f, 0.70f, 1f, 0.8f);
frost.CoreSize = new float2(0.34f, 0.52f);
frost.Sparks = 3;
// frost 在加载期保存到武器表现数据，后续每次命中不创建对象。
fx.Emit(frost, hitPosition, ((ulong)tick << 32) | sourceId);
```

不同 profile 的 Id 应不同；相同 Id 的近距离事件可在短窗口内合并。合并不刷新 Age，也不无限加亮，持续射击不能造出永不消失的大亮斑。高优先级效果可替换低优先级；同优先级满池直接丢弃。低优先级新事件最多用 75% 的单帧发射名额，为关键反馈预留空间；最后一个名额只留给最高优先级（例如主角受伤）。搜索尝试也有上限，避免极端事件洪峰把所有时间花在去重/合并。

## 预算与诊断

| 档位 | 活跃 burst 上限 | 新事件/帧 | burst sprite/帧 | 单 burst 火星上限 | burst 透明四边形面积/视口 | 柔光 |
|---|---:|---:|---:|---:|---:|---|
| 0 | 96 | 48 | 384 | 7 | 0.30 | 开 |
| 1 | 64 | 32 | 256 | 4 | 0.20 | 开 |
| 2 | 40 | 20 | 128 | 2 | 0.12 | 关 |
| 3 | 24 | 12 | 48 | 0 | 0.07 | 关 |

`VfxStats` 含 Accepted / Merged / Duplicates / Dropped / Evicted / Expired，以及当帧 Sprites / SpriteDrops / ScreenArea。拒绝表现从不回写伤害。池只处理视觉时间和自己的整数 seed，不取模拟 RNG。

发射处理最多检查固定容量的近期 key 与活跃 burst；优先级 0/1 最多 `4×Emissions` 次搜索，优先级 2 独立最多 `2×Emissions` 次搜索，优先级 3 再独立最多 `Emissions` 次搜索，其后 O(1) 丢弃。三个计数隔离，即使优先级 2 的请求全部是重复或被拒绝，也不能占掉主角关键反馈的尝试预算。

实际提交顺序：所有高优先级可读核心 → 冲击环 → 火星 → 可选柔光。副层先受限。画质调低时立即回收超出的低优先级 burst。屏幕外效果不提交。

### 带宽与覆盖量公式

- PackedSprite：32 bytes/实例。
- GPU batch API payload：`32 × N + 20` bytes（间接参数），空批次为 0。
- DataTexture：每实例 8 个 RGBA8 texel；`4 × textureWidth × textureHeight`，由已预热 prefix/page 的真实大小决定，不能只把 N×32 当真实上传量。
- burst 批次最多 384 sprites：GPU 12,308 bytes；DataTexture 16,384 bytes。真实 Unity 图像测试还会设置批次 Count=384 并断言这个 API payload。这是包含零尺寸尾部槽位的容量上传探针，不代表画面上有 384 个有意义的效果。实际五组分层效果另行渲染、记录真实 sprite 数并比较像素。
- pool 的保守透明覆盖代理：`Σ(abs(width × height)) / (viewportWidth × viewportHeight)`。含透明角与重叠区域。预算不是设备 GPU 毫秒、总场景 overdraw 或物理显存总线流量。
- Guard 持续的两个电环是独立的固定上限 gameplay telegraph：高档每环 64 段、低档 32 段，每段外线 + 亮芯；至多 256 / 128 个实例。其覆盖约为 `2π(Ra+Rb) × (0.09+0.022) / viewArea`，另加微小接缝补偿。环不是瞬时 burst，不能随普通命中洪峰消失。
- Shooter 持续射线最多 4 个实例；尾焰 2 个；它们也不进入 burst 池。高档额外覆盖约为 `beamLength × (0.30+0.085+0.035) / viewArea`，接触点另计。实体投射物继续用既有固定表容量/剔除，不由 FX 接纳结果决定。

每种玩法 renderer 的 `SpritesDrawn` / `BytesUploaded` 是整个场景各批次合计；应与 burst 的局部指标分开读。降低特效不能替代碰撞/敌群/排序/血条的独立预算。

## 验证入口

- EditMode：CombatVfxTests（容量、优先级、关键保留名额、过期、重置、防重、合并、确定性、四档覆盖预算、非法输入、零托管分配、解析 mask）。
- ShooterVfxIsolationTests：同种子同输入 90 tick，极端不同 FX 容量/档位/seed 使用后，模拟 snapshot bytes 一致。
- PlayMode：CombatVfxVisualTests.LayeredBurstPixelsAndPayloadMatchBothTiers，输出 `Artifacts/Screenshots/combat-vfx-gpu.png` 与 `combat-vfx-datatex.png`，比较实际像素，并记录局部上传数。
- 现有 ShooterPlayTests.PortraitFlowAndCancelOnBothTiers 与 SvGuardPlayTests.GuardPortraitRingsHealthQualityAndRestart 会生成实际玩法截图，覆盖重开与两档路径。
- 现有 ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality 继续负责真实引擎稳态分配与画质不改 snapshot 验证。

### 录制建议

Unity 2022.3 生成 Shooter / Guard 示例场景，选对应平滑美术；竖屏 540×960 或 720×1280。每档录制 10–15 秒：开火→单次命中→连续命中→密集击杀→暂停/恢复→重开；每段记录 VfxStats 与 Renderer.BytesUploaded。横屏 Guard 追加 1280×720 验证两环和信标同时可读。录制仅通过实际 Unity 游戏输出，不以静态合成图冒充 gameplay。

## 本地结果

本地 .NET Harness 已编译全部 71 个程序集（0 warning / 0 error），初次新增 EditMode 10/10 通过：CombatVfxTests 9 项 + ShooterVfxIsolationTests 1 项。最后回归还通过了共享 Sprite/Art/FX 33 项与 Shooter EditMode 全部 16 项（合计 49）。随后独立 review 增加了两个优先级 2 洪峰回归（重复/拒绝），修复最高优先级尝试名额被提前消耗的问题；修复后的 CombatVfxTests 11/11 再次通过，零分配断言仍为 0 bytes。独立 review 还要求修复怪物使用 dense row 作为动画相位的问题，新增 SvVisualMotionTests 检查 swap-removal 后姿势不变和静止 / 新 generation 的行为，2/2 通过。零分配断言测量的是预热后的 emit / 去重 / 过期 / 打包路径，包含 1,000 次循环，结果为 0 bytes。

真实 Unity shader / 图片 / 完整引擎 GC 验证待中央导入项目串行运行；不得把 .NET 桩通过写成实机或真实渲染通过。桌面 llvmpipe 软件渲染只能证明真实 Unity shader / 图片正确性，不能证明 Android / iOS 真机帧率、发热、功耗或帧时。

## 后续中央验证

真实 Unity 局部验证已通过 14 项 EditMode 和 7 项图形/玩法用例，完整 PlayMode 随后通过 94 项、跳过 1 项显式分配调用栈诊断。GPU/DataTexture 的分层特效与更新后的 Shooter/Guard 实际截图已检查。修复后的优先级洪峰与稳定实体相位回归保留。Unity 同步分配断言现改用经过 32/0 正负对照的 GC.Alloc 事件计数；1,000 次实际合并/发射/打包循环为 0 样本，不再把失效字节计数器的 0 当证据。范围和全帧剩余分配见 MobilePresentationAndHudCheckpoint.md。
