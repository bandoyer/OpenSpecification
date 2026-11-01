using OpenSpecification.Paging;

namespace Tests.Paging;

public class PaginationSettingsTests
{
    [Fact]
    public void Default_Is10To100()
    {
        PaginationSettings.Default.DefaultPageSize.Should().Be(10);
        PaginationSettings.Default.PageSizeLimit.Should().Be(100);
    }

    [Fact]
    public void Constructor_KeepsTheValuesItIsGiven()
    {
        var settings = new PaginationSettings(5, 20);

        settings.DefaultPageSize.Should().Be(5);
        settings.PageSizeLimit.Should().Be(20);
    }

    [Fact]
    public void Constructor_AcceptsALimitEqualToTheDefault()
    {
        var settings = new PaginationSettings(1, 1);

        settings.PageSizeLimit.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_GivenDefaultPageSizeBelowOne(int defaultPageSize)
    {
        var action = () => new PaginationSettings(defaultPageSize, 10);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("defaultPageSize");
    }

    [Fact]
    public void Constructor_Throws_GivenLimitBelowTheDefault()
    {
        var action = () => new PaginationSettings(10, 5);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("pageSizeLimit");
    }
}
