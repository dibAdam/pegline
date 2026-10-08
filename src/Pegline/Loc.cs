using System.Globalization;

namespace Pegline
{
    /// <summary>
    /// Tiny three-language helper. The app has a handful of strings, so .resx
    /// files would be more ceremony than content.
    /// </summary>
    static class Loc
    {
        static readonly string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        public static string L(string english, string spanish, string french) =>
            language == "es" ? spanish : language == "fr" ? french : english;
    }
}
