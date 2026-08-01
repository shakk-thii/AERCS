import asyncio
from mavsdk import System


async def main():
    drone = System()
    print("Connecting on udpin://0.0.0.0:14540 ...")
    await drone.connect(system_address="udpin://0.0.0.0:14540")

    print("Waiting for drone...")
    async for state in drone.core.connection_state():
        if state.is_connected:
            print("-> Drone discovered!")
            break

    print("Waiting for GPS lock...")
    async for health in drone.telemetry.health():
        if health.is_global_position_ok and health.is_home_position_ok:
            print("-> GPS OK.")
            break

    position = await anext(drone.telemetry.position())
    battery = await anext(drone.telemetry.battery())

    print("\n--- Telemetry ---")
    print(f"Lat:      {position.latitude_deg}")
    print(f"Lon:      {position.longitude_deg}")
    print(f"Altitude: {position.relative_altitude_m} m")
    print(f"Battery:  {battery.remaining_percent * 100:.1f}%")
    print("-----------------")
    print("\nPhase 0 complete.")


if __name__ == "__main__":
    asyncio.run(main())