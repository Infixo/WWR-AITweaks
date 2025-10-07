#define LOGGING

using HarmonyLib;
using STM;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.AI;
using STM.GameWorld.Commands;
using STM.GameWorld.Users;
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
        // If there are already new vehicles on the line, there is no point in doing eval again, must wait till they start producing results.
        // New vehicles means either added by the player of AI. 1 new for 4 / 5 existing looks ok.
        VehicleBaseUser[] vehs = [.. vehicles.ToArray().Where(v => v.Age > 0)];
        if (vehs.Length == 0 || (vehicles.Length - vehs.Length) > vehicles.Length/5) // TODO: PARAMETER
            return false;

        // Step 2 Assess the line
        // Calculate: waiting, throughput min/ now / max, min vehicles, profitability, total balance
        // Do NOT use average, use simple sum. Since all is extrapolated via min_cap and max_cap. It will simply evaluate the last 3 months performance in total,
        // not average. This will eliminate "10 days" problem.

        VehicleEvaluation evaluation = default;
        for (int i = 0; i < vehs.Length; i++)
            evaluation.Evaluate(vehs[i]);

        Line line = scene.Session.Companies[vehs[0].Company].Line_manager.GetLine(vehs[0]);
        int waiting = (int)line.GetWaiting();

        // optimal number of vehicles
        int optimal = line.Instructions.Cities.Length;
        if (line.Vehicle_type == 1) optimal /= 2; // train
        else if (line.Vehicle_type == 3) optimal *= 2; // ship
        if (line.Instructions.Cyclic) optimal--; // += line.Instructions.Cities.Length - 2;
        optimal = Math.Max(optimal, 2);

        // Step 3 Sort vehicles descending
        // This signature matches the Comparison<T> delegate required by Array.Sort.
        //  < 0 v1 comes first
        //  0 equal
        //  > 0 v1 comes after v2
        static int CompareVehiclesDescending(VehicleBaseUser v1, VehicleBaseUser v2)
        {
            if (v1.Entity_base.Tier == v2.Entity_base.Tier)
                return v2.Balance.GetSum(3).CompareTo(v1.Balance.GetSum(3));
            else
                return (int)(v2.Entity_base.Tier - v1.Entity_base.Tier);
        }
        //Array.Sort(vehs, CompareVehiclesDescending);
        Array.Sort(vehs, (v1, v2) => v2.Balance.GetSum(3).CompareTo(v1.Balance.GetSum(3))); // balance only

#if LOGGING
        // Debug part
        string header = $"[{company.ID}-{line.ID+1}-{scene.Cities[manager.Hub.City].User.Name}] ";
        Log.Write(header + $"START n={vehs.Length}/{optimal} t={evaluation.throughput_min}/{evaluation.throughput_now}/{evaluation.throughput_max} e={evaluation.throughput_now*100m/evaluation.throughput_max:F0} b={evaluation.balance/100/1000:F0}k/{evaluation.profitability/100/1000:F0}k");
        for (int i = 0; i < vehs.Length; i++)
            Log.Write(header + $"{i}. {vehs[i].ID}-{vehs[i].Entity_base.Tier}-{vehs[i].Entity_base.Translated_name} e={vehs[i].Efficiency.GetSumAverage(3)} b={vehs[i].Balance.GetSum(3)/100/1000:F0}k");
#endif

        // Step 3 Decision tree
        int range = __instance.CallPrivateMethod<int>("GetRange", [vehs]);
        VehicleBaseUser best = vehs[0]; // first
        VehicleBaseUser worst = vehs[^1]; // last one
#if LOGGING
        VehicleBaseEntity upgradeTmp = __instance.CallPrivateMethod<VehicleBaseEntity>("GetUpgrade", [company, best, manager, range]);
        VehicleBaseEntity downgradeTmp = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, worst, range]);
        string text = $" up={evaluation.Upgrade} dn={evaluation.Downgrade} b={best.ID} w={worst.ID}";
        if (upgradeTmp == null) text += " up=none";
        else text += $" {upgradeTmp.Tier}-{upgradeTmp.Translated_name}";
        if (downgradeTmp == null) text += " dn=none";
        else text += $" {downgradeTmp.Tier}-{downgradeTmp.Translated_name}";
        string dec = "DEC_";
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
            if (vehs.Length > optimal && worst.Balance.GetSum(3) < -evaluation.profitability / vehs.Length)
                dec += "_SELL";
        }
        if (evaluation.Upgrade && evaluation.Downgrade)
        {
            dec += "ERROR";
        }
        Log.Write(header + dec + text);
