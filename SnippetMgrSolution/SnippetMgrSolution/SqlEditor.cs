using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SnippetMgr.Models;

namespace SnippetMgr.Controls
{
    public class SqlEditor : UserControl
    {
        private ToolStrip _toolbar = null!;
        private Panel _gutter = null!;
        private TableLayoutPanel _layoutTable = null!;
        public RichTextBox InnerEditor { get; private set; } = null!;
        private ContextMenuStrip _ctxMenu = null!;
        private ListBox _suggestionBox = null!;

        private string? _currentFilePath = null;
        public bool IsModified { get; private set; } = false;
        private List<string> _dbObjects = new();
        private List<EntryConfig> _snippets = new();
        private bool _isTyping = false;

        private class SuggestionItem
        {
            public string DisplayText { get; set; } = "";
            public string InsertText { get; set; } = "";
            public override string ToString() => DisplayText;
        }

        // --- P/Invoke ---
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, ref RECT lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const int WM_USER = 0x400;
        private const int EM_GETFIRSTVISIBLELINE = 0xCE;
        private const int EM_SETRECT = 0xB3;
        private const int EM_SETOPTIONS = WM_USER + 77;

        private const int ECOOP_SET = 0x0001;
        private const int ECOOP_OR = 0x0002;
        private const int ECOOP_AND = 0x0003;
        private const int ECOOP_XOR = 0x0004;
        private const int ECO_VIEWWHITESPACE = 0x0020;

        private const int WM_VSCROLL = 0x115;
        private const int WM_MOUSEWHEEL = 0x20A;
        private const int WM_PAINT = 0xF;
        private const int WM_SETREDRAW = 0x000B;

        // --- Highlighting ---
        private static readonly string P_Keywords =
            @"\b(SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|SET|DELETE|DROP|CREATE|ALTER|TABLE|VIEW|PROCEDURE|FUNCTION|TRIGGER|USE|GO|INNER|LEFT|RIGHT|JOIN|ON|GROUP|BY|ORDER|HAVING|TOP|DISTINCT|UNION|AND|OR|NOT|IN|IS|NULL|LIKE|AS|WITH|IF|BEGIN|END|COMMIT|ROLLBACK)\b";

        private static readonly string P_Strings = @"'([^']|'')*'";
        private static readonly string P_Comments = @"--.*";

        public SqlEditor()
        {
            InitializeComponent();
            InitializeAutoComplete();
        }

        [Browsable(true)]
        public override string Text
        {
            get => InnerEditor.Text;
            set
            {
                InnerEditor.Text = value ?? string.Empty;
                HighlightSyntax();
                UpdateGutterWidth();
            }
        }

        public void SetDatabaseObjects(List<string> objects) =>
            _dbObjects = objects ?? new List<string>();

        public void SetSnippets(List<EntryConfig> snippets) =>
            _snippets = snippets ?? new List<EntryConfig>();

        /// <summary>
        /// Pokazuje / ukrywa znaki niedrukowalne przy użyciu ECO_VIEWWHITESPACE
        /// na kontrolce RichEdit (RICHEDIT50W).
        /// </summary>
        public void ToggleWhitespace(bool show)
        {
            if (InnerEditor == null || InnerEditor.IsDisposed)
                return;

            int mask = ECO_VIEWWHITESPACE;

            if (show)
            {
                // dodajemy bit VIEWWHITESPACE
                SendMessage(InnerEditor.Handle, EM_SETOPTIONS,
                    (IntPtr)ECOOP_OR, (IntPtr)mask);
            }
            else
            {
                // usuwamy bit VIEWWHITESPACE – AND z negacją maski
                SendMessage(InnerEditor.Handle, EM_SETOPTIONS,
                    (IntPtr)ECOOP_AND, (IntPtr)(~mask));
            }
        }

        private void SetInnerMargins()
        {
            if (InnerEditor == null) return;

            var r = new RECT
            {
                Left = 10,
                Top = 5,
                Right = InnerEditor.ClientSize.Width - 5,
                Bottom = InnerEditor.ClientSize.Height - 5
            };
            SendMessage(InnerEditor.Handle, EM_SETRECT, IntPtr.Zero, ref r);
        }

