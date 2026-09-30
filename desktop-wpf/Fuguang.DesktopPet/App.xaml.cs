using System.IO;
using System.Threading;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Fuguang.DesktopPet;

public partial class App : System.Windows.Application
{
    private const string PipeName = "fuguang-desktop-pet";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "Fuguang.DesktopPet.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            NotifyExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "pet.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} | 未处理 WPF 异常 | {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(500);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new PetEventMessage { Command = "show" }));
        }
        catch (IOException)
        {
        }
        catch (TimeoutException)
        {
        }
    }
}