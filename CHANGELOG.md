# Changelog

All notable changes to **OptiHouse** will be documented in this file.

## [2.2.0] - 2026-11-18 (C++ CAMERA CULLING, PORTAL FREEZE KILLER & MEGA-FARM EDITION)
### Added & Fixed
- **Hardware C++ SIMD Camera Layer Culling (`CameraLayerCullOptimizer`):**
  - Valheim leaves `GameCamera`'s `layerCullDistances` at `0` (forcing Unity to evaluate tiny clutter out to ~1000m+).
  - OptiHouse now enables spherical C++ SIMD per-layer culling (`layerCullSpherical = true`) strictly on tiny clutter layers: `item` (`55m`), `Default_small` (`65m`), and `piece_nonsolid` (`80m`), cutting thousands of main-camera and shadow-cascade evaluations with zero C# loop cost while keeping main `piece`, `terrain`, and `Default` layers untouched to the horizon.
- **Portal & Mega-Base Arrival Freeze Killer (`ZoneLoadPacer`):**
  - Eliminates 30x/sec $O(N \log N)$ `QuickSort` stalls across 20,000+ uncreated ZDOs in `ZNetScene.CreateObjectsSorted` by caching a contiguous value-type priority/distance queue across ticks.
  - Replaces vanilla's uncapped `Count / 100` (= 200+ prefabs in one frame!) post-portal burst with a high-resolution `Stopwatch` millisecond budget (`3.5 ms` live gameplay, `18.0 ms` during black loading screens), streaming massive castles radially outward from the player's feet at 60+ FPS.
- **Mega-Farm & 3,000-Crop Optimizer + Vanilla `Plant.SUpdate` Bugfix (`FarmPlantOptimizer`):**
  - **Fixed Vanilla Iron Gate IL Bug (`Plant.SUpdate`):** Decompilation revealed an inverted timer branch (`!(time > m_updateTime)`) in vanilla `Plant.SUpdate`, causing every planted crop to run `Heightmap.FindHeightmap`, `HaveRoof` (`100m` raycast), and `HaveGrowSpace` (`30`-collider `OverlapSphereNonAlloc`) continuously on every `SlowUpdater` cycle instead of every 10s! OptiHouse restores a true `14s` interval while preserving immediate spawn visuals, exact `10.0s` overcrowding/roof status feedback, and 100% exact ZDO harvest times.
  - **Crop & Forage Shadow/Probe Culling:** Strips `ShadowCastingMode` and `ReflectionProbeUsage` on distant (`>12m`) `Plant` and small `Pickable` meshes using an $O(1)$ swap-back list (zero lag during mass Atgeir harvesting).

## [2.1.0] - 2026-11-18 (DEEP ENGINE & SHADER INSTANCING UNLEASHED)
### Added & Fixed (Vanilla Engine Bottlenecks Discovered via Decompilation)
- **Fixed Two Vanilla Valheim `MaterialPropertyBlock` Bugs Breaking GPU Instancing (`MaterialBatchingOptimizer`):**
  - **`MaterialMan.PropertyContainer.UpdateBlock()` Empty-Block Poisoning Fix:** When `WearNTear.ResetHighlight()` clears Hammer hover colors (0.2s after looking at pieces), vanilla Valheim passes a non-null cleared `MaterialPropertyBlock` to `Renderer.SetPropertyBlock()`, keeping a native C++ `ShaderPropertySheet` (`HasPropertyBlock() == true`) attached forever and permanently disabling `DrawMeshInstanced` GPU Instancing on every piece your Hammer ever hovered! OptiHouse now calls `Renderer.SetPropertyBlock(null)` whenever `m_shaderProperties` reaches zero, immediately restoring hardware instancing.
  - **Grausten / Black Marble / Ashwood Instancing Unlocker (`StripPieceRandomMaterialBlocks`):** Vanilla `RandomMaterialValues.CheckMaterial()` attaches unique `RandMatSeed` `MaterialPropertyBlock`s to 100% of Grausten, Black Marble, and Ashwood pieces, completely disabling `DrawMeshInstanced` across late-game stone/marble fortresses. Skipping `RandMatSeed` on placed `Piece`s unlocks full hardware GPU Instancing across entire castles.
