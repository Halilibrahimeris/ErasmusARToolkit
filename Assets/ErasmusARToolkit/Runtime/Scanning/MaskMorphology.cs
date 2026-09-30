namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Tidies a mask of cells, as image processing does with black-and-white pictures. Closing fills
    /// gaps of one or two cells (the camera sampled the surface too coarsely there); opening removes
    /// what is thinner than three cells (the top edge of a chair back is not a surface to put things
    /// on). Cells the scan knows to be empty ("locked": seen through, or something stands there) are
    /// never switched on.
    /// </summary>
    internal static class MaskMorphology
    {
        /// <summary>Fills small gaps. No cell of the mask is lost.</summary>
        /// <param name="locked">Cells that must stay off, or null.</param>
        /// <param name="scratch">A buffer of the size of the mask.</param>
        public static void Close(bool[] mask, int width, int height, bool[] locked, bool[] scratch)
        {
            Dilate(mask, scratch, width, height, locked);
            for (var z = 0; z < height; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = z * width + x;
                    mask[index] = mask[index] || (scratch[index] && All(scratch, width, height, x, z));
                }
            }
        }

        /// <summary>Removes what is thinner than three cells. No cell is added.</summary>
        public static void Open(bool[] mask, int width, int height, bool[] locked, bool[] scratch)
        {
            for (var z = 0; z < height; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    scratch[z * width + x] = All(mask, width, height, x, z);
                }
            }

            for (var z = 0; z < height; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = z * width + x;
                    mask[index] = mask[index] && (locked == null || !locked[index]) && Any(scratch, width, height, x, z);
                }
            }
        }

        static void Dilate(bool[] source, bool[] target, int width, int height, bool[] locked)
        {
            for (var z = 0; z < height; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = z * width + x;
                    target[index] = source[index]
                                    || ((locked == null || !locked[index]) && Any(source, width, height, x, z));
                }
            }
        }

        static bool Any(bool[] cells, int width, int height, int x, int z)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var row = z + dz;
                if (row < 0 || row >= height)
                {
                    continue;
                }

                for (var dx = -1; dx <= 1; dx++)
                {
                    var column = x + dx;
                    if (column >= 0 && column < width && cells[row * width + column])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Cells outside of the mask count as off.</summary>
        static bool All(bool[] cells, int width, int height, int x, int z)
        {
            if (x < 1 || z < 1 || x >= width - 1 || z >= height - 1)
            {
                return false;
            }

            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (!cells[(z + dz) * width + x + dx])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
