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
