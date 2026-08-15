# Session Handover — IvoLodMeshData Support for SC 4.5+ .cga Files

**Date:** 2026-04-21 (session 1-2), updated 2026-08-06 (session 3), updated 2026-08-11 (session 4),
updated 2026-08-12 (session 5, then session 6 same day), updated 2026-08-13 (session 7),
updated 2026-08-14 (session 8, then session 9 same day)
**Branch:** release/v2.0
**Deployed binary:** `D:\projects\sc-scrafting-sync\Tools\cgf-converter.exe` (**rebuilt and redeployed in
session 9** with three new fixes — multi-section CAFEBABE reading, the half-scale/wrong-origin decode
correction, and the unbuildable-section guards. Previous binary backed up to
`D:\projects\sc-scrafting-sync\Tools\bak\cgf-converter_20260814_161500.exe`. All prior sessions' fixes are
still included.)

> ✅ **This repo is now under git** (2026-08-15), pushed to `https://github.com/city028/Cryengine-Converter`
> as `master`, tagged `v2.1.0`. All prior work, including session 9's four edited files, is captured in the
> initial commit `dd0f477` on top of the fork's existing `v2.0` history. `bak/*.cs` timestamped copies remain
> the local editing-backup convention (see root `CLAUDE.md`) but are no longer the only history — use `git log`
> / `git diff` going forward. `bak/` and `sync.ffs_db` are gitignored and stay local-only.

---

## Session 9 Update (2026-08-14, same day as session 8): Multi-section CAFEBABE reading implemented; found and fixed a universal half-scale decode; unbuildable sections no longer invalidate whole files

Session 8 ended with the ship-weapon combine working for one item via a Blender-side workaround. Session 9
removed the need for that workaround by fixing the converter, and in doing so found a much more basic defect
that had been present since IvoLodMeshData support was first written.

### Fix 1 — every CAFEBABE section is now read (`ChunkIvoLodMeshData_900.cs`, `CryEngine.cs`)

