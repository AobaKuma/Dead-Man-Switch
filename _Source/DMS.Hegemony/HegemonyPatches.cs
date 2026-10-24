using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using FactionTerritories2;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS.Hegemony
{
    /// <summary>
    /// Institutions.MechKind 與 MechRosters.Plan 都是 Hegemony 的 internal 方法,沒有公開 API 保證。
    /// 每個目標都先以完整簽章查找;找不到就只停用該補丁並寫一行警告,不影響 DMS 其他功能。
    /// Both targets are Hegemony internals with no public API guarantee. Each is resolved by its full
    /// signature first; a missing target disables only that patch with one warning.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HegemonyPatches
    {
        private const string HarmonyId = "AOBA.DMS.Hegemony";

        static HegemonyPatches()
        {
            Harmony harmony = new Harmony(HarmonyId);
            Assembly hegemony = typeof(SettlementState).Assembly;
            bool kind = TryPatch(harmony, hegemony, "FactionTerritories2.Institutions", "MechKind",
                typeof(PawnKindDef), nameof(MechKindPostfix));
            bool plan = TryPatch(harmony, hegemony, "FactionTerritories2.MechRosters", "Plan",
                typeof(List<MechRosterEntry>), nameof(PlanPostfix));
            if (kind || plan)
            {
                ReportRosters();
            }
            // 反叛判定與附庸頁面都讀這個方法,改這裡兩邊顯示一致。Both the rebellion roll and the vassalage tab read it.
            TryPatch(harmony, hegemony, "FactionTerritories2.SettlementCulture", "Chance",
                typeof(float), nameof(RebellionChancePostfix));
            // 與 DMS.dll 一起發佈;DMS.dll 較舊、沒有 FleetSettlementBridge 時只停用這部分。
            // 缺型別的例外在 JIT 編譯 RegisterFleetBridge 時從呼叫端丟出,所以 try 必須包在這裡。
            // Ships with DMS.dll; an older DMS.dll without FleetSettlementBridge disables only this part. The missing-type
            // exception is thrown at the call site when RegisterFleetBridge is JIT-compiled, so the try has to wrap the call.
            try
            {
                RegisterFleetBridge();
            }
            catch (Exception e) when (e is TypeLoadException || e is MissingMemberException)
            {
                Log.Warning("[DMS] DMS.dll has no FleetSettlementBridge; Hegemony supply, aid and court-martial links are disabled: " + e.Message);
            }
        }

        // 不可內聯,否則缺型別會讓整個靜態建構式 JIT 失敗。Must not inline, or a missing type fails the whole static ctor.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterFleetBridge()
        {
            FleetSettlementBridge.Current = new HegemonyFleetBridge();
        }

        private static bool TryPatch(Harmony harmony, Assembly assembly, string typeName, string methodName,
            Type returnType, string postfix)
        {
            Type type = assembly.GetType(typeName);
            MethodInfo target = type == null ? null
                : AccessTools.Method(type, methodName, new[] { typeof(SettlementState) });
            if (target == null || !target.IsStatic || target.ReturnType != returnType)
            {
                Log.Warning("[DMS] Hegemony " + typeName + "." + methodName + "(SettlementState) was not found; "
                    + "DMS mech rosters for that path are disabled. This Hegemony version is untested.");
                return false;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(HegemonyPatches), postfix));
            return true;
        }

        private static void MechKindPostfix(SettlementState s, ref PawnKindDef __result)
        {
            try
            {
                MechRosterExtension extension = FactionMechRosters.For(s);
                if (extension == null || (__result != null && extension.Allows(__result)))
                {
                    return;
                }
                __result = FactionMechRosters.Pick(extension, s);
                // MechKind 的退路會把原版底盤寫進 mechChassis;改成派系底盤,讓直接讀這個欄位的流程一致。
                // MechKind's fallback stores a vanilla chassis in mechChassis; keep that field on the faction chassis.
                if (__result != null)
                {
                    s.mechChassis = __result;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[DMS] Hegemony MechKind postfix failed: " + e, 0x444D5301);
            }
        }

        private static void PlanPostfix(SettlementState state, ref List<MechRosterEntry> __result)
        {
            try
            {
                MechRosterExtension extension = FactionMechRosters.For(state);
                if (extension == null)
                {
                    return;
                }
                // 聚落已有派系名單內的規劃(例如由 DMS 聚落分出的新聚落)就沿用,只濾掉名單外的底盤。
                // A town already planning on-roster chassis (e.g. founded from a DMS town) keeps its plan minus off-roster rows.
                if (__result != null && FactionMechRosters.HasOwnRoster(extension, state))
                {
                    __result.RemoveAll(e => e?.kind == null || !extension.Allows(e.kind));
                    if (__result.Count > 0)
                    {
                        return;
                    }
                }
                __result = FactionMechRosters.Build(extension, state);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[DMS] Hegemony Plan postfix failed: " + e, 0x444D5302);
            }
        }

        private static void RebellionChancePostfix(SettlementState state, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }
            try
            {
                // 與 Hegemony 計算文化異議相同,用政治擁有者(玩家附庸算玩家)的主要意識形態。
                // Like Hegemony's dissent, use the political owner's primary ideoligion (player vassals count as the player).
                Ideo ideo = HegemonyApi.GetOwner(state)?.ideos?.PrimaryIdeo;
                if (ideo == null)
                {
                    return;
                }
                float factor = 1f;
                foreach (MemeDef meme in ideo.memes)
                {
                    RebellionModifierExtension extension = meme.GetModExtension<RebellionModifierExtension>();
                    if (extension != null)
                    {
                        factor = Math.Min(factor, extension.chanceFactor);
                    }
                }
                __result *= factor;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[DMS] Hegemony rebellion chance postfix failed: " + e, 0x444D5303);
            }
        }

        // 啟動時列出各派系實際可用的底盤,方便確認 CompOverseerSubject 等條件。
        // Lists each faction's usable chassis at startup to confirm CompOverseerSubject and similar requirements.
        private static void ReportRosters()
        {
            foreach (FactionDef faction in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                MechRosterExtension extension = faction.GetModExtension<MechRosterExtension>();
                if (extension == null)
                {
                    continue;
                }
                StringBuilder usable = new StringBuilder();
                StringBuilder unusable = new StringBuilder();
                foreach (MechRosterOption option in extension.roster)
                {
                    if (option?.kind == null)
                    {
                        continue;
                    }
                    StringBuilder target = FactionMechRosters.Usable(option.kind) ? usable : unusable;
                    if (target.Length > 0)
                    {
                        target.Append(", ");
                    }
                    target.Append(option.kind.defName);
                    if (target == usable)
                    {
                        target.Append(" x").Append(option.weight).Append(" (").Append(FactionMechRosters.Technology(option.kind)).Append(')');
                    }
                }
                if (usable.Length == 0)
                {
                    Log.Warning("[DMS] Hegemony mech roster for " + faction.defName + " has no usable chassis"
                        + (ModsConfig.BiotechActive ? "" : " (Biotech is not active)") + "; its towns will build no mechs.");
                }
                else
                {
                    Log.Message("[DMS] Hegemony mech roster for " + faction.defName + ": " + usable);
                }
                if (unusable.Length > 0 && ModsConfig.BiotechActive)
                {
                    Log.Warning("[DMS] Hegemony cannot field these " + faction.defName + " chassis: " + unusable);
                }
            }
        }
    }
}
