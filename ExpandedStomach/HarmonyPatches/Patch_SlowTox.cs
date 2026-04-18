using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ExpandedStomach.HarmonyPatches
{
    /// <summary>
    /// SlowTox compatibility module.
    ///
    /// No Harmony patches are applied here. We interact with SlowTox through
    /// two mechanisms that avoid any method patching entirely (and therefore
    /// sidestep JIT-inlining issues that affect other compat patches):
    ///
    ///   1. Entity stat "slowtox:tolerance" — writing a named "fatTolerance" key
    ///      scales how hard it is for a fat player to accumulate intoxication per
    ///      drink. Higher fat = higher tolerance = harder to get drunk.
    ///
    ///   2. Per-entity _config field replacement — SlowTox's SlowToxBehavior holds
    ///      a private _config field that normally points to the shared
    ///      ModConfig.Instance.Intoxication singleton. We create a fresh
    ///      IntoxicationConfig copy for each player and lower its DecayRate in
    ///      proportion to their fat level. Because SlowTox reads _config.DecayRate
    ///      inside DigestToxins on every tick, the fat player's intoxication decays
    ///      more slowly, making the effects linger longer. Thin players are left
    ///      on the unmodified singleton and sober up at the normal rate.
    /// </summary>
    public static class SlowToxCompat
    {
        // Cached reflection handles — resolved once in Initialize().
        private static FieldInfo      _configField;
        private static PropertyInfo[] _configProps;
        private static PropertyInfo   _decayRateProp;
        private static PropertyInfo   _modConfigInstanceProp;
        private static PropertyInfo   _modConfigIntoxProp;

        public static bool IsActive { get; private set; }

        /// <summary>
        /// Resolves all SlowTox types and members via reflection.
        /// Must be called from <see cref="ExpandedStomachModSystem.StartServerSide"/>
        /// after SlowTox has been confirmed present by the mod loader.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                Type behaviorType = AccessTools.TypeByName("SlowTox.SlowToxBehavior");
                Type modCfgType   = AccessTools.TypeByName("SlowTox.Config.ModConfig");

                if (behaviorType == null || modCfgType == null)
                {
                    ExpandedStomachModSystem.Logger.Error("SlowToxCompat: could not find SlowTox types — compat disabled.");
                    return;
                }

                _configField = AccessTools.Field(behaviorType, "_config");
                if (_configField == null)
                {
                    ExpandedStomachModSystem.Logger.Error("SlowToxCompat: could not find SlowToxBehavior._config — compat disabled.");
                    return;
                }

                // Derive the IntoxicationConfig type directly from the field rather than
                // looking it up by name, so a namespace refactor in SlowTox won't break us.
                Type configType = _configField.FieldType;
                _configProps   = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                _decayRateProp = configType.GetProperty("DecayRate");
                if (_decayRateProp == null)
                {
                    ExpandedStomachModSystem.Logger.Error("SlowToxCompat: could not find IntoxicationConfig.DecayRate — compat disabled.");
                    return;
                }

                _modConfigInstanceProp = modCfgType.GetProperty("Instance",     BindingFlags.Public | BindingFlags.Static);
                _modConfigIntoxProp    = modCfgType.GetProperty("Intoxication", BindingFlags.Public | BindingFlags.Instance);
                if (_modConfigInstanceProp == null || _modConfigIntoxProp == null)
                {
                    ExpandedStomachModSystem.Logger.Error("SlowToxCompat: could not find ModConfig.Instance or .Intoxication — compat disabled.");
                    return;
                }

                IsActive = true;
                ExpandedStomachModSystem.Logger.Notification("SlowToxCompat: initialized successfully.");
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error("SlowToxCompat: exception during initialization — " + ex.Message);
            }
        }

        /// <summary>
        /// Updates both the tolerance stat and the per-entity decay rate config
        /// to reflect the entity's current fat level. Safe to call when
        /// <see cref="IsActive"/> is false — exits immediately as a no-op.
        /// </summary>
        public static void UpdateEntityForFat(Entity entity, float fatMeter)
        {
            if (!IsActive) return;
            UpdateTolerance(entity, fatMeter);
            UpdateDecayRate(entity, fatMeter);
        }

        // ── private helpers ──────────────────────────────────────────────────

        private static void UpdateTolerance(Entity entity, float fatMeter)
        {
            // "slowtox:tolerance" is a FlatMultiply stat — every registered key's
            // value is multiplied together. A multiplier of 1.0 is neutral.
            // We scale linearly from 1.0 at fat 0 up to (1 + scale) at fat 1.
            float scale      = ExpandedStomachModSystem.sConfig?.slowToxFatToleranceScale ?? 0.5f;
            float multiplier = 1f + fatMeter * scale;
            entity.Stats.Set("slowtox:tolerance", "fatTolerance", multiplier, persistent: true);
        }

        private static void UpdateDecayRate(Entity entity, float fatMeter)
        {
            EntityBehavior behavior = entity.GetBehavior("slowtox");
            if (behavior == null) return; // SlowTox not active on this entity yet

            // Re-fetch the canonical base config on every call so that any
            // server-side SlowTox config reloads are always honoured.
            object modConfigInstance = _modConfigInstanceProp.GetValue(null);
            if (modConfigInstance == null) return;
            object baseConfig = _modConfigIntoxProp.GetValue(modConfigInstance);
            if (baseConfig == null) return;

            // Build a fresh per-entity copy of IntoxicationConfig, starting with
            // a verbatim copy of all properties from the live singleton.
            object newConfig = Activator.CreateInstance(_configField.FieldType);
            foreach (PropertyInfo prop in _configProps)
            {
                if (prop.CanWrite)
                    prop.SetValue(newConfig, prop.GetValue(baseConfig));
            }

            // Reduce decay rate proportionally to fat level.
            //   fatMeter = 0.0 → decayRate unchanged (no effect on thin players)
            //   fatMeter = 1.0 → decayRate × (1 - scale)
            // Floored at 10 % of base so intoxication can never become permanent
            // even at max fat.
            float scale    = ExpandedStomachModSystem.sConfig?.slowToxFatDecayRateScale ?? 0.5f;
            float baseRate = (float)_decayRateProp.GetValue(baseConfig);
            float adjusted = baseRate * (1f - fatMeter * scale);
            adjusted       = MathF.Max(adjusted, baseRate * 0.1f);
            _decayRateProp.SetValue(newConfig, adjusted);

            // Assign the personalised config to this entity's SlowToxBehavior.
            // All other players' behaviors still reference the shared singleton.
            _configField.SetValue(behavior, newConfig);
        }
    }
}
