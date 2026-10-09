namespace Maf.Lab.Plugins.FeedbackReview;

public sealed class FeedbackReviewPlugin : IMafPlugin, IContributesEndpoints
{
    public const string PluginName = "feedback-review";
    public string Name => PluginName;
    public void MapEndpoints(IMafEndpoints endpoints) => endpoints.Routes.MapFeedbackReview();
}
