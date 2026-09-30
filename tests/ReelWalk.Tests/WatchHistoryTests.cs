using ReelWalk.Services.Playback;
using Xunit;

namespace ReelWalk.Tests;
public class WatchHistoryTests
{
    [Fact]
    public void Limit_BelowOneBecomesTen_AndAboveFiveHundredBecomesFiveHundred()
    {
        var history = new WatchHistory();
        history.SetLimit(0);
        for (int i = 0; i < 12; i++)
            history.NoteJump(i);
        int current = 99;
        Assert.Equal(10, history.Retreat(ref current, 20));

        history.Clear();
        history.SetLimit(900);
        for (int i = 0; i < 510; i++)
            history.NoteJump(i);
        current = 0;
        Assert.Equal(500, history.Retreat(ref current, 600));
    }

    [Fact]
    public void RetreatAndAdvance_WalkTheTrail_AndANewJumpClearsForward()
    {
        var history = new WatchHistory();
        history.SetLimit(10);
        history.NoteJump(1);
        history.NoteJump(2);
        int current = 3;
        Assert.True(history.TryRetreat(ref current));
        Assert.Equal(2, current);
        Assert.True(history.TryAdvance(ref current));
        Assert.Equal(3, current);

        history.NoteJump(3);
        Assert.Equal(-1, history.RecentForward);
        Assert.False(history.TryAdvance(ref current));
    }

    [Fact]
    public void EmptyTrail_DoesNotMove()
    {
        var history = new WatchHistory();
        int current = 4;
        Assert.False(history.TryRetreat(ref current));
        Assert.False(history.TryAdvance(ref current));
        Assert.Equal(0, history.Retreat(ref current, 0));
        Assert.Equal(4, current);
        Assert.Equal(-1, history.RecentBack);
    }

    [Fact]
    public void Shift_DropsTheRemovedIndex_AndMovesLaterOnesDown()
    {
        var history = new WatchHistory();
        history.NoteJump(1);
        history.NoteJump(3);
        history.NoteJump(3);
        history.NoteJump(5);
        history.Shift(3);

        int current = 9;
        Assert.True(history.TryRetreat(ref current));
        Assert.Equal(4, current);
        Assert.True(history.TryRetreat(ref current));
        Assert.Equal(1, current);
        Assert.False(history.TryRetreat(ref current));
    }

    [Fact]
    public void Clear_DropsBothDirections()
    {
        var history = new WatchHistory();
        history.NoteJump(2);
        int current = 3;
        history.TryRetreat(ref current);
        history.Clear();
        Assert.Equal(-1, history.RecentBack);
        Assert.Equal(-1, history.RecentForward);
    }
}
