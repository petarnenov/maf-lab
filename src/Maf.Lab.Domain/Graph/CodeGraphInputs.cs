namespace Maf.Lab.Indexing.Graph;
/// <summary>A C# file of the repository: its path from the root, and its text.</summary>
public sealed record CodeFile(string Path, string Text);

/// <summary>A project of the repository: its folder from the root, its name, and the projects it references.</summary>
public sealed record CodeProject(string Directory, string Name, IReadOnlyList<string> References);