`Read()` scanned for a single CAFEBABE magic and stopped; the old comment even said so ("only the first
(primary) section is read here"). `BuildNodeStructure()` then built one `GeometryInfo` and assigned **the
same object** to every `GeometryType == Geometry` node. A file whose geometry spans several sections
therefore rendered the same mesh two or three times and lost the rest.

**Verified the mapping before writing any fix** — sections correspond 1:1 to Geometry-type nodes, in file
order, cross-checked against the independent Python decoder in `sc_crafting_sync/classify_multipart.py`:

| file | sections (verts) | Geometry nodes | old output |
|---|---|---|---|
| `banu_tac_can_s1_bar_1.cga` | 3 (808 / 4310 / 360) | 3 | 3 x 658 identical |
| `espr_las_can_s1_mec_1.cga` | 2 (5164 / 3575) | 2 | 2 x 4925 identical |
| `espr_las_can_s1_bar_1.cga` | 1 (10926) | 1 | 1 x 10874 correct |

New `IvoLodMeshSection` type plus a `Sections` list on `ChunkIvoLodMeshData`; the old single-section
properties remain as section-0 accessors so existing callers are unaffected. Each section's parse is bounded
by the next section's offset, so a malformed descriptor table cannot run on into the following header.

### Fix 2 — every Ivo lod mesh was decoding at half scale on the wrong origin (`CryEngine.cs`)

Found while chasing a fragment that stayed misplaced after fix 1. For **every** sample measured, the owning
node's `BoundingBoxMin/Max` is *exactly 2x* the section's decoded extent, and the section's decoded centre
equals its `QuantizationCenter` to the last digit:

| file | node bbox size | section size | ratio |
|---|---|---|---|
| `cool_acom_s01_pl02` (reference) | 0.7504, 0.4676, 0.2511 | 0.3752, 0.2338, 0.1255 | 2.000 |
| `espr_las_can_s1_bar_1` | 0.0974, 0.7438, 0.1920 | 0.0487, 0.3719, 0.0960 | 2.000 |
| `banu_tac_can_s1_pow_1` (sec 0) | 0.1646, 0.4143, 0.2948 | 0.0823, 0.2071, 0.1474 | 2.000 |

The decisive evidence is external to the converter: the ESPR barrel's node box (0.0974, 0.7438, 0.1920)
matches that part's entity-XML `inventoryOccupancyLocalBounds` (0.098, 0.744, 0.192) exactly. So the node box
is true model space, and the decode was landing at half size on its quantization origin.

`BuildLodMeshGeometry` now takes the owning node's bounding box and applies
`v = (v_raw - QuantizationCenter) * IvoLodMeshScaleCorrection + nodeBoxCentre`, the correction constant being
2.0f (`QuantizationScale` is 0.5 in every sample — it behaves as a half-extent). When no node box is
available (files whose `NodeMeshCombo` reports zero nodes, e.g. the shared radar meshes) only the scale is
corrected, about the section's own centre — there is no second source to place it against, and such files are
always rendered alone.

**This corrects session 8's conclusion.** Session 8 saw the same 2.0x ratio on the trusted cooler reference
and concluded the stored bbox was not a valid comparison target. It was valid; the defect was simply
universal, so the control sample was wrong in the same direction.

Practical consequence: geometry got visibly **bigger**, which reads as a regression. Barrels are longer
because they had been half-length all along — confirmed by the user against in-game knowledge.

### Fix 3 — an unbuildable section no longer invalidates the whole file (`CryEngine.cs`)

Surfaced by spot-checking Reign-3 Repeater, which failed with Blender's `Couldn't parse glTF`.

- When `BuildLodMeshGeometry` declines a section it returns an empty `GeometryInfo`; the caller attached it
  regardless, writing `null` into the glTF `meshes` array and invalidating the entire file. Pre-existing —
  this is the old CVSA `meshes: [None, None]` symptom — but fix 1 made it common, since section 0 was
  previously duplicated to every node and was usually valid. Such nodes are now emitted without a mesh.
- A second failure on the same item came from `DMG_base_body`: 87 vertices but indices up to 93, and an index
  count not divisible by 3. Added an index-range validation that declines the section like the corrupt case.

Across the tested fleet every section actually skipped is a `glow_*` marker or a `DMG_*` damage-state
variant — never visible geometry.

### Regression verification

`cool_acom_s01_pl02.cga` and `radr_grnp_s02_pl01.cga` produced **byte-identical** `.glb` output through fixes
1 and 3, and keep identical vertex counts (17,026 / 19,973) through fix 2 with only the intended
scale/position change. `espr_las_can_s1_bar_1.cga` reported "changed" with a **byte-identical geometry
buffer** — the difference is dummy material colours, which `CryEngine.cs` generates with `new Random()` per
run when a `.mtl` cannot be resolved. Any future "same input, different output" report on an asset with
missing materials is probably this, not conversion non-determinism; compare the BIN chunk, not the whole file.

### Files changed (backed up to `bak/` with the `20260814_160500` stamp)

- `CgfConverter/CryEngineCore/Chunks/ChunkIvoLodMeshData.cs` — new `IvoLodMeshSection`, `Sections` list
- `CgfConverter/CryEngineCore/Chunks/ChunkIvoLodMeshData_900.cs` — reads all sections, bounded per section
- `CgfConverter/CryEngine/CryEngine.cs` — per-section geometry, node-box mapping, both guards
- `CgfConverterTestingConsole/Program.cs` — `RunCustom()` diagnostics (node bbox vs. section extent);
  **left in place rather than restored to placeholder**, since the open issue below will want it

### Impact and what remains open

Ship-weapon auto-combine went from **4 to 52 items** in `sc_crafting_sync`, 34 of them previously blocked by
multi-section parts.

**Open — the "no per-LOD boundary marker" limitation is now the next real converter issue.** DR Model-XJ2's
`barrel_02` renders truncated: `BuildLodMeshGeometry` takes `lod0VertCount` from the last descriptor entry,
covering 1941 of that section's 2105 vertices and dropping 164. Its sibling `barrel_01` drops a comparable
174 and looks correct, so the boundary lands differently relative to real geometry in the two sections. This
is exactly the limitation flagged in the original architecture notes at the bottom of this file ("the code
uses ALL descriptors … this probably needs refinement"). Parked deliberately at the user's direction.

---

## Session 8 Update (2026-08-14): Disproved session 7's "2x scale" lead; found the real cross-format (skinMesh vs. lodMesh) split; fix implemented and deployed to `sc_crafting_sync`; barrel confirmed by user, ventilation re-check pending

**Problem investigated:** continuation of session 7's stopping point — the barrel's `IvoLodMeshData`-decoded
geometry appeared to be exactly 2x too large versus its own chunk-stored `NodeMeshCombo` bounding box,
correlating with descriptor count (62 vs. the chassis's 42), pointing at the documented "no per-LOD boundary
marker" limitation in `BuildLodMeshGeometry`.

### Finding #1 (confirmed): the "2x scale" lead was a false lead, not a real per-file bug

Extended `CgfConverterTestingConsole`'s `RunCustom()` to dump full `IvoLodMeshData` descriptor tables (including
the `Packed` field, previously never inspected) and `NodeMeshCombo` per-node bounding boxes, then compared
stored-bbox-vs-decoded-span for three files: the barrel (62 descriptors), the chassis (42 descriptors), and —
critically — `cool_acom_s01_pl02.cga`, the untouched, long-verified session-1/2 reference baseline. **All three
showed the identical, clean 2.0x span discrepancy**, including the known-good reference file that nobody has
ever doubted. Verified this wasn't a bug in my own diagnostic by exporting a real GLB for `cool_acom` via the
deployed `cgf-converter.exe` and confirming its actual accessor `min`/`max` matched my raw `RunCustom`
computation exactly (not the stored bbox). Conclusion: `NodeMeshCombo.BoundingBoxMin/Max` was never a valid
ground-truth reference for comparing against `IvoLodMeshData`-decoded geometry, for *any* file — the barrel was
never decoding at the wrong scale. Session 7's Fix #2 candidate is disproven.

### Finding #2 (confirmed): the chassis and barrel don't even use the same geometry decode path

Having ruled out a scale bug, regenerated a **fresh** chassis GLB (the pre-existing one in
`sc_crafting_sync\glb\` turned out to be stale, from an earlier converter build — a real trap, caught by
comparing its bbox against a value that shouldn't have matched anything and finding it matched the "unreliable"
stored bbox suspiciously exactly) and found its real vertex positions matched **neither** my raw
`IvoLodMeshData` computation **nor** a simple 2x of it — a completely different absolute coordinate region, not
just a different scale.

Root cause: `espr_las_can_s1_bas.cgf` has a companion `.cgfm` file carrying an `IvoSkin2` chunk.
`CryEngine.cs` (~line 143-159) checks for a skin companion **before** falling back to `IvoLodMeshData`, and the
chassis has one, so it renders via `BuildNodeGeometryInfo(skinMesh, subsets)` — a completely different code
path from `BuildLodMeshGeometry`, one this investigation had never examined. Confirmed the mechanism precisely:
`BuildNodeGeometryInfo` returns `VertUVs = skinMesh.VertsUvs` (not `Vertices`), which routes through
`WriteMeshOrLogError`'s `VertUVs`-specific branch (`BaseGltfRenderer.Geometry.cs` ~948-976) — this branch
rescales the raw `VertUV` data into `NodeMeshCombo`'s own stored `MinBound`/`MaxBound` via
`(x * multiplerVector) + boundaryBoxCenter`. **This is exactly why the stored bbox looked authoritative for the
chassis specifically** — for a skinned mesh, it genuinely *is* the decode target, by design — while being
completely irrelevant for `IvoLodMeshData`-path files (radar, `cool_acom`, and the barrel, none of which have a
skin companion and so take the simple `verts is not null` branch with no bbox involvement at all).

**This reframes item 13 entirely.** It was never a same-format scale/offset mismatch between two files using
the same decode — it's a genuine cross-format problem: the chassis renders correctly via the skinned-mesh path
(confirmed: its real position is already near-origin, matching `NodeMeshCombo`'s bbox by construction — no
chassis-side fix needed), while the barrel genuinely uses the raw lod-mesh path with its own large, arbitrary,
unrelated `QuantizationCenter`. Nothing in the pipeline today relates these two coordinate spaces to each other.

### Finding #3 (confirmed): the actual combining transform, and it differs by which decode path the part uses

Since only an attached part needs repositioning, the chassis being already correct, the formula depends on
whether *that part's own file* has a skin companion:

```
# No skin companion (e.g. barrel) — raw IvoLodMeshData decode, absolute quantized coords:
offset(cry) = hardpoint.Translation − part.QuantizationCenter

# Has a skin companion (e.g. ventilation) — skinned decode, already centered near its own origin:
offset(cry) = hardpoint.Translation
```

i.e. for the quantized case, recenter the part to its own local origin (undo its arbitrary quantization center),
then place it via the (already-correctly-exported, since session 7's Fix #1) hardpoint position; for the skinned
case, just translate. **`part.QuantizationCenter` needs no converter change to reach the consumer** — it turned
out to be numerically identical to the exported mesh's own bounding-box center (unsurprising in hindsight: SNORM
encoding is literally `center ± scale × normalized`, so the decoded bbox is symmetric around the center by
construction), so it's computable directly from the glTF's own accessor `min`/`max` without touching this repo.

Hand-computed bounding-box arithmetic for the barrel case looked very promising: X range fully contained within
the chassis's, Y overlapping with the barrel protruding past the chassis body (physically correct for a gun
barrel), Z fully within the chassis's range.

**The first empirical test failed, and the reason is the most important implementation detail here.** Testing
in a standalone headless-Blender script against fresh GLBs initially produced a nonsense result. Two distinct
Blender-side traps, both found by dumping raw matrices rather than reasoning about them:

1. **The offset must be applied with NO CryEngine→glTF axis swap.** Blender's glTF importer applies its own
   Y-up→Z-up correction on import, which cancels the C#-side `SwapAxesForPosition` **exactly**. Net effect:
   objects imported via `bpy.ops.import_scene.gltf` sit at coordinates numerically *identical to raw CryEngine
   space*. Confirmed by dumping `matrix_local` after import — `bar_lvl1` reads `(0, −0.3109, 0.1543)`, matching
   its raw `BoneToWorld` CryEngine value directly, unswapped. Applying `SwapAxesForPosition` on the Blender side
   (the intuitive move) is what broke the first attempt and nearly caused a correct hypothesis to be abandoned.
2. **`parent_clear()` is required before repositioning.** A direct `matrix_world` write is silently recomputed
   from the import hierarchy's parent chain on the next depsgraph update. Note also the imported objects use
   `rotation_mode='QUATERNION'`, so resetting `rotation_euler` alone is a no-op.

**Verified visually from four angles (front/side/top/iso), not merely by bounding-box overlap** — bbox
containment alone can't rule out a rotation error. Result: the barrel was perfectly centered on the chassis
centerline (top view), correctly seated in the muzzle housing (front view), and protruded forward by a
plausible amount (side view). This is the same weapon that previously rendered with the barrel floating ~3
units away.

**Sent the same style of comparison render to the `sc_crafting_sync` user for sign-off — user approved barrel
and ventilation both, but ventilation's approval turned out to be based on a wrong render.** The verification
script's `hardpoint_ven` value had been hand-copied from an earlier tool-call transcript with Y and Z swapped —
a transcription slip, not a math error. This was caught (not shipped silently) because the real implementation,
built afterward, reads hardpoint positions from the scene's actual empties rather than any copied constant, and
its output bounding box didn't match the approved render — that discrepancy was chased down rather than
dismissed, which is what surfaced the bug. The barrel's transcribed constant was correct by luck and its bbox is
identical across both rounds, so that confirmation stands; ventilation does not yet have a valid user
confirmation. See `sc_crafting_sync`'s own `SESSION-HANDOVER.md` for the corrected renders and outcome.

### Implemented and deployed — on the `sc_crafting_sync` side, not in this repo

The converter's decode is correct for both files; they simply live in different coordinate spaces by design
(one raw-quantized, one skin-rescaled), and relating them was entirely a consumer-side (Python/Blender) problem.
No production code was changed in this repo this session — `RunCustom()` was extended for diagnostics, then
restored to placeholder per convention (verified it still builds cleanly). `Tools\cgf-converter.exe` is
unchanged from session 7.

**Correction to this session's earlier note**: a `QuantizationCenter`-export converter change was floated as
"worth doing" mid-session but turned out to be unnecessary once it was confirmed the value is recoverable from
the glTF's own accessor bounds (see Finding #3) — no converter change was needed or made.

`renderer/blender_render.py`'s `--hardpoint-map` handling (~line 226) was rewritten in `sc_crafting_sync` per
Finding #3: clears parent, determines quantized-vs-skinned by comparing the part's own bbox-center distance
from origin against its size, applies the appropriate offset. Old code did
`obj.matrix_world = hp_matrix @ obj.matrix_world` — composing the hardpoint onto the part's own baked-in
`BoneToWorld`, which for the barrel is the exact negative of the hardpoint and cancels to identity. That's the
literal mechanism behind the "applied the fix, nothing visibly changed" symptom logged in session 7.

FiringMechanism/PowerArray remain excluded by the existing `_ATTACHED_PART_SUBTYPES` allowlist and are
untested — session 4's note that they "rendered disconnected" predates every fix since, so that exclusion is
worth re-testing in `sc_crafting_sync` rather than treating as settled.

Diagnostic scripts (scratch-only, not committed, lived in `sc_crafting_sync`'s Claude scratchpad):
`debug_import.py` (dumps post-import transform state — the tool that actually cracked this),
`test_combine2.py`/`build_assembly.py` (placement logic), `test_multiangle.py`/`render_shaded.py` (4-angle
visual verification, later rebuilt to read hardpoints from the scene instead of hand-copied constants after the
transcription bug above was found).

---

## Session 7 Update (2026-08-13): Fixed a real glTF-export bug (Ivo node transforms silently zeroed) — deployed and verified; found a second, deeper, still-unresolved issue (2x mesh scale on multi-descriptor files)

**Problem investigated:** `sc_crafting_sync`'s ship-weapon "missing barrel" feature (item 13) was
re-attempted after item 12 (cgf-converter non-determinism) turned out to no longer reproduce —
confirmed the specific repro file (`espr_las_can_s1_bar_1.cga`) has migrated off the old
"traditional Crydata + .cgam companion" format entirely and is now `#ivo`/CAFEBABE, same as 28
other checked ship-weapon files. Rebuilt the Phase 1/3/4 assembly feature fresh; the barrel and
ventilation attachments converted cleanly but rendered as a disconnected chunk floating far from
the chassis — same visual symptom as the old (unrelated) non-determinism bug, but this time fully
reproducible and investigatable, at the user's suggestion, from first principles: CryEngine's own
attachment-socket convention (named helper/dummy nodes on the parent skeleton, referenced by
attached-part XML) rather than assuming it was the same already-documented Karna Rifle SNORM-clamp
bug.

### Fix #1 (confirmed, deployed): `ChunkNode.LocalTransform` double-transposes Ivo-format node transforms, silently zeroing translation

**Root cause:** `ChunkNode.LocalTransform => Matrix4x4.Transpose(Transform)` is a blanket full-4x4
transpose. This is correct for traditional-format nodes (`ChunkNode_824.Read()` reads the raw
on-disk matrix with rotation already in a layout requiring exactly one transpose to become
.NET-native). It is **wrong** for Ivo-format nodes: `CryEngine.cs BuildNodeStructure()` builds
`Transform` via `Matrix3x4.ConvertToLocalTransformMatrix()`, which *already* pre-transposes the
rotation block and places translation in M41-M43 (.NET's native slot, what `Matrix4x4.Decompose()`
reads). Applying `LocalTransform`'s transpose on top double-transposes the rotation back to the
wrong orientation and — critically — moves the translation out of M41-M43 into M14/M24/M34, which
`Decompose()` never reads, so it silently comes back as `Vector3.Zero`.

**Confirmed precisely:** ESPR Laser Cannon S1's chassis file (`espr_las_can_s1_bas.cgf`) has 4
named empty helper nodes — `hardpoint_bar`, `hardpoint_mec`, `hardpoint_pow`, `hardpoint_ven` —
each with a real, distinct, non-zero position (verified via `--dump-nodes`, which reads
`node.Transform` directly, e.g. `hardpoint_bar` at CryEngine-space `(0, 0.311, -0.154)`). The
exported GLB showed these same nodes present and correctly named, but with `translation: null`
(= identity) for all of them — the position data was real internally but never reached the output
file.

**Fix (scoped, minimal):** in `BaseGltfRenderer.Geometry.cs`'s `CreateGltfNode`, branch on
`cryData.IsIvoFile`: for Ivo files, decompose `cryNode.Transform` directly (already correct,
ready-to-decompose); for traditional files, keep using `cryNode.LocalTransform` exactly as before.
Did **not** touch the shared `ChunkNode.LocalTransform` property itself, since Collada's renderer
also consumes it and traditional-format node transforms are known-working — a blanket fix there
risked regressing formats/renderers never re-verified this session.

```csharp
var transformedMatrix = SwapAxes(cryData.IsIvoFile ? cryNode.Transform : cryNode.LocalTransform);
```

**Verified:**
- Re-exported the chassis GLB: `hardpoint_bar` now shows `translation=[0.0, -0.1543, -0.3109]` —
  matches `--dump-nodes`' raw value exactly once run through the documented CryEngine→glTF axis
  swap `(X,Y,Z) → (X,Z,-Y)`.
- **Zero regression**, checked against the two most load-bearing reference items from prior
  sessions: `cool_acom_s01_pl02.cga` (session 1-2's original baseline) — 17,026 vertices, exact
  match. `radr_grnp_s01_pl01.cga` (session 5's radar fix) — 15,029 vertices, exact match. Both are
  single-node rigid meshes with identity node transforms, so the fix is a no-op for them by
  construction (translation was already correctly zero) — this confirms the fix only changes
  behavior for the specific case it targets.
- Rebuilt Release, `dotnet publish cgf-converter/cgf-converter.csproj -c Release -r win-x64`,
  deployed to `Tools\cgf-converter.exe`.

**This fix is real and worth keeping independent of what follows** — it makes hardpoint/socket
data usable at all, for any future attempt at this feature or anything else that needs Ivo
node-hierarchy positions (e.g. a correct future fix for FiringMechanism/PowerArray, or non-ship
uses of named attachment points).

### Fix #1 was necessary but not sufficient — attaching the barrel at the hardpoint doesn't produce a connected result

Wired the now-correct hardpoint transform through to `sc_crafting_sync`'s Python side (position
each attached part's imported Blender objects at the chassis's corresponding hardpoint empty).
Result: **still disconnected**, no visible improvement. Debugged directly in Blender (standalone
script importing chassis + barrel, printing matrices at each step):

- The barrel file's own mesh node (`bar_lvl1`) carries a baked-in local transform of
  `(0, -0.3109, 0.1543)` — the **exact negative** of `hardpoint_bar`'s position, to 8 significant
  figures. Composing the hardpoint transform on top of this exactly cancels it out (pure
  translations: `hardpoint + (-hardpoint) = 0`), landing the barrel's pivot back at world origin —
  but the mesh's own vertices aren't centered on that pivot, so the net visual result is
  unchanged.
- Checked `WorldToBone` vs `BoneToWorld` for a "fields swapped" theory (a plausible, common bug
  class in reverse-engineered binary formats) — **ruled out**: both fields are byte-identical in
  the raw chunk data, for every node in both files, not a parsing bug.
- Tried several other transform compositions (replace instead of compose, invert) — none produced
  a barrel bounding box anywhere near the chassis's. The real gap (~1.0-1.3 units) is far larger
  than any single node-transform value found.

### Fix #2 candidate, NOT yet confirmed or attempted: multi-descriptor (multi-LOD) files may decode at ~2x their correct scale

Compared each file's `NodeMeshCombo`-stored `BoundingBoxMin`/`BoundingBoxMax` (a raw field baked
directly into the chunk, independent of the vertex-decode path) against the *decoded* mesh's
actual bounding box:

- **Chassis** (`bas` node, 42 CAFEBABE descriptors): stored bbox `(-0.117,-0.358,-0.302)` to
  `(0.117,0.256,0.002)` — **matches the decoded/rendered geometry exactly.**
- **Barrel** (`bar_lvl1` node, 62 CAFEBABE descriptors): stored bbox spans are **precisely 2.0x**
  the decoded geometry's spans (Y: 0.743 vs 0.372, ratio 1.997; Z: 0.192 vs 0.096, ratio 2.0 —
  clean, not approximate).

