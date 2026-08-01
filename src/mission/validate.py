"""
Accuracy harness.

Validates the intercept solver by simulating the pursuit tick-by-tick and
comparing what actually happens against what the solver predicted. This is
the difference between "the code runs" and "the code is correct".

Metrics reported:
  time error      predicted intercept time vs simulated
  position error  predicted intercept point vs where they actually met
  miss distance   closest approach achieved
  escort coverage fraction of the journey escorted

Run: python src/mission/validate.py
"""

import sys
import os

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mission.intercept import (haversine_m, build_timeline, position_at,
                               solve_intercept)

TICK_S = 0.5
CAPTURE_RADIUS_M = 50.0


def lerp(a, b, f):
    f = max(0.0, min(1.0, f))
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


def synthetic_route(start, end, via=None, points=80):
    """A road-like route with a dogleg, standing in for an OSRM response."""
    if via is None:
        return [lerp(start, end, i / points) for i in range(points + 1)]
    half = points // 2
    return ([lerp(start, via, i / half) for i in range(half + 1)]
            + [lerp(via, end, i / half) for i in range(1, half + 1)])


def simulate_pursuit(scene, drone_speed_ms, timeline, prediction):
    """
    Fly the pursuit forward in time. The drone uses proportional navigation
    toward the solver's predicted intercept point, which is what the real
    controller does when it issues goto commands.
    """
    drone = scene
    closest_m = float("inf")
    met_at_s = None
    t = 0.0
    total_s = timeline[-1][0]

    while t <= total_s:
        ambulance = position_at(timeline, t)
        gap = haversine_m(drone, ambulance)
        closest_m = min(closest_m, gap)

        if gap <= CAPTURE_RADIUS_M and met_at_s is None:
            met_at_s = t
            break

        # Steer toward the predicted intercept point until reached, then
        # switch to direct pursuit of the ambulance.
        target = prediction["point"]
        if haversine_m(drone, target) < 30.0:
            target = ambulance

        step_m = drone_speed_ms * TICK_S
        remaining = haversine_m(drone, target)
        drone = target if remaining <= step_m else lerp(drone, target, step_m / remaining)

        t += TICK_S

    return {"met_at_s": met_at_s, "met_point": drone, "closest_m": closest_m}


SCENARIOS = [
    ("short urban",   (10.9964, 76.9702), (11.0060, 77.0290), (11.0150, 77.0000)),
    ("straight run",  (10.9964, 76.9702), (11.0270, 77.0060), None),
    ("long corridor", (10.9964, 76.9702), (11.0780, 76.9990), (11.0400, 76.9800)),
    ("short hop",     (10.9964, 76.9702), (10.9700, 76.9500), None),
    ("wide dogleg",   (10.9964, 76.9702), (11.0600, 77.0400), (11.0100, 77.0500)),
]

AMBULANCE_SPEED_MS = 100 / 3.6
DRONE_SPEED_MS = 28.0


def main():
    print(f"\nIntercept accuracy validation")
    print(f"ambulance {AMBULANCE_SPEED_MS * 3.6:.0f} km/h | "
          f"drone {DRONE_SPEED_MS * 3.6:.0f} km/h | "
          f"capture radius {CAPTURE_RADIUS_M:.0f} m\n")
    print(f"{'scenario':<15}{'route':>8}{'pred':>8}{'actual':>8}"
          f"{'dt':>7}{'dpos':>8}{'escort':>8}")
    print("-" * 62)

    failures = 0
    time_errors = []
    position_errors = []

    for name, hospital, scene, via in SCENARIOS:
        route = synthetic_route(hospital, scene, via)
        timeline = build_timeline(route, AMBULANCE_SPEED_MS)
        prediction = solve_intercept(scene, DRONE_SPEED_MS, timeline)

        if prediction is None:
            print(f"{name:<15}{'--':>8}  no intercept possible")
            continue

        actual = simulate_pursuit(scene, DRONE_SPEED_MS, timeline, prediction)

        if actual["met_at_s"] is None:
            print(f"{name:<15}  SIMULATION FAILED TO INTERCEPT")
            failures += 1
            continue

        dt = actual["met_at_s"] - prediction["intercept_at_s"]
        dpos = haversine_m(actual["met_point"], prediction["point"])
        time_errors.append(abs(dt))
        position_errors.append(dpos)

        route_km = timeline[-1][0] * AMBULANCE_SPEED_MS / 1000
        print(f"{name:<15}{route_km:>7.2f}k"
              f"{prediction['intercept_at_s']:>7.0f}s"
              f"{actual['met_at_s']:>7.0f}s"
              f"{dt:>+6.1f}s"
              f"{dpos:>7.0f}m"
              f"{prediction['escort_fraction'] * 100:>7.0f}%")

    print("-" * 62)
    if time_errors:
        print(f"mean time error     {sum(time_errors) / len(time_errors):.2f} s")
        print(f"max time error      {max(time_errors):.2f} s")
        print(f"mean position error {sum(position_errors) / len(position_errors):.0f} m")
        print(f"max position error  {max(position_errors):.0f} m")
    print(f"failures            {failures}/{len(SCENARIOS)}\n")

    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())