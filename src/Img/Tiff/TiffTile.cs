using System;
using PdfLib.Util;
using System.Diagnostics;
using PdfLib.Img.Tiff.Internal;

namespace PdfLib.Img.Tiff
{
    /// <summary>
    /// Contains either compressed or uncompressed tile data.
    /// </summary>
    [DebuggerDisplay("{Width} - {Height}")]
    public class TiffTile
    {
        /// <summary>
        /// Only set on tiles where Width is smaller than TileWidth.
        /// In other words, only for use on edge tiles. 
        /// </summary>
        private TiffRawContents _parent;

        /// <summary>
        /// Only set on edge tiles that crosses the full image width or height.
        /// </summary>
        public readonly int[] StrideAr;

        /// <summary>
        /// Stride for chunky tiles. 
        /// </summary>
        public int Stride
        {
            get
            {
                if (Data.Length != 1) throw new NotSupportedException();
                if (StrideAr != null) return StrideAr[0];
                return (_parent.BitsPerPixel * Width + 7) / 8;
            }
        }
        /// <summary>
        /// Width, as defined in the tiff
        /// </summary>
        public int TileWidth { get { return _parent.TileWidth; } }

        /// <summary>
        /// Height, as defined in the tiff
        /// </summary>
        public int TileHeight { get { return _parent.TileHeight; } }

        /// <summary>
        /// Real height and width of the tile
        /// </summary>
        public readonly int Width, Height;
        public readonly byte[][] Data;
        public SampleFormat[] Format { get { return _parent.SampleFormat; } }
        public int NComponents { get { return _parent.NComponents; } }
        public ushort[] BitsPerSample { get { return _parent.BitsPerSample; } }

        internal TiffTile(int[] stride, int w, int h, byte[][] d, TiffRawContents parent)
        { StrideAr = stride; Width = w; Height = h; Data = d; _parent = parent; }

        internal int[][] ChunkyToPlanar(byte[] data)
        {
            var planar = new int[NComponents][];
            var bps = _parent.BitsPerSample;
            for (int c = 0; c < planar.Length; c++)
                planar[c] = new int[Width * Height];
            int bpc = Util.ArrayHelper.Max(bps);
            if (bpc > 32) throw new TiffNotImplementedException("More than 32-bit chunky to planar conversion");

            var br = new BitStream64(data);
            int end = planar[0].Length;
            for (int pos = 0; pos < end; pos++)
            {
                for (int c = 0; c < planar.Length; c++)
                {
                    bpc = bps[c];
                    if (!br.HasBits(bpc))
                        throw new TiffReadException("Not enough data for c2p");
                    planar[c][pos] = unchecked((int) br.PeekBits(bpc));
                    br.ClearBits(bpc);
                }
            }
            return planar;
        }
    }

    public abstract class TiffRawContents
    {
        /// <summary>
        /// Number of tiles in the horizontal direction
        /// </summary>
        public int TilesAcross { get; protected set; }

        /// <summary>
        /// Number of tiles in the vertical direction
        /// </summary>
        public int TilesDown { get; protected set; }

        /// <summary>
        /// Totall number of tiles
        /// </summary>
        public int NumberOfTiles { get; protected set; }

        /// <summary>
        /// Size of the tile
        /// </summary>
        public int TileWidth { get; protected set; }
        public int TileHeight { get; protected set; }
        public int ImageWidth { get; protected set; }
        public int ImageHeight { get; protected set; }

        /// <summary>
        /// If data is planar or cunky
        /// </summary>
        public readonly bool Planar;

        /// <summary>
        /// How many samples are in one pixel
        /// </summary>
        public readonly int SamplesPerPixel, BitsPerPixel;
        public readonly ushort[] BitsPerSample;

        public readonly SampleFormat[] SampleFormat;
        public int NComponents { get { return SampleFormat.Length; } }

        /// <summary>
        /// All the compressed data of the image
        /// </summary>
        public TiffTile[,] RawData
        {
            get
            {
                var data = new TiffTile[TilesAcross, TilesDown];
                for (int pos = 0, y = 0; y < TilesDown; y++)
                {
                    for (int x = 0; x < TilesAcross; x++, pos++)
                        data[x,y] = GetTile(pos);
                }
                return data;
            }
        }

