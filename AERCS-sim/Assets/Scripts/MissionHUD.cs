using UnityEngine;

/// <summary>
/// Mission dashboard drawn over the camera feeds.
///
/// Uses IMGUI rather than a uGUI Canvas deliberately: no prefabs, no fonts,
/// no manual scene wiring. For a demo that must work the moment someone
/// presses Play on a fresh clone, that reliability is worth more than the
/// extra features a Canvas would bring.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MissionHUD : MonoBehaviour
{
    public MissionController mission;
    public DroneCameraRig rig;
    public MavlinkBridge bridge;

    private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.09f, 0.88f);
    private static readonly Color AccentColor = new Color(0.24f, 0.55f, 1f);
    private static readonly Color WarnColor = new Color(1f, 0.62f, 0.27f);
    private static readonly Color GoodColor = new Color(0.18f, 0.83f, 0.48f);
    private static readonly Color BadColor = new Color(0.95f, 0.32f, 0.32f);
    private static readonly Color MutedColor = new Color(0.49f, 0.55f, 0.64f);

    private Texture2D pixel;
    private GUIStyle label, value, heading, big, phaseStyle, tiny;
    private bool stylesReady;

    private void EnsureStyles()
    {
        if (stylesReady) return;

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();

        label = new GUIStyle(GUI.skin.label)
        { fontSize = 10, normal = { textColor = MutedColor } };
        tiny = new GUIStyle(GUI.skin.label)
        { fontSize = 9, normal = { textColor = MutedColor } };
        value = new GUIStyle(GUI.skin.label)
        { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        heading = new GUIStyle(GUI.skin.label)
        { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = AccentColor } };
        big = new GUIStyle(GUI.skin.label)
        { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = WarnColor } };
        phaseStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
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

        DrawTopBar();
        DrawTelemetryPanel();
        DrawPhaseTimeline();
        DrawFeedFrames();
    }

    private void DrawTopBar()
    {
        Rect_(new Rect(0, 0, Screen.width, 44), PanelColor);
        Rect_(new Rect(0, 44, Screen.width, 1), new Color(0.12f, 0.16f, 0.21f));

        GUI.Label(new Rect(16, 5, 560, 18),
                  "AERCS  -  AUTONOMOUS EMERGENCY ROUTE CLEARANCE SYSTEM", heading);

        string view = rig != null ? rig.Mode.ToString() : "-";
        GUI.Label(new Rect(16, 20, 620, 16),
                  $"MAIN: {view}   [1] chase drone  [2] follow ambulance  " +
                  $"[3] operator  [4] nose  [5] feeds", label);

        // PX4 link indicator - the difference between a rendered animation
        // and a view of a real autopilot.
        DrawLinkBadge();

        string t = $"T+{mission.ElapsedTime:00.0}s";
        GUI.Label(new Rect(Screen.width - 120, 12, 110, 22), t, value);
    }

    private void DrawLinkBadge()
    {
        bool up = bridge != null && bridge.linkUp;
        string text = up
            ? $"PX4 LINK  LIVE   {bridge.packetsReceived} pkt"
            : "PX4 LINK  OFFLINE  (simulated flight model)";

        float w = 250f;
        float x = Screen.width - w - 140f;

        Rect_(new Rect(x, 12, w, 20), new Color(0.08f, 0.1f, 0.14f, 0.9f));
        Rect_(new Rect(x, 12, 3, 20), up ? GoodColor : BadColor);

        GUIStyle s = new GUIStyle(label)
        { normal = { textColor = up ? GoodColor : BadColor } };
        GUI.Label(new Rect(x + 9, 14, w, 16), text, s);
    }

    private void DrawTelemetryPanel()
    {
        const float w = 244f;
        float x = Screen.width - w - 14f;
        float y = 58f;

        Rect_(new Rect(x, y, w, 300f), PanelColor);

        float cy = y + 12f;

        string headKey, headVal;
        if (mission.CurrentPhase == MissionController.Phase.CORRIDOR)
        {
            headKey = "SEPARATION";
            headVal = $"{mission.SeparationDistance:F0} m";
        }
        else if (mission.CurrentPhase == MissionController.Phase.MET)
        {
            headKey = "CORRIDOR OPEN";
            headVal = $"{mission.CorridorClearedFraction * 100f:F0}%";
        }
        else if (!mission.AmbulanceDispatched)
        {
            headKey = "AMBULANCE DISPATCH IN";
            headVal = $"{Mathf.Max(0f, mission.config.ambulanceDispatchDelay - mission.ElapsedTime):F1}s";
        }
        else
        {
            headKey = "DRONE INBOUND";
            headVal = $"{mission.ElapsedTime:F0}s";
        }

        GUI.Label(new Rect(x + 14, cy, w, 14), headKey, label);
        GUI.Label(new Rect(x + 14, cy + 12, w, 36), headVal, big);
        cy += 56f;

        Rect_(new Rect(x + 14, cy, w - 28, 1), new Color(0.12f, 0.16f, 0.21f));
        cy += 10f;

        GUI.Label(new Rect(x + 14, cy, w, 14), "DRONE", heading);
        cy += 15f;
        cy = Row(x, cy, w, "Altitude",
                 $"{(mission.drone != null ? mission.drone.position.y : 0f):F1} m");
        cy = Row(x, cy, w, "Phase speed", PhaseSpeed());
        cy = Row(x, cy, w, "Swept", $"{mission.DroneSweptDistance:F0} m");

        if (bridge != null && bridge.linkUp)
        {
            cy = Row(x, cy, w, "PX4 groundspeed", $"{bridge.GroundSpeed * 3.6f:F0} km/h");
            cy = Row(x, cy, w, "PX4 altitude", $"{bridge.AltitudeMetres:F1} m");
        }

        cy += 8f;
        GUI.Label(new Rect(x + 14, cy, w, 14), "AMBULANCE", heading);
        cy += 15f;
        cy = Row(x, cy, w, "Speed",
                 mission.AmbulanceDispatched
                     ? $"{mission.AmbulanceSpeedKmh:F0} km/h" : "held");
        cy = Row(x, cy, w, "Distance", $"{mission.AmbulanceDistance:F0} m");
        cy = Row(x, cy, w, "To scene",
                 $"{mission.config.routeLength - mission.AmbulanceDistance:F0} m");

        cy += 10f;
        GUI.Label(new Rect(x + 14, cy, w, 14), "CORRIDOR CLEARED", label);
        cy += 14f;
        Rect_(new Rect(x + 14, cy, w - 28, 8), new Color(0.1f, 0.13f, 0.18f));
        Rect_(new Rect(x + 14, cy,
                       (w - 28) * Mathf.Clamp01(mission.CorridorClearedFraction), 8),
              GoodColor);
    }

    private string PhaseSpeed()
    {
        switch (mission.CurrentPhase)
        {
            case MissionController.Phase.DASH:
                return $"{mission.config.dashSpeed * 3.6f:F0} km/h";
            case MissionController.Phase.CORRIDOR:
                return $"{mission.config.corridorSpeed * 3.6f:F0} km/h";
            case MissionController.Phase.CLIMB:
                return $"{mission.config.climbRate:F1} m/s up";
            case MissionController.Phase.DESCEND:
                return $"{mission.config.descentRate:F1} m/s down";
            default:
                return "station";
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

    private void DrawPhaseTimeline()
    {
        string[] names = { "CLIMB", "DASH", "DESCEND", "CORRIDOR", "MET" };
        int active = (int)mission.CurrentPhase - 1;

        float boxW = 100f, boxH = 24f, gap = 5f;
        float total = names.Length * boxW + (names.Length - 1) * gap;
        float x = (Screen.width - total) * 0.5f;
        float y = Screen.height - 44f;

        Rect_(new Rect(0, Screen.height - 62f, Screen.width, 62f), PanelColor);

        for (int i = 0; i < names.Length; i++)
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

            GUI.Label(r, names[i], s);
        }
    }

    /// <summary>
    /// Frames and captions every inset feed. Without captions a column of
    /// seven small views is unreadable , the operator cannot tell the port
    /// camera from the starboard one.
    /// </summary>
    private void DrawFeedFrames()
    {
        if (rig == null || !rig.showInsets) return;

        Color border = new Color(0.24f, 0.55f, 1f, 0.5f);

        for (int i = 0; i < rig.FeedCount; i++)
        {
            Rect r = rig.GetFeedRect(i);
            if (r.width <= 0f) continue;

            Rect_(new Rect(r.x, r.y, r.width, 1f), border);
            Rect_(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            Rect_(new Rect(r.x, r.y, 1f, r.height), border);
            Rect_(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);

            Rect_(new Rect(r.x, r.y, r.width, 13f), new Color(0f, 0f, 0f, 0.62f));
            GUI.Label(new Rect(r.x + 5f, r.y - 1f, r.width, 14f),
                      rig.GetFeedLabel(i), tiny);
        }
    }
}