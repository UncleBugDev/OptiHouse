<p align="center">
  <img src="icon.png" width="220" height="220" alt="OptiHouse Logo" />
</p>

<h1 align="center">OptiHouse ⚡</h1>

<p align="center">
  <b>High-Performance Client-Side Base Optimization Mod for Valheim</b><br />
  <i>Eliminates massive framerate drops in large player bases, villages, and castles with zero risk to game worlds.</i>
</p>

<p align="center">
  <a href="https://github.com/UncleBugDev/OptiHouse/releases"><img src="https://img.shields.io/github/v/release/UncleBugDev/OptiHouse?style=for-the-badge&color=007ec6&logo=github" alt="GitHub Release" /></a>
  <a href="https://thunderstore.io/c/valheim/p/UncleBugDev/OptiHouse/"><img src="https://img.shields.io/badge/Thunderstore-v1.3.0-orange?style=for-the-badge&logo=thunderstore" alt="Thunderstore" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License: MIT" /></a>
  <img src="https://img.shields.io/badge/Valheim-PC%20%7C%20Steam-red?style=for-the-badge&logo=steam" alt="Platform" />
</p>

---

### 🛡️ Zero-Risk Guarantee (No World or Structure Corruption)
Unlike destructive mods that merge or delete GameObjects and corrupt Valheim's persistent ZDO database (`.db` / `.fwl`), **OptiHouse operates 100% on the client presentation layer**:
- **Zero ZDO modifications:** Pieces are never deleted, baked into new GameObjects, or desynchronized.
- **Save files remain 100% vanilla:** You can safely install or uninstall OptiHouse at any time with zero impact on your saved worlds.
- **Hammer Safety:** Equipping a hammer or building tool instantly restores all colliders for pixel-perfect snapping and repairing.

---

**OptiHouse** is a client-side performance optimization mod engineered to eliminate framerate drops, stuttering, and render lag in large Valheim bases, villages, and mega-castles. 

When hundreds of building pieces, torches, braziers, smoke puffs, tamed animals, trophies, banners, and crafting stations meet in one area, Valheim's rendering and physics pipeline bottlenecks your GPU and CPU with thousands of unculled shadow draw calls, redundant particle bursts, physics vertex calculations, and excessive indoor smoke raycasts. **OptiHouse** smartly optimizes these subsystems without altering building integrity, fire mechanics, or core gameplay visuals.

---

## 🚀 Key Features & Optimizations

### 💡 1. Dynamic Lighting & Shadow Management (`[Lighting]`)
- **Point Light Shadow Limiter:** Dynamically tracks light sources around the player and restricts active shadow-casting point lights to the closest lights (default: 6). Excess lights continue to illuminate surfaces smoothly with shadows set to `None`, drastically reducing shadow map render passes.
- **Torch & Sconce Shadow Stripping:** Wall torches, sconces, and standing braziers maintain their ambient glow while stripping heavy real-time shadows.
- **Interior Light Distance Culling:** Intelligently reduces rendering overhead for distant small light sources within large settlements.

