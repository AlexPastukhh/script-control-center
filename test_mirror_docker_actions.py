"""Disposable Git-repository tests for Docker-only Stage, Commit and Push.

Never invokes a write on a real Docker repo, GitHub, Windows or a live service.
The only 'remote' is a bare repository created inside TemporaryDirectory.
"""
import base64
import importlib.util
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parent / "tools/Docker-Mirror-Git-Actions.py"


def load_actions():
    spec = importlib.util.spec_from_file_location("docker_git_actions_under_test", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class DockerGitActionsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "Repos"
        self.root.mkdir()
        self.repo = self.root / "fixture"
        self.repo.mkdir()
        self.remote = Path(self.tmp.name) / "github_fixture.git"
        self.actions = load_actions()
        self.git("init", "-q")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "Fixture")
        self.git("branch", "-m", "main")
        (self.repo / "README.md").write_text("initial")
        (self.repo / "deleted.txt").write_text("will be removed")
        self.git("add", "README.md", "deleted.txt")
        self.git("commit", "-qm", "initial")
        self.initial_head = self.git("rev-parse", "HEAD")
        subprocess.run(["git", "init", "--bare", "-q", str(self.remote)],
                       check=True, timeout=10)
        self.git("remote", "add", "origin", self.remote.as_uri())
        self.git("push", "-q", "origin", "main")
        # Allow only this temporary local bare origin within this test instance.
        self.actions.mirror.ORIGIN = re.compile(
            re.escape(self.remote.as_uri()) + r"\Z")
        # Branch operations use exact_repo; this tests the real action code.
        self.assertEqual(self.initial_head,
                         self.git("ls-remote", "origin", "refs/heads/main").split()[0])

    def git(self, *args):
        res = subprocess.run(["git", "-C", str(self.repo), *args],
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             check=True, text=True, timeout=20)
        return res.stdout.strip()

    def test_staging_selected_only_then_commit_and_normal_push(self):
        (self.repo / "README.md").write_text("modified but NOT selected")
        (self.repo / "new file.txt").write_text("selected")
        (self.repo / "deleted.txt").unlink()
        token, entries = self.actions.status_entries(self.repo)
        self.assertEqual(len(entries), 3)
        self.assertTrue(all(x[0][:2] for x in entries))
        index = next(i for i, value in enumerate(entries)
                     if "new file.txt" in value[0])
        preview = self.actions.stage_preview(self.root, "fixture")
        self.assertIn("FORTIS_DOCKER_STAGE_V1\tmain\t" + self.initial_head,
                      preview)
        self.assertIn(token, preview)
        result = self.actions.stage(self.root, "fixture", "main",
                                    self.initial_head, token, [index])
        self.assertIn("STAGED\tfixture\tmain\t1", result)
        self.assertEqual("new file.txt", self.git("diff", "--cached", "--name-only"))
        self.assertEqual(self.initial_head, self.git("rev-parse", "HEAD"))
        self.assertTrue((self.repo / "README.md").read_text() == "modified but NOT selected")
        with self.assertRaisesRegex(ValueError, "differ"):
            self.actions.stage(self.root, "fixture", "main",
                               self.initial_head, token, [index])

        digest, staged_files = self.actions.staged_manifest(self.repo)
        self.assertEqual(len(staged_files), 1)
        self.assertIn("new file.txt", staged_files[0])
        commit_preview = self.actions.commit_preview(self.root, "fixture")
        self.assertIn("FORTIS_DOCKER_COMMIT_V1\tmain", commit_preview)
        self.assertIn(digest, commit_preview)
        message = base64.b64encode("Add selected new file".encode()).decode()
        result = self.actions.commit(self.root, "fixture", "main",
                                     self.initial_head, digest, message)
        self.assertTrue(result.startswith("COMMITTED\tfixture\tmain\t"))
        committed = self.git("rev-parse", "HEAD")
        self.assertNotEqual(self.initial_head, committed)
        self.assertIn("README.md", self.git("status", "--porcelain=v1"))
        self.assertIn("deleted.txt", self.git("status", "--porcelain=v1"))
        self.assertEqual("", self.git("diff", "--cached", "--name-only"))
        self.assertEqual(self.initial_head, self.actions.remote_sha(self.repo, "main"))
        with self.assertRaisesRegex(ValueError, "branch or HEAD changed"):
            self.actions.push(self.root, "fixture", "main", self.initial_head,
                              self.initial_head)
        push_view = self.actions.push_preview(self.root, "fixture")
        self.assertIn("\t" + committed + "\t" + self.initial_head, push_view)
        pushed = self.actions.push(self.root, "fixture", "main",
                                   committed, self.initial_head)
        self.assertEqual("PUSHED\tfixture\tmain\t" + committed, pushed)
        self.assertEqual(committed, self.actions.remote_sha(self.repo, "main"))
        # Pushing an existing branch never implicitly stages other changes.
        self.assertIn("README.md", self.git("status", "--porcelain=v1"))

    def test_preview_digest_rejects_unreviewed_new_index(self):
        (self.repo / "README.md").write_text("stage me")
        token, entries = self.actions.status_entries(self.repo)
        self.actions.stage(self.root, "fixture", "main",
                           self.initial_head, token, [0])
        digest, files = self.actions.staged_manifest(self.repo)
        self.assertEqual(len(files), 1)
        self.git("reset", "-q", "HEAD", "--", "README.md")
        with self.assertRaisesRegex(ValueError, "staged files changed"):
            self.actions.commit(self.root, "fixture", "main", self.initial_head,
                                digest, base64.b64encode(b"Do not commit").decode())
        self.assertEqual(self.initial_head, self.git("rev-parse", "HEAD"))
        with self.assertRaisesRegex(ValueError, "invalid commit message"):
            self.actions.commit(self.root, "fixture", "main", self.initial_head,
                                digest, "not@base64")

    def test_rename_and_special_name_are_selected_by_exact_git_status(self):
        self.git("mv", "deleted.txt", "new renamed file.txt")
        token, entries = self.actions.status_entries(self.repo)
        self.assertEqual(len(entries), 1, entries)
        self.assertEqual(len(entries[0][1]), 2, entries)
        self.actions.stage(self.root, "fixture", "main", self.initial_head, token, [0])
        self.assertIn("R", self.git("diff", "--cached", "--name-status"))
        with self.assertRaisesRegex(ValueError, "invalid selected-file"):
            self.actions.stage(self.root, "fixture", "main",
                               self.initial_head, token, [0, 0])
        for bad in ("../outside", ".git/config", "/etc/passwd"):
            with self.assertRaisesRegex(ValueError, "unsafe"):
                self.actions.safe_relative(bad)

    def test_literal_pathspec_never_stages_wildcard_matches(self):
        (self.repo / "a[1].txt").write_text("literal")
        (self.repo / "a1.txt").write_text("should remain untracked")
        token, files = self.actions.status_entries(self.repo)
        idx = next(i for i, item in enumerate(files) if "a[1].txt" in item[0])
        self.actions.stage(self.root, "fixture", "main",
                           self.initial_head, token, [idx])
        staged = self.git("diff", "--cached", "--name-only")
        self.assertEqual(staged, "a[1].txt")
        self.assertIn("?? a1.txt", self.git("status", "--porcelain=v1"))

    def test_github_origin_must_match_docker_folder(self):
        self.git("remote", "set-url", "origin",
                 "https://github.com/AlexPastukhh/unrelated.git")
        self.actions.mirror.ORIGIN = re.compile(
            r"https://github\.com/AlexPastukhh/[A-Za-z0-9_-]+\.git\Z")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.actions.exact_repo(self.root, "fixture")

    def test_protected_names_and_stale_push(self):
        with self.assertRaisesRegex(ValueError, "protected"):
            self.actions.exact_repo(self.root, "tunnel")
        with self.assertRaisesRegex(ValueError, "moved since preview"):
            self.actions.push(self.root, "fixture", "main", self.initial_head,
                              "1" * 40)
        self.assertEqual(self.initial_head, self.actions.remote_sha(self.repo, "main"))


if __name__ == "__main__":
    unittest.main()
