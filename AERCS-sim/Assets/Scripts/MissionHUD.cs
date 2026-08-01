using UnityEngine;

/// <summary>
/// Mission dashboard, drawn over the main camera view.
///
/// Also composites the inset camera feeds. Those cameras render into
/// RenderTextures rather than screen viewport rects, and this class blits
/// them at explicit positions. Doing the compositing here means the feeds,
/// their frames, their labels, and the dashboard are all drawn in one pass
/// in a known order, instead of several screen-clearing cameras competing
/// over who renders last.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MissionHUD : MonoBehaviour
{
    public MissionController mission;
    public DroneCameraRig rig;
    public MavlinkBridge bridge;

    [Header("Inset layout")]
    public float insetWidth = 268f;
    public float insetMargin = 14f;
    public float insetGap = 30f;
    public float insetTop = 72f;

    private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.09f, 0.88f);
    private static readonly Color AccentColor = new Color(0.24f, 0.55f, 1f);
    private static readonly Color WarnColor = new Color(1f, 0.62f, 0.27f);
    private static readonly Color GoodColor = new Color(0.18f, 0.83f, 0.48f);
    private static readonly Color BadColor = new Color(0.95f, 0.32f, 0.32f);
    private static readonly Color MutedColor = new Color(0.49f, 0.55f, 0.64f);

    private Texture2D pixel;
    private GUIStyle label, value, heading, big, phaseStyle, tiny;
    private bool stylesReady;

    private static readonly string[] TimelineNames =
        { "CLIMB", "DASH", "CORRIDOR", "INBOUND", "ON SCENE", "RETURN", "HOME" };

    private void EnsureStyles()
    {
        if (stylesReady) return;

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();

        label = new GUIStyle(GUI.skin.label)
        { fontSize = 10, normal = { textColor = MutedColor } };
        tiny = new GUIStyle(GUI.skin.label)
        { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        value = new GUIStyle(GUI.skin.label)
        { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        heading = new GUIStyle(GUI.skin.label)
        { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = AccentColor } };
        big = new GUIStyle(GUI.skin.label)
        { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = WarnColor } };
        phaseStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        stylesReady = true;
    }

    private void Rect_(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, pixel);
        GUI.color = prev;
    }

    private void OnGUI()
    {
        if (mission == null) return;
        EnsureStyles();

        DrawInsetFeeds();
        DrawTopBar();
        DrawTelemetryPanel();
        DrawTimeline();
    }

    /// <summary>
    /// Blits each feed's RenderTexture, then frames and captions it.
    /// </summary>
    private void DrawInsetFeeds()
    {
        if (rig == null || !rig.showInsets) return;

        float w = Mathf.Min(insetWidth, Screen.width * 0.24f);
        float h = w * 9f / 16f;
        float y = insetTop;

        Color border = new Color(0.24f, 0.55f, 1f, 0.65f);

        for (int i = 0; i < rig.FeedCount; i++)
        {
            DroneCameraRig.Feed feed = rig.GetFeed(i);
            if (feed == null || feed.Texture == null) continue;

            Rect r = new Rect(insetMargin, y, w, h);

            GUI.DrawTexture(r, feed.Texture, ScaleMode.ScaleAndCrop);

            Rect_(new Rect(r.x, r.y, r.width, 1f), border);
            Rect_(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            Rect_(new Rect(r.x, r.y, 1f, r.height), border);
            Rect_(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);

            Rect_(new Rect(r.x, r.y, r.width, 15f), new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(r.x + 6f, r.y, r.width, 15f), feed.Label, tiny);

            y += h + insetGap;
        }
    }

    private void DrawTopBar()
    {
        Rect_(new Rect(0, 0, Screen.width, 52), PanelColor);
        Rect_(new Rect(0, 52, Screen.width, 1), new Color(0.12f, 0.16f, 0.21f));

        GUI.Label(new Rect(16, 6, 600, 18),
                  "AERCS  -  AUTONOMOUS EMERGENCY ROUTE CLEARANCE SYSTEM", heading);

        string view = rig != null ? rig.Mode.ToString() : "-";
        string hint = "MAIN: " + view
                      + "   [1] chase drone  [2] follow ambulance"
                      + "  [3] operator  [4] nose  [5] feeds";
        GUI.Label(new Rect(16, 22, 660, 16), hint, label);

        GUI.Label(new Rect(16, 35, 400, 16),
                  "HEADING FOR: " + mission.DestinationName.ToUpper(), label);

        DrawLinkBadge();

        string clock = "T+" + mission.ElapsedTime.ToString("00.0") + "s";
        GUI.Label(new Rect(Screen.width - 120, 16, 110, 22), clock, value);
    }

    private void DrawLinkBadge()
    {
        bool up = bridge != null && bridge.linkUp;
        string text = up
            ? "PX4 LINK  LIVE   " + bridge.packetsReceived + " pkt"
            : "PX4 LINK  OFFLINE  (simulated flight model)";

        float w = 250f;
        float x = Screen.width - w - 140f;

        Rect_(new Rect(x, 16, w, 20), new Color(0.08f, 0.1f, 0.14f, 0.9f));
        Rect_(new Rect(x, 16, 3, 20), up ? GoodColor : BadColor);

        GUIStyle s = new GUIStyle(label)
        { normal = { textColor = up ? GoodColor : BadColor } };
        GUI.Label(new Rect(x + 9, 18, w, 16), text, s);
    }

    private void DrawTelemetryPanel()
    {
        const float w = 244f;
        float x = Screen.width - w - 14f;
        float y = 66f;

        Rect_(new Rect(x, y, w, 296f), PanelColor);
        float cy = y + 12f;

        string key, val;
        HeadlineFor(out key, out val);

        GUI.Label(new Rect(x + 14, cy, w, 14), key, label);
        GUI.Label(new Rect(x + 14, cy + 12, w, 36), val, big);
        cy += 56f;

        Rect_(new Rect(x + 14, cy, w - 28, 1), new Color(0.12f, 0.16f, 0.21f));
        cy += 10f;

        GUI.Label(new Rect(x + 14, cy, w, 14), "DRONE", heading);
        cy += 15f;

        float droneAlt = mission.drone != null ? mission.drone.position.y : 0f;
        cy = Row(x, cy, w, "Altitude", droneAlt.ToString("F1") + " m");
        cy = Row(x, cy, w, "Separation",
                 mission.SeparationDistance.ToString("F0") + " m");

        if (bridge != null && bridge.linkUp)
        {
            cy = Row(x, cy, w, "PX4 groundspeed",
                     (bridge.GroundSpeed * 3.6f).ToString("F0") + " km/h");
            cy = Row(x, cy, w, "PX4 altitude",
                     bridge.AltitudeMetres.ToString("F1") + " m");
        }

        cy += 8f;
        GUI.Label(new Rect(x + 14, cy, w, 14), "AMBULANCE", heading);
        cy += 15f;

        string ambSpeed;
        if (mission.CurrentPhase == MissionController.Phase.ON_SCENE)
            ambSpeed = "loading";
        else if (mission.AmbulanceDispatched && mission.AmbulanceSpeedKmh > 0.1f)
            ambSpeed = mission.AmbulanceSpeedKmh.ToString("F0") + " km/h";
        else
            ambSpeed = "held";

        cy = Row(x, cy, w, "Speed", ambSpeed);
        cy = Row(x, cy, w, "Bound for", mission.DestinationName);
        cy = Row(x, cy, w, "Remaining",
                 mission.DistanceRemaining.ToString("F0") + " m");

        cy += 10f;
        GUI.Label(new Rect(x + 14, cy, w, 14), "CORRIDOR CLEARED", label);
        cy += 14f;
        Rect_(new Rect(x + 14, cy, w - 28, 8), new Color(0.1f, 0.13f, 0.18f));
        Rect_(new Rect(x + 14, cy,
                       (w - 28) * Mathf.Clamp01(mission.CorridorClearedFraction), 8),
              GoodColor);
    }

    /// <summary>
    /// The headline metric changes meaning with the phase, because what the
    /// operator most needs to know is not the same throughout the mission.
    /// </summary>
    private void HeadlineFor(out string key, out string val)
    {
        switch (mission.CurrentPhase)
        {
            case MissionController.Phase.CORRIDOR:
                key = "SEPARATION";
                val = mission.SeparationDistance.ToString("F0") + " m";
                return;

            case MissionController.Phase.ON_SCENE:
                float sinceMet = mission.ElapsedTime - mission.MetAtTime;
                float loadRemaining = Mathf.Max(
                    0f, mission.config.sceneDwellTime - sinceMet);
                key = "PATIENT LOADING";
                val = loadRemaining.ToString("F0") + "s";
                return;

            case MissionController.Phase.RETURN:
                key = "TO HOSPITAL";
                val = mission.DistanceRemaining.ToString("F0") + " m";
                return;

            case MissionController.Phase.COMPLETE:
                key = "MISSION COMPLETE";
                val = mission.ElapsedTime.ToString("F0") + "s";
                return;

            default:
                if (!mission.AmbulanceDispatched)
                {
                    float dispatchIn = Mathf.Max(
                        0f, mission.config.ambulanceDispatchDelay - mission.ElapsedTime);
                    key = "AMBULANCE DISPATCH IN";
                    val = dispatchIn.ToString("F1") + "s";
                }
                else
                {
                    key = "TO SCENE";
                    val = mission.DistanceRemaining.ToString("F0") + " m";
                }
                return;
        }
    }

    private float Row(float x, float y, float w, string k, string v)
    {
        GUI.Label(new Rect(x + 14, y, 130, 16), k, label);
        GUIStyle right = new GUIStyle(value)
        { fontSize = 11, alignment = TextAnchor.MiddleRight };
        GUI.Label(new Rect(x + w - 134, y - 1, 120, 16), v, right);
        return y + 18f;
    }

    private int TimelineIndex()
    {
        switch (mission.CurrentPhase)
        {
            case MissionController.Phase.CLIMB: return 0;
            case MissionController.Phase.DASH:
            case MissionController.Phase.DESCEND: return 1;
            case MissionController.Phase.CORRIDOR: return 2;
            case MissionController.Phase.MET:
            case MissionController.Phase.INBOUND: return 3;
            case MissionController.Phase.ON_SCENE: return 4;
            case MissionController.Phase.RETURN: return 5;
            case MissionController.Phase.COMPLETE: return 6;
            default: return -1;
        }
    }

    private void DrawTimeline()
    {
        int active = TimelineIndex();

        float boxW = 92f, boxH = 24f, gap = 4f;
        float total = TimelineNames.Length * boxW + (TimelineNames.Length - 1) * gap;
        float x = (Screen.width - total) * 0.5f;
        float y = Screen.height - 44f;

        Rect_(new Rect(0, Screen.height - 62f, Screen.width, 62f), PanelColor);

        for (int i = 0; i < TimelineNames.Length; i++)
        {
            Rect r = new Rect(x + i * (boxW + gap), y, boxW, boxH);

            Color fill;
            if (i < active) fill = new Color(0.11f, 0.34f, 0.22f, 0.9f);
            else if (i == active) fill = new Color(0.13f, 0.32f, 0.62f, 0.95f);
            else fill = new Color(0.09f, 0.12f, 0.16f, 0.9f);

            Rect_(r, fill);

            GUIStyle s = new GUIStyle(phaseStyle);
            if (i > active) s.normal.textColor = MutedColor;
            else if (i < active) s.normal.textColor = GoodColor;

            GUI.Label(r, TimelineNames[i], s);
        }
    }
}