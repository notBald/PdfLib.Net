//This seems to be in line with what other tiff readers expect.
#define PADD_BOTTOM_TILES
using System;
using System.Collections.Generic;
using System.IO;
using PdfLib.Util;
using System.Diagnostics;

namespace PdfLib.Img.Tiff.Internal
{
    [System.Diagnostics.DebuggerDisplay("{ID}: {Type}")]
    public abstract class Tag
    {
        public TagID ID;
        public readonly DataType Type;
        public abstract ulong Count { get; }
        public abstract bool BigEndian { get; }
        public abstract bool HasData { get; }

        /// <summary>
        /// Size of data
        /// </summary>
        //public ulong ContentSize { get { return (ulong) TagSize * Count; } }
        protected ulong _count;

        public int TagSize { get { return GetTagSize(Type); } }

        public Tag(TagID id, DataType type, ulong count)
        { ID = id; Type = type; _count = count; }

        public abstract ulong GetSingleValue();
        public abstract double? GetRealValue();
        public abstract byte[] GetBytes();
        public abstract ushort[] GetUShorts();
        public abstract uint[] GetUInts();
        public abstract ulong[] GetULongs();
        public abstract double[] GetReals();
        public abstract ushort[] GetAsUShorts();
        public abstract ulong[] GetAsULongs();
        public abstract object[] GetObjects();

        public abstract Tag LoadData();

        internal static int GetTagSize(DataType type)
        {
            int length = 4;
            switch (type)
            {
                default:
                    break;
                case DataType.UNDEFINED:
                case DataType.ASCII:
                case DataType.BYTE:
                    length = 1;
                    break;
                case DataType.DOUBLE:
                case DataType.RATIONAL:
                case DataType.SRATIONAL:
                case DataType.SLONG8:
                case DataType.LONG8:
                case DataType.IFD8:
                    length = 8;
                    break;
                case DataType.SHORT:
                case DataType.SSHORT:
                    length = 2;
                    break;
            }
            return length;
        }

        internal static ulong GetTagMax(DataType type)
        {
            switch (type)
            {
                default:
                    break;
                case DataType.UNDEFINED:
                case DataType.ASCII:
                case DataType.BYTE:
                    return byte.MaxValue;
                case DataType.DOUBLE:
                case DataType.RATIONAL:
                case DataType.SRATIONAL:
                case DataType.SLONG8:
                case DataType.LONG8:
                case DataType.IFD8:
                    return ulong.MaxValue;
                case DataType.SHORT:
                case DataType.SSHORT:
                    return ushort.MaxValue;
            }
            return uint.MaxValue;
        }

        #region Meta

        protected internal MetaTag Meta { get { return GetMeta(ID); } }

        internal static MetaTag GetMeta(TagID id)
        {
            switch (id)
            {
                //Byte:
                case TagID.Photoshop:
                case TagID.XMP:
                    return new MetaTag(DataType.BYTE);

                //Shorts
                case TagID.ExtraSamples:
                    return new MetaTag(DataType.SHORT);
                case TagID.CLEANFAXDATA:
                case TagID.CellLength:
                case TagID.CellWidth:
                case TagID.PhotometricInterpretation:
                case TagID.PageNumber:
                    return new MetaTag(DataType.SHORT, 2);
                case TagID.Indexed:
                    return new MetaTag(DataType.SHORT, 1, 0);
                case TagID.YCbCrPositioning:
                case TagID.InkSet:
                case TagID.Predictor:
                case TagID.PlanarConfiguration:
                case TagID.Orientation:
                case TagID.FILLORDER:
                case TagID.Threshholding:
                case TagID.Compression:
                case TagID.SamplesPerPixel:
                    return new MetaTag(DataType.SHORT, 1, 1);
                case TagID.ResolutionUnit:
                case TagID.GrayResponseUnit:
                    return new MetaTag(DataType.SHORT, 1, 2);
                case TagID.YCbCrSubSampling:
                    return new MetaTag(DataType.SHORT, 2, 2);

                //Longs
                case TagID.FreeByteCounts:
                case TagID.FreeOffsets:
                    return new MetaTag(DataType.LONG);
                case TagID.ExifIFD: //<-- problem tag
                    return new MetaIFD(DataType.LONG, 1, TiffIFD.IFDType.Exif);
                case TagID.T6OPTIONS:
                case TagID.T4OPTIONS:
                    return new MetaTag(DataType.LONG, 1, 0);

                //Rational
                case TagID.YPosition:
                case TagID.XPosition:
                case TagID.YResolution:
                case TagID.XResolution:
                    return new MetaTag(DataType.RATIONAL, 1);
                
                //Strings
                case TagID.Copyright:
                case TagID.HostComputer:
                case TagID.Artist:
                case TagID.DateTime:
                case TagID.Software:
                case TagID.Model:
                case TagID.Make:
                case TagID.ImageDescription:
                case TagID.DocumentName:
                    return new MetaTag(DataType.ASCII);

                //Undefined
                case TagID.JPEGTables:
                    return new MetaTag(DataType.UNDEFINED);

                //Multiple types
                case TagID.ConsecutiveBadFaxLines:
                case TagID.BadFaxLines:
                case TagID.TileLength:
                case TagID.TileWidth:
                case TagID.ImageLength:
                case TagID.ImageWidth:
                    return new MetaTag(new DataType[] { DataType.SHORT, DataType.LONG }, 1);
                case TagID.RowsPerStrip:
                    return new MetaTag(new DataType[] { DataType.SHORT, DataType.LONG }, 1, uint.MaxValue);
                case TagID.SubIFDs:
                    return new MetaIFD(TiffIFD.IFDType.Image);

                //Custom
                case TagID.MaxSampleValue: return new MetaCSPP(uint.MaxValue);
                case TagID.MinSampleValue: return new MetaCSPP(0);
                case TagID.BitsPerSample: return new MetaCSPP(1);
                case TagID.StripByteCounts: return new MetaStripBC();
                case TagID.StripOffsets: return new MetaStripOffsets();
                case TagID.GrayResponseCurve: return new MetaBPS();
                case TagID.ColorMap: return new MetaCM();
                case TagID.TileOffsets: return new MetaTileOffsets();
                case TagID.TileByteCounts: return new MetaTileBC();
                case TagID.DotRange: return new MetaDR();
                case TagID.SampleFormat: return new MetaSF();
                case TagID.YCbCrCoefficients: return new MetaYCC();
                case TagID.ReferenceBlackWhite: return new MetaBW();
                case TagID.SubfileType: return new MetaSFT();
                case TagID.NewSubfileType: return new MetaNSFT();
            }

            return new MetaTag();
        }

        internal protected class MetaTag
        {
            public readonly DataType[] DTypes;
            protected const int NO_COUNT = 0;
            protected readonly int Count;
            protected uint? _defult;
            /// <summary>
            /// True, if the data is an offset into the file
            /// </summary>
            public virtual bool IsOffsetData { get { return false; } }
            public virtual bool WriteTag { get { return true; } }
            public virtual TagID Replace { get { return TagID.PROGRESSIVE; } }

