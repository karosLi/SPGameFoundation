import copy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import verify_inputs


class InputVerificationTests(unittest.TestCase):
    def capture(self, root, dirty=False, commit=None):
        math=root/'Tools/DotnetHarness/.deps/Unity.Mathematics'
        source=math/'src/Unity.Mathematics';source.mkdir(parents=True,exist_ok=True)
        (source/'A.cs').write_text('// synthetic source, not a real dependency capture\n')
        def command(args,cwd):
            if args[:2]==['dotnet','--info']: return 'Synthetic SDK/runtime info for schema testing only'
            if args[1]=='diff': return ''
            if args[1]=='status': return ' M src/Unity.Mathematics/A.cs' if dirty else ''
            if args[1]=='ls-files': return 'src/Unity.Mathematics/A.cs' if cwd==math else ''
            if args[1]=='rev-parse':
                if cwd==math:return commit or verify_inputs.MATH_COMMIT
                return 'a'*40 if args[2]=='HEAD' else 'b'*40
            raise AssertionError(args)
        with patch.object(verify_inputs,'command',command): return verify_inputs.capture(root)

    def test_pinned_clean_source_retains_hash_and_sdk_context(self):
        with tempfile.TemporaryDirectory() as d:
            context=self.capture(Path(d));self.assertTrue(context['unity_mathematics_clean'])
            self.assertEqual(64,len(context['unity_mathematics_source_sha256']))
            self.assertIn('Synthetic',context['dotnet_info'])

    def test_dirty_dependency_refuses_exact_source_label(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(ValueError,'dirty'):self.capture(Path(d),dirty=True)

    def test_wrong_dependency_commit_refuses_exact_source_label(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(ValueError,'pin'):self.capture(Path(d),commit='c'*40)

    def test_even_ignored_extra_csharp_source_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.capture(root)
            (root/'Tools/DotnetHarness/.deps/Unity.Mathematics/src/Unity.Mathematics/Hidden.cs').write_text('// ignored extra')
            with self.assertRaisesRegex(ValueError,'ignored'):self.capture(root)

    def test_context_cannot_omit_runtime_or_use_dirty_flag(self):
        with tempfile.TemporaryDirectory() as d:
            context=self.capture(Path(d))
        for key,value in [('dotnet_info',''),('unity_mathematics_clean',False),('unity_mathematics_clean',1),('schema_version',True)]:
            bad=copy.deepcopy(context);bad[key]=value
            with self.assertRaises(ValueError):verify_inputs.validate_context(bad)


if __name__=='__main__':unittest.main()
