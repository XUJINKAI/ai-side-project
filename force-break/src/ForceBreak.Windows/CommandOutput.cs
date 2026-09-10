using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ForceBreak.Windows;

/// <summary>Unicode console output; pipes follow the receiving console's code page.</summary>
public static class CommandOutput
{
    public static TextWriter? Open()
    {
        var original = GetStdHandle(-11);
        var kind = GetFileType(original);
        var redirected = kind is 1 or 3;
        // Attaching may replace standard handles. Keep the original redirect handle.
        if (!GetConsoleMode(original, out _)) AttachConsole(uint.MaxValue);
        if (redirected)
        {
            var codePage = kind == 3 ? GetConsoleOutputCP() : 65001;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = codePage is 0 or 65001 ? new UTF8Encoding(false) : Encoding.GetEncoding((int)codePage);
            return new StreamWriter(new FileStream(new SafeFileHandle(original, ownsHandle: false), FileAccess.Write), encoding) { AutoFlush = true };
        }
        var handle = GetStdHandle(-11);
        return GetConsoleMode(handle, out _) ? new UnicodeConsoleWriter(handle) : null;
    }

    private sealed class UnicodeConsoleWriter(IntPtr handle) : TextWriter
    {
        public override Encoding Encoding => Encoding.Unicode;
        public override void Write(string? value)
        {
            if (value is null) return;
            for (var offset = 0; offset < value.Length;)
            {
                var chunk = value.Substring(offset, Math.Min(4096, value.Length - offset));
                if (!WriteConsoleW(handle, chunk, (uint)chunk.Length, out var written, IntPtr.Zero) || written == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                offset += (int)written;
            }
        }
        public override void WriteLine(string? value) => Write(value + "\r\n");
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WriteConsoleW(IntPtr handle, string text, uint count, out uint written, IntPtr reserved);
}
