#!/usr/bin/env python3
"""Owner-confirmed Docker Git actions for Fortis Windows Mirror Workflow.

All mutations occur exclusively inside an allowlisted Docker Git checkout.
No shell, no implicit checkout, reset, force push, Windows write, or mass stage.
Stage is by identity of individual current status entries, guarded by digest;
Commit is only for the reviewed staged index; Push is regular non-force only.
"""
import argparse
import base64
import hashlib
import importlib.util
import os
from pathlib import Path
import re
import subprocess
import sys

EXPORTER = Path(__file__).with_name("Export-Docker-Branch-Workflow.py")
spec = importlib.util.spec_from_file_location("fortis_workflow_exporter", EXPORTER)
mirror = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mirror)

ROOT = mirror.ROOT
SHA = mirror.SHA
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
NO_SHA = "0" * 40
PROTECTED = mirror.PROTECTED


def git_result(repo, args, *, write=False, timeout=45):
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="never",
               GIT_CONFIG_NOSYSTEM="1", GIT_OPTIONAL_LOCKS="1" if write else "0",
               GIT_SSH_COMMAND="ssh -o BatchMode=yes -o ConnectTimeout=10")
    full = ["git", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null",
            "-c", "http.sslVerify=true", "-c", "submodule.recurse=false",
            "-C", str(repo), *args]
    try:
        result = subprocess.run(full, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                env=env, timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise ValueError("Git command unavailable or timed out; inspect the repository")
    if result.returncode:
        raise ValueError("Git refused operation; inspect Docker repository manually")
    if len(result.stdout) > 2_000_000:
        raise ValueError("Git output exceeds safety limit")
    return result.stdout


def exact_repo(root, name):
    if name in PROTECTED:
        raise ValueError("protected infrastructure repository; use direct owner workflow")
    repo = mirror.safe_repo(root, name)
    if repo is None:
        raise ValueError("Docker repository is missing or unsafe")
    origin = mirror.git(repo, "remote", "get-url", "origin")
    if not origin or not mirror.ORIGIN.fullmatch(origin):
        raise ValueError("origin is not the approved GitHub namespace")
    if origin.startswith(("https://github.com/", "git@github.com:")):
        repo_remote = origin.split("AlexPastukhh/", 1)[-1]
        if repo_remote.endswith(".git"):
            repo_remote = repo_remote[:-4]
        if repo_remote.lower() != name.lower():
            raise ValueError("origin repository name does not match selected Docker folder")
    return repo


def identity(repo):
    branch = mirror.git(repo, "symbolic-ref", "--quiet", "--short", "HEAD")
    head = mirror.git(repo, "rev-parse", "--verify", "HEAD")
    if not mirror.valid_branch(branch) or not head or not SHA.fullmatch(head):
        raise ValueError("Docker repository has no verified active branch and HEAD")
    return branch, head


def checked(repo, expected_branch, expected_head):
    branch, head = identity(repo)
    if (not mirror.valid_branch(expected_branch) or
            not SHA.fullmatch(expected_head) or
            branch != expected_branch or head != expected_head):
        raise ValueError("branch or HEAD changed since preview")
    return branch, head


def safe_relative(path):
    if (not path or path in (".", "..") or path.startswith("/") or
            path.startswith("../") or "/../" in path or
            path == ".git" or path.startswith(".git/") or "\0" in path):
        raise ValueError("unsafe file path from Git status")
    # Git pathspecs treat wildcard-like characters as patterns by default.
    # Literal+top scopes the write to exactly this Git-reported pathname.
    return ":(top,literal)" + path


def status_entries(repo):
    """(digest, entries), using the same raw NUL Git porcelain for preview/write.

    Git porcelain v1 -z records rename/copy with second NUL path. Each entry
    maps to exactly the paths Git reported. No string splitting on ' -> '.
    """
    raw = git_result(repo, ["status", "--porcelain=v1", "-z",
                            "--untracked-files=all"])
    token = hashlib.sha256(raw).hexdigest()
    fields = raw.split(b"\0")
    if fields and fields[-1] == b"":
        fields.pop()
    entries = []
    pos = 0
    while pos < len(fields):
        item = fields[pos]
        pos += 1
        if len(item) < 4 or item[2:3] != b" ":
            raise ValueError("unexpected Git porcelain entry")
        xy = item[:2].decode("ascii", errors="strict")
        path = os.fsdecode(item[3:])
        names = [path]
        if "R" in xy or "C" in xy:
            if pos >= len(fields):
                raise ValueError("incomplete Git rename entry")
            names.append(os.fsdecode(fields[pos]))
            pos += 1
        for name in names:
            safe_relative(name)
        def show(x):
            if not x.isprintable() or x != x.strip() or " " in x:
                return repr(x)
            return x
        display = xy + " " + " -> ".join(show(x) for x in names)
        entries.append((display, names))
    if len(entries) > 20000:
        raise ValueError("too many changed entries")
    return token, entries


def stage_preview(root, name):
    repo = exact_repo(root, name)
    branch, head = identity(repo)
    token, entries = status_entries(repo)
    if len(entries) > 500:
        raise ValueError("too many files for selective stage; use direct Docker Git")
    lines = ["FORTIS_DOCKER_STAGE_V1\t{}\t{}\t{}\t{}".format(
        branch, head, token, len(entries))]
    for idx, (display, _) in enumerate(entries):
        lines.append("F\t{}\t{}".format(
            idx, base64.b64encode(display.encode("utf-8")).decode("ascii")))
    return "\n".join(lines)


def stage(root, name, branch, head, token, indices):
    repo = exact_repo(root, name)
    checked(repo, branch, head)
    current, entries = status_entries(repo)
    if not DIGEST.fullmatch(token) or current != token:
        raise ValueError("file changes differ from preview; refresh before staging")
    if len(entries) > 500:
        raise ValueError("too many changes for selective stage")
    if not indices or len(indices) > 500 or len(set(indices)) != len(indices):
        raise ValueError("invalid selected-file indices")
    if min(indices) < 0 or max(indices) >= len(entries):
        raise ValueError("selected-file index outside snapshot")
    # For an already-staged rename, the previous name is NOT in the index
    # and 'git add -A -- oldname' would fail with "pathspec did not match".
    # For an unstaged rename its old path IS in the index; stage that deletion
    # separately using -u. No other Git files are staged.
    indexed_raw = git_result(repo, ["ls-files", "--cached", "-z"])
    indexed = {os.fsdecode(x) for x in indexed_raw.split(b"\0") if x}
    added = set()
    for idx in indices:
        names = entries[idx][1]
        main_path = safe_relative(names[0])
        if main_path not in added:
            git_result(repo, ["add", "-A", "--", main_path], write=True, timeout=80)
            added.add(main_path)
        if len(names) == 2 and names[1] in indexed:
            old_path = safe_relative(names[1])
            if old_path not in added:
                git_result(repo, ["add", "-u", "--", old_path], write=True, timeout=80)
                added.add(old_path)
    checked(repo, branch, head)
    return "STAGED\t{}\t{}\t{}".format(name, branch, len(indices))


def staged_manifest(repo):
    # Raw records include index object ids, not just displayed file names.
    raw = git_result(repo, ["diff", "--cached", "--raw", "-z", "--no-ext-diff"])
    token = hashlib.sha256(raw).hexdigest()
    names = git_result(repo, ["diff", "--cached", "--name-status", "-z", "--no-ext-diff"])
    fields = names.split(b"\0")
    if fields and fields[-1] == b"":
        fields.pop()
    files = []
    i = 0
    while i < len(fields):
        st = fields[i].decode("ascii", errors="strict")
        i += 1
        size = 2 if st.startswith(("R", "C")) else 1
        if i + size > len(fields):
            raise ValueError("incomplete staged Git status")
        paths = [os.fsdecode(x) for x in fields[i:i + size]]
        i += size
        files.append(st + " " + " -> ".join(repr(x) if " " in x else x for x in paths))
    return token, files


def commit_preview(root, name):
    repo = exact_repo(root, name)
    branch, head = identity(repo)
    token, files = staged_manifest(repo)
    if len(files) > 500:
        raise ValueError("too many staged files for safe preview")
    lines = ["FORTIS_DOCKER_COMMIT_V1\t{}\t{}\t{}\t{}".format(
        branch, head, token, len(files))]
    for line in files:
        lines.append("F\t" + base64.b64encode(line.encode("utf-8")).decode("ascii"))
    return "\n".join(lines)


def commit(root, name, branch, head, staged_hash, msg_b64):
    repo = exact_repo(root, name)
    checked(repo, branch, head)
    if not DIGEST.fullmatch(staged_hash):
        raise ValueError("invalid staged manifest digest")
    try:
        message = base64.b64decode(msg_b64, validate=True).decode("utf-8", errors="strict")
    except (ValueError, UnicodeError):
        raise ValueError("invalid commit message")
    if (not message.strip() or len(message) > 4000 or "\0" in message or
            message != message.strip()):
        raise ValueError("invalid commit message length/content")
    token, files = staged_manifest(repo)
    if not files or token != staged_hash:
        raise ValueError("staged files changed; preview again")
    git_result(repo, ["-c", "commit.gpgsign=false", "commit",
                      "--no-verify", "-m", message], write=True, timeout=120)
    new_branch, new_head = identity(repo)
    if new_branch != branch or new_head == head:
        raise ValueError("commit postcondition failed; inspect Docker repository")
    return "COMMITTED\t{}\t{}\t{}".format(name, branch, new_head)


def remote_sha(repo, branch):
    # Exact ref, no stale tracking ref and no auto fetch.
    out = git_result(repo, ["ls-remote", "--heads", "origin",
                            "refs/heads/" + branch], timeout=35)
    records = out.decode("ascii", errors="strict").strip().splitlines()
    if not records:
        return NO_SHA
    if len(records) != 1:
        raise ValueError("ambiguous GitHub branch")
    parts = records[0].split()
    if (len(parts) != 2 or parts[1] != "refs/heads/" + branch or
            not SHA.fullmatch(parts[0])):
        raise ValueError("GitHub branch response is invalid")
    return parts[0]


def push_preview(root, name):
    repo = exact_repo(root, name)
    branch, head = identity(repo)
    remote = remote_sha(repo, branch)
    return "FORTIS_DOCKER_PUSH_V1\t{}\t{}\t{}".format(branch, head, remote)


def push(root, name, branch, head, previous_remote):
    repo = exact_repo(root, name)
    checked(repo, branch, head)
    if not SHA.fullmatch(previous_remote):
        raise ValueError("invalid expected GitHub SHA")
    actual = remote_sha(repo, branch)
    if actual != previous_remote:
        raise ValueError("GitHub branch moved since preview")
    if actual == head:
        return "PUSHED\t{}\t{}\t{}".format(name, branch, head)
    # Normal non-forcing push. If GitHub has new commits or diverged, Git refuses.
    git_result(repo, ["push", "--porcelain", "origin",
                      "HEAD:refs/heads/" + branch], write=True, timeout=120)
    if remote_sha(repo, branch) != head:
        raise ValueError("GitHub post-push SHA mismatch; check remotely")
    return "PUSHED\t{}\t{}\t{}".format(name, branch, head)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--repo", required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    for operation in ("stage-preview", "stage", "commit-preview", "commit",
                      "push-preview", "push"):
        group.add_argument("--" + operation, action="store_true")
    parser.add_argument("--expect-branch")
    parser.add_argument("--expect-head")
    parser.add_argument("--expect-digest")
    parser.add_argument("--indices")
    parser.add_argument("--message-b64")
    parser.add_argument("--expect-remote")
    args = parser.parse_args()
    if not mirror.NAME.fullmatch(args.repo):
        parser.error("invalid repo")
    try:
        if args.stage_preview:
            print(stage_preview(args.root, args.repo))
        elif args.commit_preview:
            print(commit_preview(args.root, args.repo))
        elif args.push_preview:
            print(push_preview(args.root, args.repo))
        else:
            if (not args.expect_branch or not args.expect_head or
                    not mirror.valid_branch(args.expect_branch) or
                    not SHA.fullmatch(args.expect_head)):
                raise ValueError("write requires expected active branch and HEAD")
            if args.stage:
                if not args.expect_digest or not args.indices or not re.fullmatch(
                        r"\d{1,4}(?:,\d{1,4}){0,499}", args.indices):
                    raise ValueError("invalid selected file positions")
                positions = [int(x) for x in args.indices.split(",")]
                print(stage(args.root, args.repo, args.expect_branch,
                            args.expect_head, args.expect_digest, positions))
            elif args.commit:
                if not args.expect_digest or not args.message_b64:
                    raise ValueError("commit requires digest and message")
                print(commit(args.root, args.repo, args.expect_branch,
                             args.expect_head, args.expect_digest, args.message_b64))
            elif args.push:
                if not args.expect_remote:
                    raise ValueError("push requires previous GitHub SHA")
                print(push(args.root, args.repo, args.expect_branch,
                           args.expect_head, args.expect_remote))
    except ValueError as e:
        parser.exit(2, "STOP: " + str(e) + "\n")


if __name__ == "__main__":
    main()
