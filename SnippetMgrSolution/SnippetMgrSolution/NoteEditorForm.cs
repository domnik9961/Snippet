using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SnippetMgr.Models;

namespace SnippetMgr
{
    public class NoteEditorForm : Form
    {
        public EntryConfig NoteEntry { get; private set; }

        private TextBox _txtTitle;
        private TextBox _txtDesc;
        private TextBox _txtTags;
        private RichTextBox _rtbContent;
        private CheckBox _chkPinned;
        private Button _btnSave;
        private Button _btnCancel;

        public NoteEditorForm(EntryConfig entry = null)
        {
            InitializeComponent();

            if (entry != null)
            {
                NoteEntry = entry;

                _txtTitle.Text = entry.Title ?? string.Empty;
                _txtDesc.Text = entry.Description ?? string.Empty;
                _rtbContent.Text = entry.NoteText ?? entry.PasteText ?? string.Empty;
                _txtTags.Text = entry.GetTagsString();

                // 🔧 TU BYŁ BŁĄD: bool? -> bool
                _chkPinned.Checked = entry.Pinned ?? false;

                Text = "Edytuj Notatkę";
            }
            else
            {
                NoteEntry = new EntryConfig();
                Text = "Dodaj Notatkę";
            }

            // pierwsze pokolorowanie tego, co już jest w treści
            SqlSyntaxHighlighter.Highlight(_rtbContent);
        }

        private void InitializeComponent()
        {
            Text = "Notatka";
            StartPosition = FormStartPosition.CenterParent;
            Width = 800;
            Height = 600;
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 6
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // tytuł
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // opis
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // tagi
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // pinned
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // treść
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // przyciski

            // TYTUŁ
            var lblTitle = new Label { Text = "Tytuł:", AutoSize = true };
            _txtTitle = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };

            // OPIS
            var lblDesc = new Label { Text = "Opis:", AutoSize = true };
            _txtDesc = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };

            // TAGI
            var lblTags = new Label { Text = "Tagi (po przecinku):", AutoSize = true };
            _txtTags = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };

            // PINNED
            _chkPinned = new CheckBox
            {
                Text = "Przypięta notatka",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 5)
            };

            // TREŚĆ – RICH TEXT + KOLOROWANIE SQL
            _rtbContent = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10F),
                AcceptsTab = true,
                WordWrap = false
            };

            // 🔵 kolorowanie SQL przy każdej zmianie treści
            _rtbContent.TextChanged += (s, e) =>
            {
                SqlSyntaxHighlighter.Highlight(_rtbContent);
            };

            // PRZYCISKI
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            _btnSave = new Button
            {
                Text = "Zapisz",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };
            _btnSave.Click += (s, e) =>
            {
                SaveBack();
                DialogResult = DialogResult.OK;
                Close();
            };

            _btnCancel = new Button
            {
                Text = "Anuluj",
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };

            buttonPanel.Controls.Add(_btnSave);
            buttonPanel.Controls.Add(_btnCancel);

            // UKŁAD
            layout.Controls.Add(lblTitle, 0, 0);
            layout.Controls.Add(_txtTitle, 0, 1);

            layout.Controls.Add(lblDesc, 0, 2);
            layout.Controls.Add(_txtDesc, 0, 3);

            layout.Controls.Add(lblTags, 0, 4);
            layout.Controls.Add(_txtTags, 0, 5);

            layout.Controls.Add(_chkPinned, 0, 6);

            // trochę „ściemy” – wstawiamy wiersz z treścią jako osobny panel,
            // bo RowCount = 6, ale chcemy, żeby treść zajmowała resztę
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_rtbContent, 0, 7);

            layout.Controls.Add(buttonPanel, 0, 8);

            Controls.Add(layout);
        }

        private void SaveBack()
        {
            NoteEntry.Title = _txtTitle.Text;
            NoteEntry.Description = _txtDesc.Text;
            NoteEntry.NoteText = _rtbContent.Text;
            NoteEntry.PasteText = _rtbContent.Text;

            // bool -> bool? (OK, konwersja w górę)
            NoteEntry.Pinned = _chkPinned.Checked;

            // TAGI – parsowanie
            var tagList = _txtTags.Text
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();

            NoteEntry.TagsObject = tagList;
        }
    }
}
