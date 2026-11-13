using System.Globalization;

namespace SnapStudio
{
    /// <summary>Textos da interface: português se o Windows estiver em PT, senão inglês.</summary>
    static class S
    {
        static readonly bool pt = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";
        public static string T(string ptText, string enText) { return pt ? ptText : enText; }
    }
}
