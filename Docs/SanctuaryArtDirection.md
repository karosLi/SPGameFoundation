# 青铜遗迹 / 青绿灵光：第一轮实际美术落地

## 范围与视觉层级

本轮直接改动实际玩法的表现层，不是独立效果图。四个优先原型共享一套原创的青铜遗迹美术语言：

| 原型 | 已接入的场景与主体 | UI |
| --- | --- | --- |
| 竖屏飞机 | 俯视河谷、石质遗迹岸线、中心低对比水面；保留陶瓷白飞机与珊瑚红敌机 | 青铜镶边卡片、暖白字、低饱和背景 |
| 横屏纵深动作 / 武器 | 手绘群山遗迹远景、前景青石战斗台、低对比地面纹章 | 紧凑状态卡、换装按钮、四技能珐琅圆章 |
| 守点怪潮 | 青石庭院、固定地面罗盘、象牙石柱与青绿晶核信标、暖白主角与暖色怪群 | 生命 / 信标分组标记、顶部经验条、升级卡片 |
| 飞剑 / 武器怪潮 | 同世界观庭院，优先让人物、剑阵和伤害反馈可读 | 安全区摇杆、技能充能、冷却弧、武器图标 |

角色：保留自然角色的 14 骨骼、14 附件、IK、武器握持和攻击时序。英雄改为暖象牙陶瓷护甲、青绿内衬和小面积青铜扣件；怪物改为珊瑚红护甲、暗酒红内衬、骨色犄角。加宽胸甲、上臂、小腿和足部附件，保持骨骼长度与接触点不变。怪物胸部比英雄更宽，避免只靠颜色识别。

场景对比和饱和度低于人物 / 弹幕；中心留出清晰战斗空间。纹章属于低对比地面装饰，不改变碰撞、通行或伤害规则。危险提示和技能冷却仍由真实玩法状态驱动。

## 参考的品类惯例

研究参考的是布局、信息密度、剪影和场景层级，没有复制角色、贴图或商标：

