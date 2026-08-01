using UnityEngine;

/// <summary>
/// The AERCS mission state machine.
///
/// Runs in one of two modes:
///
///   SIMULATED       Unity moves the drone using the flight model ported
///                   from the validated Python code. Self-contained; needs
///                   nothing else running.
///
///   PX4_TELEMETRY   A real PX4 autopilot flies the aircraft. Python
///                   commands it over MAVSDK; this scene reads the
///                   resulting state from MavlinkBridge and renders where
///                   PX4 actually put it. Phase is then INFERRED from
///                   observed motion rather than commanded, because in this
///                   mode Unity is an observer, not the pilot.
///
/// Keeping both in one class is deliberate: the mission logic, the metrics,
/// and the dashboard are identical either way, so switching modes changes
/// only where the drone's position comes from.
/// </summary>
public class MissionController : MonoBehaviour
{
    public enum Phase { IDLE, CLIMB, DASH, DESCEND, CORRIDOR, MET }
    public enum FlightSource { Simulated, PX4Telemetry }

    [Header("Configuration")]
    public MissionConfig config = new MissionConfig();

    [Header("Flight source")]
    public FlightSource flightSource = FlightSource.Simulated;
    [Tooltip("If the PX4 link drops, fall back to the simulated model " +
             "rather than freezing the aircraft mid-air.")]
    public bool fallBackIfLinkLost = true;
    public MavlinkBridge bridge;

    [Header("Scene references")]
    public Transform drone;
    public Transform ambulance;

    public Phase CurrentPhase { get; private set; } = Phase.IDLE;
    public float ElapsedTime { get; private set; }
    public float DroneSweptDistance { get; private set; }
    public float AmbulanceDistance { get; private set; }
    public float AmbulanceSpeedKmh { get; private set; }
    public float SeparationDistance { get; private set; }
    public bool AmbulanceDispatched { get; private set; }
    public float MetAtTime { get; private set; } = -1f;
    public bool RunningOnPX4 { get; private set; }

    public float CorridorClearedFraction =>
        config.routeLength <= 0f ? 0f : DroneSweptDistance / config.routeLength;

    private TrafficModel traffic;
    private Vector3 hospitalGround;
    private Vector3 sceneGround;
    private bool reachedScene;

    public void Begin()
    {
        traffic = new TrafficModel(
            cruiseKmh: config.ambulanceCruiseKmh,
            minKmh: config.ambulanceMinKmh,
            maxKmh: config.ambulanceMaxKmh,
            seed: config.trafficSeed);

        hospitalGround = Vector3.zero;
        sceneGround = new Vector3(0f, 0f, config.routeLength);

        ElapsedTime = 0f;
        DroneSweptDistance = 0f;
        AmbulanceDistance = 0f;
        AmbulanceDispatched = false;
        MetAtTime = -1f;
        reachedScene = false;

        if (ambulance != null) ambulance.position = hospitalGround;
        if (drone != null) drone.position = hospitalGround;

        CurrentPhase = Phase.CLIMB;
    }

    private void Update()
    {
        if (CurrentPhase == Phase.IDLE || drone == null || ambulance == null)
            return;

        float dt = Time.deltaTime;
        ElapsedTime += dt;

        AdvanceAmbulance(dt);

        bool linkLive = flightSource == FlightSource.PX4Telemetry
                        && bridge != null && bridge.linkUp;
        RunningOnPX4 = linkLive;

        if (linkLive)
            FollowPX4(dt);
        else if (flightSource == FlightSource.Simulated || fallBackIfLinkLost)
            FlySimulated(dt);

        SeparationDistance = Vector3.Distance(
            new Vector3(drone.position.x, 0f, drone.position.z),
            new Vector3(ambulance.position.x, 0f, ambulance.position.z));
    }

    /// <summary>
    /// The ambulance is held at the hospital for the configured delay, then
    /// drives continuously. It is a separate vehicle with its own dispatch,
    /// not something that waits for the drone to finish manoeuvring.
    /// </summary>
    private void AdvanceAmbulance(float dt)
    {
        if (ElapsedTime < config.ambulanceDispatchDelay)
        {
            AmbulanceSpeedKmh = 0f;
            return;
        }

        AmbulanceDispatched = true;

        float speed = traffic.Step(dt);
        AmbulanceSpeedKmh = speed * 3.6f;
        AmbulanceDistance = Mathf.Min(config.routeLength,
                                      AmbulanceDistance + speed * dt);

        Vector3 pos = Vector3.Lerp(hospitalGround, sceneGround,
                                   AmbulanceDistance / config.routeLength);
        pos.y = ambulance.position.y;
        ambulance.position = pos;
        ambulance.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    }

