using System.Text;

namespace Packer;

public static class CliRunner
{
    private const string Usage =
        "Usage: dotnet run --project src/Packer -- <pack|inspect|describe-release> --config <path> [--package-version <value>] [--milestone <value>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(args, out var command, out var configPath, out var packageVersion, out var milestone, out var parseError))
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
                return 0;
            }

            if (string.Equals(command, "describe-release", StringComparison.OrdinalIgnoreCase))
            {
                var description = TranslationPackBuilder.DescribeReleaseMilestone(
                    config,
                    repositoryRoot,
                    milestone!.Value,
                    packageVersion!);
                await stdout.WriteAsync(FormatReleaseMilestoneDescription(description));
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
        out int? milestone,
        out string? error)
    {
        command = null;
        configPath = null;
        packageVersion = null;
        milestone = null;
        error = null;

        if (args.Length == 0)
        {
            error = Usage;
            return false;
        }

        if (!string.Equals(args[0], "pack", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "describe-release", StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(arg, "--milestone", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for --milestone.{Environment.NewLine}{Usage}";
                    return false;
                }

                if (!int.TryParse(args[++i], out var parsedMilestone))
                {
                    error = $"Invalid value for --milestone.{Environment.NewLine}{Usage}";
                    return false;
                }

                milestone = parsedMilestone;
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

        if (string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase) &&
            milestone is not null)
        {
            error = $"--milestone is not supported by the inspect command.{Environment.NewLine}{Usage}";
            return false;
        }

        if (string.Equals(command, "pack", StringComparison.OrdinalIgnoreCase) &&
            milestone is not null)
        {
            error = $"--milestone is only supported by the describe-release command.{Environment.NewLine}{Usage}";
            return false;
        }

        if (string.Equals(command, "describe-release", StringComparison.OrdinalIgnoreCase))
        {
            if (milestone is null)
            {
                error = $"Missing required --milestone argument.{Environment.NewLine}{Usage}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                error = $"Missing required --package-version argument.{Environment.NewLine}{Usage}";
                return false;
            }
        }

        return true;
    }

    private static string FormatReleaseMilestoneDescription(ReleaseMilestoneDescription description)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Auto-generated release for reaching {description.MilestoneCount} translated mod language files.");
        builder.AppendLine();
        builder.AppendLine($"Included packaged translation count: {description.SelectedTranslationCount}");
        builder.AppendLine($"Skipped directories without zh-cn.json: {description.SkippedDirectoryCount}");
        builder.AppendLine($"Package version: {description.PackageVersion}");
        builder.AppendLine();
        builder.AppendLine($"Milestone entries ({description.BatchStartIndex}-{description.BatchEndIndex}):");

        for (var i = 0; i < description.Entries.Count; i++)
        {
            var entry = description.Entries[i];
            builder.AppendLine(
                $"{i + 1}. mod-name: {entry.ProjectSlug} | mod-version: {entry.TargetModVersion} | modid: {entry.RealModId}");
        }

        return builder.ToString();
    }
}
