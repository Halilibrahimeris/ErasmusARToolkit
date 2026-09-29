using UnityEngine;
using UnityEngine.Rendering;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Materials for the depth correction of scanned surfaces ("Erasmus AR Toolkit/Surface Depth"): per slot a
    /// mark and a write material, in their own pair of render queues so each surface finishes both steps
    /// before the next surface starts (they share one stencil bit). A surface without a free slot simply keeps
    /// the measured depth.
    /// </summary>
    internal sealed class SurfaceDepthMaterials
    {
        /// <summary>Real points up to this high (meters) above a scanned surface count as that surface (depth noise).</summary>
        public const float PullHeight = 0.15f;

        /// <summary>Limit of the pull toward the camera, as a fraction of the distance.</summary>
        public const float MaxPull = 0.35f;

        /// <summary>The written surface depth lies this fraction of the distance farther, so objects on it never flicker.</summary>
        public const float PushBack = 0.004f;

        static readonly int PullHeightId = Shader.PropertyToID("_PullHeight");
        static readonly int MaxPullId = Shader.PropertyToID("_MaxPull");
        static readonly int PushBackId = Shader.PropertyToID("_PushBack");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

        readonly Material source;
        readonly Material[] markMaterials = new Material[RenderOrder.SurfaceDepthSlots];
        readonly Material[] writeMaterials = new Material[RenderOrder.SurfaceDepthSlots];
        readonly bool[] used = new bool[RenderOrder.SurfaceDepthSlots];

        public SurfaceDepthMaterials(Material surfaceDepthMaterial)
        {
            source = surfaceDepthMaterial;
        }

        public bool IsAvailable => source != null;

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
                markMaterials[slot] = Create("Mark", RenderOrder.SurfaceDepth + slot * 2,
                    PullHeight, 0f, CompareFunction.LessEqual, false, CompareFunction.Always, StencilOp.Replace);
            }

            return markMaterials[slot];
        }

        public Material Write(int slot)
        {
            if (writeMaterials[slot] == null)
            {
                writeMaterials[slot] = Create("Write", RenderOrder.SurfaceDepth + slot * 2 + 1,
                    0f, PushBack, CompareFunction.Always, true, CompareFunction.Equal, StencilOp.Zero);
            }

            return writeMaterials[slot];
        }

        public void Dispose()
        {
            for (var slot = 0; slot < used.Length; slot++)
            {
                ObjectUtility.SafeDestroy(markMaterials[slot]);
                ObjectUtility.SafeDestroy(writeMaterials[slot]);
                markMaterials[slot] = null;
                writeMaterials[slot] = null;
                used[slot] = false;
            }
        }

        Material Create(string step, int queue, float pullHeight, float pushBack, CompareFunction zTest, bool zWrite,
            CompareFunction stencilComp, StencilOp stencilPass)
        {
            var material = new Material(source) { name = $"Surface Depth {step} ({queue})", renderQueue = queue };
            material.SetFloat(PullHeightId, pullHeight);
            material.SetFloat(MaxPullId, MaxPull);
            material.SetFloat(PushBackId, pushBack);
            material.SetFloat(ZTestId, (float)zTest);
            material.SetFloat(ZWriteId, zWrite ? 1f : 0f);
            material.SetFloat(StencilCompId, (float)stencilComp);
            material.SetFloat(StencilPassId, (float)stencilPass);
            return material;
        }
    }
}
