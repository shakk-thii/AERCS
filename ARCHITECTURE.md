# Architecture

## Why PX4 + MAVSDK, and not "build a drone AI from scratch"

Flight control is a real-time control-systems problem: reading an IMU
hundreds of times per second and adjusting motor speeds to keep a physically
unstable object (a multirotor naturally wants to tip over) upright and
moving where commanded. Getting this wrong isn't a bug, it's a crash. PX4 has
years of engineering and real-world flight hours behind it, released under
the permissive BSD-3 license, which is exactly why professional drone
software is built *on top of* it rather than reinventing it.

**MAVSDK** is the Python (or C++/Swift/etc.) library that talks to PX4 over
MAVLink, the standard protocol drones use to communicate. It exposes clean
async functions like `drone.action.takeoff()` instead of requiring you to
hand-craft MAVLink protocol messages.

## Connection details

- PX4 SITL (Software In The Loop) runs a full copy of PX4 against a
  simulator instead of real hardware/sensors.
- PX4's offboard API (what MAVSDK talks to) listens on **UDP port 14540**.
  This is standard/hardcoded PX4 behavior, not something we configure.
- Ground control stations (like QGroundControl, if you want a visual map
  view of the simulated drone) use a *different* port, 14550 — both can be
  connected to the same running simulation at once.

## System components (target end state)

1. **`drone/controller.py`** — thin wrapper around MAVSDK: connect, arm,
   takeoff, fly to a GPS coordinate, land, read telemetry (position, battery,
   speed). This is the only module that talks to PX4 directly.
2. **`routing/`** (Phase 2) — calls Google Maps Directions API to get a
   real-world route between two points, converts it into a list of GPS
   waypoints the drone module can consume.
3. **`rendezvous/`** (Phase 3) — the actual "invention" of AERCS: given the
   drone's live position and a simulated ambulance's live position, computes
   where they should meet and how long until that happens.
4. **`dashboard/`** (Phase 4) — a small web frontend showing live drone
   stats and the countdown to rendezvous, fed by a WebSocket stream from the
   backend.
5. **`supervisor/`** (Phase 5) — the human-in-the-loop control: a single
   approve/abort interface, plus automatic deviation alerts. This is what
   makes the system "supervised autonomy" rather than fully unsupervised.

## Design principle carried through every phase

Each module should be independently testable without the others running.
`controller.py` should work and be testable purely against the simulator,
with no dependency on Google Maps or the dashboard existing yet. This is
also why we're building and committing phase-by-phase — each phase is a
genuinely separate, demonstrable piece of working software.
