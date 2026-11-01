"""Offline proof for Git behind/ahead/diverged counting on temporary repositories.

Does not touch live checkouts, require credentials, or invoke Windows programs.
"""
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


class MirrorGitHistory(unittest.TestCase):
    def git(self, *args, cwd=None, check=True):
        proc = subprocess.run(
            ["git", *args], cwd=cwd,
            env=self.env, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=12,
        )
        if check and proc.returncode:
            self.fail("git command failed: {}: {}".format(args, proc.stderr[:250]))
        return proc

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="mirror-history-")
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.env = dict(os.environ)
        self.env["GIT_CONFIG_NOSYSTEM"] = "1"
        self.env["GIT_CONFIG_GLOBAL"] = os.devnull
        self.env["GIT_TERMINAL_PROMPT"] = "0"
        self.remote = root / "remote.git"
        self.author = root / "docker"
        self.clone = root / "windows"
        self.git("init", "--bare", str(self.remote))
        self.git("clone", str(self.remote), str(self.author))
        self.git("config", "user.name", "Offline Test", cwd=self.author)
        self.git("config", "user.email", "test@example.invalid", cwd=self.author)
        self.git("switch", "-c", "main", cwd=self.author)
        self.commit(self.author, "initial", "initial")
        self.git("push", "--set-upstream", "origin", "main", cwd=self.author)
        self.git("clone", "--branch", "main", str(self.remote), str(self.clone))
        self.git("config", "user.name", "Offline Test", cwd=self.clone)
        self.git("config", "user.email", "test@example.invalid", cwd=self.clone)

    def commit(self, checkout, name, content):
        (checkout / (name + ".txt")).write_text(content)
        self.git("add", name + ".txt", cwd=checkout)
        self.git("commit", "-m", name, cwd=checkout)

    def count(self):
        head = self.git("rev-parse", "refs/heads/main", cwd=self.clone).stdout.strip()
        github = self.git(
            "ls-remote", "--exit-code", "origin", "refs/heads/main",
            cwd=self.clone,
        ).stdout.split()[0]
        self.git("fetch", "--no-tags", "--no-recurse-submodules",
                 "origin", "main", cwd=self.clone)
        # Identical command and operand order as Mirror-Status.UI.cs.
        result = self.git("rev-list", "--left-right", "--count",
                          head + "..." + github, cwd=self.clone).stdout.split()
        return tuple(map(int, result))

    def test_behind_two_then_pull(self):
        self.commit(self.author, "a", "a")
        self.commit(self.author, "b", "b")
        self.git("push", "origin", "main", cwd=self.author)
        before = self.git("rev-parse", "HEAD", cwd=self.clone).stdout.strip()
        self.assertEqual((0, 2), self.count())
        self.assertEqual(before, self.git("rev-parse", "HEAD", cwd=self.clone).stdout.strip(),
                         "Fetch must not change the checked-out Windows main")
        self.git("pull", "--ff-only", "--no-rebase", "--no-recurse-submodules",
                 "origin", "main", cwd=self.clone)
        self.assertEqual((0, 0), self.count())

    def test_exceptional_local_ahead(self):
        self.commit(self.clone, "local", "local")
        self.assertEqual((1, 0), self.count())

    def test_divergent_history(self):
        self.commit(self.clone, "local", "local")
        self.commit(self.author, "remote", "remote")
        self.git("push", "origin", "main", cwd=self.author)
        self.assertEqual((1, 1), self.count())
        before = self.git("rev-parse", "HEAD", cwd=self.clone).stdout.strip()
        refused = self.git("pull", "--ff-only", "--no-rebase",
                           "origin", "main", cwd=self.clone, check=False)
        self.assertNotEqual(refused.returncode, 0)
        self.assertEqual(before, self.git("rev-parse", "HEAD", cwd=self.clone).stdout.strip())


if __name__ == "__main__":
    unittest.main()
