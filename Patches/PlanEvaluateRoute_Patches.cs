//#define LOGGING

using HarmonyLib;
using Microsoft.Xna.Framework;
using STM.Data;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.AI;
using STM.GameWorld.Commands;
using STM.GameWorld.Users;
using STM.UI;
using STMG.Engine;
using System.Runtime.CompilerServices;
using Utilities;

namespace AITweaks.Patches;


[HarmonyPatch(typeof(PlanEvaluateRoute))]
public static class PlanEvaluateRoute_Patches
{

    [HarmonyPatch("EvaluateLine"), HarmonyPrefix]
    public static bool PlanEvaluateRoute_EvaluateLine_Prefix(PlanEvaluateRoute __instance, Company company, VehicleBaseUser[] vehicles, GameScene scene, HubManager manager)
    {
        if (vehicles.Length == 0) return false; // it happens!
        if (manager == null && company.AI == null) return false; // just a precaution and to simplify code later

        // Step 1
        // Exclude vehicles that have not yet transported anyone (e.g. totally new lines added to hubs with managers)
        VehicleBaseUser[] vehs = vehicles; //  [.. vehicles.ToArray().Where(v => v.Throughput.GetSum(2) > 0)];
        //if (vehs.Length == 0 || (vehicles.Length - vehs.Length) > vehicles.Length/5) // TODO: PARAMETER
            //return false;

        // Step 1a
        // This is to prevent repeated evals when the data has not changed i.e. vehicles has not reached destination
        bool alreadyEvaluated = true;
        foreach (VehicleBaseUser veh in vehs)
            alreadyEvaluated &= veh.evaluated_on == veh.stops;
        if (alreadyEvaluated)
        {
#if LOGGING
            Log.Write("[EVALDONE]", false);
#endif
            return false;
        }

        // Step 2 Assess the line
        // Calculate: waiting, throughput min/ now / max, min vehicles, profitability, total balance
        // Do NOT use average, use simple sum. Since all is extrapolated via min_cap and max_cap. It will simply evaluate the last 3 months performance in total,
        // not average. This will eliminate "10 days" problem.

        VehicleEvaluation evaluation = default;
        for (int i = 0; i < vehs.Length; i++)
            evaluation.Evaluate(vehs[i]);

        Line line = scene.Session.Companies[vehs[0].Company].Line_manager.GetLine(vehs[0]);
        int waiting = (int)line.GetWaiting();

        // Step 2a Calculate more advanced metrics
        float distance = (float)line.GetTotalDistance();
        int numStops = line.Instructions.Cyclic ? line.Instructions.Cities.Length + 1 : line.Instructions.Cities.Length;
        (int stationTime, float frequency, float cities) calcParams = (0, 2f, 1f); // freq - how many times vehicles visit a city, cities - modifier for number of cities

        // Get station wait time
        switch (line.Vehicle_type)
        {
            case 0: calcParams = (MainData.Defaults.Bus_station_time, 10f, 1f); break; // bus
            case 1: calcParams = (MainData.Defaults.Train_station_time, 6f, 0.25f); break; // train
            case 2: calcParams = (MainData.Defaults.Plane_airport_time, 4f, 1.5f); break; // plane
            case 3: calcParams = (MainData.Defaults.Ship_port_time, 2.5f, 0.5f); break; // ship, 2 for large, 3 for medium/small
        }
        float travelTime = (distance / evaluation.AvgSpeed + (float)(numStops - 1) * (float)calcParams.stationTime / 3600f);
        float numTrips = line.Instructions.Cyclic ? 24f / travelTime : 12f / travelTime;
        float months = 1f + (float)scene.Session.Second / 86400f; // TODO: 2 is related to GetSum(3) - we use 2 full months and a fraction of the current one
        float monthlyThroughput = (float)evaluation.throughput_max / months;
        float perVehicle = monthlyThroughput / (float)vehs.Length;
        float waitingFraction = (float)waiting * ((float)vehs.Length / (float)line.Routes.Count);
        float waitPerTrip = waitingFraction / numTrips;
        float vehicleGap = waitPerTrip / perVehicle;
        evaluation.gap = vehicleGap;

        // optimal number vehicles
        float fOptimal = (float)line.Instructions.Cities.Length * calcParams.cities;
        fOptimal += calcParams.frequency * travelTime / 12f;
        int optimal = Math.Max((int)(fOptimal+0.5f), 2);

        // optimal tier
        int optTier = 1 + vehicles.Sum(v => v.Entity_base.Tier) / vehicles.Length;

        // Step 3 Sort vehicles in order for upgrade
        // This signature matches the Comparison<T> delegate required by Array.Sort.
        //  < 0 v1 comes first, 0 equal, > 0 v1 comes after v2
        // TODO: this is used to find vehicles to upgrade/downgrade -> probably should account for efficiency too!
        static int CompareVehicles(VehicleBaseUser v1, VehicleBaseUser v2)
        {
            if (v1.Entity_base.Tier != v2.Entity_base.Tier)
                return (int)(v1.Entity_base.Tier - v2.Entity_base.Tier);
            // if balance > 0 - sort by eff, if balance < 0 sort by balance
            long val1 = v1.Balance.GetSum(2) > 0 ? v1.Efficiency.GetSumAverage(2) : v1.Balance.GetSum(2);
            long val2 = v2.Balance.GetSum(2) > 0 ? v2.Efficiency.GetSumAverage(2) : v2.Balance.GetSum(2);
            return (int)(val2 - val1);
        }
        Array.Sort(vehs, CompareVehicles);
        //Array.Sort(vehs, (v1, v2) => v2.Balance.GetSum(3).CompareTo(v1.Balance.GetSum(3))); // balance only

        // Evaluation toolip & debug logging
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        string header = $"{company.ID}-{line.ID + 1}";
        if (manager != null) header += $"-{scene.Cities[manager.Hub.City].User.Name}";
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        line.NewEvaluation(header);
        header = $"[{header}] ";

        void LogEvalLine(string text)
        {
            line.AddEvaluationText(text);
#if LOGGING
            Log.Write(header + text, false);
#endif
        }

        string GetBalanceTxt(long value)
        {
            decimal ks = scene.currency.Convert((decimal)value);
            if (ks > 100000000m) return $"{ks/1000000m:F0}M";
            if (ks > 1000000m) return $"{ks/1000000m:F1}M";
            return $"{ks/1000:F0}k";
        }

        float eMin = (float)evaluation.throughput_min * 100f / (float)evaluation.throughput_max;
        float eThird = (float)(evaluation.throughput_max - evaluation.throughput_min) * 100f / 3f / (float)evaluation.throughput_max;
        string upDown = "up=" + (evaluation.Upgrade ? 1 : 0) + " dn=" + (evaluation.Downgrade ? 1 : 0);
        LogEvalLine($"START n={vehs.Length}/{line.Routes.Count} opt={fOptimal:F1}xT{optTier} b={GetBalanceTxt(evaluation.balance)} p={GetBalanceTxt(evaluation.profitability)}");
        LogEvalLine($"line {distance:F0}km +{numStops} {evaluation.AvgSpeed:F0}kmh {travelTime:F1}h {numTrips:F1}tr");
        LogEvalLine($"thru min={eMin:F1}% {evaluation.throughput_min}/{evaluation.throughput_now}/{evaluation.throughput_max} w={waiting}");
        LogEvalLine($"eval {evaluation.throughput_now * 100m / evaluation.throughput_max:F1}% ({eMin + eThird:F1}-{eMin + eThird * 2:F1}) gap={vehicleGap:F2} {upDown}");
        LogEvalLine($"vgap {months:F2}m {monthlyThroughput:F0}/m {perVehicle:F0}/v wf={waitingFraction:F0} {waitPerTrip:F0}/tr");
        for (int i = 0; i < vehs.Length; i++)
            if (i < 2 || i > vehs.Length - 3)
                LogEvalLine($"{i+1}. {vehs[i].ID}-{vehs[i].Entity_base.Tier}-{vehs[i].Entity_base.Translated_name} {vehs[i].Efficiency.GetSumAverage(2)}% b={GetBalanceTxt(vehs[i].Balance.GetSum(2))} s={vehs[i].stops} e={vehs[i].evaluated_on}");

        // Step 3 Decision tree
        int range = __instance.CallPrivateMethod<int>("GetRange", [vehs]);
        VehicleBaseUser best = vehs[0]; // first
        VehicleBaseUser worst = vehs[^1]; // last one
#if LOGGING
        string text = $" up={evaluation.Upgrade} dn={evaluation.Downgrade}";
        string dec = "TMP_";
        if (evaluation.Upgrade)
        {
            if (vehs.Length < optimal) dec += "ADD_NEW";
            else dec += "UPGRADE";
        }
        if (evaluation.Downgrade)
        {
            if (vehs.Length > optimal) dec += "SELL";
            else dec += "DOWNGRADE";
        }
        if (!evaluation.Upgrade && !evaluation.Downgrade)
        {
            dec += "WAIT";
            // check if too many
            if (vehs.Length > optimal && worst.Balance.GetSum(2) < -evaluation.profitability / 2 / vehs.Length)
                dec += "_SELL";
        }
        if (evaluation.Upgrade && evaluation.Downgrade)
        {
            dec = "ERROR";
        }
        Log.Write(header + " " + dec + text, false);
#endif
        void LogDecision(string type, VehicleBaseUser vh1, VehicleBaseEntity? vh2 = null)
        {
            string vh2txt = vh2 != null ? $"  vh2= {vh2.Tier}-{vh2.Translated_name}" : "";
            LogEvalLine($"{type} vh1= {vh1.ID}-{vh1.Entity_base.Tier}-{vh1.Entity_base.Translated_name} {vh2txt}");
        }

        // this is to prevent repeated evals when the data has not changed i.e. vehicles has not reached destination
        void MarkLineAsEvaluated()
        {
            foreach (VehicleBaseUser veh in vehs)
                veh.evaluated_on = veh.stops;
        }

        // Scenario: Upgrade existing
        const float GapPerUpgrade = 1.25f; // PARAM: 1f gap per upgrade, could be higher / lower
        if (evaluation.Upgrade && (vehs.Length >= optimal || evaluation.gap > 2f * GapPerUpgrade))
        {
            int maxUpgrades = Math.Max(1, (int)(evaluation.gap / GapPerUpgrade)); // ... but at least 1 upgrade
            int numUpgrades = 0; // how many upgrades are scheduled
            // Find vehicle to upgrade, strting with best; if none can be upgraded then ADD_NEW
            for (int i = 0; i < vehs.Length && numUpgrades < maxUpgrades; i++) // stop when all tried or we did enough upgrades
            {
                VehicleBaseUser vehicle = vehs[i];
                if (vehicle.Entity_base.Tier >= optTier) continue; // do not upgrade if we reached optimal tier - this will allow to add new before upgrading to T6
                VehicleBaseEntity? upgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetUpgrade", [company, vehicle, manager!, range]);
                if (upgrade == null) continue; // get next one
                //{
                    // LOGIC
                    //vehicle.evaluated_on = vehicle.stops;
                NewRouteSettings settings = new(vehicle);
                settings.SetVehicleEntity(upgrade);
                long price = upgrade.GetPrice(scene, company, scene.Cities[vehicle.Hub.City].User);
                price -= vehicle.GetValue();
                if (price <= 0L) price = 1L;
                decimal weight = __instance.CallPrivateMethod<long>("GetBalance", [vehicle]) * 2;
                LogDecision("UPGRADE", vehicle, upgrade);
                if (manager != null) manager.AddNewPlan(new GeneratedPlan(weight / (decimal)price, settings, price, vehicle));
                else company.AI.AddNewPlan(new GeneratedPlan(weight / (decimal)price, settings, price, vehicle));
                vehicleGap -= GapPerUpgrade;
                numUpgrades++;
                //}
            }
            // when to go to ADDNEW? a) nothing was upgraded b) not enough upgrades
            // original stop: upgrade done and vehs>=optimal -> not enough if gap is still big?
            bool addNew = false;
            if (numUpgrades == 0) addNew = true;
            else if (vehs.Length < optimal && vehicleGap > 1f) addNew = true;
            if (!addNew)
            {
                MarkLineAsEvaluated();
                return false;
            }
        }

        // Scenario: Add a new vehicle
        if (evaluation.Upgrade)
        {
            // find "example to copy" - the first that is optimal tier (or higher); if not found - just first vehicle
            best = vehs.FirstOrDefault(v => v.Entity_base.Tier >= optTier) ?? vehs[0];
            // determine if copy the same or add 1 tier lower - depends of vehicle gap size, if gap is small - add lower tier
            VehicleBaseEntity newVehicle = vehicleGap > 1f ? best.Entity_base : (__instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, best, range]) ?? best.Entity_base.Company.Entity.Vehicles[0]);
            // generate commands
            NewRouteSettings settings = new(best);
            settings.SetVehicleEntity(newVehicle);
            long price = newVehicle.GetPrice(scene, company, scene.Cities[best.Hub.City].User);
            //if (best.Hub.Full())
            //price += best.Hub.GetNextLevelPrice(scene.Session); // Covered by GenPlan.Apply
            LogDecision("ADDNEW", best, newVehicle);
            if (manager != null) manager.AddNewPlan(new GeneratedPlan(1m, settings, price, null)); // no vehicle to sell
            else company.AI.AddNewPlan(new GeneratedPlan(1m, settings, price, null));
            MarkLineAsEvaluated();
            return false;
        }

        // Scenario: Downgrade existing
        if (evaluation.Downgrade && vehs.Length <= optimal)
        {
            // Find vehicle to downgrade, strting with the worst; if none can be downgraded, then SELL
            VehicleBaseUser? vehicle = null;
            VehicleBaseEntity? downgrade = null;
            for (int i = 0; i < vehs.Length && downgrade == null; i++)
            {
                vehicle = vehs[^(i + 1)];
                downgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, vehicle, range]);
            }
            if (downgrade != null && vehicle != null)
            {
                LogDecision("DOWNGRADE", vehicle, downgrade);
                //scene.Session.AddEndOfFrameAction(delegate
                //{
                NewRouteSettings settings = new(vehicle);
                settings.SetVehicleEntity(downgrade);
                settings.upgrade = new UpgradeSettings(vehicle, scene);
                // This should prevent from selling and not getting a new one!
                //scene.Session.Commands.Add(new CommandSell(company.ID, vehicle.ID, vehicle.Type, manager)); 
                //scene.Session.Commands.Add(new CommandNewRoute(company.ID, settings, open: false, manager));
                long price = downgrade.GetPrice(scene, company, scene.Cities[vehicle.Hub.City].User);
                price -= vehicle.GetValue();
                // 2025-10-25 When downgrading it is possible to get price=0, so weight cannot be derived from that;
                if (price <= 0L) price = 1L;
                decimal weight = __instance.CallPrivateMethod<long>("GetBalance", [vehicle]) * 2 / (decimal)price;
                if (manager != null) manager.AddNewPlan(new GeneratedPlan(weight, settings, price, vehicle));
                else company.AI.AddNewPlan(new GeneratedPlan(weight, settings, price, vehicle));
                //});
                MarkLineAsEvaluated();
                return false;
            }
        }

        // Scenario: Sell vehicle
        if (evaluation.Downgrade ||
            (!evaluation.Downgrade && vehs.Length > optimal && worst.Balance.GetSum(2) < -evaluation.profitability / 2 / vehs.Length))
        {
            if (vehs.Length >= 2)
            {
                LogDecision("SELLVEH", worst);
                scene.Session.Commands.Add(new CommandSell(company.ID, worst.ID, worst.Type, manager));
                MarkLineAsEvaluated();
                return false;
            }
        }

        // Scenario: AI last vehicle sell
        if (manager == null && vehs.Length == 1 && worst.Age > 6 && worst.Balance.GetSum(6) < -2 * evaluation.profitability)
        {
            LogDecision("AISELL", worst);
            scene.Session.Commands.Add(new CommandSell(company.ID, worst.ID, worst.Type, manager));
        }

        LogDecision("WAIT", best);

        return false;
    }


    [HarmonyPatch("IsBetter"), HarmonyPrefix]
    public static bool PlanEvaluateRoute_IsBetter_Prefix(PlanEvaluateRoute __instance, ref bool __result, Company company, VehicleBaseEntity original, VehicleBaseEntity next, Hub hub, int range)
    {
        __result = false;
        if (next.Graphics.Entity == null || !next.CanBuy(company, hub.Longitude))
        {
            return false;
        }
        if (next is PlaneEntity _plane && _plane.Range < range)
        {
            return false;
        }
        if (original is TrainEntity _o && next is TrainEntity _n)
        {
            if (_n.Tier > _o.Tier || (_n.Tier == _o.Tier && _n.Max_capacity > _o.Max_capacity))
            {
                __result = __instance.GetPrivateField<long>("wealth") > next.Price;
            }
            return false;
        }
        if (next.Tier > original.Tier || (next.Tier == original.Tier && next.Capacity > original.Capacity))
        {
            // price will be checked against wealth during command execution, as long as there are available vehicles we should try to get one
            __result = true; //  __instance.GetPrivateField<long>("wealth") > next.Price;
            //return false;
        }
        return false;
    }


    [HarmonyPatch("IsWorse"), HarmonyPrefix]
    public static bool PlanEvaluateRoute_IsWorse_Prefix(PlanEvaluateRoute __instance, ref bool __result, Company company, VehicleBaseEntity original, VehicleBaseEntity next, Hub hub, int range)
    {
        __result = false;
        if (next.Graphics.Entity == null)
        {
            return false;
        }
        if (next is PlaneEntity _plane && _plane.Range < range)
        {
            return false;
        }
        if (next.CanBuy(company, hub.Longitude) && original.Price > next.Price)
        {
            __result = true;
        }
        return false;
    }
}


