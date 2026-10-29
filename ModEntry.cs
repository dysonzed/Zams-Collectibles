using System;
using UnityEngine;
using HarmonyLib;

namespace ZamsCollectibles
{
    /// <summary>
    /// Entry point of the mod. The loader finds the one class implementing IMod, creates it,
    /// applies the [HarmonyPatch] classes of this assembly, then calls Init and OnEnable.
    /// All of that happens in the main menu: no save is loaded yet, so do not touch game
    /// state here. Subscribe to ModHook events instead (see ModdingGuide.md, "Hook Points").
    /// </summary>
    public class ModEntry : IMod
    {
        public static ModEntry Instance { get; private set; }
        public ModManifest Manifest { get; private set; }
        public ModLog Log { get; private set; }

        public void Init(ModManifest manifest)
        {
            Instance = this;
            Manifest = manifest;
            Log = new ModLog(manifest);
            Log.Log($"{manifest.Name} v{manifest.ModVersion} loaded.");
        }

        public void OnEnable()
        {
            // Runs right after Init, still in the main menu. Register hooks, items, loot edits,
            // options and configuration here.
            Log.Log("Enabled.");
        }

        public void OnDisable()
        {
            // Runs when the game shuts down. Unsubscribe what you subscribed in OnEnable.
            Log.Log("Disabled.");
        }
    }
}
