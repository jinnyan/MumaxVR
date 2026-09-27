// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Instanced glyph renderer for mumax cell grids (MumaxCellShader; color by mz
/// red/blue or mumax-style hue wheel). A culling pass (Resources/
/// MumaxCull.compute) rebuilds the visible-instance lists every frame:
///   - physics filter: cells (near-)parallel to all neighbors are hidden, so
///     only domain walls and other textured regions remain
///   - proximity LOD: full arrows near the viewer, thin sticks at mid
///     distance, nothing beyond the far distance
///   - region of interest: only glyphs inside a box / lens / slice render;
///     the handle is auto-created and grabbable in VR
/// Buffers are (re)allocated lazily because frames arrive asynchronously.
/// </summary>
public class MumaxRenderer : MonoBehaviour
{
    public enum RoiMode { None = 0, Box = 1, Lens = 2, Slice = 3 }
    public enum ColorMode { OutOfPlane = 0, HueWheel = 1 }

    [Tooltip("Leave empty to auto-create a low-poly shaft+cone arrow.")]
    public Mesh arrowMesh;
    [Tooltip("Leave empty to auto-create a material from Custom/MumaxCellShader.")]
    public Material arrowMaterial;

    [Tooltip("Size of the individual arrows")]
    public float scale = 0.5f;
    [Tooltip("Distance between the arrows")]
    public float gridSpacing = 1.0f;
    [Tooltip("Out Of Plane: red = +mz, blue = -mz, white = in-plane. Hue Wheel (mumax-style): in-plane angle = hue, mz = lightness — best for vortices and in-plane textures.")]
    public ColorMode colorMode = ColorMode.OutOfPlane;

    [Header("Physics-based filter")]
    [Tooltip("Hide cells whose magnetization is (near-)parallel to all 6 neighbors — only domain walls, vortices, skyrmions remain.")]
    public bool filterUniformCells = true;
    [Range(0f, 45f)]
    [Tooltip("A cell is kept if it differs from its most misaligned neighbor by at least this angle (degrees).")]
    public float minNeighborAngle = 2f;

    [Header("Proximity LOD")]
    [Tooltip("Full arrows near the camera, thin sticks at mid distance, hidden beyond the far distance.")]
    public bool distanceLod = true;
    [Tooltip("Within this distance (m) cells are full arrows.")]
    public float lodNearDistance = 8f;
    [Tooltip("Beyond this distance (m) cells are hidden entirely.")]
    public float lodFarDistance = 80f;

    [Header("Region of interest")]
    public RoiMode roiMode = RoiMode.None;
    [Tooltip("Transform defining the region. Box: unit cube scaled by the transform. Lens: sphere, radius = largest scale axis / 2. Slice: plane through the position, normal along forward. Leave empty to auto-create a grabbable handle.")]
    public Transform roiTransform;
    public Vector3 roiBoxSize = new Vector3(12, 12, 12);
    public float roiLensRadius = 6f;
    [Tooltip("Slice slab thickness in world units.")]
    public float sliceThickness = 1.5f;

    [Tooltip("Desktop control panel, drawn below the frame source's panel.")]
    public bool showDebugUI = true;

    private ComputeBuffer positionBuffer;
    private ComputeBuffer directionBuffer;
    private ComputeBuffer nearIdsBuffer;   // append: cells drawn as arrows
    private ComputeBuffer midIdsBuffer;    // append: cells drawn as sticks
    private ComputeBuffer allIdsBuffer;    // identity list for the no-compute fallback
    private ComputeBuffer argsNearBuffer;
    private ComputeBuffer argsMidBuffer;

    private MumaxFrameSource source;
    private ComputeShader cullShader;
    private int cullKernel = -1;

    private Material instancedMaterial;
    private MaterialPropertyBlock nearBlock;
    private MaterialPropertyBlock midBlock;
    private Mesh stickMesh;
    private Camera viewCamera;
    private int allocatedCount = -1;

    private GameObject autoHandle;
    private Material autoHandleMaterial;
    private RoiMode autoHandleMode = RoiMode.None;

    private int visibleArrows = -1;
    private int visibleSticks = -1;
    private float nextCountPoll;