    // ---------------------------------------------------------------- PX4

    /// <summary>
    /// Render whatever PX4 reports, and work out which phase that implies.
    /// Nothing here commands the aircraft; the Python mission script does
    /// that over MAVSDK on the other port.
    /// </summary>
    private void FollowPX4(float dt)
    {
        drone.position = bridge.UnityPosition;
        drone.rotation = bridge.UnityRotation;

        float altitude = drone.position.y;
        float alongRoute = Mathf.Clamp(drone.position.z, 0f, config.routeLength);

        if (!reachedScene)
        {
            if (altitude < config.dashAltitude * 0.9f && alongRoute < 5f)
                CurrentPhase = Phase.CLIMB;
            else if (alongRoute < config.routeLength - 10f)
                CurrentPhase = Phase.DASH;
            else if (altitude > config.corridorAltitude + 1f)
                CurrentPhase = Phase.DESCEND;
            else
                reachedScene = true;
        }

        if (reachedScene && CurrentPhase != Phase.MET)
        {
            CurrentPhase = Phase.CORRIDOR;
            DroneSweptDistance = Mathf.Clamp(config.routeLength - alongRoute,
                                             0f, config.routeLength);

            float gap = config.routeLength - AmbulanceDistance - DroneSweptDistance;
            if (gap <= config.captureRadius)
            {
                MetAtTime = ElapsedTime;
                CurrentPhase = Phase.MET;
            }
        }
    }

    // ---------------------------------------------------------- simulated

    private void FlySimulated(float dt)
    {
        switch (CurrentPhase)
        {
            case Phase.CLIMB: Climb(dt); break;
            case Phase.DASH: Dash(dt); break;
            case Phase.DESCEND: Descend(dt); break;
            case Phase.CORRIDOR: Corridor(dt); break;
            case Phase.MET: Escort(dt); break;
        }
    }

    private void Climb(float dt)
    {
        Vector3 p = drone.position;
        p.y = Mathf.Min(config.dashAltitude, p.y + config.climbRate * dt);
        drone.position = p;

        if (p.y >= config.dashAltitude - 0.05f) CurrentPhase = Phase.DASH;
    }

    private void Dash(float dt)
    {
        Vector3 target = new Vector3(sceneGround.x, config.dashAltitude, sceneGround.z);
        drone.position = Vector3.MoveTowards(drone.position, target,
                                             config.dashSpeed * dt);
        FaceTravel(Vector3.forward);

        if (Vector3.Distance(drone.position, target) < 0.2f)
            CurrentPhase = Phase.DESCEND;
    }

    private void Descend(float dt)
    {
        Vector3 p = drone.position;
        p.y = Mathf.Max(config.corridorAltitude, p.y - config.descentRate * dt);
        drone.position = p;

        if (p.y <= config.corridorAltitude + 0.05f)
        {
            DroneSweptDistance = 0f;
            CurrentPhase = Phase.CORRIDOR;
        }
    }

    private void Corridor(float dt)
    {
        DroneSweptDistance = Mathf.Min(config.routeLength,
                                       DroneSweptDistance + config.corridorSpeed * dt);

        float fromHospital = config.routeLength - DroneSweptDistance;
        Vector3 p = Vector3.Lerp(hospitalGround, sceneGround,
                                 fromHospital / config.routeLength);
        p.y = config.corridorAltitude;
        drone.position = p;

        FaceTravel(Vector3.back);

        float gap = config.routeLength - AmbulanceDistance - DroneSweptDistance;
        if (gap <= config.captureRadius)
        {
            MetAtTime = ElapsedTime;
            CurrentPhase = Phase.MET;
        }
    }

    private void Escort(float dt)
    {
        float lead = Mathf.Min(config.routeLength, AmbulanceDistance + 25f);
        Vector3 p = Vector3.Lerp(hospitalGround, sceneGround, lead / config.routeLength);
        p.y = config.corridorAltitude;
        drone.position = Vector3.MoveTowards(drone.position, p, config.dashSpeed * dt);
        FaceTravel(Vector3.forward);
    }

    private void FaceTravel(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        drone.rotation = Quaternion.Slerp(drone.rotation,
            Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * 4f);
    }
}