using System.Runtime.InteropServices;
using HarmonyLib;
using Utilities;

namespace AITweaks;


public static class ModEntry
{
    public static readonly string harmonyId = "Infixo." + nameof(AITweaks);

    // mod's instance and asset
    //public static Mod instance { get; private set; }
    //public static ExecutableAsset modAsset { get; private set; }
    // logging
    //public static ILog log = LogManager.GetLogger($"{nameof(InfoLoom)}").SetShowsErrorsInUI(false);

    [UnmanagedCallersOnly]
    //[UnmanagedCallersOnly(EntryPoint = "InitializeMod")] // not needed when called via CLR
    //[ModuleInitializer] // only works with CLR, not native loads?
    public static int InitializeMod()
    {
        DebugConsole.Show();
        Log.Write($"WWR mod {nameof(AITweaks)} successfully started, harmonyId is {harmonyId}.");
        try
        {
            // Harmony
            var harmony = new Harmony(harmonyId);
            //harmony.PatchAll(typeof(Mod).Assembly);
            harmony.PatchAll();
            var patchedMethods = harmony.GetPatchedMethods().ToArray();
            Log.Write($"Plugin {harmonyId} made patches! Patched methods: " + patchedMethods.Length);
            foreach (var patchedMethod in patchedMethods)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                Log.Write($"Patched method: {patchedMethod.Module.Name}:{patchedMethod.DeclaringType.Name}.{patchedMethod.Name}");
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            }
        }
        catch (Exception ex)
        {
            Log.Write("EXCEPTION");
            Log.Write(ex.ToString());
        }
        // do other stuff here to initialize
        return 0;
    }
}
