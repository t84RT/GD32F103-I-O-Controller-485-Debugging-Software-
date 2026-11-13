using System;
using System.IO;
using System.Windows.Forms;
namespace IOModbusTester
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--verify-startup")
            {
                if (args.Length != 2) return 2;
                // Packaging smoke check: create native UI resources without showing
                // a window, opening a serial port, or issuing any device command.
                try
                {
                    using (var form = new MainForm())
                    {
                        var handle = form.Handle;
                        form.PerformLayout();
                        File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new {
                            Success = true, Runtime = Environment.Version.ToString(),
                            Title = form.Text, SerialPortOpened = false
                        }));
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1], ex.ToString());
                    return 1;
                }
            }
            Application.Run(new MainForm());
            return 0;
        }
    }
}
