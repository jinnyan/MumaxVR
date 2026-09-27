// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// Phase 0 frame source: loads mumax OVF output files from a folder and plays
/// them back. Watches the folder so it also works while mumax is still running
/// and autosave() keeps dropping new frames.
/// Parsing happens on a background thread; only frame swapping touches Update().
/// </summary>
public class MumaxPlaybackManager : MumaxFrameSource
{
    [Tooltip("Folder with .ovf frames. Absolute path, or relative to the Unity project root (the folder containing Assets).")]
    public string ovfFolder = "OvfSamples";

    [Tooltip("Keep polling the folder for new files (live-follow a running mumax).")]
    public bool watchFolder = true;

    [Tooltip("Playback speed in frames per second.")]
    public float playbackFps = 20f;

    [Tooltip("Loop when the last frame is reached (ignored while new files keep arriving).")]
    public bool loop = true;

    public bool playOnLoad = true;

    [Tooltip("Simple on-screen play/pause/scrub panel (desktop debugging).")]
    public bool showDebugUI = true;

    readonly List<OvfFrame> frames = new List<OvfFrame>();
    readonly object framesLock = new object();
    readonly HashSet<string> loadedFiles = new HashSet<string>();
    readonly Dictionary<string, int> failedAttempts = new Dictionary<string, int>();

    Thread loaderThread;
    volatile bool running;
    volatile int lastLoadTick; // Environment.TickCount when the loader last added a frame
    string resolvedFolder;

    bool playing;
    float frameTimer;
    int currentFrame;
    int shownFrame = -1;
    bool gridBuilt;

    public int FrameCount { get { lock (framesLock) { return frames.Count; } } }
    public int CurrentFrame { get { return currentFrame; } }
    public bool IsPlaying { get { return playing; } }

    void Awake()
    {
        // Application.dataPath is main-thread only — resolve before the thread starts.
        resolvedFolder = Path.IsPathRooted(ovfFolder)
            ? ovfFolder
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", ovfFolder));

        playing = playOnLoad;
        running = true;
        loaderThread = new Thread(LoaderLoop) { IsBackground = true, Name = "MumaxOvfLoader" };
        loaderThread.Start();
    }

    void OnDestroy()
    {
        running = false;
        if (loaderThread != null) loaderThread.Join(1000);
    }

    // ── Background: scan folder, parse new files in filename order ──
    void LoaderLoop()
    {
        while (running)
        {
            try
            {
                if (Directory.Exists(resolvedFolder))
                {
                    string[] files = Directory.GetFiles(resolvedFolder, "*.ovf");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                    foreach (string file in files)
                    {
                        if (!running) return;
                        if (loadedFiles.Contains(file)) continue;
                        int fails;
                        if (failedAttempts.TryGetValue(file, out fails) && fails >= 3) continue;

                        try
                        {
                            OvfFrame frame = OvfLoader.Load(file);
                            lock (framesLock) { frames.Add(frame); }
                            loadedFiles.Add(file);
                            lastLoadTick = Environment.TickCount;
                        }
                        catch (Exception e)
                        {
                            // Likely still being written — retry a couple of passes, then give up.
                            failedAttempts[file] = fails + 1;
                            if (fails + 1 >= 3)
                                Debug.LogWarning("MumaxVR: giving up on " + file + " — " + e.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("MumaxVR: OVF loader error — " + e.Message);
            }

            if (!watchFolder && loadedFiles.Count > 0) return;
            Thread.Sleep(500);
        }
    }

    // ── Main thread: playback ──
    void Update()
    {
        int count = FrameCount;
        if (count == 0) return;

        if (!gridBuilt)
        {
            OvfFrame first;
            lock (framesLock) { first = frames[0]; }
            BuildGridPositions(first.Nx, first.Ny, first.Nz);
            gridBuilt = true;
        }

        if (playing && playbackFps > 0f)
        {
            frameTimer += Time.deltaTime;
            float step = 1f / playbackFps;
            while (frameTimer >= step)
            {
                frameTimer -= step;
                if (currentFrame + 1 < count) currentFrame++;
                else if (loop && !FilesStillArriving()) currentFrame = 0;
                // else: hold the last frame — live-follow picks up new files as they land
            }
        }

        if (currentFrame != shownFrame) ApplyFrame(currentFrame);
    }

    void ApplyFrame(int index)
    {
        OvfFrame frame;
        lock (framesLock)
        {
            if (index < 0 || index >= frames.Count) return;
            frame = frames[index];
        }
        lock (DataLock)
        {
            CellDirections = frame.Vectors;
            SimTime = frame.SimTime;
        }
        shownFrame = index;
    }

    // A file that landed within the last 3 s counts as "still arriving": a
    // live-followed run holds its newest frame instead of looping mid-run.
    bool FilesStillArriving()
    {
        return watchFolder && unchecked(Environment.TickCount - lastLoadTick) < 3000;
    }

    // ── Public controls (also used by the debug UI; VR panels hook in here later) ──
    public void Play()
    {
        int count = FrameCount;
        if (count > 0 && currentFrame >= count - 1) ScrubTo(0); // restart from the held last frame
        playing = true;
    }
    public void Pause() { playing = false; }
    public void TogglePlay() { if (playing) Pause(); else Play(); }

    public void ScrubTo(int frameIndex)
    {
        int count = FrameCount;
        if (count == 0) return;
        currentFrame = Mathf.Clamp(frameIndex, 0, count - 1);
        frameTimer = 0f;
    }

    void OnGUI()
    {
        if (!showDebugUI) return;

        int count = FrameCount;
        GUILayout.BeginArea(new Rect(10, 10, 340, 90), GUI.skin.box);
        GUILayout.Label(count == 0
            ? "MumaxVR playback — waiting for .ovf files in:\n" + resolvedFolder
            : string.Format("MumaxVR playback — frame {0}/{1}   t = {2:0.###} ns",
                currentFrame + 1, count, SimTime * 1e9));

        if (count > 0)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(60))) TogglePlay();
            int scrubbed = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(currentFrame, 0, count - 1, GUILayout.Width(240)));
            GUILayout.EndHorizontal();
            if (scrubbed != currentFrame) { Pause(); ScrubTo(scrubbed); }
        }
        GUILayout.EndArea();
    }
}
