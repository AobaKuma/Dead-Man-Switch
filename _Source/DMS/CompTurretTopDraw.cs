using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace DMS
{
	public class CompProperties_TurretTopDraw : CompProperties
	{
		public GraphicData graphic;

		public float maxAngle = 360f;

		public CompProperties_TurretTopDraw()
		{
			compClass = typeof(CompTurretTopDraw);
		}
	}
	public class CompTurretTopDraw : ThingComp
	{
		private CompProperties_TurretTopDraw Props => (CompProperties_TurretTopDraw)props;

		public float angle;

		public override void PostPostMake()
		{
			base.PostPostMake();
			angle = Rand.Range(0, Props.maxAngle);
		}

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			Vector3 pos = drawLoc + Altitudes.AltIncVect;
			Props.graphic.Graphic.Draw(pos, parent.Rotation, parent, angle);
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref angle, "turretTopAngle");
		}
	}
}