Both files report the *same* quantization `scale` value (`0.500002`) in their CAFEBABE header, so
this isn't a universal decode-formula bug (that would break the chassis too, and it doesn't). It's
specific to the barrel — and correlates with descriptor count (62 vs 42), which lines up with an
**already-documented, known limitation**: `docs/RENDER-PIPELINE.md`/this file's own "Known
Remaining Issues" section already flags that `BuildLodMeshGeometry` has no explicit per-LOD
boundary marker and currently treats *all* descriptors in a file as one single mesh. If the
barrel genuinely packs 2 LOD levels into its 62 descriptors and something about how per-LOD
scale/center (or descriptor-range selection) gets read differs from a single-LOD file like the
chassis, that could plausibly produce exactly this kind of clean, file-specific 2x inflation.

**Not confirmed, not fixed, not attempted beyond this observation** — user asked to log the
finding and stop here rather than continue an open-ended dive. This is comparable in depth to the
original CAFEBABE format reverse-engineering (session 1-2) — needs a dedicated session comparing
the raw multi-descriptor layout between a known-single-LOD file (chassis) and a known-multi-LOD
file (barrel) byte-for-byte, most likely starting from `ChunkIvoLodMeshData_900.Read()`'s
descriptor-table parsing and `CryEngine.cs BuildLodMeshGeometry()`'s consumption of it.

