using BedtimeGuard.Core;
using BedtimeGuard.Windows;
using Microsoft.Win32;
using System.Security.Principal;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows is required."); return 1; }
var folder = Path.Combine(Path.GetTempPath(), "BedtimeGuard-" + Guid.NewGuid());
var subkey = @"Software\BedtimeGuard.Tests\" + Guid.NewGuid();
var sid = WindowsIdentity.GetCurrent().User!.Value;
Directory.CreateDirectory(folder);
var journal = Path.Combine(folder, "lease.json");
var policy = new TaskManagerPolicy(journal, subkey, () => false);
var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
try
{
    using var key = Registry.CurrentUser.CreateSubKey(subkey, true);
    var now = DateTimeOffset.UtcNow;
    policy.Apply(sid, now.AddHours(1), now);
    Check(key.GetValue("DisableTaskMgr") is 1 && File.Exists(journal), "journal accompanies policy write");
    policy.Restore(onlyExpired: true);
    Check(key.GetValue("DisableTaskMgr") is 1, "fresh unexpired lease retained");
    policy.Restore();
    Check(key.GetValue("DisableTaskMgr") is null && !File.Exists(journal), "original absence restored");

    key.SetValue("DisableTaskMgr", 0, RegistryValueKind.DWord);
    policy.Apply(sid, now.AddHours(1), now);
    policy.Restore();
    Check(key.GetValue("DisableTaskMgr") is 0, "original explicit zero preserved");

    policy.Apply(sid, now.AddHours(1), now);
    key.SetValue("DisableTaskMgr", 2, RegistryValueKind.DWord);
    policy.Restore();
    Check(key.GetValue("DisableTaskMgr") is 2 && !File.Exists(journal), "external change not overwritten");
    key.DeleteValue("DisableTaskMgr");

    // Simulate process termination after write-ahead journal + registry write, before any final bookkeeping.
    JsonStorage.Write(journal, new PolicyLease(sid, false, 0, now.AddMinutes(-1), now));
    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
    new TaskManagerPolicy(journal, subkey, () => false).Restore(onlyExpired: true);
    Check(key.GetValue("DisableTaskMgr") is null, "expired crash journal recovered by fresh process");

    JsonStorage.Write(journal, new PolicyLease(sid, false, 0, now.AddHours(1), now.AddMinutes(-3)));
    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
    policy.Restore(onlyExpired: true);
    Check(key.GetValue("DisableTaskMgr") is null, "stale service heartbeat releases policy");

    // Crash after writing journal but BEFORE registry mutation.
    JsonStorage.Write(journal, new PolicyLease(sid, false, 0, now.AddMinutes(-1), now));
    policy.Restore(onlyExpired: true);
    Check(key.GetValue("DisableTaskMgr") is null && !File.Exists(journal), "prepared-only journal is idempotent");

    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
    var rejected = false;
    try { policy.Apply(sid, now.AddHours(1), now); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected && !File.Exists(journal), "existing policy is not taken over");
    key.DeleteValue("DisableTaskMgr");

    rejected = false;
    try { new TaskManagerPolicy(journal, subkey, () => true).Apply(sid, now.AddHours(1), now); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected && key.GetValue("DisableTaskMgr") is null, "managed device remains unchanged");

    File.WriteAllText(journal, "corrupt");
    rejected = false;
    try { policy.Restore(); } catch (System.Text.Json.JsonException) { rejected = true; }
    Check(rejected && File.Exists(journal), "corrupt recovery journal is retained for repair");
    Console.WriteLine($"{count} Windows policy checks passed; real Task Manager policy was never touched.");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(subkey, false);
    Directory.Delete(folder, true);
}
