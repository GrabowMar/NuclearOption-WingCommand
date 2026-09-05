# Launch safety changes

Requisitions select an exact compatible hangar and check nearby aircraft and pending
spawns before using its native door/spawn sequence. Clearance includes both airframes'
dimensions with a 10 m margin and a 30 m minimum. A loss before leaving this area blocks
that hangar/airframe combination for the mission. Other compatible pads remain usable;
normal field-selection preferences still apply. This does not establish whether a
particular shelter mesh fits the aircraft or protect it from enemy fire.

Native taxi and takeoff retain control until they finish. Fixed-wing delivery handoff
requires at least 80 m AGL, 1.1 times takeoff speed and no more than 2 m/s sink, sustained
for two seconds. The short departure controller begins a gentle turn toward the leader,
limits climb demand to 8 degrees and levels to accelerate when short of speed. Formation
releases at 150 m and 1.25 times takeoff speed. Formation banking remains limited by both
terrain clearance and speed, including the leader bank-matching trim.

Automated checks cover clearance dimensions, flight handoff gates, the shorter climb,
energy recovery and bank limits. Live validation remains necessary:

- At Island 14, order three Vortexes rapidly. Check that nearby occupied or reserved pads
  queue orders and that a failed apron launch blocks that pad for further Vortex orders.
- Repeat on AssaultCarrier1. Verify the native door/catapult sequence and the logged
  stable airborne handoff, then observe a gentle turn toward a player behind the runway.
- Repeat with a heavy loadout and a helicopter. Confirm the jet accelerates instead of
  hanging in a steep climb and the helicopter does not inherit a jet speed requirement.
- Test with the player low or on deck, on rising terrain, and with a queued non-formation
  order. Confirm terrain protection and the player's standing order remain effective.

Logs now identify the exact accepted hangar and spawn position, blocked launch points,
and altitude, speed and climb rate at handoff. Ground ejections alone cannot distinguish
spawn contact, combat damage and the native takeoff stuck timeout.