### Reusable diagnostic code (not currently in the repo — `CgfConverterTestingConsole/Program.cs`'s `RunCustom()` restored to placeholder per convention)

```csharp
static void RunCustom(CryEngine cryData)
{
    var comboChunk = (ChunkNodeMeshCombo?)cryData.Models[0].ChunkMap.Values
        .FirstOrDefault(c => c.ChunkType == ChunkType.NodeMeshCombo);
    if (comboChunk?.NodeMeshCombos is null) { Console.WriteLine("No NodeMeshCombo chunk found."); return; }
    var names = comboChunk.NodeNames ?? new List<string>();
    for (int i = 0; i < comboChunk.NodeMeshCombos.Count; i++)
    {
        var node = comboChunk.NodeMeshCombos[i];
        var name = i < names.Count ? names[i] : $"node_{i}";
        Console.WriteLine($"Node[{i}] '{name}'  ParentIndex={node.ParentIndex}  GeometryType={node.GeometryType}");
        Console.WriteLine($"  BoneToWorld = {node.BoneToWorld}");
        Console.WriteLine($"  ScaleComponent = {node.ScaleComponent}");
        Console.WriteLine($"  BoundingBoxMin = {node.BoundingBoxMin}  BoundingBoxMax = {node.BoundingBoxMax}");
    }
}
```

Requires `CgfConverter.csproj`'s `InternalsVisibleTo` to include `CgfConverterTestingConsole` (added
this session, kept — `ChunkNodeMeshCombo`/`NodeMeshCombo` are internal types). Run via:
```bash
dotnet run --project CgfConverterTestingConsole -- "<file>" --objectdir "<extracted root>" --custom
```

### Files changed (kept)

- `CgfConverter/Renderers/Gltf/BaseGltfRenderer.Geometry.cs` — the `IsIvoFile` branch fix (Fix #1
  above). Backed up pre-session state to `bak/BaseGltfRenderer.Geometry_20260813_140051.cs`.
- `CgfConverter/CgfConverter.csproj` — added `InternalsVisibleTo` for `CgfConverterTestingConsole`.
- `CgfConverterTestingConsole/Program.cs` — `RunCustom()` restored to placeholder; diagnostic code
  preserved above.
- Deployed: `D:\projects\sc-scrafting-sync\Tools\cgf-converter.exe`.

### `sc_crafting_sync` side (kept, currently inert for everything except ship weapons)

`renderer/find_model_path.py` (`_extract_attached_parts()` now also returns each part's hardpoint
name), `renderer/convert_geometry.py` (converts attached parts), `renderer/render_glb.py` (excludes
attached parts from chassis selection, builds `--hardpoint-map`), `renderer/blender_render.py`
(positions attached parts at their chassis hardpoint empty — new `--hardpoint-map` arg). All gated
behind `model_info["attached_part_paths"]` being non-empty, which is only true for ship weapons
with `SItemPortLoadoutManualParams` — zero risk to any other item type. The 26 ship weapons
remain in `data/multipart_manual_slugs.txt` (routed to manual assembly), unaffected by any of this
— this plumbing isn't live in the auto-render path for them and won't be until fix #2 (or
whatever the real root cause turns out to be) is found.

---

## Session 6 Update (2026-08-12, same day as session 5): Fixed CVSA Cannon crash — real fix, deployed; found a second, different, still-unfixed crash

**Problem investigated:** `docs/TODO.md` in `sc_crafting_sync` tracked CVSA Cannon's geometry file
(`behr_bal_can_s2.cga`) as crashing the converter with an unhandled `IndexOutOfRangeException` —
first noticed as a side effect of session 5's radar work, session 3 had already flagged the same
file as one of "3 produce no GLB at all with no CAFEBABE warning (different failure mode, not
investigated)" alongside `metamaterial_1_a.cgf`/`metamaterial_2_a.cgf`. User asked to look into it
specifically.