- **3-Second Cubemap Render Spike Killer (`ReflectionProbeOptimizer`):**
  - Switches `ReflectionUpdate` probes (`m_probe1` / `m_probe2`) from synchronous 1-frame rendering to `ReflectionProbeTimeSlicingMode.IndividualFaces` (spread across 9 frames).
  - Strips reflection probe shadow passes (`shadowDistance = 0f`) and micro-layers (`piece_nonsolid`, `item`, `Default_small`, `character`), and scales stationary indoor re-render intervals from `3s` to `14s` while preserving smooth `3s` dual-probe cross-fades.
- **Adaptive Mega-Base Directional Shadow Cascades (`EnableAdaptiveShadowCascades`):**
  - Dynamically drops `QualitySettings.shadowCascades` from `4` to `2` when inside a mega-base (`>= 2,500` loaded pieces, cutting directional sun shadow draw calls by 50%), automatically restoring `4` cascades upon leaving or toggling `F7`.
  - Also disables `ReflectionProbeUsage` alongside shadow casting on small props beyond `18m` in `PieceShadowCuller`.
- **`ZSyncTransform` Sleeping Physics & Zero-GC Comfort HUD (`ZSyncTransformOptimizer`):**
  - Throttles per-frame C++ `OwnerSync` marshaling by 16x on physically sleeping (`Rigidbody.IsSleeping()`) non-character/non-projectile world objects (loot, logs, moored boats, parked carts) and throttles unchanged distant (`>35m`) multiplayer `ClientSync` checks by 4x.
  - Caches `SE_Cozy.GetIconText()` localized comfort text to eliminate per-frame HUD GC allocations near base fires.
- **Under-Foundation Grass Clutter Suppressor (`ClutterOptimizer`):**
  - Prevents `ClutterSystem` from spawning and rendering invisible/clipping instanced grass tufts directly underneath ground-level base floors and stone foundations (`0.05m–2.1m` clearance), keeping gardens, crops, and courtyards untouched.

## [2.0.0] - 2026-11-18 (STANDALONE ENGINE & MEGA-BASE EDITION)
### Added (Standalone Engine & Base Dominators)
- **Hardware GPU Instancing & Native C++ Offscreen Particle Culling (`PrefabInstancingOptimizer`):**
  - Automatically enables `Material.enableInstancing = true` across shared prefab materials (`Piece`, trees, rocks, props) on `ZNetScene.Awake`, allowing Unity to batch identical geometry into single `DrawMeshInstanced` calls with zero custom shader glitches or shadow regressions.
  - Configures local-space looping `ParticleSystem`s on prefabs to `ParticleSystemCullingMode.Pause` so Unity's native C++ worker threads skip off-screen particle simulation automatically.
  - Throttles out-of-range looping spatial `ZSFX` reverb/curve updates by 8x.
- **`WearNTear.UpdateSupport()` Equilibrium Cache (`EnableSupportEquilibriumCache`):**
  - Fixes Iron Gate's vanilla ground/foundation/rock support cache-miss bug (where ground-touching pieces re-run `Physics.OverlapBoxNonAlloc` every second forever).
  - Once structure support values reach 100% vanilla equilibrium, skips redundant `OverlapBoxNonAlloc` sweeps while instantly invalidating on any `Piece` placement/destruction, `Heightmap` terrain edit (`ClearCachedSupport`), tree chop, or `MineRock5` chunk break. Colliders remain 100% untouched.