        public abstract byte[][] GetRawData();

        /// <summary>
        /// Total size of the data
        /// </summary>
        public abstract ulong TotalSize { get; }

        /// <summary>
        /// Size of each tile
        /// </summary>
        internal abstract ulong[] TileSizes { get; }

        internal TiffRawContents(TiffImage img)
        {
            ImageWidth = img.Width;
            ImageHeight = img.Height;
            TileWidth = (int) img.TileWidth;
            Planar = img.PlanarConfiguration == Internal.PixelMode.Planar;
            SamplesPerPixel = img.SamplesPerPixel;
            BitsPerSample = img.BitsPerSample;
            var bpc_ar = BitsPerSample;
            for (int c = 0; c < bpc_ar.Length; c++)
                BitsPerPixel += bpc_ar[c];
            SampleFormat = img.SampleFormat;
        }

        internal abstract void WriteData(int row_nr, byte[] buffer);

        public abstract TiffTile GetTile(int nr);
        protected TiffTile GetTile(int nr, byte[][] data)
        {
            int w_pos = nr % TilesAcross + 1;
            int width = Math.Min(0, ImageWidth - w_pos * TileWidth);
            int h_pos = nr / TilesAcross + 1;
            int height = Math.Min(0, ImageHeight - h_pos * TileHeight);
            int[] stride;
            if (width < 0)
            {
                stride = new int[data.Length];
                if (stride.Length == BitsPerSample.Length)
                {
                    for (int c = 0; c < stride.Length; c++)
                        stride[c] = (TileWidth * BitsPerSample[c] + 7) / 8;
                }
                else
                {
                    stride[0] = (TileWidth * BitsPerPixel + 7) / 8;
                }
            }
            else stride = null;

            return new TiffTile(stride, TileWidth + width, TileHeight + height, data, this);
        }
    }

    internal sealed class TiffMemContents : TiffRawContents
    {
        byte[][] _data;
        public override ulong TotalSize
        {
            get 
            {
                ulong size = 0;
                for (int c = 0; c < _data.Length; c++)
                    size += (ulong) _data[c].Length;
                return size;
            }
        }
        internal override ulong[] TileSizes
        {
            get
            {
                ulong[] size = new ulong[_data.Length];
                for (int c = 0; c < _data.Length; c++)
                    size[c] = (ulong)_data[c].Length;
                return size;
            }
        }
        public TiffMemContents(TiffImage img, byte[][] data) : base(img) 
        {
            if (data == null)
                throw new ArgumentException("Image has no data");
            _data = data;

            if (TileWidth == 0)
            {
                TilesAcross = 1;
                if (Planar)
                    NumberOfTiles = TilesDown = data.Length / SamplesPerPixel;
                else
                    NumberOfTiles = TilesDown = data.Length;
                TileWidth = ImageWidth;
            }
            else
            {
                TilesAcross = img.TilesAcross;
                TilesDown = img.TilesDown;
                NumberOfTiles = TilesAcross * TilesDown;
            }
            TileHeight = (int)Math.Min(img.RowsPerStrip, (uint)img.Height);
            if (TileHeight <= 0) TileHeight = img.Height;
        }
        public override byte[][] GetRawData()
        {
            return _data;
        }
        public override TiffTile GetTile(int nr)
        {
            byte[][] data;
            if (Planar)
            {
                data = new byte[SamplesPerPixel][];
                int offset = _data.Length / SamplesPerPixel;
                for (int c = 0; c < SamplesPerPixel; c++)
                {
                    int n = nr + c * offset;
                    data[c] = _data[n];
                }
            }
            else
            {
                data = new byte[][] { _data[nr] };
            }

            return GetTile(nr, data);
        }
        internal override void WriteData(int row_nr, byte[] buffer)
        {
            var data = _data[row_nr];
            var l = Math.Min(buffer.Length, data.Length);
            Buffer.BlockCopy(data, 0, buffer, 0, l);
        }
    }

