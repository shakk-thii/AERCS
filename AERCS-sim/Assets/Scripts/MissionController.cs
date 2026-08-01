using UnityEngine;

/// <summary>
/// The AERCS mission state machine - full round trip.
///
///   CLIMB     drone rises to cruise altitude
///   DASH      direct flight, hospital -> incident scene
///   DESCEND   drops to sweep altitude on arrival
///   CORRIDOR  flies back along the road toward the inbound ambulance,
///             clearing traffic ahead of it
///   MET       rendezvous; corridor is open
///   INBOUND   drone leads the ambulance the rest of the way to the scene
///   ON_SCENE  ambulance stopped, patient being loaded
///   RETURN    the leg that actually matters - patient aboard, drone leads
///             the ambulance back to the hospital, clearing the road ahead
///   COMPLETE  ambulance home
///
/// The return leg is the critical one: a cleared corridor saves more life
/// years carrying a patient to definitive care than it does running empty
/// toward a scene. The outbound leg exists to get the drone in position.
///
/// Runs either on the built-in flight model or on live telemetry from a
/// real PX4 autopilot.
/// </summary>
public class MissionController : MonoBehaviour
{
    public enum Phase
    {
        IDLE, CLIMB, DASH, DESCEND, CORRIDOR, MET,
        INBOUND, ON_SCENE, RETURN, COMPLETE
    }

    public enum FlightSource { Simulated, PX4Telemetry }

    [Header("Configuration")]
    public MissionConfig config = new MissionConfig();

    [Header("Flight source")]
    public FlightSource flightSource = FlightSource.Simulated;
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
    public bool Returning { get; private set; }
    public float MetAtTime { get; private set; } = -1f;
    public bool RunningOnPX4 { get; private set; }

    public float CorridorClearedFraction =>
        config.routeLength <= 0f ? 0f : DroneSweptDistance / config.routeLength;

    /// <summary>Distance still to travel to the current destination.</summary>
    public float DistanceRemaining =>
        Returning ? AmbulanceDistance : config.routeLength - AmbulanceDistance;

    public string DestinationName => Returning ? "hospital" : "scene";

    private TrafficModel traffic;
    private Vector3 hospitalGround;
    private Vector3 sceneGround;
    private bool reachedScene;
    private float dwellStarted = -1f;

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
        Returning = false;
        MetAtTime = -1f;
        reachedScene = false;
        dwellStarted = -1f;

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

        if (linkLive) FollowPX4();
        else if (flightSource == FlightSource.Simulated || fallBackIfLinkLost)
            FlySimulated(dt);

