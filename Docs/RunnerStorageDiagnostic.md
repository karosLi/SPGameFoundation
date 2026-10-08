# Read-only runner capacity snapshot

The native run `37776649150` reported thirteen explicit no-space-left failures.
On 2026-10-08 the user confirmed that space had been freed and authorized
continuing. This one-time snapshot checks current capacity before resuming heavy
Unity/Lab work; directory investigation and cleanup are outside its scope.

The available GitHub connector cannot dispatch workflows, and no direct Mac
environment is connected. The authorized alternative is one push of the isolated
`dot/ci-disk-diagnostic` branch. The dedicated workflow accepts only a push to that
exact branch. All other workflow jobs reject that branch as a push/manual ref or
either side of a pull request. Ordinary branch behavior is unchanged, and this
source base contains no Lab workflow. Do not merge this branch into a release.

The single inline Python step runs on the existing `[self-hosted, unity]` runner
with `contents: read`, a five-minute job timeout and a 30-second script deadline.
It checks the actual `GITHUB_WORKSPACE` and `RUNNER_TEMP` against the fixed approved
Mac locations, rejects symlinks in every ancestor, then uses `statvfs` to print
only UTC capture time and total/free/available filesystem bytes for those two
labels. No user paths, environment values, directory names or file contents are
printed. Missing or unexpected roots and timeouts produce an incomplete result.

There is no checkout, downloaded action, Unity invocation, package installation,
subprocess, directory enumeration, `du`, upload, credential use, deletion or
cleanup. GitHub runner infrastructure still creates its normal job/log
bookkeeping; the diagnostic step does not write project data. A completed
snapshot reports capacity, not a guarantee that a later Unity run has enough
space or that APFS reclaimable storage is immediately available.