### Root cause (confirmed, unrelated to the radar bug)

Full stack trace: `BuildLodMeshGeometry` → `var bmin = lod0Verts[0];` on an empty array. Traced to
raw data via Python's already-proven `classify_multipart.py` decoder (same binary layout, byte-for-
byte verified against this project's own reader in session 5): `behr_bal_can_s2.cga` has **two**
CAFEBABE sections (the known, separate, already-tracked multi-section case — `cgf-converter` only
ever reads the first). The **first** section's own descriptor table is corrupt: its one real
submesh entry has `CumVerts=0, CumIdx=0` (identical to the null-header entry) alongside clearly
garbage `CumTri`/`Packed` values (e.g. `CumTri=3183688622`). The **second** section (never read by
the converter — same as every other multi-section file) is completely well-formed: 609 real
vertices, sane cumulative submesh boundaries. So `lod0VertCount = descs[last].CumVerts` evaluates
to `0`, `lod0Verts = new Vector3[0]`, and indexing `[0]` throws.

### Fix

Added a defensive guard in `BuildLodMeshGeometry` right after computing `lod0VertCount`/
`lod0IdxCount`: if either is `0`, log a warning and return the same empty-bounding-box `GeometryInfo`
already used by the adjacent `descs.Count < 2` guard, instead of proceeding to index into an empty
array. **This does not make CVSA Cannon render correctly** — the first section's data is genuinely
unrecoverable (no real submesh boundaries survive), so the automated single-file conversion still
produces an empty mesh (confirmed: `meshes: [None, None]`, `buffers: [{"byteLength": 0}]`) — it
just does so cleanly instead of crashing the whole file's conversion. The item's *correct* geometry
(the second, well-formed 609-vertex section) is already captured by `sc_crafting_sync`'s
`classify_multipart.py` manual-assembly export, unaffected by this change either way.

### Verified

Rebuilt in Debug, re-ran `cool_acom_s01_pl02.cga` (17,026 verts, exact match) and
`radr_grnp_s01_pl01.cga` (15,029 verts) — both unchanged, confirming this second fix in the same
session didn't regress session 5's radar work. Rebuilt Release, redeployed to
`Tools\cgf-converter.exe`, re-tested with the deployed binary directly — no crash.

### Second, different, still-unfixed crash found along the way

`metamaterial_1_a.cgf` and `metamaterial_2_a.cgf` (the other 2 files in session 3's original
"different failure mode" bucket) do **not** share this bug — they crash with a completely different
exception: `System.ArgumentException: An item with the same key has already been added. Key:
Unknown`, from `CgfConverter/Renderers/MaterialTextures/MaterialTextureManager.cs:35`
(`cryMaterial.Textures!.Where(...).ToDictionary(x => x.Map, x => x)` — LINQ's `ToDictionary` throws
when the source has duplicate keys; here the material apparently has 2+ texture entries that both
fail to classify and fall back to `Texture.MapTypeEnum.Unknown`). This is the same "Key: Unknown"
error seen for the `sc_crafting_sync` pipeline's 'Metamaterial Test #152' item during a real run — likely a shared,
more widespread issue in material/texture loading rather than a per-file quirk. **Not investigated
further or fixed this session** — located the source line only, kept scope to the CVSA Cannon crash
that was actually asked about. `CgfConverterTestingConsole/Program.cs`'s `RunCustom()` reset to
placeholder (used for this session's reflection-based descriptor-table dump).

---

## Session 5 Update (2026-08-12): Fixed radar empty-mesh bug — REAL FIX, deployed

**Problem investigated:** every Star Citizen radar item (Fleming, Predator, Surveyor family,
Observer family, SNS-R series, etc. — 63 of 76 radar entity XMLs, all four size tiers) rendered as
a completely empty mesh. Session 3 had already flagged this exact symptom
(`radr_grnp_s0*_pl01.cga` — "previously a hard failure, now partially parses but likely still
needs the manual `.blend` override") but never root-caused it. User confirmed in the downstream
`sc_crafting_sync` pipeline: "none of the radars are rendered correctly... this has been an issue
since they were introduced."

### Root cause (fully confirmed, not the multi-section CAFEBABE issue)

Verified directly with `CgfConverterTestingConsole --dump-nodes` / `--custom` against
`radr_grnp_s01_pl01.cga`: the file's `IvoLodMeshData` chunk parses perfectly (90 real descriptors,
15,260 real vertices, single CAFEBABE section — confirmed independently by manually decoding the
same binary section in Python, matching byte-for-byte). But its `NodeMeshCombo` chunk — a
*separate* chunk that's supposed to carry the node hierarchy the mesh attaches to — is genuinely
only 64 bytes (just the fixed header, no node entries) and reports `NumberOfNodes=0`,
`NumberOfMeshSubsets=0`.

`CryEngine.cs BuildNodeStructure()`'s top-level gate is:
```csharp
if (comboChunk is not null && comboChunk.NumberOfNodes != 0)
    hasValidNodeMeshCombo = true;
```
With `NumberOfNodes=0`, `hasValidNodeMeshCombo` is `false`, so the entire `if (hasValidNodeMeshCombo)`
branch — the *only* place `IvoLodMeshData`/`BuildLodMeshGeometry` is ever consulted — is skipped
completely. Control falls into the `else` branch (originally written for skinned/companion-file
items), which does `ChunkMesh? chunkMesh = Models.Count == 1 ? null : CreateMeshData();` — for a
single-file rigid `.cga` like this, `Models.Count == 1`, so `chunkMesh = null`. The real,
successfully-parsed 15,260-vertex mesh is simply never looked at. Confirmed this is a genuine
*second* valid file layout SC uses (not a parser bug reading the wrong offset) by comparing against
`radr_grnp_s01_pl02.cga`, which has a normal, fully-populated `NodeMeshCombo`
(`NumberOfNodes=2`, `Size=536` bytes, a `GeometryType=Geometry` node + a `Helper2` parent) and
converts correctly today. Both patterns are legitimate SC 4.5+ output; the converter only handled
one of them.

### Fix

`CryEngine.cs BuildNodeStructure()`, in the `else` branch (~line 285): before falling back to
`Models.Count == 1 ? null : CreateMeshData()`, check for an `IvoLodMeshData` chunk with real
descriptor data (`RawDescriptors.Count >= 2`) and, if present, build a single implicit root node
directly from it via the existing `BuildLodMeshGeometry()` (same method the `hasValidNodeMeshCombo`
branch already uses — its `_unused` second parameter confirms it never needed
`NumberOfMeshSubsets` in the first place, per session 3's own architecture notes). Only the `else`
branch changed; the `hasValidNodeMeshCombo=true` branch (files with a real node table) is
byte-for-byte untouched, so nothing that worked before this session can regress via this code path.

### Verification

- All 4 radar size tiers (`radr_grnp_s01_pl01` small, `s02_pl01` medium, `s03_pl01` large,
  `v01_pl01` vehicle) now produce real geometry: 15,029 / 19,973 / 36,545 / 5,967 vertices
  respectively (slightly under each file's raw vertex count — expected, `BuildLodMeshGeometry`
  correctly slices to LOD0 only, same behavior as the working cooler reference case).
- Zero regressions: re-tested the original reference file `cool_acom_s01_pl02.cga` — **17,026
  vertices, exact match** to the table in this document's "Verified Results" section from session
  1-2; re-tested `radr_grnp_s01_pl02.cga` (already-working small radar sibling, goes through the
  untouched `hasValidNodeMeshCombo=true` path) — unchanged, 132 vertices; re-tested Demeco LMG's
  `_handle.cgf` (a working `sc_crafting_sync` multi-part sibling) — unchanged, 1,039 vertices.
- Full end-to-end test through the **unmodified** `sc_crafting_sync` Python pipeline
  (`renderer.pipeline.render_item("Fleming", "Radar", ...)`, deployed binary, real `find_model_path`
  → extract → convert → Blender render → resize): `SUCCESS: True, source: auto` — no manual
  `.blend` override needed. Rendered PNG visually confirmed as a correct radar dish component (not
  garbage/empty).
- Camera axis: user picked `X` from a 4-way (auto/X/Y/Z) render comparison as the correct
  orientation for radar components ("how it's mostly viewable in a ship as it is rack mounted"),
  confirmed consistent across all 4 size tiers. Added to `sc_crafting_sync`'s
  `renderer/render_glb.py` `_AXIS_BY_TYPE["radar"] = "X"`.

### Deployed

`dotnet publish cgf-converter/cgf-converter.csproj -c Release -r win-x64`, copied to
`D:\projects\sc-scrafting-sync\Tools\cgf-converter.exe`. Previous binary backed up to
`D:\projects\sc-scrafting-sync\Tools\bak\cgf-converter_20260812_122109.exe`.
`CgfConverterTestingConsole/Program.cs`'s `RunCustom()` (used for this session's reflection-based
chunk inspection) restored to its placeholder per that project's own convention — the diagnostic
code is preserved above if this needs reproducing.

### New, separate finding — NOT fixed, NOT investigated further this session

**CVSA Cannon's geometry file crashes the converter outright:**
`Objects/Spaceships/Weapons_bespoke/Vanguard/behr_bal_can_s2.cga` throws an unhandled
`System.IndexOutOfRangeException` (no further stack trace captured). This is the same file session
3 already flagged as one of "3 produce no GLB at all with no CAFEBABE warning (different failure
mode, not investigated): `metamaterial_1_a.cgf`, `metamaterial_2_a.cgf`, `behr_bal_can_s2.cga`" —
still unresolved, now confirmed to be a hard crash rather than a quiet failure. Worth checking
whether the two `metamaterial_*.cgf` files crash the same way. Not attempted this session — kept
scope to the radar fix the user explicitly asked for.