            public MetaTag() { DTypes = new DataType[0]; }
            public MetaTag(DataType type) { DTypes = new DataType[] { type }; }
            public MetaTag(DataType[] type) { DTypes = type; }
            public MetaTag(DataType[] types, int count, uint def_val)
            { DTypes = types; Count = count; _defult = def_val; }
            public MetaTag(DataType[] types, int count)
            { DTypes = types; Count = count; Count = count; }
            public MetaTag(DataType type, int count)
            { DTypes = new DataType[] { type }; Count = count; }
            public MetaTag(DataType type, int count, uint def_val)
            { DTypes = new DataType[] { type }; Count = count; Count = count; _defult = def_val; }

            public virtual Tag CreateAssocTag(TagID id, Tag tag)
            {
                throw new TiffNotImplementedException("Creating tags for this tag");
            }

            public virtual Tag CreateMemTag(Tag t, TiffStreamReader file, bool is_big_tiff)
            {
                throw new TiffNotImplementedException("Creating mem tags for this tag");
            }


            /// <summary>
            /// Writes out the data and retuns the lengths
            /// </summary>
            /// <param name="tw">Target</param>
            /// <returns>Counts</returns>
            public virtual KeyValuePair<DataType, ulong[]> Write(TiffWriter tw)
            {
                throw new TiffNotImplementedException("Writing out this tag type");
            }

            internal static DataType DetermineArSize(ulong max_size, TiffWriter tw)
            {
                if (max_size <= ushort.MaxValue)
                    return DataType.SHORT;
                if (max_size <= uint.MaxValue)
                    return DataType.LONG;
                if (!tw.BigTiff)
                    throw new TiffException("Too much data for non-big tiff file");
                return DataType.LONG8;
            }

            internal static DataType DetermineIFDSize(ulong max_size, TiffWriter tw)
            {
                if (max_size <= uint.MaxValue)
                    return DataType.IFD;
                if (!tw.BigTiff)
                    throw new TiffException("Too much data for non-big tiff file");
                return DataType.IFD8;
            }

            /// <summary>
            /// Calculates the total size of the tag
            /// </summary>
            internal virtual ulong Size(TiffWriter tw, TiffIFD img, Tag tag, ulong offset)
            {
                ulong size = tag.Count * (ulong)tag.TagSize;
                if (size <= tw.IFD_SIZE) return 0;
                if (offset % 2 != 0)
                    size++;
                return size;
            }

            public virtual object Default(TiffImage img)
            {
                return _defult;
            }

            public virtual ulong FixedCount(TiffImage img)
            {
                return (ulong) Count;
            }

            public bool Match(DataType d)
            {
                for (int c = 0; c < DTypes.Length; c++)
                    if (DTypes[c] == d) return true;
                return false;
            }

            /// <summary>
            /// Tags can point at other types of objects. This
            /// makes sure they're of the correct type.
            /// </summary>
            /// <param name="objects">Objects to check</param>
            /// <returns>If this tag support these objects</returns>
            public virtual bool Match(object[] objects)
            {
                return false;
            }
        }

        protected sealed class MetaIFD : MetaTag
        {
            TiffWriter.TagList[] _lists;
            ulong[] _offsets;
            DataType _dt;
            TiffIFD.IFDType _type;

            public override bool IsOffsetData { get { return true; } }
            internal MetaIFD(DataType t, int count, TiffIFD.IFDType type)
                : base(t, count)
            { _type = type; }
            internal MetaIFD(TiffIFD.IFDType type) 
                : base(new DataType[] { DataType.IFD, DataType.IFD8, DataType.LONG, DataType.LONG8 })
            { _type = type; }

            public override Tag CreateMemTag(Tag tag, TiffStreamReader file, bool is_big_tiff)
            {
                if (tag is MemTag) return tag;
                var idfs = tag.GetAsULongs();
                if (_type == TiffIFD.IFDType.Image)
                {
                    TiffImage[] images = new TiffImage[idfs.Length];
                    for (int c = 0; c < images.Length; c++)
                        images[c] = (TiffImage)TiffStream.ParseTiff((long)idfs[c], is_big_tiff, file, TiffIFD.IFDType.Image);
                    return new MemTag(tag.ID, tag.Type, (ulong)images.Length, images, tag.BigEndian);
                }
                else
                {
                    TiffExif[] exifs = new TiffExif[idfs.Length];
                    for (int c = 0; c < exifs.Length; c++)
                        exifs[c] = TiffExif.Create((long) idfs[c], file, is_big_tiff);
                    return new MemTag(tag.ID, tag.Type, (ulong)exifs.Length, exifs, tag.BigEndian);
                }
            }

            public override KeyValuePair<DataType, ulong[]> Write(TiffWriter tw)
            {
                //Writes out all the image data
                byte[] ifd = null;
                for (int c = 0; c < _lists.Length; c++)
                {
                    if (tw.Position % 2 != 0)
                        tw.WriteByte(0);

                    int ifd_pos = tw.Write(_lists[c].List, ref ifd);
                    tw.Write(ifd, 0, ifd_pos);

                    //There's never a "next image"
                    tw.WritePos(0);
                }

                return new KeyValuePair<DataType,ulong[]>(_dt, _offsets);
            }

            internal override ulong Size(TiffWriter tw, TiffIFD ifd, Tag tag, ulong offset)
            {
                var ifds = ifd.ParseIFDs(tag.ID, _type);
                _lists = new TiffWriter.TagList[ifds.Length];
                _offsets = new ulong[ifds.Length];
                ulong pos = offset;
                for (int c = 0; c < ifds.Length; c++)
                {
                    if (pos % 2 != 0) pos++;
                    var image = ifds[c];
                    if (image is TiffImage)
                        ((TiffImage)image).Repair();

                    var tl = _lists[c] = tw.CreateTagList(ifds[c], pos);
                    pos += tl.Size;

                    //Records the TFD position.
                    _offsets[c] = pos;

                    //Adds the size of the TFD
                    if (tw.BigTiff)
                        pos += (ulong)(8 + tl.List.Count * 20 + 8);
                    else
                        pos += (ulong)(2 + tl.List.Count * 12 + 4);
                }

                _dt = DetermineIFDSize(_offsets[_offsets.Length - 1], tw);
                var size = (ulong) (Tag.GetTagSize(_dt) * _offsets.Length);
                if (size > tw.IFD_SIZE)
                {
                    if (pos % 2 != 0)
                        pos++;
                    pos += size;
                }

                return pos - offset;
            }

            public override bool Match(object[] objects)
            {
                if (_type == TiffIFD.IFDType.Image)
                {
                    foreach (var obj in objects)
                        if (!(obj is TiffImage))
                            return false;
                }
                else if (_type == TiffIFD.IFDType.Exif)
                {
                    foreach (var obj in objects)
                        if (!(obj is TiffExif))
                            return false;
                }
                else
                {
                    foreach (var obj in objects)
                        if (!(obj is TiffIFD))
                            return false;
                }
                return true;
            }
        }

