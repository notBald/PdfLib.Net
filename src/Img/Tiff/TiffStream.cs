using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Img.Tiff.Internal;

namespace PdfLib.Img.Tiff
{
    /// <summary>
    /// For reading and creating Tiff files, one image at the time.
    /// </summary>
    public class TiffStream : IEnumerable<TiffImage>, IDisposable
    {
        readonly bool _is_big_tiff;
        bool _dispose_stream, _demand_load;
        protected TiffStreamReader _file;
        long _first_idf;

        internal TiffStream(bool is_big_tiff, long idf, TiffStreamReader file, bool dispose_stream, bool demand_load)
        {
            _is_big_tiff = is_big_tiff;
            _first_idf = idf;
            _file = file;
            _dispose_stream = dispose_stream;
            _demand_load = demand_load;
        }

        /// <summary>
        /// Reads out one tiff image
        /// </summary>
        /// <param name="idf">Offset to image's descriptor</param>
        /// <param name="big">is this a big tiff</param>
        /// <param name="file">Source file</param>
        /// <returns>A new Tiff Image</returns>
        internal static TiffIFD ParseTiff(long idf, bool big, TiffStreamReader file, TiffIFD.IFDType type)
        {
            try
            {
                if (big)
                    return ParseTiff(idf, (int)file.ReadLong(idf), big, file, type);
                else
                    return ParseTiff(idf, file.ReadUShort(idf), big, file, type);
            }
            catch (Exception)
            { return null; }
        }

        private static TiffIFD ParseTiff(long idf, int count, bool big, TiffStreamReader file, TiffIFD.IFDType type)
        {
            if (idf < 8)
                return null;

            //Reads out all tags in one go
            int size;
            long position;
            if (big)
            {
                size = count * 20;
                position = idf + 8;
            }
            else
            {
                size = (int)count * 12;
                position = idf + 2;
            }

            byte[] tags = new byte[size];
            file.ReadEx(position, tags, 0, size);

            TiffIFD img;
            if (type == TiffIFD.IFDType.Image)
                img = new TiffImage(file, big);
            else
                img = new TiffExif(file, big);
            img.Tags = CreateTags(count, tags, 0, img);
            return img;
        }

