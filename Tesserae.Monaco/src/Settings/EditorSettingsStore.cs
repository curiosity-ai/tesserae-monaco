using System;
using System.Collections.Generic;
using Transpose;
using Transpose.Core;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Where a person's <see cref="EditorSettings"/> live: the browser's <c>localStorage</c>, under one key for the
    /// whole origin, so a choice made in one editor is the choice in every editor of the application and on the
    /// next visit. They are a few hundred bytes read once per editor mount and written on a click, which is the
    /// shape <c>localStorage</c> suits (the revision history, which is neither, uses IndexedDB).
    ///
    /// Saving, or resetting, tells every live editor through <see cref="Subscribe"/>, and so does a change made in
    /// another tab, so open editors follow without a reload. Storage that throws - a private window, a sandboxed
    /// frame - is treated as empty: settings then apply to the current page's editors only until it reloads.
    /// </summary>
    public static class EditorSettingsStore
    {
        /// <summary>The key the settings are stored under.</summary>
        public const string KEY = "tesserae-monaco-editor-settings";

        private static readonly List<Action> _subscribers = new List<Action>();

        private static bool           _listeningToOtherTabs;
        private static EditorSettings _sessionOnly;

        /// <summary>The saved settings read over <paramref name="baseline"/>, or null when nothing has been saved.</summary>
        public static EditorSettings Load(EditorSettings baseline)
        {
            if (_sessionOnly is object) return _sessionOnly.Clone();

            try
            {
                var json = window.localStorage.getItem(KEY);

                if (string.IsNullOrEmpty(json)) return null;

                return EditorSettings.FromPlainObject(es5.JSON.parse(json), baseline ?? new EditorSettings());
            }
            catch (Exception exception)
            {
                console.warn("Tesserae.Monaco settings: could not read local storage", exception);

                return null;
            }
        }

        /// <summary>Saves the settings and tells every live editor.</summary>
        public static void Save(EditorSettings settings)
        {
            if (settings is null) return;

            _sessionOnly = null;

            try
            {
                window.localStorage.setItem(KEY, es5.JSON.stringify(settings.ToPlainObject()));
            }
            catch (Exception exception)
            {
                // Keep them for this page at least: the person pressed Save and the editors should follow.
                _sessionOnly = settings.Clone();

                console.warn("Tesserae.Monaco settings: could not write local storage", exception);
            }

            Notify();
        }

        /// <summary>Forgets the saved settings, so the editors go back to the host's defaults, and tells every live editor.</summary>
        public static void Clear()
        {
            _sessionOnly = null;

            try
            {
                window.localStorage.removeItem(KEY);
            }
            catch (Exception exception)
            {
                console.warn("Tesserae.Monaco settings: could not clear local storage", exception);
            }

            Notify();
        }

        /// <summary>Calls <paramref name="onChanged"/> after every save or reset, here or in another tab. Dispose of the returned action when the editor goes.</summary>
        public static Action Subscribe(Action onChanged)
        {
            if (onChanged is null) return () => { };

            ListenToOtherTabs();

            _subscribers.Add(onChanged);

            return () => _subscribers.Remove(onChanged);
        }

        private static void Notify()
        {
            // A copy: a subscriber that unsubscribes while being told must not skip its neighbour.
            foreach (var subscriber in _subscribers.ToArray())
            {
                subscriber();
            }
        }

        private static void ListenToOtherTabs()
        {
            if (_listeningToOtherTabs) return;

            _listeningToOtherTabs = true;

            window.addEventListener("storage", (Event e) =>
            {
                // `key` is null when the whole storage was cleared.
                var key = Script.Get<string>(e, "key");

                if (key is null || key == KEY) Notify();
            });
        }
    }
}
