using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// A scan source that sees a <see cref="RoomSimulator"/> room from where a camera is, so the room scan
    /// component can be run without an AR device.
    /// </summary>
    public sealed class SimulatedRoomSource : IScanFrameSource
    {
        readonly RoomSimulator room;
        readonly Transform camera;

        public SimulatedRoomSource(RoomSimulator room, Transform camera, bool precise = true)
        {
            this.room = room;
            this.camera = camera;
            IsPrecise = precise;
        }

        public bool IsPrecise { get; }

        public bool IsAvailable { get; set; } = true;

        public bool Began { get; private set; }

        public bool Ended { get; private set; }

        public int FramesRead { get; private set; }

        public void Begin() => Began = true;

        public void End() => Ended = true;

        public bool TryReadFrame(ScanFrame frame)
        {
            if (!IsAvailable)
            {
                return false;
            }

            room.Capture(camera.position, camera.rotation, frame, IsPrecise);
            FramesRead++;
            return true;
        }
    }
}
