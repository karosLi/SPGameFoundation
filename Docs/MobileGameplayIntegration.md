# 移动端 2D 玩法接入与扩展边界

本轮给竖屏射击、守点幸存者切片补充可共用的技能形状、平滑贴图与接触阴影；现有九种玩法的默认点采样 atlas、30 Hz 模拟、快照与渲染层级不因此改变。代码入口见 `CombatShapes`、`SmoothSpriteArt`、`BlobShadow` 和 `BonePaletteMath`。

## 1. 当前能力与明确未实现的部分

| 能力 | 本轮可用 | 边界 |
| --- | --- | --- |
| 线段/光束、环带碰撞 | 无分配 CPU/Burst 兼容纯数学，闭区间相切算命中 | 不自动查询网格、去重或排序；不把视觉特效当碰撞体 |
| 平滑 2D 美术 | 原始绘制图超采样降采样；可读 Texture2D / RGBA 数组导入；双线性 atlas 独立挤出边距 | 示例仍是原创程序占位美术，不是截图原资产；没有自动商业美术导入器、动画编辑器或资产下载 |
| 接触阴影 | 一张软椭圆 mask，通过现有 SpriteBatch 两档绘制 | 不是 shadow map，不处理遮挡、投影接收面或光照方向 |
| 骨骼动画、两骨 IK | 已有 `Skeletal` / `SkeletonPoseJob` / `SkeletonSpriteJob`：CPU/Burst 采样与 cutout 拼装 | 本轮不修改已有骨骼核心；新切片是否使用骨骼由其 renderer 决定 |
| BAT / weighted mesh | 新增 CPU 仿射矩阵、bind-inverse 映射、帧采样参考及测试 | **没有** GPU 骨骼采样、权重网格蒙皮、矩阵纹理烘焙/上传、GPU IK 或相应 shader；不能把已有 GPU sprite instancing 称为 GPU 蒙皮 |

## 2. 模拟与视觉的所有权

- 游戏规则在固定 Tick 中读取输入快照、更新 AI、检测命中并产生反馈事件；渲染只能读取完成的模拟结果。对 session 数据的主线程访问仍须按已有约定 Sync。
- AI 使用有上限的目标查询、状态/冷却/计时器和稳定种子。网格只是 broad phase；窄相交测试必须使用权威位置/半径。不要每敌人全量扫描所有敌人或通过材质位置反推命中。
- 飞行物快速移动用上一个 Tick 到当前 Tick 的 swept segment；`BeamHitsCircle` 表示圆头 capsule，不是无限射线或平头矩形。`halfWidth` 是半宽，不是直径。
- 环形技能每次 Tick 是否伤害、是否一轮只命中一次、是否持续多段，由玩法系统保存状态。视觉圈的透明度、网格精度和生命周期不会隐式改变伤害。
- 候选顺序不保证稳定。单目标/最近目标命中必须明确比较距离或线段参数，完全相同再比较稳定实体 ID；不要用渲染排序或并行写入顺序解平局。形状 helper 本身不选择目标。
- 能力降级只影响呈现：关闭粒子、阴影、光照或未来 GPU 蒙皮不得改变移动、命中、掉落、随机序列、胜负与回放结果。

### 技能形状 API

命名空间 `SPF.L2.Combat`：

```csharp
CombatShapes.BeamHitsCircle(start, end, halfWidth, target, targetRadius);
CombatShapes.AnnulusHitsCircle(origin, innerRadius, outerRadius, target, targetRadius);
CombatShapes.BeamBounds(start, end, halfWidth, out float2 min, out float2 max);
CombatShapes.AnnulusBounds(origin, innerRadius, outerRadius, out min, out max);
```

规则：有限世界坐标；负半径视为 0；环带内外半径传反会规范化；内边界和外边界均包含；长度为 0 的 beam 是圆形；内外半径相等是闭合圆周。bounds 只包围技能本身。如果网格记录目标中心，查询 bounds 还必须向外扩最大目标半径，否则会漏掉边缘重叠目标。碰撞函数无托管分配，不读 Unity Physics 状态。