        protected sealed class MetaCSPP : MetaTag
        {
            public MetaCSPP(uint def)
                : base(DataType.SHORT, NO_COUNT, def)
            { }

            public override object Default(TiffImage img)
            {
                if (_defult == uint.MaxValue)
                {
                    var bps = img.BitsPerSample;
                    var r = new ushort[bps.Length];
                    for (int c = 0; c < bps.Length; c++)
                        r[c] = (ushort) (Math.Pow(2, bps[c]) - 1);
                    return r;
                }
                return base.Default(img);
            }

            public override ulong FixedCount(TiffImage img)
            {
                return img.SamplesPerPixel;
            }
        }

        protected class MetaStripBC : MetaTag
        {
            public override bool WriteTag { get { return false; } }
            public override TagID Replace { get { return TagID.StripOffsets; } }

            public MetaStripBC()
                : base(new DataType[] { DataType.SHORT, DataType.LONG, DataType.LONG8 }, NO_COUNT)
            { }

            public override ulong FixedCount(TiffImage img)
            {              
                uint rps = img.RowsPerStrip;
                if (rps != 0)
                    return (ulong) ((img.Height + rps - 1) / rps);
                return (ulong) Count;
            }
        }

        protected sealed class MetaStripOffsets : MetaStripBC
        {
            public override bool IsOffsetData { get { return true; } }
            public override bool WriteTag { get { return true; } }
            public override TagID Replace
            {
                get
                {
                    return (_wp == null || !_wp.ByteSwap) ? TagID.PROGRESSIVE : TagID.Compression;
                }
            }
            private WriteParams _wp;

            public override Tag CreateMemTag(Tag t, TiffStreamReader file, bool is_big_tiff)
            {
                return t;
            }

            public override Tag CreateAssocTag(TagID id, Tag tag)
            {
                if (id == TagID.Compression)
                    return new MemTag(id, DataType.SHORT, 1, (ulong)COMPRESSION_SCHEME.UNCOMPRESSED, tag.BigEndian);

                if (id != TagID.StripByteCounts)
                    throw new ArgumentException();

                return new MemTag(id, _wp.Counts_type, (ulong)_wp.TileSizes.Length, _wp.TileSizes.Clone(), tag.BigEndian);
            }

            internal override ulong Size(TiffWriter tw, TiffIFD ifd, Tag tag, ulong offset)
            {
                var img = (TiffImage)ifd;
                var data = img.RawContents;
                _wp = SizeImpl(tw, img, tag, data, offset);
                return _wp.TotalSize;
            }

            internal static WriteParams SizeImpl(TiffWriter tw, TiffImage img, Tag tag, TiffRawContents raw, ulong offset)
            {
                //Estimates the size of the data
                ulong last_offset = offset, total_size, max_size;
                var comp = img.Compression;
                var tile_sizes = raw.TileSizes;
                var wp = new WriteParams() { Raw = raw, Offset = offset };

                if (tag.BigEndian != tw.BigEndian && Util.ArrayHelper.Max(raw.BitsPerSample) > 8
                    && comp != COMPRESSION_SCHEME.JPEG && comp != COMPRESSION_SCHEME.JP_2000)
                {
                    if (img.PhotometricInterpretation == Photometric.YCbCr)
                    {
                        if (img.YCbCrSubSampling.Horizontal != SUBSAMPLING.None &&
                            img.YCbCrSubSampling.Vertical != SUBSAMPLING.None)
                            throw new TiffNotImplementedException("Byteswapping of subsampled YcCbCr");
                    }

                    wp.ByteSwap = true;
                    wp.Image = img;
                    tile_sizes = new ulong[tile_sizes.Length];
                    if (raw.Planar)
                    {
                        ulong last = 0;
                        max_size = 0;

                        //The bottom tiles need TLC, so we grab one
                        var bottom_tile = raw.GetTile(raw.NumberOfTiles - 1);

                        //Calculates for one component at a time
                        for (int c = 0, pos = 0; c < raw.BitsPerSample.Length; c++)
                        {
                            //Word aligns
                            if (last_offset % 2 != 0)
                                last_offset++;

                            //Calculates size of one byte[] in the tile
                            last = tile_sizes[pos++] = (ulong)((raw.BitsPerSample[c] * raw.TileWidth + 7) / 8 * raw.TileHeight);

                            //Finds the maxium size
                            max_size = Math.Max(last, max_size);

                            //Moves offset to the next tile
                            last_offset += last;

                            //If there's only one tile, it spans the full height of the image. 
                            if (raw.TilesDown == 1)
                                continue;

                            //The bottom tiles may be cut.
                            int n_fullsize_tiles = raw.NumberOfTiles - raw.TilesAcross, k = 1;

                            for (int j = 0; ; j++)
                            {
                                //Fills out the fullsized tiles
                                for (; k < n_fullsize_tiles; k++)
                                {
                                    //Word aligns
                                    if (last_offset % 2 != 0)
                                        last_offset++;

                                    tile_sizes[pos++] = last;

                                    //Moves offset to the next tile
                                    last_offset += last;
                                }
                                if (j == 1) break;

                                //Word aligns
                                if (last_offset % 2 != 0)
                                    last_offset++;

                                //Calculates size of bottom tile
#if PADD_BOTTOM_TILES
                                tile_sizes[pos++] = last;
#else
                                //last = tile_sizes[pos++] = (ulong)((raw.BitsPerSample[c] * raw.TileWidth + 7) / 8 * bottom_tile.Height);
#endif

                                //Moves offset to the next tile
                                last_offset += last;

                                //Fills out the rest
                                k = n_fullsize_tiles + 1;
                                n_fullsize_tiles = raw.NumberOfTiles;
                            }
                        }

                        total_size = last_offset - offset;
                        last_offset = total_size - last;
                    }
                    else
                    {
                        //Total and max size of the strips, except the bottom ones
                        ulong tile_size = max_size = (ulong) ((raw.BitsPerPixel * raw.TileWidth + 7) / 8 * raw.TileHeight);
                        for (int c = 0; c < tile_sizes.Length; c++)
                            tile_sizes[c] = tile_size;

                        //Word aligns the start offset
                        total_size = last_offset % 2 == 0 ? 0u : 1u;

                        //Adjust so that each offset starts on a word
                        if ((last_offset + total_size + tile_size) % 2 != 0) tile_size++;

                        //Last tile needs TLC.
                        ulong last_pos = (ulong)tile_sizes.Length - 1;

                        if (raw.TilesAcross == 1)
                        {
                            //Total size, except last tile
                            total_size += tile_size * last_pos;

                            //Get the tile size, calc it and add it.
                            var tile = raw.GetTile((int)last_pos);
                            tile_size = tile_sizes[last_pos] = (ulong)((raw.BitsPerPixel * raw.TileWidth + 7) / 8 * tile.Height);
                            total_size += tile_size;
                        }
                        else
                        {
                            //Total size, except bottom tiles
                            total_size += tile_size * (ulong) (tile_sizes.Length - raw.TilesAcross);

#if !PADD_BOTTOM_TILES
                            //Gets a bottom tile size, calc it.
                            var tile = raw.GetTile((int)last_pos);
                            tile_size = tile_sizes[last_pos] = (ulong)((raw.BitsPerPixel * raw.TileWidth + 7) / 8 * tile.Height);
#endif

                            //Updates the size array
                            for (int c = 0, pos = raw.TilesAcross * (raw.TilesDown - 1); c < raw.TilesAcross; c++)
                            {
                                tile_sizes[pos + c] = tile_size;
                                total_size += tile_size;
                            }

                            //Adjust so that each offset starts on a word
                            if (tile_size % 2 != 0)
                                total_size += (ulong)(raw.TilesAcross - 1);
                        }


                        //Offset of the last tile
                        last_offset += total_size - tile_size;
                        Debug.Assert(last_offset % 2 == 0);
                    }
                }
                else
                {
                    //Size of all tiles added together, including space for word alignement
                    total_size = 0;

                    //Maximum tile size
                    max_size = 0;

                    //Calculates offsets for each tile
                    for (int c = 0; c < tile_sizes.Length; c++)
                    {
                        //Adjust for word alignment
                        if (last_offset % 2 != 0)
                        {
                            total_size++;
                            last_offset++;
                        }

                        //The tile's size
                        var tile = tile_sizes[c];

                        //Finds the maximum size of a tile
                        max_size = Math.Max(tile, max_size);

                        //Offset to the next tile, if any
                        last_offset += tile;

                        //Adds the tile to the total
                        total_size += tile;
                    }
                    Debug.Assert(total_size == last_offset - offset);                    
                    
                    //Jumps one spot back, as last offset is pointing at a tile
                    //that don't exist.
                    last_offset -= (ulong) tile_sizes[tile_sizes.Length - 1];
                }
                
                wp.TileSizes = tile_sizes;
                wp.MaxSize = (uint)max_size;
                wp.TotalSize = total_size;

                //Estimates the size of the byte count array
                wp.Counts_type = DetermineArSize(max_size, tw);

                //Estimates the size of the offset array
                wp.Offset_type = DetermineArSize(last_offset, tw);
                total_size = (ulong) (Tag.GetTagSize(wp.Offset_type) * tile_sizes.Length);
                if (total_size > tw.IFD_SIZE)
                {
                    //Word aligns the offset pointer
                    if ((offset + wp.TotalSize) % 2 != 0)
                        wp.TotalSize++;

                    wp.TotalSize += total_size;
                }

                return wp;
            }

