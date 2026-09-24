using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StatTracker
{
    internal readonly struct PlayerRef
    {
        public readonly string Key;
        public readonly string Name;
        public readonly string RawName;
        public readonly int LobbyId;

        public PlayerRef(ClientInstance client)
        {
            int lobbyId = client.PlayerId;
            ulong steamId = client.PlayerSteamID;
            LobbyId = lobbyId;
            // Steam ID keeps session stats stable across lobbies; lobby slot is the fallback.
            Key = steamId != 0 ? "steam:" + steamId : "lobby:" + lobbyId;
            RawName = client.PlayerName ?? string.Empty;
            string plain = NameRenderer.StripTags(RawName);
            Name = plain.Length > 0 ? plain : "Player " + lobbyId;
        }
    }

    internal sealed class PlayerStats
    {
        public string Key;
        public string Name;
        public string RawName;
        public int Kills;
        public int Deaths;
        public int RoundsWon;
        public int MatchesWon;
    }

    internal sealed class StatScope
    {
        public readonly Dictionary<string, PlayerStats> Players = new Dictionary<string, PlayerStats>();
        // HP of damage dealt by the local player.
        public float Damage;
        public int Number;
        public int RoundsPlayed;
        public int MatchesPlayed;

        public PlayerStats Get(PlayerRef player)
        {
            if (!Players.TryGetValue(player.Key, out var stats))
            {
                stats = new PlayerStats { Key = player.Key };
                Players[player.Key] = stats;
            }
            stats.Name = player.Name;
            stats.RawName = player.RawName;
            return stats;
        }

        public void Reset()
        {
            Players.Clear();
            Damage = 0f;
            RoundsPlayed = 0;
            MatchesPlayed = 0;
        }
    }

    internal sealed class Tracker
    {
        // The game's own HUD displays health as health / 4 * 100.
        private const float HpPerHealthUnit = 25f;
        // The killer SyncVar can arrive a moment after the health update, so attribution waits briefly.
        private const float KillerResolveDelay = 0.3f;

        private const string MainMenuScene = "MainMenu";
        private const string VictoryScene = "VictoryScene";

        private struct PendingDeath
        {
            public PlayerHealth Health;
            public PlayerRef Victim;
            public float Time;
        }

        public readonly StatScope Match = new StatScope();
        public readonly StatScope Session = new StatScope();
        public bool MatchInProgress { get; private set; }

        private readonly Dictionary<int, PlayerRef> _playersByRoot = new Dictionary<int, PlayerRef>();
        private readonly HashSet<int> _killingBlowLanded = new HashSet<int>();
        private readonly List<PendingDeath> _pending = new List<PendingDeath>();
        private bool _matchStartPending;
        private bool _matchResultRecorded;
        private int _matchCounter;
        private float _nextScan;

        private IEnumerable<StatScope> ActiveScopes
        {
            get
            {
                if (MatchInProgress)
                    yield return Match;
                yield return Session;
            }
        }

        public string LocalKey
        {
            get
            {
                var local = ClientInstance.Instance;
                return local != null ? new PlayerRef(local).Key : null;
            }
        }

        public void OnHealthChanged(PlayerHealth health, float before)
        {
            if (health == null)
                return;
            Register(health);

            float now = health.health;
            if (now > 0f)
            {
                // Respawned / pooled object back in play.
                if (before <= 0f)
                    _killingBlowLanded.Remove(health.GetInstanceID());
                return;
            }
            if (before <= 0f || InMenu())
                return;
            if (!TryGetPlayer(health, out var victim))
                return;

            _pending.Add(new PendingDeath { Health = health, Victim = victim, Time = Time.unscaledTime });
        }

        // A hit initiated on this machine, i.e. by the local player.
        public void OnLocalHit(PlayerHealth victim, float damage, bool lethal)
        {
            if (victim == null || victim.IsOwner || InMenu())
                return; // own explosives, fall/void zones etc.

            int id = victim.GetInstanceID();
            float remaining = victim.health;
            if (remaining <= 0f || _killingBlowLanded.Contains(id) || IsLocalTeammate(victim))
                return;

            // Clamp to remaining health so overkill (e.g. explosions dealing 10) is not inflated.
            float dealt = lethal ? remaining : Mathf.Min(damage, remaining);
            if (lethal || damage >= remaining)
                _killingBlowLanded.Add(id);
            if (dealt <= 0f)
                return;

            foreach (var scope in ActiveScopes)
                scope.Damage += dealt * HpPerHealthUnit;
        }

        public void OnRoundEnded(int winningTeamId)
        {
            if (!MatchInProgress)
                return;
            FlushPending();
            foreach (var scope in ActiveScopes)
                scope.RoundsPlayed++;
            foreach (var winner in PlayersOnTeam(winningTeamId))
                foreach (var scope in ActiveScopes)
                    scope.Get(winner).RoundsWon++;
        }

        public void OnVictoryScreen()
        {
            if (_matchResultRecorded || _matchCounter == 0)
                return;
            _matchResultRecorded = true;

            // Same ordering VictoryMenuUI.Start uses to pick the "Victory" team.
            var score = ScoreManager.Instance;
            if (score == null)
                return;
            var teams = new List<int>(score.TeamIdToPlayerIds.Keys);
            if (teams.Count == 0)
                return;
            teams.Sort((a, b) => score.GetPoints(b).CompareTo(score.GetPoints(a)));

            Session.MatchesPlayed++;
            foreach (var winner in PlayersOnTeam(teams[0]))
            {
                Match.Get(winner).MatchesWon++;
                Session.Get(winner).MatchesWon++;
            }
            WriteHistory(finished: true);
        }

        public void OnSceneChanged(string scene)
        {
            FlushPending();
            _killingBlowLanded.Clear();
            _playersByRoot.Clear();

            if (scene == MainMenuScene || scene == VictoryScene)
            {
                _matchStartPending = false;
                if (MatchInProgress)
                {
                    MatchInProgress = false;
                    if (scene == MainMenuScene)
                        WriteHistory(finished: false); // left before the victory screen
                }
                return;
            }

            // Start the match once players actually spawn, so transition scenes are ignored
            // and the last match stays visible while the first map loads.
            if (!MatchInProgress)
                _matchStartPending = true;
        }

        public void Update()
        {
            float t = Time.unscaledTime;
            if (t >= _nextScan)
            {
                _nextScan = t + 1f;
                foreach (var health in Object.FindObjectsOfType<PlayerHealth>())
                    Register(health);
                EnsureLobbyPlayersListed();
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                if (t - _pending[i].Time < KillerResolveDelay)
                    continue;
                Resolve(_pending[i]);
                _pending.RemoveAt(i--);
            }
        }

        private void Register(PlayerHealth health)
        {
            if (!TryGetPlayer(health, out var player))
                return;
            _playersByRoot[health.transform.GetInstanceID()] = player;

            if (_matchStartPending && !InMenu())
            {
                _matchStartPending = false;
                StartMatch();
            }
        }

        private void FlushPending()
        {
            foreach (var death in _pending)
                Resolve(death);
            _pending.Clear();
        }

        private void Resolve(PendingDeath death)
        {
            // Reading the managed field is safe even if the Unity object was despawned meanwhile.
            var killer = ResolveKiller(death.Health.killer);
            var victim = death.Victim;
            bool kill = killer.HasValue && killer.Value.Key != victim.Key && !SameTeam(killer.Value.LobbyId, victim.LobbyId);

            foreach (var scope in ActiveScopes)
            {
                scope.Get(victim).Deaths++;
                if (kill)
                    scope.Get(killer.Value).Kills++;
            }
        }

        private PlayerRef? ResolveKiller(Transform killer)
        {
            if (ReferenceEquals(killer, null))
                return null;
            if (_playersByRoot.TryGetValue(killer.GetInstanceID(), out var cached))
                return cached;
            if (killer != null)
            {
                var health = killer.GetComponentInParent<PlayerHealth>();
                if (health != null && TryGetPlayer(health, out var player))
                    return player;
            }
            return null;
        }

        private static bool TryGetPlayer(PlayerHealth health, out PlayerRef player)
        {
            var values = health.playerValues;
            var client = values != null ? values.playerClient : null;
            if (client == null)
            {
                // playerClient may not be synced yet right after spawn; fall back to network ownership.
                int ownerId = health.OwnerId;
                client = ClientInstance.playerInstances.Values.FirstOrDefault(c => c != null && c.OwnerId == ownerId);
            }
            if (client == null)
            {
                player = default;
                return false;
            }
            player = new PlayerRef(client);
            return true;
        }

        private static IEnumerable<PlayerRef> PlayersOnTeam(int teamId)
        {
            return ClientInstance.playerInstances.Values
                .Where(c => c != null && TeamOf(c.PlayerId) == teamId)
                .Select(c => new PlayerRef(c))
                .ToList();
        }

        private void EnsureLobbyPlayersListed()
        {
            if (InMenu())
                return;
            foreach (var client in ClientInstance.playerInstances.Values)
            {
                if (client == null)
                    continue;
                var player = new PlayerRef(client);
                foreach (var scope in ActiveScopes)
                    scope.Get(player);
            }
        }

        private bool IsLocalTeammate(PlayerHealth victim)
        {
            var local = ClientInstance.Instance;
            return local != null && TryGetPlayer(victim, out var player) && SameTeam(local.PlayerId, player.LobbyId);
        }

        // ScoreManager.GetTeamId assigns a team when a player has none, so read the dictionary
        // directly and fall back to the same default (team id = player id).
        private static int TeamOf(int playerId)
        {
            var score = ScoreManager.Instance;
            return score != null && score.PlayerIdToTeamId.TryGetValue(playerId, out var team) ? team : playerId;
        }

        private static bool SameTeam(int lobbyIdA, int lobbyIdB)
        {
            var game = GameManager.Instance;
            return game != null && game.playingTeams && TeamOf(lobbyIdA) == TeamOf(lobbyIdB);
        }

        private static bool InMenu()
        {
            var pause = PauseManager.Instance;
            if (pause != null && (pause.inMainMenu || pause.inVictoryMenu))
                return true;
            var lobby = SteamLobby.Instance;
            return lobby != null && lobby.isInExplorationMap;
        }

        private void StartMatch()
        {
            MatchInProgress = true;
            _matchResultRecorded = false;
            Match.Reset();
            Match.Number = ++_matchCounter;
        }

        private void WriteHistory(bool finished)
        {
            if (!Plugin.WriteHistory.Value || Match.Players.Count == 0)
                return;
            try
            {
                string localKey = LocalKey;
                var parts = Match.Players.Values
                    .OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths)
                    .Select(p => $"{p.Name}{(p.Key == localKey ? " (you)" : "")}{(p.MatchesWon > 0 ? " [WIN]" : "")} {p.Kills}K/{p.Deaths}D/{p.RoundsWon}RW");
                string status = finished ? "" : " (left early)";
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm}  Match {Match.Number}{status}, {Match.RoundsPlayed} round(s)  |  your damage {Match.Damage:0}  |  {string.Join(", ", parts)}";
                string path = Path.Combine(Paths.BepInExRootPath, "StatTracker_history.txt");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write match history: {e.Message}");
            }
        }
    }
}
