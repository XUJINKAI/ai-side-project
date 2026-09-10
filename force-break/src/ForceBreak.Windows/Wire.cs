using System.IO.Pipes;
using System.Text.Json;
using ForceBreak.Core;
using System.Security.Principal;

namespace ForceBreak.Windows;

public static class Wire
{
    // Length-limited frame, so an authenticated but broken client cannot allocate unlimited RAM.
    public static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > 32768) throw new InvalidDataException("Invalid message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty message.");
    }

    public static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 32768) throw new InvalidDataException("Message too large.");
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public static async Task<Response> Send(Request request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var pipe = new NamedPipeClientStream(".", Paths.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token);
        await Write(pipe, request, timeout.Token);
        return await Read<Response>(pipe, timeout.Token);
    }
}
