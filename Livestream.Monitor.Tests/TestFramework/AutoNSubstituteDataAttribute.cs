using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AutoFixture.Xunit2;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class AutoNSubstituteDataAttribute : AutoDataAttribute
    {
        public AutoNSubstituteDataAttribute()
            : base(() => new Fixture()
                .Customize(new CompositeCustomization(
                    new LivestreamModelCustomization(),
                    new AutoNSubstituteCustomization() { ConfigureMembers = true }
                )))
        {
        }
    }
}
