using System.Globalization;
using DevForge.Desktop.Presentation;

namespace DevForge.E2ETests.Desktop;

public sealed class PresentationValueConverterTests
{
    [Theory]
    [InlineData(959d, "1100", true)]
    [InlineData(1099.99d, "1100", true)]
    [InlineData(1100d, "1100", false)]
    [InlineData(1280d, "1100", false)]
    public void WidthBreakpointIsStrictAndCultureInvariant(
        double value,
        string parameter,
        bool expected)
    {
        var sut = new DoubleLessThanConverter();

        Assert.Equal(
            expected,
            sut.Convert(
                value,
                typeof(bool),
                parameter,
                CultureInfo.GetCultureInfo("vi-VN")));
    }

    [Fact]
    public void WidthBreakpointRejectsInvalidValues()
    {
        var sut = new DoubleLessThanConverter();

        Assert.Equal(
            false,
            sut.Convert(
                "960",
                typeof(bool),
                "invalid",
                CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Dashboard", "Dashboard", true)]
    [InlineData("Dashboard", "Settings", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "Settings", false)]
    public void RouteEqualityRequiresExactlyTwoEqualValues(
        object? first,
        object? second,
        bool expected)
    {
        var sut = new EqualityMultiConverter();

        Assert.Equal(
            expected,
            sut.Convert(
                [first!, second!],
                typeof(bool),
                null,
                CultureInfo.InvariantCulture));
    }
}
