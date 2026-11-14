using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace HansJack.ModpackLocalization;

/// <summary>Localizes reviewed UI strings in two assemblies without replacing their files.</summary>
public sealed class LocalizationBridge : ModSystem
{
    private const string PatchId = "hansjackmodpacklocalization";
    private Harmony? harmony;
    private static readonly Dictionary<string, string> Catalog = LoadCatalog();
    private static readonly MethodInfo TranslateMethod = typeof(LocalizationBridge).GetMethod(nameof(Translate))!;

    public override double ExecuteOrder() => -10000;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        harmony = new Harmony(PatchId);
        foreach (var target in new[] { ("crawlanddive", "CrawlandDive"), ("progressivebleeding", "ProgressiveBleeding") })
        {
            if (!api.ModLoader.IsModEnabled(target.Item1)) continue;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == target.Item2);
            if (assembly == null)
            {
                api.Logger.Warning("[HansJack] Could not find {0} for localization.", target.Item1);
                continue;
            }
            var count = ApplyPatches(harmony, assembly);
            api.Logger.Notification("[HansJack] Localized {0} method(s) in {1}.", count, target.Item1);
        }
    }

    public static int ApplyPatches(Harmony patcher, Assembly assembly)
    {
        int count = 0;
        foreach (var type in assembly.GetTypes())
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                var instructions = PatchProcessor.GetOriginalInstructions(method);
                bool hasLiteral = instructions.Any(i => i.opcode == OpCodes.Ldstr && i.operand is string text && Catalog.ContainsKey(text));
                bool weaponLabels = type.FullName == "ProgressiveBleeding.BleedingConfigDialog" && method.Name == "Build";
                bool networkStatus = type.FullName == "ProgressiveBleeding.ConfigMessage" && method.Name == "get_Status";
                if (!hasLiteral && !weaponLabels && !networkStatus) continue;
                patcher.Patch(method, transpiler: new HarmonyMethod(typeof(LocalizationBridge), nameof(Transpile)));
                count++;
            }
        }
        return count;
    }

    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        bool weaponLabels = __originalMethod.DeclaringType?.FullName == "ProgressiveBleeding.BleedingConfigDialog" && __originalMethod.Name == "Build";
        bool networkStatus = __originalMethod.DeclaringType?.FullName == "ProgressiveBleeding.ConfigMessage" && __originalMethod.Name == "get_Status";
        foreach (var instruction in instructions)
        {
            // Translate network responses before they reach either the dialog or chat.
            if (networkStatus && instruction.opcode == OpCodes.Ret)
            {
                var translate = new CodeInstruction(OpCodes.Call, TranslateMethod);
                translate.labels.AddRange(instruction.labels);
                translate.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return translate;
            }
            yield return instruction;
            bool literal = instruction.opcode == OpCodes.Ldstr && instruction.operand is string text && Catalog.ContainsKey(text);
            bool combined = weaponLabels && instruction.operand is MethodInfo method && method.DeclaringType == typeof(string) && method.Name == nameof(string.Concat) && method.ReturnType == typeof(string);
            if (literal || combined) yield return new CodeInstruction(OpCodes.Call, TranslateMethod);
        }
    }

    public static string Translate(string text)
    {
        if (Lang.CurrentLocale != "zh-cn" || text == null || !Catalog.TryGetValue(text, out var key)) return text!;
        return Lang.HasTranslation(key, false, false) ? Lang.GetUnformatted(key) : text;
    }

    private static Dictionary<string, string> LoadCatalog()
    {
        var assembly = typeof(LocalizationBridge).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("catalog.json")))!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(PatchId);
        harmony = null;
    }
}
