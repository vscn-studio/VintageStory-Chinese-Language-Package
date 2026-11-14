using System.Text;

namespace Packer;

public static class CliRunner
{
    private const string Usage =
        "Usage: dotnet run --project src/Packer -- <pack|inspect|describe-release|describe-package> --config <path> [--package-version <value>] [--release-kind <release|pre-release>] [--previous-package <zip>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(args, out var command, out var configPath, out var packageVersion, out var releaseKind, out var previousPackage, out var parseError))
        {
            await stderr.WriteLineAsync(parseError ?? Usage);
            return 1;
        }

        try
        {
            var config = await PackerConfigLoader.LoadAsync(configPath!, repositoryRoot, cancellationToken);
            if (string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase))
            {
                var inspection = TranslationPackBuilder.Inspect(config, repositoryRoot);
                await stdout.WriteLineAsync($"selected_translation_count={inspection.SelectedTranslationCount}");
                await stdout.WriteLineAsync($"skipped_directory_count={inspection.SkippedDirectoryCount}");
                await stdout.WriteLineAsync($"release_milestone_count={inspection.ReleaseMilestoneCount}");
                await stdout.WriteLineAsync($"recommended_package_version={inspection.RecommendedPackageVersion}");
                await stdout.WriteLineAsync($"game_translation_included={inspection.GameTranslationIncluded.ToString().ToLowerInvariant()}");
                if (inspection.TargetGameVersion is not null)
                {
                    await stdout.WriteLineAsync($"target_game_version={inspection.TargetGameVersion}");
                }
                return 0;
            }

            if (string.Equals(command, "describe-release", StringComparison.OrdinalIgnoreCase))
            {
                config.PackageVersion = packageVersion!;
                config.Validate();
                var packagePath = Path.Combine(PackerConfigLoader.ResolvePath(config.OutputDirectory, repositoryRoot), config.ResolveOutputFileName());
                var current = PackageHistory.Read(packagePath);
                if (current.PackageVersion != config.PackageVersion || current.ModId != config.ModId)
                    throw new PackerException("Current package does not match the requested version or mod ID.");
                var previous = previousPackage is null ? null : PackageHistory.Read(PackerConfigLoader.ResolvePath(previousPackage, repositoryRoot));
                await stdout.WriteAsync(PackageHistory.FormatChanges(current, previous, releaseKind!));
                return 0;
            }

            if (string.Equals(command, "describe-package", StringComparison.OrdinalIgnoreCase))
            {
                var description = TranslationPackBuilder.DescribeReleasePackage(
                    config,
                    repositoryRoot,
                    packageVersion!);
                var gameMetadata = await LoadGameTranslationMetadataAsync(
                    config,
                    repositoryRoot,
                    description.GameTranslation,
                    cancellationToken);
                await stdout.WriteAsync(FormatReleasePackageDescription(description, gameMetadata, "release"));
                return 0;
            }

            if (packageVersion is not null)
            {
                config.PackageVersion = packageVersion.Trim();
                config.Validate();
            }

            var result = await TranslationPackBuilder.BuildAsync(config, repositoryRoot, cancellationToken);
            var relativeOutputPath = Path.GetRelativePath(repositoryRoot, result.OutputZipPath);

            await stdout.WriteLineAsync(
                $"Packed {result.SelectedTranslationCount} translation file(s) to {relativeOutputPath}.");

            if (result.GameTranslationIncluded)
            {
                await stdout.WriteLineAsync(
                    $"Included Vintage Story {result.TargetGameVersion} game translation.");
            }

            if (result.SkippedDirectoryCount > 0)
            {
                await stdout.WriteLineAsync(
                    $"Skipped {result.SkippedDirectoryCount} directory(s) without {config.TargetLanguage}.json.");
            }

