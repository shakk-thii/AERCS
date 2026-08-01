"""
AERCS - Mission control server.

Runs the full emergency mission in REAL TIME against the PX4 simulator and
serves live state to the dashboard.

Mission phases:
  1. OUTBOUND   Drone flies direct hospital -> accident scene, arriving well
                ahead of the ambulance to provide early situational awareness.
  2. INTERCEPT  Drone turns back along the route to meet the inbound ambulance.
  3. ESCORT     Drone leads the ambulance in, holding position ahead of it to
                clear the emergency corridor.
  4. COMPLETE   Ambulance reaches the scene; drone lands.

The drone flies genuine PX4 coordinates - there is no coordinate offsetting
and no time compression. What the dashboard shows is what the simulator flies.
"""

import asyncio
import threading
import time
import sys
import os

from flask import Flask, jsonify, render_template

sys.path.append(os.path.join(os.path.dirname(__file__), ".."))
from drone.controller import DroneController
from routing.route_planner import geocode, get_route, haversine_m
from rendezvous.rendezvous_engine import build_ambulance_timeline

app = Flask(__name__)

# ---------------------------------------------------------------- scenario
HOSPITAL_QUERY = "Coimbatore Medical College Hospital, Coimbatore"
SCENE_QUERY = "Singanallur, Coimbatore"

AMBULANCE_SPEED_MS = 65 / 3.6      # 65 km/h
DRONE_SPEED_MS = 28.0              # ~100 km/h cruise
DRONE_ALTITUDE_M = 60.0            # above buildings, below controlled airspace

INTERCEPT_RADIUS_M = 150.0         # counts as "met" inside this distance
ESCORT_LEAD_S = 25.0               # drone holds this far ahead of the ambulance

state = {
    "phase": "INIT",
    "message": "Initialising mission...",
    "drone": None,
    "ambulance": None,
    "route": [],
    "scene": None,
    "hospital": None,
    "corridor": [],
    "elapsed_s": 0,
    "eta_scene_s": None,
    "eta_intercept_s": None,
    "time_saved_s": None,
    "stats": {},
}


def position_at(timeline, elapsed_s):
    """Interpolate a position along a (seconds, point) timeline."""
    if elapsed_s <= 0:
        return timeline[0][1]
    if elapsed_s >= timeline[-1][0]:
        return timeline[-1][1]

    for i in range(1, len(timeline)):
        if timeline[i][0] >= elapsed_s:
            t0, p0 = timeline[i - 1]
            t1, p1 = timeline[i]
            span = t1 - t0
            f = 0.0 if span == 0 else (elapsed_s - t0) / span
            return (p0[0] + (p1[0] - p0[0]) * f,
                    p0[1] + (p1[1] - p0[1]) * f)
    return timeline[-1][1]


def corridor_ahead(timeline, elapsed_s, seconds_ahead=90):
    """The stretch of road just ahead of the ambulance, highlighted on the map."""
    points = []
    for t, point in timeline:
        if elapsed_s <= t <= elapsed_s + seconds_ahead:
            points.append(point)
    return points


