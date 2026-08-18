using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    public static partial class MonacoEditor
    {
        /// <summary>The theme name applied to editors while the Tesserae theme is light.</summary>
        public const string LIGHT_THEME = "tss-light";

        /// <summary>The theme name applied to editors while the Tesserae theme is dark.</summary>
        public const string DARK_THEME = "tss-dark";

        private const string DEFAULT_ASSETS_PATH = "assets/js/monaco";

        private static string      _assetsPath = DEFAULT_ASSETS_PATH;
        private static Task        _loading;
        private static HTMLElement _overflowWidgetsHost;

        private static readonly HashSet<string>        _registeredLanguages = new HashSet<string>();
        private static readonly List<TokenColor>       _tokenColors         = new List<TokenColor>();
        private static readonly List<LanguageDefinition> _pendingLanguages  = new List<LanguageDefinition>();

        /// <summary>
        /// Where this package's Monaco bundle lives, relative to the page - the folder holding
        /// <c>monaco.js</c> and the <c>*.worker.js</c> files. Defaults to <c>assets/js/monaco</c>,
        /// which is where the build copies them, so you only need to set this if you serve Monaco
        /// from somewhere else (a CDN, a shared static host). Must be set before the first editor is
        /// built.
        ///
        /// The workers are located by the bundle itself, relative to its own script URL, so pointing
        /// this at another origin moves them too - no second setting to keep in sync.
        /// </summary>
        public static string AssetsPath
        {
            get => _assetsPath;
            set => _assetsPath = string.IsNullOrWhiteSpace(value) ? DEFAULT_ASSETS_PATH : value.TrimEnd('/');
        }

        /// <summary>
        /// Called with each markdown block Monaco renders inside a hover or completion-details
        /// popup, after the package's own post-processing. Use it to bind behaviour to links or
        /// otherwise decorate documentation rendered by a language backend.
        /// </summary>
        public static Action<HTMLElement> OnRenderedMarkdown { get; set; }

        /// <summary>
        /// True once Monaco has finished loading and <c>monaco.*</c> is safe to call.
        /// </summary>
        public static bool IsLoaded => JsWindow.monaco != null && JsWindow.monaco.editor != null;

        /// <summary>
        /// Loads Monaco, at most once per page. Every component awaits this before creating its
        /// editor, so callers rarely need it - reach for it when you want to call <c>monaco.*</c>
        /// directly, or to warm Monaco up before it is first shown.
        /// </summary>
        public static Task LoadAsync()
        {
            if (_loading is null)
            {
                _loading = LoadCoreAsync();
            }
            return _loading;
        }

        /// <summary>
        /// The absolute URL of the folder holding the Monaco bundle.
        ///
        /// Resolved by the browser against <c>document.baseURI</c> rather than assembled by hand:
        /// that gets the directory right whether the app is served as <c>/index.html</c> or from
        /// <c>/some/path/</c>, honours a <c>&lt;base href&gt;</c>, and passes an already-absolute
        /// <see cref="AssetsPath"/> (a CDN) straight through.
        /// </summary>
        private static string BaseUrl => new URL(_assetsPath, document.baseURI).href.TrimEnd('/');

        private static async Task LoadCoreAsync()
        {
            var baseUrl = BaseUrl;

            // One script, and everything else is already inside it: the bundle injects Monaco's
            // stylesheet and installs MonacoEnvironment itself, resolving its own worker URLs from
            // the script's own src. See build/bundle-monaco.mjs.
            await Require.LoadScriptAsync(baseUrl + "/monaco.js");

            if (!IsLoaded)
            {
                throw new Exception("Loaded " + baseUrl + "/monaco.js but window.monaco is not defined.");
            }

            // Languages requested before Monaco was ready were queued; apply them before the themes
            // so their token colours are part of the first theme definition.
            foreach (var language in _pendingLanguages)
            {
                ApplyLanguage(language);
            }

            _pendingLanguages.Clear();

            DefineThemes();
        }

        /// <summary>
        /// (Re)defines the light and dark editor themes from the current Tesserae theme colours.
        /// Called automatically once Monaco loads; call it again after switching the Tesserae theme
        /// at runtime, followed by <see cref="ApplyTheme"/> on the editors that should follow.
        /// </summary>
        public static void DefineThemes()
        {
            // Monaco wants a plain #rrggbb; the Tesserae token is a CSS var that resolves to rgb(...).
            var background = Color.FromString(Color.EvalVar(Theme.Secondary.Background)).ToHex();
            var rules      = BuildThemeRules();
            var colors     = new ThemeColors().Set("editor.background", background);

            MonacoApi.editor.defineTheme(LIGHT_THEME, new StandaloneThemeData
            {
                baseTheme = "vs",
                inherit   = true,
                rules     = rules,
                colors    = colors
            });

            MonacoApi.editor.defineTheme(DARK_THEME, new StandaloneThemeData
            {
                baseTheme = "vs-dark",
                inherit   = true,
                rules     = rules,
                colors    = colors
            });
        }

        /// <summary>The theme name matching the active Tesserae theme.</summary>
        public static string ActiveTheme => Theme.IsDark ? DARK_THEME : LIGHT_THEME;

        /// <summary>Switches every editor on the page to the theme matching the active Tesserae theme.</summary>
        public static void ApplyTheme()
        {
            if (!IsLoaded) return;

            MonacoApi.editor.setTheme(ActiveTheme);
        }

        private static ThemeRule[] BuildThemeRules()
        {
            var rules = new List<ThemeRule>();

            foreach (var color in _tokenColors)
            {
                if (color is null || string.IsNullOrWhiteSpace(color.Token)) continue;

                rules.Add(new ThemeRule
                {
                    token      = color.Token,
                    foreground = (color.Foreground ?? "").TrimStart('#'),
                    fontStyle  = color.FontStyle
                });
            }

            return rules.ToArray();
        }

        /// <summary>
        /// Registers a custom language with Monaco. Safe to call repeatedly - the second and later
        /// calls for a given <see cref="LanguageDefinition.Id"/> are ignored, so a component can
        /// register its language unconditionally.
        ///
        /// Safe to call before Monaco has loaded, which is the normal case: a component built in
        /// <c>Main</c> selects its language long before it is mounted. Definitions registered early
        /// are queued and applied as soon as Monaco is ready.
        /// </summary>
        public static void RegisterLanguage(LanguageDefinition language)
        {
            if (language is null || string.IsNullOrWhiteSpace(language.Id)) return;
            if (_registeredLanguages.Contains(language.Id)) return;

            _registeredLanguages.Add(language.Id);

            if (!IsLoaded)
            {
                _pendingLanguages.Add(language);
                return;
            }

            ApplyLanguage(language);

            // A language registered after the first editor exists needs the themes rebuilt, since
            // its token colours live on the theme rather than on the language.
            if (language.TokenColors is object && language.TokenColors.Length > 0)
            {
                DefineThemes();
                ApplyTheme();
            }
        }

        private static void ApplyLanguage(LanguageDefinition language)
        {
            MonacoApi.languages.register(new LanguageRegistration
            {
                id         = language.Id,
                aliases    = language.Aliases    ?? new string[0],
                extensions = language.Extensions ?? new string[0]
            });

            if (language.Tokenizer is object)
            {
                MonacoApi.languages.setMonarchTokensProvider(language.Id, language.Tokenizer);
            }

            if (language.Configuration is object)
            {
                MonacoApi.languages.setLanguageConfiguration(language.Id, language.Configuration);
            }

            // A provider that returns nothing, registered only so Monaco treats these characters as
            // completion triggers - without it the suggest widget never opens on punctuation, and
            // the per-editor completion handler is never reached.
            if (language.CompletionTriggerCharacters is object && language.CompletionTriggerCharacters.Length > 0)
            {
                MonacoApi.languages.registerCompletionItemProvider(language.Id, new CompletionItemProvider
                {
                    triggerCharacters      = language.CompletionTriggerCharacters,
                    provideCompletionItems = (model, position) => new CompletionList { suggestions = new CompletionItem[0] }
                });
            }

            if (language.TokenColors is object && language.TokenColors.Length > 0)
            {
                _tokenColors.AddRange(language.TokenColors);
            }
        }

        /// <summary>Every language id Monaco currently knows about.</summary>
        public static string[] GetLanguageIds()
        {
            if (!IsLoaded) return new string[0];

            var languages = MonacoApi.languages.getLanguages();
            var ids       = new string[languages.Length];

            for (var i = 0; i < languages.Length; i++)
            {
                ids[i] = languages[i].id;
            }

            return ids;
        }

        /// <summary>
        /// Resolves a file extension (with or without the leading dot) to the language id Monaco
        /// associates with it - e.g. <c>"cs"</c> to <c>"csharp"</c>.
        /// </summary>
        public static bool TryGetLanguageIdForExtension(string extension, out string languageId)
        {
            languageId = null;

            if (!IsLoaded || string.IsNullOrWhiteSpace(extension)) return false;

            if (!extension.StartsWith(".")) extension = "." + extension;

            var languages = MonacoApi.languages.getLanguages();

            foreach (var language in languages)
            {
                if (language.extensions is null) continue;

                foreach (var candidate in language.extensions)
                {
                    if (candidate == extension)
                    {
                        languageId = language.id;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adapts a <see cref="Task"/> into the native JavaScript <c>Promise</c> that a Monaco
        /// language provider expects. <c>Transpose.toPromise</c> is the runtime's own adapter - the
        /// same one the compiler emits for <c>await</c> - so a faulted task rejects the promise
        /// instead of the exception being swallowed, and the handler always receives exactly the
        /// task's result.
        ///
        /// <c>Task&lt;T&gt;</c> derives from <see cref="Task"/>, so this one overload covers both;
        /// the runtime reads the result off the completed task.
        /// </summary>
        public static IPromise AsPromise(Task task) => PromiseExtensions.ToPromise(task);

        /// <summary>
        /// A single, body-mounted element that Monaco renders its suggest/hover popups into when an
        /// editor sets <c>fixedOverflowWidgets</c>. Without it those popups are clipped by any
        /// ancestor with <c>overflow: hidden</c> - a modal, a panel, a split view.
        /// </summary>
        internal static HTMLElement GetOverflowWidgetsHost()
        {
            if (_overflowWidgetsHost is object && _overflowWidgetsHost.IsMounted())
            {
                return _overflowWidgetsHost;
            }

            var existing = document.querySelector("body > div[data-monaco-overflow-host=\"1\"]").As<HTMLElement>();

            if (existing is object)
            {
                _overflowWidgetsHost = existing;
                return _overflowWidgetsHost;
            }

            var host = DIV();
            host.setAttribute("data-monaco-overflow-host", "1");
            host.className      = "monaco-editor"; // required: Monaco scopes its CSS to this class
            host.style.position = "absolute";
            host.style.top      = "0";
            host.style.left     = "0";
            host.style.width    = "0";
            host.style.height   = "0";
            host.style.zIndex   = "100000"; // above modal overlays so the suggest popup isn't clipped
            document.body.appendChild(host);

            var hostObserver = new MutationObserver((records, _) =>
            {
                foreach (var record in records)
                {
                    foreach (var mountedNode in record.addedNodes)
                    {
                        var el = mountedNode.As<HTMLElement>();

                        if (el is null || !Script.InstanceOf(el, typeof(HTMLElement))) continue;

                        FixRenderedMarkdown(el);
                    }
                }
            });

            hostObserver.observe(host, new MutationObserverInit
            {
                childList = true,
                subtree   = true,
            });

            _overflowWidgetsHost = host;
            return _overflowWidgetsHost;
        }

        /// <summary>
        /// Monaco renders hover/completion documentation as markdown and escapes any HTML in it.
        /// A language backend that genuinely needs to emit HTML (rendered type signatures, for
        /// instance) prefixes its content with <c>!!HTML</c>; this unwraps that back into real
        /// markup, then hands the element to <see cref="OnRenderedMarkdown"/>.
        /// </summary>
        internal static void FixRenderedMarkdown(HTMLElement root)
        {
            if (root is null) return;

            TryFix(root);

            var renderedMarkdowns = root.querySelectorAll(".rendered-markdown");

            for (var i = 0; i < renderedMarkdowns.length; i++)
            {
                TryFix(renderedMarkdowns[i].As<HTMLElement>());
            }

            void TryFix(HTMLElement renderedMarkdown)
            {
                if (renderedMarkdown is null || renderedMarkdown.classList is null || !renderedMarkdown.classList.contains("rendered-markdown"))
                {
                    return;
                }

                var textContent = renderedMarkdown.textContent ?? "";

                if (textContent.StartsWith(HTML_MARKER))
                {
                    renderedMarkdown.innerHTML = textContent.Substring(HTML_MARKER.Length);
                    ResizeWidgetAfterInjectedHtml(renderedMarkdown);
                }

                OnRenderedMarkdown?.Invoke(renderedMarkdown);
            }
        }

        /// <summary>
        /// The prefix a hover/completion documentation string uses to opt into raw HTML rendering.
        /// Pair it with <c>supportHtml = true</c> and <c>isTrusted = true</c> on the
        /// <see cref="MarkdownString"/>, and escape anything untrusted before concatenating.
        /// </summary>
        public const string HTML_MARKER = "!!HTML";

        /// <summary>
        /// Escapes text so it is safe to place inside a <see cref="HTML_MARKER"/> payload.
        /// </summary>
        public static string EscapeHtml(string text)
        {
            if (text is null) return null;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        // Monaco sizes the popup before we swap markdown for HTML, so the widget has to be
        // re-measured or the injected content is clipped to the old height.
        private static void ResizeWidgetAfterInjectedHtml(HTMLElement renderedMarkdown)
        {
            var container = renderedMarkdown.closest(".monaco-hover-content, .suggest-details, .monaco-hover").As<HTMLElement>();

            if (container is null) return;

            container.style.height    = "auto";
            container.style.maxHeight = "none";

            var scrollable = renderedMarkdown.closest(".monaco-scrollable-element").As<HTMLElement>();

            if (scrollable is object)
            {
                scrollable.style.height    = "auto";
                scrollable.style.maxHeight = "none";
            }
        }
    }
}
