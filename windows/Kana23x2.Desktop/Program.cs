namespace Kana23x2.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool smokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        using var singleton = new Mutex(true, @"Local\Kana23x2.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("Kana23x2 已在运行，请查看系统托盘。", "Kana23x2");
            return;
        }
        ApplicationConfiguration.Initialize();
        try
        {
            if (smokeTest && System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>() !=
                (IntPtr.Size == 8 ? 40 : 28))
                throw new InvalidOperationException("Unexpected Win32 INPUT structure size.");
            using var application = new TrayApplication();
            using var smokeTimer = new System.Windows.Forms.Timer { Interval = 750 };
            if (smokeTest)
            {
                smokeTimer.Tick += (_, _) => application.ExitThread();
                smokeTimer.Start();
            }
            Application.Run(application);
        }
        catch (Exception error)
        {
            if (smokeTest)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
                return;
            }
            MessageBox.Show($"Kana23x2 无法运行：\n{error.Message}", "Kana23x2",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
