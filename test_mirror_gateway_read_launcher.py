"""Offline Script Center Mirror GET-only launcher contract; no Windows execution."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parent


class MirrorGatewayReadLauncherTest(unittest.TestCase):
    def test_reviewed_source_and_fixed_listener(self):
        text = (ROOT / "Fortis-Mirror-Gateway-Read.ps1").read_text("utf-8-sig")
        for part in (
            "#requires -Version 5.1", "Start-MirrorGatewayReadOnly.ps1",
            "Get-FortisMirrorWindowsMain.ps1", "-Caller 'MIRROR'",
            "MIRROR_ELEVATED_DENIED",
            "MIRROR_PRIVATE_ACL_", "SetAccessRuleProtection($true, $false)",
            "AreAccessRulesProtected", "MIRROR_CONFIG_SCHEMA_INVALID",
            "MIRROR_HTTPS_PATHS_CONFIGURED=YES", "MIRROR_HTTPS_STARTED=NO",
            "MIRROR_SCRIPT_CENTER_PREFLIGHT_PASS",
            "MIRROR_HTTPS_PORT_443=OCCUPIED_OR_UNKNOWN", "MIRROR_HTTPS_LIVE_HEALTH=NOT_TESTED",
            "-SourceSha $mirrorSource.HeadSha", "-Bind '127.0.0.1'", "-AllowedRepo @('fortis-approvals')",
        ):
            with self.subTest(part=part):
                self.assertIn(part, text)
        for banned in ("Start-MirrorAPI.ps1", "git pull", "git fetch", "git reset",
                       "docker run", "docker volume", "Set-NetFirewallRule",
                       "Manage-Integrations.ps1", "Start-Service", "Stop-Process",
                       "Invoke-Expression", "catalog.xml"):
            with self.subTest(banned=banned):
                self.assertNotIn(banned, text)

    def test_gateway_read_passes_verified_current_head_to_launcher(self):
        source = (ROOT / "Fortis-Mirror-Gateway-Read.ps1").read_text("utf-8-sig")
        for marker in (
            ". (Join-Path $PSScriptRoot 'Get-FortisMirrorWindowsMain.ps1')",
            "Get-FortisMirrorWindowsMain -Root $root -Caller 'MIRROR'",
            "HeadSha = $mirror.HeadSha",
            "& ($mirrorSource.Launcher) -SourceSha $mirrorSource.HeadSha"):
            self.assertIn(marker, source)
        for removed in ("$pin =", "runtimeBlobs", "SOURCE_PIN_CHANGED", "RUNTIME_BLOB_MISMATCH"):
            self.assertNotIn(removed,source)
        for blocked in ("git pull", "git fetch"):
            self.assertNotIn(blocked, source)

    def test_paths_only_first_setup_and_manual_catalog(self):
        data = (ROOT / "TEXT-IMPORT-MIRROR-GATEWAY-READ.txt").read_text("utf-8-sig")
        lines = [x.strip() for x in data.splitlines()
                 if x.strip() and not x.strip().startswith("#")]
        self.assertEqual([
            r"Fortis Mirror — Gateway READ HTTPS | @app\Fortis-Mirror-Gateway-Read.ps1 | window"
        ], lines)
        source = (ROOT / "Fortis-Mirror-Gateway-Read.ps1").read_text("utf-8-sig")
        self.assertIn("Get-OwnerSetup", source)
        self.assertIn("Get-NetTCPConnection -LocalPort 443", source)
        self.assertIn("if ($Status)", source)
        self.assertIn("MIRROR_CONFIG_ALREADY_EXISTS", source)
        self.assertIn("ConvertFrom-Json", source)
        self.assertIn("Save-OwnerSetup $settings", source)
        catalog = (ROOT / "ScriptCatalog.cs").read_text("utf-8-sig")
        self.assertNotIn("Fortis-Mirror-Gateway-Read.ps1", catalog)


if __name__ == "__main__":
    unittest.main()
