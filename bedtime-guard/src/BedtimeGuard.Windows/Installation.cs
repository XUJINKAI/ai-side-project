using BedtimeGuard.Core;
using System.Security.Principal;

namespace BedtimeGuard.Windows;

public sealed record Installation(string UserSid, string AppPath);

public static class Paths
{
    public const string ServiceName = "BedtimeGuard";
    public const string PipeName = "BedtimeGuard.v1";
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BedtimeGuard");
    public static string Install => Path.Combine(Data, "installation.json");
    public static string State => Path.Combine(Data, "state.json");
    public static string Lease => Path.Combine(Data, "task-manager-lease.json");
    public static string Paused => Path.Combine(Data, "paused");
    public static Installation ReadInstallation()
    {
        var install = JsonStorage.Read<Installation>(Install);
        _ = new SecurityIdentifier(install.UserSid);
        if (!Path.IsPathFullyQualified(install.AppPath) || !File.Exists(install.AppPath))
            throw new InvalidDataException("Installed UI executable is missing.");
        return install;
    }
    public static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) && !identity.IsSystem)
            throw new UnauthorizedAccessException("请以管理员身份运行恢复工具。");
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Data);
            var file = Path.Combine(Data, "service.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
