using System.Text.Json;

namespace Packer;

public sealed record GameTranslationMetadata(ModContributor[] Contributors);

public static class GameTranslationMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<GameTranslationMetadata> LoadAsync(
        string contentRoot,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);

        var indexPath = Path.Combine(contentRoot, "index.json");
        if (!File.Exists(indexPath))
        {
            return Empty();
        }

        try
        {
            await using var stream = File.OpenRead(indexPath);
            var index = await JsonSerializer.DeserializeAsync<Dictionary<string, GameTranslationMetadata>>(
                stream,
                JsonOptions,
                cancellationToken);

            if (index is null ||
                !index.TryGetValue(targetVersion.Trim(), out var metadata))
            {
                return Empty();
            }

            return Normalize(metadata);
        }
        catch (JsonException ex)
        {
            throw new PackerException($"Invalid game translation metadata index '{indexPath}': {ex.Message}");
        }
    }

    private static GameTranslationMetadata Empty() => new([]);

    private static GameTranslationMetadata Normalize(GameTranslationMetadata? metadata)
    {
        var contributors = metadata?.Contributors?
            .Select(contributor => new ModContributor(
                contributor.Name?.Trim() ?? string.Empty,
                contributor.Url?.Trim() ?? string.Empty,
                contributor.Role?.Trim() ?? string.Empty))
            .Where(contributor => !string.IsNullOrWhiteSpace(contributor.Name))
            .DistinctBy(
                contributor => $"{contributor.Name}\n{contributor.Url}\n{contributor.Role}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        return new GameTranslationMetadata(contributors);
    }
}
