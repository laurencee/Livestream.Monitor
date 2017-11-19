using Ploeh.AutoFixture;
using Ploeh.AutoFixture.AutoNSubstitute;
using Ploeh.AutoFixture.Xunit2;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class AutoNSubstituteDataAttribute : AutoDataAttribute
    {
        public AutoNSubstituteDataAttribute()
            : base(new Fixture()
                .Customize(new CompositeCustomization(
                    new LivestreamModelCustomization(),
                    new AutoConfiguredNSubstituteCustomization()
                )))
        {
        }
    }
}
