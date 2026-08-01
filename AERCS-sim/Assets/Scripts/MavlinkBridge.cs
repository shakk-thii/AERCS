using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Receives live telemetry from a running PX4 autopilot.
///
/// This is what turns the Unity scene from an animation into a view of a
/// real flight stack. PX4 SITL runs the actual EKF, control loops, and
/// flight dynamics; Python commands it over MAVSDK on port 14540; this
/// bridge listens to the parallel stream on 14550 and reports where PX4
/// believes the aircraft actually is.
///
/// A minimal MAVLink parser is used rather than a full library so the
/// project has no external package dependency. Only two messages are
/// decoded, both of which are streamed by PX4 by default:
///
///   #32 LOCAL_POSITION_NED   position and velocity in metres, NED frame
///   #30 ATTITUDE             roll, pitch, yaw in radians
///
/// Frame checksums are not verified. That is acceptable here because the
/// link is a local UDP loopback with no corruption risk, and because a
/// malformed frame would only produce one bad sample. It would NOT be
/// acceptable over a radio link to real hardware.
/// </summary>
public class MavlinkBridge : MonoBehaviour
{
    [Header("Link")]
    public int listenPort = 14550;
    public bool autoStart = true;

    [Header("Status (read only)")]
    public bool linkUp;
    public int packetsReceived;
    public float lastPacketAge;

    // --- latest decoded state, written by the receive thread ---
    private readonly object stateLock = new object();
    private Vector3 nedPosition;
    private Vector3 nedVelocity;
    private float roll, pitch, yaw;
    private float lastPacketTime;

    private UdpClient socket;
    private Thread receiveThread;
    private volatile bool running;

    /// <summary>Position converted from NED into Unity's left-handed frame.</summary>
    public Vector3 UnityPosition
    {
        get
        {
            lock (stateLock)
            {
                return new Vector3(nedPosition.y, -nedPosition.z, nedPosition.x);
            }
        }
    }

    /// <summary>Attitude converted from aerospace convention into Unity's.</summary>
    public Quaternion UnityRotation
    {
        get
        {
            lock (stateLock)
            {
                return Quaternion.Euler(-pitch * Mathf.Rad2Deg,
                                        yaw * Mathf.Rad2Deg,
                                        -roll * Mathf.Rad2Deg);
            }
        }
    }

    public float GroundSpeed
    {
        get
        {
            lock (stateLock)
            {
                return new Vector2(nedVelocity.x, nedVelocity.y).magnitude;
            }
        }
    }

    public float AltitudeMetres
    {
        get { lock (stateLock) { return -nedPosition.z; } }
    }

    private void Start()
    {
        if (autoStart) StartLink();
    }

    public void StartLink()
    {
        if (running) return;

        try
        {
            socket = new UdpClient(listenPort);
            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
            Debug.Log($"MAVLink bridge listening on UDP {listenPort}");
        }
        catch (Exception e)
        {
            // Most commonly: the port is already bound by QGroundControl.
            Debug.LogWarning($"MAVLink bridge could not bind port {listenPort}: " +
                             $"{e.Message}. Falling back to simulated flight.");
            running = false;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] data = socket.Receive(ref from);
                ParseFrames(data);
            }
            catch (SocketException) { /* socket closed on shutdown */ }
            catch (ObjectDisposedException) { break; }
            catch (Exception e) { Debug.LogWarning($"MAVLink parse error: {e.Message}"); }
        }
    }

    /// <summary>
    /// A single UDP datagram can carry several MAVLink frames back to back,
    /// so walk the buffer rather than assuming one frame per packet.
    /// </summary>
    private void ParseFrames(byte[] buf)
    {
        int i = 0;
        while (i < buf.Length)
        {
            byte magic = buf[i];

            int headerLen, msgId, payloadStart;

            if (magic == 0xFD && i + 10 <= buf.Length)          // MAVLink v2
            {
                int payloadLen = buf[i + 1];
                bool signed = (buf[i + 2] & 0x01) != 0;
                msgId = buf[i + 7] | (buf[i + 8] << 8) | (buf[i + 9] << 16);
                headerLen = 10;
                payloadStart = i + headerLen;

                int frameLen = headerLen + payloadLen + 2 + (signed ? 13 : 0);
                if (payloadStart + payloadLen > buf.Length) return;

                Decode(msgId, buf, payloadStart, payloadLen);
                i += frameLen;
            }
            else if (magic == 0xFE && i + 6 <= buf.Length)      // MAVLink v1
            {
                int payloadLen = buf[i + 1];
                msgId = buf[i + 5];
                headerLen = 6;
                payloadStart = i + headerLen;

                if (payloadStart + payloadLen > buf.Length) return;

                Decode(msgId, buf, payloadStart, payloadLen);
                i += headerLen + payloadLen + 2;
            }
            else
            {
                i++;   // resynchronise on the next magic byte
            }
        }
    }

    private void Decode(int msgId, byte[] buf, int off, int len)
    {
        switch (msgId)
        {
            case 32:   // LOCAL_POSITION_NED
                if (len < 28) return;
                lock (stateLock)
                {
                    nedPosition = new Vector3(
                        BitConverter.ToSingle(buf, off + 4),
                        BitConverter.ToSingle(buf, off + 8),
                        BitConverter.ToSingle(buf, off + 12));
                    nedVelocity = new Vector3(
                        BitConverter.ToSingle(buf, off + 16),
                        BitConverter.ToSingle(buf, off + 20),
                        BitConverter.ToSingle(buf, off + 24));
                    lastPacketTime = Time.realtimeSinceStartup;
                }
                packetsReceived++;
                break;

            case 30:   // ATTITUDE
                if (len < 28) return;
                lock (stateLock)
                {
                    roll = BitConverter.ToSingle(buf, off + 4);
                    pitch = BitConverter.ToSingle(buf, off + 8);
                    yaw = BitConverter.ToSingle(buf, off + 12);
                    lastPacketTime = Time.realtimeSinceStartup;
                }
                packetsReceived++;
                break;
        }
    }

    private void Update()
    {
        float age;
        lock (stateLock) { age = Time.realtimeSinceStartup - lastPacketTime; }

        lastPacketAge = age;
        // Two seconds without a packet counts as a dead link.
        linkUp = running && packetsReceived > 0 && age < 2f;
    }

    private void OnDestroy() { StopLink(); }
    private void OnApplicationQuit() { StopLink(); }

    public void StopLink()
    {
        running = false;
        socket?.Close();
        socket = null;
        receiveThread?.Join(200);
        receiveThread = null;
    }
}