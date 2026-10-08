# S1a compatibility and evidence matrix

Updated 2026-10-08. [Run 37761086135 attempt 2](BootstrapContractCorrection-20261008.md) compiled all four owned assemblies and passed import. EditMode/Burst-on ran all 49 cases: **45 passed, 4 strict cleanup failures**. Two separate bootstrap assertions are recorded; the owned empty-default-World correction remains unexecuted. Off-mode and standalone PlayMode/player gates remain NOT_RUN. Original product P0/CI results remain separate.

| Scope | Prepared test/entry | Current evidence | Passing would mean |
| --- | --- | --- | --- |
| Isolated launcher and pin validation | `Tools/lab.py`, `Tools/test_lab.py`, `Tools/audit_domain.py` | Static preflight + 32 Lab and 23 CI Python tests passed; reviewed pending hashes recorded | Launcher/parser/source-review behavior only; not changed-source Unity execution |
| Full pinned package + dependency compilation | `import` and genuine lock/inventory | Attempt 2 has all four compiled and import/inventory PASSED; pending bootstrap source NOT RUN | Exact Editor/host/package graph imports; not every module's runtime behavior |
| Core collections/source generation | `TrackedWriterReaderAndDisposal`, exception case | On: Remove/DisposeWorld and expected exception passed; all 3 DestroyEntity cases failed at retained owner | Generated pseudo-component and tracked jobs dispose correctly in tested paths |
| Core lifecycle | two owned worlds; two domain-reloaded play entries; PlayMode repeat | Two-owned-World case passed; reentry failed cleanup with separate bootstrap assertions; standalone PlayMode NOT RUN | Tested create/update/destroy/reentry cycles; not arbitrary disabled-domain-reload settings |
| QVVS | two explicitly installed empty-world updates | On-mode owned-World/import update passed | Basic installer/update path; not transform hierarchy/content/animation acceptance |
| Psyshock arrays | 36 candidate/oracle cases + scheduled known geometry | All 37 passed in on-mode; off-mode NOT RUN | Small finite input cases and stated geometry, not final SPF hit semantics |
| Burst on/off controls | query witness + separate process configurations | Actual scheduled query Burst-on witness passed; off-mode NOT RUN | The observed query job uses expected backend; logs still needed for upstream compilation |
| Desktop IL2CPP build | `LabPlayerBuild.BuildSmokePlayer` | NOT RUN | Only chosen desktop target builds; actual player run remains separate |
| Desktop IL2CPP run | generated minimal scene + `LabPlayerSmoke` | NOT RUN | Tested Core/Psyshock executable reaches explicit success witness |
| Kinemation / Myri / Calligraphics / Mimic / other upstream assemblies | Upstream package kept whole; no optional runtime installer | Full package inventory now captured after successful import; optional runtime OUT OF SCOPE | Compilation inventory is not install/execution proof |
| Android / iOS physical devices | separately approved target/device matrix | NOT RUN / external devices required | Only observed devices/backends; no mobile claim from desktop |
| SPF bridge / default product adoption | No code or dependency change | NOT IMPLEMENTED | Requires later S2 semantics, full-cost A/B and product regressions |
| New upstream capability backport | No patch applied | NOT IMPLEMENTED | Requires green S1a and one selected feature ledger |

Inventory caveat: `CompilationPipeline.GetAssemblies()` reports the Editor's compilation assembly set, while `installedManagedSystems` records managed systems in the small lab world. It is not a full unmanaged-system execution trace. The imported package brings more code/plugins into compilation than the actual small world uses. Exact system/job tracing, clean-repeat logs and reviewed output are still needed before narrowing claims.

Native evidence must retain commit/tree, dirty status, hashes, real lock, package sources, Editor, OS/CPU, actual target/API/defines, Burst mode/safety, logs, exact discovered test counts, skips/failures and build/player result. Hashes do not replace review. No historical green SPF result, source inspection or offline parser test counts as an S1a pass.