#endif

        // Scenario: Upgrade existing
        if (evaluation.Upgrade && vehs.Length >= optimal)
        {
            // Find vehicle to upgrade, strting with best; if none can be upgraded then ADD_NEW
            VehicleBaseUser vehicle = null;
            VehicleBaseEntity upgrade = null;
            for (int i = 0; i < vehs.Length && upgrade == null; i++)
            {
                vehicle = vehs[i];
                upgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetUpgrade", [company, vehicle, manager, range]);
            }
            if (upgrade != null && vehicle != null)
            {
                // LOGIC
                vehicle.evaluated_on = vehicle.stops;
                NewRouteSettings settings = new(vehicle);
                settings.SetVehicleEntity(upgrade);
                long price = upgrade.GetPrice(scene, company, scene.Cities[vehicle.Hub.City].User);
                price -= vehicle.GetValue();
                if (price <= 0L) price = 1L;
                decimal weight = __instance.CallPrivateMethod<long>("GetBalance", [vehicle]) * 2;
#if LOGGING
                LogEvent("UPGRADE", vehicle, 0, upgrade);
#endif
                manager.AddNewPlan(new GeneratedPlan(weight / (decimal)price, settings, price, vehicle));
                return false;
            }
        }

        // Scenario: Add a new vehicle
        if (evaluation.Upgrade)
        {
            VehicleBaseEntity newVehicle = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, best, range]);
            newVehicle ??= best.Entity_base.Company.Entity.Vehicles[0];
            // generate commands
            NewRouteSettings settings = new(best);
            settings.SetVehicleEntity(newVehicle);
            long price = newVehicle.GetPrice(scene, company, scene.Cities[best.Hub.City].User);
            if (best.Hub.Full())
                price += best.Hub.GetNextLevelPrice(scene.Session);
#if LOGGING
            LogEvent("ADDNEW", best, 0, newVehicle);
