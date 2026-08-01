using UnityEngine;

// Direct port of intercept.py + corridor.py + the dash-phase math from
// validate.py. This is the actual AERCS mission logic, unchanged from
// what was validated in Python - only the language and the "print" have
// changed to Unity types and GameObject movement.
//
// MonoBehaviour means this script attaches to a GameObject and Unity
// calls Start() once and Update() every frame automatically.
public class MissionController : MonoBehaviour
{
    public enum Phase { CLIMB, DASH, DESCEND, CORRIDOR, MET }

    [Header("Scene references")]
    public Transform drone;
    public Transform ambulance;
    public Transform hospitalPoint;
    public Transform scenePoint;

    [Header("Mission parameters (compressed scale for a watchable scene)")]
    public float ambulanceCruiseKmh = 22f;
    public float ambulanceMinKmh = 10f;
    public float ambulanceMaxKmh = 35f;
    public float dashSpeedMps = 14f;      // drone dash speed
    public float corridorSpeedMps = 5f;   // drone corridor sweep speed
    public float climbRateMps = 4f;
    public float descentRateMps = 3f;
    public float dashAltitude = 12f;
    public float corridorAltitude = 3f;
    public float captureRadius = 3f;

    public Phase currentPhase = Phase.CLIMB;
    public float elapsedS = 0f;
    public float corridorClearedFraction = 0f;

    private TrafficModel traffic;
    private Vector3 groundHospital;
    private Vector3 groundScene;
    private float routeLength;

    // How far the drone has swept back from the scene, and how far the
    // ambulance has driven forward from the hospital - same one-
    // dimensional reduction as corridor.py, since both travel the same
    // straight line in this simplified scene.
    private float droneSweptM;
    private float ambulanceTravelledM;

    void Start()
    {
        traffic = new TrafficModel(cruiseKmh: ambulanceCruiseKmh,
    minKmh: ambulanceMinKmh, maxKmh: ambulanceMaxKmh, seed: 42);

        groundHospital = new Vector3(hospitalPoint.position.x, 0f, hospitalPoint.position.z);
        groundScene = new Vector3(scenePoint.position.x, 0f, scenePoint.position.z);
        routeLength = Vector3.Distance(groundHospital, groundScene);

        ambulance.position = groundHospital;
        drone.position = groundHospital + Vector3.up * 0f;

        currentPhase = Phase.CLIMB;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        elapsedS += dt;

        if (currentPhase != Phase.MET)
        {
            float ambulanceSpeed = traffic.Step(dt);
            ambulanceTravelledM = Mathf.Min(routeLength,
                ambulanceTravelledM + ambulanceSpeed * dt);

            Vector3 ambulancePos = Vector3.Lerp(groundHospital, groundScene,
                ambulanceTravelledM / routeLength);
            ambulancePos.y = ambulance.position.y;
            ambulance.position = ambulancePos;
        }

        switch (currentPhase)
        {
            case Phase.CLIMB:
                RunClimb(dt);
                break;
            case Phase.DASH:
                RunDash(dt);
                break;
            case Phase.DESCEND:
                RunDescend(dt);
                break;
            case Phase.CORRIDOR:
                RunCorridor(dt);
                break;
            case Phase.MET:
                break;
        }
    }

    void RunClimb(float dt)
    {
        Vector3 pos = drone.position;
        pos.y = Mathf.Min(dashAltitude, pos.y + climbRateMps * dt);
        drone.position = pos;

        if (pos.y >= dashAltitude - 0.05f)
        {
            currentPhase = Phase.DASH;
        }
    }

    void RunDash(float dt)
    {
        // Straight-line flight, hospital -> scene, at cruise altitude.
        Vector3 target = new Vector3(groundScene.x, dashAltitude, groundScene.z);
        drone.position = Vector3.MoveTowards(drone.position, target, dashSpeedMps * dt);

        if (Vector3.Distance(drone.position, target) < 0.1f)
        {
            currentPhase = Phase.DESCEND;
        }
    }

    void RunDescend(float dt)
    {
        Vector3 pos = drone.position;
        pos.y = Mathf.Max(corridorAltitude, pos.y - descentRateMps * dt);
        drone.position = pos;

        if (pos.y <= corridorAltitude + 0.05f)
        {
            // Corridor phase begins now - reset the one-dimensional
            // sweep counters, same as CorridorPursuit.__init__ in Python.
            droneSweptM = 0f;
            ambulanceTravelledM = 0f;
            currentPhase = Phase.CORRIDOR;
        }
    }

    void RunCorridor(float dt)
    {
        droneSweptM = Mathf.Min(routeLength,
            droneSweptM + corridorSpeedMps * dt);

        float gap = routeLength - ambulanceTravelledM - droneSweptM;
        corridorClearedFraction = droneSweptM / routeLength;

        float droneDistanceFromHospital = routeLength - droneSweptM;
        Vector3 dronePos = Vector3.Lerp(groundHospital, groundScene,
            droneDistanceFromHospital / routeLength);
        dronePos.y = corridorAltitude;
        drone.position = dronePos;

        if (gap <= captureRadius)
        {
            currentPhase = Phase.MET;
        }
    }
}