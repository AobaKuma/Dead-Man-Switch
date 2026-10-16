using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 從掃描器以外的來源（例如伺服主機被駭）拉設施警報，行為與 CompAlertScanner.FireSignal 對齊：
    /// 先累加地圖警戒值，再廣播帶 MAP 參數的訊號，讓同一張地圖的 CompAlertEffector 反應。
    /// 地圖上完全沒有反應建築時，改呼叫 fallbackRaidWave 保底，確保「一定有警報響應」。
    ///
    /// Trips the facility alarm from something other than a scanner (e.g. a server being hacked), matching
    /// CompAlertScanner.FireSignal: bump the map's alert level, then broadcast the signal with a MAP arg so
    /// same-map CompAlertEffectors respond. With no responders on the map at all, call fallbackRaidWave
    /// instead so the alarm always brings something.
    /// </summary>
    public static class FacilityAlarmUtility
    {
        public const string DefaultSignal = "FFF_AlertScanner_Triggered";

        public static void Trip(Thing source, float increment, string signal, RaidWaveDef fallbackRaidWave)
        {
            Map map = source?.MapHeld;
            if (map == null) return;

            map.GetComponent<MapComponent_AlertCounter>()?.Notify(increment);

            if (!signal.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(signal,
                    source.Named("SUBJECT"), source.PositionHeld.Named("POSITION"), map.Named("MAP")));
            }

            if (fallbackRaidWave != null && !HasResponders(map, signal))
            {
                BattleGroupCallUtility.TryCall(fallbackRaidWave, source, respectCooldown: false);
            }
        }

        /// <summary>
        /// 地圖上有沒有任何聽這個訊號的警報反應建築。被 EMP 癱瘓或斷電的也算：
        /// 玩家先處理掉防禦再駭本來就是正解，這時不該再保底叫援軍。
        /// Whether anything on the map listens for this signal. Stunned or unpowered ones count too: dealing with
        /// the defences first is the intended play, and that shouldn't summon the fallback wave.
        /// </summary>
        private static bool HasResponders(Map map, string signal)
        {
            List<Building> buildings = map.listerBuildings.allBuildingsNonColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building b = buildings[i];
                if (b.Destroyed) continue;
                List<ThingComp> comps = b.AllComps;
                for (int j = 0; j < comps.Count; j++)
                {
                    if (comps[j] is CompAlertEffector effector && effector.Props.listenSignal == signal) return true;
                }
            }
            return false;
        }
    }
}
