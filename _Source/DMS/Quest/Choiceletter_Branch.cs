using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DMS
{
    class Choiceletter_Branch : ChoiceLetter
    {
        public string signalAccept;

        public string signalReject;

        public Slate slate;

        public Map map;

        public List<Pawn> pawns;
        public override bool CanDismissWithRightClick => false;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                pawns = map.mapPawns.AllHumanlikeSpawned.FindAll(o=>o.Faction==Faction.OfPlayer&&!o.DeadOrDowned);

                Log.Warning(pawns.Count.ToString());
                foreach (Pawn pawn in pawns)
                {
                    DiaOption diaOptionAccept = new DiaOption(pawn.Name.ToString() + "承接".Translate());
                    diaOptionAccept.resolveTree = true;
                    diaOptionAccept.action = () =>
                    {
                        slate.Set("Accepter", pawn);
                        Find.SignalManager.SendSignal(new Signal(signalAccept));
                        Find.LetterStack.RemoveLetter(this);
                    };
                    yield return diaOptionAccept;
                }

                DiaOption diaOptionRefuse = new DiaOption("拒绝".Translate());
                diaOptionRefuse.resolveTree = true;
                diaOptionRefuse.action = () =>
                {
                    Find.SignalManager.SendSignal(new Signal(signalReject));
                    Find.LetterStack.RemoveLetter(this);
                };
                yield return diaOptionRefuse;

            }
        }
    }
}
