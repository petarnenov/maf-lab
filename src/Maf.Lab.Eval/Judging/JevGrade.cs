namespace Maf.Lab.Eval.Judging;

/// <summary>
/// One answer's grade, computed by code from Jev's yes/no answers (design D4). Shares, never Jev's arithmetic: a Noul
/// counts as yes at <see cref="Yes"/>, and the answers between <see cref="BandLow"/> and <see cref="BandHigh"/> are
/// counted, not decided on.
/// </summary>
/// <param name="Faithfulness">Supported claim sentences over claim sentences; 1 when no sentence makes a claim.</param>
/// <param name="Relevance">1 when the answer addresses the question, else 0.</param>
/// <param name="Completeness">Reference points stated over reference points; null when the case has none.</param>
/// <param name="ReferenceAgreement">1 minus the share of reference points contradicted; null when the case has none.</param>
/// <param name="RetrievalJudged">Sources on the question's subject over sources sent; 1 when none was read.</param>
/// <param name="Uncertain">The share of the case's answers in the review band.</param>
/// <param name="Unsupported">The claim sentences graded unsupported, in order.</param>
/// <param name="Missed">The reference points graded not stated.</param>
/// <param name="Contradicted">The reference points graded contradicted.</param>
/// <param name="OffSubject">How many sources were graded off the subject.</param>
/// <param name="Stated">Per reference point, whether it was graded stated (for the labelled suite).</param>
/// <param name="PointContradicted">Per reference point, whether it was graded contradicted.</param>
/// <param name="Answers">Every probability by question id: the record behind the numbers.</param>
public sealed record JevGrade(
    double Faithfulness,
    double Relevance,
    double? Completeness,
    double? ReferenceAgreement,
    double RetrievalJudged,
    double Uncertain,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> Missed,
    IReadOnlyList<string> Contradicted,
    int OffSubject,
    IReadOnlyList<bool> Stated,
    IReadOnlyList<bool> PointContradicted,
    IReadOnlyDictionary<string, double> Answers)
{
    /// <summary>Where Jev finds yes likelier than no (§4.5). A read-only eval: the lowest-risk class.</summary>
    public const double Yes = 0.5;

    public const double BandLow = 0.2;

    public const double BandHigh = 0.8;

    /// <summary>
    /// Reads the answers back by the ids the request asked. A question with no answer reads as no — an unsupported
    /// sentence, a missed point — so a partial response can only lower a score, never raise it.
    /// </summary>
    internal static JevGrade From(JevGradeRequest request, IReadOnlyDictionary<string, double> answers)
    {
        double P(string id) => answers.TryGetValue(id, out var p) ? p : 0;
        bool IsYes(string id) => P(id) >= Yes;

        var sentences = request.State.AnswerSentences;
        var points = request.State.ReferencePoints;
        var sources = request.State.Sources;

        var claims = Enumerable.Range(0, sentences.Count).Where(i => IsYes(JevGradeRequest.ClaimId(i))).ToList();
        var unsupported = claims.Where(i => !IsYes(JevGradeRequest.SupportedId(i))).Select(i => sentences[i]).ToList();
        var faithfulness = claims.Count == 0 ? 1 : (double)(claims.Count - unsupported.Count) / claims.Count;

        var stated = Enumerable.Range(0, points.Count).Select(j => IsYes(JevGradeRequest.StatedId(j))).ToList();
        var contradicted = Enumerable.Range(0, points.Count).Select(j => IsYes(JevGradeRequest.ContradictsId(j))).ToList();
        double? completeness = points.Count == 0 ? null : (double)stated.Count(s => s) / points.Count;
        double? agreement = points.Count == 0 ? null : 1 - (double)contradicted.Count(c => c) / points.Count;

        var offSubject = Enumerable.Range(0, sources.Count).Count(k => !IsYes(JevGradeRequest.OnSubjectId(k)));
        var retrieval = sources.Count == 0 ? 1 : (double)(sources.Count - offSubject) / sources.Count;

        var asked = request.Questions.Keys.ToList();
        var uncertain = asked.Count == 0 ? 0 : (double)asked.Count(id => P(id) is > BandLow and < BandHigh) / asked.Count;

        return new JevGrade(faithfulness, IsYes(JevGradeRequest.RelevantId) ? 1 : 0, completeness, agreement, retrieval, uncertain,
            unsupported,
            [.. points.Where((_, j) => !stated[j])],
            [.. points.Where((_, j) => contradicted[j])],
            offSubject, stated, contradicted,
            asked.ToDictionary(id => id, P));
    }

    /// <summary>The failure line's grade half: what failed and why, in the words of the answer and the points.</summary>
    public string Reason()
    {
        var parts = new List<string>();
        if (Unsupported.Count > 0)
        {
            parts.Add($"unsupported: {string.Join(" | ", Unsupported.Select(Clip))}");
        }
        if (Relevance < 1)
        {
            parts.Add("does not address the question");
        }
        if (Missed.Count > 0)
        {
            parts.Add($"missed: {string.Join(" | ", Missed.Select(Clip))}");
        }
        if (Contradicted.Count > 0)
        {
            parts.Add($"contradicted: {string.Join(" | ", Contradicted.Select(Clip))}");
        }
        if (OffSubject > 0)
        {
            parts.Add($"{OffSubject} source(s) off the subject");
        }
        return string.Join("; ", parts);
    }

    private static string Clip(string text) => text.Length <= 120 ? text : text[..117] + "…";
}