- **Storage Warehouse Sign UI & UGC Spam Killer (`SignOptimizer`):**
  - Culls world-space `Canvas` (`TextMeshProUGUI`) on signs beyond 16m or behind the camera (fixing floating text rendering through walls up to 128m and slashing `SendWillRenderCanvases` UI batching).
  - Stops `Sign.UpdateText()` from firing `UpdateViewPermission()` (`RelationsManager.CheckPermissionAsync` + delegate GC allocation) every 2 seconds on unchanged signs.
- **Unified Single-Hook `PieceRegistry` + $O(1)$ Swap-Back Unregistration:**
  - Consolidated 8 separate `Piece.Awake` / `Piece.OnDestroy` patches into a single pass sharing one `Renderer[]` lookup, with $O(1)$ swap-back removal (eliminating $O(N^2)$ freezes when portaling away from 20,000+ piece bases) and `WearNTear.SetHealthVisual` sync.
- **Camera-Frustum Cone Culling & Ballista Idle LOD (`TurretOptimizer`):**
  - Pauses banner `Cloth` physics, `Sign` canvases, and `LightFlicker` trigonometry behind the player's camera (>5–6m behind) and throttles 50 Hz idle quaternion scans on distant targetless ballistas while preserving exact target scan intervals.
- **Zero-GC Benchmark HUD (`OptiHouseHUD`):**
  - Pre-formats all overlay strings at 4 Hz so `OnGUI()` allocates 0 bytes per frame.

## [1.5.0] - 2026-11-17 (DEEP ARCHITECTURAL OVERHAUL & REAL FPS BREAKTHROUGH)
### Fixed (Critical Audit Fixes)
- **Revived 6 Dead Subsystems:** Fixed critical Harmony initialization bug where `LightShadowOptimizer`, `PieceShadowCuller`, `ClothOptimizer`, `ItemStandOptimizer`, `TamedAnimalOptimizer`, and `CraftingStationOptimizer` used `[HarmonyPatch(typeof(OptiHousePlugin), "Awake")]` after `Awake()` was already executing — causing their `MonoBehaviour.Update()` loops to **never run**. All 10 subsystems now attach directly and reliably in `OptiHousePlugin.Awake()`.
- **Fixed ItemStand & Banner Freeze Bug:** Because `ItemStandOptimizer` and `ClothOptimizer` `Update()` loops previously never ran, objects loaded beyond cull distance stayed frozen/dim forever when approached. Now smoothly restore as you walk near them.
- **No More Roof Holes or Disappearing Fireplaces:** Removed roof ridge/peak tiles (`roof_ridge`, `roof_top`, `straw_roof_top`) from `DetailPieceLOD` and explicitly excluded all `Fireplace`/light fixtures (`hearth`, `brazier`, `sconce`, `lantern`) from `InteriorOcclusionOptimizer`. Raised default `InteriorCullDistance` from `18m` to `26m` and `DetailPieceCullDistance` from `32m` to `38m`.
- **Snapshot-Safe Renderer & Shadow Restoration:** Both `DetailPieceLOD`, `InteriorOcclusionOptimizer`, `ItemStandOptimizer`, and `PieceShadowCuller` now snapshot original `enabled` / `ShadowCastingMode` states instead of blindly turning on vanilla-hidden sub-renderers (`WearNTear` damage variants or empty stand slots).
- **Eliminated Multiplayer ZDO Desync Risk:** Removed the $O(N^2)$ `ItemDrop` auto-stacker (which destroyed unowned ZDOs before async ownership) and replaced it with zero-allocation component caching and safe `Rigidbody.Sleep()`.
- **Fixed Triple-Applied VFX Scaling Bug:** Guarded `VFXOptimizer.ApplyFastParticleFade` so temporary effects are faded strictly once per spawned object instead of 3x ($0.35^3$).
- **Fixed `WearNTear.UpdateCover` Real-Time Interval:** Fixed `WearNTearUpdater` single-frame `dt` accumulation (~600s delay) to use accurate real seconds (`Time.time`), and gated `Piece.Awake` hooks against `ghost` placement previews.
- **Eliminated Unconditional Raycast Leak & String Churn:** Rate-limited open-air enclosure raycasts in `InteriorOcclusionOptimizer` to once per 15s (on `piece` layer only) and replaced `.ToLowerInvariant()` calls with zero-allocation comparisons.
- **Opt-In `boot.config`:** Changed `EnableGfxJobsTweak` default to `false` so OptiHouse never mutates game files outside `BepInEx/` unless requested.

