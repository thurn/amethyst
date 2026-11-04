using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Look
{
    /// <summary>
    /// The kill shatter (docs/game-design.md, Enemies &gt; Rules: kills shatter along outlined cracks
    /// in the enemy's hue, thrown from the blow, gone within half a second): Playlist Survivors'
    /// GPU MonsterShatter, which broke the neon edge trailer's monsters. Each type's rest pose is cut
    /// at bake time into 20 to 40 chunks, its weapon and shield with it
    /// (<see cref="MonsterType.ShardMesh"/>). On a death's frame the presenter writes the body
    /// <see cref="CrowdFlags.Dying"/> (the crowd blends its palette without drawing it) and calls
    /// <see cref="Break"/>; then, on the frame's first camera render, after the crowd's own (make
    /// the shatter after the crowd, so it renders after it):
    /// - each of the frame's deaths is seeded: its palette frozen into a ring slot and its chunks
    ///   placed at their pivots on that pose, kicked by the blow (MonsterShards.compute), so they
    ///   start where the body was on the frame it disappears;
    /// - every live shard is integrated to <see cref="Advance"/>'s time: gravity and tumble;
    /// - and every render draws each type's live deaths as instances of its chunk mesh in the edge
    ///   look (MonsterEdgeShard.shader), each chunk outlined along its crack.
    /// A type's deaths live in a ring; every death lives the same time, so they end in the order
    /// they began, and a full ring gives up its oldest.
    /// </summary>
    public sealed class MonsterShatter : IDisposable
    {
        /// <summary>MonsterShards.compute's LIFE: every chunk is gone by then.</summary>
        public const float LifeSeconds = 0.5f;

        /// <summary>Deaths a type can show at once.</summary>
        public const int RingCapacity = 320;

        /// <summary>Chunks alive at once across every type; the oldest deaths go first past it.</summary>
        public const int PieceBudget = 12000;

        /// <summary>Where a blow lands on a standing monster, as shares of its height: up from its
        /// feet, and back toward the hero.</summary>
        public const float HitHeight = 0.6f,
            HitDepth = 0.12f;

        const int ShardStride = 80; // MonsterShards.hlsl's Shard

        /// <summary>One death as the GPU passes read it: 64 bytes, matching MonsterShards.hlsl's Death.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct Death
        {
            public Vector3 Position;
            public float Yaw;
            public Vector3 Hit;
            public float Scale;
            public Vector2 Blow;
            public float Force;
            public float Time;
            public uint Tint;
            public uint Seed;
            public uint CrowdSlot;
            public uint Slot;

            public const int Stride = 64;
        }

        static readonly int IdSeeds = Shader.PropertyToID("_Seeds");
        static readonly int IdDeaths = Shader.PropertyToID("_Deaths");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdFrozen = Shader.PropertyToID("_Frozen");
        static readonly int IdShards = Shader.PropertyToID("_Shards");
        static readonly int IdShardRest = Shader.PropertyToID("_ShardRest");
        static readonly int IdSeedCount = Shader.PropertyToID("_SeedCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdShardCount = Shader.PropertyToID("_ShardCount");
        static readonly int IdRingStart = Shader.PropertyToID("_RingStart");
        static readonly int IdRingCount = Shader.PropertyToID("_RingCount");
        static readonly int IdRingCapacity = Shader.PropertyToID("_RingCapacity");
        static readonly int IdMotion = Shader.PropertyToID("_Motion");
        static readonly int IdMotionCount = Shader.PropertyToID("_MotionCount");
        static readonly int IdNow = Shader.PropertyToID("_Now");
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");
        static readonly int IdLargest = Shader.PropertyToID("_Largest");

        readonly ComputeShader cs;
        readonly MonsterCrowd crowd;
        readonly int kSeedPalette,
            kSeedShards,
            kIntegrate;
        readonly TypeState[] types;
        readonly CommandBuffer cmd = new CommandBuffer { name = "MonsterShatter" };
        readonly uint[] args = new uint[5];
        float now;
        int steppedFrame = -1;

        sealed class TypeState
        {
            public MonsterType Type;
            public int ShardCount;
            public float Largest;
            public GraphicsBuffer Seeds,
                Deaths,
                Frozen,
                Shards,
                Rest,
                Args,
                NoMotion;
            public Material Material;
            public MaterialPropertyBlock Block;
            public readonly Death[] Pending = new Death[RingCapacity];
            public int PendingCount;
            public readonly float[] Times = new float[RingCapacity];
            public int Start,
                Count,
                DrawnCount = -1;
        }

        /// <summary>Deaths showing shards.</summary>
        public int LiveDeaths { get; private set; }

        /// <summary>
        /// The shatter for <paramref name="crowd"/>'s types, seeding from its palettes; make it after
        /// the crowd, so each camera render seeds after the crowd has blended them.
        /// </summary>
        public MonsterShatter(MonsterCrowd crowd, Palette palette)
        {
            this.crowd = crowd;
            var lib = crowd.Library;
            cs = MonsterLibrary.Load<ComputeShader>("AmethystLook/MonsterShards");
            kSeedPalette = cs.FindKernel("SeedPalette");
            kSeedShards = cs.FindKernel("SeedShards");
            kIntegrate = cs.FindKernel("Integrate");
            types = new TypeState[lib.Types.Length];
            for (int i = 0; i < types.Length; i++)
                types[i] = Create(lib.Types[i], palette);
            RenderPipelineManager.beginCameraRendering += OnCamera;
        }

        static TypeState Create(MonsterType t, Palette palette)
        {
            if (t.ShardMesh == null || t.Shards.Length == 0)
                throw new InvalidOperationException(
                    $"MonsterShatter: {t.Id} has no shards (rebake: Amethyst.Look.Editor.CrowdBaker.Bake)"
                );
            var s = new TypeState { Type = t, ShardCount = t.Shards.Length };
            foreach (var sh in t.Shards)
                s.Largest = Mathf.Max(s.Largest, sh.Size);
            s.Seeds = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                RingCapacity,
                Death.Stride
            )
            {
                name = t.Id + "_shardSeeds",
            };
            s.Deaths = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                RingCapacity,
                Death.Stride
            )
            {
                name = t.Id + "_deaths",
            };
            s.Frozen = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                RingCapacity * t.BoneCount * 3,
                16
            )
            {
                name = t.Id + "_frozen",
            };
            s.Shards = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                RingCapacity * s.ShardCount,
                ShardStride
            )
            {
                name = t.Id + "_shards",
            };
            s.Rest = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                s.ShardCount,
                MonsterShard.Stride
            )
            {
                name = t.Id + "_shardRest",
            };
            s.Rest.SetData(t.Shards);
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, 4)
            {
                name = t.Id + "_shardArgs",
            };
            s.Args.SetData(new uint[5]);
            // The shards skin through the crowd's SkinVertex, whose motion layers skip them (not
            // alive): a stand-in buffer.
            s.NoMotion = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                MonsterMotion.Stride
            )
            {
                name = t.Id + "_shardNoMotion",
            };
            s.NoMotion.SetData(new[] { MonsterMotion.Still(0) });
            s.Material = EdgeLook.Shards(t, palette);
            s.Block = new MaterialPropertyBlock();
            s.Block.SetBuffer(IdDeaths, s.Deaths);
            s.Block.SetBuffer(IdShards, s.Shards);
            s.Block.SetBuffer(IdPalette, s.Frozen);
            s.Block.SetBuffer(IdMotion, s.NoMotion);
            s.Block.SetInt(IdMotionCount, 0);
            s.Block.SetInt(IdBoneCount, t.BoneCount);
            s.Block.SetInt(IdShardCount, s.ShardCount);
            s.Block.SetInt(IdRingCapacity, RingCapacity);
            return s;
        }

        /// <summary>Where a blow lands on a standing monster.</summary>
        public static Vector3 HitPoint(Vector3 feet, Vector3 blow, float height) =>
            feet + Vector3.up * (HitHeight * height) - blow * (HitDepth * height);

        /// <summary>
        /// A death on this frame (after writing <paramref name="body"/> Dying at
        /// <paramref name="crowdSlot"/> of the type's instances): broken at <paramref name="time"/> by a
        /// blow along <paramref name="blow"/> on the ground with launch distance
        /// <paramref name="force"/> m (1.85 is a normal sword blow). <paramref name="serial"/> seeds
        /// its kicks. False when the type's ring had no room left this frame.
        /// </summary>
        public bool Break(
            int type,
            int crowdSlot,
            in MonsterInstance body,
            int serial,
            float time,
            Vector2 blow,
            float force
        )
        {
            var s = types[type];
            if (s.PendingCount == RingCapacity)
                return false;
            MakeRoom(s.ShardCount, time);
            if (s.Count == RingCapacity)
            {
                s.Start = (s.Start + 1) % RingCapacity;
                s.Count--;
            }
            int slot = (s.Start + s.Count) % RingCapacity;
            s.Count++;
            s.Times[slot] = time;
            var dir = blow.sqrMagnitude > 1e-9f ? blow.normalized : Vector2.zero;
            float height = s.Type.HeightM * body.Scale;
            s.Pending[s.PendingCount++] = new Death
            {
                Position = body.Position,
                Yaw = body.Yaw,
                Hit = HitPoint(body.Position, new Vector3(dir.x, 0, dir.y), height),
                Scale = body.Scale,
                Blow = dir,
                Force = force,
                Time = time,
                Tint = body.Tint,
                Seed = (uint)serial * 2654435761u + 0x9e37u,
                CrowdSlot = (uint)crowdSlot,
                Slot = (uint)slot,
            };
            return true;
        }

        /// <summary>The shards' clock this frame (the presenter's, frozen while the game is).</summary>
        public void Advance(float time)
        {
            if (time < now)
                Clear();
            now = time;
        }

        // Retires deaths past their life, then the oldest across types while over the budget.
        void MakeRoom(int incoming, float at)
        {
            int pieces = 0;
            foreach (var s in types)
            {
                while (s.Count > 0 && s.Times[s.Start] + LifeSeconds <= at)
                {
                    s.Start = (s.Start + 1) % RingCapacity;
                    s.Count--;
                }
                pieces += s.Count * s.ShardCount;
            }
            while (pieces + incoming > PieceBudget)
            {
                TypeState oldest = null;
                foreach (var s in types)
                    if (
                        s.Count > 0
                        && (oldest == null || s.Times[s.Start] < oldest.Times[oldest.Start])
                    )
                        oldest = s;
                if (oldest == null)
                    break;
                oldest.Start = (oldest.Start + 1) % RingCapacity;
                oldest.Count--;
                pieces -= oldest.ShardCount;
            }
        }

        void OnCamera(ScriptableRenderContext context, Camera camera)
        {
            if (
                camera.cameraType != CameraType.Game
                || (camera.cullingMask & (1 << MonsterCrowd.Layer)) == 0
            )
                return;
            if (steppedFrame != Time.frameCount)
            {
                steppedFrame = Time.frameCount;
                Step(context);
            }
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in types)
            {
                if (s.Count == 0)
                    continue;
                var rp = new RenderParams(s.Material)
                {
                    worldBounds = bounds,
                    matProps = s.Block,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    camera = camera,
                    layer = MonsterCrowd.Layer,
                };
                Graphics.RenderMeshIndirect(rp, s.Type.ShardMesh, s.Args, 1, 0);
            }
        }

        // Seeds this frame's deaths from the crowd's palettes and moves every live shard to now.
        void Step(ScriptableRenderContext context)
        {
            cmd.Clear();
            MakeRoom(0, now);
            int deaths = 0;
            for (int type = 0; type < types.Length; type++)
            {
                var s = types[type];
                if (s.PendingCount > 0)
                    Seed(type, s);
                if (s.Count > 0)
                    Integrate(s);
                if (s.Count != s.DrawnCount)
                {
                    var mesh = s.Type.ShardMesh;
                    args[0] = mesh.GetIndexCount(0);
                    args[1] = (uint)s.Count;
                    args[2] = mesh.GetIndexStart(0);
                    args[3] = mesh.GetBaseVertex(0);
                    args[4] = 0;
                    cmd.SetBufferData(s.Args, args);
                    s.DrawnCount = s.Count;
                }
                s.Block.SetInt(IdRingStart, s.Start);
                s.Block.SetFloat(IdNow, now);
                deaths += s.Count;
            }
            context.ExecuteCommandBuffer(cmd);
            LiveDeaths = deaths;
        }

        void Seed(int type, TypeState s)
        {
            int n = s.PendingCount;
            s.PendingCount = 0;
            var palette = crowd.PaletteOf(type);
            cmd.SetBufferData(s.Seeds, s.Pending, 0, 0, n);
            cmd.SetComputeIntParam(cs, IdSeedCount, n);
            cmd.SetComputeIntParam(cs, IdBoneCount, s.Type.BoneCount);
            cmd.SetComputeIntParam(cs, IdShardCount, s.ShardCount);
            cmd.SetComputeFloatParam(cs, IdHeightM, s.Type.HeightM);
            cmd.SetComputeFloatParam(cs, IdLargest, s.Largest);
            cmd.SetComputeBufferParam(cs, kSeedPalette, IdSeeds, s.Seeds);
            cmd.SetComputeBufferParam(cs, kSeedPalette, IdPalette, palette);
            cmd.SetComputeBufferParam(cs, kSeedPalette, IdFrozen, s.Frozen);
            cmd.DispatchCompute(cs, kSeedPalette, Groups(n * s.Type.BoneCount * 3), 1, 1);
            cmd.SetComputeBufferParam(cs, kSeedShards, IdSeeds, s.Seeds);
            cmd.SetComputeBufferParam(cs, kSeedShards, IdDeaths, s.Deaths);
            cmd.SetComputeBufferParam(cs, kSeedShards, IdPalette, palette);
            cmd.SetComputeBufferParam(cs, kSeedShards, IdShards, s.Shards);
            cmd.SetComputeBufferParam(cs, kSeedShards, IdShardRest, s.Rest);
            cmd.DispatchCompute(cs, kSeedShards, Groups(n * s.ShardCount), 1, 1);
        }

        void Integrate(TypeState s)
        {
            cmd.SetComputeIntParam(cs, IdShardCount, s.ShardCount);
            cmd.SetComputeIntParam(cs, IdRingStart, s.Start);
            cmd.SetComputeIntParam(cs, IdRingCount, s.Count);
            cmd.SetComputeIntParam(cs, IdRingCapacity, RingCapacity);
            cmd.SetComputeFloatParam(cs, IdNow, now);
            cmd.SetComputeBufferParam(cs, kIntegrate, IdDeaths, s.Deaths);
            cmd.SetComputeBufferParam(cs, kIntegrate, IdShards, s.Shards);
            cmd.DispatchCompute(cs, kIntegrate, Groups(s.Count * s.ShardCount), 1, 1);
        }

        static int Groups(int threads) => Mathf.Max(1, (threads + 63) / 64);

        /// <summary>Drops every live and pending death.</summary>
        public void Clear()
        {
            foreach (var s in types)
                s.Start = s.Count = s.PendingCount = 0;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnCamera;
            cmd.Release();
            foreach (var s in types)
            {
                s.Seeds?.Dispose();
                s.Deaths?.Dispose();
                s.Frozen?.Dispose();
                s.Shards?.Dispose();
                s.Rest?.Dispose();
                s.Args?.Dispose();
                s.NoMotion?.Dispose();
                EdgeLook.Destroy(s.Material);
            }
        }
    }
}
