"""Run the real source resolver against disposable Git clones, never installed state."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
POWERSHELL = (os.environ.get("FORTIS_TEST_POWERSHELL")
              or shutil.which("powershell.exe" if os.name == "nt" else "pwsh"))
GIT = shutil.which("git.exe" if os.name == "nt" else "git")


@unittest.skipUnless(POWERSHELL and GIT, "PowerShell and Git runtime required")
class ExecutorSourceResolver(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.profile = Path(self.tmp.name)
        self.repo = self.profile / "Repos/fortis-powershell-executor"
        self.repo.mkdir(parents=True)
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Resolver fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("remote", "add", "origin",
                 "https://github.com/AlexPastukhh/fortis-powershell-executor.git")
        (self.repo / "README.md").write_text("disposable source fixture\n")
        self.git("add", "README.md")
        self.git("commit", "-m", "fixture")
        self.head = self.git("rev-parse", "HEAD")
        self.git("update-ref", "refs/remotes/origin/main", self.head)
        self.env = {**os.environ, "GIT_TERMINAL_PROMPT": "0"}
        if os.name != "nt":
            shim = self.profile / "bin"
            shim.mkdir()
            exe = shim / "git.exe"
            exe.symlink_to(Path(GIT).resolve())
            self.env["PATH"] = str(shim) + os.pathsep + self.env.get("PATH", "")
        self.wrapper = self.profile / "probe.ps1"
        self.wrapper.write_text(r"""
param([string]$Helper,[string]$ProfileRoot,[string]$SourceRoot,[switch]$RepeatGitDiscovery)
$ErrorActionPreference = 'Stop'
$env:USERPROFILE = $ProfileRoot
if ($RepeatGitDiscovery) {
    function Get-Command {
        param($Name,$CommandType,$ErrorAction)
        $found = @(Microsoft.PowerShell.Core\Get-Command @PSBoundParameters)
        $found
        $found
    }
}
. $Helper
try {
    $value = Get-FortisPowerShellExecutorWindowsMain -Root $SourceRoot -Caller OWNER_SHELL
    Write-Output ('RESULT:' + ($value | ConvertTo-Json -Compress))
    exit 0
} catch {
    Write-Output ('DENIED:' + $_.Exception.Message)
    exit 3
}
""", encoding="utf-8-sig")

    def git(self, *args):
        return subprocess.check_output(
            [GIT, "-c", "core.hooksPath=/dev/null", "-C", str(self.repo), *args],
            stderr=subprocess.DEVNULL).decode().strip()

    def probe(self, root=None, duplicate_git=False):
        return subprocess.run(
            [POWERSHELL, "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
             str(self.wrapper), "-Helper",
             str(ROOT / "Get-FortisPowerShellExecutorWindowsMain.ps1"),
             "-ProfileRoot", str(self.profile), "-SourceRoot", str(root or self.repo)]
            + (["-RepeatGitDiscovery"] if duplicate_git else []),
            capture_output=True, text=True, env=self.env, timeout=20)

    def denied(self, code, root=None):
        response = self.probe(root)
        self.assertEqual(3, response.returncode, response.stdout + response.stderr)
        self.assertIn("DENIED:" + code, response.stdout)

    def test_clean_synced_standalone_primary_is_accepted(self):
        response = self.probe()
        self.assertEqual(0, response.returncode, response.stdout + response.stderr)
        line = next(x for x in response.stdout.splitlines() if x.startswith("RESULT:"))
        data = json.loads(line[len("RESULT:"):])
        self.assertEqual(self.head, data["HeadSha"])
        self.assertTrue(self.repo.samefile(data["Root"]), data["Root"])

    def test_multiple_git_discovery_results_select_one_executable(self):
        response = self.probe(duplicate_git=True)
        self.assertEqual(0, response.returncode, response.stdout + response.stderr)
        line = next(x for x in response.stdout.splitlines() if x.startswith("RESULT:"))
        self.assertEqual(self.head, json.loads(line[len("RESULT:"):])["HeadSha"])

    def test_mirror_path_is_rejected(self):
        self.denied("OWNER_SHELL_SOURCE_LOCATION_DENIED",
                    self.profile / "Repos/docke-windows-mirror")

    def test_wrong_origin_is_rejected(self):
        self.git("remote", "set-url", "origin",
                 "https://github.com/AlexPastukhh/docke-windows-mirror.git")
        self.denied("OWNER_SHELL_SOURCE_ORIGIN_DENIED")

    def test_dirty_source_is_rejected(self):
        (self.repo / "untracked.txt").write_text("preserve")
        self.denied("OWNER_SHELL_SOURCE_DIRTY")

    def test_non_main_is_rejected(self):
        self.git("switch", "-c", "feature")
        self.denied("OWNER_SHELL_SOURCE_NOT_MAIN")

    def test_missing_upstream_is_rejected(self):
        self.git("update-ref", "-d", "refs/remotes/origin/main")
        self.denied("OWNER_SHELL_SOURCE_UPSTREAM_UNVERIFIED")

    def test_stale_source_is_rejected(self):
        self.git("commit", "--allow-empty", "-m", "advance fixture")
        self.denied("OWNER_SHELL_SOURCE_NOT_SYNCED")

    def test_git_file_is_not_a_primary_clone(self):
        meta = self.repo / ".git"
        moved = self.profile / "fixture-git"
        meta.rename(moved)
        meta.write_text("gitdir: " + str(moved) + "\n")
        self.denied("OWNER_SHELL_SOURCE_NOT_PRIMARY_CLONE")


if __name__ == "__main__":
    unittest.main()
