using BedtimeGuard.Core;
using BedtimeGuard.Windows;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;

namespace BedtimeGuard.Service;

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

    public GuardService()
    {
        ServiceName = Paths.ServiceName;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        installation = Paths.ReadInstallation();
        if (File.Exists(Paths.Paused)) throw new InvalidOperationException("管理员恢复模式已启用，请运行 Resume.ps1。");
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
        var sessions = new Sessions(installation);
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

    private void Refresh(Schedule? replacement = null)
    {
        var candidate = JsonStorage.Clone(state);
        var planner = new Planner(candidate, () => RandomNumberGenerator.GetInt32(1_000_000));
        var now = DateTimeOffset.UtcNow;
        var next = replacement is null ? planner.Tick(now) : planner.Update(replacement, now);
        var json = JsonSerializer.Serialize(candidate, JsonStorage.Options);
        if (json != savedJson)
        {
            JsonStorage.Write(Paths.State, candidate); // Commit before exposing or enforcing changes.
            savedJson = json;
        }
        state = candidate;
        status = next;
        try
        {
            if (next.TaskManagerRequested && next.ReleaseAt is { } release)
                policy.Apply(installation.UserSid, release, now);
            else policy.Restore();
            status = status with { PolicyMessage = next.TaskManagerRequested ? "任务管理器限制已生效" : "任务管理器不受本工具限制" };
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
                    else if (File.Exists(Paths.Paused)) response = new(false, Error: "管理员已暂停，请运行 Resume.ps1。");
                    else
                    {
                        try
                        {
                            switch (request.Command)
                            {
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

    protected override void OnStop()
    {
        stop?.Cancel();
        try { runner?.GetAwaiter().GetResult(); }
        finally
        {
            try { policy.Restore(); } catch (Exception error) { Paths.Log(error.Message); }
            stop?.Dispose();
            Paths.Log("Service stopped; requested policy restoration.");
        }
    }
    protected override void OnShutdown() => OnStop();
}
