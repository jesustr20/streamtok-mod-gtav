using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using StreamTok.GtaV.Actions;

namespace StreamTok.GtaV
{
    /// <summary>Comando recibido de StreamTok (canal "mod-command").</summary>
    internal sealed class ModCommand
    {
        public string Id { get; set; }
        public string Action { get; set; }
        public Dictionary<string, object> Params { get; set; }

        /// <summary>Texto a dibujar sobre las entidades que cree la acción. Opcional.</summary>
        public string NameTag { get; set; }

        /// <summary>Texto para la notificación en pantalla. Opcional.</summary>
        public string Notify { get; set; }
    }

    /// <summary>
    /// Contrato WebSocket entre el mod y StreamTok. Todos los mensajes son
    /// { "channel": "...", "payload": { ... } }.
    ///
    ///   mod → app   mod-hello    catálogo de acciones que el mod sabe ejecutar
    ///   app → mod   mod-command  { id, action, params, nameTag?, notify? }
    ///   mod → app   mod-ack      { id, ok, error? }
    ///
    /// El mod no conoce TikTok: solo recibe acciones ya resueltas por la app.
    /// </summary>
    internal static class Protocol
    {
        public const string ModId = "gtav";

        public static string BuildHello(string version, IEnumerable<ActionDef> actions)
        {
            var payload = new Dictionary<string, object>
            {
                ["mod"] = ModId,
                ["version"] = version,
                ["actions"] = actions.Select(DescribeAction).ToList(),
            };
            return Envelope("mod-hello", payload);
        }

        public static string BuildAck(string id, string error)
        {
            var payload = new Dictionary<string, object>
            {
                ["id"] = id,
                ["ok"] = error == null,
            };
            if (error != null)
            {
                payload["error"] = error;
            }
            return Envelope("mod-ack", payload);
        }

        /// <summary>
        /// Devuelve true si el mensaje es un mod-command válido. Otros canales devuelven
        /// false con error = null (se ignoran en silencio); JSON roto devuelve false con error.
        /// </summary>
        public static bool TryParseCommand(string json, out ModCommand command, out string error)
        {
            command = null;
            error = null;

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                error = "JSON inválido: " + ex.Message;
                return false;
            }

            if (root == null || !(Get(root, "channel") is string channel) || channel != "mod-command")
            {
                return false;
            }

            if (!(Get(root, "payload") is Dictionary<string, object> payload))
            {
                error = "mod-command sin payload";
                return false;
            }

            command = new ModCommand
            {
                Id = Get(payload, "id")?.ToString() ?? Guid.NewGuid().ToString("N"),
                Action = Get(payload, "action") as string,
                Params = Get(payload, "params") as Dictionary<string, object> ?? new Dictionary<string, object>(),
                NameTag = Get(payload, "nameTag") as string,
                Notify = Get(payload, "notify") as string,
            };

            if (string.IsNullOrEmpty(command.Action))
            {
                error = "mod-command sin 'action'";
                return false;
            }

            return true;
        }

        private static Dictionary<string, object> DescribeAction(ActionDef action) => new Dictionary<string, object>
        {
            ["id"] = action.Id,
            ["name"] = action.Name,
            ["supportsNameTag"] = action.SupportsNameTag,
            ["params"] = action.Params.Select(DescribeParam).ToList(),
        };

        private static Dictionary<string, object> DescribeParam(ParamDef p)
        {
            var d = new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["type"] = p.Type,
                ["default"] = p.Default,
            };
            if (p.Min.HasValue) d["min"] = p.Min.Value;
            if (p.Max.HasValue) d["max"] = p.Max.Value;
            if (p.Options != null) d["options"] = p.Options;
            return d;
        }

        private static string Envelope(string channel, object payload) =>
            new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                ["channel"] = channel,
                ["payload"] = payload,
            });

        private static object Get(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) ? v : null;
    }
}
