using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Browser
{
    /// <summary>
    /// Client that connects to the BrowserCoordinator pipe server.
    /// Used by native messaging host instances to forward messages to the main app.
    /// </summary>
    internal sealed class BrowserCoordinatorClient : IDisposable
    {
        private const int CoordinatorPort = 32145;
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly CancellationTokenSource _cts = new();
        private Task? _readTask;
        private bool _disposed;

        public event EventHandler<string>? MessageReceived;
        public event EventHandler? Disconnected;

        public bool IsConnected => _client?.Connected ?? false;

        public bool TryConnect(int timeoutMs = 5000)
        {
            try
            {
                _client = new TcpClient();
                var connectTask = _client.ConnectAsync(IPAddress.Loopback, CoordinatorPort);
                if (!connectTask.Wait(timeoutMs))
                {
                    throw new TimeoutException();
                }

                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                
                _readTask = Task.Run(ReadMessagesAsync);
                
                LoggingEngine.LogLine("[CoordinatorClient] Connected to main app (TCP)",
                    category: LoggingEngine.LogCategory.MediaControl);
                return true;
            }
            catch (TimeoutException)
            {
                LoggingEngine.LogLine("[CoordinatorClient] Connection timed out - main app not running?",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return false;
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[CoordinatorClient] Connection failed: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return false;
            }
        }

        private async Task ReadMessagesAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested && _client?.Connected == true)
                {
                    var line = await _reader!.ReadLineAsync();
                    if (line == null) break;
                    
                    MessageReceived?.Invoke(this, line);
                }
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[CoordinatorClient] Read error: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
            finally
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SendMessage(object message)
        {
            if (_client?.Connected != true || _writer == null) return;
            
            try
            {
                var json = JsonSerializer.Serialize(message);
                _writer.WriteLine(json);
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[CoordinatorClient] Send failed: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cts.Cancel();
            _reader?.Dispose();
            _writer?.Dispose();
            _client?.Close();
        }
    }
}
