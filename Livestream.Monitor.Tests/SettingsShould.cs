using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Livestream.Monitor.Core;
using Newtonsoft.Json;
using Xunit;

namespace Livestream.Monitor.Tests
{
    public class SettingsShould
    {
        public static IEnumerable<object[]> SettingsProperties
        {
            get
            {
                return typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                        .Select(x => new[] { x });
            }
        }

        [Theory]
        [MemberData("SettingsProperties")]
        public void AllPublicPropertiesHaveJsonPropertyAttribute(PropertyInfo property)
        {
            var jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
            Assert.NotNull(jsonProperty);
        }
    }
}
