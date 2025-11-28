using HarmonyLib;
using STM.Data;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.AI;
using STM.GameWorld.Commands;
using STM.GameWorld.Users;
using STVisual.Utility;
using Utilities;

namespace AITweaks.GameWorld;


[HarmonyPatch(typeof(GeneratedPlan))]
public static  class GeneratedPlan_Patches
{
    // Patch constructor to store Price
    [HarmonyPatch(typeof(GeneratedPlan), MethodType.Constructor, [ typeof(decimal), typeof(NewRouteSettings), typeof(long), typeof(VehicleBaseUser)]), HarmonyPostfix]
#pragma warning disable IDE0060 // Remove unused parameter
    public static void GeneratedPlan_Constructor_Postfix(GeneratedPlan __instance, decimal weight, NewRouteSettings settings, long price, VehicleBaseUser current /*= null*/)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        __instance.age = 1; // there is later a cleanup when age > 2, so instead of changing that, we can start with +1
        __instance.SetPublicProperty("Price", price);
    }


    // Perform as many checks as possible before selling a vehicle!
    [HarmonyPatch("Apply"), HarmonyPrefix]
    public static bool GeneratedPlan_Apply_Prefix(GeneratedPlan __instance, ref bool __result, Company company, GameScene scene, HubManager manager /*= null*/, RoadRoute ___road)
    {
        __result = false; // this will keep the plan, only true removes the plan

        // Route good?
        if (__instance.Settings.Cities.Count == 0) // || __instance.Settings.Cities.Count > 10) // WHY LIMITER???
        {
            __result = true; return false;
        }

        // Check inventory
        VehicleBaseEntity _vehicle = __instance.Settings.GetVehicleEntity();
        if (company.GetInventory(_vehicle, __instance.GetCountry(scene), scene) == 0)
        {
            return false;
        }

        // Check hub
        Hub _hub = ((manager == null) ? __instance.Settings.Cities[0].GetHub(company.ID) : manager.Hub);
        if (_hub == null)
        {
            __result = true;  return false;
        }

        long _wealth = manager?.Budget ?? company.Wealth;

        // Upgrade hub
        if (_hub.Full())
        {
            if ((decimal)_wealth > (decimal)(MainData.Defaults.Hub_level_cost * _hub.Level) * scene.Session.GetPriceAdjust())
            {
                scene.Session.Commands.Add(new CommandUpgradeHub(company.ID, _hub.City, (ushort)(_hub.Level + 1), manager));
            }
            return false;
        }

        // Update price
        long price = _vehicle.GetPrice(scene, company, scene.Cities[_hub.City].User);
        if (__instance.Current != null) price -= __instance.Current.GetValue();
        __instance.SetPublicProperty("Price", price);

        // Check price and budget
        if (_wealth < __instance.Price)
        {
            if (manager != null)
            {
                return false;
            }
            __result = company.GetLastMonthBalance() <= 0; return false;
        }

        // Check infrastructure
        if ((_vehicle is TrainEntity && !__instance.CallPrivateMethod<bool>("InfrastructureIsReady", [company, scene, false, manager!])) || (_vehicle is RoadVehicleEntity && !__instance.CallPrivateMethod<bool>("InfrastructureIsReady", [company, scene, true, manager!])))
        {
            // Patch 1.1.15
            if (___road != null)
            {
                return ___road.Road.Length == 0;
            }
            return false;
        }

        // Sell existing as late as possible
        if (__instance.Current != null && !__instance.CallPrivateMethod<bool>("SellCurrent", [company, scene, manager!]))
        {
            return false;
        }
        // When selling, it will proceed here ONLY if Current.Destroyed = true, 
        // so it probably needs 2 tries - first to sell, and then once sold - to buy a new one!
        __instance.SetPublicProperty("Current", null!); // Sold

        // Successful finish
        scene.Session.Commands.Add(new CommandNewRoute(company.ID, __instance.Settings, open: false, manager));
        __result = true;  return false;
    }


    // This patch prevents adding the same plan many times
    // In that case, old one is removed
    [HarmonyPatch(typeof(HubManager), "AddNewPlan"), HarmonyPrefix]
    public static bool HubManager_AddNewPlan_Prefix(GeneratedPlan plan, GrowArray<GeneratedPlan> ___generated)
    {
        // remove older plans
        for (int j = ___generated.Count - 1; j >= 0; j--)
            if (___generated[j].Current == plan.Current)
                if (plan.Current != null || SameRoute(___generated[j].Settings.Cities, plan.Settings.Cities))
                    ___generated.RemoveAt(j);

        // add a new one
        ___generated.Add(plan);
        return false;

        // Local helper - check if routes are the same
        static bool SameRoute(GrowArray<CityUser> r1, GrowArray<CityUser> r2)
        {
            if (r1.Count != r2.Count) return false;
            for (int i = 0; i < r1.Count; i++)
                if (r1[i] != r2[i]) return false;
            return true;
        }
    }
}
