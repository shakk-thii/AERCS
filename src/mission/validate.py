"""
Full-mission accuracy validation.

Tests the actual current design end to end: dash phase (drone launches
from the hospital, flies direct to the scene against a response deadline)
followed by corridor phase (drone sweeps the road backward from the scene
to meet an ambulance whose speed varies with traffic).

This replaces the earlier harness, which tested a mid-route intercept
model that the project no longer uses.

Run: python src/mission/validate.py
"""

import sys
import os

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mission.intercept import haversine_m
from mission.traffic import TrafficModel, required_dash_speed
from mission.corridor import CorridorPursuit

TICK_S = 0.5

# Airframe constraints - a real emergency-response quadcopter is fast but
# not limitless. These numbers describe a specific, plausible airframe.
AIRFRAME_MAX_MS = 45.0        # ~162 km/h hard mechanical limit
CLIMB_RATE_MS = 4.0           # vertical m/s while climbing
DESCENT_RATE_MS = 3.0         # vertical m/s while descending (slower, by design)
DASH_ALTITUDE_M = 70.0        # cruise altitude, clear of towers/obstacles
CORRIDOR_ALTITUDE_M = 20.0    # low sweep altitude, matched to traffic

RESPONSE_DEADLINE_S = 180.0   # 3-minute dash requirement
CORRIDOR_SPEED_MS = 50 / 3.6  # uniform 50 km/h clearing speed


def lerp(a, b, f):
    f = max(0.0, min(1.0, f))
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


def synthetic_route(start, end, via=None, points=80):
    """A road-like polyline standing in for an OSRM response."""
    if via is None:
        return [lerp(start, end, i / points) for i in range(points + 1)]
    half = points // 2
    return ([lerp(start, via, i / half) for i in range(half + 1)]
            + [lerp(via, end, i / half) for i in range(1, half + 1)])


def run_dash_phase(hospital, scene):
    """
    Straight-line dash from hospital to scene. Returns whether the
    3-minute deadline is achievable and how long the vertical profile
    (climb + descend) actually costs.

    Both climb and descend time are subtracted from the deadline BEFORE
    solving for the required cruise speed - solving on climb time alone
    and adding descent afterward looks fine at the speed-check step but
    silently blows the real deadline, which is what the first version of
    this file got wrong.
    """
    direct_m = haversine_m(hospital, scene)
    climb_s = DASH_ALTITUDE_M / CLIMB_RATE_MS
    descend_s = (DASH_ALTITUDE_M - CORRIDOR_ALTITUDE_M) / DESCENT_RATE_MS

    horizontal_budget_s = RESPONSE_DEADLINE_S - climb_s - descend_s
    dash = required_dash_speed(direct_m, horizontal_budget_s, AIRFRAME_MAX_MS)

    total_arrival_s = climb_s + dash["actual_arrival_s"] + descend_s

    return {
        "direct_m": direct_m,
        "climb_s": climb_s,
        "descend_s": descend_s,
        "cruise_speed_ms": dash["commanded_ms"],
        "cruise_speed_kmh": dash["commanded_ms"] * 3.6,
        "deadline_achievable": dash["achievable"],
        "total_arrival_s": total_arrival_s,
        "deadline_met": total_arrival_s <= RESPONSE_DEADLINE_S,
    }


def run_corridor_phase(route, seed):
    """
    Simulate the corridor sweep against a variable-speed ambulance,
    tick by tick, using the actual CorridorPursuit class.
    """
    pursuit = CorridorPursuit(route, CORRIDOR_SPEED_MS)
    traffic = TrafficModel(seed=seed)

    speeds = []
    max_ticks = int(1800 / TICK_S)  # 30 min safety cap against infinite loops
    ticks = 0

    while not pursuit.met and ticks < max_ticks:
        speed = traffic.step(TICK_S)
        speeds.append(speed * 3.6)
        pursuit.step(TICK_S, speed)
        ticks += 1

    return {
        "met": pursuit.met,
        "met_at_s": pursuit.met_at_s,
        "corridor_cleared_fraction": pursuit.state()["corridor_cleared_fraction"],
        "ambulance_mean_kmh": sum(speeds) / len(speeds) if speeds else 0,
        "ambulance_min_kmh": min(speeds) if speeds else 0,
        "ambulance_max_kmh": max(speeds) if speeds else 0,
    }


