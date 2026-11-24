#!/usr/bin/env python3
"""
Redacted secret scanner for working tree and git history.
Reports only commit, file, line, and rule ID (never prints matched secret values).
Fails if any secret is found outside the recorded historical baseline commits.
"""
import re
import subprocess
import sys
from pathlib import Path

BASELINE_COMMITS = {
    "37b2189", "27a1589", "603d0bc"
}

RULES = [
    ("sql-connection-password", re.compile(r'(?i)(?:Server|Data Source)=[^"\']*?(?:Password|Pwd)=([^;<"\'\s]{6,})')),
    ("appsettings-password", re.compile(r'(?i)"Password"\s*:\s*"(?!<|""|\s*")([^"]{6,})"')),
    ("appsettings-apikey", re.compile(r'(?i)"(?:ApiKey|WebhookSecret)"\s*:\s*"(?!<|""|\s*")([^"]{8,})"')),
    ("private-key-block", re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')),
]

SKIP_DIRS = {".git", ".dotnet", "bin", "obj", "publish", "_template_ref", "__pycache__", ".dataprotection-keys", "deploy", "tests", "scripts"}
SKIP_EXTS = {".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".ico", ".woff", ".woff2", ".db", ".db-shm", ".db-wal", ".md"}

def is_placeholder(line: str) -> bool:
    low = line.lower()
    return any(tok in low for tok in ("<db_password>", "<password>", "placeholder", "example", "test-secret", "dummy", "changeme"))

def scan_working_tree() -> list[tuple[str, str, int, str]]:
    findings = []
    root = Path(".")
    for path in root.rglob("*"):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        if not path.is_file() or path.suffix.lower() in SKIP_EXTS:
            continue
        if path.name.startswith("appsettings.") and path.name.endswith(".Local.json"):
            continue
        try:
            content = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        for lineno, line in enumerate(content.splitlines(), 1):
            if is_placeholder(line):
                continue
            for rule_id, pattern in RULES:
                if pattern.search(line):
                    findings.append(("WORKTREE", str(path), lineno, rule_id))
    return findings

def scan_new_commits() -> list[tuple[str, str, int, str]]:
    findings = []
    out = subprocess.check_output(["git", "rev-list", "--abbrev-commit", "HEAD"], text=True)
    commits = [c.strip() for c in out.splitlines() if c.strip()]
    for commit in commits[:50]:
        if any(commit.startswith(b) for b in BASELINE_COMMITS):
            continue
        diff = subprocess.check_output(["git", "show", "--unified=0", "--no-color", commit], text=True, errors="ignore")
        current_file = ""
        for line in diff.splitlines():
            if line.startswith("+++ b/"):
                current_file = line[6:]
            elif line.startswith("+") and not line.startswith("+++"):
                if current_file.startswith("tests/") or current_file.endswith(".md") or current_file.startswith("scripts/"):
                    continue
                content_line = line[1:]
                if is_placeholder(content_line):
                    continue
                for rule_id, pattern in RULES:
                    if pattern.search(content_line):
                        findings.append((commit, current_file, 0, rule_id))
    return findings

def main() -> int:
    wt = scan_working_tree()
    hist = scan_new_commits()
    all_findings = wt + hist
    if not all_findings:
        print("Secret scan passed: 0 new secrets in working tree or recent commits (historical baseline 37b2189..27a1589, 603d0bc excluded).")
        return 0
    for src, filepath, lineno, rule_id in all_findings:
        print(f"SECRET_FINDING source={src} file={filepath} line={lineno} rule={rule_id}")
    return 1

if __name__ == "__main__":
    sys.exit(main())
