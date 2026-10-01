#!/usr/bin/env bash
# Publishes Artifacts/Screenshots (and the perf / results summaries) to the branch
# ci-screenshots/<source branch> as a single force-pushed commit, so they can be fetched with plain git
# (artifact downloads go through blob storage that some sandboxes cannot reach).
# Pushes made with GITHUB_TOKEN do not trigger workflows, so this cannot loop.
set -euo pipefail
cd "$(dirname "$0")/../.."
SRC=Artifacts/Screenshots
shopt -s nullglob
shots=("$SRC"/*.png)
if [ ${#shots[@]} -eq 0 ]; then echo "No screenshots to publish."; exit 0; fi
: "${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
BRANCH="ci-screenshots/${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-local}}"

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT
cp "${shots[@]}" "$OUT/"
cp Artifacts/perf-*.txt "$OUT/" 2>/dev/null || true
{
  echo "# CI screenshots"
  echo
  echo "- source commit: ${GITHUB_SHA:-unknown}"
  echo "- run: ${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-?}/actions/runs/${GITHUB_RUN_ID:-?}"
  echo "- files:"
  for f in "${shots[@]}"; do echo "  - $(basename "$f")"; done
} > "$OUT/README.md"

cd "$OUT"
git init -q
git checkout -q -b publish
git -c user.name="unity-ci" -c user.email="unity-ci@users.noreply.github.com" add -A
git -c user.name="unity-ci" -c user.email="unity-ci@users.noreply.github.com" commit -q -m "Screenshots for ${GITHUB_SHA:-local}"
git push -q --force "https://x-access-token:${GITHUB_TOKEN}@github.com/${GITHUB_REPOSITORY}.git" "publish:refs/heads/${BRANCH}"
echo "Published ${#shots[@]} screenshot(s) to ${BRANCH}"
