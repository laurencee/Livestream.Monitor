using System;
using System.Threading;
using Livestream.Monitor.Core.Utility;
using Xunit;

namespace Livestream.Monitor.Tests;

public class SingleInstanceShould
{
    [Fact]
    public void LetOnlyTheFirstClaimRunAskItToShowItselfAndFreeTheNameOnRelease()
    {
        var name = "Livestream.Monitor.Tests." + Guid.NewGuid().ToString("N");

        using var activated = new ManualResetEventSlim();
        var first = SingleInstance.Claim(name, activated.Set);

        try
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstance.Claim(name, () => { }));
            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)), "The first instance was not asked to show itself.");
        }
        finally
        {
            first?.Dispose();
        }

        using var next = SingleInstance.Claim(name, () => { });
        Assert.NotNull(next);
    }
}
