using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;
using static Mono.Security.X509.X520;

namespace DMS
{
    public class QuestPart_ToSendSignal : QuestPart
    {
        public string inSignal;

        public string outSignal;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (signal.tag == inSignal)
            {
                Find.SignalManager.SendSignal(new Signal(outSignal));
            }
        }
    }
}