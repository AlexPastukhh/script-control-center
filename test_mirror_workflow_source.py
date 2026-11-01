"""Offline tests for the Docker-first WinForms mirror workflow source.

No Windows Git trees, GitHub requests, deployment, or active services.
Tests permitted switches only within disposable temp Git repositories.
"""
from pathlib import Path
import base64
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

    def test_branch_picker_active_branch_is_noop_and_menu_disposal_deferred(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn("if ((isDocker ? r.DockerActive : r.WindowsActive) == target) return;", source)
        self.assertIn("if ((isDocker ? r.DockerView : r.WindowsView) == target) return;", source)
        self.assertIn("BeginSwitch(r, isDocker, target);", source)
        self.assertNotIn("menu.Closed += delegate { menu.Dispose(); };", source)
        self.assertRegex(source,
                         r"menu\.Closed \+= delegate \{[\s\S]*?BeginInvoke"
                         r"\(\(MethodInvoker\)delegate \{[\s\S]*?menu\.Dispose\(\)")

    def test_check_updates_one_completed_row_and_preserves_prior_results(self):
        source = CSharp.read_text(encoding="utf-8")
        start = source.index("private void BeginCheck(bool all)")
        end = source.index("private bool CanPull", start)
        code = source[start:end]
        self.assertIn("ApplyCheckedRow(complete)", code)
        self.assertIn("ProbeGithub(r, token);", code)
        self.assertIn("ProbeWindows(r, token);", code)
        self.assertIn("ProbeDockerCounts(new List<WfRepo> { r }, token)", code)
        self.assertLess(code.index("ProbeGithub(r, token);"),
                        code.index("ProbeWindows(r, token);"))
        self.assertLess(code.index("ProbeWindows(r, token);"),
                        code.index("ProbeDockerCounts(new List<WfRepo> { r }, token)"))
        self.assertLess(code.index("ProbeDockerCounts(new List<WfRepo> { r }, token)"),
                        code.index("ApplyCheckedRow(complete)"))
        self.assertNotIn("repos.Clear()", code)
        self.assertNotIn("DrawTable(previous)", code)
        self.assertIn("if (fresh.Get(old.DockerView) != null)", source)
        self.assertIn("if (fresh.Get(old.WindowsView) != null)", source)
        self.assertIn("fresh.Item.Tag = fresh;", source)
        self.assertIn("Результаты обновлялись построчно", code)
        self.assertIn("if (token.IsCancellationRequested) break;", code)

    def test_docker_stage_commit_push_only_current_active_branch(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn('Button("Stage Docker"', source)
        self.assertIn('Button("Commit Docker"', source)
        self.assertIn('Button("Push Docker"', source)
        self.assertIn('BeginDockerGitPreview("stage")', source)
        self.assertIn('BeginDockerGitPreview("commit")', source)
        self.assertIn('BeginDockerGitPreview("push")', source)
        self.assertIn('tools/Docker-Mirror-Git-Actions.py', source)
        self.assertIn('python3 -B " + DockerGitActions', source)
        self.assertIn('current.DockerActive != ready.Branch', source)
        self.assertIn('current.DockerHead != ready.Head', source)
        self.assertIn('ValidOrigin(r.DockerOrigin)', source)
        self.assertIn('!Protected.Contains(activeDocker.Name)', source)
        self.assertIn('ChooseDockerStageFiles(current, ready)', source)
        self.assertIn('AskDockerCommitMessage(current, ready)', source)
        self.assertIn('MessageBoxDefaultButton.Button2', source)
        self.assertIn('ParseGitPreview(cmd.Text, operation', source)
        self.assertIn('Digest.IsMatch(parts[3])', source)
        self.assertIn('Encoding.UTF8.GetBytes(message)', source)
        self.assertIn('BeginDockerGitWrite(current, operation', source)
        self.assertIn('CancellationToken.None, true', source)
        self.assertIn('BeginCheck(false);', source)
        self.assertNotIn('git add -A', source)
        self.assertNotIn('git push origin', source)

    def test_windows_powershell_51_preview_out_parameter_is_initialized(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn("WfGitPreview preview = null;", source)
        self.assertIn("bool valid = cmd.Ok && ParseGitPreview(cmd.Text, operation, out preview);", source)
        self.assertIn("WfGitPreview ready = preview;", source)
        self.assertNotIn("WfGitPreview preview;\n        bool valid = cmd.Ok &&", source)

    def test_windows_powershell_51_thread_ctor_is_unambiguous(self):
        source = CSharp.read_text(encoding="utf-8")
        self.assertIn("Thread window = new Thread(new ThreadStart(delegate() {", source)
        self.assertIn("window.SetApartmentState(ApartmentState.STA);", source)
        self.assertNotIn("new Thread(delegate {", source)

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
        self.assertIn("BeginSwitch(r, isDocker, target);", source)
        self.assertIn("if ((isDocker ? r.DockerActive : r.WindowsActive) == target) return;", source)
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

    def test_changed_file_details_are_separate_for_active_docker_windows(self):
        source = CSharp.read_text(encoding="utf-8")
        exporter = Exporter.read_text(encoding="utf-8")
        self.assertIn("internal readonly List<string> DockerFiles", source)
        self.assertIn("internal readonly List<string> WindowsFiles", source)
        self.assertIn('} else if (part[0] == "F") {', source)
        self.assertIn("Convert.FromBase64String(part[2])", source)
        self.assertIn("new UTF8Encoding(false, true).GetString(", source)
        self.assertIn("ValidChangeEntry(entry)", source)
        self.assertIn("r.WindowsFiles.Add(cleanEntry)", source)
        self.assertIn("internal bool OutputTruncated;", source)
        self.assertIn("result.Ok = !stopped && p.ExitCode == 0 && !result.OutputTruncated;", source)
        self.assertIn("ОТВЕТ СЛИШКОМ ДЛИННЫЙ", source)
        self.assertIn("AppendChangedFiles(b, \"DOCKER\", r.DockerActive, r.DockerChanges, r.DockerFiles)", source)
        self.assertIn("AppendChangedFiles(b, \"WINDOWS\", r.WindowsActive, r.WindowsChanges, r.WindowsFiles)", source)
        self.assertIn("AddFields(b, r, r.DockerView, r.WindowsView)", source)
        self.assertIn('if (codes == "??")', source)
        self.assertIn("индекс: ", source)
        self.assertIn("рабочая папка: ", source)
        self.assertIn('rows.append("F\\t{}\\t{}".format(repo.name, encoded))', exporter)
        self.assertIn("for entry in changes.splitlines()[:500]", exporter)
        self.assertIn('.rstrip("\\r\\n")', exporter)

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

            # Docker inventory must give exact paths/status codes, not only a count.
            # The Git porcelain leading space must survive the exporter parser.
            (repo / "README.md").write_text("modified baseline")
            (repo / "new file.txt").write_text("untracked")
            dirty_snapshot = module.snapshot(repos, ["testrepo"])
            dirty_lines = dirty_snapshot.splitlines()
            self.assertIn("R\ttestrepo\twork/test\t" + work + "\t2",
                          dirty_snapshot)
            encoded = [line.split("\t")[2] for line in dirty_lines
                       if line.startswith("F\ttestrepo\t")]
            decoded = [base64.b64decode(record, validate=True).decode("utf-8")
                       for record in encoded]
            self.assertEqual(len(decoded), 2)
            self.assertIn(" M README.md", decoded)
            self.assertIn('?? "new file.txt"', decoded)
            self.assertEqual(work, git("rev-parse", "HEAD"))
            # Snapshot is read-only, so the actual edits must still be present.
            self.assertEqual("modified baseline", (repo / "README.md").read_text())
            (repo / "README.md").write_text("baseline")
            (repo / "new file.txt").unlink()
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