            public override KeyValuePair<DataType, ulong[]> Write(TiffWriter tw)
            {
                return new KeyValuePair<DataType, ulong[]>(_wp.Offset_type, WriteImpl(_wp, tw));
            }

            internal static ulong[] WriteImpl(WriteParams wp, TiffWriter tw)
            {
                var data = wp.Raw;
                ulong[] counts;
                byte[] buffer = new byte[wp.MaxSize];

                //When data > 8bpp is saved in a opposite endian mode, it
                //has to be decompressed and byteswapped. 
                if (wp.ByteSwap)
                {
                    //The lenghts of the compressed data
                    var compressed_lengths = data.TileSizes;

                    //Offset array for pointers at the data in the file
                    counts = new ulong[wp.TileSizes.Length];
                    
                    //Number of byte arrays for each tile
                    int n_data, r_data;
                    if (data.Planar)
                    {
                        n_data = data.SamplesPerPixel;
                        r_data = 1;
                    }
                    else
                    {
                        n_data = 1;
                        r_data = data.SamplesPerPixel;
                        if (!Util.ArrayHelper.Same(data.BitsPerSample, data.BitsPerSample[0]))
                        {
                            //I know I got a non-uniform chunky 2 planar algo somewhere (in TiffImage I think). Run that,
                            //byteswap and reassemble.

                            throw new TiffIsNonUniformException();
                        }
                    }

                    //How the data is compressed.
                    var comp = wp.Image.Compression;

                    //Offset position
                    ulong last = wp.Offset;

                    //Sizes of the decompressed data
                    var tile_sizes = wp.TileSizes;
                    var tiles = data.RawData;

                    //Go through each byte array in the tile.
                    for (int compno = 0, c = 0; compno < n_data; compno++)
                    {
                        for (int y = 0; y < data.TilesDown; y++)
                        {
                            for (int x = 0; x < data.TilesAcross; x++)
                            {
                                //Fetches the tile and its compressed data
                                var tile = tiles[x, y];

                                //Word aligns the offset
                                if (last % 2 != 0)
                                {
                                    last++;
                                    tw.WriteByte(0);
                                }

                                //Reccords the offset posittions
                                counts[c] = last;

                                //Decompresses the data
                                var raw_data = wp.Image.MakeStream(tile, compno, comp, data.BitsPerSample, r_data).DecodedStream;

                                //True length of the uncompressed data. Can be less than raw_data.length
                                ulong length = tile_sizes[c];
                                if (length > int.MaxValue)
                                    throw new TiffNotImplementedException("Lengths over 2GB");

                                //Padds data if it's too short
                                if (raw_data.Length < (int) length)
                                    Array.Resize<byte>(ref raw_data, (int)length);

                                //Moves pointer to the next tile (if any)
                                last += length;

                                //Checks if byte swapping is needed for this particular component.
                                int bpc = data.BitsPerSample[compno];
                                if (bpc > 8)
                                    raw_data = TiffImage.ByteSwap(raw_data, data.TileWidth, tile.Height, bpc);
                                tw.Write(raw_data, 0, (int)length);

                                //C is the strip number, or strip nr * ncomps in planar mode
                                c++;
                            }
                        }
                    }
                }
                else
                {
                    counts = (ulong[])data.TileSizes.Clone();

                    ulong last = wp.Offset, size;
                    for (int c = 0; c < counts.Length; c++)
                    {
                        if (last % 2 != 0)
                        {
                            last++;
                            tw.WriteByte(0);
                        }
                        size = counts[c];
                        counts[c] = last;
                        last += size;
                        data.WriteData(c, buffer);
                        tw.Write(buffer, 0, (int) size);
                    }
                }

                return counts;
            }

            internal class WriteParams
            {
                public ulong TotalSize, Offset;
                public bool ByteSwap;
                public TiffRawContents Raw;
                public TiffImage Image;
                public uint MaxSize;
                public ulong[] TileSizes;
                public DataType Counts_type, Offset_type;
            }
        }

        protected class MetaTileBC : MetaTag
        {
            public override bool WriteTag { get { return false; } }
            public override TagID Replace { get { return TagID.TileOffsets; } }

