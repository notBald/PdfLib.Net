using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Read.TrueType
{
    internal class GlyfTable : Table, IEnumerable<Glyph>
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.glyf); } }

        readonly uint[] _offs;

        //If the last glyph "must have length" one can remove this. Only
        //used to check that the last glyph have length.
        readonly uint _end;

        #endregion

        #region Init

        public GlyfTable(TableDirectory td, LocaTable loca, uint end)
            : base(td)
        {
            _offs = loca.GlyfOffsets;
            _end = end;
        }

        #endregion

        #region Fetch glyph info

        /// <summary>
        /// Returns the position of this glyph in the file
        /// </summary>
        public uint GetGlyphPos(int index)
        {
            return _offs[index];
        }

        /// <summary>
        /// Returns parsed data for a glyph
        /// </summary>
        /// <param name="index">Glyph index</param>
        /// <returns>Parsed data</returns>
        public Glyph GetGlyph(int index)
        {
            if (index >= _offs.Length)
                return null;

            var offs = _offs[index++];
            uint len = ((index < _offs.Length) ? _offs[index] : _end) - offs;
            if (len <= 0) return null;

            var s = _td.Reader;
            s.Position = offs;

            short numberOfContours = s.ReadShort();
            if (numberOfContours < 0)
                 return new CompositeGlyphs(numberOfContours, s);

            return new SimpleGlyph(numberOfContours, s);
        }

        /// <summary>
        /// Returns the raw data for the glyph
        /// </summary>
        /// <param name="gid">Glyph index</param>
        /// <returns></returns>
        public byte[] GetGlyphData(int gid)
        {
            if (gid >= _offs.Length)
                return null;

            var offs = _offs[gid++];
            uint len = ((gid < _offs.Length) ? _offs[gid] : _end) - offs;
            if (len <= 0) return null;

            byte[] data = new byte[len];
            var s = _td.Reader;
            s.Position = offs;
            s.Read(data, 0, (int) len);
            return data;
        }

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator()
        { return GetEnumerator(); }

        public IEnumerator<Glyph> GetEnumerator()
        {
            return new GlyfEnumerator(_offs.Length, this);
        }

        class GlyfEnumerator : IEnumerator<Glyph>
        {
            readonly int _n_glyphs;
            readonly GlyfTable _parent;
            Glyph _current; int _pos;
            public GlyfEnumerator(int nglyphs, GlyfTable parent)
            { _n_glyphs = nglyphs; _parent = parent; }
            object IEnumerator.Current { get { return _current; } }
            public Glyph Current { get { return _current; } }
            public bool MoveNext()
            {
                if (_pos == _n_glyphs) return false;
                _current = _parent.GetGlyph(_pos++);
                return true;
            }
            public void Reset()
            {
                _pos = 0;
                _current = null;
            }
            public void Dispose() { }
        }

        #endregion
    }

    public abstract class Glyph
    {
        /// <summary>
        /// Min/Max coordinate data
        /// </summary>
        public readonly short XMin, YMin, XMax, YMax;

        public readonly short NumberOfContours;

        public Glyph(short n_contures, StreamReaderEx s)
        {
            NumberOfContours = n_contures;
            XMin = s.ReadShort();
            YMin = s.ReadShort();
            XMax = s.ReadShort();
            YMax = s.ReadShort();
        }
    }

    internal sealed class SimpleGlyph : Glyph
    {
        /// <summary>
        /// The last point of each contour
        /// </summary>
        public readonly ushort[] EndPtsOfContours;

        /// <summary>
        /// The flags for each point
        /// </summary>
        public readonly Flag[] Flags;

        /// <summary>
        /// Point coordinates
        /// </summary>
        public readonly short[] XCoords, YCoors;

        /// <summary>
        /// Number of points in the glyph
        /// </summary>
        public int NumPoints { get { return Flags.Length; } }

        public SimpleGlyph(short n_contures, StreamReaderEx s)
            : base((short)(n_contures), s)
        {
            EndPtsOfContours = new ushort[n_contures];
            for (int c = 0; c < EndPtsOfContours.Length; c++)
                EndPtsOfContours[c] = s.ReadUShort();

            //Skipping instructions for now
            ushort instructionLength = s.ReadUShort();
            s.Position += instructionLength;

            // The number of points in a glyph equals the index
            // of the last end point
            int n_points = EndPtsOfContours[n_contures - 1] + 1;
            Flags = new Flag[n_points];

            //Reads all the flags, checking for the repeat flag 
            //along the way
            for (int c = 0; c < n_points; c++)
            {
                var flag = (Flag) s.ReadByte();
                Flags[c] = flag;
                if ((flag & Flag.repeat) != 0)
                {
                    int n_reps = s.ReadByte();
                    for (int i = 0; i < n_reps; i++)
                        Flags[++c] = flag;
                }
            }

            //Reads in the points (Note: point positions are relative)
            XCoords = new short[n_points];
            
            //Keeps the last position 
            short last_pos = 0;
            for (int c = 0; c < n_points; c++)
            {
                var flag = Flags[c];
                if ((flag & Flag.xShortVector) != 0)
                {
                    if ((flag & Flag.XDis) != 0)
                        last_pos = (short)s.ReadByte();
                    else
                        last_pos = (short)(-s.ReadByte());
                }
                else
                {
                    if ((flag & Flag.XDis) == 0)
                        last_pos = s.ReadShort();
                    else
                        //xShortVector = 0, xdis = 0 means
                        //that the value is to be repeated
                        last_pos = 0;
                }
                XCoords[c] = last_pos;
            }

            YCoors = new short[n_points];
            for (int c = 0; c < n_points; c++)
            {
                var flag = Flags[c];
                if ((flag & Flag.yShortVector) != 0)
                {
                    if ((flag & Flag.YDis) != 0)
                        last_pos = (short)s.ReadByte();
                    else
                        last_pos = (short)(-s.ReadByte());
                }
                else
                {
                    if ((flag & Flag.YDis) == 0)
                        last_pos = s.ReadShort();
                    else
                        last_pos = 0;
                }
                YCoors[c] = last_pos;
            }
        }

        public enum Flag : byte
        {
            onCurve = 0x01,
            xShortVector = 0x02,
            yShortVector = 0x04,

            /// <summary>
            /// If set, the next byte specifies the number of 
            /// additional times this set of flags is to be 
            /// repeated.
            /// </summary>
            repeat = 0x08,

            /// <summary>
            /// This flag has two meanings
            /// XShortVector == 1: This bit is the sign bit
            /// XShortVector == 0: 
            ///  XDis == 1: Current x-coord is the same as 
            ///             the previous x-coord
            ///  XDis == 0: The current x-coord is a signed 
            ///             16-bit delta vector
            /// </summary>
            XDis = 0x10,
            YDis = 0x20,
        }
    }

    internal sealed class CompositeGlyphs : Glyph
    {
        List<CGlyph> _cglyps = new List<CGlyph>();

        /// <summary>
        /// Number of glyphs in this composition
        /// </summary>
        public int Count
        {
            get { return _cglyps.Count; }
        }

        /// <summary>
        /// List of glyphs
        /// </summary>
        public List<CGlyph> Glyphs { get { return _cglyps; } }

        public CompositeGlyphs(short n_contures, StreamReaderEx s)
            : base((short)(n_contures * -1), s)
        {
            CGlyph glyph;
            do
            {
                glyph = new CGlyph(s);
                _cglyps.Add(glyph);

            } while ((glyph.Flags & Flag.MORE_COMPONENTS) != 0);

            //Not bothering with instructions
            //if ((glyph.Flags & Flag.WE_HAVE_INSTRUCTIONS) != 0)
            //{
	        //    int n_instructions = s.ReadUShort();
                
            //}
        }

        /// <summary>
        /// A single composite glyph
        /// </summary>
        internal class CGlyph
        {
            public readonly Flag Flags;
            public readonly ushort GlyphIndex;

            /// <summary>
            /// This is the offset into the file,
            /// used for updating the glyph id
            /// </summary>
            public readonly int Offset;

            public readonly double Xscale = 1.0; //a
            public readonly double Yscale = 1.0; //d
            public readonly double Scale01 = 0.0; //b
            public readonly double Scale10 = 0.0; //c
            public readonly short OffsetX = 0; //e
            public readonly short OffsetY = 0; //f
            public readonly int point1 = 0;
            public readonly int point2 = 0;

            /// <summary>
            /// Transform associated with this Composite Glyph
            /// </summary>
            /// <remarks>
            /// http://developer.apple.com/fonts/TTRefMan/RM06/Chap6glyf.html
            /// </remarks>
            public xMatrix Transform
            {
                get
                {
                    double a_xscale = Math.Abs(Xscale);
                    double a_scale10 = Math.Abs(Scale10);

                    double offx_scale = Math.Max(a_xscale, Math.Abs(Scale01));
                    if (Math.Abs(a_xscale - a_scale10) < (33 / 65536))
                        offx_scale *= 2;

                    double a_yscale = Math.Abs(Yscale);

                    double offy_scale = Math.Max(a_scale10, a_yscale);
                    if (Math.Abs(a_scale10 - a_yscale) < (33 / 65536))
                        offy_scale *= 2;

                    return new xMatrix(Xscale, Scale01, Scale10, Yscale, offx_scale * OffsetX, offy_scale * OffsetY);
                }
            }

            public CGlyph(StreamReaderEx s)
            {
                Flags = (Flag)s.ReadUShort();
                Offset = (int) s.Position;
                GlyphIndex = s.ReadUShort();
                short argument1, argument2;

                //Reads out arguments as bytes or shorts
                if ((Flags & Flag.ARG_1_AND_2_ARE_WORDS) != 0)
                {
                    argument1 = s.ReadShort();
                    argument2 = s.ReadShort();
                }
                else
                {
                    //Note: The byte values are signed. 
                    argument1 = s.ReadSByte();
                    argument2 = s.ReadSByte();
                }

                //Sets the values accordance with the type
                if ((Flags & Flag.ARGS_ARE_XY_VALUES) != 0)
                {
                    OffsetX = argument1;
                    OffsetY = argument2;
                }
                else
                {
                    point1 = argument1;
                    point2 = argument2;
                }

                //Reads out the scale factors
                //The numbers (stored as shorts) are treated as signed fixed 
                //binary point numbers with one bit to the left of the binary 
                //point and 14 to the right.
                if ((Flags & Flag.WE_HAVE_A_SCALE) != 0)
                {
                    Xscale = s.ReadShort() / 16384d;
                    Yscale = Xscale;
                }
                else if ((Flags & Flag.WE_HAVE_AN_X_AND_Y_SCALE) != 0)
                {
                    Xscale = s.ReadShort() / 16384d;
                    Yscale = s.ReadShort() / 16384d;
                }
                else if ((Flags & Flag.WE_HAVE_A_TWO_BY_TWO) != 0)
                {
                    Xscale = s.ReadShort() / 16384d;
                    Scale01 = s.ReadShort() / 16384d;
                    Scale10 = s.ReadShort() / 16384d;
                    Yscale = s.ReadShort() / 16384d;
                }
            }
        }

        [Flags]
        internal enum Flag : ushort
        {
            /// <summary>
            /// If set, the arguments are words; 
            /// otherwise, they are bytes.
            /// </summary>
            ARG_1_AND_2_ARE_WORDS = 0x1,
            /// <summary>
            /// If set, the arguments are xy values; 
            /// otherwise, they are points
            /// </summary>
            ARGS_ARE_XY_VALUES = 0x2,
            /// <summary>
            /// For the xy values if the preceding 
            /// is true
            /// </summary>
            ROUND_XY_TO_GRID = 0x4,
            /// <summary>
            /// FThis indicates that there is a simple 
            /// scale for the component. 
            /// Otherwise, scale = 1.0
            /// </summary>
            WE_HAVE_A_SCALE = 0x8,
            /// <summary>
            /// Indicates at least one more glyph 
            /// after this one
            /// </summary>
            MORE_COMPONENTS = 0x20,
            /// <summary>
            /// The x direction will use a different 
            /// scale from the y direction.
            /// </summary>
            WE_HAVE_AN_X_AND_Y_SCALE = 0x40,
            /// <summary>
            /// There is a 2 by 2 transformation that 
            /// will be used to scale the component
            /// </summary>
            WE_HAVE_A_TWO_BY_TWO = 0x80,
            /// <summary>
            /// Following the last component are 
            /// instructions for the composite 
            /// character
            /// </summary>
            WE_HAVE_INSTRUCTIONS = 0x100,
            /// <summary>
            /// If set, this forces the aw and lsb 
            /// (and rsb) for the composite to be 
            /// equal to those from this original 
            /// glyph
            /// </summary>
            USE_MY_METRICS = 0x200
        }
    }
}
