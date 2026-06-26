using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PreySense.Input
{
    public sealed class QuickAccessModeWatcher : IDisposable
    {
        private static readonly Uri QuickAccessUri = new("wss://localhost:5141/");
        private const string QuestionString = "AcerQuickAccessFunctionalityTests_HandshakingQuestion";
        private const string EncryptionKeyA = "A6052DC8A6E44210";
        private const string EncryptionKeyB = "AB252AB73BED1CDB";

        private readonly Action _onModeKeyPressed;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;
        private DateTime _lastKeyEvent = DateTime.MinValue;
        private bool _disposed;

        public QuickAccessModeWatcher(Action onModeKeyPressed)
        {
            _onModeKeyPressed = onModeKeyPressed ?? throw new ArgumentNullException(nameof(onModeKeyPressed));
            _worker = Task.Run(RunAsync);
        }

        public static bool TryGetCurrentPowerMode(out byte mode)
        {
            try
            {
                mode = TryGetCurrentPowerModeAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"QuickAccessModeWatcher: current mode query failed: {ex.Message}");
                mode = 0;
                return false;
            }
        }

        private static async Task<byte> TryGetCurrentPowerModeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            CancellationToken token = linkedCts.Token;

            using var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            await socket.ConnectAsync(QuickAccessUri, token);
            await SendAuthPacketAsync(socket, token);

            string handshake = await ReceiveTextMessageAsync(socket, token);
            if (!IsValidHandshake(handshake))
                throw new InvalidOperationException($"handshake rejected: {handshake}");

            string session = Guid.NewGuid().ToString();
            await SendJsonAsync(socket, new
            {
                PacketType = 2,
                Version = 1,
                Session = session,
                Command = "SystemUsageControl",
                Action = "Get"
            }, token);

            while (socket.State == WebSocketState.Open)
            {
                string json = await ReceiveTextMessageAsync(socket, token);
                if (TryParseSystemUsageControl(json, session, out byte acerMode))
                {
                    AppLogger.Log($"QuickAccessModeWatcher: startup SystemUsageControl -> Acer mode 0x{acerMode:X2}.");
                    return acerMode;
                }
            }

            throw new InvalidOperationException("socket closed before SystemUsageControl response.");
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var socket = new ClientWebSocket();
                    socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                    await socket.ConnectAsync(QuickAccessUri, _cts.Token);
                    AppLogger.Log("QuickAccessModeWatcher: connected to wss://localhost:5141.");

                    await SendAuthPacketAsync(socket, _cts.Token);
                    await ReceiveLoopAsync(socket, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"QuickAccessModeWatcher: connection unavailable: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task SendAuthPacketAsync(ClientWebSocket socket, CancellationToken token)
        {
            string session = Guid.NewGuid().ToString();
            string authData = EncodeAes(
                JsonSerializer.Serialize(new { Question = QuestionString, Key = EncryptionKeyB }),
                EncryptionKeyA);

            await SendJsonAsync(socket, new
            {
                PacketType = 1,
                Session = session,
                Version = 1,
                Data = authData
            }, token);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            using var message = new MemoryStream();
            bool handshaking = true;

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                string json = Encoding.UTF8.GetString(message.ToArray());

                if (handshaking)
                {
                    if (!IsValidHandshake(json))
                    {
                        AppLogger.Log($"QuickAccessModeWatcher: handshake failed: {json}");
                        return;
                    }

                    handshaking = false;
                    AppLogger.Log("QuickAccessModeWatcher: handshake accepted.");
                    await SendFunctionQueryAsync(socket, token);
                    continue;
                }

                HandlePacket(json);
            }
        }

        private static bool IsValidHandshake(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (!TryGetInt(root, "PacketType", out int packetType) || packetType != 1)
                    return false;

                if (!root.TryGetProperty("Data", out JsonElement dataElement))
                    return false;

                string? data = dataElement.GetString();
                string expected = EncodeAes(QuestionString, EncryptionKeyB);
                return string.Equals(data, expected, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static Task SendFunctionQueryAsync(ClientWebSocket socket, CancellationToken token)
        {
            return SendJsonAsync(socket, new
            {
                PacketType = 2,
                Version = 1,
                Session = Guid.NewGuid().ToString(),
                Command = "FunctionQuery"
            }, token);
        }

        private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            using var message = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new EndOfStreamException("Quick Access socket closed.");

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Quick Access returned a non-text websocket message.");

            return Encoding.UTF8.GetString(message.ToArray());
        }

        private static bool TryParseSystemUsageControl(string json, string expectedSession, out byte acerMode)
        {
            acerMode = 0;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string? session = TryGetString(root, "Session");
                if (!string.Equals(session, expectedSession, StringComparison.OrdinalIgnoreCase))
                    return false;

                string? command = TryGetString(root, "Command");
                string? action = TryGetString(root, "Action");
                if (!string.Equals(command, "SystemUsageControl", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(action, "Get", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!root.TryGetProperty("Result", out JsonElement result) ||
                    result.ValueKind != JsonValueKind.Object ||
                    !TryGetInt(result, "Value", out int quickAccessMode))
                {
                    return false;
                }

                return TryMapQuickAccessMode(quickAccessMode, out acerMode);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryMapQuickAccessMode(int quickAccessMode, out byte acerMode)
        {
            acerMode = quickAccessMode switch
            {
                0 => 0x00, // Silent
                1 => 0x01, // Normal -> Balanced
                2 => 0x04, // Performance
                3 => 0x05, // Turbo
                4 => 0x06, // Eco
                5 => 0x00, // Quiet
                6 => 0x06, // Eco+
                _ => 0xFF
            };

            return acerMode != 0xFF;
        }

        private void HandlePacket(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string? action = TryGetString(root, "Action");
                string? command = TryGetString(root, "Command");
                if (!string.Equals(action, "Notify", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(command, "KeyEvent", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if ((DateTime.Now - _lastKeyEvent).TotalMilliseconds < 250)
                    return;

                _lastKeyEvent = DateTime.Now;
                string param = TryGetInt(root, "Param1", out int param1) ? param1.ToString() : "null";
                AppLogger.Log($"QuickAccessModeWatcher: KeyEvent received (Param1={param}).");
                _onModeKeyPressed();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"QuickAccessModeWatcher: failed to parse packet: {ex.Message}");
            }
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, object packet, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(packet);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }

        private static string EncodeAes(string text, string key)
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] input = Encoding.UTF8.GetBytes(text);
            byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);
            return Convert.ToBase64String(encrypted);
        }

        private static string? TryGetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool TryGetInt(JsonElement element, string name, out int value)
        {
            value = 0;
            if (!element.TryGetProperty(name, out JsonElement jsonValue))
                return false;

            return jsonValue.ValueKind switch
            {
                JsonValueKind.Number => jsonValue.TryGetInt32(out value),
                JsonValueKind.String => int.TryParse(jsonValue.GetString(), out value),
                _ => false
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