    internal sealed class TiffFileContents : TiffRawContents
    {
        ulong[] _tile_offsets, _tile_sizes;
        TiffStreamReader _file;
        public override ulong TotalSize
        {
            get 
            {
                ulong ts = 0;
                for (int c = 0; c < _tile_sizes.Length; c++)
                    ts += (ulong) _tile_sizes[c];
                return ts;
            }
        }
        internal override ulong[] TileSizes { get { return _tile_sizes; } }
        internal TiffFileContents(TiffImage img, TiffStreamReader file)
            :base(img)
        {
            _file = file;

            if (TileWidth != 0)
            {
                _tile_offsets = img.TileOffsets;
                _tile_sizes = img.TileByteCounts;
                if (_tile_offsets == null)
                {
                    _tile_offsets = img.StripOffsets;
                    _tile_sizes = img.StripByteCounts;
                }
                if (_tile_offsets != null)
                {
                    if (_tile_sizes == null) throw new TiffReadException("Tile byte counts missing");
                    TilesAcross = img.TilesAcross;
                    TilesDown = img.TilesDown;
                    TileWidth = (int)img.TileWidth;
                    TileHeight = (int)Math.Min(img.TileLength, (uint)img.Height);

                    if (Planar)
                        NumberOfTiles = _tile_offsets.Length / SamplesPerPixel;
                    else
                        NumberOfTiles = _tile_offsets.Length;
                    if (_tile_offsets.Length != _tile_sizes.Length && TilesAcross * TilesDown != NumberOfTiles)
                        throw new TiffReadException("Tile missmatch");

                    return;
                }
            }

            _tile_offsets = img.StripOffsets;
            if (_tile_offsets != null)
            {
                _tile_sizes = img.StripByteCounts;
                if (_tile_sizes == null)
                {
                    if (img.Compression == COMPRESSION_SCHEME.UNCOMPRESSED)
                    {
                        _tile_sizes = new ulong[_tile_offsets.Length];
                        int h = (int)Math.Min(img.RowsPerStrip, (uint)img.Height);
                        ulong size = (ulong)((img.BitsPerPixel * img.Width + 7) / 8 * h);
                        for (int c = 0; c < _tile_sizes.Length; c++)
                            _tile_sizes[c] = size;
                    }
                    else
                        throw new TiffReadException("Strip byte counts missing");
                }
                TilesAcross = 1;
                if (Planar)
                    NumberOfTiles = TilesDown = _tile_offsets.Length / SamplesPerPixel;
                else
                    NumberOfTiles = TilesDown = _tile_offsets.Length;
                TileWidth = ImageWidth;
                TileHeight = (int)Math.Min(img.RowsPerStrip, (uint)img.Height);
                if (TileHeight <= 0) TileHeight = img.Height;

                if (_tile_offsets.Length != _tile_sizes.Length)
                    throw new TiffReadException("Strip missmatch");

                return;
            }

            throw new TiffReadException("Image has no data");
        }

        public override byte[][] GetRawData()
        {
            byte[][] data = new byte[_tile_offsets.Length][];
            for (int c = 0; c < _tile_offsets.Length; c++)
            {
                ulong l = _tile_sizes[c];
                var d = data[c] = new byte[l];
                _file.ReadEx((long)_tile_offsets[c], d, 0, (int)l);
            }

            return data;
        }

        public override TiffTile GetTile(int nr)
        {
            byte[][] data;

            if (Planar)
            {
                data = new byte[SamplesPerPixel][];
                int offset = _tile_offsets.Length / SamplesPerPixel;
                for (int c = 0; c < SamplesPerPixel; c++)
                {
                    int n = nr + c * offset;
                    ulong l = _tile_sizes[n];
                    data[c] = new byte[l];
                    _file.ReadEx((long)_tile_offsets[n], data[c], 0, (int)l);
                }
            }
            else
            {
                ulong l = _tile_sizes[nr];
                data = new byte[][] { new byte[l] };
                _file.ReadEx((long)_tile_offsets[nr], data[0], 0, (int)l);
            }

            return GetTile(nr, data);
        }

        internal override void WriteData(int row_nr, byte[] buffer)
        {
            _file.ReadEx((long)_tile_offsets[row_nr], buffer, 0, (int)_tile_sizes[row_nr]);
        }
    }
}
