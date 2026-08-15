using CgfConverter.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace CgfConverter.CryEngineCore.Chunks;

internal sealed class ChunkIvoLodMeshData_900 : ChunkIvoLodMeshData
{
    private const uint CafeBabeMagic = 0xCAFEBABE;

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        // The CAFEBABE geometry section's offset from the chunk start varies per file
        // (observed 0x58-0x10F00 across a sample of SC items) — it is NOT fixed.  Scan
        // the chunk's byte range for the magic instead of assuming a constant delta.
        //
        // A chunk can contain SEVERAL sections — one per mesh-bearing node — and they
        // are independent meshes with their own vertex pools and quantization frames,
        // not LODs of one another. Reading only the first (the behaviour before
        // 2026-08-14) meant every geometry node was handed the same section-0 data:
        // the Banu tachyon cannon barrel's 808/4310/360-vertex sections all surfaced
        // as three identical 658-vertex copies, losing the actual barrel.
        long chunkStart = Offset;
        long chunkEnd = Size > 0 ? Offset + Size : b.BaseStream.Length;
        var cafeOffsets = FindAllCafeBabe(b, chunkStart, chunkEnd);

        if (cafeOffsets.Count == 0)
        {
            HelperMethods.Log(LogLevelEnum.Warning,
                $"ChunkIvoLodMeshData: CAFEBABE not found in chunk range 0x{chunkStart:X}-0x{chunkEnd:X}");
            return;
        }

        for (int i = 0; i < cafeOffsets.Count; i++)
        {
            // Bound each section's parse by where the next one starts, so a section
            // whose descriptor table does not terminate cleanly cannot run on into
            // the following section's header.
            long sectionEnd = i + 1 < cafeOffsets.Count ? cafeOffsets[i + 1] : chunkEnd;
            var section = ReadSection(b, cafeOffsets[i], sectionEnd);
            if (section is not null)
                Sections.Add(section);
        }

        if (Sections.Count == 0)
        {
            HelperMethods.Log(LogLevelEnum.Warning,
                "ChunkIvoLodMeshData: no CAFEBABE section could be parsed");
            return;
        }

