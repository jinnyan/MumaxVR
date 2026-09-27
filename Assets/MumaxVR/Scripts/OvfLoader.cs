// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Parsed content of one OVF 2.0 file (the format mumax3 writes via save()/autosave()).
/// </summary>
public class OvfFrame
{
    public int Nx, Ny, Nz;
    public Vector3 StepSize;   // cell size in meters
    public double SimTime;     // seconds, from the "Total simulation time" Desc line (0 if absent)
    public Vector3[] Vectors;  // x-fastest order, length Nx*Ny*Nz, mumax coordinates (Z-up)

    public int CellCount { get { return Nx * Ny * Nz; } }
}

/// <summary>
/// Reader for OVF 2.0 rectangular-mesh vector files (valuedim 3).
/// Supports "Data Text", "Data Binary 4" and "Data Binary 8" segments — mumax3
/// writes Binary 4 by default; Text is what our synthetic sample generator emits.
/// </summary>
public static class OvfLoader
{
    const float ControlB4 = 1234567.0f;
    const double ControlB8 = 123456789012345.0;

    public static OvfFrame Load(string path)
    {
        // ReadWrite share: mumax/the generator may still hold the file open
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            return Load(fs, path);
        }
    }

    static OvfFrame Load(Stream s, string name)
    {
        var frame = new OvfFrame();
        int valueDim = 0;
        string dataFormat = null;

        // ── Header: "# key: value" lines until "# Begin: Data <format>" ──
        string line;
        while ((line = ReadLine(s)) != null)
        {
            if (line.Length == 0 || line[0] != '#') continue;
            string body = line.Substring(1).Trim();

            int colon = body.IndexOf(':');
            if (colon < 0) continue;
            string key = body.Substring(0, colon).Trim().ToLowerInvariant();
            string val = body.Substring(colon + 1).Trim();

            if (key == "begin" && val.StartsWith("Data", StringComparison.OrdinalIgnoreCase))
            {
                dataFormat = val.Substring(4).Trim();
                break;
            }

            switch (key)
            {
                case "xnodes": frame.Nx = ParseInt(val); break;
                case "ynodes": frame.Ny = ParseInt(val); break;
                case "znodes": frame.Nz = ParseInt(val); break;
                case "xstepsize": frame.StepSize.x = (float)ParseDouble(val); break;
                case "ystepsize": frame.StepSize.y = (float)ParseDouble(val); break;
                case "zstepsize": frame.StepSize.z = (float)ParseDouble(val); break;
                case "valuedim": valueDim = ParseInt(val); break;
                case "desc": TryParseSimTime(val, frame); break;
            }
        }

        if (dataFormat == null)
            throw new IOException("OVF: no data segment found in " + name);
        if (frame.Nx <= 0 || frame.Ny <= 0 || frame.Nz <= 0)
            throw new IOException("OVF: missing/invalid node counts in " + name);
        if (valueDim != 3)
            throw new IOException("OVF: valuedim " + valueDim + " unsupported (need 3) in " + name);

        int n = frame.CellCount;
        frame.Vectors = new Vector3[n];

        switch (dataFormat.ToLowerInvariant())
        {
            case "text": ReadText(s, frame, name); break;
            case "binary 4": ReadBinary4(s, frame, name); break;
            case "binary 8": ReadBinary8(s, frame, name); break;
            default: throw new IOException("OVF: unsupported data format '" + dataFormat + "' in " + name);
        }

        return frame;
    }

    static void ReadText(Stream s, OvfFrame frame, string name)
    {
        char[] sep = { ' ', '\t' };
        for (int i = 0; i < frame.Vectors.Length; i++)
        {
            string line = ReadLine(s);
            if (line == null)
                throw new IOException("OVF: unexpected EOF at row " + i + " in " + name);
            if (line.Length > 0 && line[0] == '#') { i--; continue; }

            string[] parts = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                throw new IOException("OVF: malformed data row " + i + " in " + name);

            frame.Vectors[i] = new Vector3(
                (float)ParseDouble(parts[0]),
                (float)ParseDouble(parts[1]),
                (float)ParseDouble(parts[2]));
        }
    }

    static void ReadBinary4(Stream s, OvfFrame frame, string name)
    {
        byte[] ctl = ReadExactly(s, 4, name);
        if (Math.Abs(BitConverter.ToSingle(ctl, 0) - ControlB4) > 1f)
            throw new IOException("OVF: bad Binary 4 control number in " + name);

        int count = frame.Vectors.Length * 3;
        byte[] raw = ReadExactly(s, count * 4, name);
        float[] vals = new float[count];
        Buffer.BlockCopy(raw, 0, vals, 0, raw.Length);

        for (int i = 0; i < frame.Vectors.Length; i++)
            frame.Vectors[i] = new Vector3(vals[i * 3], vals[i * 3 + 1], vals[i * 3 + 2]);
    }

    static void ReadBinary8(Stream s, OvfFrame frame, string name)
    {
        byte[] ctl = ReadExactly(s, 8, name);
        if (Math.Abs(BitConverter.ToDouble(ctl, 0) - ControlB8) > 1.0)
            throw new IOException("OVF: bad Binary 8 control number in " + name);

        int count = frame.Vectors.Length * 3;
        byte[] raw = ReadExactly(s, count * 8, name);
        double[] vals = new double[count];
        Buffer.BlockCopy(raw, 0, vals, 0, raw.Length);

        for (int i = 0; i < frame.Vectors.Length; i++)
            frame.Vectors[i] = new Vector3((float)vals[i * 3], (float)vals[i * 3 + 1], (float)vals[i * 3 + 2]);
    }

    // Byte-wise line reader so the stream position stays exact for the binary payloads.
    static string ReadLine(Stream s)
    {
        var sb = new StringBuilder(96);
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            if (b == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)b);
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd('\r') : null;
    }

    static byte[] ReadExactly(Stream s, int count, string name)
    {
        byte[] buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int read = s.Read(buf, off, count - off);
            if (read <= 0) throw new IOException("OVF: unexpected EOF in binary data of " + name);
            off += read;
        }
        return buf;
    }

    // mumax writes: "Desc: Total simulation time:  1.5e-09  s"
    static void TryParseSimTime(string desc, OvfFrame frame)
    {
        int idx = desc.LastIndexOf("time:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        string[] parts = desc.Substring(idx + 5).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        double t;
        if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out t))
            frame.SimTime = t;
    }

    static int ParseInt(string v) { return int.Parse(v, CultureInfo.InvariantCulture); }
    static double ParseDouble(string v) { return double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture); }
}
