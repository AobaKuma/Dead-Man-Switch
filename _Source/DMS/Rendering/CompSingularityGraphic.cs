using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 讓 <see cref="CompSingularityGraphic"/> 知道建築現在是否在運作。建築上任何實作此介面的 comp 回傳 false，奇點就收合。
    /// Tells <see cref="CompSingularityGraphic"/> whether the building is running. If any comp on the building that
    /// implements this returns false, the singularity collapses.
    /// </summary>
    public interface ISingularityDriver
    {
        bool SingularityActive { get; }
    }

    /// <summary>
    /// 在建築上畫一個穩定存在的微型黑洞：重力透鏡扭曲背後的畫面、旋轉的吸積盤與光子環（shaderType），
    /// 並隨機放出打向 arcTargets 或任意方向的微型電弧（arcShaderType）。運作時張開、停止時收合。
    /// 有 glitchShaderType 時另有彩蛋：鏡頭拉近盯著它太久，畫面偶爾出現噪點與方格故障。
    /// 「運作」= 有電（沒有 CompPowerTrader 時視為有電），且建築上所有 <see cref="ISingularityDriver"/> 都回傳 true。
    /// 只影響繪製，不存檔。需要 drawerType MapMeshAndRealTime。
    /// Draws a stable micro black hole on the building: gravitational lensing of the scene behind it, a swirling
    /// accretion disk and a photon ring (shaderType), plus random micro arcs that strike arcTargets or a random
    /// direction (arcShaderType). With glitchShaderType there is also an easter egg: stare at it up close for too
    /// long and the view now and then breaks into noise and block glitches. It opens while the building runs and
    /// collapses when it stops. Running means
    /// powered (a building without a CompPowerTrader counts as powered) and every <see cref="ISingularityDriver"/>
    /// on it returning true. Drawing only, nothing is saved. Needs drawerType MapMeshAndRealTime.
    /// </summary>
    public class CompProperties_SingularityGraphic : CompProperties
    {
        public ShaderTypeDef shaderType;

        public ShaderTypeDef arcShaderType;

        /// <summary>奇點中心相對建築中心的位置。Singularity centre relative to the building centre.</summary>
        public Vector3 offset = Vector3.zero;

        /// <summary>效果方塊的邊長；透鏡扭曲在方塊邊緣淡出。Side of the effect quad; the lensing fades out at its rim.</summary>
        public float size = 1.3f;

        /// <summary>事件視界半徑，以方塊半邊長的比例表示。Event-horizon radius as a fraction of the quad's half-size.</summary>
        public float horizon = 0.17f;

        /// <summary>透鏡扭曲強度倍率。Lensing strength multiplier.</summary>
        public float lensing = 1f;

        /// <summary>吸積盤與光暈的色調。Tint of the accretion disk and halo.</summary>
        public Color color = new Color(0.45f, 0.8f, 1f);

        public float openSeconds = 1.5f;

        public float closeSeconds = 0.8f;

        public Color arcColor = new Color(0.5f, 0.85f, 1f);

        public Color arcCoreColor = new Color(0.9f, 0.97f, 1f);

        /// <summary>兩次放電之間的間隔（秒）。Seconds between discharges.</summary>
        public FloatRange arcIntervalSeconds = new FloatRange(0.25f, 1.2f);

        /// <summary>單條電弧持續多久（秒）。How long one arc lasts, in seconds.</summary>
        public FloatRange arcLifeSeconds = new FloatRange(0.08f, 0.22f);

        /// <summary>不打向目標的電弧長度（格）。Length of arcs that strike no target, in cells.</summary>
        public FloatRange freeArcLength = new FloatRange(0.15f, 0.4f);

        /// <summary>電弧可以打中的點，相對奇點中心（y 忽略）。Points arcs may strike, relative to the singularity (y ignored).</summary>
        public List<Vector3> arcTargets;

        /// <summary>有 arcTargets 時，電弧打向其中一點的機率。Chance an arc strikes one of arcTargets when there are any.</summary>
        public float arcTargetChance = 0.55f;

        /// <summary>
        /// 彩蛋：鏡頭拉近、盯著完全張開的奇點一段時間後，畫面偶爾出現噪點與方格故障。留空就沒有這個彩蛋。
        /// Easter egg: after the camera stays zoomed in on the fully open singularity for a while, the view now and then
        /// breaks into noise and block glitches. Leave empty for no easter egg.
        /// </summary>
        public ShaderTypeDef glitchShaderType;

        /// <summary>要連續盯多久（真實秒數）才可能發作。Real seconds of continuous staring before an episode can happen.</summary>
        public float glitchObserveSeconds = 6f;

        /// <summary>盯夠久之後，每秒發作的機率。Chance per second of an episode once stared at long enough.</summary>
        public float glitchChancePerSecond = 0.15f;

        public FloatRange glitchDurationSeconds = new FloatRange(0.4f, 1.2f);

        /// <summary>發作結束後多久內不會再發作（所有奇點共用）。Quiet time after an episode, shared by every singularity.</summary>
        public FloatRange glitchCooldownSeconds = new FloatRange(300f, 300f);

        public CompProperties_SingularityGraphic()
        {
            compClass = typeof(CompSingularityGraphic);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (shaderType == null || arcShaderType == null)
                yield return $"{parentDef.defName}: CompProperties_SingularityGraphic needs shaderType and arcShaderType";
            if (size <= 0f)
                yield return $"{parentDef.defName}: CompProperties_SingularityGraphic.size must be > 0";
            // 視界本身必須留在效果方塊裡。The horizon itself has to stay inside the effect quad.
            if (horizon <= 0f || horizon >= 1f)
                yield return $"{parentDef.defName}: CompProperties_SingularityGraphic.horizon must be between 0 and 1";
            if (parentDef.drawerType != DrawerType.MapMeshAndRealTime)
                yield return $"{parentDef.defName}: CompSingularityGraphic needs drawerType MapMeshAndRealTime";
        }
    }

    [StaticConstructorOnStartup]
    public class CompSingularityGraphic : ThingComp
    {
        private struct Arc
        {
            /// <summary>起點，相對奇點中心。Start, relative to the singularity centre.</summary>
            public Vector3 start;

            public float angle;

            public float length;

            public int bucket;

            public int variant;

            public float born;

            public float life;
        }

        /// <summary>電弧每隔多久換一條同長度級的網格，看起來像不斷重新放電。How often a live arc swaps to another mesh of its length bucket, so it crackles.</summary>
        private const float ReshapeSeconds = 0.06f;

        private const float FlickerSeconds = 0.03f;

        /// <summary>張開到這個程度才開始放電。Arcs start once the hole is at least this open.</summary>
        private const float ArcMinOpen = 0.75f;

        /// <summary>故障一段一段地閃，每段換一組方塊。Glitch episodes flicker in segments of this length, each with new blocks.</summary>
        private const float GlitchSegmentSeconds = 0.05f;

        /// <summary>同一時間只讓一個奇點發作（真實時間）。Keeps episodes from overlapping across singularities (real time).</summary>
        private static float glitchQuietUntil;

        // 純視覺的亂數不用 Verse.Rand，以免動到遊戲（與 Multiplayer 同步）的亂數序列。
        // Visual-only randomness stays off Verse.Rand so the game's (and Multiplayer's synced) sequence is untouched.
        private static readonly System.Random VisualRand = new System.Random();

        // DrawMesh 送出時就複製屬性，所以共用一個 block 沒問題。DrawMesh copies the properties on submit, so one shared block is safe.
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private static readonly int AgeSecsId = Shader.PropertyToID("_AgeSecs");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int HorizonId = Shader.PropertyToID("_Horizon");
        private static readonly int LensingId = Shader.PropertyToID("_Lensing");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int GlitchSeedId = Shader.PropertyToID("_GlitchSeed");

        private CompPowerTrader power;
        private readonly List<ISingularityDriver> drivers = new List<ISingularityDriver>();
        private readonly List<Arc> arcs = new List<Arc>();

        /// <summary>張開程度 0..1（只影響繪製，不存檔）。How far the hole is open, 0..1; drawing only, not saved.</summary>
        private float open;

        /// <summary>
        /// 視覺時鐘：以真實時間前進、暫停時停住，所以動畫不受遊戲速度影響。
        /// Visual clock: advances in real time and stops while paused, so the animation ignores game speed.
        /// </summary>
        private float clock;

        private float nextArcAt;
        private float seed;
        private int lastFrame = -1;
        private float lastRealtime = -1f;

        // 彩蛋狀態，全部以真實時間計，所以暫停中盯著看也算。Easter-egg state, all in real time, so staring at a paused game counts.
        private float observedSeconds;
        private float nextGlitchRollAt;
        private float glitchStart;
        private float glitchEnd = -1f;
        private int glitchSeed;

        /// <summary>第一次繪製時直接跳到目前狀態，讀檔時不會重播張開動畫。The first draw snaps to the current state, so loading a save does not replay the opening.</summary>
        private bool settled;

        public CompProperties_SingularityGraphic Props => (CompProperties_SingularityGraphic)props;

        private bool Active
        {
            get
            {
                if (!parent.Spawned || (power != null && !power.PowerOn)) return false;
                for (int i = 0; i < drivers.Count; i++)
                {
                    if (!drivers[i].SingularityActive) return false;
                }
                return true;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            drivers.Clear();
            foreach (ThingComp comp in parent.AllComps)
            {
                if (comp is ISingularityDriver driver)
                    drivers.Add(driver);
            }
            seed = parent.thingIDNumber % 997 * 0.173f;
            arcs.Clear();
            settled = false;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Vector3 center = parent.DrawPos + Props.offset;
            center.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            Advance(center);

            if (glitchEnd > lastRealtime)
                DrawGlitch(center);
            if (open <= 0f) return;
            DrawSingularity(center);
            if (arcs.Count > 0)
                DrawArcs(center);
        }

        /// <summary>每個畫格只推進一次視覺狀態。Advances the visual state once per frame.</summary>
        private void Advance(Vector3 center)
        {
            int frame = Time.frameCount;
            if (frame == lastFrame) return;
            lastFrame = frame;

            float now = Time.realtimeSinceStartup;
            float dt = lastRealtime < 0f ? 0f : Mathf.Min(now - lastRealtime, 0.1f);
            lastRealtime = now;

            bool active = Active;
            if (!settled)
            {
                settled = true;
                open = active ? 1f : 0f;
            }
            UpdateGlitch(center, now, dt);
            if (Find.TickManager.Paused) return;

            clock += dt;

            float target = active ? 1f : 0f;
            float seconds = target > open ? Props.openSeconds : Props.closeSeconds;
            open = seconds > 0f ? Mathf.MoveTowards(open, target, dt / seconds) : target;

            for (int i = arcs.Count - 1; i >= 0; i--)
            {
                if (clock - arcs[i].born >= arcs[i].life)
                    arcs.RemoveAt(i);
            }

            if (open >= ArcMinOpen && clock >= nextArcAt)
            {
                SpawnDischarge();
                nextArcAt = clock + Lerp(Props.arcIntervalSeconds);
            }
        }

        /// <summary>
        /// 盯著完全張開的奇點夠久之後，每秒擲一次骰決定是否發作。
        /// Once the fully open singularity has been stared at long enough, rolls once a second for an episode.
        /// </summary>
        private void UpdateGlitch(Vector3 center, float now, float dt)
        {
            if (Props.glitchShaderType == null || glitchEnd > now) return;
            if (open < 1f || !Observed(center))
            {
                observedSeconds = 0f;
                return;
            }

            observedSeconds += dt;
            if (observedSeconds < Props.glitchObserveSeconds || now < glitchQuietUntil || now < nextGlitchRollAt) return;
            nextGlitchRollAt = now + 1f;
            if (VisualRand.NextDouble() < Props.glitchChancePerSecond)
                StartGlitch(now);
        }

        private void StartGlitch(float now)
        {
            glitchStart = now;
            glitchEnd = now + Lerp(Props.glitchDurationSeconds);
            glitchSeed = VisualRand.Next(997);
            glitchQuietUntil = glitchEnd + Lerp(Props.glitchCooldownSeconds);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
            if (!DebugSettings.ShowDevGizmos || Props.glitchShaderType == null) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Singularity glitch",
                action = () => StartGlitch(Time.realtimeSinceStartup)
            };
        }

        /// <summary>
        /// 鏡頭在最近的兩級縮放，且奇點落在畫面中央附近。
        /// The camera is at one of the two closest zoom levels and the singularity sits near the middle of the view.
        /// </summary>
        private bool Observed(Vector3 center)
        {
            if (Find.CurrentMap != parent.Map) return false;
            CameraDriver driver = Find.CameraDriver;
            CameraZoomRange zoom = driver.CurrentZoom;
            if (zoom != CameraZoomRange.Closest && zoom != CameraZoomRange.Close) return false;
            CellRect view = driver.CurrentViewRect;
            Vector3 mid = view.CenterVector3;
            return Mathf.Abs(center.x - mid.x) < view.Width * 0.2f && Mathf.Abs(center.z - mid.z) < view.Height * 0.2f;
        }

        /// <summary>
        /// 以奇點為中心、蓋滿整個視野的故障方塊。發作期間一段一段地閃，約三成的片段留白。
        /// A glitch quad centred on the singularity that covers the whole view. It flickers in segments during an
        /// episode, about a third of them blank.
        /// </summary>
        private void DrawGlitch(Vector3 center)
        {
            float age = lastRealtime - glitchStart;
            int segment = (int)(age / GlitchSegmentSeconds);
            if (Hash01(glitchSeed * 31 + segment) < 0.3f) return;
            float strength = Mathf.Lerp(0.35f, 1f, Hash01(glitchSeed * 17 + segment * 7 + 3));

            CellRect view = Find.CameraDriver.CurrentViewRect;
            Vector3 mid = view.CenterVector3;
            float sizeX = (Mathf.Abs(center.x - mid.x) + view.Width * 0.5f + 1f) * 2f;
            float sizeZ = (Mathf.Abs(center.z - mid.z) + view.Height * 0.5f + 1f) * 2f;
            center.y = AltitudeLayer.MetaOverlays.AltitudeFor();

            Material mat = MaterialPool.MatFrom(new MaterialRequest(BaseContent.WhiteTex, Props.glitchShaderType.Shader));
            Color color = Props.color;
            color.a = strength;
            Block.Clear();
            Block.SetColor(ShaderPropertyIDs.Color, color);
            Block.SetVector(QuadSizeId, new Vector4(sizeX, sizeZ, 0f, 0f));
            Block.SetFloat(GlitchSeedId, glitchSeed + segment * 1.37f);
            Block.SetFloat(AgeSecsId, age);
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(sizeX, 1f, sizeZ));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0, null, 0, Block);
        }

        private void SpawnDischarge()
        {
            int count = 1;
            if (VisualRand.NextDouble() < 0.35) count++;
            if (VisualRand.NextDouble() < 0.1) count++;

            float horizonRadius = Props.horizon * Props.size * 0.5f;
            List<Vector3> targets = Props.arcTargets;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir;
                float length;
                if (!targets.NullOrEmpty() && VisualRand.NextDouble() < Props.arcTargetChance)
                {
                    Vector3 target = targets[VisualRand.Next(targets.Count)];
                    target.y = 0f;
                    float distance = target.magnitude;
                    if (distance <= horizonRadius) continue;
                    dir = target / distance;
                    length = distance - horizonRadius;
                }
                else
                {
                    dir = Vector3.forward.RotatedBy((float)VisualRand.NextDouble() * 360f);
                    length = Lerp(Props.freeArcLength);
                }

                arcs.Add(new Arc
                {
                    start = dir * horizonRadius,
                    angle = dir.AngleFlat(),
                    length = length,
                    bucket = SingularityArcMeshes.BucketFor(length),
                    variant = VisualRand.Next(SingularityArcMeshes.Variants),
                    born = clock,
                    life = Lerp(Props.arcLifeSeconds)
                });
            }
        }

        private void DrawSingularity(Vector3 center)
        {
            Material mat = MaterialPool.MatFrom(new MaterialRequest(BaseContent.WhiteTex, Props.shaderType.Shader));
            Color color = Props.color;
            color.a = open;
            Block.Clear();
            Block.SetColor(ShaderPropertyIDs.Color, color);
            Block.SetFloat(AgeSecsId, clock);
            Block.SetFloat(SeedId, seed);
            Block.SetFloat(HorizonId, Props.horizon);
            Block.SetFloat(LensingId, Props.lensing);
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(Props.size, 1f, Props.size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0, null, 0, Block);
        }

        private void DrawArcs(Vector3 center)
        {
            Material mat = MaterialPool.MatFrom(new MaterialRequest(BaseContent.WhiteTex, Props.arcShaderType.Shader));
            center.y += 0.01f;
            for (int i = 0; i < arcs.Count; i++)
            {
                Arc arc = arcs[i];
                float age = clock - arc.born;
                float u = Mathf.Clamp01(age / arc.life);
                // 瞬間打亮再衰減，外加逐格閃爍。A near-instant strike, then decay, with per-step flicker.
                float strength = u < 0.15f ? u / 0.15f : Mathf.Pow(1f - (u - 0.15f) / 0.85f, 1.5f);
                int flickerStep = (int)(age / FlickerSeconds);
                strength *= (0.55f + 0.45f * Hash01(arc.variant * 7919 + flickerStep * 104729 + (int)(arc.born * 977f))) * open;
                if (strength <= 0f) continue;

                int reshapeStep = (int)(age / ReshapeSeconds);
                Mesh mesh = SingularityArcMeshes.Get(arc.bucket, arc.variant + reshapeStep);
                float scale = arc.length / SingularityArcMeshes.BucketLength(arc.bucket);
                Matrix4x4 matrix = Matrix4x4.TRS(center + arc.start, Quaternion.AngleAxis(arc.angle, Vector3.up), new Vector3(scale, 1f, scale));

                Color color = Props.arcColor;
                color.a = strength;
                Block.Clear();
                Block.SetColor(ShaderPropertyIDs.Color, color);
                Block.SetColor(CoreColorId, Props.arcCoreColor);
                Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0, Block);
            }
        }

        private static float Lerp(FloatRange range) => Mathf.Lerp(range.min, range.max, (float)VisualRand.NextDouble());

        private static float Hash01(int x)
        {
            unchecked
            {
                uint h = (uint)x;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>
    /// 預先產生的電弧網格：從原點沿 +z 延伸的鋸齒折線帶，分四個長度級、每級數條。畫的時候等比例縮放到實際長度
    /// （寬度跟著變 ±25%，看起來自然），所以不必每次放電都建新網格。橫截面五個頂點，頂點色 r = 白熱的芯、
    /// g = 染色的輝光、a = 沿電弧的亮度，由 DMS_SingularityArc.frag.glsl 解讀。
    /// Pre-built arc meshes: jagged ribbons running from the origin along +z, in four length buckets with several
    /// variants each. They are scaled uniformly to the actual length when drawn (width varies by about ±25%, which
    /// reads naturally), so no discharge ever builds a mesh. Each cross-section has five vertices whose colour holds
    /// r = white-hot core, g = tinted glow, a = brightness along the arc, read by DMS_SingularityArc.frag.glsl.
    /// </summary>
    internal static class SingularityArcMeshes
    {
        public const int Variants = 6;

        private static readonly float[] BucketLengths = { 0.2f, 0.35f, 0.55f, 0.8f };

        private const float CoreHalfWidth = 0.01f;
        private const float GlowHalfWidth = 0.04f;

        private static readonly float[] CrossOffsets = { -GlowHalfWidth, -CoreHalfWidth, 0f, CoreHalfWidth, GlowHalfWidth };
        private static readonly float[] CrossCore = { 0f, 0f, 1f, 0f, 0f };
        private static readonly float[] CrossGlow = { 0f, 0.85f, 1f, 0.85f, 0f };

        private static Mesh[] meshes;

        public static float BucketLength(int bucket) => BucketLengths[bucket];

        public static int BucketFor(float length)
        {
            int best = 0;
            float bestError = float.MaxValue;
            for (int i = 0; i < BucketLengths.Length; i++)
            {
                float error = Mathf.Abs(Mathf.Log(Mathf.Max(length, 0.01f) / BucketLengths[i]));
                if (error < bestError)
                {
                    bestError = error;
                    best = i;
                }
            }
            return best;
        }

        public static Mesh Get(int bucket, int variant)
        {
            // 1.7 重新載入資源時若網格被銷毀（Unity 意義下的 null），整批重建。
            // Rebuild the whole pool if a 1.7 asset reload destroyed the meshes (Unity-null).
            if (meshes == null || meshes[0] == null)
                Build();
            return meshes[bucket * Variants + (variant % Variants + Variants) % Variants];
        }

        private static void Build()
        {
            // 固定種子：每次啟動的網格都一樣。A fixed seed keeps the pool identical between launches.
            System.Random rand = new System.Random(0x5EED);
            meshes = new Mesh[BucketLengths.Length * Variants];
            for (int b = 0; b < BucketLengths.Length; b++)
            {
                for (int v = 0; v < Variants; v++)
                    meshes[b * Variants + v] = MakeArc(BucketLengths[b], rand);
            }
        }

        private static Mesh MakeArc(float length, System.Random rand)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> triangles = new List<int>();

            List<Vector2> path = JaggedPath(Vector2.zero, new Vector2(0f, length), length * 0.16f, 4, rand);
            AddRibbon(path, 1f, 1f, vertices, colors, triangles);

            // 約一半的電弧在中段岔出一條較細較短的支流。About half the arcs fork a thinner, shorter branch midway.
            if (rand.NextDouble() < 0.5)
            {
                Vector2 origin = path[path.Count / 3 + rand.Next(path.Count / 3)];
                float side = rand.NextDouble() < 0.5 ? -1f : 1f;
                float angle = side * Mathf.Lerp(25f, 55f, (float)rand.NextDouble()) * Mathf.Deg2Rad;
                float branchLength = length * Mathf.Lerp(0.25f, 0.45f, (float)rand.NextDouble());
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                List<Vector2> branch = JaggedPath(origin, origin + dir * branchLength, branchLength * 0.2f, 3, rand);
                AddRibbon(branch, 0.6f, 0.7f, vertices, colors, triangles);
            }

            Mesh mesh = new Mesh
            {
                name = "DMS_SingularityArc",
                vertices = vertices.ToArray(),
                colors = colors.ToArray(),
                uv = new Vector2[vertices.Count],
                triangles = triangles.ToArray()
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 中點位移法產生的鋸齒折線，座標為 (x, z)，端點固定。
        /// Jagged polyline by midpoint displacement, in (x, z), with fixed end points.
        /// </summary>
        private static List<Vector2> JaggedPath(Vector2 from, Vector2 to, float amplitude, int levels, System.Random rand)
        {
            int segments = 1 << levels;
            float[] offsets = new float[segments + 1];
            float amp = amplitude;
            for (int step = segments; step >= 2; step /= 2)
            {
                int half = step / 2;
                for (int i = half; i < segments; i += step)
                    offsets[i] = (offsets[i - half] + offsets[i + half]) * 0.5f + Signed(rand) * amp;
                amp *= 0.55f;
            }

            Vector2 along = to - from;
            Vector2 normal = new Vector2(-along.y, along.x).normalized;
            List<Vector2> path = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                if (i > 0 && i < segments)
                    t += Signed(rand) * 0.3f / segments;
                path.Add(from + along * t + normal * offsets[i]);
            }
            return path;
        }

        private static void AddRibbon(List<Vector2> path, float widthScale, float brightness, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            int first = vertices.Count;
            int columns = CrossOffsets.Length;
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 tangent = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 normal = new Vector2(tangent.y, -tangent.x);
                float u = (float)i / (path.Count - 1);
                // 根部略細、中段最粗、尾端收細。Slightly thin at the root, widest early on, tapering to the tip.
                float taper = Mathf.Min(1f, 0.35f + u * 6f) * (1f - 0.6f * u) * widthScale;
                float alpha = (1f - 0.35f * u) * brightness;
                for (int j = 0; j < columns; j++)
                {
                    Vector2 p = path[i] + normal * (CrossOffsets[j] * taper);
                    vertices.Add(new Vector3(p.x, 0f, p.y));
                    colors.Add(new Color(CrossCore[j], CrossGlow[j], 0f, alpha));
                }
            }

            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int j = 0; j < columns - 1; j++)
                {
                    int a = first + i * columns + j;
                    int b = a + 1;
                    int c = a + columns + 1;
                    int d = a + columns;
                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                }
            }
        }

        private static float Signed(System.Random rand) => (float)rand.NextDouble() * 2f - 1f;
    }
}
