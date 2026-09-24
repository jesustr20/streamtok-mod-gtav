using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Cliente WebSocket hacia el sidecar de StreamTok.
    ///
    /// Corre en hilos de fondo: NUNCA llama a la API del juego desde aquí
    /// (los natives de GTA solo son seguros en el hilo del script). Los comandos
    /// entrantes quedan en <see cref="Commands"/> y el script los ejecuta en su Tick;
    /// las respuestas salen por <see cref="Send"/>.
    /// </summary>
    internal sealed class StreamTokClient : IDisposable
    {
        private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

        private readonly Uri _uri;
        private readonly string _helloJson;
        private readonly Action<string> _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();
        private string _lastError;

        public StreamTokClient(Uri uri, string helloJson, Action<string> log)
        {
            _uri = uri;
            _helloJson = helloJson;
            _log = log;
        }

        /// <summary>Comandos recibidos, pendientes de ejecutar en el hilo del juego.</summary>
        public ConcurrentQueue<ModCommand> Commands { get; } = new ConcurrentQueue<ModCommand>();

        /// <summary>true = se conectó, false = se desconectó. Solo se encola al cambiar.</summary>
        public ConcurrentQueue<bool> StatusChanges { get; } = new ConcurrentQueue<bool>();

        public void Start()
        {
            Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>Encola un mensaje JSON para enviar. Seguro desde cualquier hilo.</summary>
        public void Send(string json)
        {
            _outbound.Enqueue(json);
        }

        public void Dispose()
        {
            // Al recargar (Insert) o cerrar el juego: cortar reconexión, envío y recepción.
            _cts.Cancel();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            TimeSpan delay = MinRetryDelay;

            while (!ct.IsCancellationRequested)
            {
                bool wasConnected = false;

                using (var ws = new ClientWebSocket())
                using (var connection = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    Task sendLoop = null;
                    try
                    {
                        await ws.ConnectAsync(_uri, ct).ConfigureAwait(false);

                        wasConnected = true;
                        delay = MinRetryDelay;
                        _lastError = null;
                        _log($"WS conectado a {_uri}");
                        StatusChanges.Enqueue(true);

                        // Lo pendiente de una conexión anterior ya no sirve; lo primero es presentarse.
                        while (_outbound.TryDequeue(out _)) { }
                        _outbound.Enqueue(_helloJson);

                        sendLoop = SendLoopAsync(ws, connection.Token);
                        await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Sin sidecar abierto esto falla cada pocos segundos: loguear solo si cambia.
                        string msg = ex.GetBaseException().Message;
                        if (msg != _lastError)
                        {
                            _log($"WS error: {msg}");
                            _lastError = msg;
                        }
                    }
                    finally
                    {
                        connection.Cancel();
                        if (sendLoop != null)
                        {
                            try { await sendLoop.ConfigureAwait(false); } catch { /* cancelado */ }
                        }
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (wasConnected)
                {
                    _log("WS desconectado; reintentando…");
                    StatusChanges.Enqueue(false);
                }

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Backoff exponencial: 1s, 2s, 4s, 8s, 10s, 10s…
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }

        private async Task SendLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            // ClientWebSocket no admite dos SendAsync a la vez: un único bucle envía todo.
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                if (_outbound.TryDequeue(out string json))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8 * 1024];

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        HandleMessage(Encoding.UTF8.GetString(message.ToArray()));
                    }
                }
            }
        }

        private void HandleMessage(string json)
        {
            if (Protocol.TryParseCommand(json, out ModCommand command, out string error))
            {
                Commands.Enqueue(command);
            }
            else if (error != null)
            {
                _log($"WS mensaje ignorado: {error}");
            }
        }
    }
}
