using ForceBreak.Core;
using ForceBreak.Windows;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Diagnostics;

namespace ForceBreak.Service;

public sealed class GuardService : ServiceBase
{
    private readonly object gate = new();
    private readonly TaskManagerPolicy policy = new();
    private CancellationTokenSource? stop;
    private Task? runner;
    private Installation installation = null!;
    private PlannerState state = null!;
    private Status status = null!;
    private string? savedJson;
    private string? lastError;
    private Sessions sessions = null!;
    private long lastTick;
    private long lastSave;
    private readonly Dictionary<int, ActivityLease> activities = new();
    private static TimeSpan MonotonicNow => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    public GuardService()
    {
        ServiceName = Paths.ServiceName;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        installation = Paths.ReadInstallation();
        sessions = new Sessions(installation);
        if (File.Exists(Paths.Paused)) throw new InvalidOperationException("管理员恢复模式已启用，请在界面中点击重新启用。");
        try
        {
            // Missing state is an error, not a silent reset of a frozen night.
            state = JsonStorage.Read<PlannerState>(Paths.State);
            if (state.Version != 1) throw new InvalidDataException("Unknown state version.");
            state.Schedule.Validate();
            lock (gate) Refresh();
        }
        catch
        {
            try { policy.Restore(); } catch (Exception e) { Paths.Log(e.Message); }
            throw;
        }
        stop = new();
        runner = Run(stop.Token);
        Paths.Log("Service started.");
    }

