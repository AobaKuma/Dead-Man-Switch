using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace DMS
{
	public class OccultechKey : ThingWithComps
	{
		public QuestPart_SubquestGenerator_Stele questPart;

		public override void PostPostMake()
		{
			base.PostPostMake();
			List<Quest> list = Find.QuestManager?.QuestsListForReading;
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

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref questPart, "questPart");
		}
	}
}
