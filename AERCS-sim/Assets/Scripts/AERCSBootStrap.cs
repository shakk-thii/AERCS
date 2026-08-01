using UnityEngine;

/// <summary>
/// Single entry point for the whole simulation.
///
/// City, vehicles, camera rig, dashboard, and the PX4 link are all
/// constructed here at runtime from prefab references. Nothing is hand
/// placed in the scene, so the demo cannot drift out of sync with the
/// configuration and a fresh clone runs correctly first time.
///
/// Attach to one empty GameObject, assign the prefabs, press Play.
/// </summary>
[RequireComponent(typeof(CityBuilder))]
public class AERCSBootstrap : MonoBehaviour
{
    [Header("Vehicle prefabs")]
    public GameObject dronePrefab;
    public GameObject ambulancePrefab;

    [Header("Vehicle scale correction")]
    [Tooltip("Asset packs rarely ship at a consistent scale. Adjust until " +
             "the vehicles look right against the buildings.")]
    public float droneScale = 1f;
    public float ambulanceScale = 1f;

    [Header("Mission")]
    public MissionConfig config = new MissionConfig();

    [Header("PX4 link")]
    [Tooltip("PX4Telemetry renders the position reported by a real PX4 " +
             "autopilot. Simulated uses the built-in flight model and needs " +
             "nothing else running.")]
    public MissionController.FlightSource flightSource =
        MissionController.FlightSource.Simulated;
    public int mavlinkPort = 14550;

    [Header("Environment")]
    public bool buildCity = true;
    public Color groundColor = new Color(0.17f, 0.18f, 0.2f);

    private CityBuilder cityBuilder;
    private MissionController mission;
    private DroneCameraRig rig;
    private MavlinkBridge bridge;

    private void Awake()
    {
        cityBuilder = GetComponent<CityBuilder>();

        if (buildCity) cityBuilder.Build(config.routeLength);
        CreateGround();

        Transform drone = SpawnVehicle(dronePrefab, "Drone", droneScale);
        Transform ambulance = SpawnVehicle(ambulancePrefab, "Ambulance", ambulanceScale);

        // The bridge binds its socket in Start(), which runs after this
        // Awake(), so wiring the reference now is safe.
        bridge = gameObject.AddComponent<MavlinkBridge>();
        bridge.listenPort = mavlinkPort;
        bridge.autoStart = flightSource == MissionController.FlightSource.PX4Telemetry;

        mission = gameObject.AddComponent<MissionController>();
        mission.config = config;
        mission.drone = drone;
        mission.ambulance = ambulance;
        mission.flightSource = flightSource;
        mission.bridge = bridge;

        SetUpCamera(drone, ambulance);
        mission.Begin();
    }

    private Transform SpawnVehicle(GameObject prefab, string label, float scale)
    {
        GameObject go;

        if (prefab != null)
        {
            go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        }
        else
        {
            // Fall back to a primitive so the mission still runs and the
            // failure is visible rather than a null reference at startup.
            Debug.LogWarning($"AERCS: no prefab assigned for {label}; " +
                             "using a placeholder primitive.");
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        }

        go.name = label;
        go.transform.localScale *= scale;
        return go.transform;
    }

    private void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";

        // A Unity plane is 10 units across, so scale by a tenth of the span.
        float span = config.routeLength * 1.6f;
        ground.transform.localScale = new Vector3(span / 10f, 1f, span / 10f);
        ground.transform.position = new Vector3(0f, -0.02f, config.routeLength * 0.5f);

        Renderer r = ground.GetComponent<Renderer>();
        if (r != null) r.material.color = groundColor;
    }

    private void SetUpCamera(Transform drone, Transform ambulance)
    {
        Camera main = Camera.main;
        GameObject camGo;

        if (main != null)
        {
            camGo = main.gameObject;
        }
        else
        {
            camGo = new GameObject("Mission Camera");
            camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        if (camGo.GetComponent<AudioListener>() == null)
            camGo.AddComponent<AudioListener>();

        rig = camGo.GetComponent<DroneCameraRig>();
        if (rig == null) rig = camGo.AddComponent<DroneCameraRig>();
        rig.Initialise(drone, ambulance, config.routeLength);

        MissionHUD hud = camGo.GetComponent<MissionHUD>();
        if (hud == null) hud = camGo.AddComponent<MissionHUD>();
        hud.mission = mission;
        hud.rig = rig;
        hud.bridge = bridge;
    }
}