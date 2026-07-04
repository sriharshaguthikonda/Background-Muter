using Moq;
using WinBGMuter.Abstractions;
using WinBGMuter.Media;
using Xunit;

namespace WinBGMute.Tests.Media;

public sealed class SessionResolverTests
{
    [Fact]
    public async Task ResolveAsync_CanceledToken_ThrowsWithoutListingSessions()
    {
        var mediaController = new Mock<IMediaController>(MockBehavior.Strict);
        var resolver = new SessionResolver(mediaController.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("spotify", cts.Token));

        mediaController.Verify(
            controller => controller.ListSessionsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_NoSessions_ReturnsNotFound()
    {
        var mediaController = CreateController(Array.Empty<MediaSessionInfo>());
        var resolver = new SessionResolver(mediaController.Object);

        var resolution = await resolver.ResolveAsync("spotify");

        Assert.Equal(SessionResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_MatchesWhenSessionAppIdContainsProcessNameIgnoringCase()
    {
        var matchingSession = Session("session-1", "Spotify.exe");
        var mediaController = CreateController(new[]
        {
            Session("session-0", "Other.Player"),
            matchingSession,
        });
        var resolver = new SessionResolver(mediaController.Object);

        var resolution = await resolver.ResolveAsync("spotify");

        Assert.Equal(SessionResolutionStatus.Success, resolution.Status);
        Assert.Equal(matchingSession.Key, resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_MatchesWhenProcessNameContainsLastAppIdSegment()
    {
        var matchingSession = Session("session-1", "Contoso.Player");
        var mediaController = CreateController(new[] { matchingSession });
        var resolver = new SessionResolver(mediaController.Object);

        var resolution = await resolver.ResolveAsync("PlayerHost");

        Assert.Equal(SessionResolutionStatus.Success, resolution.Status);
        Assert.Equal(matchingSession.Key, resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_UsesHintedAppIdWhenProcessNameDoesNotMatch()
    {
        var matchingSession = Session("session-1", "Windows.Media.Player");
        var mediaController = CreateController(new[] { matchingSession });
        var resolver = new SessionResolver(
            mediaController.Object,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["legacy-video"] = "windows.media.player",
            });

        var resolution = await resolver.ResolveAsync("legacy-video");

        Assert.Equal(SessionResolutionStatus.Success, resolution.Status);
        Assert.Equal(matchingSession.Key, resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_UsesHintedSessionKeyWhenAppIdIsMissing()
    {
        var matchingSession = Session("session-1", null);
        var mediaController = CreateController(new[] { matchingSession });
        var resolver = new SessionResolver(
            mediaController.Object,
            new Dictionary<string, string>
            {
                ["vlc"] = "session-1",
            });

        var resolution = await resolver.ResolveAsync("vlc");

        Assert.Equal(SessionResolutionStatus.Success, resolution.Status);
        Assert.Equal(matchingSession.Key, resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_NoProcessOrHintMatch_ReturnsNotFoundWithoutFallback()
    {
        var mediaController = CreateController(new[]
        {
            Session("session-1", "Spotify.exe"),
            Session("session-2", "Chrome.exe"),
        });
        var resolver = new SessionResolver(mediaController.Object);

        var resolution = await resolver.ResolveAsync("notepad");

        Assert.Equal(SessionResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Session);
    }

    [Fact]
    public async Task ResolveAsync_EmptyProcessName_MatchesFirstSessionWithNonEmptyAppId()
    {
        var firstSessionWithAppId = Session("session-2", "Spotify.exe");
        var mediaController = CreateController(new[]
        {
            Session("session-1", null),
            firstSessionWithAppId,
            Session("session-3", "Chrome.exe"),
        });
        var resolver = new SessionResolver(mediaController.Object);

        var resolution = await resolver.ResolveAsync(string.Empty);

        Assert.Equal(SessionResolutionStatus.Success, resolution.Status);
        Assert.Equal(firstSessionWithAppId.Key, resolution.Session);
    }

    private static Mock<IMediaController> CreateController(IReadOnlyList<MediaSessionInfo> sessions)
    {
        var mediaController = new Mock<IMediaController>(MockBehavior.Strict);
        mediaController
            .Setup(controller => controller.ListSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);
        return mediaController;
    }

    private static MediaSessionInfo Session(string id, string? appId)
        => new(
            new MediaSessionKey(id, appId),
            displayName: appId,
            appId: appId,
            MediaPlaybackState.Playing,
            DateTimeOffset.UtcNow);
}
