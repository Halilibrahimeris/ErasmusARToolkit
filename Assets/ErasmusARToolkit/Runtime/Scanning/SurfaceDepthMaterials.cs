using UnityEngine;
using UnityEngine.Rendering;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Materials for the depth correction of scanned surfaces ("Erasmus AR Toolkit/Surface Depth"): per slot a
    /// mark, an unmark and a write material, in three render queues of their own, so each surface finishes
    /// all steps before the next surface starts (they share one stencil bit). A surface without a free slot
    /// simply keeps the measured depth.
    /// </summary>
    internal sealed class SurfaceDepthMaterials
    {
        /// <summary>
        /// Real points up to this high (meters) above a scanned surface count as that surface, right in
        /// front of the camera: depth noise. Higher real things (chair feet, bags) keep their measured depth.
        /// </summary>
        public const float NoiseBase = 0.02f;

        /// <summary>The height grows by this much per square meter of distance, as the noise of depth does.</summary>
        public const float NoiseGrowth = 0.01f;

        public const float MinPullHeight = 0.03f;
        public const float MaxPullHeight = 0.10f;

        /// <summary>Limit of the pull toward the camera, as a fraction of the distance.</summary>
        public const float MaxPull = 0.25f;

        /// <summary>The written surface depth lies this fraction of the distance farther, so objects on it never flicker.</summary>
        public const float PushBack = 0.004f;

        static readonly int NoiseBaseId = Shader.PropertyToID("_NoiseBase");
        static readonly int NoiseGrowthId = Shader.PropertyToID("_NoiseK");
        static readonly int MinPullId = Shader.PropertyToID("_MinPull");
        static readonly int MaxPullHeightId = Shader.PropertyToID("_MaxPullHeight");
        static readonly int MaxPullId = Shader.PropertyToID("_MaxPull");
        static readonly int DirectionId = Shader.PropertyToID("_Direction");
        static readonly int PushBackId = Shader.PropertyToID("_PushBack");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

        readonly Material source;
        readonly int firstQueue;
        readonly Material[] markMaterials;
        readonly Material[] unmarkMaterials;
        readonly Material[] writeMaterials;
        readonly bool[] used;

        public SurfaceDepthMaterials(Material surfaceDepthMaterial)
            : this(surfaceDepthMaterial, RenderOrder.SurfaceDepth, RenderOrder.SurfaceDepthSlots)
        {
        }

        /// <param name="firstRenderQueue">Queue of the first step of the first slot.</param>
        public SurfaceDepthMaterials(Material surfaceDepthMaterial, int firstRenderQueue, int slots)
        {
            source = surfaceDepthMaterial;
            firstQueue = firstRenderQueue;
            markMaterials = new Material[slots];
            unmarkMaterials = new Material[slots];
            writeMaterials = new Material[slots];
            used = new bool[slots];
        }

        public bool IsAvailable => source != null;

        public int SlotCount => used.Length;

        /// <summary>Slots in use right now.</summary>
        public int UsedCount
        {
            get
            {
                var count = 0;
                for (var slot = 0; slot < used.Length; slot++)
                {
                    if (used[slot])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>A free slot, or -1 when all are taken (or there is no material).</summary>
        public int Acquire()
        {
            if (source == null)
            {
                return -1;
            }

            for (var slot = 0; slot < used.Length; slot++)
            {
                if (!used[slot])
                {
                    used[slot] = true;
                    return slot;
                }
            }

            return -1;
        }

        public void Release(int slot)
        {
            if (slot >= 0 && slot < used.Length)
            {
                used[slot] = false;
            }
        }

        public Material Mark(int slot)
        {
            if (markMaterials[slot] == null)
            {
                markMaterials[slot] = Create("Mark", QueueOf(slot, 0), 1f, 0f, CompareFunction.LessEqual, false,
                    CompareFunction.Always, StencilOp.Replace);
            }

            return markMaterials[slot];
        }

        public Material Unmark(int slot)
        {
            if (unmarkMaterials[slot] == null)
            {
                unmarkMaterials[slot] = Create("Unmark", QueueOf(slot, 1), -1f, 0f, CompareFunction.LessEqual, false,
                    CompareFunction.Equal, StencilOp.Zero);
            }

            return unmarkMaterials[slot];
        }

        public Material Write(int slot)
        {
            if (writeMaterials[slot] == null)
            {
                writeMaterials[slot] = Create("Write", QueueOf(slot, 2), 0f, PushBack, CompareFunction.Always, true,
                    CompareFunction.Equal, StencilOp.Zero);
            }

            return writeMaterials[slot];
        }

        public void Dispose()
        {
            for (var slot = 0; slot < used.Length; slot++)
            {
                ObjectUtility.SafeDestroy(markMaterials[slot]);
                ObjectUtility.SafeDestroy(unmarkMaterials[slot]);
                ObjectUtility.SafeDestroy(writeMaterials[slot]);
                markMaterials[slot] = null;
                unmarkMaterials[slot] = null;
                writeMaterials[slot] = null;
                used[slot] = false;
            }
        }

        int QueueOf(int slot, int step) => firstQueue + slot * RenderOrder.SurfaceDepthQueuesPerSlot + step;

        Material Create(string step, int queue, float direction, float pushBack, CompareFunction zTest, bool zWrite,
            CompareFunction stencilComp, StencilOp stencilPass)
        {
            var material = new Material(source) { name = $"Surface Depth {step} ({queue})", renderQueue = queue };
            material.SetFloat(NoiseBaseId, NoiseBase);
            material.SetFloat(NoiseGrowthId, NoiseGrowth);
            material.SetFloat(MinPullId, MinPullHeight);
            material.SetFloat(MaxPullHeightId, MaxPullHeight);
            material.SetFloat(MaxPullId, MaxPull);
            material.SetFloat(DirectionId, direction);
            material.SetFloat(PushBackId, pushBack);
            material.SetFloat(ZTestId, (float)zTest);
            material.SetFloat(ZWriteId, zWrite ? 1f : 0f);
            material.SetFloat(StencilCompId, (float)stencilComp);
            material.SetFloat(StencilPassId, (float)stencilPass);
            return material;
        }
    }
}
