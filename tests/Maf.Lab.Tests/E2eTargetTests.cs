namespace Maf.Lab.Tests;

/// <summary>
/// The model-free end-to-end run's target: a tiny pure helper whose contract is its name —
/// Sign reports which side of zero a value sits on, Clamp pulls a value into a range.
/// </summary>
public class E2eTargetTests
{
    [Theory]
    [InlineData(-1, "negative")]
    [InlineData(-100, "negative")]
    [InlineData(0, "zero")]
    [InlineData(1, "positive")]
    [InlineData(42, "positive")]
    public void Sign_reports_which_side_of_zero_the_value_sits_on(int value, string expected)
    {
        Assert.Equal(expected, Maf.Lab.Api.Coverage.Fixtures.E2eTarget.Sign(value));
    }

    [Theory]
    [InlineData(5, 0, 10, 5)]
    [InlineData(0, 0, 10, 0)]
    [InlineData(-1, 0, 10, 0)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(11, 0, 10, 10)]
    public void Clamp_pulls_a_value_into_the_range(int value, int min, int max, int expected)
    {
        Assert.Equal(expected, Maf.Lab.Api.Coverage.Fixtures.E2eTarget.Clamp(value, min, max));
    }

    [Fact]
    public void Clamp_treats_the_bounds_as_inclusive_and_the_order_as_given()
    {
        // The bounds themselves are returned unchanged, and an inverted range simply clamps to it.
        Assert.Equal(3, Maf.Lab.Api.Coverage.Fixtures.E2eTarget.Clamp(3, 3, 3));
        Assert.Equal(7, Maf.Lab.Api.Coverage.Fixtures.E2eTarget.Clamp(3, 7, 2));
    }
}