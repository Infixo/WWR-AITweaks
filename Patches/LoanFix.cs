using HarmonyLib;
using STM.Data;
using STM.GameWorld;
using STM.GameWorld.Commands;
using STMG.Utility;
using Utilities;

namespace AITweaks.Patches;


[HarmonyPatch]
public static class LoanFix
{
    [HarmonyPatch(typeof(Company), "SetDataLate"), HarmonyPostfix]
    public static void Company_SetDataLate(Company __instance, SavingHeader header, ReadingStream stream, GameScene scene)
    {
        if (__instance.Loan > MainData.Defaults.Max_loan)
        {
            Log.Write($"LoanFix: [{__instance.ID}] {__instance.Info.Name} loan={__instance.Loan / 100000L}k");
            long _payment = __instance.Loan - MainData.Defaults.Max_loan;
            __instance.PayBackLoan(_payment);
            Log.Write($"LoanFix: [{__instance.ID}] loan={__instance.Loan / 100000L}k money={__instance.Frame_balance / 100000L}k");
        }
    }

    [HarmonyPatch(typeof(CommandLoan), "Same"), HarmonyPrefix]
    public static bool CommandLoan_Same(CommandLoan __instance, ref bool __result, ICommand command)
    {
        __result = __instance.Company == ((CommandLoan)command).Company;
        return false;
    }
}