        HelperMethods.Log(LogLevelEnum.Debug,
            $"ChunkIvoLodMeshData: {Sections.Count} section(s); " +
            string.Join(", ", Sections.ConvertAll(s =>
                $"[verts={s.VertexCount}, idx={s.IndexCount}, descs={s.Descriptors.Count}]")));
    }

    /// <summary>
    /// Parses one CAFEBABE section starting at <paramref name="cafeOffset"/>. Returns null
    /// if the section's header or vertex data runs past <paramref name="sectionEnd"/>.
    /// </summary>
    private static IvoLodMeshSection? ReadSection(BinaryReader b, long cafeOffset, long sectionEnd)
    {
        b.BaseStream.Seek(cafeOffset + 4, SeekOrigin.Begin);  // past the magic itself

        // Header fields after CAFEBABE (alternating descriptor/value pairs at every 8 bytes)
        b.ReadUInt32();                         // +0x04 format version (256)
        b.ReadUInt32();                         // +0x08 unknown
        b.ReadUInt32();                         // +0x0C sentinel 0xFFFFFFFE
        b.ReadUInt32();                         // +0x10 sentinel 0xFFFFFFFF
        uint vertexCount  = b.ReadUInt32();     // +0x14
        b.ReadUInt32();                         // +0x18 unknown
        uint indexCount   = b.ReadUInt32();     // +0x1C
        b.ReadUInt32();                         // +0x20 unknown
        uint triCount     = b.ReadUInt32();     // +0x24
        b.ReadUInt32();                         // +0x28 unknown
        b.ReadUInt32();                         // +0x2C unknown (102)
        b.ReadUInt32();                         // +0x30 unknown
        b.ReadUInt32();                         // +0x34 unknown (20)
        b.ReadUInt32();                         // +0x38 unknown
        b.ReadUInt32();                         // +0x3C unknown (1)
        b.ReadUInt32();                         // +0x40 unknown (0)

        // Quantization scales: 3 × (descriptor u32, scale f32)
        float[] scale = new float[3];
        for (int i = 0; i < 3; i++)
        {
            b.ReadUInt32();                     // descriptor
            scale[i] = b.ReadSingle();
        }

        // Quantization centers: (0, cx, 0, cy, ~0, cz)
        b.ReadUInt32();
        float cx = b.ReadSingle();
        b.ReadUInt32();
        float cy = b.ReadSingle();
        b.ReadUInt32();
        float cz = b.ReadSingle();

        // Now at CAFEBABE+0x74 = vertex data start. Verify the declared payload fits
        // inside this section before allocating for it — a bad count would otherwise
        // read into the next section or past the chunk.
        long payloadBytes = (long)vertexCount * 8 + indexCount + triCount;
        if (vertexCount == 0 || b.BaseStream.Position + payloadBytes > sectionEnd)
        {
            HelperMethods.Log(LogLevelEnum.Warning,
                $"ChunkIvoLodMeshData: section at 0x{cafeOffset:X} declares " +
                $"verts={vertexCount}/idx={indexCount}/tri={triCount} which does not fit " +
                $"before 0x{sectionEnd:X} — skipping section");
            return null;
        }

        var section = new IvoLodMeshSection
        {
            VertexCount = vertexCount,
            IndexCount = indexCount,
            TriangleCount = triCount,
            QuantizationCenter = new Vector3(cx, cy, cz),
            QuantizationScale = new Vector3(scale[0], scale[1], scale[2]),
        };

        // Read SNORM16 positions (8 bytes each: i16 x, i16 y, i16 z, i16 pad=0x7FFF)
        var positions = new Vector3[vertexCount];
        for (uint i = 0; i < vertexCount; i++)
        {
            float x = b.ReadInt16() / 32767.0f * scale[0] + cx;
            float y = b.ReadInt16() / 32767.0f * scale[1] + cy;
            float z = b.ReadInt16() / 32767.0f * scale[2] + cz;
            b.ReadInt16(); // padding 0x7FFF
            positions[i] = new Vector3(x, y, z);
        }
        section.RawPositions = positions;

        // Read per-submesh local indices (uint8 each — 0-based within each submesh's vertex pool)
        var localIndices = new byte[indexCount];
        for (uint i = 0; i < indexCount; i++)
            localIndices[i] = b.ReadByte();
        section.RawLocalIndices = localIndices;

        // Read per-triangle material/submesh IDs (uint8 each)
        var triMatIDs = new byte[triCount];
        for (uint i = 0; i < triCount; i++)
            triMatIDs[i] = b.ReadByte();
        section.RawTriMatIDs = triMatIDs;

        // Read submesh descriptor table (16 bytes each, cumulative across this section's LODs).
        // Entry [0] is always a null header (cumIdx=0); real LOD0 entries follow.
        // Stop at indexCount, at the section boundary, or when the table stops ascending.
        uint prevCumIdx = 0;
        while (b.BaseStream.Position + 16 <= sectionEnd && section.Descriptors.Count < 512)
        {
            uint cumVerts = b.ReadUInt32();
            uint cumIdx   = b.ReadUInt32();
            uint cumTri   = b.ReadUInt32();
            uint packed   = b.ReadUInt32();

            // Stop if cumIdx decreases (end of descriptor table) or exceeds total count
            if (cumIdx < prevCumIdx || cumIdx > indexCount)
                break;

            section.Descriptors.Add((cumVerts, cumIdx, cumTri, packed));
            prevCumIdx = cumIdx;

            if (cumIdx >= indexCount)
                break;
        }

        return section;
    }

    /// <summary>
    /// Scans [start, end) for every little-endian CAFEBABE magic, verifying the
    /// format-version field (uint32, value 256) that always follows it at +0x04 to
    /// reject false positives. Returns offsets in ascending order.
    /// </summary>
    private static List<long> FindAllCafeBabe(BinaryReader b, long start, long end)
    {
        var offsets = new List<long>();
        long savedPos = b.BaseStream.Position;
        try
        {
            long rangeEnd = System.Math.Min(end, b.BaseStream.Length);
            int length = (int)(rangeEnd - start);
            if (length < 8)
                return offsets;

            b.BaseStream.Seek(start, SeekOrigin.Begin);
            byte[] window = b.ReadBytes(length);

            // Little-endian byte pattern for 0xCAFEBABE.
            byte[] pattern = [0xBE, 0xBA, 0xFE, 0xCA];

            for (int i = 0; i <= window.Length - 8; i++)
            {
                if (window[i] != pattern[0] || window[i + 1] != pattern[1] ||
                    window[i + 2] != pattern[2] || window[i + 3] != pattern[3])
                    continue;

                uint formatVersion = System.BitConverter.ToUInt32(window, i + 4);
                if (formatVersion == 256)
                    offsets.Add(start + i);
            }
            return offsets;
        }
        finally
        {
            b.BaseStream.Seek(savedPos, SeekOrigin.Begin);
        }
    }
}
