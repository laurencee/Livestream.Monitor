using System.Collections.Generic;

namespace ExternalAPIs
{
    public static class Extensions
    {
        public static string AppendQueryStringValues(this string request, string queryStringParamName, IEnumerable<string> values, bool isFirstParam = true)
        {
            return $"{request}{(isFirstParam ? "?" : "&")}{queryStringParamName}={string.Join($"&{queryStringParamName}=", values)}";
        }
    }
}
