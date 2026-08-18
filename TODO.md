TODO File

2.0 release (done)
- Animations
- USD support

2.1 release (done)
- Star Citizen #ivo LOD mesh chunk support (ChunkIvoLodMeshData/_900), including multi-section
  geometry and the scale/origin decode fix (see DEVNOTES.md, SESSION-HANDOVER.md)
- Project moved under git version control, pushed to city028/Cryengine-Converter

2.1.1 (pending release)
- Two geometry fixes landed AFTER the v2.1.0 tag (a923aaf), so the published v2.1.0 release
  does not contain them:
    224903d  pivot Ivo LOD mesh scale on the decoded slice centre, not QuantizationCenter
    2683860  emit the final submesh of every Ivo LOD-mesh section (+31,637 verts / 52 items)
- Tag v2.1.1 from HEAD so the published release matches what actually works.
- Rebuild and redeploy the binary from a committed state. The currently deployed
  D:\projects\sc-scrafting-sync\Tools\cgf-converter.exe stamps ProductVersion 2.1.0+224903d
  but was built 2026-08-16 10:24, 76 minutes before 2683860 was committed. It DOES contain
  that fix (confirmed by UTF-16 string search of the binary) -- it was built from a dirty
  tree, so the stamp merely names the last commit at build time. Functionally fine,
  but it means checking out 224903d does not reproduce what the shipped exe produces.

Future
- .pak file system support (issue #193 related)
  - CryEngine .pak files are renamed ZIP archives
  - Add PakFileSystem implementing IPackFileSystem, using ZipArchive for on-demand entry access
  - Parse ZIP central directory at construction (seek to end of file), decompress entries on demand
  - For multi-pak games (MWO has 20+), add each to CascadedPackFileSystem (last-added wins, matches CryEngine pak priority)
  - For huge single paks (Star Citizen 140GB+), ZipArchive handles this since it only reads the directory index, not the whole file
  - Add IPackFileSystem.EnumerateFiles() for UI browsing of pak contents
  - Split texture problem: pak files may contain split DDS textures (.dds + .dds.1 etc.)
    - Recombine at the renderer level (in ResolveTextureFile), not in PakFileSystem
    - PakFileSystem stays a dumb file accessor, renderer knows output format requirements
  - UI scenario: point to Steam game root, auto-discover .pak files, browse directory tree to select model
  - WiiuStreamPackFileSystem is a good implementation template

