using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-feed camera system, laid out like a real drone ground station.
///
///   MAIN     large third-person view, cyclable between chase, ambulance
///            follow, and a fixed overhead operator framing
///   LEFT     a column of small feeds:
///              drone   forward, rear, left, right, nadir
///              ambulance  forward, rear
///
/// Feeds are described declaratively as an offset and rotation relative to
/// a parent transform, so adding another one is a single list entry rather
/// than new code. Each renders to a viewport rectangle rather than a
/// RenderTexture, which keeps the whole thing to one script with no extra
/// assets.
/// </summary>
public class DroneCameraRig : MonoBehaviour
{
    public enum MainView { ChaseDrone, FollowAmbulance, Operator, DroneNose }

    private class Feed
    {
        public string Label;
        public Transform Parent;
        public Vector3 LocalOffset;
        public Vector3 LocalEuler;
        public Camera Cam;
        public Rect ScreenRect;
        public bool GroupBreakBefore;
    }

    [Header("References")]
    public Transform drone;
    public Transform ambulance;

    [Header("Inset column")]
    public bool showInsets = true;
    public float insetMargin = 12f;
    public float insetGap = 5f;
    public float groupGap = 16f;
    public float topBarHeight = 44f;
    public float bottomBarHeight = 62f;
    public float insetAspect = 16f / 9f;
    public float maxInsetWidth = 232f;

    [Header("Main view")]
    public Vector3 chaseOffset = new Vector3(6f, 5f, -13f);
    public Vector3 ambulanceChaseOffset = new Vector3(5f, 4f, -11f);
    public float chaseDamping = 4.5f;
    public float operatorHeight = 170f;

    [Header("Drone nose")]
    public Vector3 noseOffset = new Vector3(0f, 0.35f, 0.9f);
    public float nosePitch = 8f;

    public MainView Mode { get; private set; } = MainView.ChaseDrone;
    public Camera MainCamera { get; private set; }

    private readonly List<Feed> feeds = new List<Feed>();
    private float routeLength = 300f;

    public IReadOnlyList<string> FeedLabels
    {
        get
        {
            List<string> l = new List<string>();
            foreach (Feed f in feeds) l.Add(f.Label);
            return l;
        }
    }

    public Rect GetFeedRect(int index) =>
        index >= 0 && index < feeds.Count ? feeds[index].ScreenRect : new Rect();

    public string GetFeedLabel(int index) =>
        index >= 0 && index < feeds.Count ? feeds[index].Label : "";

    public int FeedCount => feeds.Count;

    public void Initialise(Transform droneTransform, Transform ambulanceTransform,
                           float route)
    {
        drone = droneTransform;
        ambulance = ambulanceTransform;
        routeLength = route;

        MainCamera = GetComponent<Camera>();
        if (MainCamera == null) MainCamera = gameObject.AddComponent<Camera>();
        Configure(MainCamera, 100);
        MainCamera.rect = new Rect(0f, 0f, 1f, 1f);

        BuildFeeds();
    }

    private void BuildFeeds()
    {
        feeds.Clear();

        // Five drone feeds. The top view is omitted deliberately: looking up
        // at empty sky carries no mission information.
        AddFeed("DRONE  FORWARD", drone, noseOffset, new Vector3(nosePitch, 0f, 0f));
        AddFeed("DRONE  REAR", drone,
                new Vector3(0f, 0.35f, -0.9f), new Vector3(nosePitch, 180f, 0f));
        AddFeed("DRONE  PORT", drone,
                new Vector3(-0.9f, 0.2f, 0f), new Vector3(5f, -90f, 0f));
        AddFeed("DRONE  STARBOARD", drone,
                new Vector3(0.9f, 0.2f, 0f), new Vector3(5f, 90f, 0f));
        AddFeed("DRONE  NADIR", drone,
                new Vector3(0f, -0.3f, 0f), new Vector3(90f, 0f, 0f));

        // Two ambulance feeds, visually separated from the drone group.
        AddFeed("AMB  FORWARD", ambulance,
                new Vector3(0f, 1.9f, 2.2f), Vector3.zero, groupBreak: true);
        AddFeed("AMB  REAR", ambulance,
                new Vector3(0f, 1.9f, -2.4f), new Vector3(0f, 180f, 0f));
    }

