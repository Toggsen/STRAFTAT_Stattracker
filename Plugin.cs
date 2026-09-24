using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

// The mod only reads game state, so it doesn't need its own matchmaking pool.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace StatTracker
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "toggsen.straftat.stattracker";
        public const string Name = "StatTracker";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static Tracker Tracker;

        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<KeyboardShortcut> ScopeKey;
        internal static ConfigEntry<bool> VisibleOnStart;
        internal static ConfigEntry<float> PositionX;
        internal static ConfigEntry<float> PositionY;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<bool> WriteHistory;

        private static bool _compatibilityLogged;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ToggleKey = Config.Bind("Controls", "ToggleOverlay", new KeyboardShortcut(KeyCode.F8), "Show / hide the stats overlay.");
            ScopeKey = Config.Bind("Controls", "CycleScope", new KeyboardShortcut(KeyCode.F7), "Switch between Match and Session stats.");
            VisibleOnStart = Config.Bind("Overlay", "VisibleOnStart", true, "Whether the overlay is visible when the game starts.");
            PositionX = Config.Bind("Overlay", "PositionX", -12f, "Horizontal position in pixels. Negative values anchor to the right edge of the screen.");
            PositionY = Config.Bind("Overlay", "PositionY", 120f, "Vertical position in pixels from the top of the screen.");
            FontSize = Config.Bind("Overlay", "FontSize", 16, "Overlay font size at 1080p (scaled automatically for other resolutions).");
            WriteHistory = Config.Bind("History", "WriteMatchHistory", true, "Append a summary line to BepInEx/StatTracker_history.txt at the end of every match.");

            Tracker = new Tracker();

            _harmony = new Harmony(Guid);
            Patches.Apply(_harmony);

            // Some games clean up the BepInEx manager object on scene loads,
            // so the per-frame work runs on our own persistent object.
            var go = new GameObject("StatTracker");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<Overlay>();

            SceneManager.activeSceneChanged += (_, next) => Tracker.OnSceneChanged(next.name);

            Log.LogInfo($"{Name} {Version} loaded.");
        }

        // SteamLobby runs the game's mod scan at startup. Report the result once it has happened.
        internal static void LogCompatibilityOnce()
        {
            if (_compatibilityLogged || AssemblyScanner.AllModNames.Length == 0)
                return;
            _compatibilityLogged = true;

            var incompatible = AssemblyScanner.IncompatibleAssemblyNamesTrimmed;
            if (incompatible.Length == 0)
                Log.LogInfo("Matchmaking: all loaded mods are vanilla-compatible.");
            else
                Log.LogWarning($"Matchmaking: incompatible mods loaded ({string.Join(", ", incompatible)}), the game will use a separate matchmaking pool.");
        }
    }
}