---

## Session 4 Update (2026-08-11): Root-caused a multi-file attachment misalignment bug — NOT fixed, reverted

**Problem investigated:** Karna Rifle (an FPS weapon, `.cdf`-based) renders as two disconnected
chunks roughly one gun-length apart when its `_body.cgf` and `_parts.skin` are combined (the
Python-side combining fix from the same session — see `sc_crafting_sync/docs/LESSONS-LEARNED.md`
2026-08-11 entries). Not a small visual gap — a real positioning bug, confirmed by rendering both
meshes color-coded and overlaid: shapes and orientation are correct, only translation is wrong.

### What was found (all confirmed, still true, worth reusing)

**1. The `.cdf` file format is fully decodable.** Signature is `CryXmlB`, not `#ivo` — it's
CryEngine's binary-encoded XML, completely unrelated to the chunk-table format. The existing
`CryXmlB/CryXmlSerializer.cs` (unmodified, from the original public repo) already has
`CryXmlSerializer.ReadFile(path) -> XmlDocument`, but nothing in `CryEngine.cs` ever calls it for
`.cdf` specifically — hence `cgf-converter.exe llmg_fps_klwe_demeco.cdf` always failed with
"No output produced" (see session 1-2's Demeco investigation, way above). Decoded, it's a plain
`<CharacterDefinition>` with a `<Model File="...chr">` (master skeleton reference) and an
`<AttachmentList>` — one `<Attachment>` per sub-part, each with `Type` (`CA_BONE` or `CA_SKIN`),
`Binding` (file path), `BoneName`, and `RelPosition`/`RelRotation`. Example (Karna):
```xml
<Attachment Type="CA_BONE" AName="body" Binding=".../prfl_fps_ksar_karna_body.cgf" RelRotation="1,0,0,0" RelPosition="0,0,0" BoneName="root" .../>
<Attachment Type="CA_SKIN" AName="parts" Binding=".../prfl_fps_ksar_karna_parts.skin" .../>
<Attachment Type="CA_BONE" AName="stock" Binding="objects/fps_weapons/attachments/stocks/ksar/stok_fps_ksar_01.cgf" RelPosition="0,0,0" BoneName="stock" .../>
<Attachment Type="CA_BONE" AName="physicalized_hook" Binding=".../prfl_fps_ksar_karna_physicalized_hook.cgf" BoneName="physicalized_hook" PA_PendulumType="2" PA_Gravity="-9.81" .../>
```
Note `stock`'s binding points to a completely separate shared-attachment file outside the item's
own folder, and `physicalized_hook` carries real pendulum-physics parameters (a dangling/swinging
attachment). **Nothing currently in the converter reads this file's content at all** — a genuine,
reusable capability gap, not just a Karna-specific issue.

**Reusable tool added:** `CgfConverterTestingConsole/Program.cs` now has a `--dump-cdf` command
that bypasses the normal `CryEngine` model-loading pipeline entirely (which doesn't recognize the
`CryXmlB` signature and would throw) and calls `CryXmlSerializer.ReadFile()` directly:
```bash
dotnet run --project CgfConverterTestingConsole -- "<path>.cdf" --dump-cdf
```

**2. The master `.chr` skeleton and each attachment's own embedded skeleton copy are
byte-for-byte identical** for every shared bone (checked via `--custom` in the testing console,
comparing `CompiledBones` world-transform positions between `karna.chr` and
`karna_parts.skin` directly — both native CryEngine-side, no glTF/Blender involved). No skeleton
corruption, no cross-file mismatch. Confirmed independently again after glTF export: Blender's
imported armature bone positions match the native values exactly. **The skeleton data is not the
problem.**

**3. Root cause: `BaseGltfRenderer.Geometry.cs`'s SNORM vertex de-quantization has a broken
clamp.** In the `else // VertsUVs (Ivo format)` branch (~line 928), both `multiplerVector` (from
`mesh.MinBound`/`MaxBound`) and `scalingVector` (from `meshChunk.ScalingVectors`, i.e.
`IvoGeometryMeshDetails.ScalingBoundingBox`) compute a half-extent and then floor every axis with
`if (x < 1) x = 1`. For Karna's `parts.skin`, the real half-extent is `(0.052, 0.261, 0.079)` —
all under 1 world-unit, the norm for sub-meter FPS weapon parts — so **all three axes get clamped
to exactly `(1,1,1)`**, discarding the real scale. SNORM-encoded positions (range -1..1) then get
multiplied by 1.0 instead of the correct small fraction, spreading the mesh out several times too
far. Confirmed by direct debug print of the raw pre-clamp values (temporarily added, then removed
— see git history / diff if needed to reproduce). This is not a Karna-specific bug — it would
affect any Ivo-format mesh whose true half-extent is under 1 unit in any axis, on either the skin
or (see below) the rigid path.

### Why the fix was reverted

The first fix attempt (raise the floor from `1` to a tiny epsilon `0.0001`) broke `body.cgf`
instead — its bounding box collapsed to near-zero on two axes. Root cause: `body.cgf` **also**
goes through this exact same code path (contrary to the assumption that the rigid `IvoLodMeshData`
path — our session-3 CAFEBABE fix — bypasses it entirely), and debug output confirmed
`useScalingBox=True` for it too, with `ScalingVectors` populated and matching the item's real,
correct bounding box. Despite that, even a targeted fix that left `multiplerVector` untouched and
only changed `scalingVector`'s clamp still produced a broken (near-zero) body bounding box on
re-test — meaning there is at least one more contributing factor not yet identified (possibly a
second mesh node — `body.glb` imports as two separate objects, `prfl_fps_ksar_karna_body` and a
small rotated `display` node, and it's not fully confirmed which one the bbox check was reading
each time, or whether both need consistent treatment).

**Every fix attempt was reverted.** `CgfConverter/Renderers/Gltf/BaseGltfRenderer.Geometry.cs` is
confirmed via `diff` to be byte-identical to its pre-session-4 state
(`bak/BaseGltfRenderer.Geometry_20260811_105300.cs`). `CgfConverterTestingConsole/Program.cs`'s
`RunCustom()` was restored to its placeholder per the `cryengine-inspect` skill's own convention;
the `--dump-cdf` command addition was kept (additive, non-destructive, genuinely reusable).
`Tools\cgf-converter.exe` in `sc-scrafting-sync` was never rebuilt/redeployed during this
investigation — every test used the separate `cgf-converter/bin/Release/.../publish/` build output
directly, never copied over the deployed binary.

### What a real fix needs to establish before touching this code again

1. **Does `body.cgf` really need `multiplerVector`/`scalingVector` applied at all**, or is its
   `GeometryInfo` (built by our custom `ChunkIvoLodMeshData_900.cs` parser, which already fully
   de-quantizes to real-world coordinates in Phase 1) reaching this renderer code by mistake and
   getting double-transformed? The evidence is contradictory: `ScalingVectors` being populated for
   it looked like it should route through the correct branch, but even correct-looking
   `scalingVector` values didn't produce a correct result on re-test.
2. **Trace exactly which node(s) `hasGeometry` vs `hasLodGeometry` produces** for a rigid `.cgf`
   like Karna's `body.cgf` — confirm definitively whether it's the skin-populated
   (`chunkMesh.ScalingVectors = geometryMeshDetails.ScalingBoundingBox`) branch or the
   LOD-populated (`chunkMesh.GeometryInfo = lodGeometry`, no `ScalingVectors` set) branch in
   `CryEngine.cs` `BuildNodeStructure()` — session 3 assumed the latter; session 4's debug output
   contradicted that assumption but wasn't fully reconciled.
3. Once (1) and (2) are resolved: implement `.cdf` attachment-socket resolution properly (item 1
   above already provides the data — `RelPosition`/`RelRotation` per attachment, `BoneName` to
   resolve against the master `.chr` skeleton) so each file's mesh is positioned via its actual
   attachment transform, rather than relying on multiple independently-converted files
   coincidentally agreeing on a shared implicit origin (which works for some items — Demeco, S71 —
   and doesn't for others — Karna — with no way to know in advance which).

**Practical status:** no regression, nothing shipped broken. Items like Karna stay on the
`sc_crafting_sync` manual `.blend` assembly workflow (`classify_multipart.py` /
`part_exports/`) — same mechanism already used for unrelated multi-section CAFEBABE cases.

---

## Session 3 Update (2026-08-06): Fixed hardcoded CAFEBABE offset

**Problem found:** `render_queue.py` (the sc_crafting_sync render pipeline) was failing
`ChunkIvoLodMeshData: expected CAFEBABE` on 127+ items — mostly vehicle weapons, not the
cooler this parser was originally built/tested against. Root cause: `ChunkIvoLodMeshData_900.Read()`
assumed the CAFEBABE marker always sits at `chunk.Offset + 0xEC`. That offset was reverse-engineered
from the single cooler test file (`cool_acom_s01_pl02.cga`) and does not generalize — parsing the
real chunk tables of the failing files showed the true delta ranges from ~0x58 to ~0x10F00 depending
on the item.

**Fix:** replaced the fixed-offset seek with a byte-pattern search for the CAFEBABE magic
(validated against the format-version field at +0x04, which is always `256`) within the chunk's
own byte range (`[Offset, Offset+Size)`). Everything after the magic is read exactly as before —
those reads are sequential/relative, not absolute-offset, so no other logic needed to change.

**Result (batch-tested against 445 real SC items that were failing before the fix):**
- 434/445 (97.5%) now produce a real, non-empty GLB (previously 0 — all hit the fixed-offset miss)
- 6 produce a small (<10 KB) GLB — includes the Fleming radar family (`radr_grnp_s0*_pl01.cga`),
  previously a hard failure, now partially parses but likely still needs the manual `.blend`
  override for a correct render
- 2 still fail (ship/vehicle collision meshes picked up as retry-siblings, not blueprint items:
  `drak_golem_nocollide.cgf`, `TMBL_Storm_Local_Grid.cgf`)
- 3 produce no GLB at all with no CAFEBABE warning (different failure mode, not investigated):
  `metamaterial_1_a.cgf`, `metamaterial_2_a.cgf`, `behr_bal_can_s2.cga`

**Known limitation carried over, unresolved:** many failing files contain **multiple** CAFEBABE
sections (up to 40 in one file — 187/445 files in this sample had more than one). The parser
still only reads the first section found. For merged multi-part meshes this means the render
will show only the first part/LOD rather than the complete model. This is the natural next step
if further improvement is needed — loop `FindCafeBabe` across the full chunk range, parse each
section, and merge them into one `GeometryInfo` in `BuildLodMeshGeometry`.

**Deployed:** rebuilt via `dotnet publish cgf-converter/cgf-converter.csproj -c Release -r win-x64`
and copied to `D:\projects\sc-scrafting-sync\Tools\cgf-converter.exe`. Previous binary backed up to
`D:\projects\sc-scrafting-sync\Tools\bak\cgf-converter_20260806_170457.exe`.

---

## What Was Done

### Goal
Star Citizen 4.5+ rigid mesh `.cga` files (e.g., ship coolers, components) use a proprietary chunk type `IvoLodMeshData` (ChunkType `0x58DE1772`) that previously had no parser. The converter produced empty 8KB GLBs for these files. The work across two sessions implemented a full parser and wired it into the geometry pipeline.

---

## Files Changed

### 1. `CgfConverter/CryEngineCore/Chunks/ChunkIvoLodMeshData.cs` *(NEW)*

Base class holding all raw data decoded from the chunk. All properties are `protected set` so subclasses can populate them.

```csharp
public class ChunkIvoLodMeshData : Chunk
{
    public uint VertexCount { get; protected set; }
    public uint IndexCount { get; protected set; }
    public uint TriangleCount { get; protected set; }
    public Vector3 QuantizationCenter { get; protected set; }
    public Vector3 QuantizationScale { get; protected set; }
    public Vector3[]? RawPositions { get; protected set; }       // ALL LODs
    public byte[]? RawLocalIndices { get; protected set; }       // ALL LODs, per-submesh 0-based
    public byte[]? RawTriMatIDs { get; protected set; }          // ALL LODs, per-triangle
    public List<(uint CumVerts, uint CumIdx, uint CumTri, uint Packed)> RawDescriptors { get; protected set; } = [];
}
```

### 2. `CgfConverter/CryEngineCore/Chunks/ChunkIvoLodMeshData_900.cs` *(NEW)*

Implements `Read()` for the CAFEBABE binary format.

**Key binary layout (offsets relative to CAFEBABE magic, which is at `chunk.Offset + 0xEC`):**

| Offset | Content |
|--------|---------|
| +0x00 | `0xCAFEBABE` magic (LE uint32) |
| +0x04 | Format version (256) |
| +0x14 | `VertexCount` (uint32) |
| +0x1C | `IndexCount` (uint32) |
| +0x24 | `TriCount` (uint32) |
| +0x2C | Total descriptor count (including null header) |
| +0x44 | 3× (descriptor u32, scale f32) — quantization scales XYZ |
| +0x5C | 3× (0/sentinel u32, center f32) — quantization centers XYZ |
| +0x74 | Vertex data: `VertexCount × 8 bytes` (SNORM16: i16 x, i16 y, i16 z, i16 pad=0x7FFF) |
| +0x74 + verts | Index data: `IndexCount × 1 byte` (uint8, per-submesh local) |
| after indices | TriMatID data: `TriCount × 1 byte` (uint8 material ID per triangle) |
| after triIDs | Submesh descriptor table: N × 16 bytes (CumVerts, CumIdx, CumTri, packed) |

**SNORM16 decode formula:**
```
position = center + (raw_int16 / 32767.0f) * scale
```

**Descriptor table structure:**
- Entry `[0]` is always a null header (all zeros)
- Entries `[1..N]` are cumulative per-submesh (each entry's values are the running total, not deltas)
- The reader stops when `cumIdx` decreases (signals end of valid entries) or exceeds `IndexCount`
- All vertices and indices span ALL LODs together — there is no per-LOD split in the binary

### 3. `CgfConverter/CryEngineCore/Chunks/Chunk.cs` *(MODIFIED)*

Added factory routing for the new chunk type:

```csharp
ChunkType.IvoLodMeshData => Chunk.New<ChunkIvoLodMeshData>(version),
```

### 4. `CgfConverter/CryEngine/CryEngine.cs` *(MODIFIED)*

Three additions in `BuildNodeStructure()`:

**a) Detect lodMesh when no skinMesh is present:**
```csharp
ChunkIvoLodMeshData? lodMesh = null;
if (skinMesh is null)
{
    lodMesh = Models[0].ChunkMap.Values
        .FirstOrDefault(x => x.ChunkType == ChunkType.IvoLodMeshData) as ChunkIvoLodMeshData;
}
```

**b) Pre-build geometry once before the node loop:**
```csharp
GeometryInfo? lodGeometry = null;
if (lodMesh is not null && comboChunk.NumberOfMeshSubsets > 0)
    lodGeometry = BuildLodMeshGeometry(lodMesh, comboChunk.NumberOfMeshSubsets);
```

**c) Assign geometry to the node with `IvoGeometryType.Geometry`:**
```csharp
var hasLodGeometry = lodGeometry is not null && node.GeometryType == IvoGeometryType.Geometry;
```