        private void UpdateGutterWidth()
        {
            if (InnerEditor == null || _layoutTable == null) return;

            int lineCount = Math.Max(1, InnerEditor.Lines.Length);
            int digits = lineCount.ToString().Length;
            int baseWidth = 45;
            int extra = Math.Max(0, digits - 2) * 10;
            int width = baseWidth + extra;

            if (Math.Abs(_layoutTable.ColumnStyles[0].Width - width) > 0.1)
            {
                _layoutTable.ColumnStyles[0].Width = width;
                _gutter.Invalidate();
            }
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // --- Toolbar ---
            _toolbar = new ToolStrip
            {
                Padding = new Padding(5),
                BackColor = Color.FromArgb(240, 240, 240),
                RenderMode = ToolStripRenderMode.System,
                Dock = DockStyle.Bottom,
                GripStyle = ToolStripGripStyle.Hidden
            };

            var btnNew = new ToolStripButton("📄") { ToolTipText = "Nowy" };
            btnNew.Click += (s, e) => NewFile();

            var btnOpen = new ToolStripButton("📂") { ToolTipText = "Otwórz" };
            btnOpen.Click += (s, e) => OpenFile();

            var btnSave = new ToolStripButton("💾") { ToolTipText = "Zapisz" };
            btnSave.Click += (s, e) => SaveFile(false);

            var btnFormat = new ToolStripButton("{ }") { ToolTipText = "Formatuj SQL" };
            btnFormat.Click += (s, e) => FormatSqlCode();

            var btnWhite = new ToolStripButton("¶")
            {
                ToolTipText = "Pokaż/ukryj znaki niedrukowalne",
                CheckOnClick = true
            };
            btnWhite.CheckedChanged += (s, e) =>
            {
                ToggleWhitespace(btnWhite.Checked);
            };

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                btnNew,
                btnOpen,
                btnSave,
                new ToolStripSeparator(),
                btnWhite,
                new ToolStripSeparator(),
                btnFormat
            });
            Controls.Add(_toolbar);

            // --- Layout: [gutter][editor] ---
            _layoutTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _layoutTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
            _layoutTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(_layoutTable);
            _layoutTable.BringToFront();

