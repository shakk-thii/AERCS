"""
Drone control module.

Wraps MAVSDK into a small, clean interface so the rest of AERCS never
touches MAVLink or MAVSDK directly. This is the only module that talks
to PX4.
"""

import asyncio
from mavsdk import System


class DroneController:
    def __init__(self, connection_string: str = "udpin://0.0.0.0:14540"):
        self.connection_string = connection_string
        self.drone = System()
        self._connected = False

    async def connect(self):
        await self.drone.connect(system_address=self.connection_string)

        async for st in self.drone.core.connection_state():
            if st.is_connected:
                break

        async for health in self.drone.telemetry.health():
            if health.is_global_position_ok and health.is_home_position_ok:
                break

        self._connected = True

    async def set_cruise_speed(self, speed_ms: float):
        """
        Raise PX4's speed limits so the drone can actually fly fast.

        PX4 ships with conservative defaults (around 12 m/s horizontally).
        An emergency-response airframe would be tuned for speed, so we set
        both the hard limit and the cruise setpoint used by goto commands.
        """
        try:
            await self.drone.param.set_param_float("MPC_XY_VEL_MAX", speed_ms)
            await self.drone.param.set_param_float("MPC_XY_CRUISE", speed_ms)
        except Exception as error:
            # Not fatal - the mission still flies, just slower.
            print(f"  (could not raise speed limits: {error})")

    async def arm_and_takeoff(self, altitude_m: float = 30.0):
        if not self._connected:
            raise RuntimeError("Call connect() before arm_and_takeoff().")

        await self.drone.action.arm()
        await self.drone.action.set_takeoff_altitude(altitude_m)
        await self.drone.action.takeoff()

        async for position in self.drone.telemetry.position():
            if position.relative_altitude_m >= altitude_m * 0.95:
                break

    async def goto(self, latitude: float, longitude: float,
                   altitude_m: float = 30.0):
        """Command a flight to a GPS coordinate. Returns immediately."""
        await self.drone.action.goto_location(
            latitude, longitude, altitude_m, float("nan")
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
        await self.drone.action.land()
        async for in_air in self.drone.telemetry.in_air():
            if not in_air:
                break


async def _demo():
    controller = DroneController()
    print("Connecting...")
    await controller.connect()
    print("Connected.")

    await controller.set_cruise_speed(28.0)
    await controller.arm_and_takeoff(altitude_m=40)
    print("Airborne.")

    telemetry = await controller.get_telemetry()
    print("Telemetry:", telemetry)

    await controller.goto(telemetry["latitude"] + 0.009,
                          telemetry["longitude"], 40)
    await asyncio.sleep(30)

    await controller.land()
    print("Landed.")


if __name__ == "__main__":
    asyncio.run(_demo())