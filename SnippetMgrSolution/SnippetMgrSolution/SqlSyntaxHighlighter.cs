using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SnippetMgr
{
    public static class SqlSyntaxHighlighter
    {
        // WinAPI do blokowania odświeżania (zapobiega migotaniu)
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0x000B;

        // Regexy dla SQL
        private static readonly string Keywords = @"\b(SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|SET|DELETE|DROP|CREATE|ALTER|TABLE|VIEW|PROCEDURE|FUNCTION|TRIGGER|USE|GO|INNER|LEFT|RIGHT|JOIN|ON|GROUP|BY|ORDER|HAVING|TOP|DISTINCT|UNION|AND|OR|NOT|IN|IS|NULL|LIKE|AS|WITH|IF|BEGIN|END|COMMIT|ROLLBACK|TRANSACTION)\b";
        private static readonly string DataTypes = @"\b(INT|BIGINT|SMALLINT|BIT|DECIMAL|NUMERIC|FLOAT|REAL|DATETIME|DATE|VARCHAR|NVARCHAR|CHAR|NCHAR|TEXT|XML|UNIQUEIDENTIFIER)\b";
        private static readonly string Functions = @"\b(GETDATE|ISNULL|COALESCE|CAST|CONVERT|COUNT|SUM|MIN|MAX|AVG|LEN|SUBSTRING|REPLACE|DATEADD|DATEDIFF)\b";
        private static readonly string Comments = @"--.*";
        private static readonly string Strings = @"'([^']|'')*'";

        public static void Highlight(RichTextBox rtb)
        {
            if (string.IsNullOrWhiteSpace(rtb.Text)) return;

            // 1. Stop painting
            SendMessage(rtb.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);

            int selStart = rtb.SelectionStart;
            int selLength = rtb.SelectionLength;

            try
            {
                // 2. Reset stylów
                rtb.SelectAll();
                rtb.SelectionColor = Color.Black;
                rtb.SelectionFont = new Font("Consolas", 10, FontStyle.Regular);

                // 3. Kolorowanie
                ApplyRegex(rtb, Strings, Color.FromArgb(163, 21, 21)); // Czerwony (Stringi)
                ApplyRegex(rtb, Keywords, Color.Blue, true);             // Niebieski (Słowa kluczowe)
                ApplyRegex(rtb, DataTypes, Color.Teal);                  // Morski (Typy danych)
                ApplyRegex(rtb, Functions, Color.Magenta);               // Fiolet (Funkcje)
                ApplyRegex(rtb, Comments, Color.Green);                  // Zielony (Komentarze)
            }
            finally
            {
                // 4. Restore painting
                rtb.Select(selStart, selLength);
                rtb.SelectionColor = Color.Black;
                SendMessage(rtb.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                rtb.Invalidate();
            }
        }

        private static void ApplyRegex(RichTextBox rtb, string pattern, Color color, bool bold = false)
        {
            foreach (Match match in Regex.Matches(rtb.Text, pattern, RegexOptions.IgnoreCase))
            {
                rtb.Select(match.Index, match.Length);
                rtb.SelectionColor = color;
                if (bold) rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            }
        }
    }
}