"""Source-only contract for opt-in Owner PowerShell Executor adapter."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parent


class OwnerPowerShellAdapterTest(unittest.TestCase):
    def test_launcher_safety(self):
        data=(ROOT/"Fortis-Owner-PowerShell-Executor.ps1").read_bytes()
        self.assertTrue(data.startswith(bytes.fromhex("efbbbf")))
        source=data.decode("utf-8-sig")
        for item in ("#requires -Version 5.1", "OWNER_SHELL_ADMIN_DENIED",
                     "OWNER_SHELL_SOURCE_BASE_MISMATCH", "OWNER_SHELL_SOURCE_DIFF_DENIED",
                     "OWNER_SHELL_SOURCE_BLOB_MISMATCH", "OWNER_SHELL_SOURCE_DIRTY",
                     "OWNER_SHELL_SOURCE_ORIGIN_INVALID", "OWNER_SHELL_PORT_IN_USE",
                     "Assert-PrivateFile", "AreAccessRulesProtected",
                     "OWNER_SHELL_PATHS_CONFIGURED=YES", "OWNER_SHELL_STARTED=NO",
                     "127.0.0.1", "10443", "Start-OwnerPowerShellExecutor.ps1",
                     "-ReceiptDb $paths.receiptDb"):
            with self.subTest(item=item):
                self.assertIn(item,source)
        for denied in ("git pull", "git fetch", "git reset",
                       "Invoke-Expression", "Stop-Process",
                       "Manage-Integrations.ps1", "docker volume", "catalog.xml"):
            with self.subTest(denied=denied):
                self.assertNotIn(denied,source)

    def test_docs_only_mirror_upgrade_does_not_relax_runtime_pin(self):
        source=(ROOT/"Fortis-Owner-PowerShell-Executor.ps1").read_text("utf-8-sig")
        self.assertIn("merge-base --is-ancestor $approvedBase $sha",source)
        self.assertIn("diff --name-only --no-ext-diff $approvedBase $sha",source)
        self.assertIn("$_ -cne 'docs/GATEWAY-READ-ONLY.md'",source)
        self.assertIn("'Start-OwnerPowerShellExecutor.ps1' = '36798dbc1c48829eb395b08128618ac3c8962b57'",source)
        self.assertIn("'mirror_api' = '390ad3cd7938364a5861cb9d3ef707f31d7037b9'",source)
        self.assertIn("OWNER_SHELL_SOURCE_DIRTY",source)
        self.assertIn("OWNER_SHELL_SOURCE_ORIGIN_INVALID",source)

    def test_one_time_winforms_setup_and_protected_persistence(self):
        source=(ROOT/"Fortis-Owner-PowerShell-Executor.ps1").read_text("utf-8-sig")
        setup=source[source.index("function Show-OwnerShellSetup"):source.index("function Save-OwnerShellPaths")]
        save=source[source.index("function Save-OwnerShellPaths"):source.index("\ntry {",source.index("function Save-OwnerShellPaths"))]
        for expected in (
            "System.Windows.Forms.Form", "System.Windows.Forms.OpenFileDialog",
            "serverCert", "serverKey", "clientCA", "secretFile", "receiptDb",
            "OWNER_SHELL_ACL_*", "ShowDialog()", "ShowDialog($form)",
            "return $null", "Assert-PrivateFile", "OWNER_SHELL_RECEIPT_DB_CHANGE_REQUIRES_REVIEW"
        ):
            self.assertIn(expected,setup)
        self.assertNotIn("Read-Host",source)
        self.assertIn("OWNER_SHELL_RECEIPT_DB_INVALID",source)
        self.assertIn("[IO.File]::Replace($temp, $ConfigPath, $backup)",save)
        self.assertIn("SetAccessRuleProtection($true, $false)",save)
        self.assertIn("Assert-PrivateFile $ConfigPath 'config' $true",save)
        self.assertIn("if (-not $exists -or $Configure)",source)
        self.assertIn("Save-OwnerShellPaths $settings $user $configPath $exists",source)
        self.assertIn("OWNER_SHELL_PATHS_CONFIGURED=YES",source)
        self.assertIn("MessageBox",source)

    def test_manual_registration(self):
        content=(ROOT/"TEXT-IMPORT-OWNER-POWERSHELL.txt").read_bytes()
        self.assertTrue(content.startswith(bytes.fromhex("efbbbf")))
        lines=[r.strip() for r in content.decode("utf-8-sig").splitlines()
               if r.strip() and not r.lstrip().startswith("#")]
        self.assertEqual(
            ["Fortis — PowerShell Executor | @app\\Fortis-Owner-PowerShell-Executor.ps1 | window"],lines)
        guide=(ROOT/"docs/OWNER-POWERSHELL-EXECUTOR-LAUNCH.md").read_text("utf-8")
        for marker in ("SAME EXISTING SQLite", "Своё окно",
                       "OWNER_SHELL_PATHS_CONFIGURED=YES", "NOT a sandbox"):
            self.assertIn(marker.casefold(),guide.casefold())
        catalog=(ROOT/"ScriptCatalog.cs").read_text("utf-8-sig")
        self.assertNotIn("Fortis-Owner-PowerShell-Executor.ps1",catalog)


if __name__=="__main__":
    unittest.main()
