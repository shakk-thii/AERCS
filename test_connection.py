"""
Phase 0 verification script.

Connects to a running PX4 SITL instance (the simulated drone) and prints
its live position and battery level. If this runs successfully, it proves
the whole chain works: simulator -> PX4 -> MAVSDK -> your Python code.

Run this AFTER starting the PX4 SITL docker container (see README.md).
"""

import asyncio
from mavsdk import System


async def main():
    drone = System()

    # udp://:14540 is PX4's standard offboard-API port (confirmed against
    # PX4's own docs, not guessed) — this is where MAVSDK listens for the
    # simulated drone's MAVLink stream.
    print("Connecting to drone on udp://:14540 ...")
    await drone.connect(system_address="udp://:14540")

    print("Waiting for drone to be discovered...")
    async for state in drone.core.connection_state():
        if state.is_connected:
            print("-> Drone discovered!")
            break

    print("Waiting for the drone's GPS/home position to be ready...")
    async for health in drone.telemetry.health():
        if health.is_global_position_ok and health.is_home_position_ok:
            print("-> GPS and home position OK, drone is ready.")
            break

    # Grab a single telemetry snapshot to prove data is actually flowing.
    position = await anext(drone.telemetry.position())
    battery = await anext(drone.telemetry.battery())

    print("\n--- Live telemetry snapshot ---")
    print(f"Latitude:  {position.latitude_deg}")
    print(f"Longitude: {position.longitude_deg}")
    print(f"Altitude:  {position.relative_altitude_m} m (relative to home)")
    print(f"Battery:   {battery.remaining_percent * 100:.1f}%")
    print("--------------------------------")
    print("\nPhase 0 complete: MAVSDK is successfully talking to the "
          "simulated drone.")


if __name__ == "__main__":
    asyncio.run(main())