    private static readonly string[] RoiLabels = { "none", "box", "lens", "slice" };
    private static readonly string[] ColorLabels = { "mz red/blue", "hue wheel" };

    void Start()
    {
        // Pick the ENABLED frame source. GetComponent returns disabled ones too,
        // so with both a PlaybackManager and a BridgeClient attached we must skip
        // whichever is unchecked instead of grabbing the first by add-order.
        var candidates = GetComponents<MumaxFrameSource>();
        foreach (var candidate in candidates)
        {
            if (candidate.enabled) { source = candidate; break; }
        }
        if (source == null)
        {
            // a source that disabled itself (e.g. bridge exe missing) already
            // logged the real reason — don't drown it in a generic error
            Debug.LogError(candidates.Length > 0
                ? "MumaxRenderer: the frame source on this GameObject is disabled — see the error above for why. Nothing will render."
                : "MumaxRenderer needs an enabled MumaxFrameSource (MumaxPlaybackManager or MumaxBridgeClient) on the same GameObject.");
            enabled = false;
            return;
        }

        if (arrowMesh == null) arrowMesh = BuildArrowMesh();
        stickMesh = BuildStickMesh();

        if (arrowMaterial == null)
        {
            Shader shader = Shader.Find("Custom/MumaxCellShader");
            if (shader == null)
            {
                Debug.LogError("MumaxRenderer: Custom/MumaxCellShader not found — assign a material manually.");
                enabled = false;
                return;
            }
            arrowMaterial = new Material(shader);
        }

        instancedMaterial = new Material(arrowMaterial);
        instancedMaterial.enableInstancing = true;
        nearBlock = new MaterialPropertyBlock();
        midBlock = new MaterialPropertyBlock();

        cullShader = Resources.Load<ComputeShader>("MumaxCull");
        if (cullShader != null && SystemInfo.supportsComputeShaders)
        {
            cullKernel = cullShader.FindKernel("CSCull");
        }
        else
        {
            cullShader = null;
            Debug.LogWarning("MumaxRenderer: MumaxCull.compute unavailable — filtering, LOD and ROI are disabled; drawing every cell.");
        }
    }

    void Update()
    {
        if (source == null || arrowMesh == null || instancedMaterial == null) return;

        int count = source.CellCount;
        if (count <= 0) return;

        if (count != allocatedCount) Reallocate(count);
        if (source.CellDirections == null) return; // grid known but no frame applied yet

        lock (source.DataLock)
        {
            if (source.CellDirections.Length == count)
                directionBuffer.SetData(source.CellDirections);
        }

        UpdateRoiHandle();

        bool culled = cullShader != null;
        if (culled)
        {
            DispatchCulling(count);
            PollVisibleCounts();
        }

        Bounds bounds = new Bounds(transform.position, new Vector3(1000, 1000, 1000));

        FillBlock(nearBlock, culled ? nearIdsBuffer : allIdsBuffer, 0f);
        Graphics.DrawMeshInstancedIndirect(arrowMesh, 0, instancedMaterial, bounds, argsNearBuffer, 0, nearBlock);

        if (culled && distanceLod)
        {
            // sticks shrink to nothing toward the far cutoff instead of popping
            FillBlock(midBlock, midIdsBuffer, lodFarDistance);
            Graphics.DrawMeshInstancedIndirect(stickMesh, 0, instancedMaterial, bounds, argsMidBuffer, 0, midBlock);
        }
    }

    void FillBlock(MaterialPropertyBlock block, ComputeBuffer visibleIds, float fadeEnd)
    {
        block.SetBuffer("_Positions", positionBuffer);
        block.SetBuffer("_Directions", directionBuffer);
        block.SetBuffer("_VisibleIds", visibleIds);
        block.SetFloat("_Scale", scale);
        block.SetFloat("_GridSpacing", gridSpacing);
        block.SetMatrix("_GridMatrix", transform.localToWorldMatrix);
        block.SetFloat("_ColorMode", (float)colorMode);
        block.SetFloat("_FadeStart", fadeEnd * 0.85f);
        block.SetFloat("_FadeEnd", fadeEnd);
    }

