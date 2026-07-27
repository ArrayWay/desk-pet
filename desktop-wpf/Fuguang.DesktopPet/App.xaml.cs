using System.IO;
using System.Threading;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;

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
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
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