using System.Windows;
using System.Windows.Threading;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Başlangıç: yönetici izinli MFT yardımcısı mı, ikinci bir örnek mi (dosyaları açık olana iletir), yoksa asıl program mı.
/// Beklenmeyen hatalar günlüğe yazılır; arayüz hatalarında program açık kalır.
/// </summary>
public partial class App : Application
{
    readonly CancellationTokenSource _pipe = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;

        if (args.Length >= 3 && args[0] == MftHelper.Flag)
        {
            Shutdown(MftHelper.Run(args[1], args[2]));
            return;
        }

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Log.Error("Yakalanmamış hata, program kapanıyor", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Error("Gözlenmeyen görev hatası", ev.Exception);
            ev.SetObserved();
        };

        var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (!SingleInstance.TryAcquire(waitForPrevious: args.Contains(SingleInstance.RestartFlag))
            && SingleInstance.SendToRunning(files))
        {
            Shutdown();
            return;
        }

        var settings = Settings.Load();
        Loc.I.Set(Strings.Resolve(settings.Language));
        Log.Info($"Arşiv Ara {AppInfo.Version} başladı; veri klasörü: {AppPaths.DataDir}");
        int days = Math.Max(0, settings.TempRetentionDays);
        _ = Task.Run(() =>
        {
            TempFiles.Clean(TimeSpan.FromDays(days));
            Log.Prune();
        });

        var window = new MainWindow(settings, files);
        MainWindow = window;
        window.Show();
        SingleInstance.Listen(received => Dispatcher.InvokeAsync(() => window.ReceiveFromOtherInstance(received)), _pipe.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipe.Cancel();
        SingleInstance.Release();
        base.OnExit(e);
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Arayüzde beklenmeyen hata", e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show(MainWindow, Loc.F("ErrorText", e.Exception.Message, Log.CurrentFile), Loc.T("ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception) { /* pencere yoksa sadece günlük */ }
    }
}
