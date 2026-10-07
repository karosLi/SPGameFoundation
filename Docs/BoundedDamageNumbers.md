# 有界普通/暴击伤害飘字

日期：2026-10-07。此项是 Survivor / Brawler 既有玩法的可选接入，不扩充公共玩法枚举，不替换旧攻击、技能、动作或音频架构。软件和原生证据分别记录；编译、池数学和桌面截图都不能代替物理 Android/iOS 验收。

## 1. 取舍与来源

检查了现有 `SpriteEffects`、`SpriteFont`、`SpriteBatch`、`CombatVfxPool` 和两个玩法的伤害结算。原 `SpriteEffects` 的普通数字只提供固定池、环形替换和整数显示，没有目标代数、合并金额、优先级或完整数字预算；原 Feedback 混合声音/视觉用途，且由 renderer 清空，不能再作为第二个消费者的事件队列。

- 逐次创建 UGUI / TMP / GameObject：接入简单，但本项目不采用。对象池仍有 Transform / Canvas 更新与逐对象管理成本。Unity 的 [UI 性能建议](https://create.unity3d.com/Unity-UI-optimization-tips)讨论 Canvas 重建、批次与池化，这些成本不应随伤害次数增长。
- 复用已有图集数字与实例批：采用。`SpriteFont.DrawNumber` 直接拆整数，不生成字符串；`DamageNumberPool` 只维护固定值数组；`SpriteBatch` 按项目既有 GPU / DataTexture 两档提交。无需第二套字体依赖、每数字材质或新 compute 模拟。Unity [GC 最佳实践](https://docs.unity3d.com/2022.3/Documentation/Manual/performance-garbage-collection-best-practices.html)支持预分配、复用和避免热路径临时字符串；[RenderMeshIndirect API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html)是既有 GPU 批后端的官方接口来源。
- 伤害真值与表现分离：采用有界、只读多消费者结算日志；声音仍保留自己的消费路径。优先级只决定飘字保留，不反向影响命中、HP、死亡、技能或 RNG。

上述是架构选择的依据，不是本项目移动性能已达标的证据。

## 2. 接入入口与请求、接受、显示

- 竖屏：`SvGameBootstrap.CreateDamageNumbersExample()`，默认由 `SvConfig.CreateDamageNumbersExample()` 建立明确选配配置。可传入该工厂生成并修改过的配置；必须同时启用 composed pulse 与 damage facts。
- 横屏：`BwGameBootstrap.CreateDamageNumbersBelt()`；可传 `CriticalDamageRule.NormalOnly` 或明确的 `EveryNthHit/Multiplier`。沿用既有开始、摇杆、攻击/踢击、暂停、重开和菜单流程。
- Editor/脚本组合可使用 `SvMode.Create` 加新配置，以及 `BwMode.CreateDamageNumbersBelt`。日志写入仅发生在选择了此功能的模式；没有额外并行游戏循环。
- 保存使用 `SvDamageNumbersSave.Capture/Restore`、`BwDamageNumbersSave.Capture/Restore`，`runtimeId` 必须由应用提供同一受支持运行域。不同暴击规则/关键内容在修改 Session 前拒绝。

以下区分请求、真实结果与显示：

1. 原攻击/武器/技能产生请求，既有命中判定、目标去重和结算决定是否接受。
2. 选配 `CriticalDamageState` 仅对真正造成正 HP 变化的玩家攻击更新权威计数。默认规则是每第 4 次实际扣血的目标命中结算 ×2，明确属于新增可选玩法规则。同一范围技能可以对不同目标同时产生普通和暴击结算；这不是每第四次挥击。没有视觉随机暴击，也不消费模拟 RNG；`NormalOnly` 可以仅记录普通伤害。
3. `AppliedDamageFact` 在 HP 修改时记录：完整 `EntityHandle(Index,Generation)`、当时接受的世界投影位置、实际 HP 减少、真实 Critical 标志、固定 Tick 和全局序号。普通或暴击溢出 HP 的部分均不计入 Amount；零伤害、无效目标、已死亡目标不会冒出数字。浮点精度不足以改变 HP 的极小请求也不消费暴击进度。
4. 伤害池保留实际 float 数值，使用 double 累加同目标的金额；合并后再向上取整显示。例如 0.2 + 0.3 显示 1，不逐次四舍五入，也不显示 0。屏幕整数是明确的近似显示，不是新伤害结算。
5. 金额超过 999999 时显示 `999999+`；暴击前缀 `!` 保留为形状提示，即 `!999999+`。截断只影响显示，有 `DisplayOverflows` 计数；实际累计值仍保留。单标签最多 8 个 glyph，避免现有整数绘制路径的任意负数/10 位前缀边界。

Survivor 的结算窗口将 transient hit row 转为当时真实 handle，只记录对敌人的实际伤害；hero/beacon 是没有真实 registry handle 的资源，继续保留原有受伤反馈，不伪造目标 ID。Brawler 记录真实 fighter handle 上的武器、踢击和 AI 伤害，暴击进度仅由玩家的有效对外伤害推进，并使用原命中/投影坐标。标签出生后脱离目标，只沿出生点上浮；目标死亡、行交换或同槽复用均不会把旧数字挂到新角色。

## 3. 生命周期与多消费者

`AppliedDamageJournal` 是可选世界资源。两个游戏配方强制 2–512 个事实（513 及以上拒绝），默认 512；共享日志本身可供其他工作负载声明至 65536，但这不是两个游戏的每帧预算。默认普通 384 / 暴击 128 两条固定环形存储，共享全局序号；读取按全局序号稳定归并。普通洪峰不会挤掉暴击 lane。每条 lane 满后只覆盖自己的最旧事实，保留 `AcceptedNormal/Critical`、`OverwrittenNormal/Critical`、`HighWater`；每个 cursor 独立记录 missed。溢出不改变模拟。

每个 renderer 持有自己的 cursor，不清空日志，也不抢音频/其他 renderer 的事实。首次绑定从当前 head 开始，隐藏期间不积攒延迟飘字。Session 实例、TimelineRevision（即使恢复到同一 Tick）、World.LevelVersion 和日志自身 identity/revision 都参与失效；恢复/重开丢弃旧日志，重新绑定跳到当前 head。暂停传入 0 表现时间，既有数字冻结，重复读取不会重播。

日志不保存事件内容，暴击权威进度必须保存。旧工厂不声明新资源，避免即使“unsaved”资源的名字/marker也悄悄改变旧 raw snapshot。新 `SvDamageNumbersSave` / `BwDamageNumbersSave` 声明完整资源、表、系统、关键内容、暴击规则和容量；旧 weapon / E composed save adapter 不放宽兼容检查。新模式必须用对应的新 envelope 适配器。

## 4. 合并、优先级与密集可读性

- 合并键：同一个完整目标 handle + 同一种普通/暴击类型；不同代数、不同类型绝不合并。
- 合并窗口从第一条事实开始，固定 0.12 秒。同时检查权威 Tick 差 × 固定步长和标签视觉 age，防止慢帧一次读取不同时间的多个命中时错误相加。
- 合并不刷新 age、不延长寿命、不改出生 anchor。普通寿命 0.72 秒，暴击强调寿命 0.65 秒。
- 普通字采用象牙白和轻微的出生强调。暴击采用明亮暖黄、`!` 标记、快速放大收缩和上浮；新模式使用原创平滑描边数字图集。两者均只作轻微侧移，保留实际受击点的空间关联。无需辨色也可区分。
- 单目标有有限 lane；满时只允许新暴击替换最旧普通值。全局满时同样优先替换最旧普通值，同优先级保留先到者。普通 admission 只可消耗每帧新标签预算的 3/4，为暴击保留余量；普通/暴击查询尝试分别有独立上限。
- 整个标签一起提交，永不因 glyph 容量剩 2 而显示“12”冒充“1234”。暴击优先提交，固定面积预算限制透明四边形总面积；标签重叠时最多尝试三个局部纵向偏移，然后统计 `OverlapDrops` 并不显示该标签。下一帧仍可重新尝试，不修改事实金额。
- 字高按正交世界视口高度缩放：普通基础字高 2.5%、出生最多放大18%；暴击基础2.8%、出生放大2.15倍；两者在0.18秒内按平方缓出缩到基础字号。lane、出生抬高和有限局部偏移属于排版，目标 anchor 始终是实际接受命中的位置。

## 5. 移动预算与资源所有权

| 质量 level（越大越低） | 最大活跃标签 | 每目标 | 每帧新标签 | glyph | 透明 quad / 视口面积上限 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0 | 64 | 4 | 32 | 192 | 0.14 |
| 1 | 48 | 3 | 24 | 144 | 0.12 |
| 2 | 32 | 2 | 16 | 96 | 0.09 |
| 3 | 20 | 2 | 12 | 64 | 0.07 |

- 数字池默认固定 64，构造上限 256；无扩容、闭包、LINQ、每次命中对象/字符串。lane 搜索至多64，显示碰撞至多64×3×64；两个移动示例的日志容量均限制在2–512，故每帧事实读取至多512（共享日志类允许其他明确预算的调用方使用更大容量，最高65536）。预算是在此类二维移动玩法中的设计上限，不是任何设备都能达到的帧率承诺。
- 使用每 renderer 一个专用预热的192实例数字批，复用该实例拥有的游戏字体图集；不和冲击粒子竞争最后几个 sprite 槽。最多一个已有 tier 的批绘制调用。数字不使用 compute 更新，GpuDriven 表示实例渲染，不称为 GPU 伤害模拟。
- 192 glyph 的有效 packed 实例 payload = 6144B（32B/实例）。GPU 满额 API 上传为6164B（含20B indirect 参数）；DataTexture 满额前缀 padding 为8192B。实际 API 上传量须从 `BytesUploaded` 读取，与物理 GPU 总线流量、图集/mesh预热内存不同。
- 图集、批、材质和原生缓冲由 renderer bind/release 持有；禁用清瞬态但保留已预热资源，销毁/重建释放本实例资源。不销毁其他实例的图集。新模式的15个26×42 glyph在加载时由原创矢量笔画生成，使用解析 alpha 边缘、近黑描边和白色可染色填充；未压缩原始 glyph 像素共65520B（实际 atlas packing另计）。旧 SpriteFont 构造函数、3×5像素字形和所有旧模式默认输出保持不变。没有第三方字体/资产或OS字体依赖。
- 本项以横屏 Brawler / 竖屏 Survivor、既有安全区/HUD/触控和质量开关接入，不更改全局 Android/iOS Player 设置。物理机 CPU/GPU p95、持续热稳态、内存、实际触摸与 GLES/Metal 后端仍须单独测量。

## 6. 验证状态

当前工作分支：从 `94a0630` 隔离。精确最终提交和结果在完成后补充。

已编写的自动化覆盖包括：浮点实际金额合并、权威时间窗、普通/暴击/代数分离、稳定替换、普通洪峰下暴击保留、每目标/每档/glyph/面积/重叠预算、整标签显示、显式超大金额、暂停与 timeline/level/session 失效、预热分配阳性/空对照。Runtime 测试覆盖两条真实结算路径、暴击保存恢复和旧 raw fixture。原生 PlayMode 使用两个真实游戏的攻击与数字批，分别保留 GPU/DataTexture 的 glyph 像素和连续采样证据入口。

完成标准：当前精确源码的完整 .NET harness、Unity API+netstandard2.1 编译、经典19组合对照、独立审查通过；原生图形/连续片段必须实际运行并检查后才能标“通过”。尚未运行、被图形设备跳过、纯数学或仅编译成功均不等价于视觉验收。此段在验证期间保持诚实，未填入的原生/物理机证据仍属待验证。


## 7. 用户参考片段后的表现修订

用户2026-10-07提供的片段中，约6.47–6.90秒的一组暖黄色数字先大后迅速缩小，并主要向上移动。逐帧观察约0.47秒寿命、0.17–0.20秒收缩、起始填充约30px到稳定约12px；源画面同时存在普通浅色数字和单独红色“暴击”文字，所以不能从黄色反推源游戏的暴击规则。本项目只借鉴快 pop/收缩、深色描边和空间关联，仍由自己的权威 Critical 标志决定类型，也保留自己的有界合并和遮挡策略。没有复制源字体/贴图。

本次独立表现提交将强调字寿命设为0.65秒、pop2.15倍/0.18秒收缩，以平衡本项目相机中的阅读时间与拥挤度；不是宣称一比一复刻参考。新增 `SmoothNumberGlyphs` 为加载期原生图集资源，`SpriteFont.CreateSmooth` 复用原来的数值拆字/批提交热路径。对15个字形分别检查透明边缘、抗锯齿覆盖、墨色描边、白色填充和独特轮廓；对快收缩/上浮/结束、旧字体尺寸、预热零分配做自动化。

离线原始字形预览只供资源审视，不是 Unity 像素证据。最终原生1/混合/密集、高低档、两个后端和连续片段仍需在集成提交上采集并审视；该视频同时提出的全身动作问题属于独立动作审视，不由飘字改色解决。
