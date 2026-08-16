namespace VscnLanguagePackUpdater;

internal static class UpdaterConstants
{
    public const string UpdaterModId = "vscnlangpackupdater";
    public const string LanguagePackModId = "vscnlangpack";
    public const string LanguagePackAssetPrefix = "VintageStory-Chinese-Language-Package-";
    public const string LanguagePackAssetSuffix = ".zip";
    public const string ReleasesApiUrl = "https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest";
    public const string ReleasesLatestUrl = "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest";
    public const string VscnStudioLatestUrl = "https://vscn.studio/VintageStory-Chinese-Language-Package/releases/latest";
    public const string ReleaseAssetDownloadUrlTemplate = "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/download/{tag}/{asset}";
    public const string ConfigFileName = "vscnlangpackupdater.json";
    public const string UserAgent = "vscnlangpackupdater/0.1.0";

    public static readonly string[] GithubUrlTemplates =
    [
        "https://ghproxy.net/{url}",
        "https://gh-proxy.org/{url}",
        "https://v4.gh-proxy.org/{url}",
        "https://v6.gh-proxy.org/{url}",
        "https://axisnow.gh-proxy.org/{url}",
        "https://v6.gh-proxy.com/{url}",
        "https://hk.gh-proxy.com/{url}",
        "https://cdn.gh-proxy.com/{url}",
        "https://edgeone.gh-proxy.com/{url}",
        "{url}"
    ];

    public static readonly string[] LegacyGithubUrlTemplates =
    [
        "https://ghproxy.net/{url}",
        "{url}"
    ];

    public static readonly string[] DeprecatedGithubUrlTemplates =
    [
        "https://gh.llkk.cc/{url}",
        "https://ghfast.top/{url}",
        "https://gh-proxy.com/{url}"
    ];

    public static readonly string[] TrustedDownloadHosts =
    [
        "api.github.com",
        "github.com",
        "vscn.studio",
        "codeload.github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "raw.githubusercontent.com",
        "ghproxy.net",
        "gh-proxy.org",
        "v4.gh-proxy.org",
        "v6.gh-proxy.org",
        "axisnow.gh-proxy.org",
        "v6.gh-proxy.com",
        "hk.gh-proxy.com",
        "cdn.gh-proxy.com",
        "edgeone.gh-proxy.com"
    ];
}
