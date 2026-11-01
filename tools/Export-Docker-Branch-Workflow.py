#!/usr/bin/env python3
"""Fortis Docker-first Git branch snapshot for the Windows workflow inspector.

Read-only --stdout is the default. --switch requires explicit owner action
through the Windows UI and strictly checks current branch, SHA and cleanliness.
No network, fetch, push, checkout on read, Docker socket or credentials.
"""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path("/workspace/repo/Repos")
NAME = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,119}\Z")
BRANCH = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_./-]{0,159}\Z")
SHA = re.compile(r"[0-9a-f]{40}\Z")
ORIGIN = re.compile(
    r"(?:https://github\.com/|git@github\.com:)AlexPastukhh/[A-Za-z0-9_.-]+(?:\.git)?\Z",
    re.I,
)
# Shared services and core infrastructure must not be switched from a GUI.
PROTECTED = frozenset(("tunnel", "rd-graphs", "script-control-center", "security"))


def valid_branch(s):
    return bool(BRANCH.fullmatch(s) and not s.startswith("-") and
                not s.endswith("/") and ".." not in s and
                "//" not in s and "@{" not in s and
                not s.endswith(".lock") and "\\" not in s)


def safe_repo(root, name):
    if not NAME.fullmatch(name) or name in (".", ".."):
        return None
    path = root / name
    if path.is_symlink() or not path.is_dir():
        return None
    # Reject .git indirection and symlinked .git for branch switching.
    dotgit = path / ".git"
    if not dotgit.is_dir() or dotgit.is_symlink():
        return None
    return path


def git(repo, *args):
    env = dict(os.environ, GIT_OPTIONAL_LOCKS="0", GIT_TERMINAL_PROMPT="0",
               GCM_INTERACTIVE="never", GIT_CONFIG_NOSYSTEM="1")
    try:
        p = subprocess.run(
            ["git", "-c", "core.fsmonitor=false", "-C", str(repo), *args],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20, env=env,
            check=False,
        )
        return p.stdout.decode("utf-8", errors="replace").strip() if p.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def snapshot_one(repo):
    origin = git(repo, "remote", "get-url", "origin")
    if not origin or not ORIGIN.fullmatch(origin):
        origin = "-"
    head = git(repo, "rev-parse", "--verify", "HEAD")
    active = git(repo, "symbolic-ref", "--quiet", "--short", "HEAD")
    if not head or not SHA.fullmatch(head.lower()):
        return None
    if not active or not valid_branch(active):
        active = "-"
    changes = git(repo, "status", "--porcelain=v1", "--untracked-files=all")
    changed = -1 if changes is None else len(changes.splitlines()) if changes else 0
    rows = ["R\t{}\t{}\t{}\t{}\t{}".format(repo.name, active, head.lower(), changed, origin)]
    listing = git(repo, "for-each-ref", "--format=%(refname:short)%09%(objectname)", "refs/heads")
    if listing is None:
        return rows
    for line in listing.splitlines():
        parts = line.split("\t")
        if len(parts) != 2 or not valid_branch(parts[0]) or not SHA.fullmatch(parts[1].lower()):
            continue
        rows.append("B\t{}\t{}\t{}".format(repo.name, parts[0], parts[1].lower()))
    return rows