    void PollVisibleCounts()
    {
        if (Time.unscaledTime < nextCountPoll || !SystemInfo.supportsAsyncGPUReadback) return;
        nextCountPoll = Time.unscaledTime + 0.25f;
        AsyncGPUReadback.Request(argsNearBuffer, r => { if (!r.hasError) visibleArrows = (int)r.GetData<uint>()[1]; });
        AsyncGPUReadback.Request(argsMidBuffer, r => { if (!r.hasError) visibleSticks = (int)r.GetData<uint>()[1]; });
    }

    void DispatchCulling(int count)
    {
        nearIdsBuffer.SetCounterValue(0);
        midIdsBuffer.SetCounterValue(0);

        Vector3Int grid = source.GridSize;
        cullShader.SetInt("_Count", count);
        cullShader.SetInt("_Nx", grid.x);
        cullShader.SetInt("_Ny", grid.y);
        cullShader.SetInt("_Nz", grid.z);
        cullShader.SetFloat("_GridSpacing", gridSpacing);
        cullShader.SetMatrix("_GridMatrix", transform.localToWorldMatrix);

        if (viewCamera == null) viewCamera = Camera.main;
        cullShader.SetVector("_CamPos", viewCamera != null ? viewCamera.transform.position : transform.position);

        cullShader.SetInt("_FilterEnabled", filterUniformCells ? 1 : 0);
        cullShader.SetFloat("_FilterThreshold", 0.5f * (1f - Mathf.Cos(minNeighborAngle * Mathf.Deg2Rad)));

        cullShader.SetInt("_LodEnabled", distanceLod ? 1 : 0);
        cullShader.SetFloat("_LodNear", lodNearDistance);
        cullShader.SetFloat("_LodFar", Mathf.Max(lodFarDistance, lodNearDistance));

        int mode = roiTransform != null ? (int)roiMode : 0;
        cullShader.SetInt("_RoiMode", mode);
        if (mode != 0)
        {
            Vector3 s = roiTransform.lossyScale;
            cullShader.SetMatrix("_RoiWorldToLocal", roiTransform.worldToLocalMatrix);
            cullShader.SetVector("_RoiCenter", roiTransform.position);
            cullShader.SetFloat("_RoiRadius", Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f);
            cullShader.SetVector("_SliceNormal", roiTransform.forward);
            cullShader.SetVector("_SlicePoint", roiTransform.position);
            cullShader.SetFloat("_SliceHalfThickness", sliceThickness * 0.5f);
        }

        cullShader.SetBuffer(cullKernel, "_Positions", positionBuffer);
        cullShader.SetBuffer(cullKernel, "_Directions", directionBuffer);
        cullShader.SetBuffer(cullKernel, "_NearIds", nearIdsBuffer);
        cullShader.SetBuffer(cullKernel, "_MidIds", midIdsBuffer);
        cullShader.Dispatch(cullKernel, (count + 255) / 256, 1, 1);

        ComputeBuffer.CopyCount(nearIdsBuffer, argsNearBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midIdsBuffer, argsMidBuffer, sizeof(uint));
    }

    // ── Region-of-interest handle ──
    void UpdateRoiHandle()
    {
        // A user-assigned transform wins; the auto handle only fills the gap.
        bool userProvided = roiTransform != null && (autoHandle == null || roiTransform != autoHandle.transform);
        if (userProvided || roiMode == RoiMode.None)
        {
            if (autoHandle != null) autoHandle.SetActive(false);
            return;
        }

        if (autoHandle == null || autoHandleMode != roiMode)
        {
            if (autoHandle != null) Destroy(autoHandle);
            autoHandle = CreateRoiHandle(roiMode);
            autoHandleMode = roiMode;
        }
        autoHandle.SetActive(true);
        roiTransform = autoHandle.transform;
    }

