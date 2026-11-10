"""Offline source checks + optional real Gitleaks CLI smoke on synthetic temporary data.

Never runs the Windows GUI or a Fortis worker. The CLI smoke skips if the
appropriate local binary is not present in this environment.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
DUMMY = "DUMMY-KEY-SYNTHETIC-FIXTURE-ONLY"
CONFIG = """[[rules]]
id = "synthetic-fixture"
description = "Offline test marker, not a credential"
regex = 'DUMMY-KEY-[A-Z-]+'
secretGroup = 0
keywords = ["DUMMY-KEY"]
"""


def run_fixed(exe, args, cwd, timeout=60):
    return subprocess.run([exe] + args, cwd=str(cwd), capture_output=True,
                          text=True, timeout=timeout, check=False)


class GitleaksSourceContract(unittest.TestCase):
    def test_explicit_registration_does_not_bypass_execution_policy(self):
        cmd = (ROOT / "Start-GitleaksScanner.cmd").read_text(encoding="utf-8")
        self.assertIn("-NoProfile -STA -File", cmd)
        self.assertNotIn("-ExecutionPolicy", cmd)
        self.assertNotIn("Bypass", cmd.splitlines()[-2])
        ps_bytes = (ROOT / "Start-GitleaksScanner.ps1").read_bytes()
        self.assertTrue(ps_bytes.startswith(b"\xef\xbb\xbf"))
        ps = ps_bytes.decode("utf-8-sig")
        self.assertIn("#requires -Version 5.1", ps)
        self.assertIn("GitleaksScanEngine.cs", ps)
        self.assertIn("GitleaksScannerForm.cs", ps)
        self.assertNotIn("-ExecutionPolicy", ps)
        entry = (ROOT / "TEXT-IMPORT-GITLEAKS.txt").read_text(encoding="utf-8-sig")
        lines = [s.strip() for s in entry.splitlines() if s.strip() and not s.startswith("#")]
        self.assertEqual([r"Gitleaks — локальная проверка | @app\Start-GitleaksScanner.cmd | window"], lines)
        catalog = (ROOT / "ScriptCatalog.cs").read_text(encoding="utf-8-sig")
        self.assertIn("ImportFromText()", catalog)
        self.assertNotIn("Start-GitleaksScanner.cmd", catalog)

    def test_no_source_or_secret_leaks_to_logs_and_reports_outside_repo(self):
        engine = (ROOT / "GitleaksScanEngine.cs").read_text()
        for marker in ("--redact=100", "--log-level=error", "--no-banner",
                       "--exit-code=3", "--report-format=json", "--report-path",
                       "GitleaksReports", "PrivateReportDirectory", "SetAccessRuleProtection(true, false)",
                       "RedirectStandardOutput = true", "RedirectStandardError = true",
                       "process.BeginOutputReadLine()", "process.BeginErrorReadLine()",
                       "private static void Discard(", "IsInside(reports, folder)",
                       "RejectReparseAncestors", "GIT_TERMINAL_PROMPT", "FindGit(folder)",
                       'info.EnvironmentVariables["PATH"]', "Path.GetFullPath(selectedFolder)"):
            with self.subTest(marker=marker):
                self.assertIn(marker, engine)
        self.assertNotIn('ProcessStartInfo("cmd.exe"', engine)
        self.assertNotIn("Invoke-RestMethod", engine)
        self.assertNotIn("HttpClient", engine)
        self.assertNotIn("git fetch", engine)
        self.assertNotIn("git pull", engine)
        self.assertNotIn('Process.Start("explorer.exe", reportPath.Text', (ROOT / "GitleaksScannerForm.cs").read_text())

    def test_results_require_both_exit_code_and_valid_json(self):
        engine = (ROOT / "GitleaksScanEngine.cs").read_text()
        self.assertIn("result.ExitCode == 0 && findings == 0", engine)
        self.assertIn("result.ExitCode == FindingsExitCode && findings > 0", engine)
        self.assertIn("ReadFindings(report)", engine)
        self.assertIn("result.Error =", engine)
        self.assertIn("Отсутствие находок не подтверждено", engine)
        self.assertIn("result.Report = File.Exists(report)", engine)
        self.assertIn("value == null", engine)
        self.assertIn("findings == null", engine)
        self.assertIn("MaxJsonBytes", engine)
        ui = (ROOT / "GitleaksScannerForm.cs").read_text()
        for marker in ("Task.Run(", "clock.Start()", "clock.Stop()", "if (busy)",
                       "Секреты не обнаружены", "Найдено потенциальных секретов",
                       "Ошибка проверки", "Открыть папку", "FolderBrowserDialog",
                       "allRefs.Enabled = gitMode.Checked"):
            self.assertIn(marker, ui)

    def test_docs_cover_windows_acceptance_and_sensitive_reports(self):
        readme = (ROOT / "README.md").read_text(encoding="utf-8-sig")
        doc = (ROOT / "docs/GITLEAKS-SCANNER.md").read_text(encoding="utf-8")
        for marker in ("TEXT-IMPORT-GITLEAKS.txt", "Start-GitleaksScanner.cmd",
                       "«Импорт из текста»", "«Проверить»", "«Импортировать»",
                       "GitleaksReports", "Execution Policy"):
            self.assertIn(marker, readme + doc)
        for marker in ("MANUAL-PENDING", "SOURCE-VERIFIED", "--all", "git.exe",
                       "искусственных", "конфиденциальн", "tools/Test-GitleaksScannerCompile.ps1"):
            self.assertIn(marker, doc)


class SafeSyntheticGitleaksFixture(unittest.TestCase):
    def test_git_fixture_history_is_local_and_contains_only_dummy_values(self):
        git = shutil.which("git")
        if not git:
            self.skipTest("Git not installed in this test environment")
        with tempfile.TemporaryDirectory(prefix="gitleaks-safe-fixture-") as temporary:
            root = Path(temporary) / "project"
            root.mkdir()
            self.assertEqual(0, run_fixed(git, ["init", "-q", str(root)], root).returncode)
            (root / ".gitleaks.toml").write_text(CONFIG, encoding="utf-8")
            target = root / "fixture.txt"
            target.write_text("fixture = " + DUMMY + "\n", encoding="utf-8")
            self.assertEqual(0, run_fixed(git, ["add", "."], root).returncode)
            self.assertEqual(0, run_fixed(git, ["-c", "user.name=Fixture",
                "-c", "user.email=fixture@example.invalid", "commit", "-qm", "synthetic fixture"], root).returncode)
            target.write_text("ordinary text without credentials\n", encoding="utf-8")
            self.assertEqual(0, run_fixed(git, ["add", "."], root).returncode)
            self.assertEqual(0, run_fixed(git, ["-c", "user.name=Fixture",
                "-c", "user.email=fixture@example.invalid", "commit", "-qm", "remove synthetic fixture"], root).returncode)
            self.assertNotIn(DUMMY, target.read_text())
            history = run_fixed(git, ["log", "--all", "-p", "--format="], root)
            self.assertEqual(0, history.returncode)
            self.assertIn(DUMMY, history.stdout)
            # This temp repo proves why 'dir' and 'git' are separate modes.
            # No real credential and no tracked project outside temporary.
            self.assertTrue((root / ".git").exists())

    def test_optional_local_gitleaks_checks_clean_leaky_and_history_modes(self):
        local = Path(os.environ.get("LOCALAPPDATA", "")) / "Tools" / "Gitleaks" / "v8.30.1" / "gitleaks.exe"
        binary = str(local) if local.is_file() else shutil.which("gitleaks")
        if not binary or not shutil.which("git"):
            self.skipTest("Real Gitleaks/Git not available here; Windows owner smoke pending")
        with tempfile.TemporaryDirectory(prefix="gitleaks-cli-safe-") as temporary:
            root = Path(temporary) / "project"
            root.mkdir()
            git = shutil.which("git")
            self.assertEqual(0, run_fixed(git, ["init", "-q", str(root)], root).returncode)
            (root / ".gitleaks.toml").write_text(CONFIG, encoding="utf-8")
            file = root / "fixture.txt"
            file.write_text(DUMMY + "\n", encoding="utf-8")
            self.assertEqual(0, run_fixed(git, ["add", "."], root).returncode)
            self.assertEqual(0, run_fixed(git, ["-c", "user.name=Fixture",
                "-c", "user.email=fixture@example.invalid", "commit", "-qm", "fake marker"], root).returncode)
            file.write_text("clean fixture\n", encoding="utf-8")
            self.assertEqual(0, run_fixed(git, ["add", "."], root).returncode)
            self.assertEqual(0, run_fixed(git, ["-c", "user.name=Fixture",
                "-c", "user.email=fixture@example.invalid", "commit", "-qm", "clean fixture"], root).returncode)
            reports = Path(temporary) / "reports"
            reports.mkdir()
            def scan(mode, name, opts=None):
                report = reports / (name + ".json")
                args = [mode, "--redact=100", "--exit-code=3", "--no-banner",
                        "--log-level=error", "--report-format=json", "--report-path", str(report)]
                if opts: args.extend(opts)
                args.append(str(root))
                proc = run_fixed(binary, args, root)
                self.assertTrue(report.exists(), "Expected local JSON report")
                data = json.loads(report.read_text(encoding="utf-8"))
                self.assertIsInstance(data, list)
                self.assertNotIn(DUMMY, json.dumps(data), "Synthetic marker must be redacted in report")
                return proc.returncode, len(data)
            self.assertEqual((0, 0), scan("dir", "clean"))
            history_status, history_count = scan("git", "history")
            self.assertEqual(3, history_status)
            self.assertGreaterEqual(history_count, 1)
            file.write_text(DUMMY + "\n", encoding="utf-8")
            dir_status, dir_count = scan("dir", "leaky")
            self.assertEqual(3, dir_status)
            self.assertGreaterEqual(dir_count, 1)


if __name__ == "__main__":
    unittest.main()