**New method `BuildLodMeshGeometry`:**

The key insight (fixed in session 2): `comboChunk.NumberOfMeshSubsets` (= 22 for the test file) represents material slots/render groups, NOT the number of CAFEBABE descriptor entries (= 101 for the test file). Using it as the slice boundary produced only ~10% of the mesh.

The fix: use `descs.Count - 1` (all valid descriptor entries) as the submesh count.

```csharp
private GeometryInfo BuildLodMeshGeometry(ChunkIvoLodMeshData lodMesh, int _unused)
{
    var descs = lodMesh.RawDescriptors;
    // descs[0] = null header; [1..descs.Count-1] = all valid submeshes
    int submeshCount  = descs.Count - 1;
    uint lod0VertCount = descs[descs.Count - 1].CumVerts;
    uint lod0IdxCount  = descs[descs.Count - 1].CumIdx;

    // Convert per-submesh local byte indices → global uint indices
    // MatID comes from RawTriMatIDs[firstTri] for each submesh
    // ...
}
```

### 5. `CgfConverter/Renderers/Gltf/BaseGltfRenderer.Geometry.cs` *(MODIFIED)*

Added null guards to handle rigid `.cga` files that have `CompiledBones` (for positional transforms / bind pose) but **no skin vertex data** (`BoneMappings` / `IntVertices`). Without these guards the renderer crashed with NullReferenceException.

