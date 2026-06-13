namespace Packer;

public sealed record PackInspection(
    int SelectedTranslationCount,
    int SkippedDirectoryCount,
    int ReleaseMilestoneCount,
    string RecommendedPackageVersion);