        SeparationDistance = Vector3.Distance(
            new Vector3(drone.position.x, 0f, drone.position.z),
            new Vector3(ambulance.position.x, 0f, ambulance.position.z));
    }

    // ----------------------------------------------------------- ambulance

    private void AdvanceAmbulance(float dt)
    {
        if (ElapsedTime < config.ambulanceDispatchDelay)
        {
            AmbulanceSpeedKmh = 0f;
            return;
        }

        AmbulanceDispatched = true;

        // Stopped at the scene while the patient is loaded.
        if (CurrentPhase == Phase.ON_SCENE)
        {
            AmbulanceSpeedKmh = 0f;

            if (ElapsedTime - dwellStarted >= config.sceneDwellTime)
            {
                Returning = true;
                CurrentPhase = Phase.RETURN;
            }
            return;
        }

        if (CurrentPhase == Phase.COMPLETE)
        {
            AmbulanceSpeedKmh = 0f;
            return;
        }

        float speed = traffic.Step(dt);
        AmbulanceSpeedKmh = speed * 3.6f;

        if (!Returning)
        {
            AmbulanceDistance = Mathf.Min(config.routeLength,
                                          AmbulanceDistance + speed * dt);

            if (AmbulanceDistance >= config.routeLength - 0.5f
                && CurrentPhase != Phase.ON_SCENE)
            {
                dwellStarted = ElapsedTime;
                CurrentPhase = Phase.ON_SCENE;
            }
        }
        else
        {
            AmbulanceDistance = Mathf.Max(0f, AmbulanceDistance - speed * dt);

            if (AmbulanceDistance <= 0.5f)
                CurrentPhase = Phase.COMPLETE;
        }

        Vector3 pos = Vector3.Lerp(hospitalGround, sceneGround,
                                   AmbulanceDistance / config.routeLength);
        pos.y = ambulance.position.y;
        ambulance.position = pos;

        // Face the direction of travel.
        ambulance.rotation = Quaternion.LookRotation(
            Returning ? Vector3.back : Vector3.forward, Vector3.up);
    }

    // ----------------------------------------------------------------- PX4

    private void FollowPX4()
    {
        drone.position = bridge.UnityPosition;
        drone.rotation = bridge.UnityRotation;

        float altitude = drone.position.y;
        float along = Mathf.Clamp(drone.position.z, 0f, config.routeLength);

        if (!reachedScene)
        {
            if (altitude < config.dashAltitude * 0.9f && along < 5f)
                CurrentPhase = Phase.CLIMB;
            else if (along < config.routeLength - 10f)
                CurrentPhase = Phase.DASH;
            else if (altitude > config.corridorAltitude + 1f)
                CurrentPhase = Phase.DESCEND;
            else
                reachedScene = true;
        }
        else if (CurrentPhase == Phase.DESCEND || CurrentPhase == Phase.CORRIDOR)
        {
            CurrentPhase = Phase.CORRIDOR;
            DroneSweptDistance = Mathf.Clamp(config.routeLength - along,
                                             0f, config.routeLength);

            if (config.routeLength - AmbulanceDistance - DroneSweptDistance
                <= config.captureRadius)
            {
                MetAtTime = ElapsedTime;
                CurrentPhase = Phase.MET;
            }
        }
    }

    // ----------------------------------------------------------- simulated

    private void FlySimulated(float dt)
    {
        switch (CurrentPhase)
        {
            case Phase.CLIMB: Climb(dt); break;
            case Phase.DASH: Dash(dt); break;
            case Phase.DESCEND: Descend(dt); break;
            case Phase.CORRIDOR: Corridor(dt); break;
            case Phase.MET: LeadToScene(dt); break;
            case Phase.INBOUND: LeadToScene(dt); break;
            case Phase.ON_SCENE: HoldAtScene(dt); break;
            case Phase.RETURN: LeadToHospital(dt); break;
            case Phase.COMPLETE: ReturnToBase(dt); break;
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
        Vector3 target = new Vector3(0f, config.dashAltitude, config.routeLength);
        drone.position = Vector3.MoveTowards(drone.position, target,
                                             config.dashSpeed * dt);
        Face(Vector3.forward);
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
        PlaceDroneAt(config.routeLength - DroneSweptDistance);
        Face(Vector3.back);

        if (config.routeLength - AmbulanceDistance - DroneSweptDistance
            <= config.captureRadius)
        {
            MetAtTime = ElapsedTime;
            CurrentPhase = Phase.INBOUND;
        }
    }

    /// <summary>Hold station ahead of the ambulance on the way to the scene.</summary>
    private void LeadToScene(float dt)
    {
        float lead = Mathf.Min(config.routeLength,
                               AmbulanceDistance + config.escortLeadDistance);
        MoveDroneToward(lead, dt);
        Face(Vector3.forward);
    }

    private void HoldAtScene(float dt)
    {
        MoveDroneToward(config.routeLength, dt);
        Face(Vector3.back);
    }

    /// <summary>
    /// The return leg. Patient aboard, so this is the run that matters:
    /// the drone stays ahead of the ambulance all the way to the hospital.
    /// </summary>
    private void LeadToHospital(float dt)
    {
        float lead = Mathf.Max(0f, AmbulanceDistance - config.escortLeadDistance);
        MoveDroneToward(lead, dt);
        Face(Vector3.back);
    }

    private void ReturnToBase(float dt)
    {
        MoveDroneToward(0f, dt);
        Face(Vector3.back);
    }

    private void MoveDroneToward(float distanceAlongRoute, float dt)
    {
        Vector3 target = Vector3.Lerp(hospitalGround, sceneGround,
                                      distanceAlongRoute / config.routeLength);
        target.y = config.corridorAltitude;
        drone.position = Vector3.MoveTowards(drone.position, target,
                                             config.dashSpeed * dt);
    }

    private void PlaceDroneAt(float distanceAlongRoute)
    {
        Vector3 p = Vector3.Lerp(hospitalGround, sceneGround,
                                 distanceAlongRoute / config.routeLength);
        p.y = config.corridorAltitude;
        drone.position = p;
    }

    private void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        drone.rotation = Quaternion.Slerp(drone.rotation,
            Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * 4f);
    }
}