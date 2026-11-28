using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class StreamingControlTests
{
    [TestMethod]
    public void CpuLimitsFollowSavedProfilesAndFactoryCalibration()
    {
        var config = new PreySenseConfig();
        config.Tuning.FactoryDefaults["Performance"] = new() { CpuPl1Watts = 60, CpuPl2Watts = 100 };
        config.Tuning.FactoryDefaults["Turbo"] = new() { CpuPl1Watts = 75, CpuPl2Watts = 120 };
        config.Tuning.Turbo.CpuPl1Watts = 85;
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => config;

        Assert.AreEqual(((int?)60, (int?)100), vm.StreamingCpuProfileLimits(4));
        Assert.AreEqual(((int?)85, (int?)120), vm.StreamingCpuProfileLimits(5));
        Assert.AreEqual(((int?)null, (int?)null), vm.StreamingCpuProfileLimits(1));
    }

    [TestMethod]
    public async Task PipeRejectsMalformedRequestsAndAcceptsTheNextClient()
    {
        string name = $"PreySense.Test.{Guid.NewGuid():N}";
        int dispatched = 0;
        using var server = new StreamingControlServer(name, (request, _) =>
        {
            dispatched++;
            return Task.FromResult(new StreamingControlReply(true, request.GetProperty("action").GetString()!));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        async Task<JsonElement> SendAsync(string request)
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            // The server allows one active connection and can still be disconnecting the previous client.
            while (true)
            {
                try { await client.ConnectAsync(250, timeout.Token); break; }
                catch (TimeoutException) { timeout.Token.ThrowIfCancellationRequested(); }
            }
            await client.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), timeout.Token);
            using var reader = new StreamReader(client);
            using var response = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
            return response.RootElement.Clone();
        }

        Assert.IsFalse((await SendAsync("[]")).GetProperty("ok").GetBoolean());
        Assert.IsFalse((await SendAsync(new string(' ', 4097))).GetProperty("ok").GetBoolean());
        var reply = await SendAsync("""{"action":"get_status"}""");
        Assert.IsTrue(reply.GetProperty("ok").GetBoolean());
        Assert.AreEqual("get_status", reply.GetProperty("message").GetString());
        Assert.AreEqual(1, dispatched);
    }
}
