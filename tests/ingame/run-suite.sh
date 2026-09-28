#!/usr/bin/env bash
# Runs a suite of in-game scenarios unattended (nomodkit's `sim run`) and summarizes them.
#
#   bash tests/ingame/run-suite.sh <suite> [out-dir]      e.g.  bash tests/ingame/run-suite.sh departures
#
# <suite> is tests/ingame/suites/<suite>.txt (one scenario name per line, # comments) or a single scenario name.
# The build under test is snapshotted first (bin/Release/netstandard2.1/WingCommand.dll, so rebuilding mid-suite changes
# nothing). The game install is shared with other work, so before each run the script waits until the game has been closed
# for QUIET seconds (default 180), keeps whatever WingCommand.dll is deployed, puts the snapshot in place, runs, and puts the
# other DLL back once the game has closed. It never closes the game. At the end tests/ingame/analyze.py prints each run's
# verdict, failed checks, member lift-offs and losses.
set -u
SUITE="${1:?suite name or scenario}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${2:-${TEMP:-/tmp}/wingcommand-suites/$SUITE-$STAMP}"
QUIET="${QUIET:-180}"
GAME="${GAME:-/c/Program Files (x86)/Steam/steamapps/common/Nuclear Option}"
PLUGIN="$GAME/BepInEx/plugins/WingCommand"
NOMOD_PY="${NOMOD_PY:-/c/Users/marci/dev/nomodkit/.venv/Scripts/python.exe}"
mkdir -p "$OUT"

if [ -f "$HERE/suites/$SUITE.txt" ]; then
  mapfile -t SCENARIOS < <(grep -v '^\s*#' "$HERE/suites/$SUITE.txt" | sed 's/\r$//' | grep -v '^\s*$')
else
  SCENARIOS=("$SUITE")
fi
SNAP="$OUT/snapshot.dll"
cp "$REPO/bin/Release/netstandard2.1/WingCommand.dll" "$SNAP" || { echo "no Release build: dotnet build WingCommand.csproj -c Release"; exit 1; }
echo "suite $SUITE: ${#SCENARIOS[@]} scenario(s), snapshot $(sha256sum "$SNAP" | cut -c1-12), results in $OUT" | tee "$OUT/queue.log"

running() { powershell -NoProfile -Command "if (Get-Process NuclearOption -ErrorAction SilentlyContinue) { 'yes' }" 2>/dev/null | grep -q yes; }
wait_quiet() {
  local calm=0
  while [ "$calm" -lt "$QUIET" ]; do
    if running; then calm=0; else calm=$((calm + 15)); fi
    sleep 15
  done
}
hash_of() { sha256sum "$1" 2>/dev/null | cut -c1-12; }

run_one() {
  local name="$1"
  for attempt in $(seq 1 20); do
    wait_quiet
    local mine here kept=""
    mine=$(hash_of "$SNAP")
    here=$(hash_of "$PLUGIN/WingCommand.dll")
    if [ -n "$here" ] && [ "$here" != "$mine" ]; then
      cp "$PLUGIN/WingCommand.dll" "$OUT/other-$here.dll" && kept="$OUT/other-$here.dll"
    fi
    running && continue
    if ! cp "$SNAP" "$PLUGIN/WingCommand.dll" 2>/dev/null; then echo "$name: deploy failed (locked), retrying" | tee -a "$OUT/queue.log"; sleep 30; continue; fi
    echo "$name: running" | tee -a "$OUT/queue.log"
    (cd "$REPO" && "$NOMOD_PY" -m nomodkit.cli sim run "tests/ingame/$name.json" --confirm --json) > "$OUT/$name.json" 2>&1
    if [ -n "$kept" ]; then
      while running; do sleep 15; done
      cp "$kept" "$PLUGIN/WingCommand.dll" 2>/dev/null
    fi
    if grep -q '"status": "busy"' "$OUT/$name.json"; then echo "$name: busy, retrying" | tee -a "$OUT/queue.log"; continue; fi
    echo "$name done: $(grep -o '"verdict": "[a-z]*"' "$OUT/$name.json" | head -1)" | tee -a "$OUT/queue.log"
    return
  done
  echo "$name gave up" | tee -a "$OUT/queue.log"
}

for s in "${SCENARIOS[@]}"; do run_one "$s"; done
echo "suite $SUITE finished" | tee -a "$OUT/queue.log"
python "$HERE/analyze.py" "$OUT" | tee "$OUT/summary.txt"