    private void AddFeed(string label, Transform parent, Vector3 offset,
                         Vector3 euler, bool groupBreak = false)
    {
        GameObject go = new GameObject("Feed_" + label.Replace(" ", "_"));
        go.transform.SetParent(transform, false);

        Camera c = go.AddComponent<Camera>();
        Configure(c, feeds.Count + 1);

        feeds.Add(new Feed
        {
            Label = label,
            Parent = parent,
            LocalOffset = offset,
            LocalEuler = euler,
            Cam = c,
            GroupBreakBefore = groupBreak
        });
    }

    private void Configure(Camera c, int depth)
    {
        c.nearClipPlane = 0.08f;
        c.farClipPlane = 2000f;
        c.depth = depth;
        c.fieldOfView = 62f;
        c.clearFlags = CameraClearFlags.Skybox;
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
                FollowTarget(drone, chaseOffset);
                break;

            case MainView.FollowAmbulance:
                if (ambulance != null) FollowTarget(ambulance, ambulanceChaseOffset);
                break;

            case MainView.Operator:
                Vector3 mid = new Vector3(0f, operatorHeight, routeLength * 0.5f);
                transform.position = Vector3.Lerp(transform.position, mid,
                                                  Time.deltaTime * 3f);
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                break;

            case MainView.DroneNose:
                transform.position = drone.TransformPoint(noseOffset);
                transform.rotation = drone.rotation * Quaternion.Euler(nosePitch, 0f, 0f);
                break;
        }
    }

    private void FollowTarget(Transform target, Vector3 offset)
    {
        Vector3 desired = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desired,
                                          Time.deltaTime * chaseDamping);
        transform.LookAt(target.position + target.forward * 5f + Vector3.up * 1f);
    }

    private void UpdateFeeds()
    {
        foreach (Feed f in feeds)
        {
            if (f.Cam == null) continue;

            f.Cam.enabled = showInsets && f.Parent != null;
            if (!f.Cam.enabled) continue;

            f.Cam.transform.position = f.Parent.TransformPoint(f.LocalOffset);
            f.Cam.transform.rotation = f.Parent.rotation * Quaternion.Euler(f.LocalEuler);
        }

        if (showInsets) LayOutFeeds();
    }

    /// <summary>
    /// Sizes the column to whatever vertical space is left between the HUD
    /// bars, so the feeds stay legible at any window size rather than
    /// running off the bottom of the screen.
    /// </summary>
    private void LayOutFeeds()
    {
        float w = Screen.width;
        float h = Screen.height;
        if (w <= 0f || h <= 0f || feeds.Count == 0) return;

        int breaks = 0;
        foreach (Feed f in feeds) if (f.GroupBreakBefore) breaks++;

        float available = h - topBarHeight - bottomBarHeight
                          - insetGap * (feeds.Count - 1)
                          - groupGap * breaks;

        float feedH = Mathf.Max(48f, available / feeds.Count);
        float feedW = Mathf.Min(maxInsetWidth, feedH * insetAspect);
        feedH = feedW / insetAspect;

        float y = topBarHeight + 12f;   // from the top of the screen

        foreach (Feed f in feeds)
        {
            if (f.GroupBreakBefore) y += groupGap;

            f.ScreenRect = new Rect(insetMargin, y, feedW, feedH);

            // Camera.rect is normalised with the origin at the bottom left.
            f.Cam.rect = new Rect(insetMargin / w,
                                  (h - y - feedH) / h,
                                  feedW / w,
                                  feedH / h);

            y += feedH + insetGap;
        }
    }
}