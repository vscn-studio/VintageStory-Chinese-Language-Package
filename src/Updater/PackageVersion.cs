using System.Globalization;
using System.Text.RegularExpressions;

namespace VscnLanguagePackUpdater;

internal sealed partial class PackageVersion : IComparable<PackageVersion>
{
    private PackageVersion(int major, int minor, int patch, string? prereleaseKind, int prereleaseNumber, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PrereleaseKind = prereleaseKind;
        PrereleaseNumber = prereleaseNumber;
        Text = text;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? PrereleaseKind { get; }

    public int PrereleaseNumber { get; }

    public string Text { get; }

    public bool IsPrerelease => PrereleaseKind is not null;

    public static bool TryParse(string? value, out PackageVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        var match = VersionRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        version = new PackageVersion(
            int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture),
            match.Groups["prekind"].Success ? match.Groups["prekind"].Value.ToLowerInvariant() : null,
            match.Groups["prenum"].Success ? int.Parse(match.Groups["prenum"].Value, CultureInfo.InvariantCulture) : 0,
            text);
        return true;
    }

    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;

        var prerelease = GetPrereleaseRank(PrereleaseKind).CompareTo(GetPrereleaseRank(other.PrereleaseKind));
        if (prerelease != 0) return prerelease;

        return PrereleaseNumber.CompareTo(other.PrereleaseNumber);
    }

    public override string ToString()
    {
        return Text;
    }

    private static int GetPrereleaseRank(string? kind)
    {
        return kind switch
        {
            "dev" => 0,
            "pre" => 1,
            "rc" => 2,
            null => 3,
            _ => -1
        };
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prekind>dev|pre|rc)\.(?<prenum>\d+))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
