using HarmonyLib;
using STM.GameWorld;
using STM.GameWorld.Users;
using STMG.Engine;
using Utilities;

namespace AITweaks.Patches;


[HarmonyPatch(typeof(RouteInstance))]
public static class RouteInstance_Patches
{
    /*
    /// <summary>
    /// Calculates number of passengers waiting to go to one of the cities on the route.
    /// </summary>
    /// <param name="city"></param>
    /// <param name="route"></param>
    /// <returns></returns>
    public static long GetPassengersEx(this CityUser city, Route route)
    {
        long result = 0L;
        // Direct passengers - easy, the city must be on the route
        // Not so easy... must also check indirect connections!
        for (int k = 0; k < city.Destinations.Items.Count; k++)
        {
            // Easy scenario - direct connection to a city on the route
            if (route.Contains(city.Destinations.Items[k].Destination.User))
            {
                result += city.Destinations.Items[k].People;
            }
            else
            // The destination is off the route, so we will count it only if there is a connection from any other city on the route excluding the current one
            {
                foreach (CityUser otherCity in route.Cities)
                    if (otherCity != city && otherCity.Connections_hash.Contains(city.Destinations.Items[k].Destination))
                    {
                        result += city.Destinations.Items[k].People;
                    }
            }
        }
        // Indirect passengers - tricky, must check if connecting route is actually the one we're on
        for (int j = 0; j < city.Indirect.Count; j++)
        {
            if (/*ty.Indirect[j].Destination == destination.City || route.Contains(city.Indirect[j].Next.User))
            {
                result += city.Indirect[j].People;
            }
        }
        // Returning passengers - same as direct, must account for indirect connection too
        for (int i = 0; i < city.Returns.Count; i++)
        {
            if (route.Contains(city.Returns[i].Home.User))
            {
                result += city.Returns[i].Ready;
            }
            else
            {
                foreach (CityUser otherCity in route.Cities)
                    if (otherCity != city && otherCity.Connections_hash.Contains(city.Returns[i].Home))
                    {
                        result += city.Returns[i].Ready;
                    }
            }
        }
        return result;
    }
    */
    
    [HarmonyPatch("GetWaiting"), HarmonyPrefix]
    public static bool RouteInstance_GetWaiting_Prefix(RouteInstance __instance, ref long __result)
    {
        //return Instructions.Cities[0].GetPassengers(Instructions.Cities[Instructions.Cities.Length - 1]) + Instructions.Cities[Instructions.Cities.Length - 1].GetPassengers(Instructions.Cities[0]);
        //long waiting = 0;
        //foreach (CityUser city in __instance.Instructions.Cities)
        //{
            //waiting += city.GetPassengersEx(__instance.Instructions);
        //}
        GameScene scene = (GameScene)GameEngine.Last.Main_scene;
        __result = scene.Session.Companies[__instance.Vehicle.Company].Line_manager.GetLine(__instance.Vehicle).GetWaiting();
        return false;
    }
    
}