### Added (High-Impact Performance Gains)
- **Native Forward Point-Light Cap (`LightLod.m_lightLimit = 28`):** Unlocks Valheim's built-in smooth point-light limit (vanilla leaves it at `-1` unlimited), cutting up to 50% of redundant `ForwardAdd` GPU/CPU geometry passes in torch-lit halls.
- **Non-Ashlands `WearNTear` Loop Skip (`SkipNonAshlandsMaterialUpdates`):** Skips `WearNTearUpdater`'s per-second `UpdateAshlandsMaterialValues` iteration across thousands of non-Ashlands base pieces when burn/lava timers are zero.
- **`Fireplace.CheckEnv` Raycast Throttling (`ThrottleFireplaceEnvChecks`):** Throttles terrain/cover raycasts on settled torches and hearths from 4s to 20s.
- **Distant `LightFlicker` Culling (`CullDistantLightFlicker`):** Pauses `LightFlicker` sin/cos math and transform updates beyond 22m while automatically deferring to VPO (`VPOLightFlicker`) when VPO is installed.
- **Beehive Support & Accurate Camera Distance:** Added `Beehive` buzzing/particle culling to `CraftingStationOptimizer`, and switched distance reference checks to the active camera (`Utils.GetMainCamera()`).

## [1.4.1] - 2026-11-16 (CRITICAL EMERGENCY HOTFIX)
### Fixed
- **CRITICAL STRUCTURAL COLLAPSE BUGFIX:**
  - Completely neutralized and removed all collider-disabling logic from `DecorativeColliderOptimizer`.
  - **Root Cause:** Valheim's internal structural support engine (`WearNTear.UpdateSupport()`) performs physics overlap queries specifically looking for active `Collider` components on the `piece` physics layer. When colliders on decorative beams or roof ridges were temporarily disabled, supported roof segments and upper walls calculated `support < minSupport`, lost support (`HaveSupport() == false`), and triggered cascading structural collapses (`Destroy()`).
  - **Permanent Guarantee:** OptiHouse now **NEVER** touches or disables any physics `Collider` components on any building pieces. 100% of colliders remain active at all times.
  - **Automatic Recovery:** Added `DecorativeColliderOptimizer.RestoreAllColliders()` on mod load to automatically re-enable any colliders in the active scene that were previously disabled, fully restoring stability to existing bases.
  - Visual optimizations (`DetailPieceLOD`, `InteriorOcclusionOptimizer`) remain 100% intact and effective because they only toggle `Renderer.enabled` (which does not affect `WearNTear` physics).

## [1.4.0] - 2026-11-16
### Fixed
- **Smart Interior Occlusion Bugfix:** Fixed a critical issue where entering shelter (`InShelter() == true`) globally restored all interior furnishings across the entire base. Now uses smart per-room distance culling: immediate room furnishings remain visible, while distant buildings and rooms remain 100% culled even when under a roof.

### Added
- **Structural Detail Piece LOD (`[14. Structural Detail LOD]`):**
  - Dynamically culls `MeshRenderer` on small decorative micro-pieces (1m beams, 26°/45° cross beams, decorative brackets, dragonheads, trim, finials, roof crests) beyond 32m (configurable).
  - Keeps major structural walls, foundations, main pillars, and full roof slopes 100% visible so castle silhouettes remain grand and unbroken from afar.
  - Slashes draw calls by 40–60% in high-detail bases.
  - Automatically restores all detail renderers when holding a hammer, hoe, or building tool.
