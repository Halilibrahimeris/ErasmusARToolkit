namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Render queues of everything that stands for the real world or lies on it. They all draw before
    /// opaque virtual objects (queue 2000), so a placed object always covers the grid and rings under it,
    /// while real-world depth (AR occlusion) can still hide them. See ARToolkitSurface.shader.
    /// </summary>
    public static class RenderOrder
    {
        /// <summary>
        /// First of the depth corrections under placed objects that stand where nothing was scanned
        /// (see ContactDepthView). Like <see cref="SurfaceDepth"/>, and before it.
        /// </summary>
        public const int ContactDepth = 1940;

        /// <summary>Heights that get a depth correction under placed objects.</summary>
        public const int ContactDepthSlots = 4;

        /// <summary>
        /// First of the depth corrections for scanned surfaces (room scan): three queues per surface, right
        /// after the AR camera image wrote the real-world depth and before anything is depth-tested against it.
        /// </summary>
        public const int SurfaceDepth = 1952;

        /// <summary>Scanned surfaces that get a depth correction.</summary>
        public const int SurfaceDepthSlots = 8;

        /// <summary>
        /// Queues one depth correction takes (mark, unmark, write): a surface finishes all steps before
        /// the next one starts, because they share one stencil bit.
        /// </summary>
        public const int SurfaceDepthQueuesPerSlot = 3;

        /// <summary>Desktop mode stand-ins for the camera picture: webcam background, simulated images.</summary>
        public const int CameraImage = 1980;

        /// <summary>Desktop mode virtual floor grid.</summary>
        public const int VirtualGround = 1985;

        /// <summary>
        /// Depth-only stand-ins for scanned tables (room scan). Before the grids so they can hide them.
        /// </summary>
        public const int RoomOccluder = 1988;

        /// <summary>AR plane grid fill.</summary>
        public const int PlaneFill = 1990;

        /// <summary>AR plane outline: after every fill so the stencil can hide inner edges.</summary>
        public const int PlaneOutline = 1991;

        /// <summary>Boxes around scanned obstacles and the panels of scanned walls.</summary>
        public const int ObstacleFill = 1992;

        /// <summary>Edges of obstacle boxes and wall panels.</summary>
        public const int ObstacleOutline = 1993;

        /// <summary>Placement ring, selection ring and image target frame.</summary>
        public const int SurfaceMark = 1995;
    }
}
