namespace PdfLib.Img.Internal
{
    public abstract class ErrorDiffusionDithering
    {
        protected readonly Matrix _matrix;
        protected readonly int _start;
        private readonly int _shift;
        private int _mask;

        /// <summary>
        /// The boundaries of the image
        /// </summary>
        public int Width, Height;
        public int BitsPerComponent
        {
            set
            {
                _mask = (1 << value) - 1;
            }
        }

        protected ErrorDiffusionDithering(Matrix m)
        {
            _matrix = m;
            _shift = FindPowerOf2(m.Divisor);

            for (int c = 0; c < m.Width; c++)
            {
                if (m.I[0, c] != 0)
                {
                    _start = c - 1;
                    break;
                }
            }
        }

        int FindPowerOf2(int x)
        {
            int c = 0;
            for (; (x & 1) == 0 && x > 1; c++)
                x >>= 1;
            return x == 1 ? c : 0;
        }

        /// <summary>
        /// Dithers while clamping the color values to 0 - max
        /// </summary>
        /// <param name="colors">An array of colors</param>
        /// <param name="org_color">
        /// The original color.
        /// </param>
        /// <param name="new_color">
        /// The new color. Supplied as a parameter since colors[x*y] may be in a different format
        /// </param>
        /// <param name="x">X position of the new color</param>
        /// <param name="y">Y position of the new color</param>
        public void Dither(int[] colors, int org_color, int new_color, int x, int y)
        {
            int error = org_color - new_color;

            for (int m_y = 0; m_y < _matrix.Height; m_y++)
            {
                int offset_y = m_y + y;

                for (int m_x = 0; m_x < _matrix.Width; m_x++)
                {
                    int mul = _matrix.I[m_y, m_x];
                    int offset_x = x + (m_x - _start);

                    if (mul != 0 && offset_x > 0 && offset_x < Width && offset_y > 0 && offset_y < Height)
                    {
                        int pixel_index = offset_y * Width + offset_x;
                        int color = colors[pixel_index];

                        if (_shift != 0)
                            color += error * mul >> _shift;
                        else
                            color += error * mul / _matrix.Divisor;

                        //Doing color & mask gives zero/low values, when we want a high value. Alternativly
                        //thus clamping could be done outside the filter. That might give better filtering
                        //result.
                        if (color > _mask)
                            color = _mask;
                        else if (color < 0)
                            color = 0;
                        colors[pixel_index] = color;
                    }
                }
            }
        }

        /// <summary>
        /// Dithers without clamping the color values to 0 - max
        /// </summary>
        /// <param name="colors">An array of colors</param>
        /// <param name="org_color">
        /// The original color.
        /// </param>
        /// <param name="new_color">
        /// The new color. Supplied as a parameter since colors[x*y] may be in a different format
        /// </param>
        /// <param name="x">X position of the new color</param>
        /// <param name="y">Y position of the new color</param>
        public void UnclampedDither(int[] colors, int org_color, int new_color, int x, int y)
        {
            int error = org_color - new_color;

            for (int m_y = 0; m_y < _matrix.Height; m_y++)
            {
                int offset_y = m_y + y;

                for (int m_x = 0; m_x < _matrix.Width; m_x++)
                {
                    int mul = _matrix.I[m_y, m_x];
                    int offset_x = x + (m_x - _start);

                    if (mul != 0 && offset_x > 0 && offset_x < Width && offset_y > 0 && offset_y < Height)
                    {
                        int pixel_index = offset_y * Width + offset_x;
                        int color = colors[pixel_index];

                        if (_shift != 0)
                            color += error * mul >> _shift;
                        else
                            color += error * mul / _matrix.Divisor;

                        colors[pixel_index] = color;
                    }
                }
            }
        }

        protected struct Matrix
        {
            public readonly int[,] I;
            //public readonly double[,] D;
            public readonly int Width, Height;
            public readonly int Divisor;
            public Matrix(int[,] m, int divisor)
            {
                I = m;
                Width = m.GetUpperBound(1) + 1;
                Height = m.GetUpperBound(0) + 1;
                Divisor = divisor;
                //D = new double[Height, Width];
                //for (int y = 0; y < Height; y++)
                //{
                //    for (int x = 0; x < Width; x++)
                //    {
                //        D[y, x] = I[y, x] / (double)divisor;
                //    }
                //}
            }
        }
    }

    public sealed class FloydSteinbergDithering : ErrorDiffusionDithering
    {
        //https://en.wikipedia.org/wiki/Floyd%E2%80%93Steinberg_dithering
        public FloydSteinbergDithering(int width, int height, int bpc)
            : base(new Matrix(new int[,] { { 0, 0, 7 }, { 3, 5, 1 } }, 16))
        { Width = width; Height = height; BitsPerComponent = bpc; }
    }

    public sealed class AtkinsonDitheringDithering : ErrorDiffusionDithering
    {
        //http://www.tannerhelland.com/4660/dithering-eleven-algorithms-source-code/
        public AtkinsonDitheringDithering(int width, int height, int bpc)
            : base(new Matrix(new int[,] { { 0, 0, 1, 1 }, { 1, 1, 1, 0 }, { 0, 1, 0, 0 } }, 8))
        { Width = width; Height = height; BitsPerComponent = bpc; }
    }
}
