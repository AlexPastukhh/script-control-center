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
                     "OWNER_SHELL_SOURCE_SHA_MISMATCH", "OWNER_SHELL_SOURCE_DIRTY",
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
