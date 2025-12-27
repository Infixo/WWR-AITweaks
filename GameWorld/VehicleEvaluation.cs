using STM.Data.Entities;
using STM.GameWorld.Users;

namespace AITweaks.GameWorld;


public struct VehicleEvaluation
{
    public decimal throughput_now;

    public decimal throughput_min;

    public decimal throughput_max;

    public long balance;

    public int samples;

    public long profitability;

    public float sumSpeed;

    public float sumCapacity;

    public float gap;

    public readonly float AvgSpeed => sumSpeed / (float)samples;

    public readonly float AvgCapacity => sumCapacity / sumSpeed;

    public readonly bool Downgrade
    {
        get
        {
            if (throughput_now < throughput_min)
                if (balance < 0 || gap < 1f)
                    return true;
            decimal treshold = throughput_min + (throughput_max - throughput_min) / 3; // 1/3 of min-max gap
            if (throughput_now < treshold)
                if (balance < -profitability / 4 || gap <0.5f) // this should relate to vehicle's innate profitability
                    return true;
            return false; // means we're in or above range or there is enough waiting passengers to not downgrade atm
        }
    }

    public readonly bool Upgrade
    {
        get
        {
            decimal third = (throughput_max - throughput_min) / 3;
            decimal treshold = throughput_max - third; // 2/3 of min-max gap
            if (throughput_now > treshold) // There are vehicles that need 80% to be even profitable; probably could relate to difficulty
                if (balance > profitability / 3 || gap > 0.5f)
                    return true;
            treshold = throughput_min + third; // 1/3 of min-max gap
            if (throughput_now > treshold && gap > 1.0f)
                if (balance > profitability / 4) // this should relate to vehicle's innate profitability
                    return true;
            if (throughput_now > throughput_min && gap > 1.5f && balance > profitability / 5)
                return true;
            return gap > 3.0f && balance > 0;
        }
    }


    // Step 2 Assess the line
    // Calculate: throughput min/ now / max, profitability, balance change, total balance
    // Do NOT use average, use simple sum. Since all is extrapolated via min_cap and max_cap.It will simply evaluate the last 2 months performance in total,
    // not average. This will eliminate "10 days" problem.
    public void Evaluate(VehicleBaseUser vehicle)
    {
        samples++;
        int maxCap = ((vehicle.Entity_base is TrainEntity _train) ? _train.Max_capacity : vehicle.Entity_base.Capacity);
        int minCap = vehicle.Entity_base.Real_min_passengers;
        long profit = vehicle.Entity_base.GetEstimatedProfit();
        sumSpeed += vehicle.Entity_base.Speed;
        sumCapacity += maxCap * vehicle.Entity_base.Speed;
        // 3 months for AI because current is always 0, and 2 months for managers
        for (int offset = 0; offset < 2 && offset < vehicle.Balance.Months; offset++)
        {
            balance += vehicle.Balance.GetOffset(offset);
            profitability += profit;
            long _efficiency = vehicle.Efficiency.GetOffset(offset);
            long throughput = vehicle.Throughput.GetOffset(offset); // now
            if (throughput > 0)
            {
                if (_efficiency > 0)
                {
                    throughput_now += (decimal)throughput;
                    throughput = throughput * 100 / _efficiency; // calculate max from efficiency
                    if (throughput > 0)
                    {
                        throughput_max += (decimal)throughput;
                        throughput_min += (decimal)(throughput * minCap / maxCap); // calculate min from capacities ratio
                    }
                    // 2025-12-27 Very rare case when throughput is so low that max cannot be calculated
                    else
                    {
                        throughput_max += (decimal)maxCap;
                        throughput_min += (decimal)minCap;
                    }
                }
                // 2025-10-17 Fix for a very rare case where both efficiency and throughput are 0, but not at the same time
                else  // (_efficiency == 0)
                {
                    // Eff=0 means travelling empty - we cannot calculate actual throughput numbers
                    // Assumption - capacity values hold
                    throughput_now += (decimal)throughput;
                    throughput_max += (decimal)maxCap; // cannot calculate this since Eff=0
                    throughput_min += (decimal)minCap;
                }
            }
            else
            {
                // Throughput=0 - destination not yet reached!
                throughput_now += (decimal)(maxCap * _efficiency) / 100m; // if Eff=0 then going empty and now=0 which is ok!
                throughput_max += (decimal)maxCap;
                throughput_min += (decimal)minCap;
            }
        }
    }
}
