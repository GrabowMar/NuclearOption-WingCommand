"""Summarizes a run-suite.sh output folder: per scenario its verdict and failed checks, then for runs that recorded tracks
(units.csv) each member's lift-off time, lowest radar altitude after lift-off and how its track ended, and WC's loss-related
log lines (losses, relocations, aborts, GCAS, exceptions).

    python tests/ingame/analyze.py <suite-output-folder>
"""
import csv
import glob
import json
import os
import re
import sys

LOSS = re.compile(r"lost|left the wing|destroy|crash|Aborted|abort|GcasActivated|fault|Exception|relocat|ejected|stopped 15 s")


def run_dir(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    try:
        data = json.loads(text)
        return data.get("run_dir") or (data.get("report") or {}).get("run_dir")
    except ValueError:
        m = re.search(r'"run_dir":\s*"([^"]+)"', text)
        return m.group(1).replace("\\\\", "\\") if m else None


def members(units):
    tracks = {}
    with open(units, encoding="utf-8", errors="replace") as f:
        for r in csv.DictReader(f):
            if r["id"] != "lead":
                tracks.setdefault(r["id"], []).append(r)
    for mid, rows in sorted(tracks.items()):
        lift = low = None
        for r in rows:
            alt, t = float(r["radar_alt"]), float(r["t"])
            if lift is None and alt > 5:
                lift = t
            if lift is not None and t > lift + 1:
                low = alt if low is None else min(low, alt)
        last = rows[-1]
        yield "%s %s: lift %s, lowest after %s, end t=%s alt=%s tas=%s alive=%s" % (
            mid, rows[0]["type"], "%.0f" % lift if lift else "-", "%.0f" % low if low is not None else "-",
            last["t"], last["radar_alt"], last["tas"], last["alive"])


def main():
    out = sys.argv[1]
    passed = failed = 0
    for path in sorted(glob.glob(os.path.join(out, "*.json"))):
        name = os.path.basename(path)[:-5]
        d = run_dir(path)
        report = None
        if d and os.path.exists(os.path.join(d, "report.json")):
            report = json.load(open(os.path.join(d, "report.json"), encoding="utf-8"))
        verdict = report.get("verdict") if report else "no report"
        passed += verdict == "pass"
        failed += verdict != "pass"
        print("=" * 8, name, verdict, d or "")
        if report:
            for c in report.get("checks", []):
                if not c.get("ok"):
                    print("   FAIL", c.get("detail"))
            if report.get("error"):
                print("   ERROR", report.get("error"))
        if d and os.path.exists(os.path.join(d, "units.csv")):
            for line in members(os.path.join(d, "units.csv")):
                print("   ", line)
        log = os.path.join(d, "bepinex.log") if d else None
        if log and os.path.exists(log):
            for line in open(log, encoding="utf-8", errors="replace"):
                if "Wing Command" in line and "[Automation] Ground:" not in line and LOSS.search(line):
                    print("    log:", line.strip()[:220])
    print("%d passed, %d not passed" % (passed, failed))


if __name__ == "__main__":
    main()
