using System.Text.RegularExpressions;

namespace Maf.Lab.Api.Agent;

public enum Intent
{
    /// <summary>How / why / procedure / explain — documentation.</summary>
    Procedural,
    /// <summary>Procedural question about a specific run ("why did run 4417 fail").</summary>
    Mixed,
    /// <summary>Current data: run status, lists of runs.</summary>
    Data,
    /// <summary>Greetings, thanks, closings.</summary>
    ChitChat,
    Other,
}

/// <summary>
/// Rules-based intent detection that runs before the first model call. Only Procedural and Mixed force
/// search_documents, and only for the current turn.
/// </summary>
public static partial class IntentClassifier
{
    public static Intent Classify(string question)
    {
        var q = question.Trim().ToLowerInvariant();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (words <= 8 && ChitChat().IsMatch(q) && !Procedural().IsMatch(q))
        {
            return Intent.ChitChat;
        }
        var procedural = Procedural().IsMatch(q);
        var runRef = RunReference().IsMatch(q);
        if (procedural && runRef)
        {
            return Intent.Mixed;
        }
        if (procedural)
        {
            return Intent.Procedural;
        }
        if (runRef || DataWords().IsMatch(q))
        {
            return Intent.Data;
        }
        return Intent.Other;
    }

    public static bool ForcesRetrieval(Intent intent) => intent is Intent.Procedural or Intent.Mixed;

    public static bool IsHowWhy(Intent intent) => intent is Intent.Procedural or Intent.Mixed;

    [GeneratedRegex(@"^(thanks|thank you|thx|ok|okay|great|perfect|bye|goodbye|hello|hi|hey|cheers|got it)\b")]
    private static partial Regex ChitChat();

    [GeneratedRegex(@"\b(how|why|procedure|process|steps?|explain|what is|what's|what are|what does|define|definition|meaning|policy|what should|what to do|when should|guide)\b")]
    private static partial Regex Procedural();

    [GeneratedRegex(@"\brun\s*#?\s*\d{3,}\b|#\d{3,}\b")]
    private static partial Regex RunReference();

    [GeneratedRegex(@"\b(status|state of|which runs|list (the )?runs|show (me )?(the )?runs|failed runs|pending runs|latest runs?|runs? (that )?failed)\b")]
    private static partial Regex DataWords();
}
