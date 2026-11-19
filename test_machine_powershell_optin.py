"""V1 machine ASK Script Center opt-in: offline checks, no Windows host change."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parent
PS=shutil.which("powershell.exe" if os.name=="nt" else "pwsh")
GIT=shutil.which("git.exe" if os.name=="nt" else "git")


class MachineOptInSourceTests(unittest.TestCase):
    def test_missing_config_keeps_owner_only_launch(self):
        src=(ROOT/"Fortis-Owner-PowerShell-Executor.ps1").read_text("utf-8-sig")
        helper=(ROOT/"Get-FortisMachinePowerShellOptIn.ps1").read_text("utf-8-sig")
        self.assertIn("Get-FortisMachinePowerShellOptIn @optinArgs",src)
        self.assertIn("Get-FortisPowerShellExecutorWindowsMain -Root $repo",src)
        self.assertIn("-ReceiptDb $paths.receiptDb @machineLaunchArgs",src)
        self.assertIn("return @{}",helper)
        self.assertIn("if (-not (Test-Path -LiteralPath $ConfigPath))",helper)
        self.assertIn("machine-script-center.json",helper)
        self.assertIn("Assert-PrivateFile $actual 'machineOptIn' $true",helper)
        self.assertIn("Assert-FortisMachineSecretFiles @guardArgs",helper)
        self.assertIn("MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED",helper)
        self.assertIn("merge-base --is-ancestor",helper)
        self.assertIn(":(glob,exclude)docs/**/*.md",helper)
        self.assertIn("--no-renames",helper)
        self.assertIn("MACHINE_SHELL_OPTIN_ASK_ONLY_REQUIRED",helper)
        self.assertIn("'repo-sonar-read-proposal-actor'",helper)
        self.assertIn("'windows-owner'",helper)
        self.assertNotIn("Invoke-Expression",src+helper)
        self.assertNotIn("git fetch",helper)
        self.assertNotIn("mode='ALLOW'",src)
        self.assertTrue((ROOT/"Fortis-Owner-PowerShell-Executor.ps1").read_bytes().startswith(b"\xef\xbb\xbf"))


@unittest.skipUnless(os.name=="nt" and PS and GIT, "PS 5.1 Windows and Git only")
class MachineOptInWindowsFixture(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.profile=Path(self.tmp.name)
        self.configdir=self.profile/"Fortis"/"OwnerShell"
        self.configdir.mkdir(parents=True)
        self.path=self.configdir/"machine-script-center.json"
        self.executor=self.profile/"executor"
        (self.executor/"scripts").mkdir(parents=True)
        # Only a synthetic assertion stub; Executor actual ACL check is
        # separately exercised by its own Windows CI.
        (self.executor/"scripts"/"Assert-MachineSecretPaths.ps1").write_text(
            "function Assert-FortisMachineSecretFiles { param("
            "[string]$OwnerShellRoot,[string]$OwnerSecret,"
            "[string]$MachineBearer,[string]$MachineProof) "
            "if ($MachineBearer -eq $MachineProof) { throw 'SYNTHETIC_REUSE' }"
            "}\n",encoding="utf-8")
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Machine ASK fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("add", "scripts/Assert-MachineSecretPaths.ps1")
        self.git("commit", "-m", "approved fixture")
        self.head=self.git("rev-parse", "HEAD")
        self.wrapper=self.profile/"probe.ps1"
        self.wrapper.write_text(r"""
param([string]$Helper,[string]$ProfileRoot,[string]$Root,
      [string]$ExpectedHead)
