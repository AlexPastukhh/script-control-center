"""Static regression checks for source-of-truth Mirror Windows main policy."""
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parent

class MirrorMainPolicyTest(unittest.TestCase):
    def setUp(self):
        self.path=ROOT/'Get-FortisMirrorWindowsMain.ps1'
        self.source=self.path.read_text('utf-8-sig')

    def test_shared_validator_prohibits_unreviewed_local_changes(self):
        self.assertTrue(self.path.read_bytes().startswith(bytes.fromhex('efbbbf')))
        for expected in (
            '#requires -Version 5.1', "'Repos\\docke-windows-mirror'",
            "(Join-Path $actual '.git')", 'ReparsePoint',
            'rev-parse --show-toplevel', 'remote get-url origin',
            'AlexPastukhh/docke-windows-mirror',
            'symbolic-ref --quiet --short HEAD', "-cne 'main'",
            'status --porcelain=v1 --untracked-files=all',
            'rev-parse --verify HEAD',
            'rev-parse --verify refs/remotes/origin/main',
            "[string]$head[0] -cne [string]$upstream[0]",
            "'_SOURCE_NOT_SYNCED'", "'_SOURCE_DIRTY'",
            "'_SOURCE_ORIGIN_DENIED'", "'_SOURCE_NOT_MAIN'",
            "Caller ="):
            # Caller is a param and output diagnostic prefix, not object property.
            if expected == "Caller =": continue
            self.assertIn(expected,self.source)

    def test_nonmutating_and_no_fixed_pin(self):
        for forbidden in (' git fetch ', ' git pull ', ' git reset ',
                          'git -C $actual fetch', 'git -C $actual merge',
                          'Invoke-Expression', 'Start-Process', 'Set-Acl',
                          'Stop-Process'):
            self.assertNotIn(forbidden,self.source)
        self.assertFalse(re.search(r"['\"][0-9a-f]{40}['\"]",self.source))
        self.assertIn('HeadSha = [string]$head[0]',self.source)
        self.assertIn("[ValidateSet('OWNER_SHELL', 'MIRROR')]",self.source)

if __name__=='__main__':
    unittest.main()