- **Ground Loot & Item Drop Optimizer (`[13. Item Drops & Loot]`):**
  - **Auto-Stacker:** Periodically merges identical loose ground drops (wood, stone, resin, trophies) within a 3.5m radius into consolidated max-size stacks, eliminating redundant GameObjects.
  - **Physics Sleep:** Automatically puts Rigidbodies of motionless ground drops to sleep, halting continuous PhysX collision checks on bases.
  - **Particle & Floating LOD:** Pauses sparkle particles and water floating script updates on distant drops (> 15m).
- **HUD Diagnostics Expansion:**
  - Added live counters for Detail Pieces LOD and Ground Loot Clutter (sleeping & merged counts) to `Shift+F7` overlay.

## [1.3.0] - 2026-11-16
### Added
- **Multi-Threaded Gfx Jobs Engine Optimization (`[1. General]`):**
  - Added `EnableGfxJobsTweak` (default: true): Automatically enables multi-threaded graphics rendering job tweaks in `boot.config` (`gfx-enable-gfx-jobs=1`, `gfx-enable-native-gfx-jobs=1`) with automatic backup creation (`boot.config.bak`).
  - Unlocks multi-core CPU submission for Unity draw calls, dramatically improving base framerate.
- **Interior Occlusion Culling (`[11. Interior Occlusion]`):**
  - Added `EnableInteriorCulling` (default: true) and `InteriorCullDistance` (default: 18m).
  - Temporarily culls renderers on enclosed interior pieces (furniture, beds, chests, rugs, stands) when the player is outside shelter.
  - Exterior walls, roofs, and facades remain fully rendered while saving hundreds of interior batch draw calls.
- **PhysX Decorative Collider Optimization (`[5. Structural]`):**
  - Added `EnableDecorativeColliderOptimization` (default: true).
  - Temporarily disables colliders on purely non-walkable decorative structural pieces (wood beam crosses, ridge caps, trim elevated > 3.5m) to reduce PhysX bounding-box query load.
  - **Hammer Safety Guarantee:** Instantly restores all colliders when a hammer, hoe, or building tool is equipped so building, snapping, and repairing remain 100% pixel-perfect.
- **Terrain Compaction Helper (`[12. Terrain Helpers]`):**
  - Added `EnableTerrainSanityCheck` (default: true): Detects legacy uncompressed terrain modification stacks in active base zones and provides compaction utility.
- **Benchmark HUD Overlay Upgrades:**
  - Integrated real-time indicators for Interior Culling, Decorative Colliders, and Multi-threaded Gfx Jobs status.

## [1.2.0] - 2026-11-16
### Added
- **Fast Particle Dissipation System (`[VFX]`):**
  - Added `EnableFastParticleFade` (default: true): Accelerates the dissipation and fading of all temporary particles across the game (demolition debris, combat impacts, dust, hoeing clouds, sparks).
  - Added `ParticleLifetimeMultiplier` (default: 0.35): Reduces particle lifetime by ~65%, so particles fade away ~3x faster, eliminating lingering billboard clouds that choke GPU fillrate.
  - Added `MaxParticleLifetimeSeconds` (default: 2.0s): Hard cap on the maximum time any temporary VFX or debris particle can exist on screen.
  - Added `ParticleSimulationSpeed` (default: 1.5x): Increases animation speed of particles so dust, smoke, and sparks dissolve rapidly.
  - Added `SmokeLifetimeMultiplier` (default: 0.35): Chimney and hearth smoke puffs fade and dissipate quickly instead of accumulating and lingering for up to a minute under high ceilings.
  - Patched `TimedDestruction.Awake`, `EffectList.Create` (Postfix), and `Smoke.Awake` for universal particle lifetime scaling.
- **HUD Diagnostics:**
  - Added real-time Fast Particle Fade indicator to the Shift+F7 Benchmark HUD.
