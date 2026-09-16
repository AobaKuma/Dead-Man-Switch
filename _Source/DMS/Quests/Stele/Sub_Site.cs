using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DMS
{
	public class QuestNode_Root_OccultechKey_Site : QuestNode
	{
		protected List<BiomeDef> allowedBiomes = new List<BiomeDef>();

		private Hilliness minHilliness = Hilliness.Flat;

		private Hilliness maxHilliness = Hilliness.SmallHills;

		protected IntRange distanceFromColonyRange = new IntRange(20, 40);

		protected List<LandmarkDef> allowedLandmarks;

		protected PlanetLayerDef layer;

		public List<SitePartDef> sitePartDefs = new List<SitePartDef>();

		public FactionDef siteFaction;

		protected override void RunInt()
		{
			Slate slate = QuestGen.slate;
			Quest quest = QuestGen.quest;
			if (!TryFindSiteTile(out var tile))
			{
				Log.Error("Could not find valid site tile for insect lair quest.");
				return;
			}
			Faction faction = Find.FactionManager.FirstFactionOfDef(siteFaction);
			slate.Set("faction", faction);
			string inSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");
			Site site = QuestGen_Sites.GenerateSite(new SitePartDefWithParams[1]
			{
				new SitePartDefWithParams(sitePartDefs.RandomElement(), new SitePartParams
				{
					points = slate.Get("points", 0f),
					threatPoints = slate.Get("points", 0f)
				})
			}, tile, faction, hiddenSitePartsPossible: false, null, WorldObjectDefOf.Site);
			slate.Set("site", site);
			quest.SpawnWorldObject(site);
			QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
			choice.rewards.Add(new Reward_DefinedThingDef(DMS_DefOf.DMS_OccultechKey));
			quest.RewardChoice().choices.Add(choice);
			quest.AddPart(new QuestPart_SteleSubQuestEnd()
			{
				inSignal = inSignal,
				signalSteleSubquestCompleted = slate.Get<string>("signalSteleSubquestCompleted"),
				sendLetter = true,
				playSound = true
			});
		}

		protected virtual bool TryFindSiteTile(out PlanetTile tile)
		{
			PlanetLayer planetLayer = null;
			if (!TileFinder.TryFindRandomPlayerTile(out var tile2, allowCaravans: false, null, canBeSpace: true))
			{
				if (!TileFinder.TryFindRandomPlayerTile(out tile2, allowCaravans: true, null, canBeSpace: true))//Some players may use caravan only play
				{
					Log.Error("Failed to find a valid root tile for occultech key site.");
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
			if (!TryFindSiteTile(out var _))
			{
				return false;
			}
			return true;
		}
	}
}
