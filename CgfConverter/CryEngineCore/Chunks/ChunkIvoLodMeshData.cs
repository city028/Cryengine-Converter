using CgfConverter.Models.Structs;
using System.Collections.Generic;
using System.Numerics;

namespace CgfConverter.CryEngineCore;

/// <summary>
/// One CAFEBABE geometry section within an IvoLodMeshData chunk.
/// A chunk holds one section per mesh-bearing node in the file's NodeMeshCombo
/// (verified 2026-08-14: node/section counts match exactly, in order, across
/// single- and multi-section files). Each section carries its own vertex pool,
/// index pool and quantization frame — they are independent meshes, not LODs of
/// one another.
/// </summary>
public sealed class IvoLodMeshSection
{
    public uint VertexCount { get; set; }
    public uint IndexCount { get; set; }
    public uint TriangleCount { get; set; }
    public Vector3 QuantizationCenter { get; set; }
    public Vector3 QuantizationScale { get; set; }

    /// <summary>Decoded positions for ALL LODs in this section (use Descriptors for the LOD0 range).</summary>
    public Vector3[]? RawPositions { get; set; }

    /// <summary>Per-submesh local indices for ALL LODs (uint8, indexed as localIndices[globalIndexPosition]).</summary>
    public byte[]? RawLocalIndices { get; set; }

    /// <summary>Per-triangle submesh IDs for ALL LODs.</summary>
    public byte[]? RawTriMatIDs { get; set; }

    /// <summary>
    /// All descriptor entries (including null header at [0]).
    /// Each entry: (cumVertices, cumIndices, cumTriangles, packed), cumulative across all LODs.
    /// LOD0 submeshes = entries [1..count-1].
    /// </summary>
    public List<(uint CumVerts, uint CumIdx, uint CumTri, uint Packed)> Descriptors { get; set; } = [];
}

public class ChunkIvoLodMeshData : Chunk
{
    /// <summary>
    /// Every CAFEBABE section in the chunk, in file order. Before 2026-08-14 only the
    /// first was read, so a file whose geometry spans several sections silently lost
    /// all but one mesh — every geometry node was handed the same section-0 data.
    /// </summary>
    public List<IvoLodMeshSection> Sections { get; protected set; } = [];

    // ── Section[0] convenience accessors ────────────────────────────────────────
    // Existing callers (and single-section files, the common case) read these
    // directly; they mirror the first section so behaviour there is unchanged.

    public uint VertexCount => Sections.Count > 0 ? Sections[0].VertexCount : 0;
    public uint IndexCount => Sections.Count > 0 ? Sections[0].IndexCount : 0;
    public uint TriangleCount => Sections.Count > 0 ? Sections[0].TriangleCount : 0;
    public Vector3 QuantizationCenter => Sections.Count > 0 ? Sections[0].QuantizationCenter : Vector3.Zero;
    public Vector3 QuantizationScale => Sections.Count > 0 ? Sections[0].QuantizationScale : Vector3.Zero;
    public Vector3[]? RawPositions => Sections.Count > 0 ? Sections[0].RawPositions : null;
    public byte[]? RawLocalIndices => Sections.Count > 0 ? Sections[0].RawLocalIndices : null;
    public byte[]? RawTriMatIDs => Sections.Count > 0 ? Sections[0].RawTriMatIDs : null;

    public List<(uint CumVerts, uint CumIdx, uint CumTri, uint Packed)> RawDescriptors =>
        Sections.Count > 0 ? Sections[0].Descriptors : [];
}
