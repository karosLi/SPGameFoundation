"""Parser/review freshness tests only. Synthetic log text is never native evidence."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest

import audit_domain
import lab

REVIEW = lab.PROJECT / 'Docs/Validation/20261008-compilation-domain/source-review.json'


class DomainAuditTests(unittest.TestCase):
    def test_response_generation_and_failed_csc_are_not_compilation_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'synthetic.log'
            path.write_text('''[1/10 0s] WriteText Library/Bee/artifacts/test/Latios2022Lab.Editor.rsp
[2/10 0s] Csc Library/Bee/artifacts/test/Latios2022Lab.Runtime.dll (+2 others)
[3/10 0s] ILPostProcess Library/Bee/artifacts/test/post-processed/Latios2022Lab.Runtime.dll (+pdb)
[4/10 0s] CopyFiles Library/ScriptAssemblies/Latios2022Lab.Runtime.dll
[5/10 0s] Csc Library/Bee/artifacts/test/Latios2022Lab.EditorTools.dll (+2 others)
Assets/Latios2022Tests/EditorTools/LabEnvironment.cs(1,1): error DC0061: synthetic generator failure
''')
            report = audit_domain.audit(lab.PROJECT, REVIEW, path)
            self.assertEqual('NO_CSC_COMPLETION_EVIDENCE', report['assemblies']['Latios2022Lab.Editor']['native_status'])
            self.assertEqual('FAILED', report['assemblies']['Latios2022Lab.EditorTools']['native_status'])
            self.assertEqual('COMPILED_AND_COPIED_IN_RECORDED_RUN', report['assemblies']['Latios2022Lab.Runtime']['native_status'])
            self.assertEqual('NO_CSC_COMPLETION_EVIDENCE', report['assemblies']['Latios2022Lab.PlayMode']['native_status'])

    def test_logged_rsp_is_labelled_and_stops_before_environment_text(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'synthetic.log'
            path.write_text('''##### Contents of Library/Bee/artifacts/test/Latios2022Lab.EditorTools.rsp
-r:"Library/Unity.Entities.ref.dll"
-define:ENABLE_UNITY_COLLECTIONS_CHECKS
-analyzer:"Library/SystemGenerator.dll"
"Assets/Latios2022Tests/EditorTools/LabEnvironment.cs"
-langversion:9.0
##### Custom Environment Variables
NOT_A_COMPILER_ARGUMENT=1
''')
            report = audit_domain.audit(lab.PROJECT, REVIEW, path, Path(tmp))
            block = next(iter(report['assemblies']['Latios2022Lab.EditorTools']['logged_response_contents'].values()))
            self.assertEqual(['Library/Unity.Entities.ref.dll'], block['references'])
            self.assertEqual(['ENABLE_UNITY_COLLECTIONS_CHECKS'], block['defines'])
            self.assertIn('not recovered original file bytes', block['provenance'])
            self.assertEqual([], report['retained_compilation_files'])

    def test_changed_source_or_assembly_cannot_reuse_frozen_review(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = Path(tmp) / 'Latios2022Lab'
            shutil.copytree(lab.PROJECT / 'Assets', project / 'Assets')
            source = project / 'Assets/Latios2022Tests/Editor/CoreTests.cs'
            original = source.read_text(); source.write_text(original + '\n// Unreviewed fixture change\n')
            with self.assertRaisesRegex(ValueError, 'C# sources differ'):
                audit_domain.audit(project, REVIEW)
            source.write_text(original)
            definition = project / 'Assets/Latios2022Tests/Editor/Latios2022Lab.Editor.asmdef'
            data = json.loads(definition.read_text()); data['allowUnsafeCode'] = True
            definition.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, 'Asmdef differs'):
                audit_domain.audit(project, REVIEW)


if __name__ == '__main__':
    unittest.main()
