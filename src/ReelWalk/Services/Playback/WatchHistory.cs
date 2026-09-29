using System.Collections.Generic;

namespace ReelWalk.Services.Playback;
internal sealed class WatchHistory
{
    private readonly List<int> back = new List<int>();
    private readonly List<int> forward = new List<int>();
    private int limit = 10;

    internal int RecentBack
    {
        get { return back.Count == 0 ? -1 : back[back.Count - 1]; }
    }

    internal int RecentForward
    {
        get { return forward.Count == 0 ? -1 : forward[forward.Count - 1]; }
    }

    // How many steps Back can return through.
    // limit below 1 becomes 10. Returns nothing.
    internal void SetLimit(int limit)
    {
        if (limit < 1)
            limit = 10;
        if (limit > 500)
            limit = 500;
        this.limit = limit;
        Trim(back);
        Trim(forward);
    }

    // Drops the trail after the playlist is rebuilt.
    // Returns nothing.
    internal void Clear()
    {
        back.Clear();
        forward.Clear();
    }

    // Remembers index before a new jump, and forgets the forward trail.
    // index is the file being left. Returns nothing.
    internal void NoteJump(int index)
    {
        Push(back, index);
        forward.Clear();
    }

    // Steps back one file and remembers the file just left.
    // current is updated. Returns false when the trail is empty.
    internal bool TryRetreat(ref int current)
    {
        if (back.Count == 0)
            return false;
        Push(forward, current);
        current = Pop(back);
        return true;
    }

    // Steps forward along files that Back just left.
    // current is updated. Returns false when there is nothing to return to.
    internal bool TryAdvance(ref int current)
    {
        if (forward.Count == 0)
            return false;
        Push(back, current);
        current = Pop(forward);
        return true;
    }

    // Walks back up to steps files.
    // current is updated. Returns how many steps were taken.
    internal int Retreat(ref int current, int steps)
    {
        int taken = 0;
        if (steps < 1)
            return 0;
        while (taken < steps && TryRetreat(ref current))
            taken++;
        return taken;
    }

    // Fixes indexes after a file is removed from the playlist.
    // removed is the old index. Returns nothing.
    internal void Shift(int removed)
    {
        Shift(back, removed);
        Shift(forward, removed);
    }

    // Keeps the newest indexes and drops the oldest past the limit.
    // stack is the trail. Returns nothing.
    private void Trim(List<int> stack)
    {
        while (stack.Count > limit)
            stack.RemoveAt(0);
    }

    // Adds index and trims to the limit.
    // Returns nothing.
    private void Push(List<int> stack, int index)
    {
        stack.Add(index);
        Trim(stack);
    }

    // Takes the newest index off the stack.
    // Returns that index.
    private static int Pop(List<int> stack)
    {
        int last = stack.Count - 1;
        int index = stack[last];
        stack.RemoveAt(last);
        return index;
    }

    // Removes an index and moves later indexes down by one.
    // Returns nothing.
    private static void Shift(List<int> stack, int removed)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == removed)
                stack.RemoveAt(i);
            else if (stack[i] > removed)
                stack[i]--;
        }
    }
}
