using WinBGMuter.State;
using Xunit;

namespace WinBGMute.Tests.State;

public sealed class PlaybackStateStoreTests
{
    [Fact]
    public void TryGetPaused_ReturnsFalseForUntrackedPid()
    {
        var store = new PlaybackStateStore();

        var found = store.TryGetPaused(Environment.ProcessId, out var state);

        Assert.False(found);
        Assert.Null(state);
    }

    [Fact]
    public void MarkPaused_TracksCurrentProcessWithMethodSessionKeyAndTimestamp()
    {
        var store = new PlaybackStateStore();
        var before = DateTimeOffset.UtcNow;

        store.MarkPaused(Environment.ProcessId, "media", "session-1");
        var found = store.TryGetPaused(Environment.ProcessId, out var state);

        var after = DateTimeOffset.UtcNow;
        Assert.True(found);
        Assert.Equal("media", state.Method);
        Assert.Equal("session-1", state.SessionKey);
        Assert.InRange(state.PausedAtUtc, before, after);
    }

    [Fact]
    public void MarkPaused_OverwritesExistingStateForSamePid()
    {
        var store = new PlaybackStateStore();
        var pid = Environment.ProcessId;

        store.MarkPaused(pid, "first", "session-1");
        store.MarkPaused(pid, "second", "session-2");
        var found = store.TryGetPaused(pid, out var state);

        Assert.True(found);
        Assert.Equal("second", state.Method);
        Assert.Equal("session-2", state.SessionKey);
    }

    [Fact]
    public void Clear_RemovesTrackedPid()
    {
        var store = new PlaybackStateStore();
        var pid = Environment.ProcessId;

        store.MarkPaused(pid, "media", "session-1");
        store.Clear(pid);
        var found = store.TryGetPaused(pid, out var state);

        Assert.False(found);
        Assert.Null(state);
    }

    [Fact]
    public void Clear_UntrackedPid_DoesNotAffectOtherTrackedPids()
    {
        var store = new PlaybackStateStore();
        var pid = Environment.ProcessId;

        store.MarkPaused(pid, "media", "session-1");
        store.Clear(-12345);

        Assert.True(store.TryGetPaused(pid, out var state));
        Assert.Equal("session-1", state.SessionKey);
    }

    [Fact]
    public void GetTrackedPids_ReturnsMarkedPids()
    {
        var store = new PlaybackStateStore();
        var pid = Environment.ProcessId;

        store.MarkPaused(pid, "media", "session-1");

        Assert.Equal(new[] { pid }, store.GetTrackedPids().ToArray());
    }

    [Fact]
    public void MarkPaused_InvalidPid_CanBeReadBecauseNoStartTimeWasCaptured()
    {
        var store = new PlaybackStateStore();
        const int pid = -12345;

        store.MarkPaused(pid, "media", "missing-session");
        var found = store.TryGetPaused(pid, out var state);

        Assert.True(found);
        Assert.Equal("media", state.Method);
        Assert.Equal("missing-session", state.SessionKey);
    }

    [Fact]
    public void CleanupExitedProcesses_RemovesInvalidPid()
    {
        var store = new PlaybackStateStore();
        const int invalidPid = -12345;

        store.MarkPaused(invalidPid, "media", "missing-session");
        store.CleanupExitedProcesses();

        Assert.Empty(store.GetTrackedPids());
    }
}
