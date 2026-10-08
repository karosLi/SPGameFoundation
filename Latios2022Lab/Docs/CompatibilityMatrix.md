# S1a compatibility and evidence matrix

Updated 2026-10-08. [Exact b6ef271 patched Local Editor evidence](PatchedLocalEditorMilestone-20261008.md) passed import, 49/49 EditMode on/off and 1/1 PlayMode on/off. Original Git and matched unpatched Local controls remain **45 passed, 4 strict cleanup failures**. Repeat import and player/mobile gates remain NOT_RUN; S1a is incomplete. Original product P0/CI results remain separate.

| Scope | Prepared test/entry | Current evidence | Passing would mean |
| --- | --- | --- | --- |
| Isolated launcher and pin validation | `Tools/lab.py`, `Tools/followup.py`, Python tests | Existing launcher remains unchanged; new follow-up rejection tests are offline only | Launcher/parser/source-review behavior, not conditional native execution |
| Full pinned package + dependency compilation | `import` and genuine lock/inventory | Patched run compiles four owned + 13 Latios assemblies; 12 actual lock copies verified | Exact Editor/host/package graph imports; not every module's runtime behavior |
| Core collections/source generation | `TrackedWriterReaderAndDisposal`, exception case | Patched on/off: all Remove/DestroyEntity/DisposeWorld/exception cases pass; original controls still fail | Generated pseudo-component and tracked jobs dispose correctly in tested paths |
| Core lifecycle | two owned worlds; two domain-reloaded play entries; PlayMode repeat | Patched on/off: two reentry cycles and standalone PlayMode pass; standalone PlayMode covers DisposeWorld only | Tested cycles, not arbitrary disabled-domain-reload settings or comprehensive leak freedom |
| QVVS | two explicitly installed empty-world updates | Patched on/off owned-World/import update passed | Basic installer/update path; not populated hierarchy/content/animation acceptance |
| Psyshock arrays | 36 candidate/oracle cases + scheduled known geometry | Patched on/off: all 37 pass | Small finite input cases and stated geometry, not final SPF hit semantics |
| Burst on/off controls | query witness + separate process configurations | Actual scheduled QueryJob witnesses pass on/off in Editor and PlayMode | Direct backend evidence only for the witnessed QueryJob |
| Second clean import | separate `repeat-clean-import` release | NOT RUN; default-closed preparation preserves native source and creates new Library/cache | A second clean import, not a second complete suite run |
| Desktop IL2CPP build | `LabPlayerBuild.BuildSmokePlayer` | NOT RUN | Only chosen desktop target builds; actual player run remains separate |
| Desktop IL2CPP run | generated minimal scene + `LabPlayerSmoke` | NOT RUN | Tested Core/Psyshock executable reaches explicit success witness |
| Kinemation / Myri / Calligraphics / Mimic / other upstream assemblies | Upstream package kept whole; no optional runtime installer | Full package inventory now captured after successful import; optional runtime OUT OF SCOPE | Compilation inventory is not install/execution proof |
| Android / iOS physical devices | separately approved target/device matrix | NOT RUN / external devices required | Only observed devices/backends; no mobile claim from desktop |
| SPF bridge / default product adoption | No code or dependency change | NOT IMPLEMENTED | Requires later S2 semantics, full-cost A/B and product regressions |
| Isolated cleanup-query compatibility experiment | Same-source unpatched/patched Local arms | Unpatched Local reproduces four failures; patched five-phase Editor controls pass | Only the explicitly patched arm's measured Editor behavior; original controls remain failed |
| New upstream capability backport | No patch applied | NOT IMPLEMENTED | Requires green S1a and one selected feature ledger |

Inventory caveat: `CompilationPipeline.GetAssemblies()` reports the Editor's compilation assembly set, while `installedManagedSystems` records managed systems in the small lab world. It is not a full unmanaged-system execution trace. The imported package brings more code/plugins into compilation than the actual small world uses. Exact system/job tracing, clean-repeat logs and reviewed output are still needed before narrowing claims.

Native evidence must retain commit/tree, dirty status, hashes, real lock, package sources, Editor, OS/CPU, actual target/API/defines, Burst mode/safety, logs, exact discovered test counts, skips/failures and build/player result. Hashes do not replace review. No historical green SPF result, source inspection or offline parser test counts as an S1a pass.