        internal static SortedList<TagID, Tag> CreateTags(int tag_count, byte[] raw_tags, int offset, TiffIFD ifd)
        {
            SortedList<TagID, Tag> tags = new SortedList<TagID, Tag>(tag_count);
            bool big = ifd.BigTiff, big_e = ifd.BigEndian;
            if (big)
            {
                for (int c = offset; c < raw_tags.Length; c += 20)
                {
                    var id = (TagID)PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c, raw_tags);
                    var type = (DataType)PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c + 2, raw_tags);
                    var count = PdfLib.Util.StreamReaderEx.ReadULong(big_e, c + 4, raw_tags);
                    if (count == 0)
                    {
                        //There are tiff files with count set to zero, yet with data. It appears it's
                        //a common enough error. This could cause trouble for some data types (rational 
                        //for instance), but I don't know what else to do.
                        //
                        //Perhaps keep it zero for DOUBLE, RATIONAL and SRATIONAL? I figure, if there
                        //is something in data, it's probably an valid offset even in case of those 
                        //datatypes.
                        //if (hasdata /* havent read the data yet */)
                        //    count = 1;

                        //Problem: In some cases, count is to be different from 1. Now we handle this
                        //situation later, when the count number can be properly estimated.
                    }
                    ulong data;
                    if (count == 1)
                    {
                        switch (type)
                        {
                            case DataType.IFD:
                            case DataType.SLONG:
                            case DataType.LONG: data = PdfLib.Util.StreamReaderEx.ReadUInt(big_e, c + 12, raw_tags); break;
                            case DataType.SSHORT:
                            case DataType.SHORT: data = PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c + 12, raw_tags); break;
                            case DataType.ASCII:
                            case DataType.BYTE:
                            case DataType.UNDEFINED: data = big_e ? raw_tags[c + 12 + 7] : raw_tags[c + 12]; break;
                            default: data = PdfLib.Util.StreamReaderEx.ReadULong(big_e, c + 12, raw_tags); break;
                        }
                    }
                    else
                        data = PdfLib.Util.StreamReaderEx.ReadULong(big_e, c + 12, raw_tags);
                    tags[id] = new DiskTag(id, type, count, data, ifd);
                }
            }
            else
            {
                for (int c = offset; c < raw_tags.Length; c += 12)
                {
                    var id = (TagID)PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c, raw_tags);
                    var type = (DataType)PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c + 2, raw_tags);
                    var count = PdfLib.Util.StreamReaderEx.ReadUInt(big_e, c + 4, raw_tags);
                    ulong data;
                    if (count == 1)
                    {
                        switch (type)
                        {
                            case DataType.SSHORT:
                            case DataType.SHORT: data = PdfLib.Util.StreamReaderEx.ReadUShort(big_e, c + 8, raw_tags); break;
                            case DataType.ASCII:
                            case DataType.BYTE:
                            case DataType.UNDEFINED: data = big_e ? raw_tags[c + 8 + 3] : raw_tags[c + 8]; break;
                            default: data = PdfLib.Util.StreamReaderEx.ReadUInt(big_e, c + 8, raw_tags); break;
                        }
                    }
                    else
                        data = PdfLib.Util.StreamReaderEx.ReadUInt(big_e, c + 8, raw_tags);
                    tags[id] = new DiskTag(id, type, count, data, ifd);
                }
            }

            return tags;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<TiffImage>)this).GetEnumerator();
        }

        IEnumerator<TiffImage> IEnumerable<TiffImage>.GetEnumerator()
        {
            return new ImgEnumerator(_file, _is_big_tiff, _demand_load, _first_idf).GetEnumerator();
        }

        public void Dispose()
        {
            if (_dispose_stream)
                _file.Dispose();
        }

        /// <summary>
        /// Itterates through the Tiff file
        /// </summary>
        private class ImgEnumerator
        {
            public long _first_idf, _current_idf, _current_count;
            TiffStreamReader _file;
            bool _is_big_tiff, _demand_load;

            /// <summary>
            /// A TIFF file can be constructed to loop. We therefor check if
            /// a image has been visited already, and if so, we stop.
            /// </summary>
            HashSet<long> _visited = new HashSet<long>();

            public ImgEnumerator(TiffStreamReader file, bool is_big_tiff, bool demand_load, long first_idf)
            {
                _file = file;
                _is_big_tiff = is_big_tiff;
                _first_idf = first_idf;
                _demand_load = demand_load;
            }

            public IEnumerator<TiffImage> GetEnumerator()
            {
                Reset();
                while (MoveNext())
                {
                    TiffImage img;
                    try
                    {
                        img = TiffStream.ParseTiff(_current_idf, (int)_current_count, _is_big_tiff, _file, TiffIFD.IFDType.Image) as TiffImage;
                        if (!img.IsValid) yield break;
                        if (!_demand_load)
                            img.LoadIntoMemory();

                        //If the image return is null, this will throw a NullPointerException, which is fine.
                        //if ((img.NewSubfileType & ImageType.Page) == 0)
                            //_current_idf = 0;
                    }
                    catch (Exception) { yield break; }
                    yield return img;
                }
            }

            public bool MoveNext()
            {
                if (_current_idf < 0)
                {
                    _current_idf = _first_idf;
                    _current_count = _is_big_tiff ? _file.ReadLong(_first_idf) : _file.ReadUShort(_first_idf);
                }
                else
                    SetCurrentIDF();
                return _current_idf != 0;
            }

            private void SetCurrentIDF()
            {
                if (_current_idf == 0)
                    return;
                try
                {
                    long next_idf;
                    if (_is_big_tiff)
                    {
                        next_idf = _file.ReadLong(_current_idf + _current_count * 20 + 8);
                    }
                    else
                    {
                        next_idf = _file.ReadUInt(_current_idf + _current_count * 12 + 2);
                    }

                    _current_idf = Math.Max(0, next_idf);
                    if (_visited.Contains(_current_idf))
                        _current_idf = 0;
                    _visited.Add(_current_idf);

                    if (_current_idf > 0)
                    {
                        if (_is_big_tiff)
                            _current_count = _file.ReadLong(_current_idf);
                        else
                            _current_count = _file.ReadUShort(_current_idf);
                    }
                }
                catch (Exception)
                {
                    _current_idf = 0;
                }
            }

            public void Reset()
            {
                _visited.Clear();
                _current_idf = -1;
            }
        }
    }
}
