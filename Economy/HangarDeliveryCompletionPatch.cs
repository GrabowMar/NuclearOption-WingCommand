using Cysharp.Threading.Tasks;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>Observe the native carrier sequence ending, including a launch skipped after damage.</summary>
    [HarmonyPatch(typeof(Hangar), "DoorSequenceCarrier")]
    internal static class HangarDeliveryCompletionPatch
    {
        // Harmony invokes this callback through reflection.
#pragma warning disable IDE0051
        [HarmonyPostfix]
        private static void Observe(Hangar __instance, ref UniTask __result)
        {
            WingShopDelivery.PendingDelivery order = WingShopDelivery.StartingAt(__instance);
            if (order == null) return;
            // Bind the exact order before awaiting: another request can later use this hangar.
            // Replacing the result lets native Forget consume the wrapper; only this wrapper
            // awaits the original UniTask, which does not support multiple consumers.
            __result = ObserveCompletion(__result, order);
        }
#pragma warning restore IDE0051

        private static async UniTask ObserveCompletion(UniTask original,
                                                       WingShopDelivery.PendingDelivery order)
        {
            try { await original; }
            finally { order.NativeSequenceFinished = true; }
        }
    }
}
