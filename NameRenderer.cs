using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace StatTracker
{
    // Player names can carry TextMeshPro tags. The game's name filter turns the custom "<g=ID>" tag into
    // "<gradient=Name>", which IMGUI can't render, so gradient names are rebuilt as rich text with one
    // colour per character and every other tag is stripped.
    internal static class NameRenderer
    {
        private static readonly Regex GradientTag = new Regex(@"<(?:gradient|g)=""?([^"">]+)""?>", RegexOptions.IgnoreCase);
        private static readonly Regex AnyTag = new Regex(@"</?(?:[A-Za-z][A-Za-z0-9_-]*|#[0-9A-Fa-f]{3,8})(?:[ =][^>]*)?>");
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        public static string StripTags(string name)
        {
            return string.IsNullOrEmpty(name) ? string.Empty : AnyTag.Replace(name, string.Empty).Trim();
        }

        // Returns null for names without a known gradient.
        public static string Colorize(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return null;
            if (Cache.TryGetValue(rawName, out var cached))
                return cached;

            var match = GradientTag.Match(rawName);
            if (!match.Success)
            {
                Cache[rawName] = null;
                return null;
            }

            var manager = PlayerNameGradientManager.Instance;
            if (manager == null || manager.gradients == null)
                return null; // not loaded yet, try again next frame

            var gradient = FindGradient(manager.gradients, match.Groups[1].Value.Trim());
            string text = StripTags(rawName);
            string result = gradient != null && text.Length > 0 ? Build(text, gradient) : null;
            Cache[rawName] = result;
            return result;
        }

        private static TMP_ColorGradient FindGradient(UnlockableColorGradient[] gradients, string id)
        {
            if (int.TryParse(id, out int index))
                return index >= 0 && index < gradients.Length ? gradients[index].gradient : null;
            foreach (var g in gradients)
                if (g.gradient != null && string.Equals(g.gradient.name, id, StringComparison.OrdinalIgnoreCase))
                    return g.gradient;
            return null; // also covers the filter's "null" / "locked" placeholders
        }

        private static string Build(string text, TMP_ColorGradient gradient)
        {
            // Split into text elements so emoji / surrogate pairs stay intact.
            var elements = new List<string>();
            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
                elements.Add(e.GetTextElement());

            var sb = new StringBuilder();
            for (int i = 0; i < elements.Count; i++)
            {
                float t = elements.Count == 1 ? 0.5f : i / (float)(elements.Count - 1);
                // Replace angle brackets so the name can't form IMGUI tags.
                string ch = elements[i].Replace('<', '‹').Replace('>', '›');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(Sample(gradient, t))).Append('>')
                  .Append(ch).Append("</color>");
            }
            return sb.ToString();
        }

        // TMP applies the gradient per glyph. IMGUI can only colour whole glyphs, so spread it across the name.
        private static Color Sample(TMP_ColorGradient g, float t)
        {
            switch (g.colorMode)
            {
                case ColorMode.Single:
                    return g.topLeft;
                case ColorMode.VerticalGradient:
                    return Color.Lerp(g.topLeft, g.bottomLeft, t);
                case ColorMode.HorizontalGradient:
                    return Color.Lerp(g.topLeft, g.topRight, t);
                default:
                    return Color.Lerp(Color.Lerp(g.topLeft, g.topRight, t), Color.Lerp(g.bottomLeft, g.bottomRight, t), 0.5f);
            }
        }
    }
}
