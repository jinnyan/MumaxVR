// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using UnityEngine;

/// <summary>
/// Anything that can feed magnetization frames to a MumaxRenderer.
/// Phase 0: MumaxPlaybackManager (OVF files from disk).
/// Phase 1+: MumaxBridgeClient (live stream from the Go bridge process).
/// All arrays are in mumax coordinates (Z-up); the shader swizzles to Unity Y-up.
/// </summary>
public abstract class MumaxFrameSource : MonoBehaviour
{
    /// <summary>Hold while reading CellPositions/CellDirections from another thread.</summary>
    public readonly object DataLock = new object();

    /// <summary>Static cell positions in grid-index units, centered on the origin. Set once when the grid is known.</summary>
    public Vector3[] CellPositions { get; protected set; }

    /// <summary>Current magnetization unit vectors, x-fastest order. Replaced (not mutated) under DataLock.</summary>
    public Vector3[] CellDirections { get; protected set; }

    public Vector3Int GridSize { get; protected set; }
    public double SimTime { get; protected set; }

    public int CellCount { get { return CellPositions != null ? CellPositions.Length : 0; } }

    /// <summary>Build centered per-cell positions from grid dimensions (mumax x,y,z order).</summary>
    protected void BuildGridPositions(int nx, int ny, int nz)
    {
        var pos = new Vector3[nx * ny * nz];
        float cx = (nx - 1) * 0.5f, cy = (ny - 1) * 0.5f, cz = (nz - 1) * 0.5f;
        int idx = 0;
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    pos[idx++] = new Vector3(i - cx, j - cy, k - cz);

        lock (DataLock)
        {
            GridSize = new Vector3Int(nx, ny, nz);
            CellPositions = pos;
        }
    }
}