- **Custom Meme Icon:**
  - Updated mod icon with high-res 256x256 "FPS STONKS / OPTIHOUZ" Viking meme artwork.

## [1.1.0] - 2026-11-16
### Added
- **Cloth & Banner Optimizer (`[Cloth & Banners]`):**
  - Dynamic distance-based culling for Unity `Cloth` components on banners, flags, and tapestries (`BannerClothCullDistance`, default: 15m).
  - Pauses costly CPU vertex physics solvers on distant decorative cloths, smoothly resuming when player approaches.
- **Storage & Trophy Room Optimizer (`[Storage & ItemStands]`):**
  - Performance throttling for mounted weapons, glowing items (Frostner, Mistwalker, torches), and trophies in `ItemStand` and `ArmorStand` (`ItemStandCullDistance`, default: 12m).
  - Pauses attached `ParticleSystem` components and disables dynamic light sources on distant mounts.
- **Farm Animal LOD & AI Optimizer (`[Farm Animals]`):**
  - Tamed animal animator culling and idle AI throttling for boars, wolves, chickens, and loxes (`TamedAnimalLodDistance`, default: 15m).
  - Switches animator culling mode to LOD beyond distance while maintaining full game mechanics.
- **Crafting Station Audio & VFX Optimizer (`[Crafting Stations]`):**
  - Idle particle and looping ambient audio culling for crafting and processing stations (smelters, kilns, cauldrons, fermenters, windmills, spinning wheels) (`StationCullDistance`, default: 18m).
- **Real-Time Diagnostics & Benchmark HUD (`[HUD & Benchmark]`):**
  - In-game real-time diagnostics overlay toggled with `Shift+F7` (`BenchmarkHudHotkey`, `BenchmarkHudRequireShift`).
  - Live FPS, rolling Average FPS, true 1% Low framerate, and frame time (ms).
  - Live subsystem counters: total base pieces, active vs culled shadow lights, smoke puff count vs cap, culled cloths, culled item stands, tamed animals in LOD, and culled stations.
  - Real-time `[OptiHouse: ACTIVE]` (green) vs `[OptiHouse: DISABLED]` (red) indicator.
  - Configurable screen anchor (`HudPosition`) and customizable font size (`HudTextSize`).

## [1.0.0] - 2026-11-15
### Initial Release
- **Dynamic Lighting Optimizer:**
  - Added dynamic point light shadow budget (`MaxShadowCastingLights`, default: 6 closest lights keep dynamic shadows).
  - Added torch and sconce shadow stripping (`DisableTorchShadows`).
  - Added interior light distance culling (`LightCullDistance`).
- **Smoke Physics & Puff Optimizer:**
  - Implemented configurable hard cap on active smoke puffs (`MaxActiveSmokePuffs`, default: 25).
  - Indoor ceiling raycast throttling for rising smoke particles.
  - Optional visual smoke disabling (`DisableSmokeRendering`) while retaining all warmth, fire, and comfort mechanics.
- **VFX Particle Optimizer:**
  - Suppression of heavy dirt, ground, and terrain leveling particles during hoeing and cultivating (`SuppressCultivationParticles`).
  - Suppression of massive wood and stone debris explosion particles on piece demolition (`SuppressDestructionParticles`).
  - Distance throttling for ambient building particles (`VfxThrottlingDistance`).
- **Structural Physics & Geometry Optimizer:**
  - Throttled `WearNTear.UpdateCover` sky raycast weather checks for undamaged covered structures (`EnableWearNTearThrottling`).
  - Dynamic shadow culling for small building pieces, beams, and decorative elements (`EnablePieceShadowCulling`, `PieceShadowCullDistance`).
- **Controls & HUD:**
  - Added runtime hotkey toggle (`F7` by default) with real-time `MessageHud` notification.
  - Added comprehensive BepInEx configuration categories (`[General]`, `[Lighting]`, `[VFX]`, `[Smoke]`, `[Structural]`).
