using System;
using RimWorld;
using Verse;

namespace DMS
{
	/// <summary>
	/// 原版 PlaceWorker_ShowTurretRadius 只認 Verb_Shoot / Verb_Spray，
	/// 砲塔槍身若用 Fortified.Verb_LaunchProjectile_AmmoSwitch 之類的其他射擊 Verb 會直接 NRE、無法放置。
	/// 這個版本改找第一個有射程的 Verb，找不到就只是不畫射程圈。
	/// </summary>
	public class PlaceWorker_ShowTurretRadiusAny : PlaceWorker
	{
		public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
		{
			ThingDef gunDef = (checkingDef as ThingDef)?.building?.turretGunDef;
			if (gunDef?.Verbs == null)
			{
				return true;
			}
			VerbProperties verb = gunDef.Verbs.Find(v => v.verbClass != null && typeof(Verb_LaunchProjectile).IsAssignableFrom(v.verbClass))
				?? gunDef.Verbs.Find(v => v.range > 0f);
			if (verb == null)
			{
				return true;
			}
			if (verb.range > 0f)
			{
				GenDraw.DrawRadiusRing(loc, verb.range);
			}
			if (verb.minRange > 0f)
			{
				GenDraw.DrawRadiusRing(loc, verb.minRange);
			}
			return true;
		}
	}
}
