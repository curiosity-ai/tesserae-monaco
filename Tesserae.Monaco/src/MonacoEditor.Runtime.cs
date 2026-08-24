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
        private static readonly List<Action>             _pendingActions    = new List<Action>();

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

            // One request, and everything else follows from it: the entry injects Monaco's stylesheet
            // and installs MonacoEnvironment itself, resolving its own worker URLs from its own
            // import.meta.url. It is an ES module rather than a plain script because that is what
            // keeps Monaco's ~90 grammars and its four language-service modes behind the dynamic
            // imports Monaco already wrote them as - each becomes a chunk fetched the first time a
            // document uses that language, instead of being inlined here. See
            // build/bundle-monaco.mjs.
            await Transpose.Require.RequireAsync(RequireKind.Module, baseUrl + "/monaco.js");

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

            // Anything else queued while Monaco was still loading - language-service configuration, a
            // host's own MonacoApi call - runs now, in the order it was requested. One that throws must
            // not strand the rest, or a single bad schema takes the whole editor down with it.
            var queued = _pendingActions.ToArray();

            _pendingActions.Clear();

            foreach (var action in queued)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    console.error("Tesserae.Monaco: a queued Monaco call failed", exception);
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> once <c>monaco.*</c> is safe to touch - immediately if it
        /// already is, otherwise queued until the load finishes.
        ///
        /// This is the safe way to make any global Monaco call from application code, because most
        /// configuration happens while components are being built in <c>Main</c>, long before the first
        /// mount triggers the load. Note it does not start the load itself: queued actions run when the
        /// first component mounts, or on an explicit <see cref="LoadAsync"/>.
        /// </summary>
        public static void WhenLoaded(Action action)
        {
            if (action is null) return;

            if (IsLoaded)
            {
                action();
                return;
            }

            _pendingActions.Add(action);
        }

        /// <summary>
        /// Colours to apply to both of the package's themes, keyed by Monaco's theme colour ids -
        /// <c>"editor.selectionBackground"</c>, <c>"editorLineNumber.foreground"</c>,
        /// <c>"diffEditor.insertedTextBackground"</c>, and the several hundred others.
        ///
        /// Only <c>editor.background</c> is set by default, derived from the Tesserae theme. Add to this
        /// before the first editor is built, or call <see cref="DefineThemes"/> and
        /// <see cref="ApplyTheme()"/> afterwards to pick up a change.
        /// </summary>
        public static Dictionary<string, string> ThemeColors { get; } = new Dictionary<string, string>();

        /// <summary>
        /// The Monaco theme the package's light theme inherits from. <c>"vs"</c> by default; set it to
        /// <c>"hc-light"</c> for the high-contrast variant. Must be set before the first editor is built.
        /// </summary>
        public static string LightBase { get; set; } = "vs";

        /// <summary>
        /// The Monaco theme the package's dark theme inherits from. <c>"vs-dark"</c> by default; set it to
        /// <c>"hc-black"</c> for the high-contrast variant.
        /// </summary>
        public static string DarkBase { get; set; } = "vs-dark";

        /// <summary>
        /// Adds syntax colours for token types produced by a <b>built-in</b> language, or by a
        /// semantic-tokens provider - the rules on a <see cref="LanguageDefinition"/> only cover the
        /// language it defines, so this is how a host restyles <c>csharp</c> or colours a semantic type.
        ///
        /// Safe to call before Monaco has loaded. Call <see cref="DefineThemes"/> and
        /// <see cref="ApplyTheme()"/> afterwards if the editors are already up.
        /// </summary>
        public static void AddTokenColors(params TokenColor[] tokenColors)
        {
            if (tokenColors is null) return;

            foreach (var color in tokenColors)
            {
                if (color is object && !string.IsNullOrWhiteSpace(color.Token)) _tokenColors.Add(color);
            }
        }

        /// <summary>
        /// Defines a theme of your own, alongside the package's two. Pass its name to
        /// <see cref="ApplyTheme(string)"/> or to a component's <c>Theme(...)</c> setter.
        /// </summary>
        /// <param name="name">The theme name to register.</param>
        /// <param name="baseTheme">One of <c>"vs"</c>, <c>"vs-dark"</c>, <c>"hc-light"</c> or <c>"hc-black"</c>.</param>
        /// <param name="rules">Token colours, or null to inherit the base theme's.</param>
        /// <param name="colors">Theme colour ids, or null for the base theme's.</param>
        public static void DefineTheme(string name, string baseTheme, TokenColor[] rules = null, Dictionary<string, string> colors = null)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(name)) return;

            MonacoApi.editor.defineTheme(name, new StandaloneThemeData
            {
                baseTheme            = string.IsNullOrWhiteSpace(baseTheme) ? "vs" : baseTheme,
                inherit              = true,
                semanticHighlighting = true,
                rules                = BuildRules(rules),
                colors               = BuildColors(colors)
            });
        }

        /// <summary>
        /// (Re)defines the light and dark editor themes from the current Tesserae theme colours, plus
        /// anything in <see cref="ThemeColors"/> and any token colours registered so far. Called
        /// automatically once Monaco loads; call it again after switching the Tesserae theme at runtime,
        /// followed by <see cref="ApplyTheme()"/> on the editors that should follow.
        /// </summary>
        public static void DefineThemes()
        {
            // Monaco wants a plain #rrggbb; the Tesserae token is a CSS var that resolves to rgb(...).
            var background = Color.FromString(Color.EvalVar(Theme.Secondary.Background)).ToHex();
            var rules      = BuildThemeRules();
            var colors     = BuildColors(null);

            // The derived background is a default rather than an override: a host that names
            // editor.background in ThemeColors meant it.
            if (!ThemeColors.ContainsKey(EDITOR_BACKGROUND)) colors.Set(EDITOR_BACKGROUND, background);

            // semanticHighlighting: true is what lets the rules above apply to a semantic-tokens
            // provider's output as well as to Monarch's. Monaco's own default is "configuredByTheme", so a
            // theme that says nothing means a registered provider is never even asked.
            MonacoApi.editor.defineTheme(LIGHT_THEME, new StandaloneThemeData
            {
                baseTheme            = LightBase,
                inherit              = true,
                semanticHighlighting = true,
                rules                = rules,
                colors               = colors
            });

            MonacoApi.editor.defineTheme(DARK_THEME, new StandaloneThemeData
            {
                baseTheme            = DarkBase,
                inherit              = true,
                semanticHighlighting = true,
                rules                = rules,
                colors               = colors
            });
        }

        private const string EDITOR_BACKGROUND = "editor.background";

        // The host's theme colour ids, which Monaco reads by name.
        private static ThemeColors BuildColors(Dictionary<string, string> extra)
        {
            var colors = new ThemeColors();

            foreach (var pair in ThemeColors)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key)) colors.Set(pair.Key, pair.Value);
            }

            if (extra is object)
            {
                foreach (var pair in extra)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key)) colors.Set(pair.Key, pair.Value);
                }
            }

            return colors;
        }

        /// <summary>The theme name matching the active Tesserae theme.</summary>
        public static string ActiveTheme => Theme.IsDark ? DARK_THEME : LIGHT_THEME;

        /// <summary>Switches every editor on the page to the theme matching the active Tesserae theme.</summary>
        public static void ApplyTheme()
        {
            ApplyTheme(ActiveTheme);
        }

        /// <summary>
        /// Switches every editor on the page to a named theme. Monaco's theme is global, so this affects
        /// all of them - use a component's <c>Theme(...)</c> setter for just one.
        /// </summary>
        public static void ApplyTheme(string theme)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(theme)) return;

            MonacoApi.editor.setTheme(theme);
        }

        private static ThemeRule[] BuildThemeRules()
        {
            return BuildRules(_tokenColors.ToArray());
        }

        // Monaco drops a rule whose fontStyle or background is present but empty, so each is only set
        // when it has a value - and the leading '#' is stripped, which Monaco requires absent.
        private static ThemeRule[] BuildRules(TokenColor[] tokenColors)
        {
            var rules = new List<ThemeRule>();

            if (tokenColors is null) return rules.ToArray();

            foreach (var color in tokenColors)
            {
                if (color is null || string.IsNullOrWhiteSpace(color.Token)) continue;

                var rule = new ThemeRule { token = color.Token };

                var foreground = (color.Foreground ?? "").TrimStart('#');
                var background = (color.Background ?? "").TrimStart('#');

                if (foreground.Length > 0)                       rule.foreground = foreground;
                if (background.Length > 0)                       rule.background = background;
                if (!string.IsNullOrWhiteSpace(color.FontStyle)) rule.fontStyle  = color.FontStyle;

                rules.Add(rule);
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

            // The eager grammar wins when both are given - there is nothing to wait for.
            if (language.Tokenizer is object)
            {
                MonacoApi.languages.setMonarchTokensProvider(language.Id, language.Tokenizer);
            }
            else if (language.TokenizerFactory is object)
            {
                ApplyTokenizerFactory(language.Id, language.TokenizerFactory);
            }

            // The raw object wins when both are given: it is the escape hatch, so a host that reached for
            // it meant to override.
            var configuration = language.Configuration ?? language.Config?.ToMonaco();

            if (configuration is object)
            {
                MonacoApi.languages.setLanguageConfiguration(language.Id, configuration);
            }
            else if (language.ConfigurationFactory is object)
            {
                ApplyConfigurationFactory(language.Id, language.ConfigurationFactory);
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

        private static void ApplyTokenizerFactory(string languageId, Func<Task<object>> factory)
        {
            // Monaco takes the grammar or a promise of one, so the task goes across as a promise and
            // the fetch happens the first time a document uses the language - never at start-up.
            MonacoApi.languages.registerTokensProviderFactory(languageId, new TokensProviderFactory
            {
                create = () => PromiseExtensions.ToPromise(factory())
            });
        }

        private static void ApplyConfigurationFactory(string languageId, Func<Task<object>> factory)
        {
            MonacoApi.languages.onLanguageEncountered(languageId, () => ApplyConfigurationAsync(languageId, factory).FireAndForget());
        }

        private static async Task ApplyConfigurationAsync(string languageId, Func<Task<object>> factory)
        {
            var configuration = await factory();

            if (configuration is object) MonacoApi.languages.setLanguageConfiguration(languageId, configuration);
        }

        /// <summary>
        /// Replaces the Monarch grammar of a language that already exists - one of Monaco's own, or one
        /// this package registered earlier. <see cref="RegisterLanguage"/> is for a language of your
        /// own; this is for restyling a built-in one, e.g. swapping Monaco's deliberately coarse
        /// <c>csharp</c> grammar for a finer one.
        ///
        /// Monaco treats tokenizers as exclusive per language, so the last one registered is the one
        /// that runs. Safe to call before Monaco has loaded - the call is queued.
        /// </summary>
        /// <param name="languageId">The language whose grammar to replace, e.g. <c>"csharp"</c>.</param>
        /// <param name="tokenizer">A Monarch <c>IMonarchLanguage</c>, shaped as for <see cref="LanguageDefinition.Tokenizer"/>.</param>
        public static void SetTokenizer(string languageId, object tokenizer)
        {
            if (string.IsNullOrWhiteSpace(languageId) || tokenizer is null) return;

            WhenLoaded(() => MonacoApi.languages.setMonarchTokensProvider(languageId, tokenizer));
        }

        /// <summary>
        /// The same, with the grammar fetched only once a document actually uses the language - for one
        /// that lives in its own script file. Nothing is requested at start-up, and a language nobody
        /// opens costs nothing.
        /// </summary>
        public static void SetTokenizer(string languageId, Func<Task<object>> loadTokenizer)
        {
            if (string.IsNullOrWhiteSpace(languageId) || loadTokenizer is null) return;

            WhenLoaded(() => ApplyTokenizerFactory(languageId, loadTokenizer));
        }

        /// <summary>
        /// Sets the comment markers, brackets and auto-closing pairs of a language that already exists.
        /// Like <see cref="SetTokenizer(string, object)"/>, this is for adjusting a built-in language;
        /// a language of your own carries its configuration on its <see cref="LanguageDefinition"/>.
        /// </summary>
        public static void SetLanguageConfiguration(string languageId, object configuration)
        {
            if (string.IsNullOrWhiteSpace(languageId) || configuration is null) return;

            WhenLoaded(() => MonacoApi.languages.setLanguageConfiguration(languageId, configuration));
        }

        /// <summary>
        /// The same, applied the first time a document uses the language, from a configuration fetched
        /// then rather than at start-up.
        /// </summary>
        public static void SetLanguageConfiguration(string languageId, Func<Task<object>> loadConfiguration)
        {
            if (string.IsNullOrWhiteSpace(languageId) || loadConfiguration is null) return;

            WhenLoaded(() => ApplyConfigurationFactory(languageId, loadConfiguration));
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
