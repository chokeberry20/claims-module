using System.Text.Json;

namespace ClaimsModule.IntegrationTests;

/// <summary>
/// Every committed JSON settings file must parse, and the SQL Express connection strings must
/// survive JSON unescaping. A regression guard: P0 committed ".\SQLEXPRESS01" with one
/// backslash, which is an invalid JSON escape (see docs/ai-log.md, C-13).
/// </summary>
public sealed class ConfigurationFilesTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    public static TheoryData<string> SettingsFiles()
    {
        var data = new TheoryData<string>();
        foreach (var dir in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.json", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                data.Add(Path.GetRelativePath(RepoRoot, file));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SettingsFiles))]
    public void Settings_file_is_valid_json(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        Should.NotThrow(() => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }));
    }

    [Theory]
    [InlineData("src/ClaimsModule.API/appsettings.Development.json", "ClaimsModule")]
    [InlineData("tests/ClaimsModule.IntegrationTests/testsettings.json", "ClaimsModule_Tests")]
    public void Local_connection_string_targets_sql_express_instance(string relativePath, string database)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, relativePath)));

        var connectionString = doc.RootElement.GetProperty("ConnectionStrings").GetProperty("ClaimsDb").GetString();

        connectionString.ShouldStartWith(@"Server=.\SQLEXPRESS01;");
        connectionString.ShouldContain($"Database={database};");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaimsModule.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("ClaimsModule.sln not found above test output directory.");
    }
}