## 3. 非像素 2D 美术路径

命名空间 `SPF.Presentation.Sprites`：

```csharp
var large = new PixelCanvas(96 * 3, 96 * 3);
// 用 3 倍坐标/线宽绘制原创图形，加载时完成。
PixelCanvas smooth = SmoothSpriteArt.Downsample(large, 3);
var builder = new SpriteAtlasBuilder();
int frame = builder.Add(smooth, "pilot");
// 导入现有美术可用 builder.Add(readableTexture, "pilot")，或 Add(pixels,w,h,name)。
SpriteSheet sheet = builder.Build(1024, FilterMode.Bilinear, padding: 2, extrudeEdges: true);
```

- `Downsample` 在源色彩空间做 box averaging，先按 alpha 加权 RGB，再输出 straight-alpha；它不是 linear-light 色彩管理。覆盖率由平均 alpha 保留，透明源像素的 RGB 不参与颜色平均。
- 降采样后做一像素透明 RGB 扩散：不改 alpha，不改已有非零 alpha 像素；使用快照读邻居，结果与遍历顺序无关。可对导入的 `PixelCanvas` 单独调用 `BleedTransparentRgb`，降低透明黑边被双线性采样后的暗边。
- atlas 的 `extrudeEdges` 把 frame 边缘的**完整 RGBA**复制到各自独占的 gutter，不改变内容 UV。透明 RGB 扩散处理图形内部透明边缘；atlas gutter 处理相邻 frame 串色，两者不是同一步。
- 默认 `Build()` 仍为 Point / 1 px / 不挤出，保留旧图集排序、origin 与尺寸。平滑路径建议 2 px gutter；挤出开启时每张图两侧各有自己的 padding，不能与邻图共用。
- atlas 没有 mipmap。当前一像素透明 RGB 扩散 + gutter 只覆盖 mipless 双线性用途，不保证强缩小/各向异性/mipmap 路径无串色。Trilinear 选项不会凭空生成 mipmap。
- `maxWidth` 是 shelf packer 宽度限制；最终宽高向上取二次幂，因此非二次幂 maxWidth 不等于严格 GPU 纹理宽度上限。帧宽加双侧 padding 超出限制时报错；生产资源应在导入阶段检查平台 maxTextureSize 和整个 atlas 高度。
- `Add(Color32[],...)` 与 `Add(Texture2D,...)` 复制输入；Texture2D 必须可读。不会修改源 import settings 或销毁源纹理。`Add(PixelCanvas)` 保留 canvas 引用，与旧行为一致，Build 前不要意外改图。
- 构造 atlas、导入、RGBA 扩散与降采样均分配内存，只能在加载阶段做。图集为 RGBA32，保留 CPU 可读副本，`SpriteSheet.Dispose()` 释放其 atlas；加载峰值还包含输入和中间大图。正式美术可离线打包降低启动成本，需另行实现和验证。
- 半透明平滑边缘必须使用合适的 translucent 材质；opaque cutout 的 0.5 alpha 阈值会重新产生锯齿。角色有遮挡关系时由 renderer 稳定后向前排序；shadow / actor / weapon / additive / UI 用明确队列，不指望透明实例自动排序。

## 4. 共享接触阴影

`BlobShadow.CreateCanvas(64,32)` 生成白色软椭圆 mask，可加入同一 atlas。`BlobShadowProfile` 描述 Size、Offset、Color、HeightFade、HeightSpread；Default 是黑色 0.3 alpha、0.8 × 0.3 世界单位 footprint。

```csharp
var profile = BlobShadowProfile.Default;
BlobShadow.Add(shadowBatch, sheet[shadowFrame].Uv, groundPosition, depth,
    profile, height: jumpHeight, scale: actorScale);
```

