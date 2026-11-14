using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;
using HansJack.ModpackLocalization;
using Vintagestory.API.Config;

// Run against the installed versions' actual DLLs, without starting a game world.
string game = Path.GetFullPath(args[0]);
string root = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { game, Path.Combine(game, "Lib") })
    {
        string file = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
    }
    return null;
};
Run();

void Run()
{
    var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "src/ModpackLocalizationBridge/catalog.json")))!;
    var zh = new Dictionary<string, string>();
    foreach (var mid in new[] { "crawlanddive", "progressivebleeding" })
    {
        var project = Path.Combine(root, "projects", "assets", mid);
        var files = Directory.GetDirectories(project)
            .Select(version => Path.Combine(version, mid, "lang", "zh-cn.json"))
            .Where(File.Exists).ToArray();
        if (files.Length != 1) throw new InvalidOperationException($"Expected one translation version for {mid}; found {files.Length}.");
        foreach (var row in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(files[0]))!) zh[mid + ":" + row.Key] = row.Value;
    }
    var service = new TranslationService("zh-cn", null!);
    typeof(TranslationService).GetField("entryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, zh);
    typeof(TranslationService).GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, true);
    Lang.AvailableLanguages["zh-cn"] = service;
    Lang.ChangeLanguage("zh-cn");
    foreach (var (source, key) in catalog) Check(LocalizationBridge.Translate(source) == zh[key], "Catalog lookup: " + source);
    Check(LocalizationBridge.Translate("progressivebleeding:active") == "progressivebleeding:active", "Internal attribute unchanged");
    Check(LocalizationBridge.Translate("spear") == "spear", "Internal weapon identifier unchanged");
    Check(LocalizationBridge.Translate("spear buildup multiplier (0–5)") == "长矛蓄积倍率（0–5）", "Combined display label");
    string preview = "64" + LocalizationBridge.Translate(" max HP | ") + "3" + LocalizationBridge.Translate(" stacks | ") + "10" + LocalizationBridge.Translate(" seconds\n") + LocalizationBridge.Translate("First pulse (") + "1" + LocalizationBridge.Translate("s): ") + "0.64" + LocalizationBridge.Translate(" HP (") + "1" + LocalizationBridge.Translate("%)\n");
    Check(preview == "64 点最大生命值 | 3 层 | 10 秒\n首次结算（1 秒）：0.64 点生命值（1%）\n", "Dynamic numeric preview");
    var patcher = new Harmony("hansjack-localization-check");
    int methods = 0;
    foreach (var path in args.Skip(2))
    {
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        int count = LocalizationBridge.ApplyPatches(patcher, assembly);
        Check(count > 0, "Applied patches to " + assembly.GetName().Name);
        methods += count;
        Console.WriteLine($"{assembly.GetName().Name}: {count} patched methods");
        if (assembly.GetName().Name != "ProgressiveBleeding") continue;
        var configType = assembly.GetType("ProgressiveBleeding.BleedingConfig")!;
        var config = Activator.CreateInstance(configType)!;
        configType.GetMethod("Validate")!.Invoke(config, null);
        var weapons = (Dictionary<string, float>)configType.GetProperty("WeaponBuildupMultipliers")!.GetValue(config)!;
        Check(weapons.ContainsKey("spear") && !weapons.ContainsKey("长矛"), "Configuration keys remain intact");
        var messageType = assembly.GetType("ProgressiveBleeding.ConfigMessage")!;
        var message = Activator.CreateInstance(messageType)!;
        messageType.GetProperty("Status")!.SetValue(message, "Saved. Reopen the world or restart the server to apply changes.");
        Check((string)messageType.GetProperty("Status")!.GetValue(message)! == "已保存。重新进入世界或重启服务器后生效。", "Actual patched network status getter");
        var number = assembly.GetType("ProgressiveBleeding.BleedingConfigDialog")!.GetMethod("Number", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { number.Invoke(null, ["not-a-number"]); throw new Exception("Expected validation failure"); }
        catch (TargetInvocationException ex) { Check(ex.InnerException!.Message == "请输入有限数值；小数分隔符可使用点或逗号。", "Actual patched validation text"); }
    }
    Lang.ChangeLanguage("en");
    Check(LocalizationBridge.Translate("Crawl / Dive") == "Crawl / Dive", "English locale fallback");
    patcher.UnpatchAll(patcher.Id);
    Console.WriteLine($"PASS: {catalog.Count} catalog entries; {methods} methods; dynamic preview, network status, validation and internal identifiers.");
}

static void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
}
