using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Services;

internal sealed class StreamingControlServer : IDisposable
{
    internal const string PipeName = "PreySense.Control";
    private const int MaxRequestBytes = 4096;
    private readonly CancellationTokenSource _stop = new();

    internal StreamingControlServer(Dispatcher dispatcher, MainViewModel viewModel)
        : this(PipeName, (input, stop) => dispatcher.InvokeAsync(
            () => viewModel.HandleStreamingControlAsync(input, stop), DispatcherPriority.Normal, stop).Task.Unwrap())
    {
    }

    internal StreamingControlServer(string pipeName, Func<JsonElement, CancellationToken, Task<StreamingControlReply>> dispatch)
    {
        var stop = _stop.Token;
        _ = Task.Run(() => ListenAsync(pipeName, dispatch, stop));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private static async Task ListenAsync(string pipeName,
        Func<JsonElement, CancellationToken, Task<StreamingControlReply>> dispatch, CancellationToken stop)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new InvalidOperationException("Could not identify the PreySense user.");
            var security = new PipeSecurity();
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            // The installed RubyLight supervisor launches its host as local SYSTEM.
            if (!user.IsWellKnown(WellKnownSidType.LocalSystemSid))
                security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Allow));

            using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            while (!stop.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(stop);
                try
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var input = await ReadRequestAsync(pipe, readTimeout.Token);
                    var response = await dispatch(input, stop);
                    await ReplyAsync(pipe, response, stop);
                }
                catch (JsonException)
                {
                    try { await ReplyAsync(pipe, new(false, "Expected a JSON object of at most 4096 bytes followed by a newline."), stop); }
                    catch (IOException) { }
                    catch (OperationCanceledException) { }
                }
                catch (IOException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppLogger.Error($"Streaming control command failed: {ex}");
                    try { await ReplyAsync(pipe, new(false, "PreySense could not complete the command."), stop); }
                    catch (IOException) { }
                    catch (OperationCanceledException) { }
                }
                finally { pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.Error($"Streaming control listener stopped: {ex}"); }
    }

    private static async Task<JsonElement> ReadRequestAsync(Stream pipe, CancellationToken stop)
    {
        var buffer = new byte[MaxRequestBytes + 1];
        int used = 0;
        while (used < buffer.Length)
        {
            int count = await pipe.ReadAsync(buffer.AsMemory(used), stop);
            if (count == 0) throw new IOException("The client disconnected.");
            int newline = Array.IndexOf(buffer, (byte)'\n', used, count);
            if (newline >= 0)
            {
                using var document = JsonDocument.Parse(buffer.AsMemory(0, newline));
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                return document.RootElement.Clone();
            }
            used += count;
        }
        throw new JsonException();
    }

    private static async Task ReplyAsync(Stream pipe, StreamingControlReply reply, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await JsonSerializer.SerializeAsync(pipe, reply, JsonSerializerOptions.Web, timeout.Token);
        await pipe.WriteAsync("\n"u8.ToArray(), timeout.Token);
        // DisconnectNamedPipe can discard queued output. Let the client consume
        // the reply and close before this server instance is reused.
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        var remaining = new byte[256];
        while (await pipe.ReadAsync(remaining, timeout.Token) != 0) { }
    }
}

internal sealed record StreamingControlReply(bool Ok, string Message, object? Data = null);
