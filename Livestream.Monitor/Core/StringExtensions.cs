using System;
using System.Text.RegularExpressions;

namespace Livestream.Monitor.Core
{
    public static class StringExtensions
    {
        /// <summary>
        /// Returns spaced output for a Pascal cased string value
        /// <example>"SomePascalCaseString" comes back as "Some Pascal Case String"</example>
        /// </summary>
        /// <param name="pascalCaseValue">A string in Pascal case format</param>
        /// <returns>Spaced Pascal case string</returns>
        public static string ToFriendlyString(this string pascalCaseValue)
        {
            return Regex.Replace(pascalCaseValue, "(?!^)([A-Z])", " $1");
        }

        /// <summary>
        /// Contains method allowing for a <see cref="StringComparison"/> to be passed in
        /// </summary>
        /// <param name="original">Original string value</param>
        /// <param name="value">String value being compared against</param>
        /// <param name="stringComparison">Specifies the culter, case and sort rules for the comparison</param>
        /// <returns>True if the original string contains the comparing string value following the <see cref="StringComparison"/> rules</returns>
        public static bool Contains(this string original, string value, StringComparison stringComparison)
        {
            return original.IndexOf(value, stringComparison) >= 0;
        }

        public static bool IsEqualTo(this string original, string value, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            return String.Compare(original, value, stringComparison) == 0;
        }
    }
}