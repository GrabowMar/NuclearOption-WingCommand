# Wing Command v0.9.2.1 — Keeping the Hamsters Alive

A follow-up to The Mind of the Body, focused on flight recovery, missile defence, pilot records and everyday command fixes. The hamsters now have faces, more survival instincts, and slightly less enthusiasm for turning before they can fly. Still a work in progress.

## Highlights

### Safer departures and formation recovery

- Fixed-wing aircraft now need sufficient forward airspeed before handing takeoff control over to formation flight.
- Improved low-speed recovery, rejoin guidance and overshoot handling.
- Better handling of slow leaders and formation demands beyond a follower's flight envelope.
- Improved helicopter landing and cargo approaches: arrest movement and centre over the destination before descending.

### Missile defence and AI

- More consistent radar-missile notching, keeping the chosen defensive direction instead of reversing as the leader moves.
- Missile interception decisions now account for ROE, time to impact and nearby cover.
- Added individual switches for stronger AI turns, target spreading and missile-warning repairs, including effects on non-wing AI.
- Improved flight diagnostics to help track remaining recovery and formation problems.

### Pilots with faces and records

- Added experimental generated pilot portraits, shared consistently between Wing and Supply.
- Expanded recruit names, unique roster callsigns, service backgrounds and personalities.
- Fixed pilot assignment and survival handling through leader changes and takeover.
- Improved fatal-loss records, including cause of loss and killer information.

### Commands, HUD and WMC

- The standalone command wheel now defaults to **C**: hold, aim and release to confirm; right-click cancels. Existing key settings are preserved.
- Added live horizontal and vertical wing HUD position adjustments.
- Updated Wing and Supply presentation for the expanded pilot roster and portraits.
- Expanded debug aircraft selection and made action logging easier to access.

## Compatibility

- Nuclear Option: `0.34.2`
- BepInEx: `5.4.23+`
- Wing Command: `0.9.2.1`

Single-player and multiplayer host control remain the supported command modes. This release does not add reliable AI command control for clients joining another host.

The wingmen are still basically a bunch of hamsters. At least now you can recognise the one who owes you an aircraft.