"""Phase 1 — wraps MAVSDK into clean functions for the rest of AERCS."""

import asyncio
from mavsdk import System


class DroneController:
    def __init__(self, connection_string: str = "udpin://0.0.0.0:14540"):
        self.connection_string = connection_string
        self.drone = System()
        self._connected = False

    async def connect(self):
        await self.drone.connect(system_address=self.connection_string)

        print("Waiting for drone...")
        async for state in self.drone.core.connection_state():
            if state.is_connected:
                break

        print("Waiting for GPS lock...")
        async for health in self.drone.telemetry.health():
            if health.is_global_position_ok and health.is_home_position_ok:
                break

        self._connected = True
        print("Drone connected and ready.")

    async def arm_and_takeoff(self, altitude_m: float = 10.0):
        if not self._connected:
            raise RuntimeError("Call connect() first.")

        print("Arming...")
        await self.drone.action.arm()

        print(f"Taking off to {altitude_m}m...")
        await self.drone.action.set_takeoff_altitude(altitude_m)
        await self.drone.action.takeoff()

        async for position in self.drone.telemetry.position():
            if position.relative_altitude_m >= altitude_m * 0.95:
                break
        print("Takeoff complete.")

    async def goto(self, latitude: float, longitude: float, altitude_m: float = 10.0):
        """Fly to a GPS coordinate using PX4's built-in goto."""
        await self.drone.action.goto_location(
            latitude, longitude, altitude_m, 0
        )

    async def get_telemetry(self) -> dict:
        position = await anext(self.drone.telemetry.position())
        battery = await anext(self.drone.telemetry.battery())
        velocity = await anext(self.drone.telemetry.velocity_ned())

        return {
            "latitude": position.latitude_deg,
            "longitude": position.longitude_deg,
            "altitude": position.relative_altitude_m,
            "battery_percent": battery.remaining_percent * 100,
            "speed_ms": (velocity.north_m_s ** 2 + velocity.east_m_s ** 2) ** 0.5,
        }

    async def land(self):
        print("Landing...")
        await self.drone.action.land()

        async for in_air in self.drone.telemetry.in_air():
            if not in_air:
                break
        print("Landed.")


async def _demo():
    controller = DroneController()
    await controller.connect()
    await controller.arm_and_takeoff(altitude_m=10)

    telemetry = await controller.get_telemetry()
    print("Position:", telemetry)

    print("Flying north ~100m...")
    await controller.goto(
        telemetry["latitude"] + 0.0009,
        telemetry["longitude"],
        altitude_m=10,
    )
    await asyncio.sleep(20)

    await controller.land()


if __name__ == "__main__":
    asyncio.run(_demo())