using Verse;
using RimWorld;
using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;

namespace DMS
{
    /// <summary>
    /// Comp用于在武器预热时临时替换装备渲染为瞄准贴图
    /// 适用于手持装备类型的武器（如DMS_DeposableRocketLauncher）
    /// 附加到装备物品上，监听穿着者的Verb预热
    /// </summary>
    public class CompAimingWeapon : ThingComp
    {
        private Graphic originalGraphic;
        private Graphic aimingGraphic;
        private Verb currentWarmingVerb;
        private bool isCurrentlyAimingValue;

        public bool isCurrentlyAiming => isCurrentlyAimingValue;
        public CompProperties_AimingWeapon Props => props as CompProperties_AimingWeapon;

        /// <summary>
        /// 获取穿着这个装备的Pawn
        /// </summary>
        private Pawn GetWearerPawn()
        {
            if (parent.ParentHolder is Pawn_EquipmentTracker equipment)
            {
                return equipment.pawn;
            }
            if (parent is Apparel apparel && apparel.Wearer != null)
            {
                return apparel.Wearer;
            }
            return null;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 保存原始Graphic
            originalGraphic = parent.Graphic;
            
            // 预加载瞄准贴图
            if (!Props?.texturePath.NullOrEmpty() ?? false)
            {
                aimingGraphic = GraphicDatabase.Get<Graphic_Single>(
                    Props.texturePath,
                    ShaderDatabase.Cutout,
                    Vector2.one,
                    Color.white
                );
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            isCurrentlyAimingValue = false;
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            isCurrentlyAimingValue = false;
            currentWarmingVerb = null;
        }

        public override void CompTick()
        {
            base.CompTick();
            
            Pawn wearer = GetWearerPawn();
            if (wearer == null || !wearer.IsHashIntervalTick(1))
                return;

            Verb verb = wearer.CurrentEffectiveVerb;
            
            // 检查是否是我们感兴趣的Verb且处于预热状态
            if (verb != null && 
                verb.verbProps.warmupTime > 0 && 
                wearer.jobs?.curDriver != null &&
                wearer.jobs.curDriver.ticksLeftThisToil >= 1 &&
                verb.caster == wearer &&
                verb.EquipmentSource == parent)
            {
                if (currentWarmingVerb != verb)
                {
                    currentWarmingVerb = verb;
                    isCurrentlyAimingValue = true;
                }
            }
            else if (isCurrentlyAiming)
            {
                isCurrentlyAimingValue = false;
                currentWarmingVerb = null;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            isCurrentlyAimingValue = false;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            isCurrentlyAimingValue = false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref currentWarmingVerb, "currentWarmingVerb");
            Scribe_Values.Look(ref isCurrentlyAimingValue, "isCurrentlyAiming", false);
        }

        /// <summary>
        /// 获取当前应该渲染的Graphic（预热时返回瞄准贴图）
        /// </summary>
        public Graphic GetCurrentGraphic()
        {
            if (isCurrentlyAiming && aimingGraphic != null)
                return aimingGraphic;
            return originalGraphic ?? parent.Graphic;
        }
    }

    /// <summary>
    /// CompAimingWeapon的属性配置类
    /// </summary>
    public class CompProperties_AimingWeapon : CompProperties
    {
        /// <summary>
        /// 预热时显示的瞄准贴图路径（例如：Things/Weapon/RocketLauncher/DMS_Launcher_Aiming）
        /// </summary>
        public string texturePath;

        public CompProperties_AimingWeapon()
        {
            compClass = typeof(CompAimingWeapon);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (texturePath.NullOrEmpty())
            {
                yield return $"{parentDef.defName} has CompProperties_AimingWeapon with empty texturePath";
            }
        }
    }

    /// <summary>
    /// PawnRenderNode_Apparel的Harmony Patch - 用于在预热时替换装备贴图显示
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNode_Apparel), "GraphicsFor")]
    public static class Patch_PawnRenderNode_Apparel_GraphicsFor
    {
        /// <summary>
        /// Postfix用于替换返回的Graphic为瞄准贴图（如果处于预热状态）
        /// </summary>
        [HarmonyPostfix]
        public static void GraphicsForPostfix(ref IEnumerable<Graphic> __result, PawnRenderNode_Apparel __instance)
        {
            try
            {
                // 获取装备对象
                Apparel apparel = __instance.apparel;
                if (apparel == null)
                    return;

                // 检查装备是否有 CompAimingWeapon 组件
                CompAimingWeapon comp = apparel.TryGetComp<CompAimingWeapon>();
                if (comp == null || !comp.isCurrentlyAiming)
                    return;

                // 获取瞄准贴图
                Graphic aimingGraphic = comp.GetCurrentGraphic();
                if (aimingGraphic == null)
                    return;

                // 替换为瞄准贴图
                __result = new List<Graphic> { aimingGraphic };
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error in Patch_PawnRenderNode_Apparel_GraphicsFor: {ex}");
            }
        }
    }
}
