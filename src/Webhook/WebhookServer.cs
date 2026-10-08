using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using StreamTok.GtaV.Actions;

namespace StreamTok.GtaV.Webhook
{
    /// <summary>
    /// Entrada opcional de comandos por HTTP local, para programas como TikFinity o Interactive
    /// que llaman a una URL (webhook). Es una puerta más hacia la MISMA cola que usa el WebSocket
    /// de la app: mismo límite por frame, mismas acciones, mismo nombre sobre las entidades.
    ///
    /// Solo escucha en 127.0.0.1 (nunca en la red) y está apagada por defecto:
    ///   [Webhook]
    ///   Enabled=false
    ///   Port=7332
    ///   Token=            (opcional; si lo pones, cada petición debe llevar ?token=...)
    ///   MaxRepeat=1000000    (tope de "repeat" por petición, para combos)
    ///
    /// Rutas (GET o POST):
    ///   /ping                      → "ok"
    ///   /actions                   → lista de ids de acciones
    ///   /action/{id}?user=Juan&amp;repeat=3&amp;{param}=valor
    ///
    /// Datos del viewer (query o cuerpo JSON; el primero que exista):
    ///   usuario: user, username, nickname, name, value1
    ///   texto:   comment, text, value2
    ///   regalo:  gift, giftname, value3
    ///   monedas: coins
    /// El resto de claves que coincidan con parámetros de la acción se pasan tal cual.
    /// </summary>
    internal sealed class WebhookServer
    {
        private static readonly string[] UserKeys = { "user", "username", "nickname", "name", "value1" };
        private static readonly string[] CommentKeys = { "comment", "text", "value2" };
        private static readonly string[] GiftKeys = { "gift", "giftname", "value3" };

        private readonly int _port;
        private readonly string _token;
        private readonly int _maxCount;
        private readonly ActionRegistry _registry;
        private readonly ConcurrentQueue<ModCommand> _queue;
        private readonly Action<string> _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private HttpListener _listener;
        private int _nextId;

        public WebhookServer(int port, string token, int maxRepeat, ActionRegistry registry,
            ConcurrentQueue<ModCommand> queue, Action<string> log)
        {
            _port = port;
            _token = string.IsNullOrEmpty(token) ? null : token;
            _maxCount = Math.Max(1, maxRepeat);
            _registry = registry;
            _queue = queue;
            _log = log;
        }

        /// <summary>Último comando recibido, para que el mod avise en pantalla (solo diagnóstico).</summary>
        public ConcurrentQueue<string> Notices { get; } = new ConcurrentQueue<string>();

        public bool Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
            }
            catch (Exception ex)
            {
                _log($"Webhook: no se pudo abrir el puerto {_port} ({ex.Message}). Queda desactivado.");
                _listener = null;
                return false;
            }

