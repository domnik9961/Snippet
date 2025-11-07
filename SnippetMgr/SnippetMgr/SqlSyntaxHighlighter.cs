using System;
using System.Drawing;
using System.Windows.Forms;

namespace SnippetMgr
{
    /// <summary>
    /// Proste, ale rozbudowane podświetlanie T-SQL w RichTextBox:
    /// - słowa kluczowe (SELECT, FROM, INNER JOIN, PRIMARY KEY CLUSTERED, ...)
    /// - typy danych (int, decimal, nvarchar...)
    /// - obiekty systemowe (SYS, INFORMATION_SCHEMA...)
    /// - funkcje / zmienne globalne
    /// - identyfikatory w nawiasach []
    /// - stringi '...' oraz komentarze -- ...
    ///
    /// Sposób użycia:
    ///     var highlighter = new SqlSyntaxHighlighter();
    ///     highlighter.Attach(richTextBox1);
    /// </summary>
    public class SqlSyntaxHighlighter
    {
        private bool _isHighlighting;
        private RichTextBox _rtb;

        // słowa kluczowe T-SQL (podstawowe + DDL + DML)
        private static readonly string[] SqlKeywords = new[]
        {
            "select","from","where",
            "inner","join","left","right","full","outer","cross","apply","on",
            "insert","into","values","update","set","delete","merge","output",
            "create","alter","drop","truncate",
            "table","view","procedure","function","trigger","index","constraint","schema",
            "if","else","begin","end","while","return","declare","set",
            "with","as","top","distinct","order","by","group","having",
            "union","all","except","intersect",
            "exists","in","between","like","is","null","not","and","or",
            "case","when","then","end",
            "primary","key","clustered","nonclustered","unique","foreign","references","default","check",
            "identity","database","use",
            "exec","execute","grant","revoke","deny",
            "cursor","fetch","open","close","deallocate",
            "transaction","commit","rollback","save",
            "try","catch","throw","cast","convert","collate","backup","restore"
        };

        // typy danych
        private static readonly string[] SqlDataTypes = new[]
        {
            "int","bigint","smallint","tinyint",
            "decimal","numeric","money","smallmoney",
            "float","real",
            "bit",
            "date","datetime","datetime2","smalldatetime","time","datetimeoffset",
            "char","varchar","nchar","nvarchar","text","ntext",
            "binary","varbinary","image",
            "uniqueidentifier",
            "xml"
        };

        // obiekty systemowe / widoki katalogowe
        private static readonly string[] SqlSystemObjects = new[]
        {
            "sys","sys.objects","sys.tables","sys.columns","sys.schemas","sys.databases",
            "sys.indexes","sys.index_columns",
            "sys.foreign_keys","sys.foreign_key_columns",
            "sys.types","sys.views","sys.procedures","sys.parameters",
            "sys.default_constraints","sys.check_constraints","sys.identity_columns",
            "information_schema","information_schema.tables","information_schema.columns",
            "information_schema.views","information_schema.routines"
        };

        // funkcje wbudowane / zmienne globalne
        private static readonly string[] SqlFunctions = new[]
        {
            "getdate","sysdatetime","sysdatetimeoffset","sysutcdatetime","current_timestamp",
            "isnull","coalesce","nullif",
            "len","substring","left","right","upper","lower","ltrim","rtrim","replace","stuff",
            "isnumeric",
            "row_number","rank","dense_rank","ntile",
            "count","sum","min","max","avg",
            "newid","newsequentialid",
            "scope_identity","@@identity","@@rowcount","@@error","@@trancount","@@version",
            "host_name","suser_sname","db_name","object_id","object_name"
        };

        /// <summary>
        /// Podłącza podany RichTextBox do mechanizmu podświetlania.
        /// </summary>
        public void Attach(RichTextBox richTextBox)
        {
            if (_rtb != null)
            {
                _rtb.TextChanged -= Rtb_TextChanged;
            }

            _rtb = richTextBox;

            if (_rtb != null)
            {
                _rtb.TextChanged += Rtb_TextChanged;
                // dodatkowo można podświetlić istniejący tekst
                Highlight();
            }
        }

        private void Rtb_TextChanged(object sender, EventArgs e)
        {
            Highlight();
        }

