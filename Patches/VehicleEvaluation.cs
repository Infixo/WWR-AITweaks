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

    public long profitability;

    public bool Downgrade
    {
        get
        {
            if (throughput_now < throughput_min)
                return true;
            decimal treshold = (throughput_max + throughput_min) / 2; // middle point
            if (throughput_now < treshold)
                return balance < -profitability / 4; // TODO: this should relate to vehicle's innate profitability
            return false;
        }
    }

    public bool Upgrade
    {
        get
        {
            decimal treshold = throughput_max - (throughput_max - throughput_min) / 4; // 3/4 of min-max gap
            if (throughput_now > treshold) // There are vehicles that need 80% to be even profitable; probably could relate to difficulty
                return balance > profitability / 4; // TODO: this should relate to vehicle's innate profitability
            treshold = (throughput_max + throughput_min) / 2; // middle point
            if (throughput_now > treshold)
                return balance > profitability / 2;
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
        profitability = 0;
        Evaluate(vehicle);
    }

    public void Remove(VehicleEvaluation eval)
    {
        throughput_min -= eval.throughput_min;
        throughput_max -= eval.throughput_max;
        balance -= eval.balance;
        throughput_now -= eval.throughput_now; // was missing?
        profitability -= eval.profitability;
        samples--;
    }

    // Step 2 Assess the line
    // Calculate: throughput min/ now / max, profitability, balance change, total balance
    // Do NOT use average, use simple sum. Since all is extrapolated via min_cap and max_cap.It will simply evaluate the last 3 months performance in total,
    // not average. This will eliminate "10 days" problem.
    public void Evaluate(VehicleBaseUser vehicle)
    {
        samples++;
        int maxCap = ((vehicle.Entity_base is TrainEntity _train) ? _train.Max_capacity : vehicle.Entity_base.Capacity);
        int minCap = vehicle.Entity_base.Real_min_passengers;
        long profit = vehicle.Entity_base.GetEstimatedProfit();
        for (int offset = 0; offset < 3 && offset < vehicle.Balance.Months; offset++)
        {
            balance += vehicle.Balance.GetOffset(offset);
            profitability += profit;
            long _efficiency = vehicle.Efficiency.GetOffset(offset);
            if (_efficiency > 0)
            {
                long throughput = vehicle.Throughput.GetOffset(offset); // now
                throughput_now += (decimal)throughput;
                throughput = throughput * 100 / _efficiency; // calculate max from efficiency
                throughput_max += (decimal)throughput;
                throughput_min += (decimal)(throughput * minCap / maxCap); // calculate min from capacities ratio
            }
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