def snapshot(root, names):
    if not root.is_dir() or root.is_symlink():
        raise ValueError("invalid Docker repository root")
    candidates = (sorted(set(names), key=str.lower) if names is not None
                  else sorted((p.name for p in root.iterdir()), key=str.lower))
    lines = ["FORTIS_DOCKER_BRANCH_V2\t" +
             datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")]
    for name in candidates:
        path = safe_repo(root, name)
        if path is None:
            continue
        item = snapshot_one(path)
        if item:
            lines.extend(item)
    return "\n".join(lines) + "\n"


def guarded_switch(root, name, target, expected_branch, expected_head):
    """Owner-reviewed switch only, no implicit creation, no remote checkout."""
    if name in PROTECTED:
        raise ValueError("protected infrastructure repository")
    repo = safe_repo(root, name)
    if repo is None or not valid_branch(target) or not valid_branch(expected_branch):
        raise ValueError("invalid Git repository or branch")
    if not SHA.fullmatch(expected_head):
        raise ValueError("invalid expected SHA")
    origin = git(repo, "remote", "get-url", "origin")
    if not origin or not ORIGIN.fullmatch(origin):
        raise ValueError("origin is not the approved GitHub namespace")
    current_branch = git(repo, "symbolic-ref", "--quiet", "--short", "HEAD")
    current_sha = git(repo, "rev-parse", "--verify", "HEAD")
    current_dirty = git(repo, "status", "--porcelain=v1", "--untracked-files=all")
    target_sha = git(repo, "rev-parse", "--verify", "refs/heads/" + target)
    if (current_branch != expected_branch or current_sha != expected_head or
            current_dirty is None or current_dirty != "" or
            not target_sha or not SHA.fullmatch(target_sha.lower())):
        raise ValueError("preflight failed: branch/SHA/dirty/target mismatch")
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="never",
               GIT_OPTIONAL_LOCKS="1", GIT_CONFIG_NOSYSTEM="1")
    try:
        p = subprocess.run([
            "git", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null",
            "-c", "submodule.recurse=false", "-C", str(repo),
            "switch", "--no-guess", target
        ], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=60, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise ValueError("switch could not complete; inspect repository manually")
    if p.returncode != 0:
        raise ValueError("Git refused branch switch; inspect repository manually")
    if git(repo, "symbolic-ref", "--quiet", "--short", "HEAD") != target:
        raise ValueError("postcondition: unexpected branch")
    return "SWITCHED\t" + name + "\t" + target


def compare_branches(root, requests):
    """Compare Docker-local refs against exact GitHub-advertised SHA; never fetch.

    C records contain -1/-1 if GitHub objects are unavailable in Docker.
    A cached origin/* ref is never treated as authoritative for GitHub.
    """
    lines = []
    for name, branch, remote_sha in requests:
        if not NAME.fullmatch(name) or not valid_branch(branch) or not SHA.fullmatch(remote_sha):
            raise ValueError("invalid compare request")
        repo = safe_repo(root, name)
        if repo is None:
            continue
        local_sha = git(repo, "rev-parse", "--verify", "refs/heads/" + branch)
        if local_sha is None or not SHA.fullmatch(local_sha.lower()):
            continue
        ahead = behind = -1
        if local_sha == remote_sha:
            ahead = behind = 0
        else:
            counts = git(repo, "rev-list", "--left-right", "--count",
                         local_sha + "..." + remote_sha)
            if counts:
                parts = counts.split()
                if (len(parts) == 2 and all(p.isdigit() for p in parts)):
                    ahead, behind = map(int, parts)
        lines.append("C\t{}\t{}\t{}\t{}".format(name, branch, ahead, behind))
    return lines


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--stdout", action="store_true")
    parser.add_argument("--repo", nargs="+", default=None)
    parser.add_argument("--compare", action="append", nargs=3, metavar=("REPO", "BRANCH", "GH_SHA"))
    parser.add_argument("--switch-repo")
    parser.add_argument("--switch-branch")
    parser.add_argument("--expect-branch")
    parser.add_argument("--expect-head")
    args = parser.parse_args()
    if args.repo and any(not NAME.fullmatch(n) for n in args.repo):
        parser.error("invalid repository name")
    switch_values = (args.switch_repo, args.switch_branch, args.expect_branch, args.expect_head)
    if any(v is not None for v in switch_values):
        if not all(v is not None for v in switch_values) or args.repo:
            parser.error("switch requires all four explicit arguments and no --repo")
        try:
            print(guarded_switch(args.root, *switch_values))
        except ValueError as e:
            parser.exit(2, "STOP: " + str(e) + "\n")
        return
    if not args.stdout:
        parser.error("snapshot output requires --stdout")
    try:
        output = snapshot(args.root, args.repo)
        if args.compare:
            output += "".join(x + "\n" for x in compare_branches(args.root, args.compare))
        sys.stdout.write(output)
    except ValueError as e:
        parser.exit(2, "STOP: " + str(e) + "\n")


if __name__ == "__main__":
    main()
