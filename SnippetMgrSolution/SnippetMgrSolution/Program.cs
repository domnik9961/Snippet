using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnippetMgr.Models;
using SnippetMgr.Services;

namespace SnippetMgr // WA¯NE: Namespace musi byæ taki sam jak w Form1.cs
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Inicjalizacja ustawieñ WinForms (DPI, czcionki)
            ApplicationConfiguration.Initialize();

            // 1. Konfiguracja kontenera DI (Wstrzykiwanie Zale¿noœci)
            // To tutaj "sk³adamy" aplikacjê w ca³oœæ.
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Rejestracja naszych serwisów
                    services.AddSingleton<IConfigService, JsonConfigService>();
                    services.AddSingleton<ISqlService, SqlService>();
                    services.AddSingleton<ICommandService, CommandService>();

                    // Rejestracja g³ównego okna (Form1 nie jest tworzone przez "new", tylko przez kontener)
                    services.AddTransient<Form1>();
                })
                .ConfigureLogging(log =>
                {
                    // Konfiguracja logowania (opcjonalnie, np. do Output w Visual Studio)
                    log.ClearProviders();
                    log.AddDebug();
                    log.AddConsole();
                })
                .Build();

            // 2. Obs³uga globalnych b³êdów (¿eby aplikacja nie znika³a bez œladu)
            Application.ThreadException += (s, e) => MessageBox.Show($"Wyst¹pi³ b³¹d aplikacji:\n{e.Exception.Message}", "B³¹d Krytyczny", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => MessageBox.Show($"Wyst¹pi³ b³¹d domeny:\n{((Exception)e.ExceptionObject).Message}", "B³¹d Krytyczny", MessageBoxButtons.OK, MessageBoxIcon.Error);

            try
            {
                // 3. Pobranie Form1 z serwisu i uruchomienie
                // U¿ywamy host.Services.GetRequiredService, aby wstrzykn¹æ wszystkie zale¿noœci do Form1
                var mainForm = host.Services.GetRequiredService<Form1>();
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie uda³o siê uruchomiæ aplikacji. SprawdŸ plik Program.cs.\nB³¹d: {ex.Message}");
            }
        }
    }
}