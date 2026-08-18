namespace Tesserae.Monaco.Sample
{
    /// <summary>The documents the sample pages open, kept in one place so several pages can share them.</summary>
    internal static class SampleCode
    {
        public const string CSharp = @"using System;

public class Greeter
{
    // TODO: make the greeting configurable
    public string Greet(string name)
    {
        return $""Hello, {name}!"";
    }
}";

        public const string CSharpChanged = @"using System;

public class Greeter
{
    private readonly string _greeting;

    public Greeter(string greeting = ""Hello"")
    {
        _greeting = greeting;
    }

    public string Greet(string name)
    {
        return $""{_greeting}, {name}!"";
    }
}";

        public const string Json = @"{
  ""name"": ""tesserae.monaco"",
  ""embedded"": true,
  ""languages"": [""csharp"", ""json"", ""typescript""]
}";

        /// <summary>A small type the decoration, widget and options pages annotate.</summary>
        public const string Order = @"public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }

    public bool IsValid()
    {
        return Total > 0;
    }
}";

        /// <summary>Two methods calling each other, so a symbol has somewhere to navigate to.</summary>
        public const string Navigable = @"int Twice(int value)
{
    return value * 2;
}

int Quadruple(int value)
{
    return Twice(Twice(value));
}

var result = Quadruple(3);";

        /// <summary>
        /// Sections of key/value pairs, with a URL and two colour literals - one document the hint,
        /// lens, folding, link and colour providers each have something to say about.
        /// </summary>
        public const string Annotated = @"# see https://microsoft.github.io/monaco-editor/ for the real thing
region palette
    accent = #4fc1ff
    warn   = #e2c08d
endregion

region layout
    padding = 12
    margin  = 8
endregion";

        public const string Messy = "public   class    Messy {   \n\n\n\n    public int X {get;set;}    \n}   \n";
    }
}
