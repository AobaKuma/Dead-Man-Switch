using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DMS
{
	public static class SteleQuestUtility
	{
		public const int CheckIntervalTicks = 250;

		/// <summary>
		/// 物品是否已在玩家手上：位於玩家據點地圖（地上、被搬運、在倉庫或微縮箱裡都算），或在玩家商隊裡。
		/// 運輸艙飛行途中不算，落地後才算。
		/// Whether the player holds the thing: on a player home map (on the ground, carried, stored or inside a
		/// minified crate), or in a player caravan. Transport pods in flight don't count until they land.
		/// </summary>
		public static bool IsHeldByPlayer(Thing thing)
		{
			if (thing == null || thing.Destroyed)
			{
				return false;
			}
			Map map = thing.MapHeld;
			if (map != null)
			{
				return map.IsPlayerHome;
			}
			for (IThingHolder holder = thing.ParentHolder; holder != null; holder = holder.ParentHolder)
			{
				if (holder is Caravan caravan)
				{
					return caravan.IsPlayerControlled;
				}
			}
			return false;
		}

		/// <summary>子任務所屬的石碑主線產生器。The stele main-line generator a subquest belongs to.</summary>
		public static QuestPart_SubquestGenerator_Stele MainPart(Quest subquest)
		{
			return subquest?.parent?.GetFirstPartOfType<QuestPart_SubquestGenerator_Stele>();
		}

		public static bool IsCheckTick(Quest quest)
		{
			return (Find.TickManager.TicksGame + (quest?.id ?? 0)) % CheckIntervalTicks == 0;
		}

		/// <summary>
		/// 版面沒有放出指定物件時的保底：在 near（預設地圖中央）附近找空地生成一個。
		/// Fallback when the layout failed to place the thing: spawn one on clear ground near <paramref name="near"/>
		/// (the map centre by default).
		/// </summary>
		public static Thing SpawnFallback(ThingDef def, Map map, Faction faction = null, IntVec3? near = null)
		{
			IntVec3 root = near ?? map.Center;
			Thing thing = ThingMaker.MakeThing(def);
			if (faction != null)
			{
				thing.SetFaction(faction);
			}
			IntVec2 size = def.Size;
			bool Fits(IntVec3 c)
			{
				foreach (IntVec3 cell in GenAdj.OccupiedRect(c, Rot4.North, size))
				{
					if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null)
					{
						return false;
					}
				}
				return true;
			}
			if (!CellFinder.TryFindRandomCellNear(root, map, 30, Fits, out IntVec3 spot))
			{
				spot = CellFinder.RandomClosewalkCellNear(root, map, 30);
			}
			return GenSpawn.Spawn(thing, spot, map, Rot4.North, WipeMode.VanishOrMoveAside);
		}
	}

	/// <summary>
	/// 撞擊地點子任務：密鑰封在站點的密鑰伺服器裡（DMS_OccultechPedestal，見 GenStructures/SteleQuests/），
	/// 駭入才會吐出來。地圖生成時替伺服器掛上任務標籤，從 <see cref="CompServerHackable"/> 的獎勵訊號接住密鑰；
	/// 密鑰被玩家帶走即成功。玩家離開時密鑰還留在站點上（隨地圖一起消失），或地圖還在時伺服器／密鑰已被摧毀，
	/// 即失敗，主線會在之後補發新的訊號。版面直接放密鑰（舊版面）也照樣追蹤。
	///
	/// Impact site subquest: the glyph is sealed in the site's glyph server (DMS_OccultechPedestal, see
	/// GenStructures/SteleQuests/) and only comes out when it is hacked. On map generation the server gets a quest tag
	/// and the glyph is caught from <see cref="CompServerHackable"/>'s reward signal; taking the glyph succeeds.
	/// Leaving it behind (it vanishes with the map), or the server or glyph being destroyed while the map still exists,
	/// fails, and the main line will issue another signal later. A glyph placed directly by the layout (the old
	/// layout) is still tracked.
	/// </summary>
	public class QuestPart_SteleKeySite : QuestPartActivable
	{
		public string inSignalMapGenerated;

		public string inSignalMapRemoved;

		public MapParent site;

		public string signalSteleSubquestCompleted;

		private List<Thing> keys = new List<Thing>();

		/// <summary>還沒吐出密鑰的密鑰伺服器。Glyph servers that have not released their glyph yet.</summary>
		private List<Thing> servers = new List<Thing>();

		/// <summary>地圖生成後已記下密鑰／伺服器；在此之前不判定遺失。Set once the map's glyphs/servers are recorded; nothing counts as lost before.</summary>
		private bool recorded;

		private bool resolved;

		/// <summary>
		/// 伺服器的任務標籤；由任務 ID 推得，不必存檔，舊存檔裡尚未生成地圖的子任務也適用。
		/// The servers' quest tag; derived from the quest ID so nothing is saved and subquests from old saves work too.
		/// </summary>
		private string ServerTag => "Quest" + quest.id + ".glyphServer";

		protected override void ProcessQuestSignal(Signal signal)
		{
			base.ProcessQuestSignal(signal);
			if (resolved)
			{
				return;
			}
			if (signal.tag == inSignalMapGenerated)
			{
				RecordKeys();
			}
			else if (signal.tag == ServerTag + "." + CompServerHackable.HackRewardSignal)
			{
				signal.args.TryGetArg("SUBJECT", out Thing server);
				signal.args.TryGetArg("REWARD", out Thing reward);
				if (reward?.def == DMS_DefOf.DMS_OccultechKey)
				{
					servers.Remove(server);
					if (!keys.Contains(reward))
					{
						keys.Add(reward);
					}
				}
			}
			else if (signal.tag == inSignalMapRemoved)
			{
				// 地圖移除時留在上面的物品會被標成 Destroyed，還活著的就是被帶走了。
				// Things left on a removed map read as Destroyed; any glyph still alive was carried off.
				if (keys.Any(k => k != null && !k.Destroyed))
				{
					Succeed();
				}
				else
				{
					Fail(destroyed: false);
				}
			}
		}

		private void RecordKeys()
		{
			Map map = site?.Map;
			if (map == null)
			{
				return;
			}
			keys.Clear();
			keys.AddRange(map.listerThings.ThingsOfDef(DMS_DefOf.DMS_OccultechKey));
			servers.Clear();
			servers.AddRange(map.listerThings.ThingsOfDef(DMS_DefOf.DMS_OccultechPedestal));
			foreach (Thing server in servers)
			{
				QuestUtility.AddQuestTag(server, ServerTag);
			}
			if (keys.Count == 0 && servers.Count == 0)
			{
				Log.Warning("[DMS] Impact site generated without an occultech glyph or glyph server; spawning a glyph near the map centre.");
				keys.Add(SteleQuestUtility.SpawnFallback(DMS_DefOf.DMS_OccultechKey, map));
			}
			recorded = true;
		}

		public override void QuestPartTick()
		{
			base.QuestPartTick();
			if (resolved || !recorded || !SteleQuestUtility.IsCheckTick(quest))
			{
				return;
			}
			if (keys.Any(SteleQuestUtility.IsHeldByPlayer))
			{
				Succeed();
				return;
			}
			// 地圖移除另由訊號處理（那時地圖上的東西都會變成 Destroyed）。Map removal is the signal's job (everything on it reads as Destroyed then).
			Map map = site?.Map;
			if (map == null)
			{
				return;
			}
			for (int i = servers.Count - 1; i >= 0; i--)
			{
				Thing server = servers[i];
				if (server != null && !server.Destroyed && server.TryGetComp<CompServerHackable>() is CompServerHackable comp && comp.FullyHacked)
				{
					// 駭完卻沒接到密鑰（附近放不下）：在伺服器旁補一枚。Hacked but no glyph came through (nowhere to put it): drop one beside the server.
					Log.Warning("[DMS] Glyph server was hacked without releasing a glyph; spawning one beside it.");
					servers.RemoveAt(i);
					keys.Add(SteleQuestUtility.SpawnFallback(DMS_DefOf.DMS_OccultechKey, map, near: server.Position));
				}
			}
			if (!keys.Any(k => k != null && !k.Destroyed) && !servers.Any(s => s != null && !s.Destroyed))
			{
				Fail(destroyed: true);
			}
		}

		private void Succeed()
		{
			resolved = true;
			NotifyParent();
			SteleQuestUtility.MainPart(quest)?.Notify_GlyphRecovered();
			Complete();
			quest.End(QuestEndOutcome.Success, sendLetter: false);
		}

		private void Fail(bool destroyed)
		{
			resolved = true;
			NotifyParent();
			Find.LetterStack.ReceiveLetter("DMS_Stele_GlyphLostLabel".Translate(),
				(destroyed ? "DMS_Stele_GlyphDestroyedText" : "DMS_Stele_GlyphLostText").Translate(),
				LetterDefOf.NegativeEvent, null, null, quest);
			Complete();
			quest.End(QuestEndOutcome.Fail, sendLetter: false);
		}

		private void NotifyParent()
		{
			if (quest.parent != null && !signalSteleSubquestCompleted.NullOrEmpty())
			{
				quest.parent.Notify_SignalReceived(new Signal(signalSteleSubquestCompleted, global: true));
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref inSignalMapGenerated, "inSignalMapGenerated");
			Scribe_Values.Look(ref inSignalMapRemoved, "inSignalMapRemoved");
			Scribe_References.Look(ref site, "site");
			Scribe_Values.Look(ref signalSteleSubquestCompleted, "signalSteleSubquestCompleted");
			Scribe_Values.Look(ref resolved, "resolved", defaultValue: false);
			Scribe_Values.Look(ref recorded, "recorded", defaultValue: false);
			Scribe_Collections.Look(ref keys, "keys", saveDestroyedThings: false, lookMode: LookMode.Reference);
			Scribe_Collections.Look(ref servers, "servers", saveDestroyedThings: false, lookMode: LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				keys ??= new List<Thing>();
				keys.RemoveAll(k => k == null);
				servers ??= new List<Thing>();
				servers.RemoveAll(s => s == null);
				// 舊存檔沒有 recorded：已記下密鑰就視為已記錄。Old saves lack recorded: tracked glyphs mean the map was recorded.
				recorded |= keys.Count > 0;
			}
		}

		public override void AssignDebugData()
		{
			base.AssignDebugData();
			inSignalMapGenerated = "DebugSignal" + Rand.Int;
			inSignalMapRemoved = "DebugSignal" + Rand.Int;
		}
	}

	/// <summary>
	/// 舊版撞擊地點子任務的結束件：地圖移除即成功。新生成的子任務改用 <see cref="QuestPart_SteleKeySite"/>，
	/// 這個類別只為了讓舊存檔裡進行中的子任務還能讀取並結束。
	/// Old impact-site end part: succeeds when the map is removed. New subquests use
	/// <see cref="QuestPart_SteleKeySite"/>; this class stays so ongoing subquests in old saves still load and end.
	/// </summary>
	public class QuestPart_SteleSubQuestEnd : QuestPart
	{
		public string inSignal;

		public string signalSteleSubquestCompleted;

		public bool sendLetter;

		public bool playSound;

		public override void Notify_QuestSignalReceived(Signal signal)
		{
			base.Notify_QuestSignalReceived(signal);
			if (signal.tag == inSignal)
			{
				if (quest.parent != null)
				{
					quest.parent.Notify_SignalReceived(new Signal(signalSteleSubquestCompleted, true));
				}
				quest.End(QuestEndOutcome.Success, sendLetter, playSound);
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref inSignal, "inSignal");
			Scribe_Values.Look(ref signalSteleSubquestCompleted, "signalSteleSubquestCompleted");
			Scribe_Values.Look(ref sendLetter, "sendLetter", defaultValue: false);
			Scribe_Values.Look(ref playSound, "playSound", defaultValue: false);
		}

		public override void AssignDebugData()
		{
			base.AssignDebugData();
			inSignal = "DebugSignal" + Rand.Int;
		}
	}
}
