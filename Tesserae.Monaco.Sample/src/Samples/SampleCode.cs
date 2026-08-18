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

        public const string Messy = "public   class    Messy {   \n\n\n\n    public int X {get;set;}    \n}   \n";
    }
}