    private async Task Run(CancellationToken token)
    {
        try { await Task.WhenAll(Clock(token), Serve(token), Serve(token), Serve(token), Serve(token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Paths.Log(error.ToString());
            // Nonzero termination lets SCM apply the documented bounded recovery policy.
            Environment.Exit(1);
        }
    }

    private async Task Clock(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var supervision = 0;
        do
        {
            lock (gate)
            {
                if (File.Exists(Paths.Paused))
                {
                    policy.Restore();
                    status = new(state.Schedule, Phase.Disabled, null, null, null, false, Error: "管理员已暂停限制。");
                }
                else Refresh();
            }
            if (++supervision % 5 == 1 && !File.Exists(Paths.Paused))
            {
                try { sessions.EnsureApp(); }
                catch (Exception e) { ReportError("Session: " + e.Message); }
            }
        } while (await timer.WaitForNextTickAsync(token));
    }

    private void Refresh(Schedule? replacement = null, int? manualMinutes = null)
    {
        var candidate = JsonStorage.Clone(state);
        var planner = new Planner(candidate, () => RandomNumberGenerator.GetInt32(1_000_000));
        var now = DateTimeOffset.UtcNow;
        var stamp = Stopwatch.GetTimestamp();
        var elapsed = lastTick == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(lastTick, stamp);
        lastTick = stamp;
        // Sleep/resume or a stalled process does not turn a wall-clock gap into worked time.
        if (elapsed > TimeSpan.FromSeconds(5)) elapsed = TimeSpan.Zero;
        var unlocked = false;
        if (candidate.Schedule.Breaks.Enabled && candidate.Break.Frozen is null)
            try { unlocked = sessions.IsTargetUnlocked(); } catch (Exception e) { ReportError(e.Message); }
        var active = !candidate.Schedule.Behavior.DetectActivity;
        if (!active && unlocked)
            foreach (var pair in activities)
                if (pair.Value.IsActive(MonotonicNow, candidate.Schedule.Behavior.IdleMinutes) && sessions.IsTargetUnlocked(pair.Key))
                { active = true; break; }
        var night = planner.Tick(now);
        var rest = BreakPlanner.Tick(candidate.Break, candidate.Schedule.Breaks, now,
            unlocked && active ? elapsed : TimeSpan.Zero, night.Phase == Phase.Restricted, candidate.Schedule.DisableTaskManager, candidate.Schedule.Behavior);
        if (replacement is not null)
        {
            night = planner.Update(replacement, now);
            rest = BreakPlanner.Tick(candidate.Break, candidate.Schedule.Breaks, now, TimeSpan.Zero,
                night.Phase == Phase.Restricted, candidate.Schedule.DisableTaskManager, candidate.Schedule.Behavior);
        }
        if (manualMinutes is { } minutes)
        {
            BreakPlanner.StartManual(candidate.Break, now, minutes, candidate.Schedule, night.Phase);
            rest = BreakPlanner.Tick(candidate.Break, candidate.Schedule.Breaks, now, TimeSpan.Zero,
                false, candidate.Schedule.DisableTaskManager, candidate.Schedule.Behavior);
        }
        var paused = candidate.Schedule.Breaks.Enabled && candidate.Break.Frozen is null && (!unlocked || !active);
        var next = BreakPlanner.Combine(night, rest) with
        {
            WorkTimerPaused = paused,
            ActivityMessage = paused ? "工作计时已暂停：空闲、会话锁定或输入检测尚未就绪。" :
                candidate.Schedule.Behavior.DetectActivity ? "输入检测已开启；承诺前仅在近期有键鼠操作时累计。" : "输入检测未开启；承诺前按解锁时间累计。"
        };
        var json = JsonSerializer.Serialize(candidate, JsonStorage.Options);
        var urgent = replacement is not null || candidate.Frozen != state.Frozen || candidate.Break.Frozen != state.Break.Frozen;
        if (json != savedJson && (savedJson is null || urgent || Stopwatch.GetElapsedTime(lastSave) >= TimeSpan.FromSeconds(15)))
        {
            JsonStorage.Write(Paths.State, candidate); // Commit before exposing or enforcing changes.
            savedJson = json;
            lastSave = stamp;
        }
        state = candidate;
        status = next;
        try
        {
            if (next.TaskManagerRequested && next.PolicyUntil is { } release)
                policy.Apply(installation.UserSid, release, now);
            else policy.Restore();
            status = status with { PolicyMessage = next.TaskManagerRequested ? "任务管理器策略值已核验；不关闭已打开的实例" : "任务管理器不受本工具限制" };
            lastError = null;
        }
        catch (Exception error)
        {
            // Locking still works when optional policy is unavailable. Never pretend it succeeded.
            status = status with { PolicyMessage = "策略未就绪：" + error.Message };
            ReportError(error.Message);
        }
    }

    private void ReportError(string message)
    {
        if (lastError != message) Paths.Log(message);
        lastError = message;
    }

    private async Task Serve(CancellationToken token)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(installation.UserSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        while (!token.IsCancellationRequested)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(Paths.PipeName, PipeDirection.InOut, 4,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            await pipe.WaitForConnectionAsync(token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var request = await Wire.Read<Request>(pipe, deadline.Token);
                var authorized = false;
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent(true);
                    authorized = identity?.User?.Value == installation.UserSid;
                });
                Response response;
                lock (gate)
                {
                    if (!authorized) response = new(false, Error: "用户身份不匹配。");
                    else if (File.Exists(Paths.Paused)) response = new(false, Error: "管理员已暂停，请在界面中点击重新启用。");
                    else
                    {
                        try
                        {
                            switch (request.Command)
                            {
                                case "activity" when request.Activity is not null:
                                    if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId) || sessionId == 0)
                                        throw new InvalidOperationException("无法确认输入检测会话。");
                                    // Flush elapsed time using the previous report before accepting new activity.
                                    Refresh();
                                    if (!activities.TryGetValue((int)sessionId, out var lease)) activities[(int)sessionId] = lease = new();
                                    lease.Update(request.Activity, MonotonicNow);
                                    Refresh(); response = new(true, status); break;
                                case "rest" when request.RestMinutes is { } minutes:
                                    Refresh(manualMinutes: minutes); response = new(true, status); break;
                                case "status": Refresh(); response = new(true, status); break;
                                case "save" when request.Schedule is not null:
                                    Refresh(request.Schedule); response = new(true, status); break;
                                default: response = new(false, Error: "不支持此操作。"); break;
                            }
                        }
                        catch (Exception error) { response = new(false, Error: error.Message); }
                    }
                }
                await Wire.Write(pipe, response, deadline.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint sessionId);

    protected override void OnStop()
    {
        stop?.Cancel();
        try { runner?.GetAwaiter().GetResult(); }
        finally
        {
            try { if (state is not null) JsonStorage.Write(Paths.State, state); } catch (Exception e) { Paths.Log(e.Message); }
            try { policy.Restore(); } catch (Exception error) { Paths.Log(error.Message); }
            stop?.Dispose();
            Paths.Log("Service stopped; requested policy restoration.");
        }
    }
    protected override void OnShutdown() => OnStop();
}