// Line Evaluation Tooltip - UITweaks will access
public static class LineEvaluation
{
    // Data extensions
    internal class ExtraData
    {
        internal string Header = "?"; // companyId-lineId-hubName
        internal readonly List<List<string>> Text = []; // text lines
    }
    private static readonly ConditionalWeakTable<Line, ExtraData> _extras = [];
    internal static ExtraData Extra(this Line line) => _extras.GetOrCreateValue(line);

    // 2025-10-26 Race condition happens here if the line is split into 2+ hubs!
    internal static void NewEvaluation(this Line line, string header)
    {
        string _header = line.Extra().Header;
        if (!_header.Contains(header))
            line.Extra().Header = _header == "?" ? header : _header + "|" + header;
        List<string> newEval = [$"[{DateTime.Now:HH:mm:ss}]  {header}"];
        List<List<string>> _text = line.Extra().Text;
        lock (_text)
        {
            _text.Insert(0, newEval);
            if (_text.Count == 4) _text.RemoveAt(3); // keep only last 3 evals
        }
    }

    internal static void AddEvaluationText(this Line line, string text)
    {
        line.Extra().Text.First().Add(text);
    }

    public static int GetNumEvaluations(Line line)
    {
        return line.Extra().Text.Count;
    }

    public static TooltipPreset GetEvaluationTooltip(Line line, GameEngine engine)
    {
        if (line.Extra().Text.Count == 0)
            return TooltipPreset.Get("No evaluations", engine, can_lock: true);
        TooltipPreset tooltip = TooltipPreset.Get(line.Extra().Header, engine, can_lock: true);
        List<List<string>> _text = line.Extra().Text;
        lock (_text)
        {
            for (int i = 0; i < _text.Count; i++)
            {
                if (i > 0) tooltip.AddSeparator();
                tooltip.AddBoldLabel(_text[i][0]);
                for (int j = 1; j < _text[i].Count; j++)
                    tooltip.AddDescription(_text[i][j]);
            }
        }
        tooltip.GetPrivateField<STMG.UI.Control.ControlCollection>("main_control").Size_local = new Vector2(650, MainData.Tooltip_header);
        return tooltip;
    }
}
