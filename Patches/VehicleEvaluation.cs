using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.Users;

namespace AITweaks.Patches;

public struct VehicleEvaluation
{
    public decimal throughput_now;

    public decimal throughput_min;

    public decimal throughput_max;

    public long balance;

    public int samples;

    public bool Downgrade
    {
        get
        {
            if (!(throughput_now < throughput_min))
            {
                return balance < 0;
            }
            return true;
        }
    }

    public bool Upgrade
    {
        get
        {
            if (throughput_now >= throughput_max * 0.8m)
            {
                return balance > 0;
            }
            return false;
        }
    }

    public VehicleEvaluation(VehicleBaseUser vehicle)
    {
        balance = 0L;
        throughput_now = default(decimal);
        throughput_min = default(decimal);
        throughput_max = default(decimal);
        samples = 0;
        Evaluate(vehicle);
    }

    public void Remove(VehicleEvaluation eval)
    {
        throughput_min -= eval.throughput_min;
        throughput_max -= eval.throughput_max;
        balance -= eval.balance;
        samples--;
    }

    public void Evaluate(VehicleBaseUser vehicle)
    {
        samples++;
        long _now = GetBest(vehicle.Throughput);
        balance += vehicle.Balance.GetSum(2);
        if (_now == 0L || vehicle.Age == 0)
        {
            throughput_now += 100m;
            throughput_max += 100m;
            throughput_min += 100m;
            return;
        }
        throughput_now += (decimal)_now;
        long _efficiency = GetBest(vehicle.Efficiency);
        if (_efficiency > 0)
        {
            _now = _now * 100 / _efficiency;
            throughput_max += (decimal)_now;
            int _capacity = ((vehicle.Entity_base is TrainEntity _train) ? _train.Max_capacity : vehicle.Entity_base.Capacity);
            int _min = vehicle.Entity_base.Real_min_passengers;
            throughput_min += (decimal)(_now * _min / _capacity);
        }
    }

    private long GetBest(YearlyMetrics metrics)
    {
        if (metrics.GetCurrentMonth() == 0L)
        {
            return metrics.GetLastMonth();
        }
        return metrics.GetCurrentMonth();
    }
}