- [Archero 官方产品页](https://www.habby.com/game/detail/archero) 和 [Habby 的 Google Play 产品页](https://play.google.com/store/apps/details?id=com.habby.archero)：移动端俯视动作，清晰主体与场地边缘的装饰层级。
- [Survivor.io 官方发行页面](https://play.google.com/store/apps/details?id=com.dxx.firenow)：密集群怪时保持主角、经验拾取与范围反馈辨识。
- [Playdigious 的 Streets of Rage 4 产品页](https://play.google.com/store/apps/details?id=com.playdigious.sor4)：横屏纵深战斗、手绘场景、保留战斗带并把移动控件放在边缘。
- 对比了项目此前真实 Unity 的 `weapon-belt-1004-right-fallback.png`、`survivor-sword-horde-portrait-gpu.png` 等截图：此前背景平铺明显，细小人物与背景同属青蓝，顶部状态文字过大，控件欠缺层级。

以上是本项目的设计判断；不把商店宣传截图当作性能测量，也不根据广告推断实际玩法承诺。

## 资源与接入

资源位置：`Assets/SinglePlayerFoundation/Presentation/ArtDirection/Resources/SPF/ArtDirection/`。

- `SanctuaryVista.png`：恢复并继续使用本次任务先前生成的原创远景，1536 × 1024 源图。
- `SanctuaryGround.png`：恢复并继续使用本次任务先前生成的原创青石地面。
- `SkyRiver.png`：本轮由内置图像生成工具生成的原创俯视河谷，1024 × 1536 源图。
- `.png.meta` 固定最大导入尺寸 1024、双线性、Clamp、关闭 Read/Write 和 mipmap、启用平台压缩。源 PNG 保留用于后续美术迭代，运行时不按源尺寸创建第二份纹理。
- `SanctuaryBackdrop` 直接进入 `BwRenderer`、`SvRenderer`、`ShooterRenderer`。资源缺失时保留原表现，原像素模式不强制加载庭院资源。
- `SanctuaryUiTheme` / `SanctuaryPanelGraphic` 作为共享 UGUI 风格入口。面板、细边框、技能纹章由保留网格绘制，不新增 UI 贴图或逐帧 Update。

`SkyRiver.png` 最终生成提示词（内置工具）：

> Use case: stylized-concept. Asset type: production background texture for a top-down portrait mobile flying-shooter game, not a mockup. Original painterly bronze sanctuary fantasy art. 1024x1536 portrait. Camera straight down, a huge muted deep-teal river canyon with very low-contrast flowing water in the central 70 percent so tiny planes and coral bullets will read clearly. On left and right edges only, broken ivory-stone ancient temple terraces, little bronze inlays, hanging foliage, soft cloud shadow, a few waterfalls. Rich hand-painted material detail on the sides, atmospheric soft detail center. Continuous top and bottom edges with water and isolated ruin ledges, suitable for vertical scrolling; no horizon, no perspective facing sideways. Palette subdued blue-green, antique gold, cream stone; lighting upper left. No characters, aircraft, projectiles, UI, text, logos, borders, or frame. This is original game environment art to integrate in Unity, one opaque texture.

早先两张恢复资源的原始完整提示词不在本工作树，不能重新构造为历史记录。

## 性能约束

- 背景每个实例固定 32 字节，复用已有 GPU-driven / DataTexture 渲染后端。
- 地面容量固定 64，装饰容量固定 64，远景最多 1 张；启动时预热，不为场景装饰创建逐个 GameObject。
- 飞机背景是连续滚动的最多 3 个不透明板块，交替垂直镜像连接，不用全屏透明混合过渡。
- 庭院用大块镜像拼接减少重复与接缝。横屏使用一张远景与一张前景，地面镶嵌单独一个小图集批次。
- 每场景导入贴图的保守 RGBA 上界为 8 MiB，原生 Unity 资源测试验证此上限。这里是资源预算，不是移动设备实测显存或帧率。
- 自然角色图集仍保持原来未装备 1 MiB / 装备 2 MiB 的 RGBA 预算；14 附件不增加 draw instance。
- 所有纹理构建只发生在 Bind / 加载阶段；新增场景 Draw 无托管数组、列表、字符串或每实体对象创建。
- 保留品质档的角色数量、阴影、VFX 限额；没有放松任何 GC、性能、玩法或输入测试阈值。
- Resources 加载的纹理是共享 Unity 资源，实例释放只销毁自己持有的批次和程序图集，不销毁共享贴图。

## 验证状态与下一步验收

本地 `.NET` 全程序集编译通过（0 error），完整 harness **849 tests passed / 0 failed**。日志：`art-direction-validation/build-verified.log`、`art-direction-validation/tests-verified.log`（任务验证附件，不作为 Unity 证据）。新增 `SanctuaryArtDirectionTests` 覆盖角色亮度分离、加宽剪影、骨骼映射及原图集预算；原生 Unity 额外检查资源实际导入、不可读、最大尺寸和每场景贴图预算。

美术验收必须查看最终集成提交在真实 Unity 中的图片 / 视频。已有四类 PlayMode 捕获会自然覆盖本轮资源，重点复查：

1. 横屏远景与战斗带相交位置、移动控件和敌人遮挡、宽屏安全区。
2. 怪潮密集帧中的主角辨识、赤红敌人和友方特效的区分、血条位置。
3. 飞机纵向滚动的连续性、中心弹幕的对比度。
4. 升级、暂停、回菜单、重开、换装与冷却按钮重复操作。
5. GPU / DataTexture 两档，以及低画质的实际资源和帧时。

在原生捕获完成前，不将生成图片或离线合成图标为“Unity 实测完成”。移动设备发热、帧率和输入手感仍需真实 Android / iOS 验证。

## 移动端交付门槛与资源预算细化

本节适用于所有交付，不只四个示例：目标是 Android / iOS；飞机、守点和飞剑优先竖屏，纵深动作优先横屏。桌面是开发、原生图形回归和录像环境，不是移动端达标证据。基准目标是稳定 30 fps，支持的较高档设备可选择 60 fps；必须先指定实体设备和画质档，再用持续场景的 CPU/GPU p50、p95、最坏帧及发热后的结果验收，不能引用桌面时间直接承诺手机结果。

| 资源项 | 当前明确上限 / 行为 | 证据边界 |
| --- | --- | --- |
| 新环境贴图 | 每个环境保守 RGBA 尺寸上界 8 MiB；源图最大导入边 1024、不可 CPU 读取；不在运行帧生成纹理 | 原生资源导入测试覆盖 3 个场景；平台压缩后的实际驻留显存仍需设备分析 |
| 角色图集 | 未装备 1 MiB / 装备 2 MiB；14 附件，不增加骨骼、每角色实例或 Animator | 图集和骨骼合约测试；不代表整场景总内存 |
| 新材质 / 批次 | 飞机背景 1 个有效材质批次；庭院地面与镶嵌 2 个；横屏远景、前景和镶嵌 3 个 | 小于 DataTexture 单页 4096 上限，两档无需按装饰对象拆 draw；不是整场景 draw-call 数 |
| 网格 / 缓冲 | 不透明地面容量 64、远景容量 1、镶嵌容量 64；启动预热；GPU 以 32 字节实例记录提交 | `SpritesDrawn` / `BytesUploaded` 统计实际有效数量和 API 提交；包含前缀填充的 DataTexture 上传不等于有效实例 × 32，也不等于物理 GPU 流量 |
| UI | 不增加 UI 贴图、独立材质或逐帧组件；铜框、圆章由缓存网格生成 | 颜色 / 冷却变化会使必要网格变脏；需继续记录 Canvas rebuild 与实际移动端 CPU 成本 |
| 背景覆盖 / 过绘制 | 飞机和庭院基本为一层不透明板块；镜像接缝仅微小重叠。横屏远景被前景覆盖处两层。镶嵌、UI、影子和 VFX 是额外局部覆盖 | 禁止改为整屏透明交叉淡入来掩盖接缝；实际 overdraw 图和 GPU tile/fill 成本待设备采集 |
| 能力回退 | GPU-driven 与 CPU/Burst/DataTexture 共用同一资源；资源缺失保留旧背景；像素模式保持原路径 | 回退只改变表现，不改变伤害、碰撞、攻击帧、冷却、掉落或结果 |
| 生命周期 | 释放实例批次和程序图集，保留共享 Resources 纹理；重建、多实例不销毁别人的资源 | 需要原生重建 / 内存曲线，并另查后台恢复与设备内存压力 |

附加的动画、弹丸、粒子、命中特效、工具和文档同样受 `AGENTS.md` 的移动端规范约束。对新基座能力先保留官方研究来源、备选实现、适用条件和代价，再实现和测量。本轮美术本身不改动作时序、步态修正或碰撞权威。

## 原生视觉审视记录：94c5d180

2026-10-07 的原生 Mac Unity 运行 `37571142177`：EditMode 882 通过 / 5 显式跳过；PlayMode 141 通过 / 2 失败 / 1 显式跳过。此提交**不能标记完整验收通过**。

- 已实际查看 `shooter-playing-gpu.png` 和 `shooter-playing-datatex.png`：540 × 960，河谷 / 侧岸遗迹和青铜 UI 已真实载入；中心飞机、友方细长弹丸、敌机、光束和两种拾取可辨，两个后端的这些静帧无明显视觉差异。静帧未证明滚动连续性、持续帧率或触摸手感。
- 庭院、守点、飞剑、横屏动作及其连续动画的完整新素材尚未取回，不能把此前旧美术截图视为这次的通过证据。该次完整 artifact 打包超过旧上限；扩大并验证证据分片属于后续 CI 修复，获取后还需逐项审视。
- 原生暴露了取消态的真实可读性问题：图标、环和 `CANCEL` 同时变为低亮度珊瑚色，在 DataTexture 捕获中缺少清晰浅色标记。后续修复保留图标 / 环的珊瑚警示，只把文字恢复为高亮象牙白；保留原像素阈值和输入取消断言。
- RPG 首局也暴露了请求与固定 Tick 的镜头时序：点击时的 Snap 可能被菜单目标提前消费，实际生成英雄后仍在平滑追赶旧目标，首帧剔除掉英雄。后续修复在表现层观察新英雄 / session / LevelVersion，在相机移动和渲染剔除之前重置镜头；不改模拟，不增加测试等待来掩盖问题。
- 上述后续修复必须在新的精确提交上跑原生回归，不能把本地 .NET 或 94c5d180 的部分通过当作修复验收。

## 弹丸 / 命中特效与美术边界

弹丸和角色碰撞规范由共享战斗 / 动作实现维护；本规范约束可读性和资源，不另建一套伤害逻辑：

- 子弹、箭和法术弹分别保留可辨的细长核、箭杆 / 箭头方向、圆核 / 符文轮廓；友方以青绿 / 暖白为主，敌方以珊瑚 / 暖红为主，不能只靠颜色，须保留轮廓和运动差异。
- 出生点、发射朝向与标准 muzzle / grip socket 对齐；脱离后沿权威弹道插值。拖尾回收、取消、换装、死亡、重建和 owner generation 必须清理旧绑定，不能重播上一轮的特效。
- 图像轮廓和 hurtbox / hitbox 的设计范围一起审视：角色身体、地面投影、跳跃高度、近战扫掠、弹丸半径分别画在调试捕获中。装饰犄角、飘带、光晕和余烟不默认扩大伤害范围。
- 命中闪光 / 冲击方向 / 击退来自固定 Tick 的真实接触事件；挥空可以有挥动拖尾，不能播放接触爆点或伤害字。粒子重叠或灯光亮度不决定伤害。
- 角色 / 武器 / 弹丸轮廓优先于烟雾、扩散光晕和装饰余粒。低画质优先保留弹丸核、接触提示、必要危险边界，削减尾迹段数和次要覆盖，不改变模拟命中。
- 继续使用[固定粒子预算](BoundedWeaponParticles.md)、[权威武器合约](AuthoritativeWeapons.md)和[武器动作表现](WeaponMotionPresentation.md)。新增弹丸 / hitbox 规范与实测由对应能力文档记录；未完成的资源或未查看的 debug 捕获明确记为待验证。

相关官方依据：[Unity 2022.3 纹理导入设置](https://docs.unity3d.com/2022.3/Documentation/Manual/class-TextureImporter.html)、[Unity 纹理数据与 CPU 可读副本说明](https://unity.com/blog/engine-platform/accessing-texture-data-efficiently)、[Unity UGUI 性能建议](https://unity.com/how-to/unity-ui-optimization-tips)。本项目据此选择导入时限尺寸 / 压缩、无需读回的背景关闭 Read/Write、静态 UI 网格与尽量小的透明覆盖；不把通用建议当作本项目的测量结果。

并行的[移动动作与弹丸协调审视](MobileActionCoordinationAudit.md)记录新 projectile 美术的具体状态矩阵、socket 和命中语义，合并时按其精确提交核对。该实现草案在原装备图集中加入箭杆 / 箭头 / 尾羽及小法术核，每发仍为现有批次中的一个 quad；32 发的有效打包记录为 1024 字节，DataTexture 前缀填充另计。箭的画面高度增加以容纳尾羽，必须承认覆盖面积增加；配置碰撞圆仍以权威中心为准，不自动等于箭头像素轮廓。1024 / 256 粒子容量、32 socket、64 command 以及 12% / 7% 保守 quad 面积预算保持不变；新弹丸实际原生像素和遮挡 / 命中对齐审视尚待对应提交采集。

## 完整取回后的原生审视：54477736

[运行 37572161333](https://github.com/karosLi/SPGameFoundation/actions/runs/37572161333)的 23 个证据分片已全部下载、按声明大小与 SHA-256 校验，并由 `Tools/ci/evidence_parts.py restore` 恢复：1038 文件，ZIP 371,637,879 字节，完整 SHA-256 `8a8f2b29878578f39076008e0519a468d33dc75dbc2d972d480a76bc440d9094`。这是新美术 / 旧步态的基线，不是后续修复的结果。

精确 XML：EditMode **882 passed / 0 failed / 5 skipped**；PlayMode **140 passed / 3 failed / 1 skipped**。失败为取消态 DataTexture 像素对比度，以及两个后端的 Horde 武器采集 fixture 使用 kind 0 触发 index -1。此运行没有有效的 grounded-Horde 步态基线。RPG 开局此轮通过，但不能否定 94c5d180 暴露的请求 / Tick 时序竞争；修复仍须独立回归。

实际查看的两档截图均位于恢复后的 `Artifacts/Screenshots/`：

| 范围 | 文件 | 观察 |
| --- | --- | --- |
| 横屏纵深动作 | `MobileHud/belt-landscape-depth-ready-{gpu,datatex}.png` | 1280×720；远景栏杆与可走地面衔接清楚，两张像素一致；暖白英雄与珊瑚怪物比旧同色细剪影更易分辨 |
| 飞剑怪潮 | `MobileHud/survivor-sword-horde-portrait-{gpu,datatex}.png` | 720×1280；剑环中心与主角可辨，青石和纹章真实加载；密集全量血条及较高状态卡仍让上方画面拥挤 |
| 守点怪潮 | `guard-portrait-{gpu,datatex}.png` | 720×1280；石质信标 / 青绿核与友方范围圈可辨，怪物轮廓有类别差异；保留程序 cutout 风格，不宣称已达最终商业美术验收 |
| 飞机 | `shooter-playing-{gpu,datatex}.png` | 540×960；河谷中心足够安静，飞机、细弹、光束、拾取及青铜 UI 可辨；这些静帧没有证明纵向循环所有相位 |
| 取消输入 | `MobileHud/survivor-portrait-safearea-cancel-datatex.png` | 真实确认 CANCEL 文本与图标同色导致低亮度；后续只恢复文本亮度，保留警示色和原断言 |

两份 `WeaponMotion/grounded-belt-live-{gpu,fallback}` 各含 160 原始采集帧、`acquisition.csv` 和 `gait.csv`。已按真实 PTS 编码 1× 视频，未插帧：GPU 采集跨度 7.623935042 秒、20.855 Hz；DataTexture 7.358870042 秒、21.607 Hz；编码时间戳最大误差 0.0005 ms。各视频 161 帧是 160 原始帧加一个末帧停留，不是生成运动帧。同步 Editor readback 影响采样速度，不能将这些 Hz 称为手机帧率。

基线 CSV 中，筛选真正移动且非权威跳跃的 Walk 样本，GPU 英雄 / 两个敌人分别有 22/111、17/85、20/66 个双脚非支撑样本；DataTexture 为 12/85、14/79、19/71。该证据交由步态修复验收作匹配比较；不在美术层压低身体 bob、改变镜头或后期插帧掩盖。新 grounded-Horde 只能交付新版本实采，不能伪造缺失的旧版配对。

当前结论：原创场景、UI 与角色改动已经进入四类真实玩法，可确认集成的第一轮视觉提升；取消态、动作连续性、新弹丸 / 命中，以及密集 UI 的最终取舍仍按各自精确提交继续验证。没有把概念图、源 sprite 预览或旧截图冒充修复后的运行证据。

## 竖屏密集战斗状态卡收紧（后续修复）

对 54477736 的真实怪潮截图继续修复已发现的遮挡，不停留在文档备注：

- 移动 HUD 默认移除 `Enemies / Bullets` 引擎诊断行，保留可玩的时间、等级、击杀、信标 / 波次目标、当前武器和待换装信息。`SvHud.ShowDebugTelemetry` 可由 Inspector / 开发工具显式打开；非移动旧 HUD 保持原计数。
- 卡片高度改为 `62 + 24 × 实际状态行数` 个参考像素：守点 / 飞剑普通两行为 110 px，带武器三行为 134 px，替代原来的固定 204 px。标准两行卡减少约 46% 的面板覆盖。诊断打开时只增加一行，并在关闭后恢复紧凑高度。
- 字体为 21 px，状态文字矩形同样按真实行数限高；标签、血条和文字统一使用安全区顶部的固定参考像素偏移，避免长屏把内容拉散到卡片外；不占用战斗中央空白，不延伸到右上菜单点击区。生命 / 信标条、经验条、安全区、技能标签、摇杆、技能取消和 GameObject 名称保持。
- 不新增贴图、材质、UI 对象或逐帧字符串。移动状态字符缓冲在 Build 时预热至 256 字符，防止中途显示诊断或数字位数增长触发常见的容量扩张。布局仅在初建或明确诊断开关变化时调整。
- 原有原生捕获加强了卡片高度、文字边界、菜单分离、两行默认内容、诊断开关与快照不变断言；增加 `survivor-portrait-compact-restored-{gpu,datatex}.png` 和 720×1600 带安全区的 `survivor-portrait-compact-tall-{gpu,datatex}.png`，同时保留原 safe-area / aim / cancel 图片及原亮度阈值。切换后仍须捕获实际文字和点击命中，不能用隐藏 HUD 通过。

验收：本地全程序集编译 0 error，完整 harness 849 passed / 0 failed；精确 3f6362f 的原生两档普通 / 合成刘海 / 诊断恢复 / 720×1600 长屏截图与武器三行图已实际查看，相关测试通过。这个具体的紧凑 HUD 问题可以关闭；[完整证据与限制](CompactMobileHudValidation.md)保留独立的 EditMode 失败、重型怪早期 UI 遮挡及物理设备门槛。54477736 的大面板截图只作为前态。

## 后续原生确认：4af9b87

精确 `4af9b87cc32fcd1a5f10a4bd2817848044ad4649` 的完整 29 分片已取回并校验，archive SHA-256 为 `2a62d8ad4776279f77b9a170ab4a4725c1134909d00c0eb1e3305557b6b6232d`。两档 portrait capture、`CancelKeepsHighContrastText`、`DelayedNewFloorSnaps` 和 `NewGameShowsTheDungeonAndTheHud` 全部通过；逐帧实际查看 fallback CANCEL PNG，象牙白文字清晰、箭头/环保持珊瑚色，RPG run 图中英雄可见。这确认了 a03c9a4 对应的两项真实修复，不再只依赖本地构建。

该头完整 graphics PlayMode 为 148 passed / 1 explicit diagnostic skip；EditMode 还有两项独立 separation candidate 精度失败，不能称全绿。新的 compact HUD 5dfb807 在它之后，仍需新原生 normal/tall/武器三行截图；密集重叠的角色/血条也不能从稀疏画面推导为已解决。新的弹丸轮廓 native cases 已通过，后续箭头碰撞前缘枢轴调整、debug overlay 和真实移动/命中可读性仍依各自精确源码验收。总状态见 [MobileFoundationFollowupValidation](MobileFoundationFollowupValidation.md)。

上述 4af9b87 段落保留当时边界；后续 3f6362f 的 compact HUD 验收已见上文及 [CompactMobileHudValidation](CompactMobileHudValidation.md)。
