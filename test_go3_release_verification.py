"""Static contract checks for the GO3 image + Repo1/Repo2 Guard release verifier."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent
SCRIPT = (ROOT / "_go3-revision07" / "Invoke-GO3-Guard-Previews.ps1").read_text(encoding="utf-8")


class Go3ReleaseVerificationContract(unittest.TestCase):
    def test_container_and_image_identity_are_pinned(self):
        self.assertIn("04fed7b726a8536d63c4d2533cfc7ee3072956ff0c3f571504d7f709f7909030", SCRIPT)
        self.assertIn("fortis-repo-mode:dc-0.2.52", SCRIPT)
        self.assertIn("sha256:2c84fabebb34265e53b020f21a11683a4c68cbb41a08f5f11f9b01c7de5ed8c9", SCRIPT)
        self.assertIn("docker inspect --type container --format '{{json .}}' -- $expectedContainerId", SCRIPT)
        self.assertIn("docker image inspect --format '{{json .}}' $expectedImageId", SCRIPT)
        self.assertIn("RepoTags", SCRIPT)
        self.assertIn("Config.User", SCRIPT)
        self.assertIn("Config.WorkingDir", SCRIPT)
        self.assertIn("MCP_CLIENT_DOCKER=true", SCRIPT)
        self.assertIn("GO3_IMAGE_IDENTITY_PASS", SCRIPT)

    def test_source_manifest_is_pinned(self):
        self.assertIn("8036348F69BB37F94F5E6FDE529BF44A292F4EB91977CE91A491E9860AADBFCA", SCRIPT)
        self.assertIn("AEF7D17084F951AEAB6F98CF91E7A39E1D6043A0DAC6BC123DBF99463887B0F9", SCRIPT)

    def test_both_guards_preview_the_same_pinned_image(self):
        self.assertIn("$repo1Path -Mode Preview", SCRIPT)
        self.assertIn("$repo2Path -Mode Preview -ContainerId $expectedContainerId", SCRIPT)
        self.assertIn("$repo1.ImageId -cne $expectedImageId", SCRIPT)
        self.assertIn("$repo2.ImageId -cne $expectedImageId", SCRIPT)
        self.assertIn("GO3_REPO1_GUARD_PREVIEW_PASS", SCRIPT)
        self.assertIn("GO3_REPO2_GUARD_PREVIEW_PASS", SCRIPT)
        self.assertIn("GO3_BOTH_GUARD_PREVIEWS_PASS", SCRIPT)
        self.assertIn("GO3_RELEASE_VERIFICATION_PASS", SCRIPT)

    def test_sanitized_snapshot_reports_unpinned_docker_fields(self):
        for field in (
            "MemorySwapBytes",
            "ShmSizeBytes",
            "RestartPolicyName",
            "Tmpfs",
            "Networks",
            "PublishedPortBindingCount",
            "Mounts",
        ):
            self.assertIn(field, SCRIPT)
        self.assertIn("GO3_CONTAINER_AUXILIARY_CONFIG=", SCRIPT)
        self.assertIn("Get-GO3PropertyValue $mount 'Name'", SCRIPT)
        self.assertIn("Get-GO3PropertyValue $mount 'Destination'", SCRIPT)
        self.assertIn("Get-GO3PropertyValue $mount 'RW'", SCRIPT)
        self.assertIn("Docker mount inspection is missing Type, Destination or RW", SCRIPT)
        self.assertIn("Docker volume mount inspection is missing the volume Name", SCRIPT)
        self.assertNotIn("Get-GO3PropertyValue $mount 'Source'", SCRIPT)
        self.assertNotIn("Get-GO3PropertyValue $mount 'Target'", SCRIPT)
        self.assertNotIn("$container.Config.Env", SCRIPT)

    def test_release_verification_has_no_lifecycle_or_profile_mutations(self):
        forbidden = re.compile(
            r"(?im)^\s*(?:docker(?:\.exe)?\s+(?:start|stop|restart|rm|create|run|compose\s+(?:up|down))\b"
            r"|Stop-Process\b|Start-Process\b|Remove-Item\b|Set-Content\b|Add-Content\b"
            r"|Copy-Item\b|Move-Item\b|Set-Acl\b)"
        )
        self.assertIsNone(forbidden.search(SCRIPT))
        for marker in (
            "NO_TUNNEL_START_OR_STOP",
            "NO_WINDOWS_PROFILE_EDITS",
            "NO_RUNTIME_CONFIG_REPAIR",
            "NO_SONAR_REQUEST",
        ):
            self.assertIn(marker, SCRIPT)


if __name__ == "__main__":
    unittest.main()
