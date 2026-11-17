"""Static contract for repository-sourced Windows launchers; no Windows execution."""
from pathlib import Path
import hashlib
import re
import unittest

ROOT = Path(__file__).resolve().parent
LAUNCHERS = ROOT / 'launchers'
EXPECTED_GUARD = '08ab2a7b3051e6899a47af8cd09fc2ffe6e560cd7638b8160d1b2c3a40402241'


def content(path):
    return (ROOT / path).read_bytes()


class GitLauncherContract(unittest.TestCase):
    def test_repo2_guard_pin_exact(self):
        guard = content('launchers/FortisRepoPlus2/Repo2DirectGuard.ps1')
        self.assertEqual(hashlib.sha256(guard).hexdigest(), EXPECTED_GUARD)
        startup = content('launchers/FortisRepoPlus2/FortisRepo2-quiet-logs.ps1')
        self.assertEqual(startup.count(EXPECTED_GUARD.upper().encode()), 1)
        self.assertNotIn(b'50084B087F65312D6FEA30FB1E67195FA08675DCADABBD4B1F45BDDA44302E26', startup)

    def test_resource_windows(self):
        for path in [
            'launchers/FortisRepoPlus2/Repo2DirectGuard.ps1',
            'launchers/FortisRDGLauncherV2/Common.ps1',
            'launchers/chatgpt-repo-mode/run-tunnel-quiet-logs.ps1',
            'launchers/chatgpt-repo-mode/run-tunnel.ps1',
        ]:
            with self.subTest(path=path):
                s = content(path)
                self.assertIn(b'-lt 4294967296', s)
                self.assertIn(b'-gt 8589934592', s)
                self.assertNotRegex(s, rb'HostConfig\.Memory\s*-ne\s*1073741824')
                self.assertIn(b'-lt 256', s)
                self.assertIn(b'-gt 1024', s)

    def test_worker_adapters_resolve_only_the_sibling_tunnel_clone(self):
        helper = content('Get-TunnelWindowsMirror.ps1')
        for marker in (b'Repos\\script-control-center', b"Join-Path $reposRoot 'tunnel'",
                       b'AlexPastukhh/tunnel', b'AlexPastukhh/script-control-center', b'scriptCenterDirty', b'branch --show-current',
                       b'status --porcelain --untracked-files=normal', b'rev-list --left-right --count', b'FF584B9F8936657CDE808390C6EC5D09D09260523D909E257652CBE5BC335F0A',
                       b'Get-FileHash'):
            self.assertIn(marker, helper)
        for worker in ('Repo1-Worker.ps1','Repo2-Worker.ps1','Updater-Worker.ps1',
                       'Watcher-Start.ps1','Watcher-Stop.ps1'):
            with self.subTest(worker=worker):
                b = content(worker)
                self.assertIn(b'Get-TunnelWindowsMirror.ps1', b)
                self.assertIn(b'Get-FortisTunnelWindowsMirror', b)
                self.assertIn(b'$mirror.Root', b)
        self.assertIn(b"Join-Path $mirror.Root 'Repo1-Worker.ps1'", content('Repo1-Worker.ps1'))
        self.assertIn(b"Join-Path $mirror.Root 'Repo2-Worker.ps1'", content('Repo2-Worker.ps1'))
        self.assertIn(b"Join-Path $mirror.Root 'launchers\\FortisRDGLauncherV2\\Start-Fortis.ps1'", content('Updater-Worker.ps1'))
        self.assertIn(b"Join-Path $mirror.Root 'launchers\\FortisRDGLauncherV2\\Common.ps1'", content('Watcher-Start.ps1'))
        self.assertIn(b"$script:GuardPath = Join-Path $PSScriptRoot '..\\FortisRepoPlus2\\Repo2DirectGuard.ps1'", content('launchers/FortisRDGLauncherV2/Common.ps1'))

    def test_repo1_control_pin(self):
        path = 'launchers/chatgpt-repo-mode/control.ps1'
        digest = hashlib.sha256(content(path)).hexdigest().upper().encode()
        self.assertIn(b"Join-Path $env:USERPROFILE 'chatgpt-repo-mode\\workspace-config.json'", content(path))
        for file in ['run-tunnel.ps1', 'run-tunnel-quiet-logs.ps1']:
            b = content('launchers/chatgpt-repo-mode/' + file)
            self.assertIn(digest, b)
            self.assertIn(b"$fortisControl = Join-Path $PSScriptRoot 'control.ps1'", b)

    def test_paired_windows_update_pulls_both_repositories_in_order(self):
        updater = content('tools/Update-Windows-ControlCenter.ps1')
        self.assertIn(b'TunnelRepoPath', updater)
        self.assertIn(b'Pulling Tunnel main first', updater)
        self.assertIn(b'Pulling Script Center main', updater)
        self.assertLess(updater.index(b'Pulling Tunnel main first'), updater.index(b'Pulling Script Center main'))
        self.assertIn(b'Get-FortisTunnelWindowsMirror', updater)
        for forbidden in (b'Stop-Process', b'Stop-Service', b'docker stop', b'docker start', b'reset --hard', b'git clean'):
            self.assertNotIn(forbidden, updater)

    def test_primary_desktop_shortcut_is_standalone_catalog(self):
        updater = content('tools/Update-Windows-ControlCenter.ps1')
        self.assertIn(b"$target = Join-Path $repo 'Start-ScriptCatalog.ps1'", updater)
        self.assertNotIn(b"$target = Join-Path $repo 'Start-ControlCenter.ps1'", updater)
        self.assertIn(b"'Start-ScriptCatalog.ps1'", updater)
        for doc in ['README.md', 'docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md']:
            self.assertIn('Start-ScriptCatalog.ps1', (ROOT / doc).read_text(encoding='utf-8'))

    def test_guard_bytes_are_not_normalized(self):
        self.assertIn('launchers/**/*.ps1 -text', (ROOT/'.gitattributes').read_text())
        for file in LAUNCHERS.rglob('*.ps1'):
            data = file.read_bytes()
            self.assertTrue(data)
            self.assertNotIn(b'BEGIN PRIVATE KEY', data)
            self.assertNotIn(b'github_pat_', data)
            self.assertNotIn(b'ghp_', data)


if __name__ == '__main__':
    unittest.main()
