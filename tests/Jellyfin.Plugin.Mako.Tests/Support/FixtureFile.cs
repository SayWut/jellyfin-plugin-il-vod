namespace Jellyfin.Plugin.Mako.Tests.Support;

/// <summary>
/// Reads a committed test fixture by name, from the directory the test run copies
/// <c>Fixtures/</c> into.
/// </summary>
public static class FixtureFile
{
    public static string ReadText(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }
}
