"""
Phase 4 — Live AERCS mission dashboard.

Runs the full mission and serves a live map showing the drone (flying in
the PX4 simulator), the ambulance (simulated on a real road route), the
rendezvous point, and a live countdown.

Start the PX4 container first, then run this and open http://localhost:5000
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
from rendezvous.rendezvous_engine import build_ambulance_timeline, find_rendezvous

app = Flask(__name__)

AMBULANCE_SPEED_MS = 13.9
DRONE_SPEED_MS = 16.7
TIME_SCALE = 20  # Run the sim 20x real time so a 54-min trip takes ~3 min

state = {
    "status": "starting",
    "drone": None,
    "ambulance": None,
    "route": [],
    "rendezvous": None,
    "countdown_s": None,
    "message": "Initialising...",
}


def ambulance_position_at(timeline, elapsed_s):
    """Interpolate the ambulance's position at a given time."""
    if elapsed_s >= timeline[-1][0]:
        return timeline[-1][1]

    for i in range(1, len(timeline)):
        if timeline[i][0] >= elapsed_s:
            t0, p0 = timeline[i - 1]
            t1, p1 = timeline[i]
            span = t1 - t0
            f = 0 if span == 0 else (elapsed_s - t0) / span
            return (p0[0] + (p1[0] - p0[0]) * f,
                    p0[1] + (p1[1] - p0[1]) * f)
    return timeline[0][1]


async def run_mission():
    state["message"] = "Planning route..."

    emergency = geocode("Mettupalayam, Tamil Nadu")
    hospital = geocode("Coimbatore Medical College Hospital, Coimbatore")
    drone_base = geocode("Gandhipuram, Coimbatore")

    route = get_route(hospital, emergency)
    state["route"] = route["waypoints"]
    timeline = build_ambulance_timeline(route["waypoints"], AMBULANCE_SPEED_MS)

    result = find_rendezvous(drone_base, DRONE_SPEED_MS, timeline)
    if result is None:
        state["status"] = "no-rendezvous"
        state["message"] = "Drone cannot intercept in time."
        return

    state["rendezvous"] = result["meeting_point"]
    state["message"] = "Connecting to drone..."

    controller = DroneController()
    await controller.connect()

    telemetry = await controller.get_telemetry()
    sim_home = (telemetry["latitude"], telemetry["longitude"])

    state["message"] = "Launching drone..."
    state["status"] = "in-flight"
    await controller.arm_and_takeoff(altitude_m=30)

    # Fly the simulated drone the same offset the real mission requires.
    target_lat = sim_home[0] + (result["meeting_point"][0] - drone_base[0])
    target_lon = sim_home[1] + (result["meeting_point"][1] - drone_base[1])
    await controller.goto(target_lat, target_lon, altitude_m=30)

    mission_start = time.time()

    while True:
        elapsed = (time.time() - mission_start) * TIME_SCALE
        telemetry = await controller.get_telemetry()

        # Map the simulator's real movement onto the Coimbatore scenario.
        state["drone"] = {
            "lat": drone_base[0] + (telemetry["latitude"] - sim_home[0]),
            "lon": drone_base[1] + (telemetry["longitude"] - sim_home[1]),
            "altitude": round(telemetry["altitude"], 1),
            "speed": round(telemetry["speed_ms"], 1),
        }

        ambulance = ambulance_position_at(timeline, elapsed)
        state["ambulance"] = {"lat": ambulance[0], "lon": ambulance[1]}

        remaining = result["countdown_s"] - elapsed
        state["countdown_s"] = max(0, round(remaining, 1))

        if remaining <= 0:
            state["status"] = "escorting"
            state["message"] = "Rendezvous reached - escorting ambulance."
        else:
            state["message"] = "Drone en route to rendezvous point."

        if elapsed > timeline[-1][0]:
            state["status"] = "complete"
            state["message"] = "Ambulance arrived at emergency site."
            await controller.land()
            return

        await asyncio.sleep(0.5)


def start_mission_thread():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    try:
        loop.run_until_complete(run_mission())
    except Exception as error:
        state["status"] = "error"
        state["message"] = f"Mission error: {error}"


@app.route("/")
def index():
    return render_template("index.html")


@app.route("/api/state")
def api_state():
    return jsonify(state)


if __name__ == "__main__":
    threading.Thread(target=start_mission_thread, daemon=True).start()
    app.run(host="127.0.0.1", port=5000, debug=False)