height 非负；越高按 HeightSpread 变大、按 HeightFade 线性变淡。输入是地面投影位置，不是跳起后身体中心。depth 与 translucent 队列由 renderer 显式控制，通常在地面上、角色下。每个可见阴影占一个 32 字节实例；不可见、零尺寸、batch 已满返回 false。运行时调用不分配，不创建材质，不查询光源。

## 5. 骨骼、IK、BAT 的扩展合约

### 5.1 当前实际运行路径

`SkeletonAsset` 保存只读 native 骨架与 clip；`Animator2D` 保存状态；`SkeletonPoseJob` 在 CPU/Burst 采样、混合、FK，已有 `Skeletal.TwoBoneIK` 可在 CPU pose 阶段处理指定链；`SkeletonSpriteJob` 输出 cutout 的 PackedSprite。每个 attachment 仍是一个四边形，**没有顶点权重**。现有 GpuDriven 只把这些实例送到间接绘制，DataTexture 使用静态分页网格和 RGBA8 实例数据纹理。

武器/脚底等参与规则的 probe 必须在权威 Tick 的 CPU pose 中取得。渲染插值可重采样姿态，但不能反过来决定命中。当前两骨 IK 的父链索引 scratch 固定 16 层，脚本/导入资产应验证链长、正骨长和 parent-before-child；本轮没有扩展 IK 限制或提供自动约束编辑器。

### 5.2 本轮提供的 CPU 数学参考

- `Affine2D` 为两行 float3：`[m00,m01,tx]`、`[m10,m11,ty]`，TransformPoint 乘列向量 `(x,y,1)`。Compose(a,b) 表示先 b 后 a。
- FromBone 与已有 BoneWorld.Transform 的左右朝向镜像一致，包括镜像局部 Y。TryInverse 拒绝不可逆/非有限变换。矩阵来自同一模型空间时，`TrySkinningTransform(bind,pose)` 得到 `pose * inverse(bind)`，再把 character world placement 单独应用。
- `SampleFrames` 定义唯一采样边界：循环 F 帧采样 `[0,duration)`，不存重复终帧；非循环 F 帧包含头尾。循环负时间 wrap，非循环 clamp；零/非法 clip 元数据返回 0/0/0，调用方不能因此读取空 palette。
- `Lerp` 是矩阵逐元素插值的视觉参考，会缩短或剪切旋转；180° 混合可能塌缩。测试特意覆盖这个限制；不可用于权威 hitbox。需要保刚性的未来实现应比较旋转/位移采样方案与内存成本。
- 这些类型没有 Texture2D、GraphicsBuffer、shader、baker、weighted vertex mesh 或自动接线。CPU struct 的 24 字节布局**不是**已发布 GPU ABI。

### 5.3 将来落地 GPU 蒙皮时必须满足

1. 资源版本化：骨骼稳定索引、parents、bind/inverse-bind、clip 首帧偏移/帧数/时长/循环、采样率、坐标/单位/朝向、bounds、顶点权重数量与归一化约定。缺资源或版本不支持须回退；同名 clip 不足以确认兼容。
2. 可选择的视觉 backend 与模拟脱钩。保持 CPU/Burst cutout 或静态帧作已验证 fallback；不能强迫现有 SpriteTex shader 解新 palette。未来 GPU 实现应先定义 capability gate，再验证 Metal/Vulkan/GLES 的格式和顶点纹理读取。
3. 建议待验证布局为每个 bone matrix 两个 float4 行（各 xyz 存仿射行，w 明确定义/保留），每帧每骨 32 B。索引 `(clipFirstFrame + frame) * boneCount + bone`，纹理 linear/point、无 mip、不得经 sRGB 或有损压缩。新规范需要 CPU/GPU 对照读回测试后才能宣称生效。
4. 顶点蒙皮先在模型空间加权 `sum(weight * palette[bone] * bindVertex)`，再放到世界；归一化/零权重/非法索引/镜像/法线策略必须明确。和目前一个 attachment 对一个 bone 的 cutout 是不同资源管线。
5. 烘焙姿态可以减少每实例 CPU 采样，但动态瞄准、脚步 IK、受击叠加不能凭空来自离线矩阵。先选择 CPU override 或单独 attachment 保留动态链；绝不把烘焙视觉 pose 用作 AI/碰撞回读。
6. 验收包括 bind identity、单骨变换、镜像、循环最后帧、crossfade、极端旋转/尺度、超大 clip 索引、退化 bounds、CPU/GPU 误差与强制 fallback。还须证明能力开关前后模拟快照/回放一致。

