using Cysharp.Threading.Tasks;
using HarmonyLib;

namespace WingCommand
{
 /// <summary>Track carrier door-sequence completion, including launches skipped after damage.</summary>
    [HarmonyPatch(typeof(Hangar), "DoorSequenceCarrier")]
    internal static class HangarDeliveryCompletionPatch
    {
        // Harmony calls this callback by reflection.
#pragma warning disable IDE0051
        [HarmonyPostfix]
        private static void Observe(Hangar __instance, ref UniTask __result)
        {
            WingShopDelivery.PendingDelivery order = WingShopDelivery.StartingAt(__instance);
            if (order == null) return;
            // Capture this order before awaiting hangar reuse. Only the wrapper consumes the original
            // UniTask, which allows one consumer; native Forget consumes the wrapper.
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