        /// <summary>
        /// Główna metoda podświetlania.
        /// Chroni się przed rekurencją (flaga _isHighlighting).
        /// </summary>
        public void Highlight()
        {
            if (_rtb == null) return;
            if (_isHighlighting) return;

            _isHighlighting = true;

            int selStart = _rtb.SelectionStart;
            int selLength = _rtb.SelectionLength;

            _rtb.SuspendLayout();

            // reset kolorów
            _rtb.SelectAll();
            _rtb.SelectionColor = Color.Black;

            string text = _rtb.Text ?? string.Empty;

            // 1) słowa kluczowe – niebieski
            ColorWords(SqlKeywords, Color.Blue, text);

            // 2) typy danych – ciemny turkus
            ColorWords(SqlDataTypes, Color.DarkCyan, text);

            // 3) obiekty systemowe – fiolet
            ColorWords(SqlSystemObjects, Color.Purple, text);

            // 4) funkcje / zmienne – ciemny magenta
            ColorWords(SqlFunctions, Color.DarkMagenta, text);

            // 5) identyfikatory w nawiasach [] – brązowy
            ColorBracketIdentifiers(text);

            // 6) stringi '...' – bordowy
            ColorStringLiterals(text);

            // 7) komentarze -- ... – zielony
            ColorLineComments(text);

            // przywrócenie zaznaczenia użytkownika
            _rtb.Select(selStart, selLength);
            _rtb.ResumeLayout();

            _isHighlighting = false;
        }

        #region Różne reguły kolorowania

        /// <summary>
        /// Koloruje podane słowa (case-insensitive) tylko gdy występują jako całe identyfikatory.
        /// </summary>
        private void ColorWords(string[] words, Color color, string text)
        {
            foreach (var word in words)
            {
                int index = 0;
                while (index < text.Length)
                {
                    index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) break;

                    // sprawdzamy, czy to całe "słowo" (np. select, a nie myselect)
                    bool isWordStart = (index == 0 || !IsSqlIdentChar(text[index - 1]));
                    int end = index + word.Length;
                    bool isWordEnd = (end >= text.Length || !IsSqlIdentChar(text[end]));

                    if (isWordStart && isWordEnd)
                    {
                        _rtb.Select(index, word.Length);
                        _rtb.SelectionColor = color;
                    }

                    index = end;
                }
            }
        }

        /// <summary>
        /// Koloruje [identyfikatory] w nawiasach kwadratowych.
        /// </summary>
        private void ColorBracketIdentifiers(string text)
        {
            int idx = 0;
            while (idx < text.Length)
            {
                int start = text.IndexOf('[', idx);
                if (start < 0) break;
                int end = text.IndexOf(']', start + 1);
                if (end < 0) break;

                _rtb.Select(start, end - start + 1);
                _rtb.SelectionColor = Color.Brown;

                idx = end + 1;
            }
        }

        /// <summary>
        /// Koloruje literały znakowe 'tekst' z obsługą '' (apostrof w środku).
        /// </summary>
        private void ColorStringLiterals(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int start = text.IndexOf('\'', i);
                if (start < 0) break;
                int end = start + 1;

                while (end < text.Length)
                {
                    if (text[end] == '\'')
                    {
                        // '' w środku
                        if (end + 1 < text.Length && text[end + 1] == '\'')
                        {
                            end += 2;
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                    end++;
                }

                if (end >= text.Length) end = text.Length - 1;

                _rtb.Select(start, end - start + 1);
                _rtb.SelectionColor = Color.Maroon;

                i = end + 1;
            }
        }

        /// <summary>
        /// Koloruje komentarze jednoliniowe zaczynające się od "--".
        /// </summary>
        private void ColorLineComments(string text)
        {
            int lineStart = 0;
            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = text.Length;

                int commentStart = text.IndexOf("--", lineStart, lineEnd - lineStart, StringComparison.Ordinal);
                if (commentStart >= 0)
                {
                    _rtb.Select(commentStart, lineEnd - commentStart);
                    _rtb.SelectionColor = Color.Green;
                }

                lineStart = lineEnd + 1;
            }
        }

        /// <summary>
        /// Sprawdza, czy znak jest częścią identyfikatora SQL (literą, cyfrą lub '_').
        /// </summary>
        private bool IsSqlIdentChar(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_';
        }

        #endregion
    }
}