### 💨 2. Indoor Smoke Physics & Puff Throttling (`[Smoke]`)
- **Smoke Puff Hard Cap:** Enforces a configurable cap on simultaneous active smoke particles (default: 25 vs vanilla's 100+). Prevents massive chimney bottlenecks and physics lag while keeping hearths functional.
- **Ceiling Collision Throttling:** Throttles continuous indoor raycasting for rising smoke puffs to save CPU cycles.
- **Optional Smoke Rendering Toggle:** Option to disable visual particle rendering while fully preserving fire, warmth, cooking, and comfort mechanics.

### 💥 3. VFX & Particle Suppression (`[VFX]`)
- **Cultivation & Terraforming Throttle:** Suppresses repetitive, heavy particle bursts when cultivating soil, leveling ground, or using the hoe in base areas.
- **Demolition Dust Suppression:** Suppresses heavy debris chunk bursts and wood/stone dust particle spam when tearing down or rebuilding structures.
- **Zero Risk to Gameplay:** Does not affect item drops, resource yields, or damage indicators.

### 🏛️ 4. Structural Physics & Mesh Optimizations (`[Structural]`)
- **WearNTear Weather Cover Throttling:** Throttles redundant `UpdateCover` sky raycast checks on undamaged, static building pieces by 3–5x. Drastically reduces main thread CPU frame times in dense bases.
- **Small Piece Shadow Culling:** Automatically disables shadow-casting on small structural pieces (short beams <= 2m, iron bars, decorative poles) beyond a configurable distance (default: 15m). Massive bases look identical from a distance while saving hundreds of shadow draw calls.
- **100% Structural Support Guarantee:** OptiHouse strictly preserves all physics colliders on all building pieces, ensuring Valheim's internal `WearNTear.UpdateSupport()` stability graph is never compromised. No buildings or roofs will ever collapse.

### 🚪 5. Interior Occlusion Culling (`[Interior Occlusion]`)
- **Enclosed Base Occlusion:** Valheim lacks indoor occlusion culling, meaning indoor furniture, chests, and decor inside a closed building are still drawn even when standing outside. OptiHouse detects when the player is outside shelter and dynamically suppresses renderers of distant interior items beyond 18m.
- **Instant Restore:** Instantly restores all indoor renderers the moment you approach an entrance or enter shelter. Exterior walls and roofs always remain 100% rendered.

### ⚡ 6. Multi-Threaded Gfx Jobs Engine Tweak (`[General]`)
- **boot.config Tweaks:** Unity's engine supports multi-threaded graphics rendering jobs (`gfx-enable-gfx-jobs=1`, `gfx-enable-native-gfx-jobs=1`) which offloads draw call submission across CPU cores. OptiHouse automatically configures this in `valheim_Data/boot.config` with a safe backup.

### 🚩 7. Cloth & Banner Physics Culling (`[Cloth & Banners]`)
- **Unity Cloth Solver Culling:** Dynamically pauses heavy CPU vertex physics solvers on distant banners, flags, and tapestries beyond the culling distance (default: 15m).
- **Hysteresis Smoothing:** 3m hysteresis margin prevents rapid toggling when moving near the boundary; simulations smoothly resume when approaching.

### 📦 8. Storage & Trophy Stand Throttling (`[Storage & ItemStands]`)
- **Item & Armor Stand Throttling:** Drastically cuts lag in dense storage halls and armories. Mounted glowing weapons (Frostner, Mistwalker, torches) and trophies pause particle systems and disable dynamic point lights when beyond the culling threshold (default: 12m).
- **Instant Restore:** Automatically restores full visual brilliance when walking up to inspect items or stands.

### 🐗 9. Farm Animal LOD & AI Throttling (`[Farm Animals]`)
- **Tamed Animal Animator LOD:** Switches animators on tamed boars, wolves, chickens, and loxes in pens to efficient LOD culling modes beyond distance (default: 15m).
- **Idle AI Throttling:** Throttles non-critical pathfinding and idle queries for distant pen animals without interfering with breeding, taming, or defense mechanics.

### 🔨 10. Crafting Station Audio & Particle Culling (`[Crafting Stations]`)
- **Processing Station Culling:** Pauses heavy particle emitters (sparks, bubbling liquids, grinding dust) and looping ambient sound sources on smelters, blast furnaces, kilns, cauldrons, fermenters, spinning wheels, and windmills beyond distance (default: 18m).

### 📊 11. Real-Time Benchmark HUD Overlay (`[HUD & Benchmark]`)
- **In-Game Diagnostics (`Shift+F7`):** High-contrast, flicker-free IMGUI overlay displaying real-time FPS, rolling Average FPS, true 1% Low framerate, and frame time in milliseconds.
- **Live Base Diagnostics:** Displays total base pieces, shadow lights, smoke puffs, banner cloths, item stands, tamed animals, crafting stations, interior culled items, detail pieces LOD, ground loot consolidation, and multi-threaded gfx jobs engine status.
- **Status Indicator:** Shows live `[OptiHouse: ACTIVE]` (green) or `[OptiHouse: DISABLED]` (red) status.
- **Customizable:** Change hotkey, anchor position (`TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`), and text size.

### ⌨️ 12. Runtime Toggle Hotkey
- **Instant Toggle (`F7` by default):** Switch optimizations on and off in real time with an on-screen HUD message to easily benchmark before-and-after FPS in your base.

---

## ⚙️ Configuration

A configuration file is generated at `BepInEx/config/com.unclebug.optihouse.cfg` upon first launch. All settings can be tweaked live in-game using the BepInEx Configuration Manager (F1).

| Category | Setting | Default | Description |
|---|---|---|---|
| **[1. General]** | `MasterOptimizationEnabled` | `true` | Master switch for all OptiHouse base optimizations. |
| **[1. General]** | `ToggleHotkey` | `F7` | Hotkey to toggle optimizations on the fly. |
| **[1. General]** | `ShowNotificationOnToggle` | `true` | Show on-screen MessageHud notification when toggling. |
| **[1. General]** | `EnableLogging` | `false` | Enable detailed diagnostic logging in BepInEx console. |
| **[1. General]** | `EnableGfxJobsTweak` | `true` | Enable multi-threaded graphics rendering job tweaks in boot.config. |
| **[2. Lighting]** | `EnableLightOptimization` | `true` | Enable dynamic base lighting and shadow management. |
| **[2. Lighting]** | `MaxShadowCastingLights` | `6` | Maximum number of point lights allowed to cast real-time shadows near the player (0–32). |
| **[2. Lighting]** | `DisableTorchShadows` | `true` | Disable shadow casting on small wall torches and sconces. |
| **[2. Lighting]** | `LightCullDistance` | `45.0` | Maximum distance at which small interior lights are actively processed (meters). |
| **[3. VFX]** | `EnableVfxOptimization` | `true` | Enable particle and visual effect throttling. |
| **[3. VFX]** | `EnableFastParticleFade` | `true` | Accelerate particle dissipation so dust, sparks, debris, and smoke vanish quickly. |
| **[3. VFX]** | `ParticleLifetimeMultiplier` | `0.35` | Multiplier for particle lifetime (0.35 = particles fade ~3x faster, no lingering clouds). |
| **[3. VFX]** | `MaxParticleLifetimeSeconds` | `2.0` | Maximum seconds any temporary VFX or debris particle can exist on screen. |
| **[3. VFX]** | `ParticleSimulationSpeed` | `1.5` | Speed multiplier for particle animations so dust and sparks disperse rapidly. |
| **[3. VFX]** | `SuppressCultivationParticles` | `true` | Suppress heavy particle bursts when hoeing or cultivating ground. |
| **[3. VFX]** | `SuppressDestructionParticles` | `true` | Suppress massive dust/debris spam when breaking pieces. |
| **[3. VFX]** | `VfxThrottlingDistance` | `30.0` | Distance beyond which non-critical ambient particles are throttled. |
| **[4. Smoke]** | `EnableSmokeOptimization` | `true` | Enable indoor smoke puff and physics optimization. |
| **[4. Smoke]** | `MaxActiveSmokePuffs` | `25` | Hard limit for simultaneous active smoke puffs in base areas. |
| **[4. Smoke]** | `SmokeLifetimeMultiplier` | `0.35` | Multiplier for chimney and hearth smoke puff lifetime (dissolves quickly). |
| **[4. Smoke]** | `ThrottleSmokeRaycasts` | `true` | Throttle indoor ceiling collision raycasts for smoke puffs. |
| **[4. Smoke]** | `DisableSmokeRendering` | `false` | Disable smoke particle visuals while keeping fire/comfort mechanics. |
| **[5. Structural]** | `EnableStructuralOptimization` | `true` | Enable structural piece shadow and CPU raycast optimizations. |
| **[5. Structural]** | `EnableWearNTearThrottling` | `true` | Throttle periodic sky/weather raycast checks on intact pieces. |
| **[5. Structural]** | `WearNTearThrottleInterval` | `10.0` | Seconds between weather protection raycasts for static covered pieces. |
| **[5. Structural]** | `EnablePieceShadowCulling` | `true` | Disable shadow casting on small structural elements beyond culling distance. |
| **[5. Structural]** | `PieceShadowCullDistance` | `15.0` | Distance beyond which small structural pieces stop casting shadows. |
| **[5. Structural]** | `EnableDecorativeColliderOptimization` | `true` | Temporarily disable colliders on non-walkable decorative pieces. |
| **[6. Cloth & Banners]** | `EnableClothOptimization` | `true` | Enable distance-based culling for Unity Cloth components on banners/flags. |
| **[6. Cloth & Banners]** | `BannerClothCullDistance` | `15.0` | Distance beyond which cloth physics simulation is disabled. |
| **[7. Storage & ItemStands]** | `EnableItemStandOptimization` | `true` | Enable particle and light throttling on distant mounted trophies and weapons. |
| **[7. Storage & ItemStands]** | `ItemStandCullDistance` | `12.0` | Distance beyond which item stand particles and lights are culled. |
| **[8. Farm Animals]** | `EnableTamedAnimalLod` | `true` | Enable animator LOD culling and AI throttling for distant tamed animals. |
| **[8. Farm Animals]** | `TamedAnimalLodDistance` | `15.0` | Distance beyond which tamed animal animators switch to LOD mode. |
| **[9. Crafting Stations]** | `EnableStationOptimization` | `true` | Enable particle and ambient audio culling on distant crafting stations. |
| **[9. Crafting Stations]** | `StationCullDistance` | `18.0` | Distance beyond which crafting station idle particles and loops pause. |
| **[10. HUD & Benchmark]** | `EnableBenchmarkHUD` | `true` | Enable the real-time in-game benchmark and diagnostics overlay. |
| **[10. HUD & Benchmark]** | `BenchmarkHudHotkey` | `F7` | Hotkey to toggle the benchmark HUD overlay. |
| **[10. HUD & Benchmark]** | `BenchmarkHudRequireShift` | `true` | Require holding Shift with hotkey (Shift+F7) to toggle the benchmark HUD. |
| **[10. HUD & Benchmark]** | `HudTextSize` | `13` | Font size for benchmark HUD overlay text (10–24). |
| **[10. HUD & Benchmark]** | `HudPosition` | `TopLeft` | Screen anchor position (`TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`). |
| **[11. Interior Occlusion]** | `EnableInteriorCulling` | `true` | Enable interior piece and furniture culling when outside base shelter. |
| **[11. Interior Occlusion]** | `InteriorCullDistance` | `18.0` | Distance (in meters) beyond which interior pieces are culled outside shelter. |
| **[12. Terrain Helpers]** | `EnableTerrainSanityCheck` | `true` | Detect legacy uncompressed terrain modification stacks in base zones. |

---

## 🛠️ Installation

### Thunderstore Mod Manager (Recommended)
1. Install [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager).
2. Search for **OptiHouse** and click **Download**.

### Manual Installation
1. Ensure [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) is installed.
2. Download `OptiHouse_v1.3.0.zip` from Thunderstore or GitHub Releases.
3. Extract `OptiHouse.dll` into your Valheim directory under `BepInEx/plugins/UncleBugDev-OptiHouse/`.
4. Launch the game!

---

## 🌐 Compatibility & Multiplayer

- **Client-Side Only:** OptiHouse only runs on the client. It does **not** need to be installed on dedicated servers.
- **Server Friendly:** Safe to use on vanilla servers, community servers, and modded servers.
- **Mod Compatibility:** Fully compatible with Valheim+, ComfyQuickTransfer, PingMaker, PlantEasily, Gizmo, and other popular building mods.

---

## 🌟 Special Thanks & Credits

This mod was created and optimized thanks to the streamer **DaymonMontage**!

- 🎮 **Twitch Channel:** [twitch.tv/daymonmontage](https://www.twitch.tv/daymonmontage)
- 🌐 **Daymon Community Hub:** [daymonmontage.github.io/daymon-site/](https://daymonmontage.github.io/daymon-site/)

---

## 📜 License & Authors

- Author: **UncleBug** ([GitHub](https://github.com/UncleBugDev))
- Special Thanks & Inspiration: **DaymonMontage** ([Twitch](https://www.twitch.tv/daymonmontage) | [Web Hub](https://daymonmontage.github.io/daymon-site/))
- Licensed under the [MIT License](LICENSE).