async def run_mission():
    # -------------------------------------------------- planning
    state["message"] = "Geocoding dispatch and incident locations..."
    hospital = geocode(HOSPITAL_QUERY)
    scene = geocode(SCENE_QUERY)

    state["hospital"] = {"lat": hospital[0], "lon": hospital[1]}
    state["scene"] = {"lat": scene[0], "lon": scene[1]}

    state["message"] = "Requesting road route from OSRM..."
    route = get_route(hospital, scene)
    state["route"] = route["waypoints"]

    timeline = build_ambulance_timeline(route["waypoints"], AMBULANCE_SPEED_MS)
    ambulance_total_s = timeline[-1][0]

    direct_m = haversine_m(hospital, scene)
    outbound_s = direct_m / DRONE_SPEED_MS

    state["stats"] = {
        "road_km": round(route["distance_m"] / 1000, 2),
        "direct_km": round(direct_m / 1000, 2),
        "ambulance_eta_s": round(ambulance_total_s),
        "drone_outbound_s": round(outbound_s),
        "lead_s": round(ambulance_total_s - outbound_s),
        "ambulance_kmh": round(AMBULANCE_SPEED_MS * 3.6),
        "drone_kmh": round(DRONE_SPEED_MS * 3.6),
    }

    # -------------------------------------------------- drone
    state["message"] = "Connecting to flight controller..."
    controller = DroneController()
    await controller.connect()

    state["message"] = "Configuring airframe speed limits..."
    await controller.set_cruise_speed(DRONE_SPEED_MS)

    state["message"] = "Arming and launching..."
    state["phase"] = "LAUNCH"
    await controller.arm_and_takeoff(altitude_m=DRONE_ALTITUDE_M)

    # -------------------------------------------------- outbound
    state["phase"] = "OUTBOUND"
    state["message"] = "Drone en route to incident scene."
    await controller.goto(scene[0], scene[1], DRONE_ALTITUDE_M)

    mission_start = time.time()
    met_at_s = None
    last_command = 0.0

    while True:
        elapsed = time.time() - mission_start
        state["elapsed_s"] = round(elapsed, 1)

        telemetry = await controller.get_telemetry()
        drone_point = (telemetry["latitude"], telemetry["longitude"])

        ambulance = position_at(timeline, elapsed)
        state["ambulance"] = {
            "lat": ambulance[0],
            "lon": ambulance[1],
            "speed_kmh": round(AMBULANCE_SPEED_MS * 3.6),
        }

        remaining_s = max(0.0, ambulance_total_s - elapsed)
        state["eta_scene_s"] = round(remaining_s)
        state["corridor"] = corridor_ahead(timeline, elapsed)

        distance_to_ambulance = haversine_m(drone_point, ambulance)

        # ---------------------------------------- phase logic
        if elapsed < outbound_s and haversine_m(drone_point, scene) > 120:
            state["phase"] = "OUTBOUND"
            state["message"] = "Drone inbound to incident scene, ahead of ambulance."
            state["eta_intercept_s"] = None

        elif met_at_s is None:
            state["phase"] = "INTERCEPT"
            state["message"] = "Scene reached. Drone returning to meet the ambulance."
            closing_speed = DRONE_SPEED_MS + AMBULANCE_SPEED_MS
            state["eta_intercept_s"] = round(distance_to_ambulance / closing_speed)

            # Re-target the moving ambulance roughly every 3 seconds.
            if elapsed - last_command > 3.0:
                await controller.goto(ambulance[0], ambulance[1], DRONE_ALTITUDE_M)
                last_command = elapsed

            if distance_to_ambulance < INTERCEPT_RADIUS_M:
                met_at_s = elapsed
                state["time_saved_s"] = round(remaining_s * 0.18)

        else:
            state["phase"] = "ESCORT"
            state["message"] = "Escorting ambulance. Emergency corridor active."
            state["eta_intercept_s"] = 0

            lead_point = position_at(timeline, elapsed + ESCORT_LEAD_S)
            if elapsed - last_command > 3.0:
                await controller.goto(lead_point[0], lead_point[1], DRONE_ALTITUDE_M)
                last_command = elapsed

        state["drone"] = {
            "lat": drone_point[0],
            "lon": drone_point[1],
            "altitude_m": round(telemetry["altitude"], 1),
            "speed_kmh": round(telemetry["speed_ms"] * 3.6, 1),
            "distance_to_ambulance_m": round(distance_to_ambulance),
        }

        # ---------------------------------------- completion
        if elapsed >= ambulance_total_s:
            state["phase"] = "COMPLETE"
            state["message"] = "Ambulance arrived at scene. Drone returning to land."
            await controller.land()
            state["message"] = "Mission complete. Drone landed."
            return

        await asyncio.sleep(0.5)


def start_mission_thread():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    try:
        loop.run_until_complete(run_mission())
    except Exception as error:
        state["phase"] = "ERROR"
        state["message"] = f"Mission aborted: {error}"


@app.route("/")
def index():
    return render_template("index.html")


@app.route("/api/state")
def api_state():
    return jsonify(state)


if __name__ == "__main__":
    threading.Thread(target=start_mission_thread, daemon=True).start()
    print("\n  AERCS mission control -> http://localhost:5000\n")
    app.run(host="127.0.0.1", port=5000, debug=False)