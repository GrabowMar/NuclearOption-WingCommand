# Hold formation collision fix

The captured session shows Vortexes -143574 and -140170 closing to 13 m,
followed by -140170 at 166 degrees bank and a large speed loss. This is consistent
with reported contact, though the logs do not contain a physics collision callback.
The session still reports the earlier launch controller, so it does not validate
the subsequently deployed launch changes.

Changes:

- Predicted close traffic receives a dedicated steering escape ahead of slot pursuit
  and leader bank matching. Both wingmen and the leader are checked over six seconds.
- The physical separation floor uses aircraft dimensions. The predictive buffer is
  bounded so it does not continually repel a steady compressed formation.
- Slot bank no longer depends on each member's individual slot error. The same leader
  attitude defines the slot frame. Hold ROE also disables automatic echelon side swaps.
- Slot motion from turns, rolls and shape changes is fed into velocity and throttle
  tracking. Hold gains and damping increase together near station, with smaller
  position tolerance zones. Aircraft spacing is unchanged.
- Descending at altitude no longer removes most turn authority. Terrain clearance,
  energy limits and excess sink still restrict banking and roll trim.

Regression checks cover predicted crossing traffic, the recorded close range,
safe parallel/diverging traffic, Hold gain blending and coordinated descent gates.
Flight validation is still required: repeat three Vortexes in Hold/Line Abreast,
stabilize, then roll, reverse turns and descend at altitude. Check the new collision
priority/clear logs, minimum separation, slot error and speed retention. Repeat with
echelon and mixed airframes, and verify low-altitude protection remains effective.
These controls do not guarantee exact mirroring of maneuvers beyond an airframe's
flight envelope.