    GameObject CreateRoiHandle(RoiMode mode)
    {
        GameObject go = GameObject.CreatePrimitive(
            mode == RoiMode.Box ? PrimitiveType.Cube :
            mode == RoiMode.Lens ? PrimitiveType.Sphere : PrimitiveType.Quad);
        go.name = "Mumax ROI " + mode + " (auto)";
        go.transform.position = transform.position;

        Vector3Int grid = source != null && source.GridSize.x > 0 ? source.GridSize : new Vector3Int(64, 64, 1);
        float extent = Mathf.Max(grid.x, Mathf.Max(grid.y, grid.z)) * gridSpacing;

        switch (mode)
        {
            case RoiMode.Box:
                go.transform.localScale = roiBoxSize;
                break;
            case RoiMode.Lens:
                go.transform.localScale = Vector3.one * roiLensRadius * 2f;
                break;
            case RoiMode.Slice:
                go.transform.localScale = new Vector3(extent * 1.1f, extent * 1.1f, 1f);
                // the quad's MeshCollider is paper-thin — swap for a box the ray can
                // hit; must be gone before XRGrabInteractable captures its colliders
                DestroyImmediate(go.GetComponent<Collider>());
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 1f, 0.02f);
                break;
        }

        if (autoHandleMaterial == null)
        {
            Shader shader = Shader.Find("Custom/MumaxRoiHandle");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            autoHandleMaterial = new Material(shader);
        }
        autoHandleMaterial.color = mode == RoiMode.Slice
            ? new Color(1f, 0.85f, 0.2f, 0.15f)
            : new Color(0.2f, 0.8f, 1f, 0.12f);
        go.GetComponent<MeshRenderer>().sharedMaterial = autoHandleMaterial;

        // The handle spawns around the grid center - usually right where the XR rig
        // stands. Its collider must not push the rig's CharacterController, or the
        // rig is shoved out (often down through the floor) and falls away with the
        // camera. The ray/direct interactors can still hit and grab the handle.
        foreach (Collider handleCollider in go.GetComponents<Collider>())
            foreach (CharacterController rig in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                Physics.IgnoreCollision(rig, handleCollider, true);

        // float in place, movable with the XR rig's ray/direct interactors
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        XRGrabInteractable grab = go.AddComponent<XRGrabInteractable>();
        grab.throwOnDetach = false;
        grab.useDynamicAttach = true; // grab where you point, don't snap to center

        return go;
    }

    // ── Buffers ──
    void Reallocate(int count)
    {
        ReleaseBuffers();

        // 1 Vector3 = 3 floats = 12 bytes
        positionBuffer = new ComputeBuffer(count, 12);
        directionBuffer = new ComputeBuffer(count, 12);
        nearIdsBuffer = new ComputeBuffer(count, sizeof(uint), ComputeBufferType.Append);
        midIdsBuffer = new ComputeBuffer(count, sizeof(uint), ComputeBufferType.Append);

        allIdsBuffer = new ComputeBuffer(count, sizeof(uint));
        uint[] ids = new uint[count];
        for (uint i = 0; i < (uint)count; i++) ids[i] = i;
        allIdsBuffer.SetData(ids);

        // instance counts are overwritten by CopyCount each frame when culling runs
        argsNearBuffer = MakeArgs(arrowMesh, cullShader != null ? 0 : count);
        argsMidBuffer = MakeArgs(stickMesh, 0);

        lock (source.DataLock)
        {
            positionBuffer.SetData(source.CellPositions);
        }

        allocatedCount = count;
    }

    static ComputeBuffer MakeArgs(Mesh mesh, int instances)
    {
        ComputeBuffer buffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        buffer.SetData(new uint[]
        {
            mesh.GetIndexCount(0), (uint)instances, mesh.GetIndexStart(0), mesh.GetBaseVertex(0), 0
        });
        return buffer;
    }

    static Mesh BuildArrowMesh()
    {
        // low-poly shaft + cone along +Y, height 2 (-1..1); unlike a capsule the
        // tip makes the direction readable without relying on color
        const int seg = 8;
        const float shaftR = 0.12f, headR = 0.32f, headBase = 0.25f;

        var verts = new List<Vector3>();
        for (int ring = 0; ring < 3; ring++)
        {
            float r = ring == 2 ? headR : shaftR;
            float y = ring == 0 ? -1f : headBase;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
            }
        }
        int tip = verts.Count; verts.Add(new Vector3(0, 1, 0));
        int bottom = verts.Count; verts.Add(new Vector3(0, -1, 0));

