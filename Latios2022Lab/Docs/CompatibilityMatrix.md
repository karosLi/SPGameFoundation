# S1a compatibility and evidence matrix

Updated 2026-10-08. The second native import **FAILED** at owned CS1654 after the assembly-reference correction; its full evidence and genuine lock are preserved in the [ownership correction record](NativeArrayOwnershipCorrection-20261008.md). The new try/finally candidate has static checks only. The first failed record remains preserved. All test/player cells remain **NOT RUN**. The original product's P0/CI results are separate and cannot be inherited by this project.

| Scope | Prepared test/entry | Current evidence | Passing would mean |
| --- | --- | --- | --- |
| Isolated launcher and pin validation | `Tools/lab.py`, `Tools/test_lab.py` | Static preflight + 25 Lab and 19 CI Python tests passed | Launcher/parser behavior only; not Unity compilation |
| Full pinned package + dependency compilation | `import` and genuine lock/inventory | First source FAILED on duplicate runners; second source FAILED on owned NativeArray CS1654; current correction NOT RUN | Exact Editor/host/package graph imports; not every module's runtime behavior |
| Core collections/source generation | `TrackedWriterReaderAndDisposal`, exception case | NOT RUN | Generated pseudo-component and tracked jobs dispose correctly in tested paths |
| Core lifecycle | two owned worlds; two domain-reloaded play entries; PlayMode repeat | NOT RUN | Tested create/update/destroy/reentry cycles; not arbitrary disabled-domain-reload settings |
| QVVS | two explicitly installed empty-world updates | NOT RUN | Basic installer/update path; not transform hierarchy/content/animation acceptance |
| Psyshock arrays | 36 candidate/oracle cases + scheduled known geometry | NOT RUN | Small finite input cases and stated geometry, not final SPF hit semantics |
| Burst on/off controls | query witness + separate process configurations | NOT RUN | The observed query job uses expected backend; logs still needed for upstream compilation |
| Desktop IL2CPP build | `LabPlayerBuild.BuildSmokePlayer` | NOT RUN | Only chosen desktop target builds; actual player run remains separate |
| Desktop IL2CPP run | generated minimal scene + `LabPlayerSmoke` | NOT RUN | Tested Core/Psyshock executable reaches explicit success witness |
| Kinemation / Myri / Calligraphics / Mimic / other upstream assemblies | Upstream package kept whole; no optional runtime installer | Second run reached compilation/IL processing but no successful complete inventory; runtime OUT OF SCOPE | Compilation inventory is not install/execution proof |
| Android / iOS physical devices | separately approved target/device matrix | NOT RUN / external devices required | Only observed devices/backends; no mobile claim from desktop |
| SPF bridge / default product adoption | No code or dependency change | NOT IMPLEMENTED | Requires later S2 semantics, full-cost A/B and product regressions |
| New upstream capability backport | No patch applied | NOT IMPLEMENTED | Requires green S1a and one selected feature ledger |

Inventory caveat: `CompilationPipeline.GetAssemblies()` reports the Editor's compilation assembly set, while `installedManagedSystems` records managed systems in the small lab world. It is not a full unmanaged-system execution trace. The imported package brings more code/plugins into compilation than the actual small world uses. Exact system/job tracing, clean-repeat logs and reviewed output are still needed before narrowing claims.

Native evidence must retain commit/tree, dirty status, hashes, real lock, package sources, Editor, OS/CPU, actual target/API/defines, Burst mode/safety, logs, exact discovered test counts, skips/failures and build/player result. Hashes do not replace review. No historical green SPF result, source inspection or offline parser test counts as an S1a pass.
