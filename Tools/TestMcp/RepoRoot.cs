namespace OnlineSeverGame.TestMcp;

public static class RepoRoot
{
    public const string Marker = "OnlineSeverGame.slnx";

    /// <summary>The nearest directory at or above <paramref name="start"/> that holds the solution file.</summary>
    public static string Find(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker))) return dir.FullName;
        return null;
    }
}
