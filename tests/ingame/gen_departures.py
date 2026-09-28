"""Writes the departure matrix: every airframe the wing can launch, from two fields, three wingmen each.

Each run launches from the field, registers the members once they are adopted, records their tracks (units.csv) so a
crash after lift-off can be read back, and checks that all three fly, none is left on the ground, none aborted and none
was lost. Run: python tests/ingame/gen_departures.py (writes tests/ingame/wc-dep-*.json).
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))

AIRFRAMES = [
    "CI-22 Cricket", "T/A-30 Compass", "A-19 Brawler", "FS-12 Revoker", "FS-20 Vortex", "KR-67 Ifrit", "EW-25 Medusa",
    "SFB-81 Darkreach", "VT-7 Vagrant", "UH-90 Ibis", "SAH-46 Chicane", "VL-49 Tarantula",
]

# (slug, lead spawn on the map, field name or "nearest")
FIELDS = [
    ("north", [-18632, 32049], "airbase_boscali_north"),
    ("mountains", [-1131, -26845], "nearest"),
]

COUNT = 3


def slug(name):
    return re.sub(r"[^a-z0-9]+", "", name.split(" ")[0].lower())


def scenario(airframe, field):
    fslug, at, fname = field
    name = "wc-dep-%s-%s" % (slug(airframe), fslug)
    return name, {
        "name": name,
        "mods": ["wingcommand"],
        "timeout_s": 600,
        "ops": [
            {"op": "spawn", "id": "lead", "type": "FS-20 Vortex", "at": {"map": at}, "altitude": 1500, "speed": 160, "heading": 0},
            {"op": "fly", "id": "lead", "program": [{"orbit": {"bank": 15, "seconds": 540}}]},
            {"op": "wait", "seconds": 3},
            {"op": "call", "method": "WingCommand.Automation.Pilots", "store": "fresh", "args": {"fresh": True}},
            {"op": "call", "method": "WingCommand.Automation.Launch", "store": "launch",
             "args": {"lead": "lead", "field": fname, "count": COUNT, "type": airframe}},
            {"op": "wait", "seconds": 30},
            {"op": "call", "method": "WingCommand.Automation.Members", "store": "adopted"},
            {"op": "record", "ids": ["*"], "hz": 4},
            {"op": "wait", "seconds": 60},
            {"op": "call", "method": "WingCommand.Automation.Ground", "store": "early"},
            {"op": "wait", "seconds": 150},
            {"op": "call", "method": "WingCommand.Automation.Ground", "store": "mid"},
            {"op": "wait", "seconds": 150},
            {"op": "call", "method": "WingCommand.Automation.Members", "store": "members"},
            {"op": "call", "method": "WingCommand.Automation.Ground", "store": "ground"},
            {"op": "call", "method": "WingCommand.Automation.Metrics", "store": "wing"},
            {"op": "end"},
        ],
        "checks": [
            {"check": "status_ok"},
            {"check": "result_above", "path": "launch.launched", "min": COUNT},
            {"check": "result_above", "path": "ground.flying", "min": COUNT},
            {"check": "result_below", "path": "ground.grounded", "max": 0},
            {"check": "result_below", "path": "ground.aborted", "max": 0},
            {"check": "result_below", "path": "wing.left", "max": 0},
            {"check": "result_below", "path": "ground.ejections_blocked", "max": 0},
        ],
    }


def main():
    names = []
    for field in FIELDS:
        for airframe in AIRFRAMES:
            name, s = scenario(airframe, field)
            with open(os.path.join(HERE, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
                json.dump(s, f, indent=1)
                f.write("\n")
            names.append(name)
    print("\n".join(names))


if __name__ == "__main__":
    main()
