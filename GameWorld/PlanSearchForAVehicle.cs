using HarmonyLib;
using STM.Data;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.AI;
using Utilities;

namespace AITweaks.GameWorld;


[HarmonyPatch]
public static class PlanSearchForAVehicle_Patches
{
    [HarmonyPatch(typeof(PlanSearchForAVehicle), "TryAddVehicle"), HarmonyPrefix]
    public static bool PlanSearchForAVehicle_TryAddVehicle_Prefix(PlanSearchForAVehicle __instance, Company company, VehicleBaseEntity entity, GameScene scene,
        ref VehicleBaseEntity ___best, ref decimal ___best_weight, int ___distance, ref int ___v_distance, decimal ___level, Hub ___hub, ref long ___road_price, ref long ___rails_price, NewRouteSettings ___settings)
    {
        if (entity.Graphics.Entity == null || ___hub == null || !entity.CanBuy(company, ___hub.Longitude) || company.GetInventory(entity, ___settings.Cities[0].City.GetCountry(scene), scene) == 0 || !scene.Settings.VehicleIsValid(entity) || (entity.Achievement != null && !entity.Achievement.Unlocked))
        {
            return false;
        }
        long _balance = company.GetLastMonthBalance();
        long _price = entity.GetPrice(scene, company, scene.Cities[___hub.City].User);
        ___v_distance = ___distance;
        if (entity is PlaneEntity _plane && _plane.Range < ___distance)
        {
            return false;
        }
        if (entity is RoadVehicleEntity)
        {
            if (!__instance.CallPrivateMethod<bool>("HasRoad", [scene]))
            {
                return false;
            }
            _price += ___road_price;
        }
        else if (entity is TrainEntity)
        {
            if (!__instance.CallPrivateMethod<bool>("HasRails", [scene]))
            {
                return false;
            }
            _price += ___rails_price;
        }
        else if (entity is ShipEntity && !__instance.CallPrivateMethod<bool>("HasSea", []))
        {
            return false;
        }
        if ((decimal)_price >= (decimal)company.Wealth + (decimal)_balance * ___level / 10m || (_price > company.Wealth + _balance && _balance <= 0))
        {
            return false;
        }
        decimal _destination = (decimal)(1036800 / MainData.Defaults.City_destination_progress) * ___level;
        int _trips = entity.TripsPerYear(___v_distance);
        if (_trips == 0 || (entity.Tier > 1 && (decimal)(_trips * entity.Real_min_passengers) > _destination))
        {
            return false;
        }
        int _tmax = _trips * entity.Capacity;
        if (entity is TrainEntity _train)
        {
            _tmax = _trips * _train.Max_capacity;
        }
        decimal _weight = ((_destination < (decimal)_tmax) ? _destination : ((decimal)_tmax));
        _weight = ((entity is RoadVehicleEntity)
            ? ((decimal)(entity.Passenger_pay_per_km * ___v_distance) * _weight + (decimal)(entity.Cost_per_km * ___v_distance * _trips) + (decimal)(MainData.Defaults.Servicing_bus_station * _trips))
            : ((entity is TrainEntity)
                ? ((decimal)(entity.Passenger_pay_per_km * ___v_distance) * _weight + (decimal)(entity.Cost_per_km * ___v_distance * _trips) + (decimal)(MainData.Defaults.Servicing_train_station * _trips))
                : ((!(entity is PlaneEntity))
                    ? ((decimal)(entity.Passenger_pay_per_km * ___v_distance) * _weight + (decimal)(entity.Cost_per_km * ___v_distance * _trips) + (decimal)(MainData.Defaults.Servicing_port * _trips))
                    : ((decimal)(entity.Passenger_pay_per_km * ___v_distance) * _weight + (decimal)(entity.Cost_per_km * ___v_distance * _trips) + (decimal)(MainData.Defaults.Servicing_airport * _trips)))));
        // _weight at this point is projected yearly revenues from that route.
        // _destination is number of passengers per year
        // _tmax is vehicle max throughput per year
        // This rule boosts routes with enough passengers to fill almost entire vehicle
        if (entity is PlaneEntity)
        {
            if (___v_distance > 2500)
            {
                _weight *= 1.5m;
            }
            if (___v_distance > 1000)
            {
                _weight *= 1.25m;
            }
            else if (___v_distance < 200)
            {
                _weight *= 0.5m;
            }
        }
        else if (entity is RoadVehicleEntity)
        {
            if (___v_distance < 300)
            {
                _weight *= 1.5m;
            }
            else if (___v_distance > 1000)
            {
                return false;
            }
            else if (___v_distance > 600)
            {
                _weight *= 0.5m;
            }
        }
        else if (entity is TrainEntity)
        {
            if (___v_distance > 1000)
            {
                _weight *= 0.5m;
            }
            else if (_destination >= (decimal)_tmax * 0.8m)
            {
                _weight *= 1.5m;
            }
            else if (___v_distance > 2000)
            {
                return false;
            }
            else if (___v_distance > 300)
            {
                _weight *= 1.25m;
            }
        }
        else if (entity is ShipEntity)
        {
            if (___v_distance < 200)
            {
                _weight *= 1.5m;
            }
            else if (___v_distance > 800)
            {
                if (entity.Capacity * 10 < ___v_distance)
                {
                    return false;
                }
                _weight *= 0.5m;
            }
        }
        if (_weight > 0 && (___best == null || _weight > ___best_weight))
        {
            ___best = entity;
            ___best_weight = _weight;
        }
        return false;
    }


    [HarmonyPatch(typeof(Company), "AICanBuyVehicle"), HarmonyPrefix]
    public static bool AICanBuyVehicle(Company __instance, ref bool __result, VehicleBaseEntity entity)
    {
        __result = __instance.Info is CompanyGenerated _generated ? _generated.VehicleIsValid(entity) : true;
        return false;
    }
}
