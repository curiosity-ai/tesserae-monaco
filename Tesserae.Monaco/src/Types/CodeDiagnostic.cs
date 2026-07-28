namespace Tesserae.Monaco
{
    /// <summary>
    /// A compiler/linter error to show in the editor, expressed in the coordinates a language
    /// backend usually reports: <b>zero-based</b> lines and characters. Converted to Monaco's
    /// one-based <see cref="CodeMarker"/> by
    /// <see cref="CodeEditor.SetDiagnostics(Tesserae.ReadOnlyArray{CodeDiagnostic})"/>.
    ///
    /// This exists so the package needs no dependency on any particular backend's error type -
    /// map whatever your server returns onto this, or build <see cref="CodeMarker"/>s directly if
    /// they are already one-based.
    /// </summary>
    public sealed class CodeDiagnostic
    {
        public int            StartLine      { get; set; }
        public int            StartCharacter { get; set; }
        public int            EndLine        { get; set; }
        public int            EndCharacter   { get; set; }
        public string         Message        { get; set; }
        public MarkerSeverity Severity       { get; set; } = MarkerSeverity.Error;

        public CodeDiagnostic() { }

        public CodeDiagnostic(
            int            startLine,
            int            startCharacter,
            int            endLine,
            int            endCharacter,
            string         message,
            MarkerSeverity severity = MarkerSeverity.Error)
        {
            StartLine      = startLine;
            StartCharacter = startCharacter;
            EndLine        = endLine;
            EndCharacter   = endCharacter;
            Message        = message;
            Severity       = severity;
        }

        /// <summary>The equivalent Monaco marker, shifted to Monaco's one-based coordinates.</summary>
        public CodeMarker ToMarker()
        {
            return new CodeMarker
            {
                startLineNumber = StartLine + 1,
                startColumn     = StartCharacter + 1,
                endLineNumber   = EndLine + 1,
                endColumn       = EndCharacter + 1,
                message         = Message,
                severity        = Severity
            };
        }
    }
}
