using UnityEngine;

// Direct port of traffic.py - ambulance speed does not stay constant.
// It drifts toward a cruise speed but wanders, with occasional holds
// (junctions, congestion). Same mean-reverting random walk as the
// Python version, seeded for reproducibility.
public class TrafficModel
{
    private float cruiseMs;
    private float minMs;
    private float maxMs;
    private float holdProbability;
    private System.Random rng;

    public float speedMs;
    private float holdRemainingS;

    public TrafficModel(float cruiseKmh = 75f, float minKmh = 25f,
                        float maxKmh = 110f, float holdProbability = 0.02f,
                        int seed = 0)
    {
        cruiseMs = cruiseKmh / 3.6f;
        minMs = minKmh / 3.6f;
        maxMs = maxKmh / 3.6f;
        this.holdProbability = holdProbability;
        rng = new System.Random(seed);

        speedMs = cruiseMs;
        holdRemainingS = 0f;
    }

    private float NextGaussian()
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1))
                       * System.Math.Sin(2.0 * System.Math.PI * u2));
    }

    public float Step(float dtS)
    {
        if (holdRemainingS > 0f)
        {
            holdRemainingS -= dtS;
            speedMs = Mathf.Max(minMs, speedMs - 4f * dtS);
            return speedMs;
        }

        if (rng.NextDouble() < holdProbability * dtS)
        {
            holdRemainingS = Mathf.Lerp(5f, 20f, (float)rng.NextDouble());
            return speedMs;
        }

        float reversion = 0.15f * (cruiseMs - speedMs) * dtS;
        float noise = NextGaussian() * 1.2f * dtS;
        speedMs += reversion + noise;
        speedMs = Mathf.Clamp(speedMs, minMs, maxMs);
        return speedMs;
    }
}