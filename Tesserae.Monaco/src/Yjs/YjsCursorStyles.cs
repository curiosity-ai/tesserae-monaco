using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The stylesheet the remote carets and selections of <see cref="MonacoYjsBinding"/> are drawn with.
    /// </summary>
    /// <remarks>
    /// <para>Monaco decorations take class names, never styles, so a collaborator's colour and name have to reach the
    /// page as CSS. There is one <c>&lt;style&gt;</c> element with the shared rules, added the first time a remote
    /// cursor is drawn, and one per collaborator with that person's three rules (selection background, caret colour,
    /// name label), shared by every binding on the page and removed when the last binding stops drawing them.</para>
    /// <para>The colour and the name come from a peer's awareness state, which is untrusted input: a colour is only
    /// used when it is a hex colour or a plain colour function (anything else falls back to
    /// <see cref="YjsUserColors"/>), and the name is written as an escaped CSS string, so neither can add a rule or a
    /// property of its own.</para>
    /// </remarks>
    internal static class YjsCursorStyles
    {
        internal const string SelectionClass = "tssm-yjs-selection";
        internal const string CaretClass     = "tssm-yjs-caret";
        internal const string RecentClass    = "tssm-yjs-caret-recent";
        internal const string TopLineClass   = "tssm-yjs-caret-top";

        private const string BaseRules =
            "." + CaretClass + "{position:absolute;height:100%;margin-left:-1px;border-left:2px solid;box-sizing:border-box;z-index:5}" +
            "." + CaretClass + "::before{content:'';position:absolute;left:-5px;width:10px;top:0;height:100%}" +
            "." + CaretClass + "::after{position:absolute;left:-2px;bottom:100%;padding:0 5px;border-radius:3px 3px 3px 0;color:#fff;" +
                "font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:11px;font-weight:600;line-height:16px;" +
                "white-space:nowrap;pointer-events:none;opacity:0;transition:opacity .15s ease-out}" +
            "." + CaretClass + ":hover::after,." + RecentClass + "::after{opacity:1}" +
            "." + TopLineClass + "::after{bottom:auto;top:100%;border-radius:0 3px 3px 3px}";

        private static readonly Regex HexColor      = new Regex("^#[0-9a-fA-F]{3,8}$");
        private static readonly Regex FunctionColor = new Regex("^(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch)\\([0-9a-zA-Z.,%\\s/+-]*\\)$");

        private static readonly Dictionary<int, Entry> _clients = new Dictionary<int, Entry>();

        private static HTMLStyleElement _base;

        private sealed class Entry
        {
            public HTMLStyleElement Element;
            public string           Signature;
            public int              References;
        }

        /// <summary>The class of a collaborator's selection.</summary>
        internal static string SelectionClassFor(int clientId) => SelectionClass + " " + SelectionClass + "-" + clientId;

        /// <summary>The class of a collaborator's caret.</summary>
        internal static string CaretClassFor(int clientId) => CaretClass + " " + CaretClass + "-" + clientId;

        /// <summary>Takes a reference on the rules of <paramref name="clientId"/>, creating them on first use.</summary>
        internal static void Acquire(int clientId)
        {
            EnsureBase();

            if (_clients.TryGetValue(clientId, out var entry))
            {
                entry.References++;
                return;
            }

            var element = (HTMLStyleElement)document.createElement("style");
            element.setAttribute("data-tssm-yjs-client", clientId.ToString());
            document.head.appendChild(element);

            _clients[clientId] = new Entry { Element = element, References = 1 };
        }

        /// <summary>Writes the colour and name of <paramref name="clientId"/>, if they changed. The client must be acquired.</summary>
        internal static void Update(int clientId, string name, string color, string colorLight)
        {
            if (!_clients.TryGetValue(clientId, out var entry)) return;

            var label = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name;
            var solid = IsSafeColor(color) ? color : YjsUserColors.ColorFor(label);
            var light = IsSafeColor(colorLight) ? colorLight : (HexColor.IsMatch(solid) && solid.Length == 7 ? solid + "33" : "color-mix(in srgb, " + solid + " 20%, transparent)");

            var signature = label + "\u0000" + solid + "\u0000" + light;

            if (entry.Signature == signature) return;

            entry.Signature = signature;
            entry.Element.textContent =
                "." + SelectionClass + "-" + clientId + "{background-color:" + light + "}" +
                "." + CaretClass + "-" + clientId + "{border-left-color:" + solid + "}" +
                "." + CaretClass + "-" + clientId + "::after{content:" + CssString(label) + ";background-color:" + solid + "}";
        }

        /// <summary>Drops a reference; the rules go with the last one.</summary>
        internal static void Release(int clientId)
        {
            if (!_clients.TryGetValue(clientId, out var entry)) return;

            entry.References--;

            if (entry.References > 0) return;

            entry.Element.remove();
            _clients.Remove(clientId);
        }

        /// <summary>How many collaborators currently have rules on the page.</summary>
        internal static int ClientCount => _clients.Count;

        private static void EnsureBase()
        {
            if (_base != null && _base.isConnected) return;

            _base = (HTMLStyleElement)document.createElement("style");
            _base.setAttribute("data-tssm-yjs", "base");
            _base.textContent = BaseRules;
            document.head.appendChild(_base);
        }

        private static bool IsSafeColor(string color)
        {
            if (string.IsNullOrEmpty(color) || color.Length > 64) return false;

            return HexColor.IsMatch(color) || FunctionColor.IsMatch(color);
        }

        /// <summary>A double-quoted CSS string: quotes, backslashes and control characters are escaped.</summary>
        private static string CssString(string value)
        {
            var sb = new StringBuilder("\"");

            foreach (var c in value)
            {
                if (c == '"' || c == '\\' || c < 0x20 || c == 0x7f || c == 0x2028 || c == 0x2029)
                {
                    sb.Append('\\').Append(((int)c).ToString("x")).Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
