using System.Text;

namespace StreamTok.GtaV
{
    internal static class TextUtil
    {
        /// <summary>Texto para dibujar sobre una entidad (nombre del viewer).</summary>
        public static string CleanTag(string s) => Clean(s, 24);

        /// <summary>Texto para una notificación.</summary>
        public static string CleanText(string s) => Clean(s, 90);

        /// <summary>
        /// Deja solo lo que la fuente de GTA puede dibujar y no es peligroso:
        ///  - quita '~' (GTA lo usa para códigos de formato como ~r~ o ~n~),
        ///  - quita caracteres de control,
        ///  - quita todo fuera de Latin-1 (emojis, otros alfabetos): la fuente del juego
        ///    los dibuja como cuadrados. Las tildes y la ñ sí se conservan.
        /// Devuelve null si no queda nada.
        /// </summary>
        public static string Clean(string s, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            var sb = new StringBuilder(s.Length);
            bool lastWasSpace = false;
            foreach (char c in s)
            {
                if (c == '~' || c < 0x20 || c > 0xFF)
                {
                    continue;
                }

                bool isSpace = c == ' ';
                if (isSpace && lastWasSpace)
                {
                    continue;
                }

                sb.Append(c);
                lastWasSpace = isSpace;
            }

            string result = sb.ToString().Trim();
            if (result.Length == 0)
            {
                return null;
            }

            return result.Length <= maxLength ? result : result.Substring(0, maxLength - 3) + "...";
        }
    }
}