        var tris = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            tris.AddRange(new[] { i, seg + i, j, j, seg + i, seg + j });                         // shaft
            tris.AddRange(new[] { seg + i, 2 * seg + i, seg + j, seg + j, 2 * seg + i, 2 * seg + j }); // head underside
            tris.AddRange(new[] { 2 * seg + i, tip, 2 * seg + j });                              // cone
            tris.AddRange(new[] { i, j, bottom });                                               // bottom cap
        }

        Mesh mesh = new Mesh { name = "MumaxArrow" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildStickMesh()
    {
        // thin quad, same -1..1 height as the capsule; Cull Off shows both sides
        Mesh mesh = new Mesh { name = "MumaxStick" };
        const float w = 0.08f;
        mesh.vertices = new[]
        {
            new Vector3(-w, -1, 0), new Vector3(w, -1, 0),
            new Vector3(-w, 1, 0), new Vector3(w, 1, 0)
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    void ReleaseBuffers()
    {
        if (positionBuffer != null) { positionBuffer.Release(); positionBuffer = null; }
        if (directionBuffer != null) { directionBuffer.Release(); directionBuffer = null; }
        if (nearIdsBuffer != null) { nearIdsBuffer.Release(); nearIdsBuffer = null; }
        if (midIdsBuffer != null) { midIdsBuffer.Release(); midIdsBuffer = null; }
        if (allIdsBuffer != null) { allIdsBuffer.Release(); allIdsBuffer = null; }
        if (argsNearBuffer != null) { argsNearBuffer.Release(); argsNearBuffer = null; }
        if (argsMidBuffer != null) { argsMidBuffer.Release(); argsMidBuffer = null; }
    }

    void OnDestroy()
    {
        ReleaseBuffers();
        if (instancedMaterial != null) Destroy(instancedMaterial);
        if (stickMesh != null) Destroy(stickMesh);
        if (autoHandle != null) Destroy(autoHandle);
        if (autoHandleMaterial != null) Destroy(autoHandleMaterial);
    }

    void OnDrawGizmosSelected()
    {
        if (roiMode == RoiMode.None || roiTransform == null) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        switch (roiMode)
        {
            case RoiMode.Box:
                Gizmos.matrix = roiTransform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                break;
            case RoiMode.Lens:
                Vector3 s = roiTransform.lossyScale;
                Gizmos.DrawWireSphere(roiTransform.position, Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f);
                break;
            case RoiMode.Slice:
                Vector3 ls = roiTransform.lossyScale;
                Gizmos.matrix = Matrix4x4.TRS(roiTransform.position, roiTransform.rotation,
                    new Vector3(ls.x, ls.y, sliceThickness));
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                break;
        }
    }

    void OnGUI()
    {
        if (!showDebugUI) return;

        GUILayout.BeginArea(new Rect(10, 110, 340, 215), GUI.skin.box);
        if (cullShader == null)
            GUILayout.Label("MumaxVR renderer (no compute — culling off)");
        else if (visibleArrows >= 0)
            GUILayout.Label(string.Format("MumaxVR renderer — {0:n0} arrows + {1:n0} sticks of {2:n0} cells",
                visibleArrows, Mathf.Max(visibleSticks, 0), allocatedCount));
        else
            GUILayout.Label("MumaxVR renderer");

        filterUniformCells = GUILayout.Toggle(filterUniformCells, " physics filter — hide uniform cells");
        if (filterUniformCells)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("min angle " + minNeighborAngle.ToString("0.#") + "°", GUILayout.Width(105));
            minNeighborAngle = GUILayout.HorizontalSlider(minNeighborAngle, 0f, 45f);
            GUILayout.EndHorizontal();
        }

        distanceLod = GUILayout.Toggle(distanceLod, " proximity LOD — arrows / sticks / hidden");
        if (distanceLod)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("near " + lodNearDistance.ToString("0") + " m", GUILayout.Width(105));
            lodNearDistance = GUILayout.HorizontalSlider(lodNearDistance, 1f, 100f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("far " + lodFarDistance.ToString("0") + " m", GUILayout.Width(105));
            lodFarDistance = GUILayout.HorizontalSlider(lodFarDistance, lodNearDistance, 400f);
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("ROI", GUILayout.Width(40));
        roiMode = (RoiMode)GUILayout.SelectionGrid((int)roiMode, RoiLabels, 4);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("color", GUILayout.Width(40));
        colorMode = (ColorMode)GUILayout.SelectionGrid((int)colorMode, ColorLabels, 2);
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