#endif
            manager.AddNewPlan(new GeneratedPlan(1m, settings, price));
            return false;
        }

        // Scenario: Downgrade existing
        if (evaluation.Downgrade && vehs.Length <= optimal)
        {
            // Find vehicle to downgrade, strting with the worst; if none can be downgraded, then SELL
            VehicleBaseUser vehicle = null;
            VehicleBaseEntity downgrade = null;
            for (int i = 0; i < vehs.Length && downgrade == null; i++)
            {
                vehicle = vehs[^(i + 1)];
                downgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, vehicle, range]);
            }
            if (downgrade != null && vehicle != null)
            {
#if LOGGING
                LogEvent("DOWNGRADE", vehicle, 0, downgrade);
#endif
                scene.Session.AddEndOfFrameAction(delegate
                {
                    NewRouteSettings settings = new(vehicle);
                    settings.SetVehicleEntity(downgrade);
                    settings.upgrade = new UpgradeSettings(vehicle, scene);
                    scene.Session.Commands.Add(new CommandSell(company.ID, vehicle.ID, vehicle.Type, manager));
                    scene.Session.Commands.Add(new CommandNewRoute(company.ID, settings, open: false, manager));
                });
                return false;
            }
        }

        // Scenario: Sell vehicle
        if (evaluation.Downgrade ||
            (!evaluation.Downgrade && vehs.Length > optimal && worst.Balance.GetSum(3) < -evaluation.profitability / vehs.Length))
        {
            if (vehs.Length >= 2)
            {
#if LOGGING
                LogEvent("SELLVEH", worst);
#endif
                scene.Session.Commands.Add(new CommandSell(company.ID, worst.ID, worst.Type, manager));
                return false;
            }
        }

        // Scenario: AI last vehicle sell
        if (manager == null && vehs.Length == 1 && worst.Age > 6 && worst.Balance.GetSum(6) < -2 * evaluation.profitability)
        {
#if LOGGING
            LogEvent("AISELL", worst);
#endif
            scene.Session.Commands.Add(new CommandSell(company.ID, worst.ID, worst.Type, manager));
        }

        return false;

        //////////////////////////////////////////////// OLD CODE


        if (!evaluation.Downgrade)
        {
            VehicleBaseUser _user = __instance.CallPrivateMethod<VehicleBaseUser>("GetWorst", [vehs]);
            if (_user == null || _user.Balance.GetSum(3) >= -10000000)
            {
                if (evaluation.Upgrade)
                {
                    // the line can be upgraded only if there is at least 1 vehicle left
                    // with 0 vehicles, Evaluation will yield Upgrade=True only if balance is > 0
                    // but it is not possible because with baalance > 0 it would not sell the last vehicle
                    VehicleBaseUser _best = __instance.CallPrivateMethod<VehicleBaseUser>("GetBest", [vehs]);

                    // This is used to establish if new vehicle will be profitable i.e. current load + new load from waiting passengers will be enough to fill Real_Min_Passangers.
                    // However, formula Waiting/24 does not make any sense.
                    //decimal _best_e = (decimal)(_best.Efficiency.GetBestOfTwo() * _best.Passengers.Capacity) / 100m + (decimal)_best.Route.GetWaiting() / 24m / (decimal)vehicles.Length;
                    // New algorithm
                    int currentlyUsedCapacity = _best.Passengers.Capacity * (int)_best.Efficiency.GetQuarterAverage() / 100;
                    //int currentlyUsedCapacity = _best.Passengers.Capacity * (int)_best.GetQuarterEfficiency() / 100;
                    // find a line to which _best belongs to
                    int averageThroughput = (int)_best.Throughput.GetQuarterAverage();
                    int extraNeededCapacity = averageThroughput > 0 ? currentlyUsedCapacity * waiting / line.Vehicles / averageThroughput : currentlyUsedCapacity/2; // inc by 50% by default
                    decimal _best_e = (decimal)(currentlyUsedCapacity + extraNeededCapacity);
#if LOGGING
                    Log.Write($"city={scene.Cities[manager.Hub.City].User.Name} id={line.ID+1} cur={currentlyUsedCapacity} ex={extraNeededCapacity} waitPerVeh={waiting / line.Vehicles} thr={averageThroughput} ");
#endif
                    NewRouteSettings _settings = new NewRouteSettings(_best);
                    if (_best.evaluated_on != _best.stops && new VehicleEvaluation(_best).Upgrade)
                    {
                        VehicleBaseEntity _upgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetUpgrade", [company, _best, manager, range]);
                        if (_upgrade != null)
                        {
                            _settings.SetVehicleEntity(_upgrade);
                            _best.evaluated_on = _best.stops;
                            // This part is executed when new load is enough to fill the upgrade
                            if (_best_e > (decimal)_upgrade.Real_min_passengers)
                            {
                                // Here we upgrade our best vehicle to even a better one
                                if (manager != null)
                                {
                                    long _price6 = _upgrade.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                                    _price6 -= _best.GetValue();
                                    if (_price6 == 0L)
                                    {
                                        _price6 = 1L;
                                    }
                                    decimal _weight4 = __instance.CallPrivateMethod<long>("GetBalance", [_best]) * 2;
#if LOGGING
                                    LogEvent("UPPLAN1", _best, _best_e, _upgrade);
#endif
                                    manager.AddNewPlan(new GeneratedPlan(_weight4 / (decimal)_price6, _settings, _price6, _best));
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
#if LOGGING
                                    LogEvent("UPPLAN2", _best, _best_e, _upgrade);
#endif
                                    company.AI.AddNewPlan(new GeneratedPlan(_weight3 / (decimal)_price5, _settings, _price5, _best));
                                }
                                else
                                {
                                    // Not sure when this happens. There is no AI, and no hub manager.
                                    // Looks like EvaluateLine is used 3 times, but only Hub!=null is a real one, other 2 are never called (stubs? dead code from the past?)
#if LOGGING
                                    LogEvent("UPGRADE1", _best, _best_e, _upgrade);
#endif
                                    scene.Session.AddEndOfFrameAction(delegate
                                    {
                                        _settings.upgrade = new UpgradeSettings(_best, scene);
                                        scene.Session.Commands.Add(new CommandSell(company.ID, _best.ID, _best.Type));
                                        scene.Session.Commands.Add(new CommandNewRoute(company.ID, _settings, open: false));
                                    });
                                }
                                return false;
                            }
                            // Here we continue with the upgrade, but the better one has too high real_min_passengers threshold (cannot fill it)
                            // Never actually been logged... Perhaps at the start of the game when high-tier vehicles are not yet available?
                            // TODO: this should buy a smaller vehicle, not sell and forcefully upgrade
                            _best.evaluated_on = _best.stops;
                            if (evaluation.samples > 1)
                            {
                                VehicleBaseUser _worst2 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehs]);
                                _best_e += (decimal)(_worst2.Efficiency.GetQuarterAverage() * _worst2.Passengers.Capacity) / 90m;
                                // This part is executed when new load is not enough to fill the upgrade.
                                // So, manager sells the worst performing vehicle and tries to move its load to a new upgrade.
                                if (_best_e > (decimal)_upgrade.Real_min_passengers)
                                {
                                    // Also dangerous, it sells a vehicle but a new may not be bought...
                                    // Granted that there is nore than 1 vehicle, but until GeneratedPlan() is fixed, that basically harms the route
                                    scene.Session.Commands.Add(new CommandSell(company.ID, _worst2.ID, _worst2.Type, manager));
#if LOGGING
                                    LogEvent("UPSELL1", _worst2);
#endif
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
#if LOGGING
                                        LogEvent("UPPLAN3", _best, _best_e, _upgrade);
#endif
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
#if LOGGING
                                        LogEvent("UPPLAN4", _best, _best_e, _upgrade);
#endif
                                    }
                                    else
                                    {
                                        // this seems stupid, it sells the vehicle and buys the same one again???
                                        scene.Session.AddEndOfFrameAction(delegate
                                        {
                                            _settings.upgrade = new UpgradeSettings(_best, scene);
                                            scene.Session.Commands.Add(new CommandSell(company.ID, _best.ID, _best.Type));
                                            scene.Session.Commands.Add(new CommandNewRoute(company.ID, _settings, open: false));
                                        });
#if LOGGING
                                        LogEvent("UPGRADE2", _best, _best_e, _upgrade);
#endif
                                    }
                                        return false;
                                }
                            }
                        }
                    }
                    
                    // Here we continue with line upgrade BUT there is no better vehicle
                    // INFIXO: What is missing here:
                    // a) scenario where all vehicles are now blocked in the manager and it needs to find one from a different company TODO for later
                    // b) adding a vehicle when there are more than 3 on the line
                    
                    if (!_best.Entity_base.CanBuy(company, _best.Hub.Longitude)) // || _evaluation.samples >= 3) // Infixo: does it mean that it will never have more than 3 vehicles? Allow for more than 3
                    {
                        return false;
                    }

                    // find best vehicle to serve half of needs - note that we need to servce extraNeededCapacity!
                    _best_e = extraNeededCapacity / 2;
                    VehicleBaseEntity newVehicle = _best.Entity_base;
                    VehicleCompanyEntity _vehicle_company = newVehicle.Company.Entity;
                    while (_best_e < (decimal)newVehicle.Real_min_passengers)
                    {
                        bool _res = false;
                        for (int i = _vehicle_company.Vehicles.Count - 1; i >= 0; i--)
                        {
                            _ = PlanEvaluateRoute_IsWorse_Prefix(__instance, ref _res, company, newVehicle, _vehicle_company.Vehicles[i], _best.Hub, range);
                            if (_res)
                            {
                                newVehicle = _vehicle_company.Vehicles[i];
                                break;
                            }
                        }
                        if (!_res) return false; // we only break if some reason we cannot a downgrade after looking through entire chain
                    }

                    // switch the vehicle
                    _settings.SetVehicleEntity(newVehicle);

                    if (manager != null)
                    {
                        long _price2 = newVehicle.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                        if (_best.Hub.Full())
                        {
                            _price2 += _best.Hub.GetNextLevelPrice(scene.Session);
                        }
#if LOGGING
                        LogEvent("UPPLAN5", _best, _best_e, newVehicle);
#endif
                        manager.AddNewPlan(new GeneratedPlan(1m, _settings, _price2));
                        return false;
                    }
                    if (company.AI != null)
                    {
                        long _price = newVehicle.GetPrice(scene, company, scene.Cities[_best.Hub.City].User);
                        if (_best.Hub.Full())
                        {
                            _price += _best.Hub.GetNextLevelPrice(scene.Session);
                        }
#if LOGGING
                        LogEvent("UPPLAN6", _best, _best_e, newVehicle);
#endif
                        company.AI.AddNewPlan(new GeneratedPlan(1m, _settings, _price));
                        return false;
                    }
                    if (_best.Hub.Full())
                    {
                        if (company.Wealth < _best.Hub.GetNextLevelPrice(scene.Session))
                        {
                            return false;
                        }
#if LOGGING
                        LogEvent("HUBFULL", _best);
#endif
                        scene.Session.Commands.Add(new CommandUpgradeHub(company.ID, _best.Hub.City));
                    }
