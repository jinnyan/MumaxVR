// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Phase 1 frame source: launches mumax-bridge.exe and streams live
/// magnetization frames from it over localhost TCP (protocol v1, see
/// mumax-bridge/README.md). The bridge exits by itself when we disconnect.
/// Reader runs on a background thread; frames are double-buffered and
/// published by reference swap under DataLock.
/// </summary>
public class MumaxBridgeClient : MumaxFrameSource
{
    [Header("Bridge process")]
    [Tooltip("Path to mumax-bridge.exe. Absolute, or relative to the Unity project root.")]
    public string bridgeExePath = "mumax-bridge/mumax-bridge.exe";
    [Tooltip("Start the bridge process automatically. Turn off to attach to one you started manually.")]
    public bool autoStartProcess = true;

    [Header("Connection")]
    public int port = 35999;
    [Tooltip("The bridge computes the demag kernel and relaxes before listening — first start can take a minute.")]
    public float connectTimeoutSeconds = 180f;

    [Header("Simulation (passed to the bridge as flags)")]
    public Vector3Int gridSizeRequest = new Vector3Int(256, 256, 1);
    [Tooltip("Cell size in meters")]
    public float cellSize = 4e-9f;
    [Tooltip("Solver steps per streamed frame")]
    public int stepsPerFrame = 25;

    const uint FrameMagic = 0x4D583346; // "MX3F"

    Process bridgeProcess;
    TcpClient client;
    Thread readerThread;
    volatile bool running;

    // double buffer: reader fills backBuffer, publishes by swapping refs under DataLock
    Vector3[] backBuffer;

    public bool Connected { get; private set; }
    public ulong LastStep { get; private set; }

    void Awake()
    {
        string exe = System.IO.Path.IsPathRooted(bridgeExePath)
            ? bridgeExePath
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", bridgeExePath));

        if (autoStartProcess)
        {
            if (!System.IO.File.Exists(exe))
            {
                Debug.LogError("MumaxBridgeClient: bridge exe not found: " + exe +
                    "\nLive mode needs the sidecar built once — see mumax-bridge/README.md. " +
                    "No GPU / just browsing? Open one of the Playback_* scenes instead.");
                enabled = false;
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "-nx {0} -ny {1} -nz {2} -cell {3:G} -steps {4} -port {5}",
                    gridSizeRequest.x, gridSizeRequest.y, gridSizeRequest.z, cellSize, stepsPerFrame, port),
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            bridgeProcess = new Process { StartInfo = psi };
            bridgeProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[bridge] " + e.Data); };
            bridgeProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[bridge] " + e.Data); };
            bridgeProcess.Start();
            bridgeProcess.BeginOutputReadLine();
            bridgeProcess.BeginErrorReadLine();
            Debug.Log("MumaxBridgeClient: bridge started (pid " + bridgeProcess.Id + "), waiting for it to listen...");
        }

        running = true;
        readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "MumaxBridgeReader" };
        readerThread.Start();
    }

    void ReaderLoop()
    {
        try
        {
            // retry-connect: the bridge computes the demag kernel + relax() first
            var deadline = DateTime.UtcNow.AddSeconds(connectTimeoutSeconds);
            while (running && client == null)
            {
                try
                {
                    var c = new TcpClient();
                    c.Connect("127.0.0.1", port);
                    client = c;
                }
                catch
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        Debug.LogError("MumaxBridgeClient: could not connect within " + connectTimeoutSeconds + "s");
                        return;
                    }
                    Thread.Sleep(1000);
                }
            }
            if (!running || client == null) return;

            Connected = true;
            Debug.Log("MumaxBridgeClient: connected, receiving frames");
            var stream = client.GetStream();

            byte[] header = new byte[32];
            byte[] payload = null;
            float[] floats = null;
            bool gridBuilt = false;

            while (running)
            {
                ReadExactly(stream, header, 32);
                uint magic = BitConverter.ToUInt32(header, 0);
                if (magic != FrameMagic)
                    throw new Exception("bad frame magic 0x" + magic.ToString("X8") + " — protocol mismatch?");

                int nx = (int)BitConverter.ToUInt32(header, 4);
                int ny = (int)BitConverter.ToUInt32(header, 8);
                int nz = (int)BitConverter.ToUInt32(header, 12);
                LastStep = BitConverter.ToUInt64(header, 16);
                double simTime = BitConverter.ToDouble(header, 24);

                int cells = nx * ny * nz;
                int byteCount = cells * 3 * 4;
                if (payload == null || payload.Length != byteCount)
                {
                    payload = new byte[byteCount];
                    floats = new float[cells * 3];
                    backBuffer = new Vector3[cells];
                }
                ReadExactly(stream, payload, byteCount);

                if (!gridBuilt)
                {
                    BuildGridPositions(nx, ny, nz); // no Unity API — safe off the main thread
                    gridBuilt = true;
                }

                Buffer.BlockCopy(payload, 0, floats, 0, byteCount);
                for (int i = 0; i < cells; i++)
                    backBuffer[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);

                lock (DataLock)
                {
                    Vector3[] published = CellDirections;
                    CellDirections = backBuffer;
                    SimTime = simTime;
                    // recycle the previously published array as the next back buffer
                    backBuffer = (published != null && published.Length == cells) ? published : new Vector3[cells];
                }
            }
        }
        catch (Exception e)
        {
            if (running) Debug.LogError("MumaxBridgeClient: reader stopped — " + e.Message);
        }
        finally
        {
            Connected = false;
        }
    }

    static void ReadExactly(NetworkStream s, byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int n = s.Read(buf, off, count - off);
            if (n <= 0) throw new Exception("bridge closed the connection");
            off += n;
        }
    }

    void OnDestroy()
    {
        running = false;
        try { if (client != null) client.Close(); } catch { } // unblocks the reader
        if (readerThread != null) readerThread.Join(2000);

        // disconnecting makes the bridge exit on its own; kill only if it lingers
        if (bridgeProcess != null)
        {
            try
            {
                if (!bridgeProcess.WaitForExit(3000))
                {
                    bridgeProcess.Kill();
                    Debug.LogWarning("MumaxBridgeClient: bridge did not exit on disconnect — killed");
                }
            }
            catch { }
            bridgeProcess.Dispose();
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 360, 50), GUI.skin.box);
        GUILayout.Label(Connected
            ? string.Format("MumaxVR live — step {0}   t = {1:0.###} ns", LastStep, SimTime * 1e9)
            : "MumaxVR live — starting bridge / connecting... (first start computes the demag kernel, ~1 min)");
        GUILayout.EndArea();
    }
}