## 6. 移动端预算与观测

以下是接入预算公式和建议上限，不是本轮真机实测收益：

- 当前 PackedSprite = 32 B。N 人 × A attachments 的 CPU 实例存储/有效上传是 `N*A*32`；1000 人 × 8 parts = 250 KiB/帧，仅有效实例在 60 FPS 就约 14.65 MiB/s，尚不含 shadow/FX/UI、间接参数和 driver。
- GPUDriven 的 BytesUploaded 包含本次提交的实例数据 + indirect args。DataTexture 每页最多 4096 实例，上传按实际 prefix 纹理向上取整：最小 256 实例是 8 KiB，满页 128 KiB。不能用 Count×32 冒充 DataTexture 实际 Apply payload；也不能称这些统计为硬件总线带宽。
- 建议从 1 张 ≤1024² 的 RGBA32 atlas/玩法、有限的 effect 容量开始；1024² 单 GPU 图约 4 MiB，可读 CPU 副本另约 4 MiB，降采样临时大图与原 source 另算。真正容量由玩法密度和目标机测量收敛；2048² 就是每份约 16 MiB。
- 未来假设 palette 32 bones × 60 frames × 8 clips × 32 B = 491,520 B（480 KiB），共享可减少每实例重复动画数据；如果 CPU 缓存和 GPU 纹理都留存要计双份。该估算没有把 CPU 当前 cutout 路径换成 GPU 蒙皮，也没有证明帧率提升。
- 动态 batch 在加载时 Warmup 到允许最大峰值，避免第一次跨 prefix 时创建资源；预热不是零成本。完整 DataTexture 纹理缓存比单满页小于 2 倍，CPU/GPU 副本都占内存，页面网格额外统计。静态地面 Draw(dirty:false)，不可对实际变化的数据跳上传。
- 移动端质量顺序建议：降低 FX、限制可见阴影、关闭昂贵光照、收紧视觉更新频率。不得用减 hitbox、AI 目标或伤害数量当透明的画质降级。
- 每轮采集实例数、各 batch BytesUploaded、CPU simulation / presentation、GPU 时间、托管 GC、native/texture/mesh 常驻与加载峰值。测试 GpuDriven 与 DataTexture、暂停恢复/多指取消、重开/回菜单、长时间发热；桌面编辑器测试不等于 Android/iOS 真机结果。

## 7. 验证入口与证据边界

- `CombatShapesTests`：beam 端点/零长/相切、annulus 内外边界/洞/交换半径、随机 broad-phase 保守性、稳态无托管分配。
- `MobileSpriteArtTests`：alpha 加权、透明 RGB 一像素扩散、不改覆盖率、参数校验、旧 packing、挤出 gutter 无重叠、阴影姿态/零分配。
- `BonePaletteMathTests`：镜像 FK 映射、逆矩阵、bind-space 到 posed-space、循环/非循环采样边界、非法数据、逐元素插值塌缩的已知限制。
- .NET harness 可验证纯数学、元数据和编译层次，纹理 stub 不保存 pixels；导入像素所有权与真实 atlas gutter 内容测试显式放在 Unity 专属分支中，不能拿 stub 通过当纹理验证。
- 实际运行结果由本轮合并验证报告记录。此文不声称以上所有目标平台测试已执行，也不提高既有 GC 阈值。