SCENARIOS = [
    ("short urban",   (10.9964, 76.9702), (11.0060, 77.0290), (11.0150, 77.0000)),
    ("straight run",  (10.9964, 76.9702), (11.0270, 77.0060), None),
    ("long corridor", (10.9964, 76.9702), (11.0780, 76.9990), (11.0400, 76.9800)),
    ("short hop",     (10.9964, 76.9702), (10.9700, 76.9500), None),
    ("far scene",     (10.9964, 76.9702), (11.0600, 77.0400), (11.0100, 77.0500)),
]

TRAFFIC_SEEDS = [1, 7, 42, 99, 123]


def main():
    print("\nFull-mission validation: dash phase + corridor phase")
    print(f"deadline {RESPONSE_DEADLINE_S:.0f}s | airframe max "
          f"{AIRFRAME_MAX_MS * 3.6:.0f} km/h | corridor speed "
          f"{CORRIDOR_SPEED_MS * 3.6:.0f} km/h\n")

    print(f"{'scenario':<14}{'direct':>8}{'dash spd':>10}{'dash ok':>9}"
          f"{'arrival':>9}")
    print("-" * 50)

    dash_failures = 0
    for name, hospital, scene, via in SCENARIOS:
        dash = run_dash_phase(hospital, scene)
        status = "YES" if dash["deadline_met"] else "NO"
        if not dash["deadline_met"]:
            dash_failures += 1
        print(f"{name:<14}{dash['direct_m']/1000:>7.2f}k"
              f"{dash['cruise_speed_kmh']:>9.0f}k"
              f"{status:>9}"
              f"{dash['total_arrival_s']:>8.0f}s")

    print(f"\ndash phase: {len(SCENARIOS) - dash_failures}/{len(SCENARIOS)} "
          f"scenarios meet the 3-minute deadline")
    print("(scenes beyond the airframe's reach are reported, not silently missed)\n")

    print(f"Corridor phase - {len(SCENARIOS)} routes x {len(TRAFFIC_SEEDS)} "
          f"traffic seeds = {len(SCENARIOS) * len(TRAFFIC_SEEDS)} runs\n")
    print(f"{'scenario':<14}{'route':>8}{'met_s':>8}{'amb avg':>9}"
          f"{'amb range':>13}{'cleared':>9}")
    print("-" * 62)

    corridor_failures = 0
    met_times = []
    cleared_fractions = []

    for name, hospital, scene, via in SCENARIOS:
        route = synthetic_route(hospital, scene, via)
        route_km = sum(haversine_m(route[i], route[i+1])
                       for i in range(len(route)-1)) / 1000

        for seed in TRAFFIC_SEEDS:
            result = run_corridor_phase(route, seed)

            if not result["met"]:
                corridor_failures += 1
                print(f"{name:<14}{route_km:>7.2f}k  FAILED TO MEET "
                      f"(seed {seed})")
                continue

            met_times.append(result["met_at_s"])
            cleared_fractions.append(result["corridor_cleared_fraction"])

            print(f"{name:<14}{route_km:>7.2f}k"
                  f"{result['met_at_s']:>7.0f}s"
                  f"{result['ambulance_mean_kmh']:>8.0f}k"
                  f"{result['ambulance_min_kmh']:>5.0f}-"
                  f"{result['ambulance_max_kmh']:<5.0f}k"
                  f"{result['corridor_cleared_fraction']*100:>7.0f}%")

    total_runs = len(SCENARIOS) * len(TRAFFIC_SEEDS)
    print("-" * 62)
    if met_times:
        print(f"corridor phase: {total_runs - corridor_failures}/{total_runs} "
              f"runs met successfully")
        print(f"mean corridor cleared: "
              f"{sum(cleared_fractions)/len(cleared_fractions)*100:.1f}%")
        print(f"corridor cleared range: "
              f"{min(cleared_fractions)*100:.1f}% - "
              f"{max(cleared_fractions)*100:.1f}%")

    total_failures = dash_failures + corridor_failures
    print(f"\ntotal failures: {total_failures}")
    return 1 if corridor_failures else 0


if __name__ == "__main__":
    sys.exit(main())