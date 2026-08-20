using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class FileSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024, "10 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1509949, "1.4 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1 TB")]
    public void Sizes_read_the_way_a_person_would_say_them(long bytes, string expected) =>
        Assert.Equal(expected, FileSize.Describe(bytes));

    [Fact]
    public void Large_values_drop_the_decimal_rather_than_reading_as_noise()
    {
        Assert.Equal("50 MB", FileSize.Describe(50L * 1024 * 1024));
        Assert.DoesNotContain(".", FileSize.Describe(500L * 1024 * 1024));
    }

    [Fact]
    public void A_negative_size_is_blank_rather_than_nonsense() =>
        Assert.Equal(string.Empty, FileSize.Describe(-1));

    [Fact]
    public void A_missing_file_is_blank_rather_than_zero_bytes()
    {
        // A capture whose file has been moved away should not claim to be 0 B.
        Assert.Equal(string.Empty, FileSize.Describe(Path.Combine(Path.GetTempPath(), "cursorpocket-not-here.png")));
        Assert.Equal(string.Empty, FileSize.Describe(string.Empty));
    }

    [Fact]
    public void A_real_file_reports_its_size()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cursorpocket-size-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[2048]);
            Assert.Equal("2 KB", FileSize.Describe(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
