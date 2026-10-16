using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;

namespace DMS
{
	/// <summary>
	/// 石碑任務線的站點子任務共用部分：在殖民地附近找地塊、以先鋒殘餘生成站點。
	/// Shared part of the stele line's site subquests: find a tile near the colony and spawn a Legacy site.
	/// </summary>
	public abstract class QuestNode_Root_SteleSite : QuestNode
	{
		protected List<BiomeDef> allowedBiomes = new List<BiomeDef>();

		protected Hilliness minHilliness = Hilliness.Flat;

		protected Hilliness maxHilliness = Hilliness.SmallHills;

		protected IntRange distanceFromColonyRange = new IntRange(20, 40);

		protected List<LandmarkDef> allowedLandmarks;

		protected PlanetLayerDef layer;

		public List<SitePartDef> sitePartDefs = new List<SitePartDef>();

		public FactionDef siteFaction;

		/// <summary>站點威脅點數 = 任務點數 × pointsFactor。Site threat points = quest points × pointsFactor.</summary>
		public float pointsFactor = 1f;

		protected Faction SiteFaction => siteFaction == null ? null : Find.FactionManager.FirstFactionOfDef(siteFaction);

		/// <summary>生成站點並放上世界地圖；slate 的 "site" 指向它。Generates the site, puts it on the world map and sets slate "site".</summary>
		protected Site GenerateSite()
		{
			Slate slate = QuestGen.slate;
			Quest quest = QuestGen.quest;
			if (!TryFindSiteTile(out PlanetTile tile))
			{
				Log.Error("[DMS] Could not find a valid tile for stele site quest " + QuestGen.Root?.defName);
				return null;
			}
			Faction faction = SiteFaction;
			slate.Set("faction", faction);
			float points = slate.Get("points", 0f) * pointsFactor;
			Site site = QuestGen_Sites.GenerateSite(new SitePartDefWithParams[1]
			{
				new SitePartDefWithParams(sitePartDefs.RandomElement(), new SitePartParams
				{
					points = points,
					threatPoints = points
				})
			}, tile, faction, hiddenSitePartsPossible: false, null, WorldObjectDefOf.Site);
			slate.Set("site", site);
			quest.SpawnWorldObject(site);
			return site;
		}

		protected virtual bool TryFindSiteTile(out PlanetTile tile)
		{
			PlanetLayer planetLayer = null;
			if (!TileFinder.TryFindRandomPlayerTile(out var tile2, allowCaravans: false, null, canBeSpace: true))
			{
				if (!TileFinder.TryFindRandomPlayerTile(out tile2, allowCaravans: true, null, canBeSpace: true))//Some players may use caravan only play
				{
					Log.Error("Failed to find a valid root tile for stele site.");
					tile = PlanetTile.Invalid;
					return false;
				}
			}
			if (layer != null && !Find.WorldGrid.TryGetFirstAdjacentLayerOfDef(tile2, layer, out planetLayer))
			{
				tile = PlanetTile.Invalid;
				return false;
			}
			if (planetLayer == null)
			{
				planetLayer = tile2.Layer;
			}
			int trueMin = distanceFromColonyRange.TrueMin;
			int trueMax = distanceFromColonyRange.TrueMax;
			FastTileFinder.TileQueryParams query = new FastTileFinder.TileQueryParams(tile2, trueMin, trueMax, FastTileFinder.LandmarkMode.Forbidden, reachable: true, minHilliness, maxHilliness);
			FastTileFinder.TileQueryParams desperate = new FastTileFinder.TileQueryParams(tile2, 1f, trueMax * 2, FastTileFinder.LandmarkMode.Any, reachable: true, minHilliness, maxHilliness, checkBiome: false);
			List<PlanetTile> list = planetLayer.FastTileFinder.Query(query, allowedBiomes, allowedLandmarks, desperate);
			if (!list.Empty())
			{
				tile = list.RandomElement();
				return true;
			}
			query = new FastTileFinder.TileQueryParams(tile2, trueMin, trueMax, FastTileFinder.LandmarkMode.Forbidden, reachable: true, minHilliness, maxHilliness);
			desperate = new FastTileFinder.TileQueryParams(tile2, 1f, float.MaxValue, FastTileFinder.LandmarkMode.Any, reachable: true, minHilliness, maxHilliness, checkBiome: false);
			list = planetLayer.FastTileFinder.Query(query, allowedBiomes, allowedLandmarks, desperate);
			if (!list.Empty())
			{
				tile = list.RandomElement();
				return true;
			}
			tile = PlanetTile.Invalid;
			return false;
		}

		protected override bool TestRunInt(Slate slate)
		{
			if (sitePartDefs.NullOrEmpty())
			{
				return false;
			}
			if (siteFaction != null && SiteFaction == null)
			{
				return false;
			}
			return TryFindSiteTile(out _);
		}
	}

	/// <summary>
	/// 撞擊地點：先鋒殘餘守著一處墜落的密鑰艙，密鑰在站點中央的掩體裡。
	/// Impact site: Legacy remnants hold a crashed glyph capsule; the glyph sits in the shelter at the centre.
	/// </summary>
	public class QuestNode_Root_OccultechKey_Site : QuestNode_Root_SteleSite
	{
		protected override void RunInt()
		{
			Slate slate = QuestGen.slate;
			Quest quest = QuestGen.quest;
			Site site = GenerateSite();
			if (site == null)
			{
				return;
			}
			string mapGenerated = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
			string mapRemoved = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");

			QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
			choice.rewards.Add(new Reward_DefinedThingDef(DMS_DefOf.DMS_OccultechKey));
			quest.RewardChoice().choices.Add(choice);

			quest.AddPart(new QuestPart_SteleKeySite
			{
				inSignalEnable = slate.Get<string>("inSignal"),
				inSignalMapGenerated = mapGenerated,
				inSignalMapRemoved = mapRemoved,
				site = site,
				signalSteleSubquestCompleted = slate.Get<string>("signalSteleSubquestCompleted")
			});
			quest.Letter(LetterDefOf.NeutralEvent, mapGenerated, text: "[letterTextMapGenerated]", label: "[letterLabelMapGenerated]");
		}
	}
}
