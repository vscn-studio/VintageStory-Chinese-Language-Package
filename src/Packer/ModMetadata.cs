namespace Packer;

public sealed record ModContributor(string Name, string Url, string Role);

public sealed record ReleaseEntryMetadata(
    string ChineseName, string EnglishName, string ModId, string LatestVersion,
    string Homepage, ModContributor[] Contributors);

public static class ModMetadataProvider
{
    public static ReleaseEntryMetadata ResolveEntryMetadata(ReleaseMilestoneEntry entry) => new(
        entry.ProjectSlug, entry.ProjectSlug, entry.RealModId, entry.TargetModVersion,
        string.Empty, ReadContributors(entry.SourceDirectory));

    public static ModContributor[] ReadContributors(string sourceDirectory)
    {
        var path = Path.Combine(sourceDirectory, "README.txt");
        if (!File.Exists(path)) return [];
        var line = File.ReadLines(path).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return line?.Split('、', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new ModContributor(name, string.Empty, "汉化")).ToArray() ?? [];
    }
}
