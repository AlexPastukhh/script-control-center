#!/usr/bin/env python3
"""Read-only Docker Git status export for Windows mirror checks.

By default the report is saved outside Git repos under Workfiles.
--stdout outputs TSV to stdout and does not write anything to the filesystem.
--repo selects specific repository directory names to avoid scanning others.
"""
import argparse
import os
from datetime import datetime, timezone
from pathlib import Path
import re
import subprocess
import sys

DEFAULT_ROOT = Path("/workspace/repo/Repos")
DEFAULT_OUTPUT = Path("/workspace/repo/Workfiles/windows-docker-audit/docker-status.tsv")
VALID_NAME = re.compile(r"[A-Za-z0-9_.-]{1,120}\Z")
VALID_SHA = re.compile(r"[0-9a-f]{40}\Z")


def git(repo, *args):
    try:
        proc = subprocess.run(
            ["git", "-c", "core.fsmonitor=false", "-C", str(repo), *args],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=20,
            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"},
        )
        return proc.stdout if proc.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def collect(root, names=None):
    results = []
    if names:
        if any(not VALID_NAME.fullmatch(name) or name in (".", "..") for name in names):
            raise ValueError("Invalid repository name")
        candidates = [root / name for name in dict.fromkeys(names)]
    else:
        candidates = sorted(root.iterdir(), key=lambda p: p.name.lower())
    for repo in candidates:
        if not repo.is_dir() or repo.is_symlink() or not VALID_NAME.fullmatch(repo.name):
            continue
        if not ((repo / ".git").is_dir() or (repo / ".git").is_file()):
            continue
        head = git(repo, "rev-parse", "--verify", "HEAD")
        if head is None:
            continue
        head = head.decode("ascii", errors="replace").strip().lower()
        if not VALID_SHA.fullmatch(head):
            continue
        status = git(repo, "status", "--porcelain=v1", "--untracked-files=all")
        count = -1 if status is None else len([x for x in status.splitlines() if x])
        results.append((repo.name, head, count))
    return results


def as_tsv(data, timestamp):
    stamp = timestamp.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    return "FORTIS_DOCKER_STATUS_V1\t" + stamp + "\n" + "".join(
        f"{name}\t{head}\t{count}\n" for name, head, count in data
    )


def export(root, output, timestamp):
    data = collect(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(as_tsv(data, timestamp), encoding="utf-8")
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--stdout", action="store_true", help="Write TSV to stdout only")
    parser.add_argument("--repo", nargs="+", metavar="NAME",
                        help="Include only these Docker repository folders")
    options = parser.parse_args()
    if not options.root.is_dir():
        parser.error("Docker repository root is missing")
    if options.repo and any(not VALID_NAME.fullmatch(n) or n in (".", "..")
                            for n in options.repo):
        parser.error("Invalid repository name")
    if options.stdout:
        # stdout is only machine-readable TSV; diagnostics go to stderr.
        rows = collect(options.root, options.repo)
        sys.stdout.write(as_tsv(rows, datetime.now(timezone.utc)))
        return
    rows = export(options.root, options.output, datetime.now(timezone.utc))
    print("Saved:", options.output)
    for name, head, changed in rows:
        print(f"{name}: {head[:8]} worktree changes={changed}")
    if not rows:
        sys.exit("No Docker Git repositories found.")


if __name__ == "__main__":
    main()
