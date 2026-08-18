using System;
using System.Linq;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// The bits every sample page shares: its title row, its "See also" footer, and the small text
    /// helpers that keep the pages looking alike.
    /// </summary>
    public static class SamplesHelper
    {
        private const string REPOSITORY = "https://github.com/curiosity-ai/tesserae-monaco";

        public static SectionStack SampleTitle(this SectionStack stack, Type sampleType, UIcons icon, string subtitle)
        {
            return stack.Title(icon, SamplePageName(sampleType), subtitle,
                Button("Documentation").SetIcon(UIcons.Books).Tooltip("Tesserae documentation").OnClick(() => window.open("https://docs.curiosity.ai/tesserae/", "_blank")),
                Button("View Code").SetIcon(UIcons.SquareTerminal).Tooltip("This page's source on GitHub").OnClick(() => window.open($"{REPOSITORY}/blob/main/Tesserae.Monaco.Sample/src/Samples/{sampleType.Name}.cs", "_blank")));
        }

        /// <summary>
        /// Closes a page with one button per related sample, each navigating to that page - the same
        /// route the sidebar uses.
        /// </summary>
        public static SectionStack SeeAlso(this SectionStack stack, params Type[] relatedSamples)
        {
            var links = HStack().WS().Wrap().Gap(8.px()).PT(8);

            foreach (var sampleType in relatedSamples)
            {
                links.Add(Button(SamplePageName(sampleType)).SetIcon(IconFor(sampleType)).OnClick(() => Router.Navigate(RouteFor(sampleType))));
            }

            return stack.FlatSection(VStack().WS().Children(
                Card(VStack().WS().Children(
                    TextBlock("Pages that usually come up together with this one - the components it composes with, or the alternatives to it."),
                    links)).SetTitle("See also")));
        }

        public static IComponent SampleSubTitle(string text) => TextBlock(text).SemiBold().PT(16).PB(8);

        /// <summary>
        /// The model for a named document, created on the first visit and reused afterwards with its
        /// text reset. Every page here is rebuilt each time it is opened, and Monaco throws when a URI
        /// is claimed a second time - so a page that names its models cannot simply create them.
        /// </summary>
        public static CodeModel EnsureModel(string uri, string text, string language)
        {
            var existing = MonacoEditor.GetModel(uri);

            if (existing is object)
            {
                existing.Text = text;

                return existing;
            }

            return MonacoEditor.CreateModel(text, language, uri);
        }

        /// <summary>A muted line under a demo, for the "now try this" instructions each page ends on.</summary>
        public static IComponent SampleHint(string text) => TextBlock(text).Small().Secondary().PT(4);

        private static string SamplePageName(Type sampleType) => SamplePage.FormatName(sampleType);

        // Mirrors the routes App.cs registers for every sample.
        private static string RouteFor(Type sampleType) => $"#/view/{SamplePage.FormatName(sampleType)}";

        // The icon the sample declares on its [SampleDetails], so a link looks like its sidebar entry.
        private static UIcons IconFor(Type sampleType)
        {
            var details = sampleType.GetCustomAttributes(typeof(SampleDetailsAttribute), true).FirstOrDefault() as SampleDetailsAttribute;

            return details is object ? details.Icon : UIcons.Circle;
        }
    }
}