            return 0;
        }
        catch (PackerException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? command,
        out string? configPath,
        out string? packageVersion,
        out string? releaseKind,
        out string? previousPackage,
        out string? error)
    {
        command = null;
        configPath = null;
        packageVersion = null;
        releaseKind = null;
        previousPackage = null;
        error = null;

        if (args.Length == 0)
        {
            error = Usage;
            return false;
        }

        if (!string.Equals(args[0], "pack", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "describe-release", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "describe-package", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unknown command '{args[0]}'.{Environment.NewLine}{Usage}";
            return false;
        }

        command = args[0];

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for --config.{Environment.NewLine}{Usage}";
                    return false;
                }

                configPath = args[++i];
                continue;
            }

            if (string.Equals(arg, "--package-version", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for --package-version.{Environment.NewLine}{Usage}";
                    return false;
                }

                packageVersion = args[++i];
                continue;
            }

            if (string.Equals(arg, "--release-kind", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for --release-kind.{Environment.NewLine}{Usage}";
                    return false;
                }

                releaseKind = args[++i].Trim();
                if (!string.Equals(releaseKind, "release", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(releaseKind, "pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Invalid value for --release-kind.{Environment.NewLine}{Usage}";
                    return false;
                }
                continue;
            }

            if (string.Equals(arg, "--previous-package", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    error = $"Missing value for --previous-package.{Environment.NewLine}{Usage}";
                    return false;
                }
                previousPackage = args[++i];
                continue;
            }

            error = $"Unknown argument '{arg}'.{Environment.NewLine}{Usage}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            error = $"Missing required --config argument.{Environment.NewLine}{Usage}";
            return false;
        }

        if (string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(packageVersion))
        {
            error = $"--package-version is only supported by the pack command.{Environment.NewLine}{Usage}";
            return false;
        }

        if (previousPackage is not null && !string.Equals(command, "describe-release", StringComparison.OrdinalIgnoreCase))
        {
            error = "--previous-package is only supported by describe-release.";
            return false;
        }

        if (string.Equals(command, "describe-release", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(releaseKind))
            {
                error = $"Missing required --release-kind argument.{Environment.NewLine}{Usage}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                error = $"Missing required --package-version argument.{Environment.NewLine}{Usage}";
                return false;
            }
        }

        if (string.Equals(command, "describe-package", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                error = $"Missing required --package-version argument.{Environment.NewLine}{Usage}";
                return false;
            }
        }

        return true;
    }

    private static async Task<GameTranslationMetadata?> LoadGameTranslationMetadataAsync(
        PackerConfig config,
        string repositoryRoot,
        GameTranslationEntry? gameTranslation,
        CancellationToken cancellationToken)
    {
        if (gameTranslation is null || config.GameTranslation is null)
        {
            return null;
        }

        var contentRoot = PackerConfigLoader.ResolvePath(config.GameTranslation.ContentRoot, repositoryRoot);
        return await GameTranslationMetadataProvider.LoadAsync(
            contentRoot,
            gameTranslation.TargetGameVersion,
            cancellationToken);
    }

    private static string FormatReleasePackageDescription(
        ReleasePackageDescription description,
        GameTranslationMetadata? gameMetadata,
        string releaseKind)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# VSCN Vintage Story 汉化包");
        builder.AppendLine();
        builder.AppendLine($"语言包版本：{description.PackageVersion}");
        builder.AppendLine($"发布类型：{releaseKind}");
        builder.AppendLine();
        builder.AppendLine("## 游戏本体翻译");
        builder.AppendLine();
        AppendGameTranslationTable(builder, description.GameTranslation, gameMetadata);
        builder.AppendLine();
        builder.AppendLine($"入包模组翻译数量：{description.SelectedTranslationCount}");
        builder.AppendLine($"跳过缺少 zh-cn.json 的目录：{description.SkippedDirectoryCount}");
        builder.AppendLine();
        builder.AppendLine("## 模组清单");
        builder.AppendLine();
        AppendEntriesTable(builder, description.Entries);
        builder.AppendLine();
        builder.AppendLine("## 贡献者统计");
        builder.AppendLine();
        AppendContributorStatsTable(builder, description.Entries, description.GameTranslation, gameMetadata);

        return builder.ToString();
    }

    private static void AppendGameTranslationTable(
        StringBuilder builder,
        GameTranslationEntry? gameTranslation,
        GameTranslationMetadata? gameMetadata)
    {
        if (gameTranslation is null)
        {
            builder.AppendLine("未启用");
            return;
        }

        builder.AppendLine("| 游戏版本 | 贡献者 |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine(
            $"| Vintage Story {EscapeMarkdownTableCell(gameTranslation.TargetGameVersion)} | " +
            $"{FormatContributors(gameMetadata?.Contributors ?? Array.Empty<ModContributor>())} |");
    }

    private static void AppendEntriesTable(
        StringBuilder builder,
        IReadOnlyList<ReleaseMilestoneEntry> entries)
    {
        builder.AppendLine("| 模组中文名称 | 模组英文名称 | 模组ID | 贡献者 | 目标模组版本 |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var entry in entries)
        {
            var item = ModMetadataProvider.ResolveEntryMetadata(entry);
            builder.AppendLine(
                $"| {FormatLinkedName(item.ChineseName, item.Homepage)} | {FormatLinkedName(item.EnglishName, item.Homepage)} | {EscapeMarkdownTableCell(item.ModId)} | {FormatContributors(item.Contributors)} | {EscapeMarkdownTableCell(item.LatestVersion)} |");
        }
    }

    private static void AppendContributorStatsTable(
        StringBuilder builder,
        IReadOnlyList<ReleaseMilestoneEntry> entries,
        GameTranslationEntry? gameTranslation,
        GameTranslationMetadata? gameMetadata)
    {
        var stats = new Dictionary<string, ContributorStat>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var item = ModMetadataProvider.ResolveEntryMetadata(entry);
            AddContributorStats(stats, item.Contributors);
        }

        if (gameTranslation is not null && gameMetadata is not null)
        {
            AddContributorStats(stats, gameMetadata.Contributors);
        }

        if (stats.Count == 0)
        {
            builder.AppendLine("- 无");
            return;
        }

        builder.AppendLine("| 贡献者 | 角色 | 翻译数量 |");
        builder.AppendLine("| --- | --- | --- |");

        foreach (var stat in stats.Values
                     .OrderByDescending(item => item.Count)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var roles = stat.Roles.Count > 0
                ? string.Join("<br>", stat.Roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).Select(EscapeMarkdownTableCell))
                : "未记录";
            builder.AppendLine(
                $"| {FormatLinkedName(stat.Name, stat.Url)} | {roles} | {stat.Count} |");
        }
    }

    private static void AddContributorStats(
        IDictionary<string, ContributorStat> stats,
        IEnumerable<ModContributor> contributors)
    {
        foreach (var contributor in contributors)
        {
            if (string.IsNullOrWhiteSpace(contributor.Name))
            {
                continue;
            }

            var key = $"{contributor.Name}\n{contributor.Url}";
            if (!stats.TryGetValue(key, out var stat))
            {
                stat = new ContributorStat(contributor.Name, contributor.Url);
                stats[key] = stat;
            }

            stat.Count++;
            if (!string.IsNullOrWhiteSpace(contributor.Role))
            {
                stat.Roles.Add(contributor.Role.Trim());
            }
        }
    }

    private static string FormatContributors(IReadOnlyCollection<ModContributor> contributors)
    {
        if (contributors.Count == 0)
        {
            return "未记录";
        }

        return string.Join("<br>", contributors.Select(FormatContributor));
    }

    private static string FormatContributor(ModContributor contributor)
    {
        var name = FormatLinkedName(contributor.Name, contributor.Url);
        return string.IsNullOrWhiteSpace(contributor.Role)
            ? name
            : $"{name} ({EscapeMarkdownTableCell(contributor.Role)})";
    }

    private static string FormatLinkedName(string name, string homepage)
    {
        var escapedName = EscapeMarkdownTableCell(name);
        if (string.IsNullOrWhiteSpace(homepage))
        {
            return escapedName;
        }

        var escapedHomepage = homepage
            .Replace(")", "%29", StringComparison.Ordinal)
            .Replace("(", "%28", StringComparison.Ordinal);
        return $"[{escapedName}]({escapedHomepage})";
    }

    private static string EscapeMarkdownTableCell(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed class ContributorStat(string name, string url)
    {
        public string Name { get; } = name;

        public string Url { get; } = url;

        public int Count { get; set; }

        public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
