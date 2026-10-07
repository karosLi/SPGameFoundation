# 有界移动音频接入（原创青铜遗迹音色）

本轮在已有 `SoundPlayer / VoicePool / SfxSynth` 上扩展，不增加另一套播放器或模拟时钟。原创音乐/音效的可重生成源、许可证、PCM 检查和试听文件见 [OriginalAudioAssets.md](OriginalAudioAssets.md)。声音只读玩法事实，不写伤害、掉落、冷却、随机数或存档。默认玩法和原始快照布局未改。

## 接入与范围

- 幸存者 `SvAudio.Create`、纵深格斗 `BwAudio.Create` 使用同一个 `SanctuaryAudioBank`；UI/menu 播探索曲，实际战斗播战斗曲。RPG 复用同一个 bank 的核心反馈与两首音乐，保留其额外原有合成提示。
- Bw bootstrap 原来的第二套短音注册和反馈订阅已替换，而非叠加；Sv/RPG 仍只订阅 renderer 的既有反馈广播，绝不再 drain 队列。
- `WeaponAudioCursor` 独立遍历权威 retained cue ring：弓 Begin 是拉弦，弓/杖 Release 是真正释放，Impact 是实际命中，Equip 是换装完成，Cancel 只停止对应 action 的拉弦声。不同 action 的旧 Cancel 不能切断新拉弦。
- 旧共享近战 ring **没有** Release；刀/剑用 `WeaponRuntime.View(1)` 的权威 contact marker 和 action pulse，一次性触发挥击音，不从插值姿态或预测延迟制造释放。可读到 Active/Recovery 时补播一次；如果整个动作都在表现停顿期间结束，不补造历史挥击。
- `ImpactAudioBudget` 在一个表现帧把 ring Impact 和 renderer Hit/Death/KO 合并成最多一声，重击优先；没有按不可靠的位置近似去猜事件来源。这样既不重复，也保留 kick/敌方/非主武器技能反馈；即使 Sv 低画质关闭 HitNumbers，ring 的非致死命中仍能发声。低画质下原本不产生反馈的非主武器非致死伤害不承诺新增声音。Sv 武器上下文仅抑制旧 Shoot，Nova 和 hero hurt 仍取既有事实。Bw hero hurt/heal 取已结算 HP 净变化，同帧互相抵消的伤害/治疗不承诺独立两声。
- RPG 原有 Swing 事件是 windup，现即时播放 anticipation，删除“按预测秒数延迟到释放”的行为，避免中断后仍播放假释放。RPG 不声称具有共享武器 ring 的精确 release 合约。
- Sv 返回菜单使用 Cancel，其他按钮 Confirm；Bw 开始/重开使用 Confirm。原 RPG 音量/静音选项继续作用于总线。

## 播放容量、所有权与移动预算

`SoundPlayer.Initialize(voices)` 只预热一次，允许 1–32，之后重复相同容量幂等，改变容量明确报错。Sv/Bw 各 16 个 one-shot AudioSource + **固定 2 个音乐 AudioSource**；RPG 20+2。每个 bank 最多 64 种声音；按类别设置 master × bus × sound × call gain。Sfx / Music / Ui 有独立音量与 mute；没有改全局 AudioSettings、系统音量或别的场景的 listener。

| 类别 | 并发/重触发 | 仲裁 |
| --- | --- | --- |
| 轻击 | 3 / 45ms | priority 1 |
| 重击 | 2 / 90ms | priority 2 |
| 受伤 | 1 / 150ms | priority 4 |
| 治疗 | 1 / 120ms | priority 3 |
| UI | 2 / 40ms | priority 5 |
| 其他武器 | 1–2 / 60–100ms | priority 0–2 |

满池只抢占不高于新请求优先级的最旧/较低优先级 voice；同一效果超过其 cap 则重启最旧同类。所有 Source 使用单 clip Play，不用可叠加而绕过池上限的 PlayOneShot。Music 两路不参与 SFX 抢占；Unity source priority 另映射为更小数值更重要。一个世界可创建多个独立 audio owner，但总预算须将各 owner 相加，未声称全项目/OS 混音器只有 18 路。

SFX 原始 PCM 24kHz mono，DecompressOnLoad、PCM、preload；音乐源 44.1kHz stereo，Streaming、Vorbis quality .7、保留采样率。精确源文件/理论 PCM byte 见素材文档；构建后的码率、Unity decoder/stream buffer native memory 需对应 Android/iOS build 实测，不能由 WAV 大小推算。短效果优先启动延迟和混音 CPU，长音乐优先内存。应用未强改 DSP buffer；目标反馈延迟预算是一个呈现帧 + 平台 DSP/output buffers，物理设备上的实际毫秒数待测。

Imported Resources clips 是共享借用资源，owner 不 Destroy/Unload；`SfxSynth` 创造的 clip 仍由注册它的 player 释放。所有 GameObject/AudioSource 随 owner 释放。没有 per-hit 创建源、逐帧增长列表或播放路径分配。RPG 原有延时列表也改为 32 条固定存储、满容量丢弃计数。

## 暂停、后台、时间线与音乐

