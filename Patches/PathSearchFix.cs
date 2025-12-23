using HarmonyLib;
using STM.GameWorld;

namespace AITweaks.Patches;


// 2025-12-17 Fixed in v1.2.0 of the game, no longer needed.
// 2025-12-23 v1.2.1 again introduces asymmetry, so enabled again.

[HarmonyPatch]
public static class PathSearchFix
{
    [HarmonyPatch(typeof(PathSearchNode), "GetNext"), HarmonyPrefix]
    public static bool GetNext(PathSearchNode __instance, ref PathSearchNode __result, PathSearchData data, City destination, int overcrowd_factor, GameScene scene)
    {
        int _adjusted_start_cost = PathSearchNode.GetDistanceCost(data.current.City, data.add.City);
        int _start_cost = __instance.start_cost + _adjusted_start_cost;
        //_adjusted_start_cost *= 1 + (int)data.add.Accepts_indirect / 20 * overcrowd_factor;
        //if (data.add.Routes.Count > 9)
        //{
            //_adjusted_start_cost /= Math.Max(data.add.Routes.Count / 5 - __instance.Step, 1);
        //}
        _adjusted_start_cost += __instance.Adjusted_start_cost;
        __result = data.GetNode(data.add.City, _start_cost, _adjusted_start_cost, PathSearchNode.GetDistanceCost(data.add.City, destination), (ushort)(__instance.Step + 1), __instance.ID);
        return false;
    }
}
