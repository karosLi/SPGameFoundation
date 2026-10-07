# Test-only separation candidate: explicit managed arithmetic compatibility

2026-10-07. Production `GroundCombatQueries.Separation`, the ordered Belt pass, spatial queries, snapshots and gameplay settings are unchanged. This is a repair of the **unadopted test candidate**, not approval to replace the production pass.

## Failure and reproducible diagnosis

The original four exact spread/dense tests remain, including the full 32/128 dense inputs and exact output/count assertions. On exact source `677611a` (native run [37581211270](https://github.com/karosLi/SPGameFoundation/actions/runs/37581211270)), Strict/High did not solve the two Apple M5 Pro differences:

| Scene/row | Managed reference raw `uint2` | Strict/High Burst raw `uint2` |
| --- | --- | --- |
| Dense32 / 0 | `(3189493657,3175785598)` | `(3189493657,3175785599)` |
| Dense128 / 2 | `(1025170858,3189639816)` | `(1025170860,3189639816)` |

Using Unity 2022.3.62f2's bundled Mono 6.13.0 (`explicit/98b53367`) on Linux x64 and the installed real Mathematics DLL, the unchanged production source reproduces **both columns**:

- `--optimize=float32` reproduces both Burst fingerprints.
- `--optimize=-float32` reproduces both Apple managed fingerprints.

This isolates a managed expression-evaluation difference; changing Burst precision alone cannot reproduce wider managed intermediates. It does not establish the exact launch flag or generated instructions on the Mac. The next native test reports its actual primitive profile and traces, rather than assuming the operating system determines the profile.

Mathematics 1.2.6 [source pinned by the harness](https://github.com/Unity-Technologies/Unity.Mathematics/blob/f110c8c230d253654afed153569030a587cc7557/src/Unity.Mathematics/math.cs) uses two scalar multiplies plus addition for `dot(float2,float2)`, `lengthsq = dot`, and a float-to-double `System.Math.Sqrt` followed by a float conversion for `sqrt(float)`. It is not a managed reciprocal-square-root implementation. Unity's [Mono evaluator](https://github.com/Unity-Technologies/mono/blob/unity-main/mono/mini/mini.c) sets its R4 stack type from `MONO_OPT_FLOAT32`; its [IR conversion code](https://github.com/Unity-Technologies/mono/blob/unity-main/mono/mini/method-to-ir.c) distinguishes R4 and R8 evaluation. Those moving source links explain the mechanism; the local executable/version and direct two-mode reproduction are the evidence used here.

A concrete dense32 contribution difference occurs before the final clamp: owner14 gives `(3192412016,3171258272)` in float32 versus `(3192412017,3171258272)` with extended scalar evaluation. The final unclamped sums are respectively `(3208621732,3195620265)` and `(3208621732,3195620264)`; the clamp length is identical (`1061785419`). Dense128 also requires matching the dot-product rounding boundary. The difference is not explained by visitor reordering.

## Candidate contract

`BwBeltSeparationArithmetic` is inside the EditMode test assembly. It selects one immutable arithmetic profile before scheduling, from **two independent scalar primitives**, never from a scene's reference output, Burst output or expected failure:

| Primitive probe | Float32 | ExtendedScalar |
| --- | --- | --- |
| `(minimum-distance)*.5f/distance`, input bits `1059313418 / 1035414974` | `1078262062` | `1078262063` |
| `math.lengthsq(delta)`, delta bits `(3195388559,3173242634)` | `1030912947` | `1030912946` |

Any other fingerprint throws an unsupported-profile error. There is no skipped case, approximate comparison, managed fallback masquerading as Burst, or output-specific correction.

- Float32 explicitly rounds each scalar product, sum, subtraction and scale operation through double-to-float conversions.
- ExtendedScalar retains double intermediates within the dot and scale expressions, then rounds at the original math/float2 call boundaries. The `math.abs(float)` height predicate also observes rounded float argument bits.
- Square roots explicitly follow the managed double-sqrt/float-result boundary. Ordered vector contributions and accumulation are unchanged.
- The independent reference visitor still calls the unchanged production `GroundCombatQueries.Separation` and original `math.length` clamp. It never calls the candidate arithmetic or candidate `Execute`.
- Strict/High and actual native `backend=1` remain mandatory, and raw-output-bit checks additionally cover signed zero.

This preserves the local runtime's frozen managed behavior. It intentionally makes **no cross-runtime canonical-bit claim**: the production reference itself has two observable results. A portable canonical gameplay contract would be a separate compatibility decision, not an incidental test fix.

## Coverage and artifacts

The four original performance scenarios still use 20 alternating-order samples × 30 repeated passes. Diagnostic work is outside those timing windows. Timing still excludes grid build, copyback, the remaining Tick and rendering; no whole-Tick or mobile performance benefit is claimed.

Added exact coverage:

- 4,096 seeded/boundary pairs, including adjacent representable values around `.64` contact and `.001` tie distance, coincident positions and reversed stable order.
- Four dense fixtures with negative cells, mirrored cancellation, tiny/coincident coordinates and neighbouring height-threshold values, scheduled with batch sizes 1/7/32.
- Per-visit owner/order, delta, squared distance, distance, scalar, contribution and running sum, followed by per-row unclamped push, length, clamp scale and final output, all recorded as raw unsigned float bits.
- Instrumented results must equal the uninstrumented production/candidate results, and per-contribution traces must match. Instrumentation cannot hide a changed result.

Unity writes `Artifacts/separation-arithmetic-*-contributions.csv`, `*-rows.csv` and `separation-arithmetic-pairs.csv` **before assertions**, so the self-hosted Mac CI evidence bundle retains useful arithmetic evidence even when an assertion fails. The separate hosted `unity.yml` upload does not currently include these CSVs. The row CSV identifies actual backend and includes the uninstrumented result. The .NET harness writes the same diagnostics to its temporary `spf-artifacts` directory; `backend=0` there is explicitly not native evidence.

## Repeatable managed reproduction

The checked-in diagnostic compiles the actual production source, candidate helper and real Mathematics DLL. It does not require starting the Unity Editor, changing any preference, or acquiring a license:

```sh
Tools/Diagnostics/separation-arithmetic-probe.sh \
  /path/to/Editor/Data/MonoBleedingEdge \
  /path/to/Unity.Mathematics.dll \
  /path/to/output
```

[Float32 results](Benchmarks/SeparationArithmetic-managed/float32.txt) and [ExtendedScalar results](Benchmarks/SeparationArithmetic-managed/extended-scalar.txt) each passed **200,000 exact production-source pair/height comparisons** and **all 160 dense rows with per-contribution accumulation and final clamp comparisons**. The two row fingerprints above were reproduced, and each explicit candidate profile matched its own unchanged production reference. This is managed arithmetic evidence, not a Burst execution claim.

## Validation and remaining gate

- All ten focused .NET candidate tests passed; detailed traces reported no differing stage.
- Final-source aggregate harness: **1,050 passed / 0 failed**, with **0 build warnings / 0 errors**. [Machine-readable summary and exact code-tree/log hashes](Benchmarks/SeparationArithmetic-managed/validation.json). Native-only PlayMode tests are not substituted by the stub aggregate.
- Compilation against installed real Unity 2022.3.62f2 engine, Collections, Jobs, Burst, Mathematics and test-runner APIs passed (four existing unused serialized-field warnings; no errors). This is compile-only.
- Native Burst execution of this patch is **pending** the next exact-head Mac CI run. Local Unity runtime testing remains license-blocked. Old green suites do not validate this patch.
- The candidate remains unadopted until exact native gates pass and an actual whole-Tick comparison shows a benefit. If native parity still fails, retain the ordered production implementation and reject the candidate. Do not relax equality, remove dense cases, reinterpret a disabled Burst run as success, or change production arithmetic to satisfy the experiment.
