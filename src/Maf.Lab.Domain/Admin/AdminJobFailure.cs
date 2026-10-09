namespace Maf.Lab.Domain.Admin;

/// <summary>A job failure whose summary may be shown to its administrator.</summary>
public sealed class AdminJobFailure(string reason, Exception? inner = null) : Exception(reason, inner);
