# AERCS — Autonomous Emergency Route Clearance System

Simulated drone-assisted ambulance escort system. A virtual drone flies ahead
of an ambulance, using live GPS tracking and route data to help clear the
fastest path to an emergency.

## How it actually works (read this first)

This project does **not** reimplement flight control (staying stable in the
air, motor mixing, attitude correction) — that's a solved, safety-critical
problem handled by **PX4**, an industry-standard open-source autopilot used
on real commercial and research drones.

What this project builds is the **mission-intelligence layer on top of PX4**:

```
┌─────────────────────────────┐
│   Your mission software     │  ← what we're building
│  (routing, rendezvous logic,│
│   dashboard, supervision)   │
└──────────────┬───────────────┘
               │ MAVSDK (offboard API, UDP:14540)
┌──────────────▼───────────────┐
│           PX4                │  ← already exists, battle-tested
│   (flight control, autopilot)│
└──────────────┬───────────────┘
               │ simulated sensors/physics
┌──────────────▼───────────────┐
│   Simulator (jMAVSim/Gazebo) │  ← stands in for a real drone
└───────────────────────────────┘
```

This mirrors how real commercial drone software is architected: companies
building drone delivery/logistics systems don't rewrite flight control either
— they build the mission layer on top of an autopilot like PX4 or ArduPilot.

## Phase 0 — Environment setup

You'll need, on your own machine (this can't be verified from inside this
chat, since I don't have network/Docker access in this sandbox):

1. **Docker** installed (for running PX4 SITL — "Software In The Loop", a
   fully simulated drone, no hardware needed)
2. **Python 3.9+**
3. **MAVSDK-Python** (`pip install mavsdk`)

### Step 1 — Start the simulated drone (PX4 SITL)

```bash
docker run --rm -it -p 14540:14540/udp jonasvautherin/px4-gazebo-headless:1.11.0
```

This starts PX4 running against a headless Gazebo simulation — a fully
virtual drone with realistic physics, no GUI required (keeps it lightweight
for a 2-day build). Leave this running in its own terminal tab.

You should see PX4 boot logs ending in something like `INFO [commander] Ready
for takeoff!` — that means the simulated drone is alive and listening.

### Step 2 — Install Python dependencies

```bash
cd aercs
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

### Step 3 — Run the connection test

```bash
python3 src/tests/test_connection.py
```

Expected output: the script connects to the simulated drone and prints its
GPS position and battery level. If this works, Phase 0 is done — you have a
live virtual drone you can command from Python.

## Project structure

```
aercs/
├── README.md
├── requirements.txt
├── docs/
│   └── ARCHITECTURE.md
├── src/
│   ├── drone/
│   │   └── controller.py     # wraps MAVSDK into simple functions
│   └── tests/
│       └── test_connection.py
```

## Phases

- [x] Phase 0 — Environment + drone connection
- [ ] Phase 1 — Drone control module (takeoff / goto / land / telemetry)
- [ ] Phase 2 — Google Maps route → GPS waypoints
- [ ] Phase 3 — Ambulance rendezvous engine
- [ ] Phase 4 — Live dashboard
- [ ] Phase 5 — Human-supervisor controls
- [ ] Phase 6 — Polish + write-up
