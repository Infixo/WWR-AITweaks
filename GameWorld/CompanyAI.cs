using HarmonyLib;
using STM.GameWorld;
using STM.GameWorld.AI;
using STVisual.Utility;
using System.Runtime.CompilerServices;

namespace AITweaks.GameWorld;


[HarmonyPatch]
public static class CompanyAI_Patches
{
    // Data extensions
    public class ExtraData
    {
        public bool SecondEval = true;
    }
    private static readonly ConditionalWeakTable<CompanyAI, ExtraData> _extras = [];
    public static ExtraData Extra(this CompanyAI ai) => _extras.GetOrCreateValue(ai);


    // Patch that introduces 2nd evaluation during a month and spreads them evenly every 256 seconds
    [HarmonyPatch(typeof(CompanyAI), "SearchForAPlan"), HarmonyPrefix]
    public static bool SearchForAPlan(CompanyAI __instance, Company company, GameScene scene, ref IAIPlan ___plan, ref bool ___new_month, GrowArray<ushort> ___invalid)
    {
        int _sec = scene.Session.Second;
        if (___new_month && _sec > ((int)company.ID << 8) && company.Vehicles > 2)
        {
            // 1st eval
            ___plan = new PlanEvaluateRoute();
            ___new_month = false;
            __instance.Extra().SecondEval = _sec < 43200;
            //Log.Write($"[{company.ID}]:1 {_sec}");
        }
        else if (_sec > 43200 && __instance.Extra().SecondEval && _sec > 43200 + ((int)company.ID << 8) && company.Vehicles > 2)
        {
            // 2nd eval
            ___plan = new PlanEvaluateRoute();
            __instance.Extra().SecondEval = false;
            //Log.Write($"[{company.ID}]:2 {_sec}");
        }
        else
        {
            ___plan = new PlanSearchForARoute(company, scene, ___invalid.ToArrayOrSelf());
        }
        return false;
    }
}
