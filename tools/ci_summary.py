#!/usr/bin/env python3
"""WP-079 · tools/main-ci.sh summary: NUnit XML (EditMode + PlayMode) + compile errors -> summary.json / summary.md,
every red mapped to its owner (tools/ci-owners.tsv), and new reds vs the previous CI run of another SHA.
usage: ci_summary.py <out-dir> <sha> <ci-root> <owners.tsv> [--userdata-warning TEXT] [--driver-head SHA] [--tools-tree HASH]
Codex-audit M-12 (C-00 30.09): --driver-head / --tools-tree = the main-ci driver worktree's HEAD and `HEAD:tools` tree hash
(summary.json driverHead / toolsTree, a line in summary.md); optional — the old call still works (both null)."""
import glob, json, os, re, sys, datetime
import xml.etree.ElementTree as ET

out, sha, root, owners_path = sys.argv[1:5]

def opt(name):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv[5:-1] else None

udwarn = opt("--userdata-warning") or ""
driver_head, tools_tree = opt("--driver-head") or None, opt("--tools-tree") or None

rules = []
for line in open(owners_path, encoding="utf-8"):
    if not line.strip() or line.startswith("#"):
        continue
    rx, owner, area = (line.rstrip("\n").split("\t") + ["", ""])[:3]
    rules.append((re.compile(rx), owner, area))

signatures = []
sig_path = os.path.join(os.path.dirname(owners_path), "ci-signatures.tsv")
if os.path.exists(sig_path):
    for line in open(sig_path, encoding="utf-8"):
        if not line.strip() or line.startswith("#"):
            continue
        rx, owner, cause = (line.rstrip("\n").split("\t") + ["", ""])[:3]
        signatures.append((re.compile(rx), owner, cause))

def root_cause(message):
    for rx, owner, cause in signatures:
        if rx.search(message or ""):
            return owner, cause
    return None, None

def owner_of(fixture):
    for rx, owner, area in rules:
        if rx.search(fixture):
            return owner, area
    return "C-00", "unmapped"

PATH_OWNERS = [("/Camera/", "S-01"), ("/Core/Camera", "S-01"), ("/Weapons/", "S-02"), ("/Player/", "S-02"), ("/Input/", "S-02"),
               ("/Enemies/", "S-03"), ("/Spawn/", "S-03"), ("/Rounds/", "S-03"), ("/Navigation/", "S-03"),
               ("/Profile/", "S-04"), ("/Economy/", "S-04"), ("/Progression/", "S-04"), ("/World/", "S-05"), ("/Rendering/", "S-05"),
               ("/Characters/", "S-06"), ("/Animation/", "S-06"), ("/Feedback/", "S-07"), ("/Vfx/", "S-07"), ("/Audio/", "S-08"),
               ("/UI/", "S-09"), ("/Diagnostics/", "S-10"), ("/Tests/", "test owner"), ("/tools/", "S-01")]

def path_owner(path):
    for key, owner in PATH_OWNERS:
        if key in path:
            return owner
    return "C-00"

suites, reds = {}, []
for mode in ("editmode", "playmode"):
    xml = os.path.join(out, mode, mode + ".xml")
    tl = os.path.join(out, mode, "timeline.txt")
    info = {"xml": os.path.exists(xml), "total": 0, "passed": 0, "failed": 0, "skipped": 0, "seconds": 0.0,
            "watchdog": False, "errorsCS": 0}
    if os.path.exists(tl):
        t = open(tl, encoding="utf-8", errors="replace").read()
        m = re.findall(r"errorsCS=(\d+)", t)
        info["errorsCS"] = int(m[-1]) if m else 0
        info["watchdog"] = "killed=1" in t or "watchdog" in t
    if info["xml"]:
        tree = ET.parse(xml)
        for case in tree.iter("test-case"):
            info["total"] += 1
            res = case.get("result", "")
            info["seconds"] += float(case.get("duration", "0") or 0)
            if res == "Passed":
                info["passed"] += 1
            elif res in ("Skipped", "Inconclusive"):
                info["skipped"] += 1
            else:
                info["failed"] += 1
                full = case.get("fullname", case.get("name", "?"))
                fixture = (case.get("classname") or full.rsplit(".", 1)[0]).rsplit(".", 1)[-1]
                msg_el = case.find("failure/message")
                msg = (msg_el.text or "").strip().splitlines()[0][:240] if msg_el is not None and msg_el.text else res
                o, area = owner_of(fixture)
                full_msg = (msg_el.text or "") if msg_el is not None else ""
                ro, cause = root_cause(full_msg)
                reds.append({"mode": mode, "test": full, "fixture": fixture, "owner": ro or o, "fixtureOwner": o,
                             "area": area, "rootCause": cause or "", "message": msg})
    suites[mode] = info

