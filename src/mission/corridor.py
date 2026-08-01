"""
Corridor pursuit.

After the dash to the scene, the drone flies back along the road at a
uniform clearing speed to meet the inbound ambulance. Flying the road
rather than a straight line is the point: the traffic that needs clearing
is on the road, and the stretch the drone has already swept is the
corridor the ambulance then drives through.

Because both vehicles travel the same polyline, the pursuit collapses to
one dimension - distance along the route. The drone advances backward from
the scene end, the ambulance forward from the hospital end, and they meet
when the two distances sum to the route length. This is exact, cheap to
compute every tick, and needs no intercept prediction, which matters
because the ambulance speed is not known in advance.
"""

from mission.intercept import haversine_m


def route_arc_lengths(waypoints: list) -> list:
    """Cumulative distance in metres from the route start to each waypoint."""
    lengths = [0.0]
    for i in range(1, len(waypoints)):
        lengths.append(lengths[-1] + haversine_m(waypoints[i - 1], waypoints[i]))
    return lengths


def point_at_distance(waypoints: list, arc: list, distance_m: float) -> tuple:
    """Interpolate the position at a given distance along the route."""
    total = arc[-1]
    if distance_m <= 0:
        return waypoints[0]
    if distance_m >= total:
        return waypoints[-1]

    lo, hi = 0, len(arc) - 1
    while lo < hi - 1:
        mid = (lo + hi) // 2
        if arc[mid] <= distance_m:
            lo = mid
        else:
            hi = mid

    span = arc[hi] - arc[lo]
    f = 0.0 if span == 0 else (distance_m - arc[lo]) / span
    a, b = waypoints[lo], waypoints[hi]
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


class CorridorPursuit:
    """
    Live state of the clearing run. Advanced one tick at a time; makes no
    assumption about the ambulance's future speed.
    """

    def __init__(self, waypoints: list, drone_speed_ms: float,
                 capture_radius_m: float = 60.0):
        self.waypoints = waypoints
        self.arc = route_arc_lengths(waypoints)
        self.total_m = self.arc[-1]
        self.drone_speed_ms = drone_speed_ms
        self.capture_radius_m = capture_radius_m

        self.drone_swept_m = 0.0          # from the scene end, backward
        self.ambulance_travelled_m = 0.0  # from the hospital end, forward
        self.elapsed_s = 0.0
        self.met = False
        self.met_at_s = None

    def step(self, dt_s: float, ambulance_speed_ms: float) -> dict:
        """Advance one tick using the ambulance's *current measured* speed."""
        self.elapsed_s += dt_s

        self.ambulance_travelled_m = min(
            self.total_m, self.ambulance_travelled_m + ambulance_speed_ms * dt_s)

        if not self.met:
            self.drone_swept_m = min(
                self.total_m, self.drone_swept_m + self.drone_speed_ms * dt_s)

        gap_m = self.total_m - self.ambulance_travelled_m - self.drone_swept_m

        if not self.met and gap_m <= self.capture_radius_m:
            self.met = True
            self.met_at_s = self.elapsed_s

        return self.state(gap_m)

    def state(self, gap_m: float = None) -> dict:
        if gap_m is None:
            gap_m = self.total_m - self.ambulance_travelled_m - self.drone_swept_m

        drone_distance = self.total_m - self.drone_swept_m
        cleared_m = self.drone_swept_m

        return {
            "elapsed_s": round(self.elapsed_s, 1),
            "met": self.met,
            "met_at_s": self.met_at_s,
            "gap_m": round(max(0.0, gap_m)),
            "drone_point": point_at_distance(self.waypoints, self.arc, drone_distance),
            "ambulance_point": point_at_distance(
                self.waypoints, self.arc, self.ambulance_travelled_m),
            "corridor_cleared_m": round(cleared_m),
            "corridor_cleared_fraction": round(cleared_m / self.total_m, 3),
            "ambulance_remaining_m": round(self.total_m - self.ambulance_travelled_m),
        }

    def cleared_corridor(self) -> list:
        """The swept stretch, for highlighting on the map."""
        start = self.total_m - self.drone_swept_m
        return [p for p, d in zip(self.waypoints, self.arc) if d >= start]