- Game pause 丢弃短暂 SFX，保留 UI 可用；两路音乐 Pause 并冻结 fade。Resume 用 UnPause，保留播放位置。
- Application pause **或** focus lost 阻止所有新音并丢弃 transient，音乐暂停；只有两个后台条件都解除且 game 不 pause 才继续。没有跨 player 改 `AudioListener.pause`。
- Disable/reset/rebind 清掉瞬态和音乐；enable 后适配器重新检查 owner。没有过期排队音在恢复时爆发。
- Session identity、TimelineRevision、LevelVersion、weapon object/Revision、逆向 tick、entity generation 变化时，cursor prime 到当前 sequence/action，不重放 retained backlog，包括相同 tick 的 restore。windup 中绑定只丢弃过去提示，未来的真实 contact 仍有一声；每次观察当前 stage/action 也可终止没有 Cancel cue 的旧 CancelAll 拉弦。Host 短暂断开也 reset；重新绑定不能追播断开期间的旧提示。
- MusicCrossfade 固定两路，从当前 gain 开始 smoothstep 常和淡入淡出；重复同曲请求不重开 clip、不延长 fade；A→B 中改回 A 保留双方当前位置和瞬时 gain。新第三首只替换较小 gain 的 lane，允许舍弃那一路剩余尾音，绝不临时增加第三 source。
- 空/未加载 SFX 返回失败并计数，不假报 Played；缺失/failed music 返回失败。没有用占位 beep 悄悄掩盖缺失资源。Streaming 的原生异步解码、平台中断和音频路由恢复仍需 native/device 门槛。

## 验证与复现

1. `python3 Tools/AudioAuthoring/generate.py`（离线生产资源）；`python3 -m unittest discover -s Tools/AudioAuthoring -p 'test_*.py'` 检查 PCM、loop seam、4×插值峰值、mono fold-down、精确 score/event 来源及两次可重复生成。
2. `Tools/DotnetHarness/run.sh`：全仓 .NET/asmdef 逻辑检查。`AudioTests` + `MobileAudioTests` 覆盖池饱和、优先级、重复初始化、volume/mute、pause/background、无效 clip、距离衰减、两路反复/中断/第三曲、相同 tick restore/rebind/generation/wrap、近战 marker 和带 empty/retained-array 对照的零分配 policy。
3. Unity EditMode `AudioImportTests` 验证真实 AudioImporter 的格式/加载/采样率设置与读回 SFX 峰值/DC。Streaming preload 位在 defaultSampleSettings；不是 obsolete 顶层 API。
4. Unity PlayMode `NativeAudioPlaybackTests` 验证实际 AudioSource 数量、有信号的 GetOutputData、短 clip 真循环、Pause/UnPause、背景、两首导入音乐 repeated/interrupt crossfade、disable/enable 和带校准的同步播放路径分配。DSP 不前进明确跳过并标记 **未验证**；不把桩结果算作音频播放。该测试不申请麦克风，不代表人耳/扬声器音质。
5. `SvAudioGameplayTests` / `BwAudioGameplayTests` 从真实游戏 bootstrap 进入玩法、固定 Tick 释放弓箭，检查独立 cursor、同 tick restore、rebind、暂停；验证权威事实到适配器，不等同于证明物理输出。
6. 真实 Unity2022.3.62f2 DLL + netstandard2.1 API 编译、19 cold composition descriptor 和旧基线 inventory 比较，日志以精确提交交付；当前 native runner 执行、听感、物理 Android/iOS 蓝牙/有线/来电/静音键/热量/持续音频负载均独立待验收。

## 方案来源与适用边界

- [Unity 2022.3 Audio Clip](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html)：区分短效果解压驻留与长音乐 streaming 的内存/CPU 取舍。实际编解码器由 build target 支持决定。
- [AudioSource.Pause](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.Pause.html)、[UnPause](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.UnPause.html)：沿用源播放位置，避免通过 Play 伪装恢复。
- [AudioSettings.dspTime](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSettings-dspTime.html)：native 探针判断音频时钟是否真实前进。音乐通过单 clip 的 loop 保持循环，crossfade 是帧级 gain，不宣称 sample-accurate 节拍对齐切曲。
- [AudioSource priority](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource-priority.html)：项目先做固定池仲裁，Unity 再按平台 voice 上限虚拟化；未修改用户平台配置。

备选 AudioMixer assets 适合复杂 routing/effect snapshots，本轮三总线 gain/mute 已可用更小的播放器 API 表达，避免要求新 mixer 资产和全局单例。新引擎、外部音乐 API 和付费服务均无必要。

## 精确软件检查点

实现提交 `bfdd90a0f770857b655691a769690d03e6d3f1ac`（基于 `0a33448`）在最终冻结源上通过：**1,280 / 1,280 .NET、0失败、0跳过；10 / 10离线音频测试；真实 Unity2022.3.62f2 API 的 net8.0 / netstandard2.1 编译均0错误（各7条既有警告）；19 cold composition 与原 inventory 一致，两次输出逐字节相同**。75个 asmdef 无环。21项新增 .NET 音频合约测试计入上述总数。

[机器可读验证清单](validation/MobileAudioSoftware-20261007.json)保存实现树、全部61个变更文件的哈希、测试程序集结果、日志哈希与待验证项。独立只读复查在修正影响音效、禁用/重绑、CancelAll拉弦、windup prime、远处不可听命中选择后无剩余P1/P2。此后文档记录提交不改变已测源码。原生新增14项导入断言和3项DSP/实际玩法fixture尚未在本工作器运行，不算已通过；并行功能集成后仍须对新提交重新跑CI。