            // --- Gutter (numery linii) ---
            _gutter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.Gray,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 0)
            };

            typeof(Panel).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null,
                _gutter,
                new object[] { true });

            _gutter.Paint += Gutter_Paint;
            _layoutTable.Controls.Add(_gutter, 0, 0);

            // --- RichTextBox (RICHEDIT50W) ---
            InnerEditor = new ExRichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 11F),
                AcceptsTab = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = false,
                HideSelection = false
            };

            InnerEditor.VScroll += (s, e) => _gutter.Invalidate();
            InnerEditor.Resize += (s, e) =>
            {
                _gutter.Invalidate();
                SetInnerMargins();
            };
            InnerEditor.TextChanged += (s, e) =>
            {
                _gutter.Invalidate();
                OnTextChanged(e); // odpala naszą logikę OnTextChanged
                IsModified = true;
                UpdateGutterWidth();
            };
            InnerEditor.SelectionChanged += (s, e) => _gutter.Invalidate();

            InnerEditor.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.S)
                {
                    SaveFile(false);
                    e.SuppressKeyPress = true;
                }
            };

            // Kontekstowe menu
            _ctxMenu = new ContextMenuStrip();
            _ctxMenu.Items.Add("Kopiuj", null, (s, e) => InnerEditor.Copy());
            _ctxMenu.Items.Add("Wklej", null, (s, e) => InnerEditor.Paste());
            InnerEditor.ContextMenuStrip = _ctxMenu;

            _layoutTable.Controls.Add(InnerEditor, 1, 0);

            Load += (s, e) =>
            {
                SetInnerMargins();
                UpdateGutterWidth();
            };

            ResumeLayout(false);
            PerformLayout();
        }

        // --- Autocomplete ---
        private void InitializeAutoComplete()
        {
            _suggestionBox = new ListBox
            {
                Parent = this,
                Visible = false,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(250, 250, 250),
                Height = 150,
                Width = 300
            };

            _suggestionBox.DoubleClick += (s, e) => CommitSuggestion();

            InnerEditor.KeyDown += InnerEditor_KeyDown;
        }

        private void InnerEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_suggestionBox.Visible)
            {
                if (e.KeyCode == Keys.Down)
                {
                    if (_suggestionBox.SelectedIndex < _suggestionBox.Items.Count - 1)
                        _suggestionBox.SelectedIndex++;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Up)
                {
                    if (_suggestionBox.SelectedIndex > 0)
                        _suggestionBox.SelectedIndex--;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
                {
                    CommitSuggestion();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode == Keys.Escape)
                {
                    _suggestionBox.Hide();
                    e.Handled = true;
                    return;
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (_isTyping) return;
            _isTyping = true;
            try
            {
                HighlightSyntax();
                ShowSuggestions();
            }
            finally
            {
                _isTyping = false;
            }

            base.OnTextChanged(e);
        }

        private void ShowSuggestions()
        {
            if (_snippets == null) _snippets = new List<EntryConfig>();
            if (_dbObjects == null) _dbObjects = new List<string>();

            int caret = InnerEditor.SelectionStart;
            if (caret <= 0)
            {
                _suggestionBox.Hide();
                return;
            }

            int start = caret - 1;
            string text = InnerEditor.Text;
            while (start >= 0 &&
                   (char.IsLetterOrDigit(text[start]) ||
                    text[start] == '.' ||
                    text[start] == '_' ||
                    text[start] == '['))
            {
                start--;
            }
            start++;

            if (start >= text.Length || start >= caret)
            {
                _suggestionBox.Hide();
                return;
            }

            string word = text.Substring(start, caret - start);
            if (string.IsNullOrWhiteSpace(word))
            {
                _suggestionBox.Hide();
                return;
            }

            var hits = new List<SuggestionItem>();

            // Snippety
            foreach (var sn in _snippets)
            {
                if (!string.IsNullOrEmpty(sn.Shortcut) &&
                    sn.Shortcut.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SuggestionItem
                    {
                        DisplayText = $"{sn.Shortcut} (Snippet)",
                        InsertText = sn.PasteText ?? string.Empty
                    });
                }
            }

            // Obiekty DB
            foreach (var t in _dbObjects)
            {
                if (!string.IsNullOrEmpty(t) &&
                    t.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SuggestionItem
                    {
                        DisplayText = t,
                        InsertText = t
                    });
                }
            }

            if (hits.Count == 0)
            {
                _suggestionBox.Hide();
                return;
            }

            _suggestionBox.DataSource = hits.Take(20).ToList();

            Point caretPos = InnerEditor.GetPositionFromCharIndex(caret);
            int x = (int)_layoutTable.ColumnStyles[0].Width + caretPos.X + 5;
            int y = caretPos.Y + 25;

            if (x + _suggestionBox.Width > Width)
                x = Width - _suggestionBox.Width;
            if (y + _suggestionBox.Height > Height)
                y = Height - _suggestionBox.Height;

            _suggestionBox.Location = new Point(x, y);
            _suggestionBox.Show();
            _suggestionBox.BringToFront();
        }

        private void CommitSuggestion()
        {
            if (_suggestionBox.SelectedItem is not SuggestionItem item)
                return;

            int caret = InnerEditor.SelectionStart;
            if (caret < 0) return;

            int start = caret - 1;
            string text = InnerEditor.Text;
            while (start >= 0 &&
                   (char.IsLetterOrDigit(text[start]) ||
                    text[start] == '.' ||
                    text[start] == '_' ||
                    text[start] == '['))
            {
                start--;
            }
            start++;

            InnerEditor.Select(start, caret - start);
            InnerEditor.SelectedText = item.InsertText ?? string.Empty;
            _suggestionBox.Hide();
        }

        // --- Kolorowanie SQL ---
        private void HighlightSyntax()
        {
            if (InnerEditor == null) return;

            SendMessage(InnerEditor.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);

            int selStart = InnerEditor.SelectionStart;
            int selLength = InnerEditor.SelectionLength;

            // reset: kolor + font
            InnerEditor.SelectAll();
            InnerEditor.SelectionColor = InnerEditor.ForeColor;
            InnerEditor.SelectionFont = InnerEditor.Font;

            // strings
            ApplyRegex(P_Strings, Color.FromArgb(214, 157, 133), bold: false);

            // keywords
            ApplyRegex(P_Keywords, Color.FromArgb(86, 156, 214), bold: true);

            // comments
            ApplyRegex(P_Comments, Color.FromArgb(87, 166, 74), bold: false);

            // przywróć zaznaczenie
            InnerEditor.Select(selStart, selLength);

            SendMessage(InnerEditor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            InnerEditor.Invalidate();
        }

        private void ApplyRegex(string pattern, Color color, bool bold)
        {
            string text = InnerEditor.Text;
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            foreach (Match m in matches)
            {
                InnerEditor.Select(m.Index, m.Length);
                InnerEditor.SelectionColor = color;
                InnerEditor.SelectionFont = bold
                    ? new Font(InnerEditor.Font, FontStyle.Bold)
                    : new Font(InnerEditor.Font, FontStyle.Regular);
            }
        }

        private void FormatSqlCode()
        {
            try
            {
                string formatted = Regex.Replace(InnerEditor.Text, @"\s+", " ").Trim();

                string[] keywords = { "SELECT", "FROM", "WHERE", "GROUP BY", "ORDER BY" };
                foreach (var kw in keywords)
                {
                    formatted = Regex.Replace(
                        formatted,
                        $@"\b({kw})\b",
                        $"\r\n{kw}",
                        RegexOptions.IgnoreCase);
                }

                InnerEditor.Text = formatted;
                HighlightSyntax();
            }
            catch
            {
                // cicho – nie psujemy edytora
            }
        }

        // --- Pliki ---
        private void NewFile()
        {
            InnerEditor.Clear();
            _currentFilePath = null;
            IsModified = false;
        }

        private void OpenFile()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Pliki SQL|*.sql|Wszystkie pliki|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                InnerEditor.Text = File.ReadAllText(dlg.FileName);
                _currentFilePath = dlg.FileName;
                IsModified = false;
                HighlightSyntax();
            }
        }

        private void SaveFile(bool saveAs)
        {
            if (saveAs || string.IsNullOrEmpty(_currentFilePath))
            {
                using var dlg = new SaveFileDialog
                {
                    Filter = "Pliki SQL|*.sql|Wszystkie pliki|*.*"
                };
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                _currentFilePath = dlg.FileName;
            }

            File.WriteAllText(_currentFilePath!, InnerEditor.Text);
            IsModified = false;
        }

        // --- Gutter: numery linii ---
        private void Gutter_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.Clear(_gutter.BackColor);
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int firstVisibleLine = (int)SendMessage(
                InnerEditor.Handle,
                EM_GETFIRSTVISIBLELINE,
                IntPtr.Zero,
                IntPtr.Zero);

            int firstCharIndex = InnerEditor.GetFirstCharIndexFromLine(firstVisibleLine);
            if (firstCharIndex < 0) firstCharIndex = 0;

            Point firstPos = InnerEditor.GetPositionFromCharIndex(firstCharIndex);
            int y = firstPos.Y + 5;

            int lineHeight = TextRenderer.MeasureText("X", InnerEditor.Font).Height;
            int currentLine = firstVisibleLine;

            while (y < InnerEditor.Height && currentLine < InnerEditor.Lines.Length + 1)
            {
                if (currentLine < InnerEditor.Lines.Length)
                {
                    string text = (currentLine + 1).ToString();
                    var rect = new Rectangle(0, y, _gutter.Width - 5, lineHeight);
                    var sf = new StringFormat { Alignment = StringAlignment.Far };

                    using var brush = new SolidBrush(Color.DimGray);
                    e.Graphics.DrawString(text, InnerEditor.Font, brush, rect, sf);
                }

                y += lineHeight;
                currentLine++;
            }

            e.Graphics.DrawLine(Pens.DimGray, _gutter.Width - 1, 0, _gutter.Width - 1, _gutter.Height);
        }

        // --- RichEdit 5.0 wrapper ---
        private class ExRichTextBox : RichTextBox
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    try
                    {
                        LoadLibrary("MsftEdit.dll");
                        cp.ClassName = "RICHEDIT50W";
                    }
                    catch
                    {
                        // zostajemy przy standardowej klasie jeśli coś pójdzie nie tak
                    }

                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL || m.Msg == WM_PAINT)
                {
                    if (Parent?.Parent is SqlEditor editor)
                        editor._gutter.Invalidate();
                }
            }
        }
    }
}
