"""
Traffic model for the ambulance.

Real ambulances do not hold a constant speed. They accelerate on clear
stretches, slow through junctions, and get held up in traffic. Modelling
this matters because it is the reason the drone cannot precompute a fixed
intercept point and fly to it: the target's arrival time keeps changing,
so the intercept has to be re-solved continuously from live position.

Speed follows a mean-reverting random walk (Ornstein-Uhlenbeck): it drifts
back toward a cruise speed but wanders, with occasional traffic holds. The
generator is seeded so runs are reproducible, which the accuracy harness
depends on.
"""

import random


class TrafficModel:
    def __init__(self, cruise_kmh: float = 75.0, min_kmh: float = 25.0,
                 max_kmh: float = 110.0, hold_probability: float = 0.02,
                 seed: int = None):
        self.cruise_ms = cruise_kmh / 3.6
        self.min_ms = min_kmh / 3.6
        self.max_ms = max_kmh / 3.6
        self.hold_probability = hold_probability
        self.rng = random.Random(seed)

        self.speed_ms = self.cruise_ms
        self.hold_remaining_s = 0.0

    def step(self, dt_s: float) -> float:
        """Advance the model and return the current speed in m/s."""
        if self.hold_remaining_s > 0:
            self.hold_remaining_s -= dt_s
            self.speed_ms = max(self.min_ms, self.speed_ms - 4.0 * dt_s)
            return self.speed_ms

        if self.rng.random() < self.hold_probability * dt_s:
            self.hold_remaining_s = self.rng.uniform(5.0, 20.0)
            return self.speed_ms

        reversion = 0.15 * (self.cruise_ms - self.speed_ms) * dt_s
        noise = self.rng.gauss(0, 1.2) * dt_s
        self.speed_ms += reversion + noise
        self.speed_ms = max(self.min_ms, min(self.max_ms, self.speed_ms))
        return self.speed_ms


def required_dash_speed(distance_m: float, deadline_s: float,
                        airframe_max_ms: float) -> dict:
    """
    Work out how fast the drone must fly to hit the response deadline, and
    whether the airframe can actually do it.
    """
    required_ms = distance_m / deadline_s
    achievable = required_ms <= airframe_max_ms

    return {
        "required_ms": required_ms,
        "required_kmh": required_ms * 3.6,
        "achievable": achievable,
        "commanded_ms": min(required_ms, airframe_max_ms),
        "actual_arrival_s": distance_m / min(required_ms, airframe_max_ms),
        "deadline_s": deadline_s,
        "margin_s": deadline_s - distance_m / min(required_ms, airframe_max_ms),
    }