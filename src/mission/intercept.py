"""
Intercept solver.

The drone launches from the incident scene and flies to meet an ambulance
inbound along a known road route. The solver finds the interception point
that maximises escort coverage: the furthest point from the scene the drone
can reach at or before the ambulance does.

Pure functions, no I/O, no network. Everything here is deterministic and
unit-testable, which is what lets the accuracy harness validate it.
"""

from math import radians, sin, cos, asin, sqrt


def haversine_m(a: tuple, b: tuple) -> float:
    """Great-circle distance in metres between two (lat, lon) points."""
    lat1, lon1 = radians(a[0]), radians(a[1])
    lat2, lon2 = radians(b[0]), radians(b[1])
    h = (sin((lat2 - lat1) / 2) ** 2
         + cos(lat1) * cos(lat2) * sin((lon2 - lon1) / 2) ** 2)
    return 2 * 6371000 * asin(sqrt(h))


def build_timeline(waypoints: list, speed_ms: float) -> list:
    """Convert road waypoints into [(seconds_from_dispatch, (lat, lon))]."""
    if speed_ms <= 0:
        raise ValueError("speed must be positive")
    if len(waypoints) < 2:
        raise ValueError("need at least two waypoints")

    timeline = [(0.0, waypoints[0])]
    elapsed = 0.0
    for i in range(1, len(waypoints)):
        elapsed += haversine_m(waypoints[i - 1], waypoints[i]) / speed_ms
        timeline.append((elapsed, waypoints[i]))
    return timeline


def position_at(timeline: list, seconds: float) -> tuple:
    """Interpolate the ambulance position at an arbitrary time."""
    if seconds <= 0:
        return timeline[0][1]
    if seconds >= timeline[-1][0]:
        return timeline[-1][1]

    lo, hi = 0, len(timeline) - 1
    while lo < hi - 1:
        mid = (lo + hi) // 2
        if timeline[mid][0] <= seconds:
            lo = mid
        else:
            hi = mid

    t0, p0 = timeline[lo]
    t1, p1 = timeline[hi]
    span = t1 - t0
    f = 0.0 if span == 0 else (seconds - t0) / span
    return (p0[0] + (p1[0] - p0[0]) * f, p0[1] + (p1[1] - p0[1]) * f)


def solve_intercept(scene: tuple, drone_speed_ms: float, timeline: list,
                    launch_delay_s: float = 0.0) -> dict:
    """
    Find the interception point.

    Walks the ambulance timeline from dispatch forward. The first point the
    drone can reach in time is the furthest one from the scene, which
    maximises the distance the drone escorts the ambulance.

    Returns None if the drone cannot intercept anywhere on the route.
    """
    if drone_speed_ms <= 0:
        raise ValueError("drone speed must be positive")

    total_s = timeline[-1][0]

    for ambulance_eta, point in timeline:
        flight_m = haversine_m(scene, point)
        drone_eta = launch_delay_s + flight_m / drone_speed_ms

        if drone_eta <= ambulance_eta:
            return {
                "point": point,
                "intercept_at_s": ambulance_eta,
                "drone_eta_s": drone_eta,
                "slack_s": ambulance_eta - drone_eta,
                "drone_distance_m": flight_m,
                "escort_duration_s": total_s - ambulance_eta,
                "escort_fraction": (total_s - ambulance_eta) / total_s,
            }

    return None


def escort_target(timeline: list, seconds: float, lead_s: float = 20.0) -> tuple:
    """Where the drone should hold while escorting: ahead of the ambulance."""
    return position_at(timeline, seconds + lead_s)