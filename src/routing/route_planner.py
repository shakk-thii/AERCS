"""
Phase 2 — Route planning using free, open-source services.

Uses OSRM (routing) and Nominatim (geocoding), both from the OpenStreetMap
ecosystem. No API key, no billing account.

Attribution required by both services:
  Routing: OSRM (router.project-osrm.org)
  Map data: OpenStreetMap contributors, ODbL license
"""

import time
import requests

OSRM_BASE = "https://router.project-osrm.org"
NOMINATIM_BASE = "https://nominatim.openstreetmap.org"

# Both services require a real User-Agent identifying the application.
# Faking another app's User-Agent will get you blocked.
HEADERS = {"User-Agent": "AERCS/1.0 (student research project)"}

# Both services ask for max 1 request/second. We enforce it here.
_last_request_time = 0.0


def _rate_limit():
    """Ensure at least 1 second between outgoing requests."""
    global _last_request_time
    elapsed = time.time() - _last_request_time
    if elapsed < 1.0:
        time.sleep(1.0 - elapsed)
    _last_request_time = time.time()


def geocode(place_name: str) -> tuple:
    """
    Convert a place name into (latitude, longitude).

    Example: geocode("Coimbatore Medical College Hospital")
             -> (11.0168, 76.9558)
    """
    _rate_limit()
    response = requests.get(
        f"{NOMINATIM_BASE}/search",
        params={"q": place_name, "format": "json", "limit": 1},
        headers=HEADERS,
        timeout=10,
    )
    response.raise_for_status()
    results = response.json()

    if not results:
        raise ValueError(f"Could not geocode: {place_name}")

    return float(results[0]["lat"]), float(results[0]["lon"])


def get_route(start: tuple, end: tuple) -> dict:
    """
    Get the driving route between two (lat, lon) points.

    Returns a dict with:
      distance_m: total road distance in metres
      duration_s: estimated driving time in seconds
      waypoints:  list of (lat, lon) tuples tracing the road path
    """
    # OSRM expects lon,lat order in the URL - the reverse of how we
    # normally write coordinates. Getting this backwards is the most
    # common bug in geospatial code.
    coords = f"{start[1]},{start[0]};{end[1]},{end[0]}"

    _rate_limit()
    response = requests.get(
        f"{OSRM_BASE}/route/v1/driving/{coords}",
        params={"overview": "full", "geometries": "geojson"},
        headers=HEADERS,
        timeout=10,
    )
    response.raise_for_status()
    data = response.json()

    if data.get("code") != "Ok":
        raise RuntimeError(f"OSRM returned: {data.get('code')}")

    route = data["routes"][0]

    # GeoJSON gives [lon, lat]; flip each pair to our (lat, lon) convention.
    waypoints = [(point[1], point[0])
                 for point in route["geometry"]["coordinates"]]

    return {
        "distance_m": route["distance"],
        "duration_s": route["duration"],
        "waypoints": waypoints,
    }


def haversine_m(point_a: tuple, point_b: tuple) -> float:
    """
    Straight-line ("as the drone flies") distance in metres between two
    (lat, lon) points, accounting for the curvature of the Earth.

    Phase 3 needs this to compare the drone's direct flight path against
    the ambulance's road distance.
    """
    from math import radians, sin, cos, asin, sqrt

    lat1, lon1 = radians(point_a[0]), radians(point_a[1])
    lat2, lon2 = radians(point_b[0]), radians(point_b[1])

    delta_lat = lat2 - lat1
    delta_lon = lon2 - lon1

    a = sin(delta_lat / 2) ** 2 + cos(lat1) * cos(lat2) * sin(delta_lon / 2) ** 2
    earth_radius_m = 6371000
    return 2 * earth_radius_m * asin(sqrt(a))


if __name__ == "__main__":
    print("Geocoding two locations in Coimbatore...")
    hospital = geocode("Coimbatore Medical College Hospital, Coimbatore")
    print(f"  Hospital: {hospital}")

    airport = geocode("Coimbatore International Airport")
    print(f"  Airport:  {airport}")

    print("\nFetching driving route between them...")
    route = get_route(hospital, airport)

    print(f"  Road distance:    {route['distance_m'] / 1000:.2f} km")
    print(f"  Driving time:     {route['duration_s'] / 60:.1f} minutes")
    print(f"  Waypoints:        {len(route['waypoints'])} points")
    print(f"  First waypoint:   {route['waypoints'][0]}")
    print(f"  Last waypoint:    {route['waypoints'][-1]}")

    direct = haversine_m(hospital, airport)
    print(f"\n  Straight-line (drone): {direct / 1000:.2f} km")
    print(f"  Road (ambulance):      {route['distance_m'] / 1000:.2f} km")
    print(f"  The drone's path is {route['distance_m'] / direct:.2f}x shorter.")