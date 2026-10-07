# Sanctuary 原创音效与 BGM

## 交付范围与验收边界

本包提供 **2 首原创 16 小节循环 BGM、12 个原创短音效**，供 `SanctuaryAudioBank` 从 `Resources/Audio/Sanctuary/` 载入。运行时播放预渲染资源，不依赖 Python、SciPy、网络服务或实时音乐生成。配色/音色方向延续[青铜遗迹美术](SanctuaryArtDirection.md)：轻的木弦、柔和管乐、金属泛音、低鼓与短促武器反馈。

这是通过离线客观检查的第一版原创合成声音，**未完成主观听审**。当前工作环境没有可用的音频听审工具或音乐生成服务；不能据此声称音色已达到商业成品标准，也不能把波形指标当作手机扬声器、耳机、Unity 压缩后声音或真机性能验收。Unity 原生导入/播放结果由本次音频系统的原生测试单独记录，不由本文的 Python 结果替代。

## 原创来源与许可

- 作曲、节奏、和声、旋律和合成器参数在 [`generate.py`](../Tools/AudioAuthoring/generate.py) 中逐音/逐事件定义；导出 [`score.json`](../Tools/AudioAuthoring/score.json) 与 [`events.json`](../Tools/AudioAuthoring/events.json) 以便审查和改编。
- 未使用外部录音、采样库、第三方 MIDI、抄录旋律或预训练音乐模型。flute / lyre / strings / horn / bronze / Foley 是合成音色的设计标签，不声称是真实乐器录制或真人演奏。“原创”描述本次制作来源，不保证短音型在全世界没有偶然相似之处。
- 原创包采用 **CC0-1.0**；范围和权利声明见 [`LICENSE-CC0.txt`](../Tools/AudioAuthoring/LICENSE-CC0.txt) 与 [Creative Commons 官方 CC0 法律文本](https://creativecommons.org/publicdomain/zero/1.0/legalcode.en)。此声明不改变仓库其余代码或 Unity/Python/NumPy/SciPy 的许可。

## 音乐设计

| 资源 | 名称 | 时长 / 节拍 | 配器与结构 |
| --- | --- | --- | --- |
| `exploration.wav` | Ivory Steps in the Bronze Garden | 48.000 s，80 BPM，4/4，16 小节 | D 中心的小调/调式音集，省略第六级；稀疏笛声问答、分解里拉琴、延续弦层、低音、隔小节金属泛音和轻框鼓 |
| `combat.wav` | The Amber Gate Holds | 32.000 s，120 BPM，4/4，16 小节 | 同一 D 中心的紧张版本；八分分解音、号角旋律、低鼓/框鼓/边击和分句加花 |

两首均为 **44,100 Hz、双声道、16-bit PCM WAV**。探索曲 252 个合成事件，战斗曲 548 个；`events.json` 中依次记录音色、MIDI 音高（鼓为 0）、起始秒数、实际渲染时长、增益、声像。鼓的实际尾音长度与节奏占位可能不同，事件表记录真实渲染长度。

循环方式：将越过 16 小节末尾的乐器尾音写回开头，短房间反射/附点延迟也做周期写回；周期频域滤波去 DC、控制带宽，轻微软整形后归一化。没有在乐句边界塞静音，也没有为了让首尾样本相同而强制拉平波形。相邻的首尾采样值可以不同，重点是跨边界增量/斜率不出现异常。曲目切换的双路交叉淡化由播放系统负责，和单曲循环是不同问题。

## 音效与试听顺序

全部音效为 **24,000 Hz、单声道、16-bit PCM**，不循环。多层噪声、短包络、频率扫动与非整数金属泛音组合；结束点固定为 0，短反射不延长声明时长。加权去 DC 保留平滑起落，输出器拒绝非有限值/越界值，不靠静默裁剪隐藏问题。

| 文件 | 秒 | 设计用途 | 试听串第一次 / 第二次开始秒数 |
| --- | ---: | --- | ---: |
| `knife_swing.wav` | 0.25 | 轻刀风切和短金属余音 | 0.00 / 0.75 |
| `sword_swing.wav` | 0.39 | 更低、更宽的剑挥动 | 2.00 / 2.89 |
| `bow_draw.wav` | 0.62 | 拉弦摩擦、张力与小幅颤动 | 4.28 / 5.40 |
| `bow_release.wav` | 0.40 | 弦回弹与箭风 | 7.02 / 7.92 |
| `staff_cast.wav` | 0.78 | 上行能量与青铜和声音 | 9.32 / 10.60 |
| `impact_light.wav` | 0.25 | 轻命中瞬态 | 12.38 / 13.13 |
| `impact_heavy.wav` | 0.48 | 低频重命中与碎噪声 | 14.38 / 15.36 |
| `hurt.wav` | 0.48 | 非人声受伤提示 | 16.84 / 17.82 |
| `heal.wav` | 1.15 | 上行四音与柔和低音 | 19.30 / 20.95 |
| `equip.wav` | 0.43 | 三次短机械/金属扣合 | 23.10 / 24.03 |
| `ui_confirm.wav` | 0.32 | 上行二音确认 | 25.46 / 26.28 |
| `ui_cancel.wav` | 0.30 | 下行二音取消 | 27.60 / 28.40 |

`Artifacts/audio/Sanctuary_SFX_Audition.wav` 为 29.7 秒试听串：每个音效播放两遍，中间 0.5 秒静音，第二遍后 1 秒静音。已测试试听中的 PCM 与实际音效逐样本相同，避免二次量化差异；时间索引也保存在 manifest。此文件被仓库现有 `/Artifacts/` 规则忽略，可重建并单独交付，不放进游戏资源包。

## Unity 2022.3 导入决定

目标编辑器是项目指定的 **2022.3.62f2**。依据版本匹配的 [Audio Clip 手册](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html)，长音乐采用 Streaming + Vorbis，短音效采用 PCM + Decompress On Load。后者以较小的固定驻留数据换取无需 Vorbis 实时解码；若未来音效包显著扩大，再对噪声类音效比较 ADPCM，不能只按压缩率决定。

| 项 | BGM | SFX |
| --- | --- | --- |
| `loadType` | Streaming = 2 | DecompressOnLoad = 0 |
| `compressionFormat` | Vorbis = 1，quality = 0.7 | PCM = 0，quality 字段不影响 PCM |
| `sampleRateSetting` | PreserveSampleRate = 0，源 44,100 Hz | OverrideSampleRate = 2，24,000 Hz |
| 声道 | 保留立体声 | ForceToMono = true（源本来为 mono） |
| Normalize / Ambisonic | false / false | false / false |
| `defaultSettings.preloadAudioData` | false | true |
| `loadInBackground` | false | false |
| 平台覆盖 | 无，Android/iOS 继承默认项 | 无，Android/iOS 继承默认项 |

数值映射核对 [Unity 官方 Audio.bindings.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/Audio/Public/ScriptBindings/Audio.bindings.cs) 和 [AudioImporter.bindings.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/AssetPipelineEditor/Public/AudioImporter.bindings.cs)。尤其 `preloadAudioData` 已移到 **defaultSettings 内**；旧顶层属性在目标版本会报 obsolete 错误。[官方 AudioImporterInspector](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/AssetPipelineEditor/ImportSettings/AudioImporterInspector.cs) 也明确把 Streaming 的预加载选项禁用并显示为未勾选。`.meta` 使用稳定文件名派生 GUID；同名资源重生成不改变引用。

这些是目标导入设置，不是已打包 Android/iOS 的编码证明。还须在目标平台查询 `AudioImporter.defaultSampleSettings` / `GetOverrideSampleSettings` 和导入后的 `AudioClip`，验证实际声道、采样率、loadType、时长及数据加载。当前 24 kHz 输入不需要额外丢弃高频；若某个目标不保留 24 kHz，需明确调整并重新听审/测量，不能让频率断言静默放行。压缩质量 0.7 也不是固定码率或固定打包大小。

## 资源预算与实测数值

以下是**源文件字节和理论 PCM 样本载荷**，不是 Unity build 压缩大小或 Profiler 原生驻留内存。

| 项目 | 当前值 | 离线门槛 |
| --- | ---: | ---: |
| 探索 WAV（含 44-byte header） | 8,467,244 B，8.07499 MiB | 每首 ≤ 10 MiB |
| 战斗 WAV（含 header） | 5,644,844 B，5.38334 MiB | 每首 ≤ 10 MiB |
| 12 SFX WAV 合计（含各 header） | 281,328 B | ≤ 300 KiB |
| 全 14 个源 WAV | 14,393,416 B，13.72663 MiB | ≤ 16 MiB |
| 最大单个 SFX（heal） | 55,244 B | ≤ 64 KiB |
| 12 SFX 若解成 float32 mono 的纯样本载荷 | 561,600 B，0.53558 MiB | ≤ 640 KiB |
| SFX 试听串 | 1,425,644 B | 单个交付文件 < 20 MiB |

若完整解码成立体声 float32，探索/战斗纯样本载荷分别为 **16,934,400 B / 11,289,600 B**；全包 float32 样本共 **28,785,600 B**。这正是音乐选择 Streaming 而非整首预解码的原因；实际流式缓冲、解码器、AudioClip/AudioSource 和混音开销必须用设备 Profiler 测。官方手册给出的每个流式 clip 约 200 KB 固定开销只是背景参考，不能当成本项目完整内存上限。

共享播放器有固定音效池（默认 16、上限 32）和两条音乐通道。资源的单轨峰值留白不能证明 32 个音效叠加后不会限幅；真实混音、主/分组音量和设备输出仍要听审/测量。

| 客观指标 | 探索 | 战斗 | 12 SFX 范围/最坏值 |
| --- | ---: | ---: | ---: |
| 样本峰值 dBFS | −2.158 | −2.158 | −2.854 |
| 全文件 RMS dBFS | −14.506 | −14.699 | −24.223 到 −14.750 |
| 4× 插值峰值估计 dBFS | −2.153 | −2.153 | 最大 −1.778（轻命中） |
| 近满量程样本数（绝对值 ≥ 0.999） | 0 | 0 | 全 0 |
| 最大绝对 DC | < 1.3×10⁻⁸ | < 1.2×10⁻⁸ | < 2.6×10⁻⁷ |
| 循环跨界单步 / 斜率误差 | 0.023987 / 0.002472 | 0.012939 / 0.001251 | 不循环，首尾为 0 |
| 单声道合并 RMS / 立体声 RMS | 0.97875 | 0.98768 | 原生 mono |

RMS 未作响度加权/门限，**不是 LUFS**。4× 峰值由 `scipy.signal.resample_poly` 的默认 FIR、64 样本周期/静音 padding 估计，**不是经认证的 dBTP 测量**。峰值仅代表当前 PCM，不能代表 Vorbis 编码后重建峰值。

## 重建和回归

参考环境：Python 3.12.14、NumPy 2.3.5、SciPy 1.17.0，Linux x86_64；依赖固定在 [`requirements.txt`](../Tools/AudioAuthoring/requirements.txt)，种子 **7012026**。全流程不下载音频素材。

```sh
# 已具备依赖时，在仓库根目录运行：
python Tools/AudioAuthoring/generate.py
python Tools/AudioAuthoring/validate.py

# 仅运行 unittest，不保存新的证据 JSON：
python -m unittest discover -s Tools/AudioAuthoring -v

# 隔离导出；不会覆盖真实资源、源目录 metadata 或正常试听目录：
python Tools/AudioAuthoring/generate.py --out /tmp/sanctuary-preview
```

10 个测试方法覆盖：14 文件完整性、PCM header/帧数/时长、manifest 与源 SHA256、峰值/RMS/DC/裁剪、循环边界/斜率/mono 合并、固定文件/解码预算、`.meta` 枚举/预加载层级/唯一稳定 GUID、16 小节与真实事件长度、损坏数值的拒绝、非有限/越界输入的拒绝、**两次完整隔离重渲染逐字节一致**。后者也验证试听串两遍音效、静音间隔和时间索引；不是只把同一个文件 hash 两遍。

本次离线结果为 **10 通过、0 失败、0 跳过**；精确 source/asset/.meta/doc hash、运行环境、UTC 时间和完整有界测试日志见 [`validation.json`](../Tools/AudioAuthoring/validation.json)。该证据绑定未提交的音频文件快照，不冒充某个 repository commit、原生 Unity、听感或移动设备结果。跨不同 NumPy/SciPy/FFT/CPU 环境可能出现浮点最低位差异；如与 golden SHA 不符，必须调查并明示更新，不能降低门槛。

[`manifest.json`](../Tools/AudioAuthoring/manifest.json) 保存逐文件完整 SHA256、样本数、字节、指标和试听时间表。交付核对：

- exploration：`4b199e193cb55cf19035c07bf21b02363633261fe0c8851aaa23111d39e7694f`
- combat：`0530a60f61c72f5c23aac0e8d660977d7ab35dab3fde97e976f1b56aa158e38f`
- SFX audition：`cb4609c8a368bd1cef5190097fe0c5f7cc50267efab69c7523ddf7cc9ab17173`

## 仍需完成的声音验收

1. 在真实 Unity 音频设备上分别听两次完整循环；检查切换、暂停/后台恢复、流式起播和循环编码边界。测试若无音频设备，记录阻塞或跳过，不算播放通过。
2. 耳机与物理 Android/iOS 扬声器听审：挥空/命中可辨，弓蓄力/释放关联清楚，受伤/治疗/UI 提示不过分刺耳，BGM 不遮挡关键反馈。长弓蓄力目前是一次短提示，不是无限 sustain 的拉弓音层。
3. 最大音效并发与双 BGM 淡化时测实际混音限幅、主线程/Audio DSP/Streaming CPU、驻留内存、起播延迟；记录设备、系统、输出采样率、DSP buffer、测试时长及 p50/p95/最坏值。
4. 同时验证 Android/iOS 构建后的实际编码、24 kHz 导入、音频焦点/后台、蓝牙路由变化和持续游戏过程。桌面与离线结果不能填写真机栏。