**Guard in `CreateGltfNodeInto` (skips IVO skeleton path for rigid meshes):**
```csharp
if (!omitSkins && cryData.SkinningInfo is { HasSkinningInfo: true } skinningInfo
    && HasObjectNodeIndexMappings(skinningInfo)
    && (skinningInfo.BoneMappings?.Count > 0 || skinningInfo.IntVertices?.Count > 0))
{
    CreateIvoSkeletonWithMeshes(nodes, cryData, skinningInfo);
    return;
}
```

**Guard in `AddMesh` (skips skin writing for rigid meshes):**
```csharp
if (cryData.SkinningInfo is { HasSkinningInfo: true } skinningInfo
    && (skinningInfo.BoneMappings?.Count > 0 || skinningInfo.IntVertices?.Count > 0))
{
    // call WriteSkinOrLogError ...
}
```

---

## Verified Results (test file: `cool_acom_s01_pl02.cga`)

| | Before | After session 1 | After session 2 (current) |
|--|--------|-----------------|--------------------------|
| GLB size | 8 KB (empty) | ~121 KB | ~500+ KB |
| Vertices | 0 | 3,728 | **17,026** |
| Indices | 0 | 15,657 | **68,982** |
| Subsets | 0 | 22 | **101** |
| Geometry | none | ~10% (fan housing only) | **complete cooler** |

---

## Known Remaining Issues / Next Steps

### Not yet implemented in IvoLodMeshData
- **UV coordinates** — data is present in the file after the normals section but format not yet mapped
- **Normals** — 4-byte format identified in the binary but not yet parsed; normals are currently 0
- **Material ID remapping** — raw MatIDs in subsets (e.g., 6, 7, 19, 20, 22) may need to route through `NodeMeshCombo.MaterialIndices` to map correctly to the loaded `.mtl` submaterials

### Visual verification
- The mesh geometry has not been manually verified in Blender — vertex counts and bounding box look correct but triangle winding/connectivity should be confirmed visually

### Other .cga files
- Only `cool_acom_s01_pl02.cga` has been tested. Other SC 4.5+ rigid `.cga` files should work if they follow the same CAFEBABE layout, but this is unverified.

---

## Architecture Notes for Future Sessions

**Why `NumberOfMeshSubsets` ≠ descriptor count:**  
`ChunkNodeMeshCombo.NumberOfMeshSubsets` is the number of material groups used by the engine's render system. The CAFEBABE descriptor table is a separate concept — one entry per physical submesh in the LOD hierarchy, with the null header at `[0]`. These are coincidentally the same only if there's exactly one submesh per material group, which is not the case for this cooler model.

**Why all LODs share one vertex/index buffer:**  
The CAFEBABE format packs all LOD levels sequentially into a single buffer. The descriptor table is cumulative across all LODs. There is no explicit LOD-boundary marker in the header — you must use the descriptor table's structure (or an external count) to know where LOD0 ends and LOD1 begins. Currently the code uses ALL descriptors (treating everything as a single mesh), which may include LOD1+ data. This probably needs refinement once UVs/normals are working.

**Rigid .cga vs skinned .skin/.chr:**  
Rigid `.cga` files use `IvoLodMeshData`. Skinned meshes use `IvoSkinMesh` (ChunkIvoSkinMesh). The detection logic in `BuildNodeStructure` tries `IvoSkinMesh` first and falls back to `IvoLodMeshData`. Both can coexist with `CompiledBones` — the bones carry bind-pose transform data even when there is no per-vertex skin weighting.
