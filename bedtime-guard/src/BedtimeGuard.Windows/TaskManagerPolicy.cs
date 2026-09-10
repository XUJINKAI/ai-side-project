using BedtimeGuard.Core;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace BedtimeGuard.Windows;

public sealed record PolicyLease(string UserSid, bool Existed, int OriginalValue,
    DateTimeOffset ExpiresAt, DateTimeOffset HeartbeatAt);

/// <summary>Write-ahead journal: a crash between registry write and bookkeeping is recoverable.</summary>
public sealed class TaskManagerPolicy
{
    private readonly string subkey;
    private readonly string journal;
    private readonly Func<bool> isManaged;
    private readonly Action<string> log;
    private readonly Func<bool> isPaused;
    private const string ValueName = "DisableTaskMgr";

    public TaskManagerPolicy() : this(Paths.Lease,
        @"Software\Microsoft\Windows\CurrentVersion\Policies\System", IsManaged)
    { log = Paths.Log; isPaused = () => File.Exists(Paths.Paused); }

    // Test seam is internal; production has one fixed policy path and protected journal.
    internal TaskManagerPolicy(string journal, string subkey, Func<bool> isManaged)
    { this.journal = journal; this.subkey = subkey; this.isManaged = isManaged; log = _ => { }; isPaused = () => false; }

    public void Apply(string sid, DateTimeOffset expires, DateTimeOffset now)
    {
        using var guard = Acquire();
        if (File.Exists(journal))
        {
            var existing = JsonStorage.Read<PolicyLease>(journal);
            if (existing.UserSid != sid || existing.ExpiresAt != expires)
            {
                RestoreCore(existing);
            }
            else
            {
                if (now - existing.HeartbeatAt >= TimeSpan.FromSeconds(30))
                    JsonStorage.Write(journal, existing with { HeartbeatAt = now });
                return;
            }
        }
        if (isManaged()) throw new InvalidOperationException("检测到域或 MDM 管理，未修改任务管理器策略。");
        // Never create a phantom HKU hive when the target user is logged out.
        using var hive = Registry.Users.OpenSubKey(sid, true)
            ?? throw new InvalidOperationException("用户注册表尚未加载，稍后重试。");
        using var key = hive.CreateSubKey(subkey, true);
        var existed = key.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase);
        var original = 0;
        if (existed)
        {
            if (key.GetValueKind(ValueName) != RegistryValueKind.DWord)
                throw new InvalidOperationException("任务管理器已有非标准策略，未覆盖。");
            original = (int)key.GetValue(ValueName)!;
            if (original != 0) throw new InvalidOperationException("任务管理器已由其他设置禁用，未接管。");
        }
        JsonStorage.Write(journal, new PolicyLease(sid, existed, original, expires, now));
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        key.Flush();
        log("Task Manager policy enabled for the selected user.");
    }

    public void Restore(bool onlyExpired = false)
    {
        using var guard = Acquire();
        if (!File.Exists(journal)) return;
        var lease = JsonStorage.Read<PolicyLease>(journal);
        var now = DateTimeOffset.UtcNow;
        if (onlyExpired && now < lease.ExpiresAt && now - lease.HeartbeatAt < TimeSpan.FromMinutes(2)
            && !isPaused()) return;
        RestoreCore(lease);
    }

    private void RestoreCore(PolicyLease lease)
    {
        using var hive = Registry.Users.OpenSubKey(lease.UserSid, true)
            ?? throw new InvalidOperationException("用户注册表未加载，保留恢复记录，登录后重试。");
        using var key = hive.OpenSubKey(subkey, true);
        if (key is not null)
        {
            // Do not clobber an intervening external change; restore only our own value.
            if (key.GetValue(ValueName) is int current && current == 1 && key.GetValueKind(ValueName) == RegistryValueKind.DWord)
            {
                if (lease.Existed) key.SetValue(ValueName, lease.OriginalValue, RegistryValueKind.DWord);
                else key.DeleteValue(ValueName, false);
                key.Flush();
            }
        }
        File.Delete(journal);
        log("Task Manager policy lease restored/released.");
    }

    private FileStream Acquire() => new(journal + ".lock",
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static bool IsManaged()
    {
        if (NetGetJoinInformation(null, out var name, out var status) != 0)
            throw new InvalidOperationException("无法确认设备管理状态，未修改策略。");
        try { if (status == 3) return true; }
        finally { if (name != IntPtr.Zero) NetApiBufferFree(name); }
        using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        if (enrollments is null) return false;
        foreach (var entry in enrollments.GetSubKeyNames())
        {
            using var key = enrollments.OpenSubKey(entry);
            if (key?.GetValue("EnrollmentState") is int value && value > 0) return true;
        }
        return false;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr name, out int status);
    [DllImport("netapi32.dll")] private static extern int NetApiBufferFree(IntPtr buffer);
}
