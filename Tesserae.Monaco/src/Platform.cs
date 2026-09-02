using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The one fact about the host platform the package needs: whether the command key is Cmd. Monaco's
    /// own keybindings already know (<c>KeyMod.CtrlCmd</c>); this is for the gestures answered in C#.
    /// </summary>
    internal static class Platform
    {
        private static bool? _isMac;

        /// <summary>Whether the browser runs on macOS, where Cmd rather than Ctrl is the command modifier.</summary>
        public static bool IsMac
        {
            get
            {
                if (!_isMac.HasValue)
                {
                    // navigator.platform is deprecated but still the reliable answer everywhere; the user agent is
                    // the fallback for a browser that has emptied it.
                    var platform = navigator.platform ?? "";
                    var agent    = navigator.userAgent ?? "";

                    _isMac = platform.StartsWith("Mac") || agent.Contains("Mac OS") || agent.Contains("Macintosh");
                }

                return _isMac.Value;
            }
        }
    }
}