compile_errors = []
for log in glob.glob(os.path.join(out, "*", "*.log")):
    for line in open(log, encoding="utf-8", errors="replace"):
        m = re.search(r"(Assets/[^\s(]+)\(\d+,\d+\): error (CS\d+): (.*)", line)
        if m and not any(c["file"] == m.group(1) and c["code"] == m.group(2) for c in compile_errors):
            compile_errors.append({"file": m.group(1), "code": m.group(2), "message": m.group(3)[:200], "owner": path_owner("/" + m.group(1))})

prev = None
for p in sorted(glob.glob(os.path.join(root, "*", "summary.json")), key=os.path.getmtime, reverse=True):
    if os.path.dirname(p) == os.path.abspath(out) or os.path.basename(os.path.dirname(p)) == sha:
        continue
    prev = json.load(open(p, encoding="utf-8"))
    break
prev_reds = {r["test"] for r in prev.get("reds", [])} if prev else set()
new_reds = [r for r in reds if r["test"] not in prev_reds] if prev else reds
fixed = sorted(prev_reds - {r["test"] for r in reds}) if prev else []

green = not reds and not compile_errors and all(s["xml"] and not s["watchdog"] for s in suites.values())
summary = {"sha": sha, "utc": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"), "green": green, "suites": suites,
           "reds": reds, "compileErrors": compile_errors, "previousSha": prev.get("sha") if prev else None,
           "newReds": [r["test"] for r in new_reds], "fixedSincePrevious": fixed, "userdataWarning": udwarn,
           "driverHead": driver_head, "toolsTree": tools_tree}
json.dump(summary, open(os.path.join(out, "summary.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)

L = [f"# main CI — `{sha[:10]}` — {'GREEN' if green else 'RED'}", "", f"UTC {summary['utc']} · önceki koşu `{(summary['previousSha'] or '—')[:10]}`", ""]
if driver_head or tools_tree:
    L[-1:-1] = [f"Sürücü (driver) HEAD `{driver_head or '—'}` · tools ağacı `{tools_tree or '—'}`"]
L += ["| Süit | toplam | geçti | kırmızı | atlandı | s | derleme hatası | bekçi |", "|---|---:|---:|---:|---:|---:|---:|---|"]
for mode, s in suites.items():
    L.append(f"| {mode} | {s['total']} | {s['passed']} | {s['failed']} | {s['skipped']} | {s['seconds']:.0f} | {s['errorsCS']} | {'KILLED' if s['watchdog'] else ('-' if s['xml'] else 'XML YOK')} |")
if compile_errors:
    L += ["", "## Derleme hataları", "| Dosya | Kod | Sahip | Mesaj |", "|---|---|---|---|"]
    L += [f"| `{c['file']}` | {c['code']} | {c['owner']} | {c['message']} |" for c in compile_errors]
if reds:
    causes = {}
    for r in reds:
        if r["rootCause"]:
            causes.setdefault((r["owner"], r["rootCause"]), 0)
            causes[(r["owner"], r["rootCause"])] += 1
    if causes:
        L += ["", "## Bilinen kök nedenler (ci-signatures.tsv)", "| Sahip | Kök neden | Kırmızı |", "|---|---|---:|"]
        L += [f"| {o} | {c} | {n} |" for (o, c), n in sorted(causes.items(), key=lambda kv: -kv[1])]
    L += ["", "## Kırmızılar (sahip)", "| Mod | Test | Sahip | Fikstür sahibi | Yeni? | Mesaj |", "|---|---|---|---|---|---|"]
    newset = set(summary["newReds"])
    L += [f"| {r['mode']} | `{r['test']}` | {r['owner']} | {r['fixtureOwner']} | {'YENİ' if r['test'] in newset else 'süren'} | {r['message'].replace('|', '/')} |" for r in reds]
if fixed:
    L += ["", "## Önceki koşuya göre düzelen", *[f"- `{t}`" for t in fixed]]
if udwarn:
    L += ["", "## Kullanıcı verisi", udwarn]
open(os.path.join(out, "summary.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")

owners = {}
for r in reds:
    owners.setdefault(r["owner"], 0)
    owners[r["owner"]] += 1
print(f"CI {sha[:10]} {'GREEN' if green else 'RED'} · edit {suites['editmode']['passed']}/{suites['editmode']['total']} · play {suites['playmode']['passed']}/{suites['playmode']['total']} · compile {len(compile_errors)} · reds by owner {owners or '-'} · new {len(new_reds)}")
sys.exit(0 if green else 1)
