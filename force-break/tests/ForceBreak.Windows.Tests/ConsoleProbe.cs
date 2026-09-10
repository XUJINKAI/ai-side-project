using ForceBreak.Windows;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

internal static class ConsoleProbe
{
    public static int Run()
    {
        FreeConsole();
        if (!AllocConsole()) return 1;
        try
        {
            ShowWindow(GetConsoleWindow(), 0);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var console = GetStdHandle(-11);
            foreach (var codePage in new uint[] { 936, 65001 })
            {
                if (!SetConsoleOutputCP(codePage)) return 2;
                SetConsoleCursorPosition(console, new Coord());
                using (var writer = CommandOutput.Open()) writer!.WriteLine("显示帮助");
                var content = new StringBuilder(100);
                if (!ReadConsoleOutputCharacterW(console, content, 8, new Coord(), out _)) return 3;
                if (!content.ToString().Replace("\0", "").Replace(" ", "").Contains("显示帮助")) return 4;
                using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
                SetStdHandle(-11, pipe.ClientSafePipeHandle.DangerousGetHandle());
                try
                {
                    using (var writer = CommandOutput.Open()) writer!.WriteLine("显示帮助：休息 30 分钟");
                    using var reader = new StreamReader(pipe, Encoding.GetEncoding((int)codePage), false, 1024, leaveOpen: true);
                    if (reader.ReadLine() != "显示帮助：休息 30 分钟") return 5;
                }
                finally { SetStdHandle(-11, console); }
                if (GetConsoleOutputCP() != codePage) return 6;
            }
            return 0;
        }
        finally { FreeConsole(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleOutputCP(uint codePage);
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCursorPosition(IntPtr handle, Coord position);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool ReadConsoleOutputCharacterW(IntPtr handle, StringBuilder buffer, uint length, Coord position, out uint read);
}
