using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Camera system for the mission.
///
///   MAIN     large third-person view, switchable between following the
///            drone, following the ambulance, and a fixed overhead framing
///   INSET 1  drone nose camera - what the aircraft sees
///   INSET 2  ambulance third person - the vehicle being escorted
///
/// The insets render to RenderTextures rather than screen viewport rects.
/// Viewport rects put every feed on its own screen-clearing camera, which
/// made the draw order fight with the dashboard: whichever camera rendered
/// last wiped the others. Rendering to textures and blitting them inside
/// the HUD's own pass makes the ordering explicit and unambiguous.
/// </summary>
public class DroneCameraRig : MonoBehaviour
{
    public enum MainView { ChaseDrone, FollowAmbulance, Operator, DroneNose }

    public class Feed
    {
        public string Label;
        public Transform Parent;
        public Vector3 LocalOffset;
        public Vector3 LocalEuler;
        public bool LookAtParent;
        public Camera Cam;
        public RenderTexture Texture;
    }

    [Header("References")]
    public Transform drone;
    public Transform ambulance;

    [Header("Insets")]
    public bool showInsets = true;
    public int feedTextureWidth = 512;
    public int feedTextureHeight = 288;

    [Header("Main view")]
    public Vector3 chaseOffset = new Vector3(7f, 5f, -14f);
    public Vector3 ambulanceChaseOffset = new Vector3(5f, 4f, -11f);
    public float chaseDamping = 4.5f;
    public float operatorHeight = 170f;

    [Header("Drone nose")]
    public Vector3 noseOffset = new Vector3(0f, 0.35f, 0.9f);
    public float nosePitch = 8f;

    [Header("Ambulance third person")]
    public Vector3 ambulanceFeedOffset = new Vector3(3.5f, 3f, -7f);

    public MainView Mode { get; private set; } = MainView.ChaseDrone;
    public Camera MainCamera { get; private set; }

    private readonly List<Feed> feeds = new List<Feed>();
    private float routeLength = 300f;

    public int FeedCount => feeds.Count;
    public Feed GetFeed(int i) => i >= 0 && i < feeds.Count ? feeds[i] : null;

    public void Initialise(Transform droneTransform, Transform ambulanceTransform,
                           float route)
    {
        drone = droneTransform;
        ambulance = ambulanceTransform;
        routeLength = route;

        MainCamera = GetComponent<Camera>();
        if (MainCamera == null) MainCamera = gameObject.AddComponent<Camera>();
        Configure(MainCamera, 0);
        MainCamera.rect = new Rect(0f, 0f, 1f, 1f);
        MainCamera.targetTexture = null;

        BuildFeeds();
    }

    private void BuildFeeds()
    {
        ReleaseFeeds();

        AddFeed("DRONE  NOSE CAMERA", drone, noseOffset,
                new Vector3(nosePitch, 0f, 0f), lookAtParent: false);

        AddFeed("AMBULANCE", ambulance, ambulanceFeedOffset,
                Vector3.zero, lookAtParent: true);
    }

    private void AddFeed(string label, Transform parent, Vector3 offset,
                         Vector3 euler, bool lookAtParent)
    {
        GameObject go = new GameObject("Feed_" + label.Replace(" ", "_"));
        go.transform.SetParent(transform, false);

        Camera c = go.AddComponent<Camera>();
        Configure(c, -10 + feeds.Count);   // render before the main view

        RenderTexture rt = new RenderTexture(feedTextureWidth,
                                             feedTextureHeight, 24);
        rt.name = "RT_" + label;
        rt.Create();
        c.targetTexture = rt;

        feeds.Add(new Feed
        {
            Label = label,
            Parent = parent,
            LocalOffset = offset,
            LocalEuler = euler,
            LookAtParent = lookAtParent,
            Cam = c,
            Texture = rt
        });
    }

    private void Configure(Camera c, int depth)
    {
        c.nearClipPlane = 0.08f;
        c.farClipPlane = 2000f;
        c.depth = depth;
        c.fieldOfView = 62f;
        c.clearFlags = CameraClearFlags.Skybox;
        c.rect = new Rect(0f, 0f, 1f, 1f);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) Mode = MainView.ChaseDrone;
        if (Input.GetKeyDown(KeyCode.Alpha2)) Mode = MainView.FollowAmbulance;
        if (Input.GetKeyDown(KeyCode.Alpha3)) Mode = MainView.Operator;
        if (Input.GetKeyDown(KeyCode.Alpha4)) Mode = MainView.DroneNose;
        if (Input.GetKeyDown(KeyCode.Alpha5)) showInsets = !showInsets;
    }

    private void LateUpdate()
    {
        if (drone == null) return;
        UpdateMainView();
        UpdateFeeds();
    }

    private void UpdateMainView()
    {
        switch (Mode)
        {
            case MainView.ChaseDrone:
                Follow(drone, chaseOffset);
                break;

            case MainView.FollowAmbulance:
                if (ambulance != null) Follow(ambulance, ambulanceChaseOffset);
                break;

            case MainView.Operator:
                Vector3 mid = new Vector3(0f, operatorHeight, routeLength * 0.5f);
                transform.position = Vector3.Lerp(transform.position, mid,
                                                  Time.deltaTime * 3f);
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                break;

            case MainView.DroneNose:
                transform.position = drone.TransformPoint(noseOffset);
                transform.rotation = drone.rotation
                                     * Quaternion.Euler(nosePitch, 0f, 0f);
                break;
        }
    }

    private void Follow(Transform target, Vector3 offset)
    {
        Vector3 desired = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desired,
                                          Time.deltaTime * chaseDamping);
        transform.LookAt(target.position + target.forward * 5f + Vector3.up);
    }

    private void UpdateFeeds()
    {
        foreach (Feed f in feeds)
        {
            if (f.Cam == null) continue;

            f.Cam.enabled = showInsets && f.Parent != null;
            if (!f.Cam.enabled) continue;

            f.Cam.transform.position = f.Parent.TransformPoint(f.LocalOffset);

            if (f.LookAtParent)
                f.Cam.transform.LookAt(f.Parent.position + Vector3.up * 0.8f);
            else
                f.Cam.transform.rotation = f.Parent.rotation
                                           * Quaternion.Euler(f.LocalEuler);
        }
    }

    private void ReleaseFeeds()
    {
        foreach (Feed f in feeds)
        {
            if (f.Texture != null) f.Texture.Release();
            if (f.Cam != null) Destroy(f.Cam.gameObject);
        }
        feeds.Clear();
    }

    private void OnDestroy() { ReleaseFeeds(); }
}