$ErrorActionPreference='Stop'
$env:LOCALAPPDATA=$ProfileRoot
function Assert-PrivateFile {
    param([string]$Value,[string]$Label,[bool]$Private)
    if (-not $Private -or $Label -ne 'machineOptIn') {
        throw 'SYNTHETIC_PRIVATE_CONFIG_REQUIRED'
    }
    return $Value
}
. $Helper
$optin=Join-Path $ProfileRoot 'Fortis\OwnerShell\machine-script-center.json'
try {
    $namedArgs=@{
        ConfigPath=$optin
        ExecutorRoot=$Root
        ExecutorHeadSha=$ExpectedHead
        OwnerSecretFile=(Join-Path $ProfileRoot 'old-secret.txt')
    }
    $value=Get-FortisMachinePowerShellOptIn @namedArgs
    if ($value.Count -eq 0) {
        Write-Output 'RESULT:OWNER_ONLY'
    } elseif ($value.Count -eq 4 -and
              $value.MachineActor -ceq 'repo-sonar-read-proposal-actor' -and
              $value.MachineTarget -ceq 'windows-owner') {
        Write-Output 'RESULT:MACHINE_ASK'
    } else { throw 'MACHINE_SHELL_OPTIN_WRONG_ARGS' }
    exit 0
} catch {
    Write-Output ('DENIED:' + $_.Exception.Message)
    exit 3
}
""",encoding="utf-8")
        self.base={
            "schemaVersion":1,"enabled":True,"mode":"ASK_ONLY",
            "executorHeadSha":self.head,
            "machineBearerFile":str(self.configdir/"machine-bearer"),
            "machineProofFile":str(self.configdir/"machine-proof"),
        }

    def git(self,*args):
        proc=subprocess.run([GIT,"-C",str(self.executor),*args],
                            capture_output=True,text=True,check=True,timeout=20)
        return proc.stdout.strip()

    def write(self,data):
        self.path.write_text(json.dumps(data),encoding="utf-8")

    def commit(self,path,content):
        target=self.executor/path
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(content,encoding="utf-8")
        self.git("add","--",path)
        self.git("commit","-m","fixture update")

    def probe(self,sha=None):
        if sha is None:
            sha=self.git("rev-parse","HEAD")
        return subprocess.run(
            [PS,"-NoLogo","-NoProfile","-NonInteractive","-File",str(self.wrapper),
             "-Helper",str(ROOT/"Get-FortisMachinePowerShellOptIn.ps1"),
             "-ProfileRoot",str(self.profile),
             "-Root",str(self.executor),"-ExpectedHead",sha],
            capture_output=True,text=True,timeout=20)

    def test_owner_only_default_absence(self):
        proc=self.probe()
        self.assertEqual(0,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("RESULT:OWNER_ONLY",proc.stdout)

    def test_valid_explicit_ask_and_exact_sha(self):
        self.write(self.base)
        proc=self.probe()
        self.assertEqual(0,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("RESULT:MACHINE_ASK",proc.stdout)

    def test_allow_disabled_wrong_head_and_unknown_keys_denied(self):
        variations=[
            ({**self.base,"mode":"ALLOW"},"MACHINE_SHELL_OPTIN_ASK_ONLY_REQUIRED",self.head),
            ({**self.base,"enabled":False},"MACHINE_SHELL_OPTIN_ASK_ONLY_REQUIRED",self.head),
            ({**self.base,"schemaVersion":2},"MACHINE_SHELL_OPTIN_ASK_ONLY_REQUIRED",self.head),
            ({**self.base,"approved":True},"MACHINE_SHELL_OPTIN_SCHEMA_DENIED",self.head),
            (self.base,"MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED","f"*40),
            ({**self.base,"machineBearerFile":"relative-path"},"MACHINE_SHELL_OPTIN_SECRET_PATH_INVALID",self.head),
        ]
        for values,reason,sha in variations:
            with self.subTest(reason=reason,sha=sha):
                self.write(values)
                proc=self.probe(sha)
                self.assertEqual(3,proc.returncode,proc.stdout+proc.stderr)
                self.assertIn("DENIED:"+reason,proc.stdout)
                self.assertNotIn("RESULT:MACHINE_ASK",proc.stdout)

    def test_doc_only_fast_forward_preserves_machine_ask(self):
        self.write(self.base)
        self.commit("README.md","approved documentation update\n")
        self.commit("docs/use-cases/executor.md","machine ASK user guide\n")
        proc=self.probe()
        self.assertEqual(0,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("RESULT:MACHINE_ASK",proc.stdout)

    def test_executable_change_after_doc_update_denied(self):
        self.write(self.base)
        self.commit("README.md","harmless docs update\n")
        self.commit("scripts/Assert-MachineSecretPaths.ps1","# new executable logic\n")
        proc=self.probe()
        self.assertEqual(3,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("DENIED:MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED",proc.stdout)

    def test_non_whitelisted_markdown_denied(self):
        self.write(self.base)
        self.commit("scripts/notes.md","not an exempt documentation path\n")
        proc=self.probe()
        self.assertEqual(3,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("DENIED:MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED",proc.stdout)

    def test_non_ancestor_documentation_pin_denied(self):
        self.git("checkout","-b","alternate")
        self.commit("README.md","alternate branch docs\n")
        self.base["executorHeadSha"]=self.git("rev-parse","HEAD")
        self.git("checkout","main")
        self.commit("README.md","main branch docs\n")
        self.write(self.base)
        proc=self.probe()
        self.assertEqual(3,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("DENIED:MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED",proc.stdout)

    def test_malformed_json_denied(self):
        self.path.write_text("{bad-json",encoding="utf-8")
        proc=self.probe()
        self.assertEqual(3,proc.returncode,proc.stdout+proc.stderr)
        self.assertIn("DENIED:MACHINE_SHELL_OPTIN_CONFIG_JSON_DENIED",proc.stdout)


if __name__=="__main__":
    unittest.main()
