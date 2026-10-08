"""Exercise launch isolation with a fake editor; never start Unity or macOS tools."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
FILTER = "ShooterFoundation.Tests.PlayMode.ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality"
BRANCH = "diagnostic/shooter-native-allocation-20261008"

FAKE_EDITOR = r'''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
import xml.etree.ElementTree as ET

args = sys.argv[1:]
with open(os.environ["FAKE_CALLS"], "a") as output:
    output.write(json.dumps({"args": args, "capture": os.environ.get("SPF_SHOOTER_GC_CAPTURE"),
                             "story": os.environ.get("SPF_STORY_GC_CAPTURE"),
                             "ability": os.environ.get("SPF_ABILITY_GAMEPLAY_SEQUENCE")}) + "\n")
Path(args[args.index("-logFile") + 1]).write_text("Fake editor only; no native evidence.\n")
mode = os.environ.get("FAKE_XML", "both")
if mode != "missing":
    root = ET.Element("test-run", result="Passed")
    tiers = ["GpuDriven", "DataTexture"] if mode != "one" else ["GpuDriven"]
    for tier in tiers:
        name = "ShooterFoundation.Tests.PlayMode.ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality(" + tier + ")"
        if mode == "unrelated":
            name = "Other.Test(" + tier + ")"
        ET.SubElement(root, "test-case", fullname=name,
                      result={"skipped": "Skipped", "failed": "Failed"}.get(mode, "Passed"))
    ET.ElementTree(root).write(args[args.index("-testResults") + 1])
sys.exit(int(os.environ.get("FAKE_EXIT", "0")))
'''


class LocalUnityLaunchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="spf launcher fixture ")
        self.project = Path(self.temp.name)
        (self.project / "Tools/ci").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "Packages").mkdir()
        (self.project / "Artifacts").mkdir()
        (self.project / "Artifacts/stale.txt").write_text("Previous run")
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2\n")
        (self.project / "Packages/packages-lock.json").write_text(
            '{"com.unity.burst": {\n"version": "1.8.0"}}\n')
        self.launcher = self.project / "Tools/ci/local-unity-tests.sh"
        shutil.copyfile(ROOT / "Tools/ci/local-unity-tests.sh", self.launcher)
        self.bin = self.project / "fake bin"
        self.bin.mkdir()
        self.editor = self.executable("fake editor", FAKE_EDITOR)
        self.executable("pgrep", "#!/bin/sh\nexit 1\n")  # Never inspect/kill host processes.
        self.executable("uname", "#!/bin/sh\nprintf 'Linux\\n'\n")
        self.calls = self.project / "calls.jsonl"
        self.env = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ["PATH"],
                        UNITY_EDITOR_PATH=str(self.editor), FAKE_CALLS=str(self.calls),
                        GITHUB_SHA="fixture-head", GITHUB_REF="refs/heads/" + BRANCH)

    def tearDown(self):
        self.temp.cleanup()

    def executable(self, name, content):
        path = self.bin / name
        path.write_text(content)
        path.chmod(0o700)  # Only newly created temporary fixture scripts.
        return path

    def run_launcher(self, *args, **env):
        result = subprocess.run(["bash", str(self.launcher), *args], cwd=self.project,
                                env=dict(self.env, **env), text=True, capture_output=True, timeout=20)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []
        return result, calls

    def test_normal_run_is_full_and_forces_capture_off(self):
        result, calls = self.run_launcher(SPF_SHOOTER_GC_CAPTURE="1")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), 2)
        self.assertEqual([call["args"][call["args"].index("-testPlatform") + 1] for call in calls],
                         ["editmode", "playmode"])
        for call in calls:
            self.assertEqual(call["capture"], "0")
            self.assertNotIn("-testFilter", call["args"])
        self.assertIn("-nographics", calls[0]["args"])
        self.assertNotIn("-nographics", calls[1]["args"])

    def test_diagnostic_runs_only_both_tiers_once_and_records_provenance(self):
        result, calls = self.run_launcher("--shooter-gc-diagnostic", SPF_STORY_GC_CAPTURE="1")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)
        call = calls[0]
        self.assertEqual(call["capture"], "1")
        self.assertEqual(call["story"], "0")
        self.assertEqual(call["ability"], "0")
        args = call["args"]
        self.assertEqual(args[args.index("-testFilter") + 1], FILTER)
        self.assertEqual(args[args.index("-testPlatform") + 1], "playmode")
        self.assertNotIn("--burst-disable-compilation", args)
        self.assertNotIn("-nographics", args)
        self.assertFalse((self.project / "Artifacts/stale.txt").exists())
        metadata = (self.project / "Artifacts/shooter-diagnostic-run.txt").read_text()
        self.assertIn("github_sha=fixture-head", metadata)
        self.assertIn("NOT full native gate", metadata)
        self.assertIn("result_scope_validation_exit_code=0", metadata)

    def test_test_failure_is_not_retried_or_hidden(self):
        result, calls = self.run_launcher("--shooter-gc-diagnostic", FAKE_EXIT="2")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)

    def test_signal_failure_has_no_retry_or_burst_disabled_fallback(self):
        result, calls = self.run_launcher("--shooter-gc-diagnostic", FAKE_EXIT="137", FAKE_XML="missing")
        self.assertEqual(result.returncode, 137, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)
        self.assertNotIn("--burst-disable-compilation", calls[0]["args"])
        self.assertNotIn("retrying", result.stdout)

    def test_failed_xml_cannot_be_hidden_by_a_zero_editor_exit(self):
        result, calls = self.run_launcher("--shooter-gc-diagnostic", FAKE_XML="failed")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)

    def test_default_hub_editor_discovery_is_reused(self):
        home = self.project / "home"
        editor = home / "Unity/Hub/Editor/2022.3.62f2/Editor/Unity"
        editor.parent.mkdir(parents=True)
        editor.symlink_to(self.editor)
        result, calls = self.run_launcher("--shooter-gc-diagnostic", UNITY_EDITOR_PATH="", HOME=str(home))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)
        self.assertIn("Unity: " + str(editor), result.stdout)

    def test_invalid_modes_rejected_before_launch_or_artifact_cleanup(self):
        for args in [("--testFilter", FILTER), ("--shooter-gc-diagnostic", "extra"),
                     ("--shooter-gc-diagnostic=1",), ("",)]:
            with self.subTest(args=args):
                result, calls = self.run_launcher(*args)
                self.assertEqual(result.returncode, 2)
                self.assertEqual(calls, [])
                self.assertTrue((self.project / "Artifacts/stale.txt").exists())

    def test_missing_partial_wrong_or_skipped_xml_is_not_a_pass(self):
        for mode in ("missing", "one", "unrelated", "skipped"):
            with self.subTest(mode=mode):
                result, _ = self.run_launcher("--shooter-gc-diagnostic", FAKE_XML=mode)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn("Incomplete Shooter diagnostic results", result.stdout)

    def test_apple_silicon_requires_and_uses_native_editor_slice(self):
        self.executable("uname", "#!/bin/sh\nprintf 'Darwin\\n'\n")
        self.executable("sysctl", "#!/bin/sh\nprintf '1\\n'\n")
        self.executable("lipo", "#!/bin/sh\nprintf 'x86_64\\n'\n")
        result, calls = self.run_launcher("--shooter-gc-diagnostic")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(calls, [])
        self.assertIn("requires the arm64 Unity editor", result.stdout)
        self.assertIn("github_sha=fixture-head", (self.project / "Artifacts/shooter-diagnostic-run.txt").read_text())
        self.assertFalse((self.project / "Artifacts/stale.txt").exists())
        self.executable("lipo", "#!/bin/sh\nprintf 'x86_64 arm64\\n'\n")
        self.executable("arch", '#!/bin/sh\n[ "$1" = -arm64 ] || exit 99\nshift\nexec "$@"\n')
        result, calls = self.run_launcher("--shooter-gc-diagnostic")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), 1)


class DiagnosticWorkflowTests(unittest.TestCase):
    def test_workflows_isolate_the_exact_branch_and_share_bounded_evidence(self):
        normal = (ROOT / ".github/workflows/unity-self-hosted.yml").read_text()
        docker = (ROOT / ".github/workflows/unity.yml").read_text()
        diagnostic = (ROOT / ".github/workflows/unity-shooter-native-allocation-diagnostic.yml").read_text()
        for workflow in (normal, docker):
            self.assertIn("github.ref != 'refs/heads/" + BRANCH + "'", workflow)
            self.assertIn("github.head_ref != '" + BRANCH + "'", workflow)
            self.assertIn("SPF_SHOOTER_GC_CAPTURE: '0'", workflow)
        self.assertIn("branches-ignore:\n      - " + BRANCH, normal)
        self.assertIn("branches:\n      - " + BRANCH, diagnostic)
        self.assertIn("github.ref == 'refs/heads/" + BRANCH + "'", diagnostic)
        self.assertNotIn("workflow_dispatch:", diagnostic)
        self.assertNotIn("pull_request:", diagnostic)
        self.assertIn("run: Tools/ci/local-unity-tests.sh --shooter-gc-diagnostic", diagnostic)
        self.assertIn("contents: read", diagnostic)
        self.assertNotIn("contents: write", diagnostic)
        self.assertNotIn("publish-screenshots", diagnostic)
        self.assertNotIn("sudo ", diagnostic)
        self.assertIn("evidence_parts.py package --source Artifacts", diagnostic)
        self.assertEqual(diagnostic.count("uses: actions/upload-artifact@v4"), 40)
        for part in range(40):
            self.assertIn("name: shooter-native-allocation-diagnostic-part{:02d}".format(part), diagnostic)
        self.assertIn("-e SPF_SHOOTER_GC_CAPTURE=0", (ROOT / "Tools/ci/unity-tests.sh").read_text())


if __name__ == "__main__":
    unittest.main()
