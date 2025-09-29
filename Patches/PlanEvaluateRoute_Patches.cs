using HarmonyLib;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.AI;
using STM.GameWorld.Commands;
using STM.GameWorld.Users;
using STM.UI.Floating;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace AITweaks.Patches;


[HarmonyPatch(typeof(PlanEvaluateRoute))]
public static class PlanEvaluateRoute_Patches
{

    [HarmonyPatch("EvaluateLine"), HarmonyPrefix]
    public static bool PlanEvaluateRoute_EvaluateLine_Prefix(PlanEvaluateRoute __instance, Company company, VehicleBaseUser[] vehicles, GameScene scene, HubManager manager)
    {

        VehicleEvaluation _evaluation = default(VehicleEvaluation);
        for (int i = 0; i < vehicles.Length; i++)
        {
            if (vehicles[i].Age > 1) // Allow a vehicle to accrue at least 2 months of data
            {
                _evaluation.Evaluate(vehicles[i]);
            }
        }
        if (!_evaluation.Downgrade)
        {
            VehicleBaseUser _user = __instance.CallPrivateMethod<VehicleBaseUser>("GetWorst", [vehicles]);
            if (_user == null || _user.Balance.GetCurrentMonth() >= -10000000)
            {
                if (_evaluation.Upgrade)
                {
                    // the line can be upgraded only if there is at least 1 vehicle left
                    // with 0 vehicles, Evaluation will yield Upgrade=True only if balance is > 0
                    // but it is not possible because with baalance > 0 it would not sell the last vehicle
                    VehicleBaseUser _best = __instance.CallPrivateMethod<VehicleBaseUser>("GetBest", [vehicles]);

                    // This is used to establish if new vehicle will be profitable i.e. current load + new load from waiting passengers will be enough to fill Real_Min_Passangers.
                    // However, formula Waiting/24 does not make any sense.
                    //decimal _best_e = (decimal)(_best.Efficiency.GetBestOfTwo() * _best.Passengers.Capacity) / 100m + (decimal)_best.Route.GetWaiting() / 24m / (decimal)vehicles.Length;
                    // New algorithm
                    int currentlyUsedCapacity = _best.Passengers.Capacity * (int)_best.Efficiency.GetSumAverage(3) / 100;
                    int extraNeededCapacity = currentlyUsedCapacity * (int)_best.Route.GetWaiting() / vehicles.Length / (int)_best.Throughput.GetSumAverage(3);
                    decimal _best_e = (decimal)(currentlyUsedCapacity + extraNeededCapacity);
                    Log.Write($"city={scene.Cities[manager.Hub.City].User.Name} cur={currentlyUsedCapacity} ex={extraNeededCapacity} waitPerVeh={(int)_best.Route.GetWaiting() / vehicles.Length} thr={_best.Throughput.GetSumAverage(3)} ");

                    NewRouteSettings _settings = new NewRouteSettings(_best);
                    int _range2 = __instance.CallPrivateMethod<int>("GetRange", [vehicles]);
                    if (_best.evaluated_on != _best.stops && new VehicleEvaluation(_best).Upgrade)
                    {
                        VehicleBaseEntity _upgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetUpgrade", [company, _best, manager, _range2]);
                        if (_upgrade != null)
                        {
                            _settings.SetVehicleEntity(_upgrade);
                            _best.evaluated_on = _best.stops;
                            // This part is executed when new load is enough to fill the upgrade
                            if (_best_e > (decimal)_upgrade.Real_min_passengers)
                            {
                                if (manager != null)
                                {
                                    long _price6 = _upgrade.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                                    _price6 -= _best.GetValue();
                                    if (_price6 == 0L)
                                    {
                                        _price6 = 1L;
                                    }
                                    decimal _weight4 = __instance.CallPrivateMethod<long>("GetBalance", [_best]) * 2;
                                    manager.AddNewPlan(new GeneratedPlan(_weight4 / (decimal)_price6, _settings, _price6, _best));
                                    LogEvent("UPPLAN1", _best, _best_e, _upgrade);
                                }
                                else if (company.AI != null)
                                {
                                    long _price5 = _upgrade.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                                    _price5 -= _best.GetValue();
                                    decimal _weight3 = __instance.CallPrivateMethod<long>("GetBalance", [_best]) * 2;
                                    if (_price5 == 0L)
                                    {
                                        _price5 = 1L;
                                    }
                                    company.AI.AddNewPlan(new GeneratedPlan(_weight3 / (decimal)_price5, _settings, _price5, _best));
                                    LogEvent("UPPLAN2", _best, _best_e, _upgrade);
                                }
                                else
                                {
                                    scene.Session.AddEndOfFrameAction(delegate
                                    {
                                        _settings.upgrade = new UpgradeSettings(_best, scene);
                                        scene.Session.Commands.Add(new CommandSell(company.ID, _best.ID, _best.Type));
                                        scene.Session.Commands.Add(new CommandNewRoute(company.ID, _settings, open: false));
                                    });
                                    LogEvent("UPGRADE1", _best, _best_e, _upgrade);
                                }
                                return false;
                            }
                            _best.evaluated_on = _best.stops;
                            if (_evaluation.samples > 1)
                            {
                                VehicleBaseUser _worst2 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehicles]);
                                _best_e += (decimal)(_worst2.Efficiency.GetSumAverage(3) * _worst2.Passengers.Capacity) / 90m;
                                // This part is executed when new load is not enough to fill the upgrade.
                                // So, manager sells the worst performing vehicle and tries to move its load to a new upgrade.
                                if (_best_e > (decimal)_upgrade.Real_min_passengers)
                                {
                                    scene.Session.Commands.Add(new CommandSell(company.ID, _worst2.ID, _worst2.Type, manager));
                                    LogEvent("UPSELL1", _worst2);
                                    if (manager != null)
                                    {
                                        long _price4 = _upgrade.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                                        _price4 -= _best.GetValue();
                                        decimal _weight2 = __instance.CallPrivateMethod<long>("GetBalance", [_best]) * 2;
                                        if (_price4 == 0L)
                                        {
                                            _price4 = 1L;
                                        }
                                        manager.AddNewPlan(new GeneratedPlan(_weight2 / (decimal)_price4, _settings, _price4, _best));
                                        LogEvent("UPPLAN3", _best, _best_e, _upgrade);
                                    }
                                    else if (company.AI != null)
                                    {
                                        long _price3 = _upgrade.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                                        _price3 -= _best.GetValue();
                                        decimal _weight = __instance.CallPrivateMethod<long>("GetBalance", [_best]) * 2;
                                        if (_price3 == 0L)
                                        {
                                            _price3 = 1L;
                                        }
                                        company.AI.AddNewPlan(new GeneratedPlan(_weight / (decimal)_price3, _settings, _price3, _best));
                                        LogEvent("UPPLAN4", _best, _best_e, _upgrade);
                                    }
                                    else
                                    {
                                        scene.Session.AddEndOfFrameAction(delegate
                                        {
                                            _settings.upgrade = new UpgradeSettings(_best, scene);
                                            scene.Session.Commands.Add(new CommandSell(company.ID, _best.ID, _best.Type));
                                            scene.Session.Commands.Add(new CommandNewRoute(company.ID, _settings, open: false));
                                        });
                                        LogEvent("UPGRADE2", _best, _best_e, _upgrade);
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                    if (!_best.Entity_base.CanBuy(company, _best.Hub.Longitude) || _evaluation.samples >= 3)
                    {
                        return false;
                    }
                    _best_e /= 2m;
                    if (_best_e <= (decimal)_best.Entity_base.Real_min_passengers)
                    {
                        return false;
                    }
                    if (manager != null)
                    {
                        long _price2 = _best.Entity_base.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                        if (_best.Hub.Full())
                        {
                            _price2 += _best.Hub.GetNextLevelPrice(scene.Session);
                        }
                        manager.AddNewPlan(new GeneratedPlan(1m, new NewRouteSettings(_best), _price2));
                        LogEvent("UPPLAN5", _best);
                        return false;
                    }
                    if (company.AI != null)
                    {
                        long _price = _best.Entity_base.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                        if (_best.Hub.Full())
                        {
                            _price += _best.Hub.GetNextLevelPrice(scene.Session);
                        }
                        company.AI.AddNewPlan(new GeneratedPlan(1m, new NewRouteSettings(_best), _price));
                        LogEvent("UPPLAN6", _best);
                        return false;
                    }
                    if (_best.Hub.Full())
                    {
                        if (company.Wealth < _best.Hub.GetNextLevelPrice(scene.Session))
                        {
                            return false;
                        }
                        scene.Session.Commands.Add(new CommandUpgradeHub(company.ID, _best.Hub.City));
                        LogEvent("HUBFULL", _best);
                    }
                    scene.Session.Commands.Add(new CommandNewRoute(company.ID, new NewRouteSettings(_best), open: false));
                    LogEvent("UPGRADE3", _best);
                }
                else if (vehicles.Length > 1)
                {
                    VehicleBaseUser _worst = __instance.CallPrivateMethod<VehicleBaseUser>("GetWorst", [vehicles]);
                    if (_worst != null && _worst.evaluated_on != _worst.stops && _worst.Age > 3 && _worst.Balance.GetRollingYear() < 0)
                    {
                        // If there are 2+ vehicles and the line is average (no upgrade, no downgrade) BUT generates loses, it sells 1 vehicle
                        scene.Session.Commands.Add(new CommandSell(company.ID, _worst.ID, _worst.Type, manager));
                        LogEvent("UPSELL2", _worst);
                    }
                }
                else if (__instance.CallPrivateMethod<bool>("AllVehiclesNotDelivering", [vehicles]))
                {
                    __instance.CallPrivateMethodVoid("CheckIndirect", [company, vehicles, scene, manager]);
                }
                return false;
            }
        }
        int _range = __instance.CallPrivateMethod<int>("GetRange", [vehicles]);
        while (_evaluation.Downgrade && _evaluation.samples > 1)
        {
            // downgrade vehicles if there are more than 1 (because Evaluation has more than 1 sample)
            _evaluation.samples--;
            // looks for vehicle to downgrade based on balance of last 2 months
            VehicleBaseUser _worst4 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehicles]);
            if (_worst4 == null || _worst4.evaluated_on == _worst4.stops || (_worst4.Balance.GetBestOfTwo() > 0 && _worst4.Balance.GetLastMonth() + _worst4.Balance.GetCurrentMonth() > -10000000))
            {
                // stop downgrading if there is no worse vehicle? or balance is > 0
                return false;
            }
            // this actually finds a vehicle entity for a replacement
            VehicleBaseEntity _downgrade2 = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, _worst4, _range]);
            if (_downgrade2 != null)
            {
                scene.Session.AddEndOfFrameAction(delegate
                {
                    NewRouteSettings newRouteSettings2 = new NewRouteSettings(_worst4);
                    newRouteSettings2.SetVehicleEntity(_downgrade2);
                    newRouteSettings2.upgrade = new UpgradeSettings(_worst4, scene);
                    scene.Session.Commands.Add(new CommandSell(company.ID, _worst4.ID, _worst4.Type, manager));
                    scene.Session.Commands.Add(new CommandNewRoute(company.ID, newRouteSettings2, open: false, manager));
                    LogEvent("DOWNGRADE1", _worst4, -1, _downgrade2);
                });
            }
            else
            {
                // the while only starts if there is more than 1 vehicle, so this would sell 2nd to last
                scene.Session.Commands.Add(new CommandSell(company.ID, _worst4.ID, _worst4.Type, manager));
                LogEvent("DOWNSELL1", _worst4);
            }
            VehicleEvaluation _e = new VehicleEvaluation(_worst4);
            _evaluation.Remove(_e);
        }
        if (!_evaluation.Downgrade || _evaluation.samples != 1)
        {
            return false;
        }
        VehicleBaseUser _worst3 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehicles]);
        if (_worst3.evaluated_on == _worst3.stops)
        {
            return false;
        }
        VehicleBaseEntity _downgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, _worst3, _range]);
        if (_downgrade != null)
        {
            scene.Session.AddEndOfFrameAction(delegate
            {
                NewRouteSettings newRouteSettings = new NewRouteSettings(_worst3);
                newRouteSettings.SetVehicleEntity(_downgrade);
                newRouteSettings.upgrade = new UpgradeSettings(_worst3, scene);
                scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
                scene.Session.Commands.Add(new CommandNewRoute(company.ID, newRouteSettings, open: false, manager));
                LogEvent("DOWNGRADE2", _worst3, -1, _downgrade);
            });
        }
        else if (_worst3.Entity_base.Tier == 1 && _worst3.Age >= 6 && _worst3.Balance.GetRollingYear() < 10000000 && manager == null)
        {
            // 2025-09-29 I don't want the last vehicle to be sold
            if (company == scene.Session.GetPlayer() && vehicles.Length == 1)
                return false;
            // DANGEROUS - can sell last Tier1 especially for new lines! it has 6 months to get to 10mil!!!
            scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
            LogEvent("DOWNSELL2", _worst3);
        }
        else if (_worst3.Entity_base.Tier == 1 && _worst3.Age >= 12 && _worst3.Balance.GetRollingYear() < 5000000 && manager == null)
        {
            // 2025-09-29 I don't want the last vehicle to be sold
            if (company == scene.Session.GetPlayer() && vehicles.Length == 1)
                return false;
            // DANGEROUS - can sell last Tier1 especially for new lines! it has 12 months to get to 5mil!!!
            scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
            LogEvent("DOWNSELL3", _worst3);
        }
        return false;

        // debug & tracking
        //[MethodImpl(MethodImplOptions.NoInlining)]
        void LogEvent(string type, VehicleBaseUser vh1, decimal bestEff = 0m, VehicleBaseEntity? vh2 = null)
        {
            string route = $"{vh1.Route.Last.Name} -> {vh1.Route.Current.Name} -> {vh1.Route.Destination.Name}";
            string vh2txt = vh2 != null ? $"  vh2= {vh2.Tier} {vh2.Translated_name}" : "";
            Log.Write($"""

                city={scene.Cities[manager.Hub.City].User.Name} dec={type} veh={vehicles.Length} {route} wait={vh1.Route.GetWaiting()}
                ..up={_evaluation.Upgrade} dn={_evaluation.Downgrade} num={_evaluation.samples} use={100*(int)_evaluation.throughput_now/(int)_evaluation.throughput_max} thr={_evaluation.throughput_min}/{_evaluation.throughput_now}/{_evaluation.throughput_max} bal={_evaluation.balance}
                ..vh1= {vh1.Entity_base.Tier} {vh1.Entity_base.Translated_name} be={bestEff}{vh2txt}
            """);
        }
    }

    /*
    [HarmonyPatch("GetBetter"), HarmonyPrefix]
    public static bool GetBetter_Prefix(PlanEvaluateRoute __instance, ref VehicleBaseEntity __result, Company company, VehicleCompanyEntity vehicle_company, VehicleBaseEntity entity, Hub hub, int range)
    {
        for (int i = 0; i < vehicle_company.Vehicles.Count; i++)
        {
            bool temp = __instance.CallPrivateMethod<bool>("IsBetter", [company, entity, vehicle_company.Vehicles[i], hub, range]);
            if (vehicle_company.Vehicles[i].Type_name == entity.Type_name && temp)
            {
                __result = vehicle_company.Vehicles[i];
                return false;
            }
        }
        __result = null;
        return false;
    }
    */

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
                return false;
            }
            return false;
        }
        if (next.Tier > original.Tier || (next.Tier == original.Tier && next.Capacity > original.Capacity))
        {
            __result = __instance.GetPrivateField<long>("wealth") > next.Price;
            return false;
        }
        return false;
    }


    [HarmonyPatch("IsWorse"), HarmonyPrefix]
    public static bool IsWorse(PlanEvaluateRoute __instance, ref bool __result, Company company, VehicleBaseEntity original, VehicleBaseEntity next, Hub hub, int range)
    {
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
            //if (original.Tier >= next.Tier)
            //{
                //return original.Real_min_passengers > next.Real_min_passengers;
            //}
            return true;
        }
        return false;
    }


    /*
    [HarmonyPatch(typeof(VehicleEvaluation), "Evaluate"), HarmonyPrefix]
    public static bool Evaluate(VehicleEvaluation __instance, VehicleBaseUser vehicle)
    {
        long GetBest(YearlyMetrics metrics)
        {
            if (metrics.GetCurrentMonth() == 0L)
            {
                return metrics.GetLastMonth();
            }
            return metrics.GetCurrentMonth();
        }

        __instance.samples++;
        long _now = GetBest(vehicle.Throughput);
        __instance.balance += vehicle.Balance.GetSum(2);
        if (_now == 0L || vehicle.Age == 0)
        {
            __instance.throughput_now += 100m;
            __instance.throughput_max += 100m;
            __instance.throughput_min += 100m;
            return false;
        }
        __instance.throughput_now += (decimal)_now;
        long _efficiency = GetBest(vehicle.Efficiency);
        if (_efficiency > 0)
        {
            _now = _now * 100 / _efficiency;
            __instance.throughput_max += (decimal)_now;
            int _capacity = ((vehicle.Entity_base is TrainEntity _train) ? _train.Max_capacity : vehicle.Entity_base.Capacity);
            int _min = vehicle.Entity_base.Real_min_passengers;
            __instance.throughput_min += (decimal)(_now * _min / _capacity);
        }
        return false;
    }
    */
}
