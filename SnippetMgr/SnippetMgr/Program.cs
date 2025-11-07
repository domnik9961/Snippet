using System;
using System.Windows.Forms;

namespace SnippetMgr
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // TU MUSI BYÆ SqlScriptForm, a nie Form1:
            Application.Run(new Form1());
        }
    }
}
