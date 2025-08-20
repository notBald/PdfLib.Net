using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Compose.Font;

namespace PdfLib.Write
{
    /// <summary>
    /// For creating Type3 fonts from scratch
    /// </summary>
    public class WritableType3Font : PdfType3Font
    {
        #region Variables and properties

        TemporaryArray _encoding = new TemporaryArray();
        PdfItem[] _widths_ar;
        TemporaryDictionary _char_procs = new TemporaryDictionary();

        /// <summary>
        /// Number of symbol glyphs in this font
        /// </summary>
        int _n_symbols = 0, _precision = 3;

        /// <summary>
        /// If a "to unicode" map is needed
        /// </summary>
        PSCMap _to_unicode = null;

        /// <summary>
        /// Precision of the glyph drawing
        /// </summary>
        public int Precision { get { return _precision; } set { _precision = value; } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a blank Type 3 font
        /// </summary>
        /// <param name="font_bbox">
        /// This is the bounding box for all glypys. It's not automatically
        /// calculated, so if you are unsure set all values to zero.
        /// </param>
        /// <param name="font_matrix">The coordinate system for this font. Can be identity</param>
        public WritableType3Font(PdfRectangle font_bbox, xMatrix font_matrix)
            : base(new TemporaryDictionary())
        {
            _elems.SetType("Font");
            _elems.SetName("Subtype", "Type3");

            var enc_dict = new TemporaryDictionary();
            enc_dict.SetNewItem("Differences", _encoding, false);
            _elems.SetNewItem("Encoding", new PdfEncoding(enc_dict), false);
            _elems.SetInt("FirstChar", 0);
            _elems.SetInt("LastChar", 0);
            _widths_ar = new PdfItem[0];
            _elems.SetNewItem("Widths", new RealArray(_widths_ar), false);
            _elems.SetNewItem("CharProcs", _char_procs, false);
            _elems.SetNewItem("FontMatrix", font_matrix.ToArray(), false);
            _elems.SetNewItem("FontBBox", font_bbox, false);
        }

        #endregion

        /// <summary>
        /// Creates a colored symbol. Remeber to explicitly set any state used.
        /// </summary>
        /// <param name="id">Id of the glyph</param>
        /// <param name="width">Advance width</param>
        /// <param name="height">Advance height</param>
        /// <returns>A object for drawing the glyph. Remeber to dispose.</returns>
        public DrawPage CreateSymbol(byte id, double width)
        {
            var draw = new DrawPage(CreateGlyph(id, "S" + (++_n_symbols), width));
            draw.Precision = _precision;
            draw.SetT3Glyph(width, 0);
            if (_to_unicode != null)
            {
                _to_unicode.AddNextChar(id, 0, 1);
                _elems.SetItem("ToUnicode", PdfCmap.Create(_to_unicode.Compile(), _to_unicode.Name), true);
            }
            return draw;
        }

        /// <summary>
        /// Creates an uncolored symbol.
        /// </summary>
        /// <param name="id">Id of the glyph</param>
        /// <param name="width">Advance width</param>
        /// <param name="height">Advance height</param>
        /// <param name="box">Bounding box</param>
        /// <returns>A object for drawing the glyph. Remeber to dispose.</returns>
        public DrawPage CreateSymbol(byte id, double width, xRect box)
        {
            var draw = new DrawPage(CreateGlyph(id, "S" + (++_n_symbols), width));
            draw.Precision = _precision;
            draw.SetT3Glyph(width, 0, box.LowerLeft.X, box.LowerLeft.Y, box.UpperRight.X, box.UpperRight.Y);
            if (_to_unicode != null)
            {
                _to_unicode.AddNextChar(id, 0, 1);
                _elems.SetItem("ToUnicode", PdfCmap.Create(_to_unicode.Compile(), _to_unicode.Name), true);
            }
            return draw;
        }

        /// <summary>
        /// Creates an uncolored or colored unicode glyph
        /// </summary>
        /// <param name="ch">Unicode character</param>
        /// <param name="width">Advance width</param>
        /// <param name="box">
        /// Bounding box of character. Set null for a colored glyph.
        /// </param>
        /// <param name="ch_code">
        /// T3 fonts only supports up to 256 glyphs, unicode 65k. So some
        /// characters must be remapped.
        /// </param>
        /// <returns></returns>
        public DrawPage CreateUnicode(char ch, double width, xRect? box, out byte ch_code)
        {
            //Gets the unicode name of the glyph.
            string name;
            int id = (int)ch;
            if (!Enc.UnicodeToNames.TryGetValue(ch, out name))
            {
                //Simply creates a name if none exist.
                // One could use http://www.unicode.org/Public/UNIDATA/UnicodeData.txt here
                name = "U" + ((int)ch);
            }

            //Remaps if needed
            bool create_unicode = false;
            if (id >= byte.MaxValue || HasEncoding(id))
            {
                create_unicode = HasEncoding(id);

                //Finds an avalible slot.
                id = 1; //<-- Avoids 0
                while (id < 256 && HasEncoding(id))
                    id++;
                if (id == 256)
                    throw new PdfNotSupportedException("More than 255 glyphs in a T3 font");

            }
            ch_code = (byte) id;

            if (_to_unicode == null && (create_unicode || !Util.ArrayHelper.HasValue(Enc.Standar, name)))
            {
                _to_unicode = new PSCMap(true);
                _to_unicode.AddCodespaceRange(0, 255, 1);

                //Adds all existing characters. Todo: Symbolic characters (/S1, /S2, etc)
                //should be mapped to glyph 0.
                int ar_id = -2, l = 0;
                for (int c = 0; c < _encoding.Length; c++, l++)
                {
                    var itm = _encoding[c];
                    if (itm is PdfInt)
                    {
                        if (ar_id != -2)
                            _to_unicode.AddRange(ar_id, ar_id + l - 1, ar_id, 1);
                        ar_id = itm.GetInteger();
                        c++;
                        l = 1;
                    }
                    else
                        ar_id++; 
                }
                if (ar_id != -2 && l > 0)
                    _to_unicode.AddRange(ar_id, ar_id + l - 2, ar_id, 1);
            }

            if (_to_unicode != null)
            {
                _to_unicode.AddChar(ch_code, (int)ch, 1);
                _elems.SetItem("ToUnicode", PdfCmap.Create(_to_unicode.Compile(), _to_unicode.Name), true);
            }

            var draw = new DrawPage(CreateGlyph((byte)id, name, width));
            draw.Precision = _precision;
            if (box != null)
                draw.SetT3Glyph(width, 0, box.Value.LowerLeft.X, box.Value.LowerLeft.Y, box.Value.UpperRight.X, box.Value.UpperRight.Y);
            else
                draw.SetT3Glyph(width, 0);
            return draw;
        }

        /// <summary>
        /// Creates and inserts a new glyph
        /// </summary>
        private DrawGlyph CreateGlyph(byte id, string name, double width)
        {
            AddEncoding(id, name);
            SetWidth(id, width);
            var glyph = new WritableStream();
            _char_procs.SetItem(name, glyph, true);
            return new DrawGlyph(this, glyph);
        }

        private void SetWidth(int id, double width)
        {
            if (_widths_ar.Length == 0)
            {
                _elems.SetInt("FirstChar", id);
                _elems.SetInt("LastChar", id);
                _widths_ar = new PdfItem[1] { new PdfReal(width) };
                _elems.SetItem("Widths", new RealArray(_widths_ar), false);
            }
            else
            {
                //Enlarges the array.
                int first = FirstChar, last = LastChar;
                if (id < first) first = id;
                else if (id > last) { last = id; _elems.SetInt("LastChar", id); }
                int l = last - first + 1;
                if (l > _widths_ar.Length)
                {
                    var new_widths = new PdfItem[l];
                    var nil = new PdfReal(0);
                    for (int c = 0; c < new_widths.Length; c++)
                        new_widths[c] = nil;
                    Array.Copy(_widths_ar, 0, new_widths, FirstChar - first, _widths_ar.Length);
                    _widths_ar = new_widths;
                    _elems.SetItem("Widths", new RealArray(_widths_ar), false);
                }
                _elems.SetInt("FirstChar", first);

                _widths_ar[id - first] = new PdfReal(width);
            }
        }

        private bool HasEncoding(int id)
        {
            int ar_id = -2;
            for (int c = 0; c < _encoding.Length; c++)
            {
                var itm = _encoding[c];
                if (itm is PdfInt)
                {
                    ar_id = itm.GetInteger();
                    c++;
                }
                else
                    ar_id++;

                if (id == ar_id)
                    return true;
            }
            return false;
        }

        private void AddEncoding(int id, string name)
        {
            int ar_id = 0;
            int last_id = -2;
            bool adj_c = false;
            for (int c = 0; c < _encoding.Length; c++)
            {
                var itm = _encoding[c];
                if (itm is PdfInt)
                {
                    ar_id = itm.GetInteger();
                    c++;
                    adj_c = true;
                }
                else
                {
                    ar_id++;
                    adj_c = false;
                }

                if (id == ar_id)
                    throw new ArgumentException("A encoding with the same value already exists");
                if (id < ar_id)
                {
                    if (adj_c) c--;

                    if (id + 1 == ar_id)
                    {
                        //The id is right before an existing chain of ids
                        if (last_id + 1 == id)
                        {
                            //In this case we have a hole between two chains of
                            //id. We can simply replace the existing value then to
                            //create a single chain.
                            _encoding.Set(c, name);
                        }
                        else
                        {
                            //Decrement the existing id and add the name after.
                            _encoding.Set(c, id);
                            _encoding.Insert(c + 1, name);
                        }
                    }
                    else
                    {
                        _encoding.Insert(c, name);
                        if (last_id + 1 != id)
                            _encoding.Insert(c, id);
                    }
                    return;
                }

                last_id = ar_id;
            }

            //Item added to the end of the array.
            if (last_id + 1 != id)
                _encoding.Add(id);
            _encoding.Add(name);
        }

        #region Required overrides

        //protected override Elements MakeCopy(PdfDictionary elems)
        //{
        //    return new WritableType3Font();
        //}

        #endregion

        #region Draw glyph class

        private class DrawGlyph : IWSPage
        {
            private readonly WritableType3Font _parent;
            private readonly WritableStream _stream;

            public PdfResources Resources { get { return _parent.Resources; } }

            internal DrawGlyph(WritableType3Font parent, WritableStream glyph)
            {
                _parent = parent;
                _stream = glyph;
            }

            public void SetContents(byte[] contents) { _stream.RawStream = contents; }
        }

        #endregion
    }
}
