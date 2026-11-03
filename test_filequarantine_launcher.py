"""Static checks for the Script Catalog FileQuarantine adapter.

No Windows process, Docker operation, checkout, Git push or live GUI launch.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent


class FileQuarantineLauncherContract(unittest.TestCase):
    def test_launcher_is_windows_powershell_51_and_uses_canonical_clone(self):
        path = ROOT / "FileQuarantine-Launcher.ps1"
        raw = path.read_bytes()
        self.assertTrue(raw.startswith(b"\xef\xbb\xbf"))
        text = raw.decode("utf-8-sig")
        for marker in (
            "#requires -Version 5.1", "Windows_NT", "$env:USERPROFILE",
            "'Repos'", "'FileQuarantine'", "'00-Preflight.ps1'",
            "'FileQuarantine-GUI.ps1'", "Test-Path -LiteralPath",
            "Join-Path $PSHOME 'powershell.exe'",
            "-NoLogo -NoProfile -ExecutionPolicy Bypass -File $preflight",
            "if ($LASTEXITCODE -ne 0)",
            "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File $gui",
            "FILEQ_PREFLIGHT_FAILED", "FILEQUARANTINE_LAUNCH_ERROR",
            "[Console]::Error.WriteLine",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, text)
        self.assertLess(text.index("-File $preflight"), text.index("-File $gui"))
        for forbidden in (
            "Start-FileQuarantine.cmd", "Read-Host", " pause", "git pull",
            "git clone", "docker compose", "docker run", "-InitializeEmptyVolumes",
            "Setup-Project.ps1 -Apply\n", "Invoke-Expression", "Start-Process",
            "-Force", "Remove-Item",
        ):
            self.assertNotIn(forbidden, text.split("#>", 1)[-1])

    def test_catalog_registers_nothing_automatically(self):
        source = (ROOT / "ScriptCatalog.cs").read_text(encoding="utf-8-sig")
        self.assertNotIn("FileQuarantine-Launcher.ps1", source)
        self.assertNotIn("filequarantine.launch", source)
        self.assertIn('AddButton(top, "＋ Добавить", delegate { AddEntry(); }', source)
        self.assertIn('new BundleDefinition("fortis-rdg.repo2"', source)
        self.assertIn('new BundleDefinition("fortis-rdg.watcher.stop"', source)

    def test_launcher_has_no_catalog_import_or_git_side_effects(self):
        source = (ROOT / "FileQuarantine-Launcher.ps1").read_text(encoding="utf-8-sig")
        for forbidden in ("catalog.xml", "ImportBuiltInBundle", "ImportFromText",
                          "git.exe", "docker.exe", "docker build", "git clone", "git pull",
                          "git push", "-InitializeEmptyVolumes"):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
