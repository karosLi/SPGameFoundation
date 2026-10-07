# Latios 适配 Unity 2022.3.62f2：版本基线、回迁边界与验证计划

研究日期：2026-10-07。本文补充并限定[前一份分层研究](LatiosLayeringExtensionReview.md)的采用结论。**Unity 6 是当前上游版本的支持边界，不是 Latios 无法适配 Unity 2022 的技术证明。可以建立 2022 兼容分支；先验证历史基线，再按需要回迁新版功能，是更可控的路线。**

本轮只查官方源码、历史 tag、作者样例与 Unity 文档，并更新文档。没有安装 Latios、创建 Unity 试验项目、改变 SPF 依赖、运行 Unity/IL2CPP、复制实现或发布上游 fork。下文“可行”分别标注为历史支持证据、源码判断或待实测，不把三者混为一谈。

## 1. 已核实的关键事实

1. **有现成的 2022 基线。** [v0.11.5 README](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/README.md) 明确最低 Editor 为 **2022.3.36f1**，目标 **Entities 1.3.5**，要求 `ENTITY_STORE_V1`。2022.3.62f2 高于这个最低版本，但本轮尚未在该精确 Editor 上跑验证。
2. **Unity 2022 支持是在 v0.12.0 移除。** [固定版本 CHANGELOG](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/CHANGELOG.md) 将该变更列在 v0.12.0，而不是 Entities 1.0/1.2/1.3 本身不支持 2022。Unity 官方 [Entities 1.0](https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/index.html)、[1.2](https://docs.unity3d.com/Packages/com.unity.entities@1.2/manual/index.html)、[1.3](https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/index.html) 文档都提供 2022.3 支持依据。
3. **旧版不仅是 metadata 声称兼容。** v0.11.5 的 Kinemation [renderer](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Kinemation/Systems/LatiosEntitiesGraphicsSystem.cs#L294-L303) 和 [culling/dispatch](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Kinemation/Systems/LatiosEntitiesGraphicsSystem.cs#L575-L604) 保留 Unity 6 与旧 Editor 的条件编译实现，是当前回迁可以参考的真实路径。
4. **上游不维护旧发行线。** [当前 README 的维护政策](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/README.md#L271-L281) 说明旧版本不会继续发布 patch。选择 0.11.5 的代价是接管所需修复，不是获得一条仍由上游维护的 2022 LTS 分支。

## 2. 版本与依赖矩阵

下表的 Latios/Editor/Entities/Graphics/Burst 主声明来自对应 tag 的 `package.json`。Collections/Mathematics 是作者样例 lock 中对应 **Entities 版本的依赖记录**，不是在 SPF 或 2022.3.62f2 本次解析出的 lock。缺少已核实传递依赖的格子明确保留未知。

| 基线 | 最低 Editor 声明 | Entities | Entities Graphics | Burst 声明 | Collections / Mathematics | 对 2022.3.62f2 的判断 |
| --- | --- | --- | --- | --- | --- | --- |
| [Latios 0.7.1](https://github.com/Dreaming381/Latios-Framework/blob/v0.7.1/package.json) | 2022.3.0f1 | 1.0.10 | 1.0.10 | 1.8.4 | 此轮未锁定传递版本 | 证明早期已有 2022 路线；不推荐倒退到这里作新 fork 起点 |
| [Latios 0.10.7](https://github.com/Dreaming381/Latios-Framework/blob/v0.10.7/package.json) | 2022.3.13f1 | 1.2.1 | 1.2.1 | 1.8.12；Entities 1.2.1 自身要求 1.8.13 | 2.4.1 / 1.3.1（Entities 依赖） | 可作为 1.2 对照线；不能把 Latios 的较低 Burst 声明当作最终解析版本 |
| **[Latios 0.11.5](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/package.json)** | **2022.3.36f1** | **1.3.5** | **1.4.2** | **1.8.18** | **2.5.1 / 1.3.2** | **优先验证的历史支持基线；未实测精确 Editor** |
| [Latios 0.12.0](https://github.com/Dreaming381/Latios-Framework/blob/v0.12.0/package.json) | 6000.0.23f1 | 1.3.9 | 1.4.6 | 1.8.19 | 2.5.2 / 1.3.2（对应 Entities 的样例 lock） | 此版起移除 2022 支持；保留 Entities 1.3 不代表整个 Latios 仍受支持 |
| [Latios 0.16.1](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/package.json) | 6000.3.8f1 | 1.4.8 | 1.4.21 | 1.8.30 | Latios 未直接声明；此轮未核实完整解析锁 | 当前源码回迁对象；有实际 API 差异，不能只改 minimum version |
| **SPF 当前 manifest** | **2022.3.62f2** | **未声明** | **未声明** | **1.8.27** | **2.1.4 / 1.2.6** | **当前不是 Entities 项目；URP 14.0.11** |

传递依赖依据：作者 [LatiosComparison 的固定 lock](https://github.com/Dreaming381/LatiosFrameworkMiniDemos/blob/f46281b4cbdbbe1bceacc75284500204735a6607/LatiosComparison/Packages/packages-lock.json) 记录 Entities 1.3.5 → Collections 2.5.1 / Mathematics 1.3.2，Graphics 1.4.2 → SRP Core 14.0.9；[Blasting Blocks 固定 lock](https://github.com/Dreaming381/LatiosFrameworkMiniDemos/blob/a76d288bbdf9a21d859c132dfda8878a99be55e0/Blasting-Blocks-Jam-Game/Packages/packages-lock.json) 记录 Entities 1.2.1 的依赖；[FeatureSamples 固定 lock](https://github.com/Dreaming381/LatiosFrameworkMiniDemos/blob/ab2845bae30f3ab46a2d4e473477891d35b6fa54/FeatureSamples/Packages/packages-lock.json) 记录 Entities 1.3.9 的依赖。**这些样例已经使用 Unity 6/URP 17，不能当作 2022+URP14 已运行成功的证据。** Unity [Entities changelog](https://docs.unity3d.com/Packages/com.unity.entities@1.3/changelog/CHANGELOG.html) 另确认 1.3.5 的 Mathematics 1.3.2/Burst 1.8.18 变更，以及 1.3.14 仍以 2022.3.13f1 为最低 Editor。

SPF 依据为本地文档头 `86b39fa` 的 [manifest](../Packages/manifest.json) 和 [Editor 版本](../ProjectSettings/ProjectVersion.txt)，运行时代码与远端 `fc1c10df57e179b7da5d5ae2b457cb0de2719738` 等价。本地没有 `packages-lock.json`，所以不把 manifest 冒充完整 Mac 已解析环境。

### 2.1 由矩阵直接得到的约束

- **不必升级 Unity 才能验证 Latios。** 0.11.5 已是合理的 2022 候选。
- **但接入旧版仍会引入 Entities 生态。** Collections/Mathematics 与 SPF 当前声明不同，要在隔离项目先得到真实 lock；不能向主项目塞包后依赖 UPM 猜一个能工作的组合。
- **版本号不能只按大小判断。** Entities Graphics 1.4.2 与 Entities 1.3.5 是这个历史 tag 的真实配对；不能误认为 Graphics 的 1.4 必须搭配 Entities 1.4。
- **Burst 1.8.27 高于旧版最低依赖，不构成必须降级的理由。** 先用上游旧版配套依赖建立控制组，再测试接近 SPF 的版本组合；两组结果分别记录。Collections 2.1.4/Math 1.2.6 则不能未经验证强压住 Entities 的较新需求。
- **运行时不安装某模块，不等于编译/包依赖消失。** 原包仍有统一 `package.json` 和模块 asmdef；例如只调用 Core installer 也不会自动移除 DSPGraph/Graphics 的包依赖。真正最小包需要独立裁剪/分包，并验证依赖闭包。

## 3. 三条路线怎样选

| 路线 | 实际工作 | 能得到什么 | 代价与适用时机 |
| --- | --- | --- | --- |
| A：固定历史版本 | 独立项目固定 0.11.5 + 配套 Entities，按旧版 bootstrap 使用 | 最快确认 2022 是否能完整导入，以及 Core/Psyshock 等历史能力能否工作 | 历史 bug 与修复自己筛选；没有后来的全部新能力；对 SPF 的数据模型接入仍是另一项工作 |
| B：维护 2022 兼容 fork | 以 A 为绿基线，按功能向前移植；或明确要求保留 0.16 API 时，为缺失引擎 API 实现旧后端 | 可以保留所需新版算法/API，并长期留在 2022 | 需要版本适配层、差异测试和修复策略；音频/渲染比单纯算法重。成本取决于承诺的模块和功能，当前不报未经验证的工期 |
| C：独立吸收设计或抽取窄算法 | 保留现有 SoA/Jobs/Burst，仅实现选中的诊断/所有权/算法接口 | 不需要另建 ECS 世界；适合当前扩展性问题 | 这是 SPF 的独立能力，不应称为“Latios 已兼容”或“完整保留 Latios 功能”；复制代码仍须处理来源和许可 |

**推荐：若目标是“让 Latios 本身适配 2022”，先 A 后 B。** C 是另一个可选目标，不能拿“借鉴思想”替代真正的库回迁请求。若目标只是在 SPF 中获得某项能力，先明确该能力，再比较 B 与 C 的完整成本。

0.11.5 已有 Core、QVVS、Psyshock、Myri、Kinemation、Calligraphics 和当时的 Mimic；Unika、LifeFX 是 [0.12.0 才加入](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/CHANGELOG.md)，Aux ECS 则在 0.15.0 加入。新版 [Core 0.16 changelog](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Core/CHANGELOG.md) 的 ticking、`ILatiosApi`、`IJobEach`、新 source generation 等也不是旧版现成功能。不能把旧版成功启动当作完整 0.16 回迁完成。

## 4. 当前版按模块回迁的真实边界

以下成本为源码级相对判断，不是编译结果或工期承诺。更换版本不应通过关闭 safety、改变行为或削弱测试来伪装兼容。

### 4.1 Core：可行候选，先把 Entities 内部桥接单独验清

Core 是 `LatiosWorld : Unity.Entities.World`，不是独立于 Entities 的任务调度库。除了公开 API，`EntitiesExposed.asmref` 将 [Exposed 代码编入 Unity.Entities](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/EntitiesExposed/EntitiesExposed.asmref)，以访问 internal。

实际敏感点包括：

- [EntityManagerExposed](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/EntitiesExposed/EntityManagerExposed.cs) 访问 `GetCheckedEntityDataAccess`、DependencyManager、ChunkIndex、内部安全句柄。
- [ArchetypeChunkExposed](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/EntitiesExposed/ArchetypeChunkExposed.cs) 访问 `m_EntityComponentStore`、`m_Chunk`，并直接构造 buffer accessor；[ComponentSystemGroupExposed](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/EntitiesExposed/ComponentSystemGroupExposed.cs) 访问内部系统更新列表。
- 资源依赖、命令播放、源码生成和 baking 是连在一起的行为；通过 C# 编译后仍要验证系统异常、在途 Job、资源替换/销毁、域重载和 IL2CPP。

这些是**必须审核的耦合点**，不是本轮已经证明每个 API 在 1.3 中都不存在。可以优先保留 0.11.5 的内部桥接，把需要的新功能逐项移植。特别注意：0.11.5 要求 `ENTITY_STORE_V1`，但 Core 0.16 changelog 已声明不再要求它，不能把旧版本设置无限期套到新版本。

### 4.2 Psyshock：优先做最小实验，但数组入口仍有依赖

新旧版本都有 `BuildCollisionLayer(NativeArray<ColliderBody>)`，旧版 [数组入口](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Builders/Physics.BuildCollisionLayer.cs#L154-L181) 可绕开 EntityQuery 数据提取，适合隔离的算法实验。

不过当前 [ColliderBody](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Types/ColliderBody.cs) 内仍有 `Unity.Entities.Entity` 与 `TransformQvvs`；[asmdef](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/PsyshockPhysics/Physics/Latios.Psyshock.asmdef) 引用 Core、Calci、Transforms、Entities/Hybrid。PairStream、Blob 和自定义容器也不能按单个 `.cs` 随手摘用。

首选小面是碰撞层构建、候选对、射线/距离查询，而不是完整求解器与场景烘焙一起搬。新版 [Psyshock changelog](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/626d1a37f432cd31e4a3faf44fc3cb07c928cc17/Psyshock%20Physics/CHANGELOG.md) 列有查询算法重做与修复，回迁时要区分修复是否适用于旧实现，不能因为函数同名就直接 cherry-pick。FindPairs 是候选阶段，不能用它代替 SPF 的命中语义。

### 4.3 Kinemation：已有旧后端可参考，当前版是高成本回迁

当前 renderer 不只是依赖某个较新包版本，还直接使用新 BRG 接口。对照 Unity 官方精确 [2022.3.62f2 BRG 源码](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Runtime/Export/Rendering/BatchRendererGroup.bindings.cs)：

- 当前[创建流程](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Systems/LatiosEntitiesGraphicsSystem.CreateDestroy.cs#L46-L50)用 `BatchRendererGroupCreateInfo` 和 `finishedCullingCallback`；旧 Editor 的构造形态是 culling callback + `IntPtr`。
- 当前[culling 流程](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Systems/LatiosEntitiesGraphicsSystem.CullingDispatch.cs#L76-L112)写 `customCullingResult`，在 finished-culling 回调里执行后续 dispatch/allocator 工作。2022 的旧接口没有同一套回调/数据契约，不能只改构造签名而忽略时序。
- 当前[draw command](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Kinemation/Systems/Culling/GenerateBrgDrawCommandsSystem.Jobs.cs#L312) 的 `LODCrossFadeValuePacked` 与旧版 `LODCrossFade` 需要匹配 shader/LOD 语义。v0.11.5 的 [旧枚举分支](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Kinemation/Systems/Culling/GenerateBrgDrawCommandsSystem.cs#L503-L509) 是可参考的实现。

因此可恢复/适配历史 culling/dispatch 路径，但要覆盖多相机、阴影、LOD crossfade、剔除后 GPU 工作、baking、mesh deformation 和资源释放。不能把当前源码里已做 `UNITY_6000_*` guard 的接口也误列为无条件阻塞；每个调用点单独核查。ACL 原生插件与目标 CPU/平台还要另外验证。

### 4.4 Myri：音频后端要恢复或重写，属于明确的引擎 API 差异

v0.11.5 的 [Myri asmdef](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/MyriAudio/Latios.Myri.asmdef) 依赖 `Unity.Audio.DSPGraph`，包版本是 `0.1.0-preview.22`。[v0.15.4 发布](https://github.com/Dreaming381/Latios-Framework/releases/tag/v0.15.4)说明迁移至 [Unity 6.3 新增的 Scriptable Audio](https://docs.unity3d.com/6000.3/Documentation/Manual/WhatsNewUnity63.html#audio)。当前 [AudioEcsController](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/MyriAudio/AudioECS/Runtime/AudioEcsController.cs#L13) 与 [AudioEcsRootOutput](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/MyriAudio/AudioECS/Runtime/AudioEcsRootOutput.cs#L14) 使用 `UnityEngine.Audio.RootOutputInstance` 的 control/realtime 接口及处理器上下文；2022 不提供这一套相同后端。

实际选择是：保留旧版 DSPGraph 后端并移植需要的上层能力，或为当前 Myri 写一个 2022 音频后端。旧 [AudioSystem](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/MyriAudio/Systems/AudioSystem.cs#L112-L119) 和 DSP 节点/组件/驱动需要成套匹配，不能只拷一个 driver 或把 `using` 改名。控制线程与实时线程交接、停止/重建、buffer 生命周期、采样率、后台暂停与设备路由都需要原生音频验证。恢复旧后端不等于保留当前 Audio ECS 全部能力。现有 SPF 音频后端可以继续保留，不必为了验证 Latios 而同时替换。

### 4.5 Unika：有移植空间，编译器与 AOT 是验证门槛

Unika 在 0.11.5 中不存在；当前版有生成器、Burst 函数指针、动态脚本 buffer 与引用重映射。此次没有找到与 Myri/BRG 同等级的“必用 Unity 6 原生 API”硬阻塞，所以不能仅因它较新就判不可回迁。

但“都是 C#”也不够：要验证当前生成器能在 2022 Editor 的编译链产生正确代码，[`__Initialize` 的反射初始化](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Unika/Internal/AssemblyManager.cs#L138-L159)没有被 IL2CPP stripping，[字段偏移与引用重映射](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/Unika/Internal/ScriptTypeInfoManager.cs#L89-L103)和 SubScene 序列化正确。Core 依赖和新 buffer/allocator 能力应一起列入闭包。作者[生成器项目](https://github.com/Dreaming381/Latios-Framework-Source-Generators/blob/6759ee1a26ca57222d18c38484194b6ebde3a4ed/LatiosFramework.Unika.SourceGen/LatiosFramework.Unika.SourceGen.csproj)的目标是 netstandard2.0/C#8，但本轮未验证它与 v0.16.1 分发 DLL 的精确构建映射。相对成本取决于所用脚本功能；本轮不预判编译一定成功，也不凭 Roslyn 版本名称宣称一定失败。

## 5. 平台与渲染范围不能省略

0.11.5 同期的[兼容指南](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/bc3be50530180ad6ee8dd10fa7388a7854131f6b/Installation%20and%20Compatibility%20Guide.md) 列出 Windows、Linux desktop、macOS、Android 的开箱支持，指出 Kinemation 原生插件限制与 IL2CPP 特殊注意。**这不是物理 Android/iOS 在 SPF 上的验收结果；不能据此承诺 iOS ACL 已准备好。** 当前指南也将完整开箱支持限于所列平台，其他平台需构建或禁用相应插件。

Unity [Entities Graphics 兼容文档](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4/manual/requirements-and-compatibility.html) 列出 SRP 要求、URP Forward+、Android Vulkan/GLES 3.1+、iOS Metal 和 Web 不支持等条件。该地址现在展示 1.4.21，不能原样套作 1.4.2 的逐项实测保证；[2022.3 BRG 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/batch-renderer-group-getting-started.html)的 Android 项当前列 Vulkan 和 OpenGL ES 3.x，并要求 SRP Batcher。两者范围不同，BRG 可用也不等于完整 Kinemation/Entities Graphics 在每个后端均已验证。试验须用目标包内文档和真实设置复核。特别是 SPF 现有 GLES 3.0/DataTexture 回退目标，不应因引入 Kinemation/Entities Graphics 而静默消失。

## 6. 最小、可判定的可行性试验

这是**待授权执行的计划**。不占用当前忙碌 Mac 的原生队列，不在 SPF 主项目或已有验收工作树里实验。若执行，使用独立目录与自己的 Library/cache/manifest/lock；开始前由主任务安排 Editor 资源。

### Gate A：确认历史支持基线确实能在精确 Editor 运行

1. 新建空白 **Unity 2022.3.62f2** 项目，固定 Latios **381a77d / v0.11.5**；先使用配套依赖，生成并保存实际 `packages-lock.json`。记录全部版本、平台、API level、defines。按[同期 Getting Started](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/bc3be50530180ad6ee8dd10fa7388a7854131f6b/Core/Getting%20Started.md) 设置 `UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS` 与 `ENTITY_STORE_V1`。
2. 首个用例仅安装必要的 Core/Transforms/Psyshock 运行系统。原包编译时仍包含其他程序集/依赖，日志要区分“包导入/全程序集编译”与“实际运行的模块”。不在第一次导入时顺便做大规模分包。
3. Core 验证：创建/销毁 `LatiosWorld`；collection component 经一个写 Job、一个读 Job，再移除/销毁，检查 JobHandle、安全检查与释放；PlayMode 重入和域重载不遗留状态。
4. Psyshock 验证：固定小数组（空集、单物体、重叠/不重叠、边界和退化输入），构建 CollisionLayer；FindPairs 与暴力 AABB oracle 比较；距离/射线用已知几何结果验证。以规范化身份比较结果集合，不要求未经 API 承诺的线程输出顺序；测试不同 batch size，Burst 开/关。
5. 通过要求：干净导入可重复、无编译/生成器错误、原生安全检查开启且不报错、结果符合 oracle、正确释放资源。输出精确版本、日志、测试和剩余限制。此阶段不声称性能提高、完整 Kinemation/Myri 运行或手机验收。

**失败时的处理：** 先分类 UPM/依赖冲突、源码/内部 API、生成器、原生运行或资源生命周期。可以修复单个已识别兼容问题再重试，但若需要升级 Unity、修改 Unity/Entities 核心布局、关闭安全检查或扩大到渲染/音频重写，就暂停并重新确认范围，不能自动转成全框架工程。

### Gate B：用一个新版能力证明增量回迁，而非只证明旧版能跑

基线绿后，先选一个确实需要的 Psyshock 查询修复或 Core 小型能力，列明上游提交、所依赖的新类型、目标行为、旧/新 oracle。新增独立兼容 patch 并跑 Gate A 回归；若要求完整 current API，则另开 Core→Psyshock→Unika→Kinemation/Myri 的分阶段兼容清单，不能用一次小功能成功代替全模块完成。

通过标准是“2022 上该新增能力及旧基线全部满足契约”，不是编译器不再报红。涉及 Burst/IL2CPP 的能力还需对应 player smoke；渲染/音频分别要真实图形/音频验收。资源/代码生成/原生插件复杂度突破原先批准范围时先评估，不隐性扩大。

### Gate C：仅在要接入 SPF 时验证桥接收益

SPF 仍拥有权威 `SimSession`、handle、固定 Tick、容量与保存语义。一个可选适配器从只读 SoA 视图生成 Psyshock 输入，返回候选/查询结果；显式映射实体身份，不能把 `EntityHandle` 强制重解释成 `Unity.Entities.Entity`。如果需要第二个 ECS World，明确其时钟、生命周期与所有权，禁止两个系统同时修改权威状态。

测量必须包含输入转换/拷贝、分配与常驻内存、调度/等待、结果整理以及完整 Tick。先做语义对照，再对比现有空间网格。没有收益或维护负担超过收益就保留独立实验，不合入主项目。已有 19 组合、回放/hash、同 Tick restore、清关/重开和移动回退验收继续有效。

## 7. Fork 维护与来源要求

- 固定上游 tag/commit；兼容改动、功能回迁、缺陷修复分开记录。不要把本地兼容分支标成“上游官方支持 2022”。
- 维护模块/功能支持矩阵、精确 dependency lock、已知不支持项与回归测试；每次更新先核对内部 API 和 generated code，而非自动追踪 upstream head。
- 对历史缺陷逐项判断是否影响目标用例，记录“已回迁 / 不适用 / 待处理”。不能因为 0.11.5 很旧就宣称全都有 bug，也不能因启动成功就忽略后续修复。
- [Latios LICENSE](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/LICENSE.md) 指向 Unity Companion License。[当前 UCL 原文](https://unity.com/legal/licenses/unity-companion-license) 涉及 Unity 使用范围、库衍生修改与 notices；[第三方 notices](https://github.com/Dreaming381/Latios-Framework/blob/ae2262afd5c80ac4850fef23c9e7d4a971fabf53/THIRD%20PARTY%20NOTICES.md) 另列 ACL、噪声/算法、HarfBuzz 等许可。保留来源、版权与适用 notices，单独核对实际复制的代码/二进制；不把整个包重新标成 MIT，也不把“可修改”扩大为任意平台/任意再许可。
- 本文是工程评估，不替具体发布/商用安排作法律结论；实施不默认包含上游 PR、公开 fork、换许可或对外分发。

## 8. 本轮建议

**值得先证明，不应因 Unity 6 标签直接排除。** 以 0.11.5 为 2022 兼容控制组，从 Core + Psyshock 的最小原生用例入手；把当前需要的新功能逐项搬上来。Kinemation 有历史回退源码，Myri 有历史 DSPGraph 后端，二者都有具体可走的工程路线，但工作量和验证面更大。是否最后整体采用、局部接入或继续独立实现，应由这个实验和实际需求决定。

当前已完成：历史版本与依赖研究、关键 API 差异核对、SPF 基线对照、分阶段试验及停止条件设计。当前未完成：精确 Editor 导入/编译、AOT/IL2CPP、音频/图形/真机测试，以及任何性能收益证明。
