using System.Globalization;
using System.Text.RegularExpressions;

namespace Packer;

public static partial class PackageVersionCalculator
{
    public static string GetReleasePackageVersion(int selectedTranslationCount)
    {
        if (selectedTranslationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedTranslationCount));
        }

        var milestoneIndex = selectedTranslationCount / 10;
        return $"0.0.{milestoneIndex}";
    }

    public static bool IsSupportedPackageVersion(string? packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
        {
            return false;
        }

        var match = SupportedPackageVersionRegex().Match(packageVersion.Trim());
        if (!match.Success)
        {
            return false;
        }

        return IsWithinVsModDbRange(match.Groups["major"].Value, 0xFFFF) &&
               IsWithinVsModDbRange(match.Groups["minor"].Value, 0xFFFF) &&
               IsWithinVsModDbRange(match.Groups["patch"].Value, 0xFFFF) &&
               (!match.Groups["prenum"].Success || IsWithinVsModDbRange(match.Groups["prenum"].Value, 0x0FFF));
    }

    private static bool IsWithinVsModDbRange(string value, int maxValue)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
               parsed >= 0 &&
               parsed <= maxValue;
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prekind>dev|pre|rc)\.(?<prenum>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedPackageVersionRegex();
}
