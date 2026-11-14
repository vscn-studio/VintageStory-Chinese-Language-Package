using System.IO.Compression;
using System.Text.Json;
using Packer;

namespace Packer.Tests;

public sealed class PackerTests
{
    [Fact]
    public async Task BuildAsync_WritesOnlyHighestVersionAndModInfo()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/carryon/1.0.0/carryon/lang/zh-cn.json",
            """
            {
              "item.old": "旧版本"
            }
            """);
        workspace.WriteText(
            "projects/assets/carryon/1.2.0/carryon/lang/zh-cn.json",
            """
            {
              "item.new": "新版本"
            }
            """);
        workspace.WriteText(
            "projects/assets/carryon/1.2.0/carryon/lang/en.json",
            """
            {
              "item.new": "new version"
            }
            """);

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);

        Assert.Equal(1, result.SelectedTranslationCount);
        Assert.True(File.Exists(result.OutputZipPath));

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        var entryNames = archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["assets/carryon/lang/zh-cn.json", PackageHistory.RecordFileName, "modinfo.json"],
            entryNames);

        using var translationReader = new StreamReader(archive.GetEntry("assets/carryon/lang/zh-cn.json")!.Open());
        var translationContent = await translationReader.ReadToEndAsync();
        Assert.Contains("\"item.new\": \"新版本\"", translationContent, StringComparison.Ordinal);
        Assert.DoesNotContain("new version", translationContent, StringComparison.Ordinal);

        using var modInfoReader = new StreamReader(archive.GetEntry("modinfo.json")!.Open());
        using var modInfoDocument = JsonDocument.Parse(await modInfoReader.ReadToEndAsync());
        var root = modInfoDocument.RootElement;
        Assert.Equal("content", root.GetProperty("type").GetString());
        Assert.Equal("client", root.GetProperty("side").GetString());
        Assert.Equal("vscnlangpack", root.GetProperty("modid").GetString());
        Assert.True(root.GetProperty("requiredOnClient").GetBoolean());
        Assert.False(root.GetProperty("requiredOnServer").GetBoolean());
    }

    [Fact]
    public async Task BuildAsync_IncludesConfiguredGameTranslationAndDependency()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "模组翻译"
            }
            """);
        workspace.WriteText(
            "projects/game/1.22.3/assets/game/lang/zh-cn.json",
            """
            {
              "mainmenu-singleplayer": "单人游戏"
            }
            """);

        var config = workspace.CreateConfig();
        config.GameTranslation = new GameTranslationConfig
        {
            ContentRoot = "projects/game",
            TargetVersion = "1.22.3"
        };

        var result = await TranslationPackBuilder.BuildAsync(config, workspace.RootPath);

        Assert.Equal(1, result.SelectedTranslationCount);
        Assert.True(result.GameTranslationIncluded);
        Assert.Equal("1.22.3", result.TargetGameVersion);

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        using var gameTranslationReader = new StreamReader(
            archive.GetEntry("assets/game/lang/zh-cn.json")!.Open());
        Assert.Contains("单人游戏", await gameTranslationReader.ReadToEndAsync(), StringComparison.Ordinal);
        Assert.NotNull(archive.GetEntry("assets/examplemod/lang/zh-cn.json"));

        using var modInfoReader = new StreamReader(archive.GetEntry("modinfo.json")!.Open());
        using var modInfoDocument = JsonDocument.Parse(await modInfoReader.ReadToEndAsync());
        Assert.Equal(
            "1.22.3",
            modInfoDocument.RootElement.GetProperty("dependencies").GetProperty("game").GetString());

        var description = TranslationPackBuilder.DescribeReleasePackage(config, workspace.RootPath, "0.0.1");
        Assert.Equal(1, description.SelectedTranslationCount);
        Assert.Single(description.Entries);
        Assert.Equal("1.22.3", description.GameTranslation?.TargetGameVersion);
    }

    [Fact]
    public async Task BuildAsync_UsesConfiguredGameVersionInsteadOfHighestVersion()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/game/1.22.3/assets/game/lang/zh-cn.json",
            """
            {
              "version": "目标版本"
            }
            """);
        workspace.WriteText(
            "projects/game/1.23.0/assets/game/lang/zh-cn.json",
            """
            {
              "version": "未来草稿"
            }
            """);

        var config = workspace.CreateConfig();
        config.GameTranslation = new GameTranslationConfig
        {
            ContentRoot = "projects/game",
            TargetVersion = "1.22.3"
        };

        var result = await TranslationPackBuilder.BuildAsync(config, workspace.RootPath);

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        using var translationReader = new StreamReader(
            archive.GetEntry("assets/game/lang/zh-cn.json")!.Open());
        var content = await translationReader.ReadToEndAsync();
        Assert.Contains("目标版本", content, StringComparison.Ordinal);
        Assert.DoesNotContain("未来草稿", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenConfiguredGameTranslationIsMissing()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "projects", "game", "1.22.3"));

        var config = workspace.CreateConfig();
        config.GameTranslation = new GameTranslationConfig
        {
            ContentRoot = "projects/game",
            TargetVersion = "1.22.3"
        };

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(config, workspace.RootPath));

        Assert.Contains("Game translation file does not exist", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1.22.3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenModTranslationConflictsWithGameDomain()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/game/lang/zh-cn.json",
            """
            {
              "source": "模组项目"
            }
            """);
        workspace.WriteText(
            "projects/game/1.22.3/assets/game/lang/zh-cn.json",
            """
            {
              "source": "本体项目"
            }
            """);

        var config = workspace.CreateConfig();
        config.GameTranslation = new GameTranslationConfig
        {
            ContentRoot = "projects/game",
            TargetVersion = "1.22.3"
        };

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(config, workspace.RootPath));

        Assert.Contains("conflicts with mod translation source", ex.Message, StringComparison.Ordinal);
        Assert.Contains("assets/game/lang/zh-cn.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_IgnoresDirectoriesWithoutTargetLanguage()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/en.json",
            """
            {
              "item.name": "Only source"
            }
            """);
        workspace.WriteText(
            "projects/assets/example/1.1.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "可打包"
            }
            """);

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);

        Assert.Equal(1, result.SelectedTranslationCount);
        Assert.Equal(1, result.SkippedDirectoryCount);
    }

    [Theory]
    [InlineData("builtin")]
    [InlineData("source")]
    [InlineData("decompiled")]
    public async Task BuildAsync_SkipsNonPackableMarkerLatestVersionWithoutFallingBack(string markerName)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "旧社区翻译"
            }
            """);
        workspace.WriteText($"projects/assets/example/1.1.0/examplemod/lang/{markerName}", string.Empty);

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);

        Assert.Equal(0, result.SelectedTranslationCount);
        Assert.Equal(0, result.SkippedDirectoryCount);

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        var entryNames = archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([PackageHistory.RecordFileName, "modinfo.json"], entryNames);
    }

    [Theory]
    [InlineData("builtin")]
    [InlineData("source")]
    [InlineData("decompiled")]
    public async Task BuildAsync_UsesCommunityTranslationWhenItIsNewerThanNonPackableMarker(string markerName)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText($"projects/assets/example/1.0.0/examplemod/lang/{markerName}", string.Empty);
        workspace.WriteText(
            "projects/assets/example/1.1.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "新社区翻译"
            }
            """);

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);

        Assert.Equal(1, result.SelectedTranslationCount);

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        Assert.NotNull(archive.GetEntry("assets/examplemod/lang/zh-cn.json"));
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenVersionCannotBeParsed()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "合法"
            }
            """);
        workspace.WriteText(
            "projects/assets/example/not-a-version/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "非法版本"
            }
            """);

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath));

        Assert.Contains("not valid NuGet/SemVer versions", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not-a-version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenNormalizedVersionsDuplicate()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.2.3/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "标准版本"
            }
            """);
        workspace.WriteText(
            "projects/assets/example/v1.2.3/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "带前缀版本"
            }
            """);

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath));

        Assert.Contains("normalized version '1.2.3'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenOutputPathConflictsByModIdCasing()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "小写"
            }
            """);
        workspace.WriteText(
            "projects/assets/example/2.0.0/ExampleMod/lang/zh-cn.json",
            """
            {
              "item.name": "大小写冲突"
            }
            """);

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath));

        Assert.Contains("same output path", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_AllowsJsonCommentsAndTrailingCommas()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              // section
              "item.name": "带注释",
            }
            """);

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);

        Assert.Equal(1, result.SelectedTranslationCount);

        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        using var translationReader = new StreamReader(archive.GetEntry("assets/examplemod/lang/zh-cn.json")!.Open());
        var translationContent = await translationReader.ReadToEndAsync();
        Assert.Contains("// section", translationContent, StringComparison.Ordinal);
        Assert.Contains("\"item.name\": \"带注释\"", translationContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenJsonIsInvalid()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "坏掉了"
              "item.other": "仍然坏着"
            }
            """);

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath));

        Assert.Contains("Invalid JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenJsonRootIsNotObject()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            [
              "not an object"
            ]
            """);

        var ex = await Assert.ThrowsAsync<PackerException>(
            () => TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath));

        Assert.Contains("JSON root must be an object", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_ReturnsNonZeroForInvalidArguments()
    {
        using var workspace = new TestWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(Array.Empty<string>(), stdout, stderr, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_PackCommand_CreatesZip()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "CLI"
            }
            """);

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["pack", "--config", configPath],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("Packed 1 translation file(s)", stdout.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "build", "VintageStory-Chinese-Language-Package-0.0.0.zip")));
    }

    [Fact]
    public async Task CliRunner_PackCommand_AllowsOverridingPackageVersion()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "CLI"
            }
            """);

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var version = "0.0.12-dev.1";

        var exitCode = await CliRunner.RunAsync(
            ["pack", "--config", configPath, "--package-version", version],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());

        var zipPath = Path.Combine(workspace.RootPath, "build", $"VintageStory-Chinese-Language-Package-{version}.zip");
        Assert.True(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        using var modInfoReader = new StreamReader(archive.GetEntry("modinfo.json")!.Open());
        using var modInfoDocument = JsonDocument.Parse(await modInfoReader.ReadToEndAsync());
        Assert.Equal(version, modInfoDocument.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task CliRunner_PackCommand_ReturnsNonZeroForInvalidPackageVersion()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "CLI"
            }
            """);

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["pack", "--config", configPath, "--package-version", "0.0.1-pr.1"],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("Vintage Story mod site version format", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_InspectCommand_ReturnsRecommendedReleaseVersionFromSelectedTranslations()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "v1"
            }
            """);
        workspace.WriteText(
            "projects/assets/example/1.1.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "v2"
            }
            """);

        for (var i = 0; i < 10; i++)
        {
            workspace.WriteText(
                $"projects/assets/mod{i}/1.0.0/mod{i}/lang/zh-cn.json",
                $$"""
                {
                  "item.name": "mod{{i}}"
                }
                """);
        }

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["inspect", "--config", configPath],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("selected_translation_count=11", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("release_milestone_count=10", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("recommended_package_version=0.0.1", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_DescribeReleaseCommand_ListsInitialAdditions()
    {
        using var workspace = new TestWorkspace();

        for (var i = 0; i < 10; i++)
        {
            workspace.WriteText(
                $"projects/assets/mod{i}/1.0.{i}/mod{i}/lang/zh-cn.json",
                $$"""
                {
                  "item.name": "mod{{i}}"
                }
                """);
        }

        workspace.WriteText(
            "projects/assets/zmod/9.9.9/zmod/lang/zh-cn.json",
            """
            {
              "item.name": "zmod"
            }
            """);

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("0.0.1"), workspace.RootPath);
        var exitCode = await CliRunner.RunAsync(
            ["describe-release", "--config", configPath, "--package-version", "0.0.1", "--release-kind", "release"],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("# VSCN Vintage Story 汉化包", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("语言包版本：0.0.1", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("发布类型：release", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("| 变更 | 模组 ID / 游戏本体 | 原目标版本 | 新目标版本 | 汉化作者 |", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("| 新增 | mod0 | — | 1.0.0 | 未记录 |", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("## 模组清单", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("zmod", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_DescribePackageCommand_WritesFullTable()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/betterloot/2.0.2/betterloot/lang/zh-cn.json",
            """
            {
              "item-gearpart": "更好的战利品"
            }
            """);

        var configPath = workspace.WriteConfigFile();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["describe-package", "--config", configPath, "--package-version", "0.0.1"],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("# VSCN Vintage Story 汉化包", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("## 模组清单", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("betterloot", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliRunner_DescribePackageCommand_ReportsGameTranslationSeparately()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "projects/assets/example/1.0.0/examplemod/lang/zh-cn.json",
            """
            {
              "item.name": "模组翻译"
            }
            """);
        workspace.WriteText(
            "projects/game/1.22.3/assets/game/lang/zh-cn.json",
            """
            {
              "mainmenu-singleplayer": "单人游戏"
            }
            """);
        workspace.WriteText(
            "projects/game/index.json",
            """
            {
              "1.22.3": {
                "contributors": [
                  {
                    "name": "GameTranslator",
                    "url": "https://example.com/game-translator",
                    "role": "游戏本体翻译"
                  }
                ]
              }
            }
            """);

        var configPath = workspace.WriteConfigFile(new GameTranslationConfig
        {
            ContentRoot = "projects/game",
            TargetVersion = "1.22.3"
        });
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["describe-package", "--config", configPath, "--package-version", "0.0.1"],
            stdout,
            stderr,
            workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("## 游戏本体翻译", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("| 游戏版本 | 贡献者 |", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "| Vintage Story 1.22.3 | [GameTranslator](https://example.com/game-translator) (游戏本体翻译) |",
            stdout.ToString(),
            StringComparison.Ordinal);
        Assert.Contains("入包模组翻译数量：1", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("| example | example | examplemod | 未记录 | 1.0.0 |", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "| [GameTranslator](https://example.com/game-translator) | 游戏本体翻译 | 1 |",
            stdout.ToString(),
            StringComparison.Ordinal);

        var releaseConfig = workspace.CreateConfig("0.0.1");
        releaseConfig.GameTranslation = new GameTranslationConfig { ContentRoot = "projects/game", TargetVersion = "1.22.3" };
        await TranslationPackBuilder.BuildAsync(releaseConfig, workspace.RootPath);
        var releaseStdout = new StringWriter();
        var releaseStderr = new StringWriter();
        var releaseExitCode = await CliRunner.RunAsync(
            ["describe-release", "--config", configPath, "--package-version", "0.0.1", "--release-kind", "release"],
            releaseStdout,
            releaseStderr,
            workspace.RootPath);

        Assert.Equal(0, releaseExitCode);
        Assert.Equal(string.Empty, releaseStderr.ToString());
        Assert.Contains(
            "| 新增 | 游戏本体 | — | 1.22.3 | GameTranslator |",
            releaseStdout.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("describe-package")]
    [InlineData("describe-release")]
    public async Task CliRunner_UsesSelectedReadmeWithoutIndex(string command)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("projects/assets/example/1.0.0/examplemod/lang/zh-cn.json", "{}");
        workspace.WriteText("projects/assets/example/1.0.0/examplemod/README.txt", "Old Author");
        workspace.WriteText("projects/assets/example/2.0.0/examplemod/lang/zh-cn.json", "{}");
        workspace.WriteText("projects/assets/example/2.0.0/examplemod/README.txt", "\uFEFF\n  HansJack、Alice  \nIgnored text");
        workspace.WriteText("projects/assets/example/2.0.0/othermod/lang/zh-cn.json", "{}");
        workspace.WriteText("projects/assets/example/2.0.0/othermod/README.txt", "Other | Author");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("0.0.1"), workspace.RootPath);
        var exitCode = await CliRunner.RunAsync(
            [command, "--config", workspace.WriteConfigFile(), "--package-version", "0.0.1", "--release-kind", "release"],
            stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        if (command == "describe-release")
        {
            Assert.Contains("| 新增 | examplemod | — | 2.0.0 | HansJack、Alice |", stdout.ToString());
            Assert.Contains("| 新增 | othermod | — | 2.0.0 | Other \\| Author |", stdout.ToString());
        }
        else
        {
            Assert.Contains("| example | example | examplemod | HansJack (汉化)<br>Alice (汉化) | 2.0.0 |", stdout.ToString());
            Assert.Contains("| HansJack | 汉化 | 1 |", stdout.ToString());
            Assert.Contains("| Alice | 汉化 | 1 |", stdout.ToString());
        }
        Assert.DoesNotContain("Old Author", stdout.ToString());
        Assert.DoesNotContain("Ignored text", stdout.ToString());

        var result = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig(), workspace.RootPath);
        using var archive = ZipFile.OpenRead(result.OutputZipPath);
        Assert.Equal(4, archive.Entries.Count);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("README.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" \n\t")]
    public void ReadmeWithoutCredits_ReturnsNoContributors(string? readme)
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Path.Combine(workspace.RootPath, "projects", "assets", "example", "1.0.0", "examplemod");
        if (readme is not null) workspace.WriteText("projects/assets/example/1.0.0/examplemod/README.txt", readme);
        Assert.Empty(ModMetadataProvider.ReadContributors(sourceDirectory));
    }

    [Fact]
    public async Task Release_UsesActualPackagesAndReportsOnlyChanges()
    {
        using var workspace = new TestWorkspace();
        foreach (var mod in new[] { "unchanged", "content", "version", "credits", "removed" })
        {
            workspace.WriteText($"projects/assets/{mod}/1.0.0/{mod}/lang/zh-cn.json", "{}");
            workspace.WriteText($"projects/assets/{mod}/1.0.0/{mod}/README.txt", "Old Translator");
        }
        var before = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.0"), workspace.RootPath);
        workspace.WriteText("projects/assets/content/1.0.0/content/lang/zh-cn.json", "{\"key\":\"修订\"}");
        workspace.WriteText("projects/assets/version/2.0.0/version/lang/zh-cn.json", "{}");
        workspace.WriteText("projects/assets/version/2.0.0/version/README.txt", "Old Translator");
        workspace.WriteText("projects/assets/credits/1.0.0/credits/README.txt", "New Translator");
        workspace.WriteText("projects/assets/added/1.0.0/added/lang/zh-cn.json", "{}");
        File.Delete(Path.Combine(workspace.RootPath, "projects/assets/removed/1.0.0/removed/lang/zh-cn.json"));
        var after = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.1"), workspace.RootPath);

        // Changes after packing must not leak into release notes or their credits.
        workspace.WriteText("projects/assets/unchanged/1.0.0/unchanged/lang/zh-cn.json", "{\"late\":\"修改\"}");
        workspace.WriteText("projects/assets/credits/1.0.0/credits/README.txt", "Unpacked Translator");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await CliRunner.RunAsync(
            ["describe-release", "--config", workspace.WriteConfigFile(), "--package-version", "1.0.1",
                "--release-kind", "release", "--previous-package", before.OutputZipPath],
            stdout, stderr, workspace.RootPath);
        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());
        var notes = stdout.ToString();
        Assert.Contains("对比版本：1.0.0", notes);
        Assert.Contains("| 新增 | added | — | 1.0.0 |", notes);
        Assert.Contains("| 更新 | content | 1.0.0 | 1.0.0 |", notes);
        Assert.Contains("| 更新 | version | 1.0.0 | 2.0.0 |", notes);
        Assert.Contains("| 更新 | credits | 1.0.0 | 1.0.0 | New Translator |", notes);
        Assert.Contains("| 移除 | removed | 1.0.0 | — | Old Translator |", notes);
        Assert.DoesNotContain("unchanged", notes);
        Assert.DoesNotContain("Unpacked Translator", notes);

        var record = PackageHistory.Read(after.OutputZipPath);
        using var zip = ZipFile.OpenRead(after.OutputZipPath);
        foreach (var item in record.Entries)
        {
            using var stream = zip.GetEntry(item.Path)!.Open();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            Assert.Equal(PackageHistory.Hash(bytes.ToArray()), item.Sha256);
            Assert.DoesNotContain(workspace.RootPath, JsonSerializer.Serialize(item));
        }
    }

    [Fact]
    public async Task Release_UnchangedPackagesHaveNoTable()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("projects/assets/example/1.0.0/example/lang/zh-cn.json", "{}");
        workspace.WriteText("projects/assets/example/1.0.0/example/README.txt", "Alice、Bob");
        var before = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.0"), workspace.RootPath);
        workspace.WriteText("projects/assets/example/1.0.0/example/README.txt", "Bob、Alice");
        var after = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.1"), workspace.RootPath);
        var notes = PackageHistory.FormatChanges(PackageHistory.Read(after.OutputZipPath), PackageHistory.Read(before.OutputZipPath), "release");
        Assert.Contains("本次没有翻译变更", notes);
        Assert.DoesNotContain("|", notes);
    }

    [Fact]
    public async Task Release_TracksGameTranslationUpdatesAndRemoval()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("projects/game/1.22.3/assets/game/lang/zh-cn.json", "{}");
        var config = workspace.CreateConfig("1.0.0");
        config.GameTranslation = new GameTranslationConfig { ContentRoot = "projects/game", TargetVersion = "1.22.3" };
        var before = await TranslationPackBuilder.BuildAsync(config, workspace.RootPath);
        workspace.WriteText("projects/game/1.22.3/assets/game/lang/zh-cn.json", "{\"key\":\"修订\"}");
        config.PackageVersion = "1.0.1";
        var after = await TranslationPackBuilder.BuildAsync(config, workspace.RootPath);
        var notes = PackageHistory.FormatChanges(PackageHistory.Read(after.OutputZipPath), PackageHistory.Read(before.OutputZipPath), "release");
        Assert.Contains("| 更新 | 游戏本体 | 1.22.3 | 1.22.3 |", notes);
        config.PackageVersion = "1.0.2";
        config.GameTranslation = null;
        var removed = await TranslationPackBuilder.BuildAsync(config, workspace.RootPath);
        notes = PackageHistory.FormatChanges(PackageHistory.Read(removed.OutputZipPath), PackageHistory.Read(after.OutputZipPath), "release");
        Assert.Contains("| 移除 | 游戏本体 | 1.22.3 | — |", notes);
    }

    [Fact]
    public async Task Release_ReadsLegacyZipAndDoesNotTreatUnknownCreditsAsChanges()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("projects/assets/example/1.0.0/example/lang/zh-cn.json", "{}");
        var before = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.0"), workspace.RootPath);
        using (var archive = ZipFile.Open(before.OutputZipPath, ZipArchiveMode.Update))
            archive.GetEntry(PackageHistory.RecordFileName)!.Delete();
        workspace.WriteText("projects/assets/example/1.0.0/example/README.txt", "Translator");
        var after = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.1"), workspace.RootPath);
        var legacy = PackageHistory.Read(before.OutputZipPath);
        Assert.Equal(0, legacy.SchemaVersion);
        var notes = PackageHistory.FormatChanges(PackageHistory.Read(after.OutputZipPath), legacy, "release");
        Assert.Contains("上一版本没有打包记录", notes);
        Assert.Contains("本次没有翻译变更", notes);
        workspace.WriteText("projects/assets/example/1.0.0/example/lang/zh-cn.json", "{\"key\":\"修订\"}");
        var changed = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.2"), workspace.RootPath);
        Assert.Contains("| 更新 | example |", PackageHistory.FormatChanges(PackageHistory.Read(changed.OutputZipPath), legacy, "release"));
    }

    [Fact]
    public async Task Release_RejectsMissingOrInvalidBaseline()
    {
        using var workspace = new TestWorkspace();
        var built = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.0"), workspace.RootPath);
        var stderr = new StringWriter();
        var exitCode = await CliRunner.RunAsync(
            ["describe-release", "--config", workspace.WriteConfigFile(), "--package-version", "1.0.0",
                "--release-kind", "release", "--previous-package", "missing.zip"],
            new StringWriter(), stderr, workspace.RootPath);
        Assert.Equal(1, exitCode);
        Assert.Contains("Package does not exist", stderr.ToString());
        using (var archive = ZipFile.Open(built.OutputZipPath, ZipArchiveMode.Update))
        {
            archive.GetEntry(PackageHistory.RecordFileName)!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(PackageHistory.RecordFileName).Open());
            writer.Write("{\"schemaVersion\":99}");
        }
        Assert.Throws<PackerException>(() => PackageHistory.Read(built.OutputZipPath));
    }

    [Fact]
    public async Task Release_RejectsRecordThatDoesNotMatchPackedContent()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("projects/assets/example/1.0.0/example/lang/zh-cn.json", "{}");
        var built = await TranslationPackBuilder.BuildAsync(workspace.CreateConfig("1.0.0"), workspace.RootPath);
        using (var archive = ZipFile.Open(built.OutputZipPath, ZipArchiveMode.Update))
        {
            archive.GetEntry("assets/example/lang/zh-cn.json")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("assets/example/lang/zh-cn.json").Open());
            writer.Write("{\"key\":\"记录外修改\"}");
        }
        Assert.Contains("hash does not match", Assert.Throws<PackerException>(() => PackageHistory.Read(built.OutputZipPath)).Message);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "vscn-packer-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(RootPath, "projects", "assets"));
        }

        public string RootPath { get; }

        public PackerConfig CreateConfig(string version = "0.0.0") => new() { PackageVersion = version };

        public string WriteConfigFile(GameTranslationConfig? gameTranslation = null)
        {
            var path = Path.Combine(RootPath, "config", "packer", "default.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var config = CreateConfig();
            config.GameTranslation = gameTranslation;
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        public void WriteText(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