            public MetaTileBC()
                : base(new DataType[] { DataType.SHORT, DataType.LONG, DataType.LONG8 }, NO_COUNT)
            { }

            public override ulong FixedCount(TiffImage img)
            {
                return (ulong) (img.PlanarConfiguration == PixelMode.Chunky ? img.TilesPerImage :
                    img.SamplesPerPixel * img.TilesPerImage);
            }
        }

        protected sealed class MetaTileOffsets : MetaTileBC
        {
            public override bool IsOffsetData { get { return true; } }
            public override bool WriteTag { get { return true; } }
            public override TagID Replace
            {
                get
                {
                    return (_wp == null || !_wp.ByteSwap) ? TagID.PROGRESSIVE : TagID.Compression;
                }
            }
            private MetaStripOffsets.WriteParams _wp;

            internal override ulong Size(TiffWriter tw, TiffIFD ifd, Tag tag, ulong offset)
            {
                var img = (TiffImage)ifd;
                var data = img.RawContents;
                _wp = MetaStripOffsets.SizeImpl(tw, img, tag, data, offset);
                return _wp.TotalSize;
            }

            public override Tag CreateMemTag(Tag t, TiffStreamReader file, bool is_big_tiff)
            {
                return t;
            }

            public override Tag CreateAssocTag(TagID id, Tag tag)
            {
                if (id == TagID.Compression)
                    return new MemTag(id, DataType.SHORT, 1, (ulong)COMPRESSION_SCHEME.UNCOMPRESSED, tag.BigEndian);

                if (id != TagID.TileByteCounts)
                    throw new ArgumentException();

                return new MemTag(id, _wp.Counts_type, (ulong)_wp.TileSizes.Length, _wp.TileSizes.Clone(), tag.BigEndian);
            }

            public override KeyValuePair<DataType, ulong[]> Write(TiffWriter tw)
            {
                return new KeyValuePair<DataType, ulong[]>(_wp.Offset_type, MetaStripOffsets.WriteImpl(_wp, tw));
            }
        }

        protected sealed class MetaBPS : MetaTag
        {
            public MetaBPS()
                : base(DataType.SHORT, NO_COUNT)
            { }

            public override ulong FixedCount(TiffImage img)
            {
                return (ulong) Math.Pow(2, img.BitsPerSample[0]);
            }
        }

        protected sealed class MetaCM : MetaTag
        {
            public MetaCM()
                : base(DataType.SHORT, NO_COUNT)
            { }

            public override ulong FixedCount(TiffImage img)
            {
                return (ulong) (img.NComponents * (Math.Pow(2, img.BitsPerSample[0])));
            }
        }

        protected sealed class MetaDR : MetaTag
        {
            public MetaDR()
                : base(new DataType[] { DataType.BYTE, DataType.SHORT }, 2)
            { }

            public override object Default(TiffImage img)
            {
                var bps = img.BitsPerSample;
                if (Util.ArrayHelper.Same(bps, bps[0]))
                    return new ushort[] { 0, (ushort)(Math.Pow(2, bps[0]) - 1) };
                var ret = new ushort[bps.Length * 2];
                for (int c = 0, pos = 1; c < bps.Length; c++, pos += 2)
                    ret[pos] = (ushort)((1 << bps[c]) - 1);
                return ret;
            }
        }

        protected sealed class MetaSF : MetaTag
        {
            public MetaSF()
                : base(DataType.SHORT, NO_COUNT, 1)
            { }

            public override ulong FixedCount(TiffImage img)
            {
                return img.SamplesPerPixel;
            }
        }

        protected sealed class MetaYCC : MetaTag
        {
            public MetaYCC()
                : base(DataType.RATIONAL, 3)
            { }

            public override object Default(TiffImage img)
            {
                return new double[] { 299/1000d, 587/1000d, 114/1000d };
            }
        }

        protected sealed class MetaBW : MetaTag
        {
            public MetaBW()
                : base(DataType.RATIONAL, 6)
            { }

            public override object Default(TiffImage img)
            {
                if (img.PhotometricInterpretation == Photometric.YCbCr)
                    return new double[] { 0, 255, 128, 255, 128, 255 };
                var bps = img.BitsPerSample;
                if (bps.Length != 3)
                {
                    var NV = Math.Pow(2, img.BitsPerComponent) - 1;
                    return new double[] { 0, NV, 0, NV, 0, NV };
                }
                return new double[] { 0, Math.Pow(2, bps[0]) - 1, 0, Math.Pow(2, bps[1]) - 1, 0, Math.Pow(2, bps[2]) - 1 };
            }
        }

        sealed class MetaSFT : MetaTag
        {
            public override bool WriteTag { get { return false; } }
            public override TagID Replace { get { return TagID.SubfileType; } }
            public MetaSFT() : base(DataType.SHORT, 1) { }
            public override Tag CreateAssocTag(TagID id, Tag tag)
            {
                if (id != TagID.SubfileType) throw new ArgumentException();
                if (!tag.HasData) return null;
                ulong val = tag.GetSingleValue();
                if (val == 2)
                    val = 1;
                else if (val == 3)
                    val = 2;
                else return null;

                return new MemTag(TagID.NewSubfileType, DataType.LONG, 1, val, tag.BigEndian); 
            }
        }

        sealed class MetaNSFT : MetaTag
        {
            public override bool WriteTag { get { return false; } }
            public override TagID Replace { get { return TagID.NewSubfileType; } }
            public MetaNSFT() : base(DataType.LONG, 1, 0) { }
            public override Tag CreateAssocTag(TagID id, Tag tag)
            {
                if (id != TagID.NewSubfileType) throw new ArgumentException();
                if (!tag.HasData) return null;
                ulong val = tag.GetSingleValue() & 0xFFFFFD;
                if (val == 0)
                    return null;

                return new MemTag(TagID.NewSubfileType, DataType.LONG, 1, val, tag.BigEndian);
            }
        }

        #endregion
    }

    internal sealed class DiskTag : Tag
    {
        object _source;

        public override bool BigEndian
        {
            get 
            { 
                if (_source is FileSource)
                    return ((FileSource) _source).BigEndian;
                if (_source is ULongSource)
                    return ((ULongSource)_source).BigEndian;
                return ((RawSource)_source).IFD.BigEndian;
            }
        }
        public override ulong Count 
        { 
            get 
            {
                if (_count == 0 && _source is RawSource)
                    Init();
                return _count;
            } 
        }
        public override bool HasData
        {
            get 
            {
                if (_source is RawSource)
                    Init();
                if (_source is ULongSource)
                    return true;
                return ((FileSource)_source).HasData;
            }
        }

        public DiskTag(TagID id, DataType type, ulong count, ulong data, TiffIFD ifd)
            :base(id, type, count)
        {
            _source = new RawSource(ifd, data);
        }

