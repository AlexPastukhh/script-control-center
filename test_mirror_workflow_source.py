"""Offline tests for the Docker-first WinForms mirror workflow source.

No Windows Git trees, GitHub requests, deployment, or active services.
Tests permitted switches only within disposable temp Git repositories.
"""
from pathlib import Path
import importlib.util
import os
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
CSharp = ROOT / "Mirror-Workflow.UI.cs"
Launcher = ROOT / "Fortis-Windows-Mirror-Status.ps1"
Exporter = ROOT / "tools/Export-Docker-Branch-Workflow.py"


def load_exporter():
    spec = importlib.util.spec_from_file_location("fortis_branch_exporter", Exporter)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class WorkflowSource(unittest.TestCase):
    def test_launcher_routes_to_new_ui_while_preserving_cli(self):
        source = Launcher.read_text(encoding="utf-8-sig")
        self.assertIn("Mirror-Workflow.UI.cs", source)
        self.assertIn("[FortisMirror.WorkflowWindow]::Open(", source)
        self.assertIn("Mirror-Status.Report.ps1", source)
        self.assertIn("if ($Cli)", source)
        self.assertIn("Add-Type -Path $source", source)

    def test_compact_docker_windows_columns_and_full_details(self):
        source = CSharp.read_text(encoding="utf-8")
        columns = re.search(r"private static readonly string\[\] Columns = \{(.*?)\};",
                            source, re.S).group(1)
        labels = re.findall(r'"([^"]+)"', columns)
        self.assertEqual(len(labels), 13)
        self.assertEqual(labels[0], "Репозиторий")
        self.assertEqual(labels[1:6], [
            "Docker · Ветка для просмотра", "Docker · Активная ветка",
            "Docker · GitHub", "Docker · Изменения", "Docker · SHA"])
        self.assertEqual(labels[6:11], [
            "Windows · Ветка для просмотра", "Windows · Активная ветка",
            "Windows · GitHub", "Windows · Изменения", "Windows · SHA"])
        self.assertIn("for (int i = 0; i < ColumnCount; i++)", source)
        self.assertIn("b.Append(Columns[i]).Append(\": \").AppendLine(values[i]);", source)
        self.assertIn("AddFields(b, r, name, r.WindowsView)", source)
        self.assertIn("AddFields(b, r, r.DockerView, name)", source)
        self.assertLess(source.index("layout.Controls.Add(list, 0, 1)"),
                        source.index("layout.Controls.Add(bottom, 0, 2)"))

    def test_docker_first_and_independent_branch_selectors(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn('Docker("--stdout", 90000, token)', source)
        self.assertIn('var snap = Docker("--stdout", 90000, token)', source)
        self.assertNotIn("Directory.GetDirectories(root)", source)
        self.assertIn("if (isDocker) r.DockerView = target;", source)
        self.assertIn("else r.WindowsView = target;", source)
        self.assertIn("else BeginSwitch(r, isDocker, target);", source)
        self.assertIn("WfBranch d = r.Get(r.DockerActive), w = r.Get(r.WindowsActive);", source)
        self.assertIn("string[] overall = Overall(r);", source)
        self.assertIn("Sha.IsMatch(b.GithubSha)", source)
        self.assertIn("r.WindowsView != r.WindowsActive", source)

    def test_git_reads_and_writes_have_explicit_guards(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn("GIT_TERMINAL_PROMPT", source)
        self.assertIn("GCM_INTERACTIVE", source)
        self.assertIn("http.sslVerify=true", source)
        self.assertIn("http.sslBackend=openssl", source)
        self.assertIn("AllowedOrigin", source)
        self.assertIn("CanonicalOrigin(origin.Text) != CanonicalOrigin(r.DockerOrigin)", source)
        self.assertIn("MessageBoxButtons.YesNo", source)
        self.assertIn("MessageBoxDefaultButton.Button2", source)
        self.assertIn("fetch --no-tags --no-recurse-submodules origin ", source)
        self.assertIn("rev-parse --verify FETCH_HEAD", source)
        self.assertIn("fetchedHead.Text != remote", source)
        self.assertIn("merge --ff-only FETCH_HEAD", source)
        self.assertIn("switch --no-guess", source)
        self.assertIn("Protected.Contains(r.Name)", source)
        self.assertIn("!r.WindowsOriginValid", source)
        self.assertIn("r.WindowsChanges != 0", source)
        self.assertIn("target.WindowsAhead != 0 || target.WindowsBehind <= 0", source)
        self.assertIn("fresh.WindowsHead != oldSha", source)
        self.assertIn("fresh.Get(branch).GithubSha != remote", source)
        self.assertNotIn("git push", source.lower())
        self.assertNotIn("add -a", source.lower())
        self.assertNotIn("reset --hard", source.lower())

    def test_accurate_unknown_git_history_and_errors(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn("SHA ≠ GH · НАПРАВЛЕНИЕ НЕИЗВЕСТНО", source)
        self.assertIn("WindowsAhead > 0", source)
        self.assertIn("DockerAhead > 0", source)
        self.assertIn("WindowsBehind > 0", source)
        self.assertIn("АКТУАЛЕН · НЕ MAIN", source)
        self.assertIn("НЕТ КЛОНА WINDOWS", source)
        self.assertIn("WINDOWS НЕОЖИДАННО ВПЕРЕДИ", source)
        self.assertIn("РАЗНЫ", source) if False else None
        self.assertIn("АКТИВНЫЕ ВЕТКИ РАЗНЫЕ", source)

    def test_exporter_clean_and_comparison(self):
        module = load_exporter()
        with tempfile.TemporaryDirectory() as td:
            base = Path(td)
            repos = base / "Repos"
            repo = repos / "testrepo"
            repo.mkdir(parents=True)

            def git(*args):
                res = subprocess.run(["git", "-C", str(repo), *args],
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     text=True, timeout=10, check=True)
                return res.stdout.strip()

            git("init", "-q")
            git("config", "user.name", "Unit Test")
            git("config", "user.email", "test@example.invalid")
            git("branch", "-m", "main")
            (repo / "README.md").write_text("baseline")
            git("add", "README.md")
            git("commit", "-qm", "initial")
            git("remote", "add", "origin",
                "https://github.com/AlexPastukhh/testrepo.git")
            head = git("rev-parse", "HEAD")
            git("switch", "-qc", "work/test")
            (repo / "test.txt").write_text("test")
            git("add", "test.txt")
            git("commit", "-qm", "branch")
            work = git("rev-parse", "HEAD")

            snapshot = module.snapshot(repos, None)
            self.assertIn("R\ttestrepo\twork/test\t" + work + "\t0", snapshot)
            self.assertIn("B\ttestrepo\tmain\t" + head, snapshot)
            self.assertIn("B\ttestrepo\twork/test\t" + work, snapshot)
            self.assertEqual(module.compare_branches(repos, [
                ("testrepo", "work/test", work)
            ])[0], "C\ttestrepo\twork/test\t0\t0")
            self.assertEqual(module.compare_branches(repos, [
                ("testrepo", "work/test", head)
            ])[0], "C\ttestrepo\twork/test\t1\t0")
            self.assertEqual(work, git("rev-parse", "HEAD"),
                             "snapshot/compare must not switch or write")
            self.assertEqual("", git("status", "--porcelain=v1"))

            with self.assertRaisesRegex(ValueError, "preflight"):
                module.guarded_switch(repos, "testrepo", "main", "main", work)
            self.assertEqual(work, git("rev-parse", "HEAD"))
            (repo / "temp.txt").write_text("untracked")
            with self.assertRaisesRegex(ValueError, "preflight"):
                module.guarded_switch(repos, "testrepo", "main", "work/test", work)
            (repo / "temp.txt").unlink()
            self.assertEqual("SWITCHED\ttestrepo\tmain",
                             module.guarded_switch(repos, "testrepo",
                                                   "main", "work/test", work))
            self.assertEqual("main", git("branch", "--show-current"))
            self.assertEqual(head, git("rev-parse", "HEAD"))
            with self.assertRaisesRegex(ValueError, "protected"):
                module.guarded_switch(repos, "tunnel", "main", "work/test", work)
            with self.assertRaises(ValueError):
                module.guarded_switch(repos, "testrepo", "../outside", "main", head)
            self.assertEqual("main", git("branch", "--show-current"))


if __name__ == "__main__":
    unittest.main()
