# S1a isolated lab preparation

2026-10-07. Implementation staging of [the Unity 2022 blueprint](LatiosUnity2022IntegrationBlueprint.md); this record supersedes the blueprint's "nothing implemented" description only for this bounded preparation, not its native acceptance gates.

The new [Latios2022Lab](../Latios2022Lab/README.md) is a separate Unity project inside the repository, outside the product `Assets`, with its own Packages, ProjectSettings, Library/cache and gated launcher. It pins Unity 2022.3.62f2 and original Latios 0.11.5 commit `381a77dbf774ff603014d5695ef6c06abaa25d96`. It adds no root package, runtime backend, S1b patch, CI job or default gameplay change.

Prepared: source-grounded Core collection/Job/lifecycle fixtures, QVVS empty-world installation, independent small Psyshock pair/ray/distance oracles, Burst execution witness, domain-reload/reentry test, minimal generated-scene desktop IL2CPP smoke build entry, and environment/evidence capture. Expected native discovery is 49 EditMode cases and one PlayMode case, each run with Burst on and off. Exact discovery must be verified by Unity.

Initial preparation verified 22 Python launcher/preflight tests. Update 2026-10-08: the [first native import failed](../Latios2022Lab/Docs/ImportCorrection-20261008.md) on duplicate runner references, before tests. Its genuine resolved lock is preserved with provenance outside active Packages. The host correction passes static preflight, 25 Lab tests and 19 CI tests; its native import/compilation, generator behavior, test execution, IL2CPP build/run and physical mobile acceptance remain **NOT RUN**. No new lock has been fabricated. Native execution still requires a separate coordinator release.

A runner reservation and inspected exact-source P0 evidence are required before using the executable launcher path. Passing these preparation checks cannot open S1b/S2 or prove performance benefit. [Matrix](../Latios2022Lab/Docs/CompatibilityMatrix.md) and [source/licence ledger](../Latios2022Lab/Docs/BackportLedger.md) separate declared dependencies, compiled assemblies, installed systems, executed algorithms and remaining platforms.
