using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The stored revisions of one document, as a browsable surface: the revision list on the left, a
    /// diff of the selected revision against what the editor holds now on the right.
    ///
    /// It is an <see cref="IComponent"/> rather than a modal, so it fits a panel, a split view or a
    /// page as readily as the overlay <see cref="EditorHistoryModal"/> puts it in. Everything it shows
    /// comes from the <see cref="EditorHistory"/> it is given - the same store, the same scope, the
    /// same document - so a host that has already configured <c>PersistHistory(...)</c> has configured
    /// this too.
    ///
    /// It is composed from Tesserae rather than drawn: <see cref="SearchableList{T}"/> is the list and
    /// its "search by content" box, a <see cref="Card"/> is a row, <see cref="ListItemText"/> is the
    /// row's two lines, a <see cref="Banner"/> is the "contents are identical" strip, a
    /// <see cref="SplitView"/> is the two panes and their draggable divider, and the diff is this
    /// package's own <see cref="DiffViewer"/>. So there is no stylesheet to ship and no element to
    /// build by hand - the selected row's colours are <c>Theme</c> variables, which is what makes the
    /// surface follow the app's light and dark themes as they change.
    ///
    /// The revisions are read when it is mounted, so building one costs nothing and loads nothing.
    /// </summary>
    public sealed class EditorHistoryView : IComponent
    {
        private readonly EditorHistory _history;

        private readonly SearchableList<Revision> _revisions;
        private readonly DiffViewer               _diff;
        private readonly TextBlock                _status;
        private readonly TextBlock                _beforeHeader;
        private readonly Stack                    _currentHeader;
        private readonly TextBlock                _note;
        private readonly Banner                   _identical;
        private readonly Button                   _restore;
        private readonly IComponent               _content;

        private readonly List<Revision> _rows = new List<Revision>();

        private readonly SearchBox _search;

        private Revision                   _selected;
        private bool                       _sideBySide = true;
        private string                     _current = "";
        private bool                       _identicalDismissed;
        private bool                       _loaded;
        private bool                       _failed;
        private Action<EditorHistoryEntry> _onRestored;

        /// <param name="history">
        /// The recorder to browse - <c>editor.History</c>, or one built by hand around a store. An
        /// unconfigured one (no scope) renders as a note saying so rather than as an empty list.
        /// </param>
        public EditorHistoryView(EditorHistory history)
        {
            _history = history;

            _note = TextBlock("").Small().Secondary().P(12);

            // The list and the box that filters it are one component, and the filtering runs over each
            // revision's own text - which is what "search by content" means here: the query is matched
            // against what the document said at that moment, not against the row's label.
            //
            // The box is captured because the empty state has to say which emptiness it is: a list with
            // nothing in it and a query that matched none of it are the same absence of rows, and
            // "nothing stored yet" under an active query reads as a lost history rather than a narrow
            // search.
            _revisions = SearchableList(new Revision[0], 100.percent())
               .CaptureSearchBox(out _search)
               .WithNoResultsMessage(EmptyMessage)
               .SearchBox(box => box.SetPlaceholder("Search by content"));

            _status        = TextBlock("").Small().Secondary();
            _beforeHeader  = TextBlock("Before").Small().Secondary();

            _diff = MonacoEditor.Diff()
               .IgnoreTrimWhitespace(true)
               .RenderIndicators(true)
               .RenderOverviewRuler(true)
               .OnDiffUpdated(ShowDiffState);

            _identical = Banner("Contents are identical").Primary().Compact().Flat()
               .SetIcon(UIcons.SquareInfo)
               .OnDismiss(() => _identicalDismissed = true);

            _restore = Button("Revert").SetIcon(UIcons.RotateLeft).Compact()
               .Tooltip("Put this revision back into the editor, as one undoable edit")
               .OnClick(RestoreSelected)
               .Disabled();

            var layout = IconToggle<bool>(
                    IconToggleItem(UIcons.SplitScreen, "Side by side", true),
                    IconToggleItem(UIcons.Square,      "Inline",       false)).Compact()
               .OnChange((_, sideBySide) => ShowSideBySide(sideBySide));

            var toolbar = HStack().WS().NoWrap().AlignItemsCenter().Gap(6.px()).PL(8).PR(8).PT(4).PB(4).Children(
                _restore,
                Button(UIcons.Refresh).Compact().NoBackground().Tooltip("Reload the revisions").OnClick(() => LoadAsync().FireAndForget()),
                Raw().Grow(),
                _status,
                Button(UIcons.AngleUp).Compact().NoBackground().Tooltip("Previous difference").OnClick(() => _diff.GoToPreviousDifference()),
                Button(UIcons.AngleDown).Compact().NoBackground().Tooltip("Next difference").OnClick(() => _diff.GoToNextDifference()),
                layout);

            // Which side is which, over each pane. The lock says the left one is the stored revision and
            // cannot be typed into; the right is the editor's own text. Inline there is only one pane,
            // so the right half is collapsed and the left one names both documents - two headers over a
            // single column would each point at nothing.
            _currentHeader = HStack().Grow().Children(TextBlock("Current").Small().Secondary());

            var headers = HStack().WS().NoWrap().AlignItemsCenter().PL(8).PR(8).PT(2).PB(2).Children(
                HStack().NoWrap().AlignItemsCenter().Gap(4.px()).Grow().Children(Icon(UIcons.Lock, size: TextSize.Small), _beforeHeader),
                _currentHeader);

            // Three things this layout depends on, each measured:
            //
            //  - MinHeight(0) on every box the diff is inside. A flex item's automatic minimum size is
            //    its content's, and Monaco's scroll layer is 16.7 million pixels tall, so a box holding
            //    it refuses to shrink to the surface and the view grows past the modal.
            //  - The furniture does not shrink. The toolbar, the banner and the pane headers are
            //    fixed-height rows; letting flexbox take height off them to fit the diff squashes the
            //    text rather than the surface that has somewhere to give.
            //  - The diff sits in a growing wrapper rather than being the flex item itself. Its own
            //    container carries height: 100% - Monaco measures the element it was created in - and a
            //    percentage height resolves against the flex *container*, so as a direct item it asks
            //    for the whole column and overflows it by exactly the height of the banner and the
            //    headers. Inside a wrapper the 100% is of the space flexbox actually left over.
            var comparison = VStack().WS().HS().NoWrap().Children(
                _identical.NoShrink().Collapse(),
                headers.NoShrink(),
                HorizontalSeparator("").NoShrink(),
                VStack().WS().Grow().MinHeight(0.px()).Children(_diff.WS().HS()));

            var panes = SplitView().WS().HS()
               .LeftIsSmaller(280.px(), 560.px(), 200.px())
               .Left(_revisions.WS().HS())
               .Right(comparison)
               .Resizable();

            _content = VStack().WS().HS().NoWrap().Children(
                    toolbar.NoShrink(),
                    HorizontalSeparator("").NoShrink(),
                    panes.Grow().MinHeight(0.px()))
               .WhenMounted(() => { if (!_loaded) LoadAsync().FireAndForget(); });
        }

        /// <summary>The recorder being browsed.</summary>
        public EditorHistory History => _history;

        /// <summary>The diff editor showing the comparison, for decorating or configuring it further.</summary>
        public DiffViewer Diff => _diff;

        /// <summary>The revision currently selected, or null when nothing is.</summary>
        public EditorHistoryEntry Selected => _selected is null ? null : _selected.Entry;

        /// <summary>Runs after a revision has been put back into the editor from here. Handlers accumulate.</summary>
        public EditorHistoryView OnRestored(Action<EditorHistoryEntry> handler)
        {
            _onRestored += handler;

            return this;
        }

        /// <summary>
        /// Re-reads the revisions from the store. Runs on mount; call it again after writing one from
        /// outside this view.
        /// </summary>
        public async Task LoadAsync()
        {
            if (_history is null || !_history.IsConfigured) return;

            // What the editor holds right now is the comparison's right-hand side, and it is read here
            // rather than kept from construction because a revert - or the user typing behind the
            // modal - moves it.
            _current = _history.CurrentText ?? "";

            EditorHistoryEntry[] entries;

            try
            {
                entries = await _history.ListAsync();
            }
            catch (Exception)
            {
                _failed = true;

                return;
            }

            _loaded = true;
            _failed = false;

            var language = _history.CurrentLanguage;

            if (!string.IsNullOrWhiteSpace(language)) _diff.SetLanguage(language);

            _rows.Clear();

            // Newest first, over the union rather than per source. A history can be fed by more than
            // one store - this browser's IndexedDB and a server's checkpoints - and each answers in its
            // own order, so the one thing a reader needs of a revision list, that it runs in time, has
            // to be established here rather than assumed of whatever answered.
            var sorted = new List<EditorHistoryEntry>(entries ?? new EditorHistoryEntry[0]);

            sorted.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));

            foreach (var entry in sorted)
            {
                _rows.Add(new Revision(entry, Select));
            }

            _revisions.Items.ReplaceAll(_rows);

            // The newest is what someone opening this wants to see first. A revert re-loads, and then
            // the row that was selected has been replaced - so the selection follows the timestamp
            // rather than the object.
            Select(Find(_selected is null ? 0 : _selected.Entry.Timestamp));
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>
        /// What stands in for the list when it has no rows to show. Built on demand, because which of
        /// the four emptinesses it is - unconfigured, still loading, a store that would not answer, or a
        /// query that matched nothing - is only known when the list finds itself empty.
        /// </summary>
        private IComponent EmptyMessage()
        {
            var query = _search is null ? null : _search.Text;

            if (_history is null || !_history.IsConfigured)
            {
                _note.Text = "This editor has no history: PersistHistory(...) needs a scope before anything is stored.";
            }
            else if (_failed)
            {
                _note.Text = "The store could not be read.";
            }
            else if (!_loaded)
            {
                _note.Text = "Loading revisions...";
            }
            else if (!string.IsNullOrWhiteSpace(query))
            {
                _note.Text = "No revision contains \"" + query + "\".";
            }
            else
            {
                _note.Text = "Nothing stored yet.";
            }

            return _note;
        }

        #region Selection

        private Revision Find(double timestamp)
        {
            foreach (var row in _rows)
            {
                if (row.Entry.Timestamp == timestamp) return row;
            }

            return _rows.Count == 0 ? null : _rows[0];
        }

        private void Select(Revision revision)
        {
            _selected = revision;

            foreach (var row in _rows)
            {
                row.Select(row == revision);
            }

            _restore.Disabled(revision is null || _history is null || !_history.IsAttached);

            ShowHeaders();

            _diff.SetContent(revision is null ? "" : revision.Entry.Text ?? "", _current);

            // The comparison is computed on the diff worker, so the status and the banner are written
            // from OnDiffUpdated rather than here - reading the diff now reads the previous one.
            _status.Text = revision is null ? "" : "comparing...";
        }

        /// <summary>Two panes or one, and the headers that name them.</summary>
        private void ShowSideBySide(bool sideBySide)
        {
            _sideBySide = sideBySide;

            _diff.SideBySide(sideBySide);

            if (sideBySide)
            {
                _currentHeader.Show();
            }
            else
            {
                _currentHeader.Collapse();
            }

            ShowHeaders();
        }

        private void ShowHeaders()
        {
            if (_selected is null)
            {
                _beforeHeader.Text = "Before";

                return;
            }

            var stamp = Stamp(_selected.Entry.Timestamp);

            _beforeHeader.Text = _sideBySide ? "Before " + stamp : "Before " + stamp + "   \u2192   Current";
        }

        #endregion

        #region The diff's state

        private void ShowDiffState()
        {
            var identical = _diff.IsIdentical;
            var changes   = _diff.ChangeCount;

            _status.Text = identical || changes == 0
                ? "No differences"
                : changes == 1 ? "1 difference" : changes + " differences";

            if (identical && !_identicalDismissed)
            {
                _identical.Show();
            }
            else
            {
                _identical.Collapse();
            }
        }

        #endregion

        #region Reverting

        private void RestoreSelected()
        {
            var revision = _selected;

            if (revision is null || _history is null || !_history.IsAttached) return;

            if (!_history.Restore(revision.Entry)) return;

            // The editor is now the revision, so the right-hand side has moved: re-read it rather than
            // leaving the diff showing the comparison the revert has just settled.
            _current = _history.CurrentText ?? "";

            _diff.SetModified(_current);

            _onRestored?.Invoke(revision.Entry);
        }

        #endregion

        #region Formatting

        /// <summary>
        /// The label the recorder filed the revision under, as the row's first line. The automatic
        /// snapshot's <c>"typing"</c> reads as a state rather than an event, so it is spelled as one.
        /// </summary>
        private static string Describe(EditorHistoryEntry entry)
        {
            var label = entry.Label;

            if (string.IsNullOrWhiteSpace(label)) return "Revision";

            return label == "typing" ? "Edits" : char.ToUpper(label[0]) + label.Substring(1);
        }

        /// <summary>
        /// An epoch-millisecond stamp as a local <c>yyyy-MM-dd, HH:mm</c>. Hand-formatted rather than
        /// through a format string: the parts are what a row shows, and padding them is the whole job.
        /// </summary>
        private static string Stamp(double epochMilliseconds)
        {
            var moment = EditorHistory.ToDateTime(epochMilliseconds).ToLocalTime();

            return moment.Year + "-" + Two(moment.Month) + "-" + Two(moment.Day) + ", " + Two(moment.Hour) + ":" + Two(moment.Minute) + ":" + Two(moment.Second);
        }

        /// <summary>
        /// The same instant the way a person would say it: minutes ago while that is still the useful
        /// answer, then the clock time for today, then the day, then the year. The precise
        /// <see cref="Stamp"/> is on the row's tooltip, so nothing is lost by being brief here - and
        /// being brief is what lets the row stay one line for the label and one for everything else.
        ///
        /// Relative only inside the hour. "5 hours ago" makes a reader do arithmetic to answer "was
        /// that before lunch"; "11:32" does not.
        /// </summary>
        private static string When(double epochMilliseconds)
        {
            var moment  = EditorHistory.ToDateTime(epochMilliseconds).ToLocalTime();
            var now     = EditorHistory.ToDateTime(EditorHistory.Now()).ToLocalTime();
            var seconds = (now - moment).TotalSeconds;

            if (seconds >= 0 && seconds < 45)   return "just now";
            if (seconds >= 0 && seconds < 90)   return "a minute ago";
            if (seconds >= 0 && seconds < 3600) return (int)System.Math.Round(seconds / 60d) + " min ago";

            var today     = now.Date;
            var day       = moment.Date;
            var clock     = Two(moment.Hour) + ":" + Two(moment.Minute);
            var dayOffset = (today - day).TotalDays;

            if (dayOffset == 0) return clock;
            if (dayOffset == 1) return "yesterday " + clock;

            var date = moment.Day + " " + MONTHS[moment.Month - 1];

            return moment.Year == now.Year ? date + ", " + clock : date + " " + moment.Year;
        }

        private static readonly string[] MONTHS = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        private static string Two(int value) => value < 10 ? "0" + value : value.ToString();

        private static int Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var lines = 1;

            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n') lines++;
            }

            return lines;
        }

        /// <summary>
        /// Where the revision came from, as one glyph in one colour with the sentence in its tooltip.
        /// A word for it would cost a third of the row's width to say something a reader only needs
        /// when two origins are actually mixed - and the icon still says it at a glance when they are:
        /// a browser window for what was typed here, a cloud for what arrived from elsewhere.
        /// </summary>
        private static IComponent OriginGlyph(EditorHistoryOrigin origin)
        {
            if (origin == EditorHistoryOrigin.Remote)
            {
                return Icon(UIcons.CloudCheck, color: Theme.Primary.Background)
                   .Tooltip("From outside this browser - a server checkpoint, or another device");
            }

            if (origin == EditorHistoryOrigin.Local)
            {
                return Icon(UIcons.Browser, color: Theme.Secondary.Foreground)
                   .Tooltip("Saved in this browser as you typed");
            }

            return Icon(UIcons.QuestionSquare, color: Theme.Secondary.Foreground)
               .Tooltip("Origin not recorded - stored before this was kept, or by a store that does not set it");
        }

        /// <summary>
        /// Who made it, as a pill washed in a colour derived from their name - so one person's
        /// revisions read as one person's down the list without the name having to be read each time.
        /// Nothing at all when no author was recorded, which is the ordinary case for a browser-only
        /// history: a pill saying "you" on every row would be noise.
        ///
        /// Only the background is coloured. The text keeps the tag's own themed foreground, because a
        /// hue picked to read on a white surface is the one that disappears on a dark one - and the
        /// pill has to stay legible through a theme change with nothing re-rendering it.
        /// </summary>
        private static IComponent AuthorChip(string author)
        {
            if (string.IsNullOrWhiteSpace(author)) return Raw();

            return Tag(author).Pill()
               .Background("hsla(" + HueFor(author) + ", 70%, 50%, 0.25)")
               .Tooltip("Made by " + author);
        }

        /// <summary>
        /// A hue for a name, from a palette rather than from the whole circle.
        ///
        /// Hashing straight onto 0-359 looks better than it is: two names that land eight degrees apart
        /// are two colours nobody can tell apart, which reads as one colour rendered inconsistently.
        /// Measured with the demo's own names, that is exactly what happened - "Alex Kim" and
        /// "build-bot" came out seven degrees apart, both green. A palette of mutually distinguishable
        /// hues instead means two people are either clearly different or exactly the same, and the name
        /// beside the pill settles the second case.
        ///
        /// The slot comes from the hash's whole range rather than its remainder, because the low digits
        /// of an accumulator this small are barely mixed: <c>hash % 10</c> put two of the three demo
        /// names in one slot, while scaling the whole value spread them.
        /// </summary>
        private static int HueFor(string author)
        {
            var hash = 0;

            // Bounded at every step, so the running value stays an exact integer where this actually
            // runs - JavaScript has no int, and an unbounded accumulation over a long name would drift
            // into the range where a double stops counting by ones.
            foreach (var character in author)
            {
                hash = (hash * 131 + character) % HASH_MODULUS;
            }

            return AUTHOR_HUES[hash * AUTHOR_HUES.Length / HASH_MODULUS];
        }

        private const int HASH_MODULUS = 100003;

        private static readonly int[] AUTHOR_HUES = { 212, 148, 28, 278, 330, 188, 100, 250, 62, 0 };

        #endregion

        /// <summary>
        /// One row: a clickable <see cref="Card"/> holding what a reader needs to place the revision -
        /// where it came from, who made it, when, and how big it was - plus the
        /// <see cref="ISearchableItem"/> half that makes the list's search box filter by the revision's
        /// own content.
        ///
        /// Two lines and nothing more, because the list is a 280px column beside the diff that is the
        /// actual subject. The origin is a coloured glyph rather than a word, the author a tinted pill
        /// in a colour derived from their name, and the time is said the way a person would say it -
        /// each with the precise version in a tooltip, which is what keeps the row short without
        /// hiding anything.
        ///
        /// The card is built once and handed back from <see cref="Render"/> every time, because the
        /// list re-renders its rows on each query and a new card each time would lose the selection.
        /// </summary>
        private sealed class Revision : ISearchableItem
        {
            private readonly EditorHistoryEntry _entry;
            private readonly Card               _card;

            internal Revision(EditorHistoryEntry entry, Action<Revision> onPicked)
            {
                _entry = entry;

                var size = HStack().NoWrap().AlignItemsCenter().Gap(6.px()).Children(AuthorChip(entry.Author));

                size.Add(TextBlock(Lines(entry.Text) + " lines").Tiny().Secondary().NoWrap());

                _card = Card(HStack().WS().NoWrap().AlignItemsCenter().Gap(8.px()).Children(
                        OriginGlyph(entry.Origin),
                        VStack().Grow().MinWidth(0.px()).Children(
                            HStack().WS().NoWrap().AlignItemsCenter().Gap(6.px()).Children(
                                TextBlock(Describe(entry)).Small().SemiBold().NoWrap(),
                                Raw().Grow(),
                                TextBlock(When(entry.Timestamp)).Tiny().Secondary().NoWrap().Tooltip(Stamp(entry.Timestamp))),
                            size)))
                   .Compact()
                   .HoverColor()
                   .OnClick(() => onPicked(this));

                Select(false);
            }

            internal EditorHistoryEntry Entry => _entry;

            /// <summary>
            /// Whether this revision's text - or its label, or its author - contains one of the query's
            /// terms. The list calls this once per term and keeps the row only if every one matches, so
            /// "alex checkpoint" narrows to one person's checkpoints.
            /// </summary>
            public bool IsMatch(string searchTerm)
            {
                if (string.IsNullOrEmpty(searchTerm)) return true;

                var needle = searchTerm.ToLower();

                return Contains(_entry.Text, needle) || Contains(_entry.Label, needle) || Contains(_entry.Author, needle);
            }

            public IComponent Render() => _card;

            /// <summary>
            /// The selected look, in theme variables rather than a stylesheet: the pressed background
            /// the rest of the toolkit uses for a chosen row, and the brand colour on the border.
            /// </summary>
            internal void Select(bool selected)
            {
                _card
                   .BackgroundColor(selected ? Theme.Default.BackgroundActive : Theme.Default.Background)
                   .Border(selected ? Theme.Primary.Background : Theme.Default.Border);
            }

            private static bool Contains(string haystack, string lowercaseNeedle)
            {
                return !string.IsNullOrEmpty(haystack) && haystack.ToLower().IndexOf(lowercaseNeedle) >= 0;
            }
        }
    }

    /// <summary>
    /// An <see cref="EditorHistoryView"/> in a Tesserae <see cref="Tesserae.Modal"/> - the "what did
    /// this file look like before" overlay, opened from a button or a keybinding and closed with Esc.
    ///
    /// <code>
    /// var editor = MonacoEditor.Editor().PersistHistory("user:42", "src/Program.cs");
    ///
    /// Button("History").OnClick(() =&gt; editor.ShowHistory());
    /// </code>
    ///
    /// Build one per opening. The view fetches on mount and the diff editor inside it is created then
    /// and disposed when the modal closes, which is what keeps a closed modal from holding a Monaco
    /// instance and two models alive.
    /// </summary>
    public sealed class EditorHistoryModal
    {
        private readonly EditorHistoryView _view;
        private readonly Modal             _modal;

        /// <param name="history">The recorder to browse.</param>
        /// <param name="title">
        /// The overlay's heading. Defaults to <c>History: </c> plus the last segment of the document
        /// id, which for the usual path-shaped id is the file name.
        /// </param>
        public EditorHistoryModal(EditorHistory history, string title = null)
        {
            _view = new EditorHistoryView(history);

            _modal = Modal(HStack().NoWrap().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.ClockFuturePast),
                    TextBlock(title ?? "History: " + DocumentName(history)).SemiBold()))
               .W(90.vw())
               .H(85.vh())
               .NoContentPadding()
               .LightDismiss()
               .ShowCloseButton()
               .Content(_view);
        }

        /// <summary>The overlay itself, for sizing it differently or hooking its show/hide events.</summary>
        public Modal Modal => _modal;

        /// <summary>The browsable surface inside it.</summary>
        public EditorHistoryView View => _view;

        /// <summary>Runs after a revision has been put back into the editor. Handlers accumulate.</summary>
        public EditorHistoryModal OnRestored(Action<EditorHistoryEntry> handler)
        {
            _view.OnRestored(handler);

            return this;
        }

        /// <summary>Opens it.</summary>
        public EditorHistoryModal Show()
        {
            _modal.Show();

            return this;
        }

        /// <summary>Closes it.</summary>
        public EditorHistoryModal Hide()
        {
            _modal.Hide();

            return this;
        }

        /// <summary>Builds one and opens it - the one-liner behind <c>editor.ShowHistory()</c>.</summary>
        public static EditorHistoryModal Show(EditorHistory history, string title = null)
        {
            return new EditorHistoryModal(history, title).Show();
        }

        private static string DocumentName(EditorHistory history)
        {
            var documentId = history is null ? null : history.DocumentId;

            if (string.IsNullOrWhiteSpace(documentId)) return "this document";

            var cut = Math.Max(documentId.LastIndexOf('/'), documentId.LastIndexOf('\\'));

            return cut < 0 ? documentId : documentId.Substring(cut + 1);
        }
    }
}
