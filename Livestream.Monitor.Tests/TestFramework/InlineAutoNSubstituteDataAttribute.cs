using Ploeh.AutoFixture.Xunit2;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class InlineAutoNSubstituteDataAttribute : InlineAutoDataAttribute
    {
        public InlineAutoNSubstituteDataAttribute(params object[] values)
            : base(new AutoNSubstituteDataAttribute(), values)
        {
        }
    }
}