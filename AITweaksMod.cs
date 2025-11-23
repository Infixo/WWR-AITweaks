using System.Runtime.InteropServices;
using Utilities;

namespace AITweaks;


public static class ModEntry
{
    public static readonly string ModName = nameof(AITweaks);

    [UnmanagedCallersOnly]
    //[UnmanagedCallersOnly(EntryPoint = "InitializeMod")] // not needed when called via CLR
    //[ModuleInitializer] // only works with CLR, not native loads?
    public static int InitializeMod()
    {
        if (ModInit.InitializeMod(ModName))
        {
            // do other stuff here to initialize
        }
        return 0;
    }
}
