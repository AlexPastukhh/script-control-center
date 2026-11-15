"""Offline Script Center Mirror GET-only launcher contract; no Windows execution."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parent


class MirrorGatewayReadLauncherTest(unittest.TestCase):
    def test_reviewed_source_and_fixed_listener(self):
        text = (ROOT / "Fortis-Mirror-Gateway-Read.ps1").read_text("utf-8-sig")
        for part in (
            "#requires -Version 5.1", "Start-MirrorGatewayReadOnly.ps1",
            "c0f5db887f29996ee3464cf991674c31d0a22eaf",
            "MIRROR_SOURCE_PIN_CHANGED", "MIRROR_RUNTIME_BLOB_MISMATCH", "MIRROR_SOURCE_DIRTY",
            "MIRROR_ORIGIN_DENIED", "MIRROR_ELEVATED_DENIED",
            "MIRROR_PRIVATE_ACL_", "SetAccessRuleProtection($true, $false)",
            "AreAccessRulesProtected", "MIRROR_CONFIG_SCHEMA_INVALID",
            "MIRROR_HTTPS_PATHS_CONFIGURED=YES", "MIRROR_HTTPS_STARTED=NO",
            "MIRROR_SCRIPT_CENTER_PREFLIGHT_PASS",
            "MIRROR_HTTPS_PORT_443=OCCUPIED_OR_UNKNOWN", "MIRROR_HTTPS_LIVE_HEALTH=NOT_TESTED",
            "-SourceSha $pin", "-Bind '127.0.0.1'", "-AllowedRepo @('fortis-approvals')",
        ):
            with self.subTest(part=part):
                self.assertIn(part, text)
        for banned in ("Start-MirrorAPI.ps1", "git pull", "git fetch", "git reset",
                       "docker run", "docker volume", "Set-NetFirewallRule",
                       "Manage-Integrations.ps1", "Start-Service", "Stop-Process",
                       "Invoke-Expression", "catalog.xml"):
            with self.subTest(banned=banned):
                self.assertNotIn(banned, text)

    def test_exact_gateway_read_runtime_blob_pins(self):
        source = (ROOT / "Fortis-Mirror-Gateway-Read.ps1").read_text("utf-8-sig")
        for marker in (
            "'Start-MirrorGatewayReadOnly.ps1' = '368db7bb6ac4b64d6ecff0fda76a62ae3ff5779c'",
            "'mirror_api/__init__.py' = '9cebe46e8c113d55d1aca036cca594a2f2853b56'",
            "'mirror_api/gateway_readonly.py' = '93c61da0270f1d5d6fe97ba36a34579c403332d9'",
            "'mirror_api/operations.py' = '43d752412c8102a92c0804aa254623ce230347a9'",
            "if ($LASTEXITCODE -ne 0 -or $sha -cne $pin) { throw 'MIRROR_SOURCE_PIN_CHANGED' }",
            "if ($LASTEXITCODE -ne 0 -or $actual -cne $runtimeBlobs[$rel])"):
            self.assertIn(marker, source)
        self.assertNotIn("git pull", source)
        self.assertNotIn("git fetch", source)

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