            Task.Run(() => LoopAsync(_cts.Token));
            _log($"Webhook: escuchando en http://127.0.0.1:{_port}/ (token: {(_token == null ? "no" : "sí")}).");
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener?.Close(); } catch { /* cerrando */ }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    break; // listener cerrado
                }

                // Cada petición en su propia tarea: una lenta no frena a las demás.
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            int status = 200;
            string body;
            try
            {
                body = Process(ctx.Request, out status);
            }
            catch (Exception ex)
            {
                _log($"Webhook: error procesando petición: {ex.Message}");
                status = 500;
                body = "error";
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // el cliente cortó la conexión
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        private string Process(HttpListenerRequest req, out int status)
        {
            status = 200;

            // Defensa contra DNS rebinding: solo se acepta si la petición llegó a 127.0.0.1/localhost.
            // (No se bloquea por "Origin": TikFinity/Interactive pueden llamar desde su interfaz web.
            //  Con el mod abierto a webhooks, usa [Webhook] Token si te preocupa que una página llame al puerto.)
            string host = req.Headers["Host"] ?? "";
            if (!(host.StartsWith("127.0.0.1") || host.StartsWith("localhost")))
            {
                status = 403;
                return "prohibido";
            }

            string path = req.Url.AbsolutePath.Trim('/');
            var data = ReadData(req);

            if (_token != null && Get(data, "token") != _token)
            {
                status = 401;
                return "token inválido";
            }

            if (path == "ping")
            {
                return "ok";
            }
            if (path == "actions")
            {
                return string.Join("\n", _registry.All.Select(a => a.Id));
            }
            if (!path.StartsWith("action/", StringComparison.OrdinalIgnoreCase))
            {
                status = 404;
                return "ruta desconocida; usa /action/{id}";
            }

            string actionId = Uri.UnescapeDataString(path.Substring("action/".Length));
            ActionDef action = _registry.Find(actionId);
            if (action == null)
            {
                status = 404;
                return $"acción desconocida: {actionId}";
            }

            string user = First(data, UserKeys);
            string comment = First(data, CommentKeys);
            string gift = First(data, GiftKeys);

            // Parámetros propios de la acción (type=sports, amount=3...).
            var values = new Dictionary<string, object>();
            foreach (ParamDef p in action.Params)
            {
                string v = Get(data, p.Name);
                if (v != null) values[p.Name] = v;
            }

            // "repeat" = cuántas veces se ejecuta la acción (combos). "count" NO se usa aquí:
            // ya es un parámetro de varias acciones (cuántos peds/vehículos) y se pasa tal cual.
            int count = 1;
            string rawRepeat = Get(data, "repeat");
            if (rawRepeat != null && int.TryParse(rawRepeat, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
            {
                count = Math.Min(_maxCount, Math.Max(1, c));
            }

            for (int i = 0; i < count; i++)
            {
                _queue.Enqueue(new ModCommand
                {
                    Id = "hook-" + Interlocked.Increment(ref _nextId),
                    Action = action.Id,
                    Params = new Dictionary<string, object>(values),
                    NameTag = user,
                });
            }

            _log($"Webhook: {action.Id} x{count} user='{user}' gift='{gift}' comment='{comment}' coins='{Get(data, "coins")}'");
            Notices.Enqueue($"Webhook {action.Id} x{count}" + (user != null ? $" ({user})" : ""));
            return $"ok {action.Id} x{count}";
        }

        /// <summary>Junta query string y cuerpo (JSON o formulario) en un solo diccionario (sin distinguir mayúsculas).</summary>
        private static Dictionary<string, string> ReadData(HttpListenerRequest req)
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in req.QueryString.AllKeys)
            {
                if (key != null) data[key] = req.QueryString[key];
            }

            if (req.HttpMethod == "POST" && req.HasEntityBody)
            {
                string text;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    char[] buf = new char[8192]; // cuerpo máximo 8 KB: esto no es para archivos
                    int n = reader.Read(buf, 0, buf.Length);
                    text = new string(buf, 0, n);
                }

                text = text.Trim();
                if (text.StartsWith("{"))
                {
                    try
                    {
                        var json = new JavaScriptSerializer().DeserializeObject(text) as IDictionary<string, object>;
                        if (json != null)
                        {
                            foreach (var kv in json)
                            {
                                if (kv.Value != null) data[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                            }
                        }
                    }
                    catch
                    {
                        // JSON roto: se usa solo lo de la URL
                    }
                }
                else if (text.Length > 0)
                {
                    foreach (string pair in text.Split('&'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        data[Uri.UnescapeDataString(pair.Substring(0, eq).Replace('+', ' '))] =
                            Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                    }
                }
            }
            return data;
        }

        private static string Get(IDictionary<string, string> data, string key) =>
            data.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        private static string First(IDictionary<string, string> data, string[] keys)
        {
            foreach (string k in keys)
            {
                string v = Get(data, k);
                if (v != null) return v;
            }
            return null;
        }
    }
}