#if LOGGING
                    LogEvent("UPGRADE3", _best, _best_e, newVehicle);
#endif
                    scene.Session.Commands.Add(new CommandNewRoute(company.ID, _settings, open: false));
                }
                else if (vehs.Length > 1)
                {
                    VehicleBaseUser _worst = __instance.CallPrivateMethod<VehicleBaseUser>("GetWorst", [vehs]);
                    if (_worst != null && _worst.evaluated_on != _worst.stops && _worst.Age > 3 && _worst.Balance.GetRollingYear() < 0)
                    {
                        // If there are 2+ vehicles and the line is average (no upgrade, no downgrade) BUT generates loses, it sells 1 vehicle
#if LOGGING
                        LogEvent("UPSELL2", _worst);
#endif
                        scene.Session.Commands.Add(new CommandSell(company.ID, _worst.ID, _worst.Type, manager));
                    }
                }
                else if (__instance.CallPrivateMethod<bool>("AllVehiclesNotDelivering", [vehs]))
                {
                    __instance.CallPrivateMethodVoid("CheckIndirect", [company, vehs, scene, manager]);
                }
                return false;
            }
        }
        int _range = __instance.CallPrivateMethod<int>("GetRange", [vehs]);
        // downgrade vehicles if there are more than 1 (because Evaluation has more than 1 sample)
        while (evaluation.Downgrade && evaluation.samples > 1)
        {
            // Infixo: there is some issue with samples, it is being decreased too many times e.g. Remove does it so why is it here?
            //_evaluation.samples--;
            // looks for vehicle to downgrade based on balance of last 2 months
            VehicleBaseUser _worst4 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehs]);
            if (_worst4 == null || _worst4.evaluated_on == _worst4.stops || (_worst4.Balance.GetBestOfTwo() > 0 && _worst4.Balance.GetLastMonth() + _worst4.Balance.GetSum(3) > -10000000))
            {
                // stop downgrading if there is no worse vehicle? or balance is > 0
                return false;
            }
            // this actually finds a vehicle entity for a replacement
            VehicleBaseEntity _downgrade2 = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, _worst4, _range]);
            if (_downgrade2 != null)
            {
#if LOGGING
                LogEvent("DOWNGRADE1", _worst4, -1, _downgrade2);
#endif
                scene.Session.AddEndOfFrameAction(delegate
                {
                    NewRouteSettings newRouteSettings2 = new NewRouteSettings(_worst4);
                    newRouteSettings2.SetVehicleEntity(_downgrade2);
                    newRouteSettings2.upgrade = new UpgradeSettings(_worst4, scene);
                    scene.Session.Commands.Add(new CommandSell(company.ID, _worst4.ID, _worst4.Type, manager));
                    scene.Session.Commands.Add(new CommandNewRoute(company.ID, newRouteSettings2, open: false, manager));
                });
            }
            else
            {
                // the while only starts if there is more than 1 vehicle, so this would sell 2nd to last
#if LOGGING
                LogEvent("DOWNSELL1", _worst4);
#endif
                scene.Session.Commands.Add(new CommandSell(company.ID, _worst4.ID, _worst4.Type, manager));
            }
            VehicleEvaluation _e = new VehicleEvaluation(_worst4);
            evaluation.Remove(_e);
        }
        if (!evaluation.Downgrade || evaluation.samples != 1)
        {
            return false;
        }
        VehicleBaseUser _worst3 = __instance.CallPrivateMethod<VehicleBaseUser>("GetNextDowngrade", [vehs]);
        if (_worst3.evaluated_on == _worst3.stops)
        {
            return false;
        }
        VehicleBaseEntity _downgrade = __instance.CallPrivateMethod<VehicleBaseEntity>("GetDowngrade", [company, _worst3, _range]);
        if (_downgrade != null)
        {
#if LOGGING
            LogEvent("DOWNGRADE2", _worst3, -1, _downgrade);
#endif
            scene.Session.AddEndOfFrameAction(delegate
            {
                NewRouteSettings newRouteSettings = new NewRouteSettings(_worst3);
                newRouteSettings.SetVehicleEntity(_downgrade);
                newRouteSettings.upgrade = new UpgradeSettings(_worst3, scene);
                scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
                scene.Session.Commands.Add(new CommandNewRoute(company.ID, newRouteSettings, open: false, manager));
            });
        }
        else if (_worst3.Entity_base.Tier == 1 && _worst3.Age >= 6 && _worst3.Balance.GetRollingYear() < -10000000 && manager == null)
        {
            // 2025-09-29 I don't want the last vehicle to be sold
            if (company == scene.Session.GetPlayer() && vehs.Length == 1)
                return false;
            // DANGEROUS - can sell last Tier1 especially for new lines! it has 6 months to get to 10mil!!!
#if LOGGING
            LogEvent("DOWNSELL2", _worst3);
#endif
            scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
        }
        else if (_worst3.Entity_base.Tier == 1 && _worst3.Age >= 12 && _worst3.Balance.GetRollingYear() < -5000000 && manager == null)
        {
            // 2025-09-29 I don't want the last vehicle to be sold
            if (company == scene.Session.GetPlayer() && vehs.Length == 1)
                return false;
            // DANGEROUS - can sell last Tier1 especially for new lines! it has 12 months to get to 5mil!!!
#if LOGGING
            LogEvent("DOWNSELL3", _worst3);
#endif
            scene.Session.Commands.Add(new CommandSell(company.ID, _worst3.ID, _worst3.Type, manager));
        }
        return false;

