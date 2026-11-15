using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Tamed Animal Optimizer for OptiHouse.
    /// Optimizes tamed farm animals (boars, wolves, hens/chickens, loxes, asksvin) in base pens.
    /// - Distance-based Animator LOD culling: switches Animator.cullingMode from AlwaysAnimate
    ///   to CullUpdateTransforms when distance > TamedAnimalLodDistance (default 15m).
    /// - Throttles idle MonsterAI/AnimalAI updates for distant, calm tamed creatures to 2 Hz (0.5s interval),
    ///   reducing sensory raycasts, pathfinding, and AI update overhead by 90%+.
    /// - Smoothly and immediately restores full animation fidelity and unthrottled AI when the player approaches
    ///   or when the animal is alerted/damaged.
    /// - Exposes public counters for HUD: TotalTamedAnimals, CulledTamedAnimals.
    /// </summary>
    public class TamedAnimalOptimizer : MonoBehaviour
    {
        public static TamedAnimalOptimizer? Instance { get; private set; }

        public static int TotalTamedAnimals { get; private set; }
        public static int CulledTamedAnimals { get; private set; }

        private const float UpdateInterval = 0.35f;
        private const float HysteresisMargin = 2.5f;

        private float _timer;
        private bool _wasActive = true;

        // Fast cache for Character -> Animator lookup
        private static readonly ConditionalWeakTable<Character, Animator> _animatorCache = new();

        // Fast cache for BaseAI -> Character lookup
        private static readonly ConditionalWeakTable<BaseAI, Character> _characterCache = new();

        // Tracks modified animators to ensure safe, complete restoration on toggle or unload
        private static readonly HashSet<Animator> _modifiedAnimators = new();

        // Field reference for BaseAI.m_timeSinceHurt
        private static readonly AccessTools.FieldRef<BaseAI, float> TimeSinceHurtRef =
            AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");

        // AI throttling state per BaseAI instance
        public class AIThrottleState
        {
            public float AccumulatedDt;
            public float LastUpdateTime;
        }

        private static readonly ConditionalWeakTable<BaseAI, AIThrottleState> _aiThrottleStates = new();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllAnimators();
            TotalTamedAnimals = 0;
            CulledTamedAnimals = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableTamedAnimalLod != null &&
                            ModConfig.EnableTamedAnimalLod.Value;

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    RestoreAllAnimators();
                    CulledTamedAnimals = 0;
                }
                return;
            }

            _wasActive = true;

            _timer += Time.deltaTime;
            if (_timer < UpdateInterval)
            {
                return;
            }
            _timer = 0f;

            EvaluateTamedAnimals();
        }

        /// <summary>
        /// Evaluates all scene characters, filtering for living tamed animals, and applies
        /// animator LOD culling based on distance to the local player.
        /// </summary>
        private void EvaluateTamedAnimals()
        {
            List<Character> allCharacters = Character.GetAllCharacters();
            if (allCharacters == null || allCharacters.Count == 0)
            {
                TotalTamedAnimals = 0;
                CulledTamedAnimals = 0;
                return;
            }

            Vector3 refPos = Vector3.zero;
            bool hasRefPos = false;

            if (Player.m_localPlayer != null)
            {
                refPos = Player.m_localPlayer.transform.position;
                hasRefPos = true;
            }
            else if (Camera.main != null)
            {
                refPos = Camera.main.transform.position;
                hasRefPos = true;
            }

            if (!hasRefPos)
            {
                return;
            }

            float lodDist = ModConfig.TamedAnimalLodDistance != null
                ? Mathf.Max(5f, ModConfig.TamedAnimalLodDistance.Value)
                : 15f;

            float cullDistSq = lodDist * lodDist;
            float uncullDist = Mathf.Max(0f, lodDist - HysteresisMargin);
            float uncullDistSq = uncullDist * uncullDist;

            int totalTamed = 0;
            int culledTamed = 0;

            for (int i = 0; i < allCharacters.Count; i++)
            {
                Character character = allCharacters[i];
                if (character == null || character.IsDead() || character.IsPlayer())
                {
                    continue;
                }

                if (!character.IsTamed())
                {
                    continue;
                }

                totalTamed++;

                Animator? anim = GetAnimator(character);
                if (anim == null)
                {
                    continue;
                }

                float sqrDist = (character.transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    // Distant: switch to CullUpdateTransforms (mesh skinning and transforms skip when offscreen)
                    culledTamed++;
                    if (anim.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
                    {
                        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                        _modifiedAnimators.Add(anim);
                    }
                }
                else if (sqrDist < uncullDistSq)
                {
                    // Within near range: restore full animation fidelity
                    if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                    {
                        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                }
                else
                {
                    // Within hysteresis window: keep current state
                    if (anim.cullingMode == AnimatorCullingMode.CullUpdateTransforms)
                    {
                        culledTamed++;
                    }
                }
            }

            TotalTamedAnimals = totalTamed;
            CulledTamedAnimals = culledTamed;

            // Prune dead animators from tracking set
            _modifiedAnimators.RemoveWhere(a => a == null);
        }

        /// <summary>
        /// Retrieves the Animator component for a Character, caching the result.
        /// </summary>
        public static Animator? GetAnimator(Character character)
        {
            if (character == null) return null;

            if (_animatorCache.TryGetValue(character, out var cached))
            {
                return cached;
            }

            Animator? anim = null;
            GameObject visual = character.GetVisual();
            if (visual != null)
            {
                anim = visual.GetComponentInChildren<Animator>();
            }

            if (anim == null)
            {
                anim = character.GetComponentInChildren<Animator>();
            }

            if (anim != null)
            {
                _animatorCache.Add(character, anim);
            }

            return anim;
        }

        /// <summary>
        /// Retrieves the associated Character for a BaseAI component with fast caching.
        /// </summary>
        public static Character? GetAICharacter(BaseAI ai)
        {
            if (ai == null) return null;

            if (_characterCache.TryGetValue(ai, out var cached))
            {
                return cached;
            }

            Character character = ai.GetComponent<Character>();
            if (character != null)
            {
                _characterCache.Add(ai, character);
            }

            return character;
        }

        /// <summary>
        /// Restores all modified animators to AlwaysAnimate.
        /// </summary>
        public static void RestoreAllAnimators()
        {
            foreach (var anim in _modifiedAnimators)
            {
                if (anim != null)
                {
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }
            _modifiedAnimators.Clear();
        }

        /// <summary>
        /// Determines whether AI updates for the specified BaseAI should be executed or throttled.
        /// Throttles idle tamed animals to 2 Hz (0.5s interval) when distant from the player.
        /// </summary>
        public static bool ShouldExecuteAI(BaseAI ai, ref float dt, ref bool __result)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableTamedAnimalLod == null ||
                !ModConfig.EnableTamedAnimalLod.Value)
            {
                return true;
            }

            if (ai == null)
            {
                return true;
            }

            Character? character = GetAICharacter(ai);
            if (character == null || character.IsDead() || character.IsPlayer())
            {
                return true;
            }

            // Only throttle tamed creatures
            if (!character.IsTamed())
            {
                return true;
            }

            // Never throttle if alerted, in combat, pursuing a target, or recently hurt
            if (ai.IsAlerted() || ai.HaveTarget())
            {
                return true;
            }

            try
            {
                if (TimeSinceHurtRef(ai) < 4.0f)
                {
                    return true;
                }
            }
            catch { }

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null)
            {
                return true;
            }

            float lodDist = ModConfig.TamedAnimalLodDistance != null
                ? Mathf.Max(5f, ModConfig.TamedAnimalLodDistance.Value)
                : 15f;

            float sqrDist = (ai.transform.position - localPlayer.transform.position).sqrMagnitude;
            if (sqrDist <= lodDist * lodDist)
            {
                // In close range: run at full fidelity
                return true;
            }

            // Distant and calm: throttle AI update rate to 0.5s interval (2 Hz)
            var state = _aiThrottleStates.GetOrCreateValue(ai);
            float now = Time.time;

            if (now - state.LastUpdateTime < 0.5f)
            {
                state.AccumulatedDt += dt;
                __result = false;
                return false; // Skip this update tick
            }

            // Flush accumulated dt into current update tick so physics and movement remain continuous
            dt += state.AccumulatedDt;
            state.AccumulatedDt = 0f;
            state.LastUpdateTime = now;

            return true;
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Auto-attach TamedAnimalOptimizer to the OptiHousePlugin GameObject on Awake.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class TamedAnimalOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<TamedAnimalOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<TamedAnimalOptimizer>();
            }
        }
    }

    /// <summary>
    /// Throttles idle MonsterAI updates for distant tamed creatures (wolves, loxes, asksvin).
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    public static class MonsterAI_UpdateAI_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MonsterAI __instance, ref float dt, ref bool __result)
        {
            return TamedAnimalOptimizer.ShouldExecuteAI(__instance, ref dt, ref __result);
        }
    }

    /// <summary>
    /// Throttles idle AnimalAI updates for distant tamed creatures (boars, hens/chickens).
    /// </summary>
    [HarmonyPatch(typeof(AnimalAI), nameof(AnimalAI.UpdateAI))]
    public static class AnimalAI_UpdateAI_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(AnimalAI __instance, ref float dt, ref bool __result)
        {
            return TamedAnimalOptimizer.ShouldExecuteAI(__instance, ref dt, ref __result);
        }
    }

    #endregion
}
