namespace Maf.Lab.Api.Coverage.Fixtures;

/// <summary>
/// The model-free end-to-end run's target (make ci-e2e): a small file with no tests of its own, which the CI stub model
/// covers completely. Nothing in the lab calls it.
/// </summary>
public static class E2eTarget
{
    public static string Sign(int value)
    {
        if (value < 0)
        {
            return "negative";
        }
        return value == 0 ? "zero" : "positive";
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}
