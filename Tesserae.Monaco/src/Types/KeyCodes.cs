using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Monaco's own virtual key codes, matching <c>monaco.KeyCode</c> - <b>not</b> the browser's
    /// <c>KeyboardEvent.keyCode</c>, and not ASCII. Combine with <see cref="KeyMod"/> to build the
    /// keybindings an editor action is bound to.
    ///
    /// Generated from monaco-editor 0.56.0's <c>monaco.d.ts</c>; re-generate if the pin moves.
    /// </summary>
    [Enum(Emit.Value)]
    public enum KeyCode
    {
        DependsOnKbLayout  = -1,
        Unknown            = 0,
        Backspace          = 1,
        Tab                = 2,
        Enter              = 3,
        Shift              = 4,
        Ctrl               = 5,
        Alt                = 6,
        PauseBreak         = 7,
        CapsLock           = 8,
        Escape             = 9,
        Space              = 10,
        PageUp             = 11,
        PageDown           = 12,
        End                = 13,
        Home               = 14,
        LeftArrow          = 15,
        UpArrow            = 16,
        RightArrow         = 17,
        DownArrow          = 18,
        Insert             = 19,
        Delete             = 20,
        Digit0             = 21,
        Digit1             = 22,
        Digit2             = 23,
        Digit3             = 24,
        Digit4             = 25,
        Digit5             = 26,
        Digit6             = 27,
        Digit7             = 28,
        Digit8             = 29,
        Digit9             = 30,
        KeyA               = 31,
        KeyB               = 32,
        KeyC               = 33,
        KeyD               = 34,
        KeyE               = 35,
        KeyF               = 36,
        KeyG               = 37,
        KeyH               = 38,
        KeyI               = 39,
        KeyJ               = 40,
        KeyK               = 41,
        KeyL               = 42,
        KeyM               = 43,
        KeyN               = 44,
        KeyO               = 45,
        KeyP               = 46,
        KeyQ               = 47,
        KeyR               = 48,
        KeyS               = 49,
        KeyT               = 50,
        KeyU               = 51,
        KeyV               = 52,
        KeyW               = 53,
        KeyX               = 54,
        KeyY               = 55,
        KeyZ               = 56,
        Meta               = 57,
        ContextMenu        = 58,
        F1                 = 59,
        F2                 = 60,
        F3                 = 61,
        F4                 = 62,
        F5                 = 63,
        F6                 = 64,
        F7                 = 65,
        F8                 = 66,
        F9                 = 67,
        F10                = 68,
        F11                = 69,
        F12                = 70,
        F13                = 71,
        F14                = 72,
        F15                = 73,
        F16                = 74,
        F17                = 75,
        F18                = 76,
        F19                = 77,
        F20                = 78,
        F21                = 79,
        F22                = 80,
        F23                = 81,
        F24                = 82,
        NumLock            = 83,
        ScrollLock         = 84,
        Semicolon          = 85,
        Equal              = 86,
        Comma              = 87,
        Minus              = 88,
        Period             = 89,
        Slash              = 90,
        Backquote          = 91,
        BracketLeft        = 92,
        Backslash          = 93,
        BracketRight       = 94,
        Quote              = 95,
        OEM_8              = 96,
        IntlBackslash      = 97,
        KEY_IN_COMPOSITION = 114,
        AudioVolumeMute    = 117,
        AudioVolumeUp      = 118,
        AudioVolumeDown    = 119,
        BrowserSearch      = 120,
        BrowserHome        = 121,
        BrowserBack        = 122,
        BrowserForward     = 123,
        MediaTrackNext     = 124,
        MediaTrackPrevious = 125,
        MediaStop          = 126,
        MediaPlayPause     = 127,
        LaunchMediaPlayer  = 128,
        LaunchMail         = 129,
        LaunchApp2         = 130,
        Clear              = 131,
        MAX_VALUE          = 132
    }

    /// <summary>
    /// The modifier bits a keybinding is OR-ed together from, matching <c>monaco.KeyMod</c>.
    ///
    /// Plain constants rather than reads of <c>monaco.KeyMod</c>: a keybinding is built where the
    /// action is <i>declared</i>, which is while components are being constructed - long before Monaco
    /// has loaded. Reading them from Monaco there throws, and takes the whole app's Main with it.
    ///
    /// Mirrors <see cref="MonacoApi.KeyMod"/>, which reads the same values off the live Monaco object -
    /// use that one where Monaco is certainly loaded. Both the values here and <see cref="Chord"/>'s
    /// packing are verified against the pinned monaco-editor build.
    /// </summary>
    public static class KeyMod
    {
        /// <summary>Ctrl on Windows and Linux, Cmd on macOS - what a portable binding should use.</summary>
        public const int CtrlCmd = 2048;

        public const int Shift = 1024;

        public const int Alt = 512;

        /// <summary>Ctrl on macOS, Win on Windows - the modifier <see cref="CtrlCmd"/> does not map to.</summary>
        public const int WinCtrl = 256;

        /// <summary>
        /// A two-stroke binding, e.g. Ctrl+K Ctrl+F: the second chord is packed into the high 16 bits,
        /// which is exactly what <c>monaco.KeyMod.chord</c> does.
        /// </summary>
        public static int Chord(int firstPart, int secondPart)
        {
            return firstPart | ((secondPart & 0x0000FFFF) << 16);
        }

        /// <summary>A modifier combined with a key, e.g. <c>KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyS)</c>.</summary>
        public static int With(int modifiers, KeyCode key)
        {
            return modifiers | (int)key;
        }
    }
}
