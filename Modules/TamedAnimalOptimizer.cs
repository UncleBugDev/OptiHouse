using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Tamed Animal Optimizer for OptiHouse.
    /// - Switches distant (>18m) tamed animals' Animator.cullingMode to CullUpdateTransforms.
    /// - Throttles idle, stationary tamed MonsterAI/AnimalAI updates from 20 Hz (0.05s) to 5 Hz (0.2s)
    ///   without disrupting movement, combat alertness, feeding, or breeding.
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

        private static readonly ConditionalWeakTable<Character, Animator> _animatorCache = new();
        private static readonly ConditionalWeakTable<BaseAI, Character> _characterCache = new();
        private static readonly ConditionalWeakTable<BaseAI, Rigidbody> _bodyCache = new();
        private static readonly HashSet<Animator> _modifiedAnimators = new();

        private static readonly AccessTools.FieldRef<BaseAI, float> TimeSinceHurtRef =
            AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");

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

        private void EvaluateTamedAnimals()
        {
            List<Character> allCharacters = Character.GetAllCharacters();
            if (allCharacters == null || allCharacters.Count == 0)
            {
                TotalTamedAnimals = 0;
                CulledTamedAnimals = 0;
                return;
            }

            if (!OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                return;
            }

            float lodDist = ModConfig.TamedAnimalLodDistance != null
                ? Mathf.Max(5f, ModConfig.TamedAnimalLodDistance.Value)
                : 18f;

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
                    culledTamed++;
                    if (anim.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
                    {
                        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                        _modifiedAnimators.Add(anim);
                    }
                }
                else if (sqrDist < uncullDistSq)
                {
                    if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                    {
                        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                }
                else if (anim.cullingMode == AnimatorCullingMode.CullUpdateTransforms)
                {
                    culledTamed++;
                }
            }

            TotalTamedAnimals = totalTamed;
            CulledTamedAnimals = culledTamed;
            _modifiedAnimators.RemoveWhere(a => a == null);
        }

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

        public static Character? GetAICharacter(BaseAI ai)
        {
            if (ai == null) return null;
            if (_characterCache.TryGetValue(ai, out var cached)) return cached;

            Character character = ai.GetComponent<Character>();
            if (character != null) _characterCache.Add(ai, character);
            return character;
        }

        public static Rigidbody? GetAIRigidbody(BaseAI ai)
        {
            if (ai == null) return null;
            if (_bodyCache.TryGetValue(ai, out var cached)) return cached;

            Rigidbody body = ai.GetComponent<Rigidbody>();
            if (body != null) _bodyCache.Add(ai, body);
            return body;
        }

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

        public static bool ShouldExecuteAI(BaseAI ai, ref float dt, ref bool __result)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableTamedAnimalLod == null ||
                !ModConfig.EnableTamedAnimalLod.Value)
            {
                return true;
            }

            if (ai == null) return true;

            Character? character = GetAICharacter(ai);
            if (character == null || character.IsDead() || character.IsPlayer() || !character.IsTamed())
            {
                return true;
            }

            if (ai.IsAlerted() || ai.HaveTarget())
            {
                return true;
            }

            try
            {
                if (TimeSinceHurtRef(ai) < 4.0f) return true;
            }
            catch { }

            // Do not throttle steering if animal is actively walking/moving
            Rigidbody? body = GetAIRigidbody(ai);
            if (body != null && body.linearVelocity.sqrMagnitude > 0.05f)
            {
                return true;
            }

            if (!OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                return true;
            }

            float lodDist = ModConfig.TamedAnimalLodDistance != null
                ? Mathf.Max(5f, ModConfig.TamedAnimalLodDistance.Value)
                : 18f;

            if ((ai.transform.position - refPos).sqrMagnitude <= lodDist * lodDist)
            {
                return true;
            }

            var state = _aiThrottleStates.GetOrCreateValue(ai);
            float now = Time.time;

            // Idle stationary distant tamed animals run at 5 Hz (0.2s) instead of 20 Hz
            if (now - state.LastUpdateTime < 0.2f)
            {
                state.AccumulatedDt += dt;
                __result = false;
                return false;
            }

            dt += state.AccumulatedDt;
            state.AccumulatedDt = 0f;
            state.LastUpdateTime = now;
            return true;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    public static class MonsterAI_UpdateAI_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MonsterAI __instance, ref float dt, ref bool __result)
        {
            return TamedAnimalOptimizer.ShouldExecuteAI(__instance, ref dt, ref __result);
        }
    }

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
