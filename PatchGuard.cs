using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ZamsCollectibles
{
    /// <summary>
    /// Keeps our Harmony patches from taking the whole mod down when a game update changes the code
    /// they patch. The game's loader applies every patch and then runs Init and OnEnable in one try:
    /// a single patch that can't be applied, or an exception in OnEnable, and it turns the mod off.
    ///
    /// So each patch checks its target in Prepare (Harmony skips the patch when it returns false,
    /// before looking the target up), reads the target's arguments by position (__0) rather than by
    /// name so a renamed parameter doesn't stop it, and runs its body through Run, which turns that
    /// patch off on its first error and leaves the game's own result. A skipped or failed patch only
    /// loses its feature, and says so in the log.
    /// </summary>
    public static class PatchGuard
    {
        private static readonly HashSet<string> failed = new HashSet<string>();

        /// <summary>
        /// For Prepare: true if the target (looked up by the patch) still exists and returns the
        /// expected type. The patch does the lookup itself, with typeof(TheGameClass): the game's mod
        /// scanner flags reflection on a type it can't see ("reflection-external"), which a Type
        /// passed in here would be, and an undeclared capability gets the mod refused.
        /// </summary>
        public static bool Target(string feature, string name, MethodInfo target, Type returns)
        {
            if (target != null && target.ReturnType == returns) return true;
            // Prepare runs before Init, so ModEntry's log isn't there yet
            UnityEngine.Debug.LogWarning($"[Zam's Collectibles] {name} has changed in this game version; {feature} is off.");
            return false;
        }

        /// <summary>Runs a patch body; false (and the patch off from then on) if it throws.</summary>
        public static bool Run(string feature, Action body)
        {
            if (failed.Contains(feature)) return false;
            try
            {
                body();
                return true;
            }
            catch (Exception e)
            {
                failed.Add(feature);
                ModEntry.Instance?.Log.Warning($"{feature} is off after an error: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
    }
}
