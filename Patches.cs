using System;
using System.Reflection;
using FishNet.Object;
using HarmonyLib;

namespace StatTracker
{
    // Every hook is a void prefix/postfix that only reads. The original methods always run unchanged.
    internal static class Patches
    {
        private static int _errorsLogged;

        public static void Apply(Harmony harmony)
        {
            var ph = typeof(PlayerHealth);

            // Health SyncVar setter: runs on every machine (server logic, client prediction and
            // incoming network updates), which is what lets us see every player's deaths.
            Patch(harmony, AccessTools.Method(ph, "sync___set_value_health", new[] { typeof(float), typeof(bool) }),
                prefix: nameof(HealthSetPrefix), postfix: nameof(HealthSetPostfix));

            // The public RPC wrapper only runs on the machine that initiated the hit (the server
            // side invokes RpcLogic___ directly), so it only ever sees *our* damage.
            Patch(harmony, AccessTools.Method(ph, nameof(PlayerHealth.RemoveHealth), new[] { typeof(float) }),
                prefix: nameof(RemoveHealthPrefix));

            // Hitscan/melee weapons each declare their own GiveDamage/KillServer RPC wrappers.
            // Discover them instead of hard-coding weapon classes so new weapons are picked up.
            int giveDamage = 0, killServer = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(ph.Assembly))
            {
                if (!typeof(NetworkBehaviour).IsAssignableFrom(type))
                    continue;

                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract)
                        continue;
                    var ps = method.GetParameters();
                    if (method.Name == "GiveDamage" && ps.Length >= 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == ph)
                    {
                        if (Patch(harmony, method, prefix: nameof(GiveDamagePrefix))) giveDamage++;
                    }
                    else if (method.Name == "KillServer" && ps.Length == 1 && ps[0].ParameterType == ph)
                    {
                        if (Patch(harmony, method, prefix: nameof(KillServerPrefix))) killServer++;
                    }
                }
            }

            Plugin.Log.LogInfo($"Hooked {giveDamage} GiveDamage and {killServer} KillServer weapon methods.");

            // End-of-round screen RPC: runs once on every client (host included) with the winning team.
            // The generated name carries a hash suffix, so match on the prefix.
            MethodInfo endRound = null;
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(RoundManager)))
            {
                if (method.Name.StartsWith("RpcLogic___EndRoundObservers_") && method.GetParameters().Length == 1)
                {
                    endRound = method;
                    break;
                }
            }
            Patch(harmony, endRound, postfix: nameof(EndRoundPostfix));

            // Victory screen: this is where the game itself decides Victory/Defeat on each machine.
            Patch(harmony, AccessTools.Method(typeof(VictoryMenuUI), "Start"), postfix: nameof(VictoryScreenPostfix));
        }

        private static bool Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null)
            {
                Plugin.Log.LogWarning($"Could not find a method to hook for {prefix ?? postfix}; that stat may be missing after a game update.");
                return false;
            }
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to hook {target.DeclaringType?.Name}.{target.Name}: {e}");
                return false;
            }
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                if (_errorsLogged++ < 10)
                    Plugin.Log.LogError(e);
            }
        }

        private static void HealthSetPrefix(PlayerHealth __instance, out float __state)
        {
            __state = __instance.health;
        }

        private static void HealthSetPostfix(PlayerHealth __instance, float __state)
        {
            Guard(() => Plugin.Tracker.OnHealthChanged(__instance, __state));
        }

        private static void RemoveHealthPrefix(PlayerHealth __instance, float damage)
        {
            Guard(() => Plugin.Tracker.OnLocalHit(__instance, damage, lethal: false));
        }

        private static void GiveDamagePrefix(NetworkBehaviour __instance, object[] __args)
        {
            Guard(() =>
            {
                if (__instance.IsOwner)
                    Plugin.Tracker.OnLocalHit(__args[1] as PlayerHealth, (float)__args[0], lethal: false);
            });
        }

        private static void KillServerPrefix(NetworkBehaviour __instance, object[] __args)
        {
            Guard(() =>
            {
                if (__instance.IsOwner)
                    Plugin.Tracker.OnLocalHit(__args[0] as PlayerHealth, 0f, lethal: true);
            });
        }

        private static void EndRoundPostfix(object[] __args)
        {
            Guard(() => Plugin.Tracker.OnRoundEnded((int)__args[0]));
        }

        private static void VictoryScreenPostfix()
        {
            Guard(() => Plugin.Tracker.OnVictoryScreen());
        }
    }
}
