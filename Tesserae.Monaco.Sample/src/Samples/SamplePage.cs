using System;
using System.Linq;
using System.Threading.Tasks;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// One entry in the sidebar and the page it opens. Built from the reflected sample type, so the
    /// class name is what names the page and the route.
    /// </summary>
    internal class SamplePage
    {
        public string                 Type             { get; }
        public string                 Name             { get; }
        public string                 Group            { get; }
        public int                    Order            { get; }
        public UIcons                 Icon             { get; }
        public Func<Task<IComponent>> ContentGenerator { get; }

        public SamplePage(string type, string name, string group, int order, UIcons icon, Func<Task<IComponent>> contentGenerator)
        {
            Type             = type;
            Name             = name;
            Group            = group;
            Order            = order;
            Icon             = icon;
            ContentGenerator = contentGenerator;
        }

        /// <summary>"CompletionAndHoverSample" reads as "Completion and Hover".</summary>
        public static string FormatName(Type sampleType) => FormatName(sampleType.Name);

        public static string FormatName(string sampleType)
        {
            // A capital starts a word unless it continues an acronym: "AIVariant" is "AI Variant", not "A I Variant".
            var name = sampleType.Replace("Sample", "");
            var text = new System.Text.StringBuilder();

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (i > 0 && char.IsUpper(c))
                {
                    var afterLower    = !char.IsUpper(name[i - 1]);
                    var beforeLower   = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (afterLower || beforeLower) text.Append(' ');
                }

                text.Append(c);
            }

            return text.ToString().Trim().Replace(" And ", " and ");
        }
    }
}
