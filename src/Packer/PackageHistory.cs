using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Packer;

public sealed record PackageRecord(int SchemaVersion, string ModId, string PackageVersion, PackageRecordEntry[] Entries);

public sealed record PackageRecordEntry(
    string Path, string Project, string ModId, string Version, string Sha256,
    ModContributor[] Contributors, bool IsGame);

public static class PackageHistory
{
    public const string RecordFileName = "assets/vscnlangpack/config/package-record.json";
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    public static PackageRecord Read(string path)
    {
        if (!File.Exists(path)) throw new PackerException($"Package does not exist: {path}");
        using var archive = ZipFile.OpenRead(path);
        var infoEntry = archive.GetEntry("modinfo.json") ?? throw new PackerException($"Missing modinfo.json: {path}");
        using var infoStream = infoEntry.Open();
        using var info = JsonDocument.Parse(infoStream);
        var modId = info.RootElement.GetProperty("modid").GetString()!;
        var version = info.RootElement.GetProperty("version").GetString()!;
        var recordEntry = archive.GetEntry(RecordFileName);
        if (recordEntry is not null)
        {
            using var stream = recordEntry.Open();
            var record = JsonSerializer.Deserialize<PackageRecord>(stream, JsonOptions);
            if (record is null || record.SchemaVersion != 1 || record.ModId != modId || record.PackageVersion != version ||
                record.Entries is null || record.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Path) ||
                    string.IsNullOrWhiteSpace(entry.ModId) || entry.Version is null || string.IsNullOrWhiteSpace(entry.Sha256) ||
                    entry.Contributors is null || entry.Contributors.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name))) ||
                record.Entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != record.Entries.Length)
                throw new PackerException($"Invalid or unsupported package record: {path}");
            foreach (var item in record.Entries)
            {
                var file = archive.GetEntry(item.Path) ?? throw new PackerException($"Recorded translation is missing from package: {item.Path}");
                using var content = file.Open();
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(content)), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PackerException($"Recorded translation hash does not match package: {item.Path}");
            }
            var languagePaths = archive.Entries.Where(entry => IsTranslation(entry.FullName)).Select(entry => entry.FullName).ToArray();
            if (languagePaths.Length != record.Entries.Length || languagePaths.Except(record.Entries.Select(entry => entry.Path), StringComparer.Ordinal).Any())
                throw new PackerException($"Package translations do not match the record: {path}");
            return record;
        }

        // Releases made before records were introduced can still be compared by their actual files.
        var entries = new List<PackageRecordEntry>();
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/');
            if (!IsTranslation(entry.FullName)) continue;
            using var stream = entry.Open();
            var isGame = parts[1] == "game";
            var targetVersion = isGame && info.RootElement.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.TryGetProperty("game", out var gameVersion) ? gameVersion.GetString() ?? "" : "";
            entries.Add(new PackageRecordEntry(entry.FullName, parts[1], parts[1], targetVersion,
                Convert.ToHexString(SHA256.HashData(stream)), [], isGame));
        }
        return new PackageRecord(0, modId, version, entries.ToArray());
    }

    public static string FormatChanges(PackageRecord current, PackageRecord? previous, string releaseKind)
    {
        if (previous is not null && !string.Equals(previous.ModId, current.ModId, StringComparison.OrdinalIgnoreCase))
            throw new PackerException("Cannot compare packages with different mod IDs.");
        var before = (previous?.Entries ?? []).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var after = current.Entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var rows = new List<string>();
        foreach (var path in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(path, out var oldEntry);
            after.TryGetValue(path, out var newEntry);
            var status = oldEntry is null ? "新增" : newEntry is null ? "移除" :
                !string.Equals(oldEntry.Sha256, newEntry.Sha256, StringComparison.OrdinalIgnoreCase) ||
                (oldEntry.Version.Length > 0 && oldEntry.Version != newEntry.Version) ||
                (previous!.SchemaVersion > 0 && !ContributorKeys(oldEntry).SequenceEqual(ContributorKeys(newEntry))) ? "更新" : "";
            if (status.Length == 0) continue;
            var entry = newEntry ?? oldEntry!;
            var name = entry.IsGame ? "游戏本体" : entry.ModId;
            var credits = entry.Contributors.Length == 0 ? "未记录" : string.Join("、", entry.Contributors.Select(item => item.Name));
            rows.Add($"| {status} | {Escape(name)} | {Escape(oldEntry?.Version ?? "—")} | {Escape(newEntry?.Version ?? "—")} | {Escape(credits)} |");
        }
        var builder = new StringBuilder();
        builder.AppendLine("# VSCN Vintage Story 汉化包");
        builder.AppendLine();
        builder.AppendLine($"语言包版本：{Escape(current.PackageVersion)}");
        builder.AppendLine($"发布类型：{Escape(releaseKind)}");
        builder.AppendLine($"对比版本：{(previous is null ? "首次打包" : Escape(previous.PackageVersion))}");
        builder.AppendLine($"入包模组翻译数量：{current.Entries.Count(entry => !entry.IsGame)}");
        builder.AppendLine();
        builder.AppendLine("## 翻译变更");
        builder.AppendLine();
        if (previous?.SchemaVersion == 0)
        {
            builder.AppendLine("上一版本没有打包记录，按 ZIP 中的译文内容比较；旧模组版本和汉化署名未知。");
            builder.AppendLine();
        }
        if (rows.Count == 0) builder.AppendLine("本次没有翻译变更。");
        else
        {
            builder.AppendLine("| 变更 | 模组 ID / 游戏本体 | 原目标版本 | 新目标版本 | 汉化作者 |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var row in rows) builder.AppendLine(row);
        }
        return builder.ToString();
    }

    private static IEnumerable<string> ContributorKeys(PackageRecordEntry entry) => entry.Contributors
        .Select(item => $"{item.Name}\n{item.Url}\n{item.Role}").Order(StringComparer.Ordinal);

    private static bool IsTranslation(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 4 && parts[0] == "assets" && parts[2] == "lang" && parts[3].EndsWith(".json", StringComparison.Ordinal);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("|", "\\|")
        .Replace("\r", " ").Replace("\n", " ");
}
