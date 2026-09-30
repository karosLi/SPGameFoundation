#!/usr/bin/env bash
# Runs the Unity EditMode + PlayMode suites inside the official unityci/editor image (no third-party action).
# Needs UNITY_LICENSE (Personal: contents of Unity_lic.ulf) or UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD.
set -euo pipefail
cd "$(dirname "$0")/../.."
VERSION=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt | tr -d '\r')
IMAGE="unityci/editor:ubuntu-${VERSION}-base-3"
echo "Unity ${VERSION} → ${IMAGE}"
mkdir -p Artifacts
docker run --rm \
  -v "$PWD":/project -w /project \
  -e UNITY_LICENSE -e UNITY_SERIAL -e UNITY_EMAIL -e UNITY_PASSWORD \
  -e HOST_UID="$(id -u)" -e HOST_GID="$(id -g)" \
  "$IMAGE" bash /project/Tools/ci/in-container.sh
