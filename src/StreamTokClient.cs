using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Cliente WebSocket hacia el sidecar de StreamTok.
    ///
    /// Corre en un hilo de fondo: NUNCA llama a la API del juego desde aquí
    /// (los natives de GTA solo son seguros en el hilo del script). En su lugar
    /// deja eventos y cambios de estado en colas que el script vacía en su Tick.
    /// </summary>
    internal sealed class StreamTokClient : IDisposable
    {
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(Envelope));

        private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

        private readonly Uri _uri;
        private readonly Action<string> _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private string _lastError;

        public StreamTokClient(Uri uri, Action<string> log)
        {
            _uri = uri;
            _log = log;
        }

        /// <summary>Eventos "live-event" recibidos, pendientes de mostrar en el juego.</summary>
        public ConcurrentQueue<LiveEvent> Events { get; } = new ConcurrentQueue<LiveEvent>();

        /// <summary>true = se conectó, false = se desconectó. Solo se encola al cambiar.</summary>
        public ConcurrentQueue<bool> StatusChanges { get; } = new ConcurrentQueue<bool>();

        public void Start()
        {
            Task.Run(() => RunAsync(_cts.Token));
        }

        public void Dispose()
        {
            // Al recargar (Insert) o cerrar el juego: cortar reconexión y recepción.
            _cts.Cancel();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            TimeSpan delay = MinRetryDelay;

            while (!ct.IsCancellationRequested)
            {
                bool wasConnected = false;

                using (var ws = new ClientWebSocket())
                {
                    try
                    {
                        await ws.ConnectAsync(_uri, ct).ConfigureAwait(false);

                        wasConnected = true;
                        delay = MinRetryDelay;
                        _lastError = null;
                        _log($"WS conectado a {_uri}");
                        StatusChanges.Enqueue(true);

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
                        HandleMessage(message.ToArray());
                    }
                }
            }
        }

        private void HandleMessage(byte[] bytes)
        {
            Envelope envelope;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    envelope = (Envelope)Serializer.ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                // Mensaje de otro canal con otra forma, o JSON inválido: se ignora sin romper la conexión.
                _log($"WS mensaje ignorado ({ex.GetType().Name}): {Truncate(Encoding.UTF8.GetString(bytes), 200)}");
                return;
            }

            if (envelope?.Channel != "live-event" || envelope.Payload == null)
            {
                return;
            }

            Events.Enqueue(envelope.Payload);
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
