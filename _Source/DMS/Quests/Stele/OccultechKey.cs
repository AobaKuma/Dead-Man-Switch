using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace DMS
{
	/// <summary>
	/// 封存技術密鑰。生成時向進行中的石碑主線登記；帶回據點才算「已取得」，
	/// 集齊後由主線解讀並用掉（見 <see cref="QuestPart_SubquestGenerator_Stele"/>）。
	/// Occultech glyph. Registers with the ongoing stele line when made; it only counts once brought home, and the
	/// line reads and spends a full set (see <see cref="QuestPart_SubquestGenerator_Stele"/>).
	/// </summary>
	public class OccultechKey : ThingWithComps
	{
		public QuestPart_SubquestGenerator_Stele questPart;

		public override void PostPostMake()
		{
			base.PostPostMake();
			List<Quest> list = Find.QuestManager?.QuestsListForReading;
			if (list == null)
			{
				return;
			}
			foreach (Quest quest in list)
			{
				if(quest?.root == DMS_DefOf.DMS_Stele && quest.State == QuestState.Ongoing)
				{
					QuestPart_SubquestGenerator_Stele stelePart = quest.GetFirstPartOfType<QuestPart_SubquestGenerator_Stele>();
					if(stelePart != null && stelePart.TryAddKey(this))
					{
						questPart = stelePart;
						break;
					}
				}
			}
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			base.Destroy(mode);
			questPart?.RemoveKey(this);
		}

		public override string GetInspectString()
		{
			StringBuilder sb = new StringBuilder(base.GetInspectString());
			if (questPart != null && questPart.CurrentStage == QuestPart_SubquestGenerator_Stele.Stage.CollectingGlyphs
				&& questPart.quest != null && questPart.quest.State == QuestState.Ongoing)
			{
				if (sb.Length > 0)
				{
					sb.AppendLine();
				}
				sb.Append("DMS_Stele_GlyphProgress".Translate(Mathf.Min(questPart.CollectedKeysCount, questPart.KeysCount), questPart.KeysCount));
			}
			return sb.ToString();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref questPart, "questPart");
		}
	}
}