        public override Tag LoadData()
        {
            if (_source is RawSource)
                Init();
            if (_source is ULongSource)
                return this;
            switch (TagSize)
            {
                case 1:
                    return new MemTag(ID, Type, _count, GetBytes(), BigEndian);
                case 2:
                    return new MemTag(ID, Type, _count, GetAsUShorts(), BigEndian);
                default:
                    return new MemTag(ID, Type, _count, GetAsULongs(), BigEndian);
            }
        }

        private bool GetBigEndian()
        {
            if (_source is ULongSource) return ((ULongSource)_source).BigEndian;
            return ((FileSource)_source).BigEndian;
        }

        /// <summary>
        /// Gets a single value.
        /// </summary>
        /// <returns></returns>
        public override ulong GetSingleValue()
        {
            if (_source is RawSource)
                Init();

            if (_source is ULongSource)
                return ((ULongSource)_source).Data;

            var fs = (FileSource)_source;
            var ba = fs.GetBytes();
            int ts = TagSize;
            if (ts > ba.Length)
                throw new TiffReadException("Not enough data");
            switch (TagSize)
            {
                case 8: return Util.StreamReaderEx.ReadULong(fs.BigEndian, 0, ba);
                case 4: return Util.StreamReaderEx.ReadUInt(fs.BigEndian, 0, ba);
                case 2: return Util.StreamReaderEx.ReadUShort(fs.BigEndian, 0, ba);
                case 1: return ba[0];
            }
            throw new TiffNotImplementedException("TagSize");
        }

        public override double? GetRealValue()
        {
            if (_source is RawSource)
                Init();

            var uints = GetBytes();
            if (uints.Length != 8)
                return null;
            var big = GetBigEndian();
            uint numerator = StreamReaderEx.ReadUInt(big, 0, uints);
            uint denominator = StreamReaderEx.ReadUInt(big, 4, uints);
            if (denominator != 0)
                return numerator / (double) denominator;

            return null;
        }

        public override double[] GetReals()
        {
            var uints = GetBytes();
            if (uints.Length < 8)
                return null;
            var big = GetBigEndian();
            var ret = new double[uints.Length / 8];
            for (int c = 0, pos = 0; c < ret.Length; c++)
            {
                uint numerator = StreamReaderEx.ReadUInt(big, pos, uints);
                pos += 4;
                uint denominator = StreamReaderEx.ReadUInt(big, pos, uints);
                pos += 4;
                if (denominator != 0)
                    ret[c] = numerator / (double) denominator;
            }
            return ret;
        }

