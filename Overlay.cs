using System.Linq;
using UnityEngine;

namespace StatTracker
{
    internal sealed class Overlay : MonoBehaviour
    {
        private static readonly Color LocalColor = new Color(1f, 0.85f, 0.35f);
        private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.6f);

        private bool _visible;
        private bool _showSession;
        private GUIStyle _label, _rich, _right, _header;
        private Texture2D _background;
        private int _styleFontSize;

        private void Awake()
        {
            _visible = Plugin.VisibleOnStart.Value;
        }

        private void Update()
        {
            Plugin.Tracker.Update();
            Plugin.LogCompatibilityOnce();

            if (Plugin.ToggleKey.Value.IsDown())
                _visible = !_visible;
            if (Plugin.ScopeKey.Value.IsDown())
                _showSession = !_showSession;
        }

        private void EnsureStyles()
        {
            // FontSize is specified for 1080p and scaled with the vertical resolution.
            int size = Mathf.Clamp(Mathf.RoundToInt(Plugin.FontSize.Value * Screen.height / 1080f), 8, 64);
            if (_label != null && _styleFontSize == size)
                return;
            _styleFontSize = size;

            _label = new GUIStyle(GUI.skin.label) { fontSize = size, richText = false, clipping = TextClipping.Clip, wordWrap = false };
            _label.normal.textColor = Color.white;
            _rich = new GUIStyle(_label) { richText = true };
            _right = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
            _header = new GUIStyle(_label) { fontStyle = FontStyle.Bold };

            if (_background == null)
            {
                _background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
                _background.Apply();
            }
        }

        private void OnGUI()
        {
            if (!_visible)
                return;
            EnsureStyles();

            var tracker = Plugin.Tracker;
            var scope = _showSession ? tracker.Session : tracker.Match;
            string localKey = tracker.LocalKey;
            var rows = scope.Players.Values
                .OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths).ThenBy(p => p.Name)
                .ToList();

            string[] columns = _showSession
                ? new[] { "K", "D", "K/D", "RW", "MW" }
                : new[] { "K", "D", "K/D", "RW" };

            float line = _styleFontSize * 1.45f;
            float pad = _styleFontSize * 0.6f;
            float colW = _styleFontSize * 2.9f;
            float nameW = _styleFontSize * 11f;
            float width = pad * 2 + nameW + colW * columns.Length;
            float height = pad * 2 + line * (3 + Mathf.Max(rows.Count, 1)) + line * 0.3f;

            float x = Plugin.PositionX.Value >= 0 ? Plugin.PositionX.Value : Screen.width + Plugin.PositionX.Value - width;
            float y = Plugin.PositionY.Value;
            GUI.DrawTexture(new Rect(x, y, width, height), _background);

            float cx = x + pad, cy = y + pad, inner = width - pad * 2;

            GUI.Label(new Rect(cx, cy, inner, line), Title(tracker, scope), _header);
            GUI.color = DimColor;
            GUI.Label(new Rect(cx, cy, inner, line), $"[{Plugin.ScopeKey.Value}] {(_showSession ? "match" : "session")}", _right);
            cy += line;

            GUI.Label(new Rect(cx, cy, nameW, line), "Player", _label);
            for (int i = 0; i < columns.Length; i++)
                GUI.Label(new Rect(cx + nameW + colW * i, cy, colW, line), columns[i], _right);
            GUI.color = Color.white;
            cy += line;

            if (rows.Count == 0)
            {
                GUI.color = DimColor;
                GUI.Label(new Rect(cx, cy, inner, line), "No data yet", _label);
                GUI.color = Color.white;
                cy += line;
            }
            foreach (var p in rows)
            {
                bool local = p.Key == localKey;
                DrawName(new Rect(cx, cy, nameW, line), p, local);

                GUI.color = local ? LocalColor : Color.white;
                float kd = p.Deaths == 0 ? p.Kills : (float)p.Kills / p.Deaths;
                string[] values = { p.Kills.ToString(), p.Deaths.ToString(), kd.ToString("0.00"), p.RoundsWon.ToString(), p.MatchesWon.ToString() };
                for (int i = 0; i < columns.Length; i++)
                    GUI.Label(new Rect(cx + nameW + colW * i, cy, colW, line), values[i], _right);
                GUI.color = Color.white;
                cy += line;
            }

            cy += line * 0.3f;
            GUI.color = LocalColor;
            GUI.Label(new Rect(cx, cy, inner, line), $"Your damage: {scope.Damage:0}", _label);
            GUI.color = Color.white;
        }

        private void DrawName(Rect rect, PlayerStats p, bool local)
        {
            string gradient = NameRenderer.Colorize(p.RawName);
            if (gradient != null)
            {
                GUI.color = Color.white;
                GUI.Label(rect, local ? "<b>" + gradient + "</b>" : gradient, _rich);
            }
            else
            {
                GUI.color = local ? LocalColor : Color.white;
                GUI.Label(rect, p.Name, _label);
            }
        }

        private static string Title(Tracker tracker, StatScope scope)
        {
            if (scope == tracker.Session)
                return scope.MatchesPlayed == 1 ? "SESSION · 1 MATCH" : $"SESSION · {scope.MatchesPlayed} MATCHES";
            if (scope.Number == 0)
                return "MATCH";
            return tracker.MatchInProgress
                ? $"MATCH {scope.Number} · ROUND {scope.RoundsPlayed + 1}"
                : $"MATCH {scope.Number} (ENDED)";
        }
    }
}
