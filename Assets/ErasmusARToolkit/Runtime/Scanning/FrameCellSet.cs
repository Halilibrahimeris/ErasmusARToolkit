namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Remembers which (cell, height) pairs a camera frame has already counted. Several depth points on
    /// one spot are one observation, so the scan behaves the same however densely a phone samples its
    /// depth image. A fixed table that is emptied by advancing a stamp: nothing is allocated per frame.
    /// </summary>
    internal sealed class FrameCellSet
    {
        const int Slots = 1 << 14;
        const int Mask = Slots - 1;

        /// <summary>Beyond this many entries a frame's further points count as already seen.</summary>
        const int MaxEntries = Slots * 3 / 4;

        readonly long[] keys = new long[Slots];
        readonly int[] stamps = new int[Slots];
        int stamp = 1;
        int entries;

        public int Count => entries;

        /// <summary>Forgets everything: the next frame starts.</summary>
        public void NextFrame()
        {
            entries = 0;
            if (++stamp == int.MaxValue)
            {
                System.Array.Clear(stamps, 0, Slots);
                stamp = 1;
            }
        }

        /// <summary>True the first time a pair is added in a frame.</summary>
        public bool Add(int x, int z, int bin)
        {
            if (entries >= MaxEntries)
            {
                return false;
            }

            var key = ((long)(x & 0xFFFFF) << 40) | ((long)(z & 0xFFFFF) << 20) | (long)(bin & 0xFFFFF);
            var slot = (int)((ulong)(key * -7046029254386353131L) >> 50) & Mask;
            while (stamps[slot] == stamp)
            {
                if (keys[slot] == key)
                {
                    return false;
                }

                slot = (slot + 1) & Mask;
            }

            stamps[slot] = stamp;
            keys[slot] = key;
            entries++;
            return true;
        }
    }
}
