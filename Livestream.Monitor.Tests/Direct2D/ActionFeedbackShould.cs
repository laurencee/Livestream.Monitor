using System.Drawing;
using Livestream.Monitor.Direct2D.Widgets;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class ActionFeedbackShould
{
    private const int HoldEndMilliseconds = 1_250;
    private const int FadeMidpointMilliseconds = 1_375;
    private const int DisappearMilliseconds = 1_500;

    [Fact]
    public void HoldThenFadeAndStopRequestingFrames()
    {
        var feedback = new ActionFeedback();
        Assert.False(feedback.IsVisible);
        Assert.False(feedback.Tick(0));

        feedback.Show("Copied", PointF.Empty, now: 0);
        Assert.Equal(1, feedback.Opacity);
        feedback.Tick(HoldEndMilliseconds);
        Assert.Equal(1, feedback.Opacity);
        feedback.Tick(FadeMidpointMilliseconds);
        Assert.Equal(0.5f, feedback.Opacity);
        Assert.True(feedback.Tick(DisappearMilliseconds));
        Assert.Equal(0, feedback.Opacity);
        Assert.False(feedback.IsVisible);
        Assert.False(feedback.Tick(DisappearMilliseconds + 1));
    }

    [Fact]
    public void RestartTheDurationWhenAnotherActionCompletes()
    {
        var feedback = new ActionFeedback();
        feedback.Show("First action", PointF.Empty, now: 0);
        const int RestartMilliseconds = FadeMidpointMilliseconds;
        feedback.Tick(RestartMilliseconds);
        feedback.Show("Second action", PointF.Empty, RestartMilliseconds);

        Assert.Equal(1, feedback.Opacity);
        feedback.Tick(RestartMilliseconds + HoldEndMilliseconds);
        Assert.Equal(1, feedback.Opacity);
        feedback.Tick(RestartMilliseconds + FadeMidpointMilliseconds);
        Assert.Equal(0.5f, feedback.Opacity);
        feedback.Tick(RestartMilliseconds + DisappearMilliseconds);
        Assert.False(feedback.IsVisible);
    }
}
