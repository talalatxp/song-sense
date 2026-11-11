"""Isolated synthetic repositories; never changes the project's index or history."""
import json
import shutil
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
fixture = (root / "tools/A303Preview/Program.cs").read_bytes()
results = []


def run(repo, *args):
    result = subprocess.run(args, cwd=repo, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError("Isolated repository setup failed")
    return result


def make(name):
    repo = output / name
    repo.mkdir()  # Never reuse or overwrite evidence.
    (repo / "scripts").mkdir()
    for file in ("Test-GitPrivacy.ps1", "PrivacyReviews.json"):
        shutil.copyfile(root / "scripts" / file, repo / "scripts" / file)
    shutil.copyfile(root / ".gitignore", repo / ".gitignore")
    run(repo, "git", "init", "-q")
    run(repo, "git", "config", "user.name", "Synthetic test")
    run(repo, "git", "config", "user.email", "fixture@example.invalid")
    run(repo, "git", "config", "core.autocrlf", "false")
    return repo


def put(repo, name, data):
    path = repo / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def commit(repo):
    run(repo, "git", "add", ".")
    run(repo, "git", "commit", "-qm", "Synthetic fixture")


def scan(repo, expected, required=()):
    result = subprocess.run(["pwsh", "-NoProfile", "-File", str(repo / "scripts/Test-GitPrivacy.ps1")], cwd=repo, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = result.stdout + result.stderr  # Scanner emits identities/categories only.
    (repo / "scanner-output.txt").write_text(text, encoding="utf-8")
    ok = (result.returncode == 0) == (expected != "BLOCKED") and all(part in text for part in required)
    if expected != "BLOCKED":
        ok = ok and ("Privacy audit " + expected) in text
    results.append({"case": repo.name, "expected": expected, "exitCode": result.returncode, "passed": ok})


path = "tools/A303Preview/Program.cs"
repo = make("clean"); commit(repo); scan(repo, "CLEAN")
repo = make("reviewed"); put(repo, path, fixture); commit(repo)
scan(repo, "REVIEWED_FIXTURES", ("worktree: " + path, "index: " + path, "history: " + path, "matches=2"))
repo = make("outside-path"); put(repo, "tools/Other.cs", fixture); commit(repo); scan(repo, "BLOCKED", ("tools/Other.cs [Literal secret",))
repo = make("mutated"); put(repo, path, fixture + b"\n// changed content\n"); commit(repo); scan(repo, "BLOCKED")
# Runtime construction prevents introducing a scanner match into this test source.
extra = ('\npassword = "' + 'z' * 24 + '"\n').encode()
repo = make("additional-match"); put(repo, path, fixture + extra); commit(repo); scan(repo, "BLOCKED", ("matches=3",))
repo = make("other-category"); put(repo, path, fixture + ('\n' + 'sk-' + 'A' * 24).encode()); commit(repo); scan(repo, "BLOCKED", ("OpenAI credential",))
repo = make("historical-mutation"); put(repo, path, fixture + b"\n// historical mutation\n"); commit(repo)
put(repo, path, fixture); commit(repo); scan(repo, "BLOCKED", ("history: " + path + " [Literal secret",))
repo = make("historical-other-path"); put(repo, "tools/Other.cs", fixture); commit(repo)
(repo / "tools/Other.cs").unlink(); put(repo, path, fixture); commit(repo); scan(repo, "BLOCKED", ("history: tools/Other.cs [Literal secret",))
repo = make("index-mutation"); put(repo, path, fixture + extra); commit(repo)
put(repo, path, fixture); scan(repo, "BLOCKED", ("index: " + path + " [Literal secret; matches=3",))
(output / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
print(json.dumps(results, indent=2))
sys.exit(0 if all(case["passed"] for case in results) else 1)