#if LOGGING
        // debug & tracking
        void LogEvent(string type, VehicleBaseUser vh1, decimal bestEff = 0m, VehicleBaseEntity? vh2 = null)
        {
            Line line = scene.Session.Companies[vh1.Company].Line_manager.GetLine(vh1);
            string header = $"[{company.ID}-{line.ID + 1}-{scene.Cities[manager.Hub.City].User.Name}] ";
            string route = $"{vh1.Route.Last.Name} -> {vh1.Route.Current.Name} -> {vh1.Route.Destination.Name}";
            string vh2txt = vh2 != null ? $"  vh2= {vh2.Tier} {vh2.Translated_name}" : "";
            Log.Write($"""

                {header} {type} veh={vehs.Length} {route} wait={line.GetWaiting()}
                ..up={evaluation.Upgrade} dn={evaluation.Downgrade} num={evaluation.samples} use={100*(int)evaluation.throughput_now/(int)evaluation.throughput_max} thr={evaluation.throughput_min}/{evaluation.throughput_now}/{evaluation.throughput_max} bal={evaluation.balance}
                ..vh1= {vh1.ID}-{vh1.Entity_base.Tier}-{vh1.Entity_base.Translated_name} be={bestEff}{vh2txt}
            """);
        }
#endif
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
                //return false;
            }
            return false;
        }
        if (next.Tier > original.Tier || (next.Tier == original.Tier && next.Capacity > original.Capacity))
        {
            __result = __instance.GetPrivateField<long>("wealth") > next.Price;
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
            //if (original.Tier >= next.Tier)
            //{
            //return original.Real_min_passengers > next.Real_min_passengers;
            //}
            //return false;
        }
        return false;
    }
}
