using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using StreamTok.GtaV;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Modes;

namespace StreamTok.CatalogExport
{
    /// <summary>
    /// Exporta el catálogo del mod a catalog.json SIN abrir GTA V.
    ///
    /// Arma el ActionRegistry real (el mismo que usa StreamTokMod) y llama a
    /// Protocol.BuildHello, la función que ya usa el mod para el mensaje "mod-hello" por
    /// WebSocket. Por eso el JSON de salida es exactamente el mismo contenido (mismo id, name,
    /// category, icon, description, params) que vería StreamTok con el juego abierto.
    ///
    /// Uso: StreamTok.CatalogExport.exe [ruta-de-salida] [version]
    ///   ruta-de-salida: default "catalog.json" en el directorio actual.
    ///   version:        default: se lee de StreamTok.GtaV.csproj; si no se encuentra, "0.0.0-dev".
    /// </summary>
    internal static class Program
    {
        private static void Main(string[] args)
        {
            string outputPath = args.Length > 0 ? args[0] : "catalog.json";
            string version = args.Length > 1 ? args[1] : ReadVersionFromCsproj();

            var log = new System.Collections.Generic.List<string>();
            Action<string> logger = msg => log.Add(msg);

            // Carpeta de trabajo propia: si no existe StreamTok.Characters.json, CharacterCatalog
            // crea uno de ejemplo acá (igual que haría el mod la primera vez), sin tocar el repo.
            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);

            // validateModels: false — es el único punto donde el mod real llama a un native
            // (model.IsInCdImage) para descartar personajes con modelos no instalados. Acá no hay
            // juego corriendo, así que se incluyen todos los personajes del JSON tal cual.
            var characters = CharacterCatalog.Load(dataDir, logger, validateModels: false);

            var rng = new Random();
            var tracker = new EntityTracker(100, 20);
            var scheduler = new FrameScheduler(logger);
            var characterManager = new CharacterManager(tracker, rng);
            var parkour = new ParkourMode(tracker, logger, dataDir);
            var arena = new ArenaMode(tracker, characterManager, characters, scheduler, rng, logger, dataDir,
                ArenaMode.DefaultHealthTiers);

            ActionRegistry registry = ActionRegistry.CreateDefault(characters, arena.CharacterIds, parkour.Courses);

            string json = Protocol.BuildHello(version, registry.All);

            // BuildHello devuelve el envoltorio { channel, payload } tal cual viaja por WebSocket.
            // Para el archivo publicado solo interesa el payload (mod, version, actions).
            string payloadJson = ExtractPayload(json);

            File.WriteAllText(outputPath, payloadJson, new UTF8Encoding(false));

            Console.WriteLine($"catalog.json generado: {outputPath}");
            Console.WriteLine($"  versión: {version}");
            Console.WriteLine($"  acciones: {registry.All.Count}");
            foreach (string line in log)
            {
                Console.WriteLine("  [mod] " + line);
            }
        }

        /// <summary>
        /// Protocol.BuildHello() envuelve el payload en { "channel": "mod-hello", "payload": {...} }
        /// porque así viaja por WebSocket. El archivo publicado solo necesita el payload
        /// ({ mod, version, actions }), así que se desenvuelve con el mismo serializador del mod.
        /// </summary>
        private static string ExtractPayload(string envelopeJson)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var root = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(envelopeJson);
            object payload = root["payload"];
            return serializer.Serialize(payload);
        }

        private static string ReadVersionFromCsproj()
        {
            try
            {
                string csprojPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "StreamTok.GtaV.csproj");
                if (!File.Exists(csprojPath))
                {
                    // Fallback para cuando se corre directo desde el repo con `dotnet run`.
                    csprojPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "StreamTok.GtaV.csproj");
                }
                if (File.Exists(csprojPath))
                {
                    string xml = File.ReadAllText(csprojPath);
                    Match m = Regex.Match(xml, "<Version>([^<]+)</Version>");
                    if (m.Success)
                    {
                        return m.Groups[1].Value.Trim();
                    }
                }
            }
            catch
            {
                // sin versión no se detiene el export: se usa el fallback
            }
            return "0.0.0-dev";
        }
    }
}
