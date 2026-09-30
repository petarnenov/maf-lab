using Maf.Lab.Api.Coverage.Fixtures;

namespace Maf.Lab.Tests;

/// <summary>
/// The model-free end-to-end run's target (make ci-e2e): the sign of a number and clamping it into a range.
/// </summary>
public class E2eTargetTests
{
    [Theory]
    [InlineData(-1, "negative")]
    [InlineData(-1000, "negative")]
    [InlineData(0, "zero")]
    [InlineData(1, "positive")]
    [InlineData(1000, "positive")]
    public void Sign_names_the_side_of_zero(int value, string expected)
    {
        Assert.Equal(expected, E2eTarget.Sign(value));
    }

    [Fact]
    public void Sign_treats_the_smallest_negative_number_as_negative()
    {
        Assert.Equal("negative", E2eTarget.Sign(int.MinValue));
    }

    [Fact]
    public void Clamp_leaves_a_value_inside_the_range_alone()
    {
        Assert.Equal(5, E2eTarget.Clamp(5, 0, 10));
    }

    [Fact]
    public void Clamp_raises_a_value_below_the_minimum_to_the_minimum()
    {
        Assert.Equal(0, E2eTarget.Clamp(-3, 0, 10));
    }

    [Fact]
    public void Clamp_lowers_a_value_above_the_maximum_to_the_maximum()
    {
        Assert.Equal(10, E2eTarget.Clamp(42, 0, 10));
    }

    [Theory]
    [InlineData(0, 0, 10, 0)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(-5, -5, -1, -5)]
    [InlineData(-1, -5, -1, -1)]
    [InlineData(-3, -5, -1, -3)]
    public void Clamp_keeps_the_bounds_themselves(int value, int min, int max, int expected)
    {
        Assert.Equal(expected, E2eTarget.Clamp(value, min, max));
    }
}