        public override ushort[] GetAsUShorts()
        {
            var ba = GetBytes();
            uint size = (uint)TagSize;
            if (ba.Length == 0 || ba.Length < (int)(_count / size)) return null;
            var sa = new ushort[_count];
            bool be = GetBigEndian();
            switch (size)
            {
                case 2:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 2)
                        sa[sa_pos] = StreamReaderEx.ReadUShort(be, ba_pos, ba);
                    break;
                case 1:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos++)
                        sa[sa_pos] = ba[ba_pos];
                    break;
            }
            return sa;
        }

        public override ulong[] GetAsULongs()
        {
            var ba = GetBytes();
            uint size = (uint) TagSize;
            if (ba == null || ba.Length == 0 || ba.Length < (int)(_count / size)) return null;
            var sa = new ulong[_count];
            bool be = GetBigEndian();
            switch (size)
            {
                case 8:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 8)
                        sa[sa_pos] = StreamReaderEx.ReadULong(be, ba_pos, ba);
                    break;
                case 4:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 4)
                        sa[sa_pos] = StreamReaderEx.ReadUInt(be, ba_pos, ba);
                    break;
                case 2:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 2)
                        sa[sa_pos] = StreamReaderEx.ReadUShort(be, ba_pos, ba);
                    break;
                case 1:
                    for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos ++)
                        sa[sa_pos] = ba[ba_pos];
                    break;
            }
            return sa;
        }

        public override ulong[] GetULongs()
        {
            var ba = GetBytes();
            if (ba.Length == 0 || ba.Length < (int) (_count / 8)) return null;
            var sa = new ulong[_count];
            bool be = GetBigEndian();
            for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 8)
                sa[sa_pos] = StreamReaderEx.ReadULong(be, ba_pos, ba);
            return sa;
        }

        public override uint[] GetUInts()
        {
            var ba = GetBytes();
            if (ba.Length == 0 || ba.Length < (int)(_count / 4)) return null;
            var sa = new uint[_count];
            bool be = GetBigEndian();
            for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 4)
                sa[sa_pos] = StreamReaderEx.ReadUInt(be, ba_pos, ba);
            return sa;
        }

        public override ushort[] GetUShorts()
        {
            var ba = GetBytes();
            if (ba.Length == 0 || ba.Length < (int)(_count / 2)) return null;
            var sa = new ushort[_count];
            bool be = GetBigEndian();
            for (int sa_pos = 0, ba_pos = 0; sa_pos < sa.Length; sa_pos++, ba_pos += 2)
                sa[sa_pos] = StreamReaderEx.ReadUShort(be, ba_pos, ba);
            return sa;
        }

        public override byte[] GetBytes()
        {
            if (_source is RawSource)
                Init();

            if (_source is ULongSource)
            {
                var source = (ULongSource)_source;

                if (_count == 1)
                {
                    //When count is 1, data is stored as a number. So we
                    //have to treat this a bit different.

                    switch (TagSize)
                    {
                        case 1: return new byte[] { (byte)source.Data };
                        case 2: return Read.Lexer.GetBytes(source.BigEndian, (ushort)source.Data);
                        case 4: return Read.Lexer.GetBytes(source.BigEndian, (uint)source.Data);
                        //Not doing case 8. The code bellow handles it fine,
                        //and it might break something in non-big tiff files.
                    }
                }

                return GetBytes(source.BigTiff, source.BigEndian, source.Data);
            }
            else
            {
                return ((FileSource)_source).GetBytes();
            }
        }

        private static byte[] GetBytes(bool big, bool be, ulong value)
        {
            byte[] ba;
            if (big)
            {
                if (be)
                    ba = Read.Lexer.GetBytes(value);
                else
                    ba = Read.Lexer.GetBytesLE(value);
            }
            else
            {
                if (be)
                    ba = Read.Lexer.GetBytes((uint)value);
                else
                    ba = Read.Lexer.GetBytesLE((uint)value);
            }

            return ba;
        }
        public override object[] GetObjects()
        {
            throw new NotSupportedException();
        }

        private void Init()
        {
            var rs = (RawSource)_source;
            var idf = rs.IFD;
            if (_count == 0 && rs.IFD is TiffImage)
            {
                var meta = Meta;
                if (meta.Match(Type))
                    _count = meta.FixedCount((TiffImage)idf);
                if (_count == 1 && TagSize < idf.IFD_SIZE)
                {
                    //Simple data is stored parsed, so we got to reparse this.
                    bool be = idf.BigEndian;
                    byte[] raw = GetBytes(idf.BigTiff, be, rs.Data);
                    switch (Type)
                    {
                        //Does not execute on normal tiff files, since TagSize == idf.IDF_SIZE
                        case DataType.IFD:
                        case DataType.SLONG:
                        case DataType.LONG: rs.Data = PdfLib.Util.StreamReaderEx.ReadUInt(be, 0, raw); break;

                        case DataType.SSHORT:
                        case DataType.SHORT: rs.Data = PdfLib.Util.StreamReaderEx.ReadUShort(be, 0, raw); break;

                        case DataType.ASCII:
                        case DataType.BYTE:
                        case DataType.UNDEFINED: rs.Data = be ? raw[idf.BigTiff ? 7 : 3] : raw[0]; break;
                    }
                }
            }

            ulong size = (ulong) TagSize * _count;
            if (size <= idf.IFD_SIZE)
            {
                //Data is stored in Offset
                _source = new ULongSource(idf.BigEndian, idf.BigTiff, rs.Data);
            }
            else
            {
                //Data is stored in file
                _source = new FileSource(idf, (long) rs.Data, (int) size);
            }
        }

        private class ULongSource
        {
            public bool BigEndian, BigTiff;
            public ulong Data;
            public ULongSource(bool b, bool bt, ulong d) { BigEndian = b; BigTiff = bt; Data = d; }
        }

        private class FileSource
        {
            private TiffIFD file;
            private long offset;
            private int size;
            public bool BigEndian { get { return file.BigEndian; } }
            public bool HasData { get { return offset > 0; } }
            public FileSource(TiffIFD file, long offset, int size)
            { this.file = file; this.offset = offset; this.size = size; }
            public byte[] GetBytes()
            {
                if (offset == 0) return null;
                var ba = new byte[size];
                int read = file.ReadFromFile(offset, ba, 0, size);
                if (read == size) return ba;
                return null;
            }
        }

        private class RawSource
        {
            public TiffIFD IFD;
            public ulong Data;
            public RawSource(TiffIFD ifd, ulong data)
            { IFD = ifd; Data = data; }
        }
    }

    class MemTag : Tag
    {
        object _data;
        bool _big_endian;
        public override bool BigEndian { get { return _big_endian; } }
        public override ulong Count { get { return _count; } }
        public override bool HasData { get { return _data != null; } }

        public MemTag(TagID id, DataType type, ulong count, object data, bool big_endian)
            : base(id, type, count)
        { _data = data; _big_endian = big_endian; }

        public override Tag LoadData()
        {
            return this;
        }

        public override ushort[] GetUShorts()
        {
            if (_data is ushort[])
                return (ushort[])_data;
            if (_data is ulong) return new ushort[] { (ushort)_data };

            
            throw new NotSupportedException();
        }
        public override ushort[] GetAsUShorts()
        {
            if (_data is ushort[])
                return (ushort[])_data;
            if (_data is uint[])
                return Util.ArrayHelper.TransformToUshort((uint[])_data);
            if (_data is ulong[])
                return Util.ArrayHelper.TransformToUshort((ulong[])_data);
            if (_data is ulong) return new ushort[] { (ushort)_data };

            throw new NotSupportedException();
        }
        public override ulong[] GetULongs()
        {
            if (_data is ulong[])
                return (ulong[])_data;
            if (_data is ulong) return new ulong[] { (ulong)_data };

            throw new NotSupportedException();
        }
        public override ulong[] GetAsULongs()
        {
            if (_data is ulong[])
                return (ulong[])_data;
            if (_data is ushort[])
                return Util.ArrayHelper.TransformToULong((ushort[])_data);
            if (_data is uint[])
                return Util.ArrayHelper.TransformToULong((uint[])_data);
            if (_data is ulong) return new ulong[] { (ulong)_data };
            if (_data is double[])
                return PackIAinLA((RatToInts((double[])_data, this.Meta.DTypes[0])));
            if (_data is byte[])
                return Util.ArrayHelper.TransformToULong((byte[])_data);

            throw new NotSupportedException();
        }
        public override uint[] GetUInts()
        {
            if (_data is uint[])
                return (uint[])_data;
            if (_data is ulong) return new uint[] { (uint)_data };

            throw new NotSupportedException();
        }
        public override ulong GetSingleValue()
        {
            if (_data is ulong) return (ulong)_data;
            var ul = GetAsULongs();
            if (ul.Length > 0) return ul[0];
            throw new NotSupportedException();
        }
        public override double? GetRealValue()
        {
            if (_data is double) return (double)_data;
            if (_data is double[])
            {
                var da = (double[])_data;
                if (da.Length > 0) return da[0];
            }
            return null;
        }
        public override double[] GetReals()
        {
            if (_data is double) return new double[] { (double)_data };
            if (_data is double[])
                return (double[])_data;
            if (_data is ulong[])
            {
                var uints = GetBytes();
                var ret = new double[((ulong[])_data).Length];
                for (int c = 0, pos = 0; c < ret.Length; c++)
                {
                    uint numerator = StreamReaderEx.ReadUInt(_big_endian, pos, uints);
                    pos += 4;
                    uint denominator = StreamReaderEx.ReadUInt(_big_endian, pos, uints);
                    pos += 4;
                    if (denominator != 0)
                        ret[c] = numerator / (double)denominator;
                }
                return ret;
            }
            throw new NotSupportedException();
        }
        public override byte[] GetBytes()
        {
            if (_data is byte[]) return (byte[]) _data;
            if (_data is ushort[])
            {
                int ds = TagSize;
                var d = (ushort[])_data;
                byte[] data = new byte[d.Length * ds];
                switch (TagSize)
                {
                    case 1:
                        for (int c = 0; c < d.Length; c++)
                            data[c] = (byte)d[c];
                        break;
                    case 2:
                        for (int c = 0, pos = 0; c < d.Length; c++, pos += 2)
                            BWriter.Write(_big_endian, (ushort)d[c], pos, data);
                        break;
                }
                return data;
            }
            if (_data is double)
                return IAtoBytes(RatToInts(new double[] { (double)_data }, this.Meta.DTypes[0]), BigEndian);
            if (_data is double[])
                return IAtoBytes(RatToInts((double[])_data, this.Meta.DTypes[0]), BigEndian);
            if (_data is long) return new byte[] { (byte) _data };
            if (_data is ulong[])
            {
                int ds = TagSize;
                var d = (ulong[]) _data;
                byte[] data = new byte[d.Length * ds];
                switch (ds)
                {
                    case 1:
                        for (int c = 0; c < d.Length; c++)
                            data[c] = (byte)d[c];
                        break;
                    case 2:
                        for (int c = 0, pos = 0; c < d.Length; c++, pos += 2)
                            BWriter.Write(_big_endian, (ushort)d[c], pos, data);
                        break;
                    case 4:
                        for (int c = 0, pos = 0; c < d.Length; c++, pos += 4)
                            BWriter.Write(_big_endian, (uint)d[c], pos, data);
                        break;
                    case 8:
                        for (int c = 0, pos = 0; c < d.Length; c++, pos += 8)
                            BWriter.Write(_big_endian, d[c], pos, data);
                        break;
                }
                return data;
            }
            throw new NotSupportedException();
        }
        public override object[] GetObjects()
        {
            return (object[]) _data;
        }

        private ulong[] PackIAinLA(uint[] ia)
        {
            var la = new ulong[ia.Length / 2];
            Buffer.BlockCopy(ia, 0, la, 0, la.Length * 8);
            //for (int c = 0, pos = 0; c < la.Length; c++)
            //    la[c] = ia[pos++] << 32 | (uint)ia[pos++];
            return la;
        }

        private uint[] RatToInts(double[] data, DataType dt)
        {
            //Using the same approach as LibTiff. A simpler method is to do
            // ((uint) val) * 65535 / 65535
            //But this approach gives better precision.

            //Max is chosen so that VAL * MUL won't overflow MAX. I.e. that there's
            //enough room over max for 8 times the number
            const uint MAX = (1U << (32 - 3));

            //Multiplies the denominator and the value with this number until a
            //number larger than max is reached. 
            const uint MUL = 8;

            uint[] ia = new uint[data.Length * 2];
            for (int c = 0, pos = 0; c < data.Length; c++)
            {
                var val = data[c];
                bool negate;

                if (val < 0)
                {
                    if (dt == DataType.SRATIONAL)
                    {
                        negate = true;
                        val -= val;
                    }
                    else
                    {
                        //This is an error
                        negate = false;
                        val = 0;
                    }
                }
                else negate = false;

                uint dem = 1;
                if (val < dem)
                {
                    while (dem < MAX)
                    {
                        val *= MUL;
                        dem *= MUL;
                    }
                }
                else
                {
                    while (val < MAX)
                    {
                        val *= MUL;
                        dem *= MUL;
                    }
                }
                if (negate) val -= val;
                ia[pos++] = (uint)(val + .5);
                ia[pos++] = dem;
                //Debug.Assert(data[c] == ia[pos - 2] / (double)ia[pos - 1]);
            }

            return ia;
        }

        private byte[] IAtoBytes(uint[] data, bool be)
        {
            var ba = new byte[data.Length * 4];
            if (be != BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(data, 0, ba, 0, ba.Length);
                return ba;
            }
            if (be)
            {
                for (int c = 0, pos = 0; c < data.Length; c++, pos += 4)
                    BWriter.WriteBE(data[c], pos, ba);
            }
            else
            {
                for (int c = 0, pos = 0; c < data.Length; c++, pos += 4)
                    BWriter.WriteLE(data[c], pos, ba);
            }
            return ba;
        }
    }

    /// <summary>
    /// Helper class for working with tags
    /// </summary>
    /// <remarks>Default tags are automatically removed when saving</remarks>
    class TagList
    {
        SortedList<TagID, Tag> _tags;
        public readonly bool BigEndian;
        public SortedList<TagID, Tag> Tags { get { return _tags; } }
        public TagList(SortedList<TagID, Tag> tags, bool big_endian)
        {
            _tags = tags;
            BigEndian = big_endian;
        }
        public TagList(int capacity, bool big_endian)
        {
            _tags = new SortedList<TagID, Tag>(capacity);
            BigEndian = big_endian;
        }

        public static void SetObjects(SortedList<TagID, Tag> tags, TagID id, object[] values, bool be)
        {
            if (values == null)
                tags.Remove(id);
            else
            {
                var meta = Tag.GetMeta(id);
                if (!meta.Match(values))
                    throw new ArgumentException("Wrong object type");
                var dtypes = meta.DTypes;
                DataType dtype = dtypes[0];

                tags[id] = new MemTag(id, dtype, (ulong)values.Length, values, be);
            }
        }

        public void Set(TagID id, ulong value)
        {
            Set(_tags, id, value, BigEndian);
        }

        public static void Set(SortedList<TagID, Tag> tags, TagID id, ulong value, bool be)
        {
            var meta = Tag.GetMeta(id);
            var dtypes = meta.DTypes;
            DataType dtype = dtypes[0];
            if (dtypes.Length > 1)
            {
                int pos = 1;
                while (value > Tag.GetTagMax(dtype))
                {
                    if (pos == dtypes.Length)
                        //Perhaps log a warning instead?
                        throw new TiffNotImplementedException("Storing " + id + " value");
                    dtype = dtypes[pos++];
                }
            }

            tags[id] = new MemTag(id, dtype, 1, value, be);
        }

        public void Set(TagID id, ulong[] values)
        {
            Set(_tags, id, values, BigEndian);
        }

        public static void Set(SortedList<TagID, Tag> tags, TagID id, ulong[] values, bool be)
        {
            if (values == null)
                tags.Remove(id);
            else
            {
                var meta = Tag.GetMeta(id);
                var dtypes = meta.DTypes;
                DataType dtype = dtypes[0];
                if (dtypes.Length > 1)
                {
                    int pos = 1;
                    var value = Util.ArrayHelper.Max(values);
                    while (value > Tag.GetTagMax(dtype))
                    {
                        if (pos == dtypes.Length)
                            //Perhaps log a warning instead?
                            throw new TiffNotImplementedException("Storing " + id + " value");
                        dtype = dtypes[pos++];
                    }
                }

                tags[id] = new MemTag(id, dtype, (ulong)values.Length, values, be);
            }
        }

        public void Set(TagID id, double[] values)
        {
            Set(_tags, id, values, BigEndian);
        }

        public static void Set(SortedList<TagID, Tag> tags, TagID id, double[] values, bool be)
        {
            if (values == null)
                tags.Remove(id);
            else
            {
                var meta = Tag.GetMeta(id);
                var dtypes = meta.DTypes;
                DataType dtype = dtypes[0];
                if (dtype != DataType.RATIONAL && dtype != DataType.SRATIONAL)
                    throw new TiffException("Wrong data type");

                tags[id] = new MemTag(id, dtype, (ulong)values.Length, values, be);
            }
        }

        public void Set(TagID id, ushort[] values)
        {
            Set(_tags, id, values, BigEndian);
        }

        public static void Set(SortedList<TagID, Tag> tags, TagID id, ushort[] values, bool be)
        {
            if (values == null)
                tags.Remove(id);
            else
            {
                var meta = Tag.GetMeta(id);
                var dtypes = meta.DTypes;
                DataType dtype = dtypes[0];
                if (dtypes.Length > 1)
                {
                    int pos = 1;
                    var value = Util.ArrayHelper.Max(values);
                    while (value > Tag.GetTagMax(dtype))
                    {
                        if (pos == dtypes.Length)
                            //Perhaps log a warning instead?
                            throw new TiffNotImplementedException("Storing " + id + " value");
                        dtype = dtypes[pos++];
                    }
                }

                tags[id] = new MemTag(id, dtype, (ulong)values.Length, values, be);
            }
        }
    }
}
