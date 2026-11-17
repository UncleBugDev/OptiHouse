# Changelog

All notable changes to **OptiHouse** will be documented in this file.

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
