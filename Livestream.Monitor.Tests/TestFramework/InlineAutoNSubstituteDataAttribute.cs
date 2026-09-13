using AutoFixture.Xunit2;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class InlineAutoNSubstituteDataAttribute(params object[] values)
        : InlineAutoDataAttribute(new AutoNSubstituteDataAttribute(), values);
}