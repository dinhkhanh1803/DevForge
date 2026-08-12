using DevForge.Desktop.Notifications;

namespace DevForge.E2ETests.Desktop;

public sealed class NotificationServiceTests
{
    [Fact]
    public void RetainsNewestTwenty()
    {
        var sut = new NotificationService();

        for (var index = 0; index < 21; index++)
        {
            Assert.True(sut.TryPublish(NotificationSeverity.Information, $"Message {index}"));
        }

        Assert.Equal(20, sut.Items.Count);
        Assert.Equal("Message 1", sut.Items[0].Message);
        Assert.Equal("Message 20", sut.Items[^1].Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nfeed")]
    [InlineData("Authorization: Bearer abc.def.ghi")]
    public void RejectsUnsafeMessages(string message)
    {
        var sut = new NotificationService();

        Assert.False(sut.TryPublish(NotificationSeverity.Error, message));
        Assert.Empty(sut.Items);
    }

    [Fact]
    public void RejectsUndefinedSeverityAndOverlongMessage()
    {
        var sut = new NotificationService();

        Assert.False(sut.TryPublish((NotificationSeverity)99, "Message"));
        Assert.False(sut.TryPublish(NotificationSeverity.Warning, new string('a', 257)));
        Assert.Empty(sut.Items);
    }
}
