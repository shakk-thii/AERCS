"""
Phase 3 — Rendezvous engine.

Given an ambulance driving a road route and a drone flying direct, compute
where and when they meet. This is the core of AERCS.

The method: walk forward along the ambulance's route second by second. At
each future position, ask whether the drone flying straight there would
arrive at or before the ambulance. The first position where it can is the
rendezvous point — earlier ones are unreachable, later ones waste time.
"""

import sys
import os

sys.path.append(os.path.join(os.path.dirname(__file__), ".."))
from routing.route_planner import haversine_m, get_route, geocode


def build_ambulance_timeline(waypoints: list, speed_ms: float) -> list:
    """Convert road waypoints into (seconds_from_now, (lat, lon)) entries."""
    timeline = [(0.0, waypoints[0])]
    cumulative_seconds = 0.0

    for i in range(1, len(waypoints)):
        segment_m = haversine_m(waypoints[i - 1], waypoints[i])
        cumulative_seconds += segment_m / speed_ms
        timeline.append((cumulative_seconds, waypoints[i]))

    return timeline


def find_rendezvous(drone_position: tuple,
                    drone_speed_ms: float,
                    ambulance_timeline: list):
    """Find the earliest interceptable point. Returns None if impossible."""
    for seconds_until_ambulance, position in ambulance_timeline:
        distance_to_fly = haversine_m(drone_position, position)
        drone_arrival_s = distance_to_fly / drone_speed_ms

        if drone_arrival_s <= seconds_until_ambulance:
            return {
                "meeting_point": position,
                "countdown_s": seconds_until_ambulance,
                "drone_flight_time_s": drone_arrival_s,
                "drone_slack_s": seconds_until_ambulance - drone_arrival_s,
                "drone_distance_m": distance_to_fly,
            }

    return None


def format_countdown(seconds: float) -> str:
    """Turn 187.4 into '3:07' for display."""
    minutes = int(seconds // 60)
    remaining = int(seconds % 60)
    return f"{minutes}:{remaining:02d}"


if __name__ == "__main__":
    AMBULANCE_SPEED_MS = 13.9   # ~50 km/h urban emergency speed
    DRONE_SPEED_MS = 16.7       # ~60 km/h quadcopter cruise

    print("Emergency scenario in Coimbatore\n")

    emergency_site = geocode("Mettupalayam, Tamil Nadu")
    hospital = geocode("Coimbatore Medical College Hospital, Coimbatore")
    drone_base = geocode("Gandhipuram, Coimbatore")

    print(f"  Emergency at:   {emergency_site}")
    print(f"  Ambulance from: {hospital}")
    print(f"  Drone based at: {drone_base}")

    print("\nFetching ambulance road route...")
    route = get_route(hospital, emergency_site)
    print(f"  Route: {route['distance_m'] / 1000:.2f} km, "
          f"{len(route['waypoints'])} waypoints")

    timeline = build_ambulance_timeline(route["waypoints"], AMBULANCE_SPEED_MS)
    total_trip_s = timeline[-1][0]
    print(f"  Ambulance ETA: {format_countdown(total_trip_s)}")

    print("\nComputing rendezvous...")
    result = find_rendezvous(drone_base, DRONE_SPEED_MS, timeline)

    if result is None:
        print("  No rendezvous possible.")
    else:
        print(f"  Meeting point: {result['meeting_point']}")
        print(f"  Countdown:     {format_countdown(result['countdown_s'])}")
        print(f"  Drone flies:   {result['drone_distance_m'] / 1000:.2f} km "
              f"in {format_countdown(result['drone_flight_time_s'])}")
        print(f"  Slack:         {result['drone_slack_s']:.1f} s to spare")

        escort_s = total_trip_s - result["countdown_s"]
        print(f"\n  Escort covers {format_countdown(escort_s)} of the journey.")