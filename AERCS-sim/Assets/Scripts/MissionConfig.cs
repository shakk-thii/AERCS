using UnityEngine;

/// <summary>
/// Single source of truth for every mission parameter.
///
/// Kept as a plain [System.Serializable] class rather than scattered public
/// fields so that tuning happens in one Inspector block, and so the same
/// values can be handed to the HUD, the camera rig, and the flight logic
/// without any of them owning the numbers.
///
/// Distances are in Unity units, treated as metres. The route length is
/// deliberately compressed relative to real Coimbatore distances so the
/// full mission is watchable in about forty seconds; the SPEED RATIOS are
/// what matter and those are preserved.
/// </summary>
[System.Serializable]
public class MissionConfig
{
    [Header("Route")]
    [Tooltip("Distance from the dispatch hospital to the incident scene.")]
    public float routeLength = 300f;

    [Header("Dispatch timing")]
    [Tooltip("The drone launches immediately. The ambulance is held at the " +
             "hospital for this long, so the drone is already airborne and " +
             "inbound before the ambulance rolls.")]
    public float ambulanceDispatchDelay = 10f;

    [Header("Drone flight envelope")]
    public float dashSpeed = 20f;        // outbound cruise, hospital -> scene
    public float corridorSpeed = 8f;     // clearing sweep, scene -> ambulance
    public float climbRate = 4f;
    public float descentRate = 3f;
    public float dashAltitude = 12f;     // high cruise, clear of buildings
    public float corridorAltitude = 3f;  // low sweep, visible to traffic

    [Header("Ambulance")]
    public float ambulanceCruiseKmh = 22f;
    public float ambulanceMinKmh = 10f;
    public float ambulanceMaxKmh = 35f;
    public int trafficSeed = 42;

    [Header("Rendezvous")]
    [Tooltip("Separation at which the drone and ambulance count as met.")]
    public float captureRadius = 6f;

    /// <summary>Total time for climb, outbound dash, and descent.</summary>
    public float PredictedCorridorReadyTime =>
        dashAltitude / climbRate
        + routeLength / dashSpeed
        + (dashAltitude - corridorAltitude) / descentRate;

    /// <summary>Time the ambulance would need for the whole route unimpeded.</summary>
    public float PredictedAmbulanceTransit =>
        routeLength / (ambulanceCruiseKmh / 3.6f);
}