using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.PostScript;
using PdfLib.PostScript.Font;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    internal class Type1Font<Path> : rFont
    {
        #region Variables

        /// <summary>
        /// Font data
        /// </summary>
        protected PSFont _fd;

        protected readonly string[] _names;

        protected IFontFactory<Path> _factory;

        #endregion

        #region Properties

        public override bool Vertical
        {
            //Todo: support vertical Type1 fonts
            get { return false; }
        }

        internal sealed override IFontFactory FontFactory => _factory;

        #endregion

        #region Init and Dispose

        internal Type1Font(double[] widths, PdfFontDescriptor desc, PdfEncoding enc, IFontFactory<Path> factory)
            : base(widths)
        {
            _factory = factory;

            //Parses the font
            var lex = new PSLexer(desc.FontFile.Stream.DecodedStream);
            //System.IO.File.WriteAllBytes(@"c:\temp\font.txt", desc.FontFile.Stream.DecodedStream);
            var psi = new PSInterpreter();
            psi.Run(lex);

            //Gets the font out of the PostScript interpreter
            var fd = psi.FontDictionary;
            if (!fd.TryGetValue(desc.FontName, out _fd))
            {
                string shortened =desc.FontName;
                int pluss = shortened.IndexOf('+') + 1;
                if (pluss > 1 && shortened.Length > pluss)
                {
                    shortened = shortened.Substring(pluss);
                    foreach (var kp in fd.Enum)
                    {
                        string long_name = kp.Key;
                        pluss = long_name.IndexOf('+');
                        if (pluss > 0)
                        {
                            string short_name = long_name.Substring(pluss + 1);
                            if (shortened == short_name)
                            {
                                _fd = kp.Value;
                                break;
                            }
                        }
                    }
                }
                if (_fd == null)
                    throw new PSReadException(PSType.Keyword, ErrCode.Missing);
            }

            //Updates decryption information
            _fd.CharStrings.lenIV = _fd.PrivateDict.lenIV;
            var subsr = _fd.PrivateDict.Subrs;
            if (subsr != null) subsr.lenIV = _fd.PrivateDict.lenIV;


            //Builds up a character encoding array
            if (enc != null)
            {
                //var dest_enc = _fd.Encoding.ToArray(true);
                //_names = enc.CreateDifferences(dest_enc);

                //From the specs
                //"A font program’s built-in encoding may be overridden by including an 
                //Encoding entry in the PDF font dictionary."
                //
                //From this I assume one should ignore the internal encoding when there's
                //a encoding dictionary.
                _names = desc.IsSymbolic ? enc.SymbolicDifferences : enc.StdMergedEncoding;
            }
            else
                //When there's no encoding we use the built in encoding
                _names = _fd.Encoding.ToArray(true);
        }

        protected Type1Font(CIDFontType0 font, IFontFactory<Path> factory)
            : this(null, font.FontDescriptor, null, factory)
        { }

        /// <summary>
        /// Init is always called before a font is to be used. Note that init can be called
        /// after dispose when a font is to be reused
        /// </summary>
        protected sealed override void InitImpl()
        {

        }

        public sealed override void Dismiss()
        {

        }

        public sealed override void DisposeImpl()
        {

        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            //Gets the character's name string
            var name = /*(_names == null) ? _fd.Encoding[ch] :*/ _names[ch];

            //Todo: Should set up a CID to GID table instead of using the names.
            // (Though string comparisons in C# are pretty fast)

            //Gets the glyph program
            byte[] glyph = _fd.CharStrings[name];

            //Runs the glyph program
            var gp = new GlyphParser(_fd, _factory.GlyphRenderer());

            var sg = gp.Execute(glyph);

            //Todo:
            //The glyph's width may differ from the width in the PDF.
            //In that case, scale the glyph to the width by first
            //transforming the glyph's width (_wd.X) through the FontMatrix
            //and get the scale by computing
            //"pdf width (_widths[ch]" / "transformed glyph's width"
            var fm = _fd.FontMatrix;
            
            var g = _factory.CreateGlyph(new Path[] { sg }, new xPoint(_widths[ch] / 1000, 0), new xPoint(0, 0), fm, true, is_space);


            return g;
        }

        internal class GlyphParser
        {
            #region Variables

            /// <summary>
            /// Last point
            /// </summary>
            xPoint _lp;

            /// <summary>
            /// Width point
            /// </summary>
            xPoint _wp;

            /// <summary>
            /// Left side bearing point
            /// </summary>
            xPoint _lsb;

            IGlyph<Path> _glyph_creator;
            IGlyphDraw _sgc;

            #region Stack implementation

            /// <summary>
            /// Parameter stack
            /// </summary>
            /// <remarks>
            /// For many commands the arguments are to be taken from
            /// the bottom of the stack, i.e. the stack functions as
            /// a queue.
            /// 
            /// Neither the generic Stack or Queue classes are suited
            /// for this, and my IndexStack wasn't a perfect fit 
            /// either, so instead I do this manually. 
            /// </remarks>
            float[] _stack = new float[8];
            int _stack_pos = -1;
            //IndexStack<float> _stack = new IndexStack<float>(8);

            float Pop() { return _stack[_stack_pos--]; }
            void Push(float val)
            {
                if (++_stack_pos == _stack.Length)
                    Array.Resize<float>(ref _stack, _stack_pos + 1);
                _stack[_stack_pos] = val;
            }
            void Clear() { _stack_pos = -1; }

            #endregion

            /// <summary>
            /// Font being rendered
            /// </summary>
            readonly PSFont _font;

            /// <summary>
            /// Used for executing embeded postscript
            /// </summary>
            PSInterpreter _ps = null;

            /// <summary>
            /// Whenever this font features the flex feature or not
            /// </summary>
            readonly bool _flex_font;

            /// <summary>
            /// For when adding flex curves to a glyph
            /// </summary>
            int _n_flex_points, _n_off_points;

            /// <summary>
            /// Off curve flex points
            /// </summary>
            /// <remarks>
            /// Collects up the off curve points so that one have
            /// enough for the BezierTo function.
            /// 
            /// Alternatively one could draw using QuadraticBezierTo
            /// by creating virtual points. Though it looks like flex
            /// curves always have enough off points for BezierTo*
            /// 
            /// Persudocode:
            /// virtual_on_point = new_off_point - old_off_point
            /// draw.QuadraticBezierTo(last_p, old_off, virt_on)
            /// old_off_point = new_off_point
            /// 
            /// *The specs say "The sequence must be formed by 
            /// exactly two Bézier curve segments." Remains to 
            /// be seen if fonts follow the specs though.
            /// </remarks>
            xPoint[] _off_flex_points = new xPoint[2];

            /// <summary>
            /// Whenever flex curves are to be drawn
            /// </summary>
            /// <remarks>
            /// This flag isn't correctly implemented. When
            /// off one should draw straight lines between
            /// the flex endpoints*.
            /// 
            /// What this does is to not parse the flex curves,
            /// which can break some fonts.
            /// 
            /// However, keeping this flag for testing purposes
            /// for now.
            /// 
            /// *Todo: fix this. Draw a straight line from
            ///        BeginFigure to the last point on the
            ///        flex curve. 
            /// </remarks>
            public bool AllowFlex = true;

            /// <summary>
            /// Used to track the draw state
            /// </summary>
            bool _begin = false, _open = false;

            #endregion

            #region Init

            public GlyphParser(PSFont font, IGlyph<Path> glyph_creator) 
            { 
                _font = font; _flex_font = font.FlexFont;
                _glyph_creator = glyph_creator;
            }

            #endregion

            #region StreamGeometry issue workaround

            /// <summary>
            /// Workaround for StreamGemoetry requering closepath
            /// upfront.
            /// </summary>
            List<PathClosed> _path_closed;
            int _n_closed;
            class T1RestartException : Exception { }

            enum PathClosed
            {
                Try,
                Closed,
                Open
            }

            bool CheckClosed()
            {
                //Checks if the open/closed state is know
                if (_path_closed.Count > _n_closed + 1)
                    return _path_closed[++_n_closed] == PathClosed.Closed;

                //Check if the previous figure
                //was closed.
                if (_path_closed.Count > 0)
                {
                    var p = _path_closed[_n_closed];
                    if (p == PathClosed.Try)
                    {
                        //Must redraw the figure
                        _path_closed[_n_closed] = PathClosed.Open;
                        throw new T1RestartException();
                    }
                }

                //Tries painting with closed path
                _path_closed.Add(PathClosed.Try);
                _n_closed++;
                return true;
            }

            #endregion

            /// <summary>
            /// Execute glyph data
            /// </summary>
            /// <param name="data">Data to execute</param>
            /// <returns>Drawn glyph</returns>
            public Path Execute(byte[] data)
            {
                _path_closed = new List<PathClosed>(2);
            restart:
                _lp = new xPoint();
                _wp = new xPoint();
                _lsb = new xPoint();
                _n_closed = -1;
                _n_flex_points = 0;
                _begin = false;
                _open = false;
                Clear();

                _sgc = _glyph_creator.Open();

                try
                {
                    Exe(data, _lp);

                    return _glyph_creator.GetPath();
                }
                catch (T1RestartException) { goto restart; }
                finally { _sgc.Dispose(); }
            }

            /// <summary>
            /// Executes a Type2 charstring
            /// </summary>
            /// <param name="data">Charstring data</param>
            /// <param name="op">
            /// Origin point. _lp is initialized to
            /// the origin point</param>
            private void Exe(byte[] data, xPoint op)
            {
                int dpos = 0;
                xPoint p1 = new xPoint(), p2 = new xPoint();
                while (dpos < data.Length)
                {
                    byte b = data[dpos++];

                    //Pushes numbers onto the stack
                    if (b >= 32)
                    {
                        if (b <= 246)
                            Push(b - 139);
                        else if (b <= 250)
                            Push(((b - 247) * 256) + data[dpos++] + 108);
                        else if (b <= 254)
                            Push(-((b - 251) * 256) - data[dpos++] - 108);
                        else
                            Push((data[dpos++] << 24 | data[dpos++] << 16 | data[dpos++] << 8 | data[dpos++]));

                        continue;
                    }

                    //Executes commands
                    // a |- at the start of a command comment means
                    //      the command fetches values from the bottom
                    //      of the stack (However actually doing this
                    //      gives badly mangled glyphs, so now doing
                    //      top of stack for all commands)
                    // a |- in the result portion (end) of a command 
                    //      comment means the stack needs to be cleared
                    switch (b)
                    {
                        case 1: // |- y dy hstem |-
                            Test(2);
                            Clear();
                            break;
                        case 3: // |- x dx vstem |-
                            Test(2);
                            Clear(); break;
                        case 4: // |- dy vmoveto |-
                            Test(1);
                            _lp.Y += Pop();
                            _begin = _n_flex_points == 0;
                            Clear();
                            break;
                        case 5: // |- dx dy rlineto |-
                            Begin(2);
                            _lp.Y += Pop();
                            _lp.X += Pop();
                            _sgc.LineTo(_lp, true, false);
                            Clear();
                            break;
                        case 6: // |- dx hlineto |-
                            Begin(1);
                            _lp.X += Pop();
                            _sgc.LineTo(_lp, true, false);
                            Clear();
                            break;
                        case 7: // |- dy vlineto |- 
                            Begin(1);
                            _lp.Y += Pop();
                            _sgc.LineTo(_lp, true, false);
                            Clear();
                            break;
                        case 8: // |- dx1 dy1 dx2 dy2 dx3 dy3 rrcurveto |-
                            Begin(6);
                            p1.X = _lp.X + _stack[_stack_pos - 5];
                            p1.Y = _lp.Y + _stack[_stack_pos - 4];
                            p2.X = p1.X + _stack[_stack_pos - 3];
                            p2.Y = p1.Y + _stack[_stack_pos - 2];
                            _lp.X = p2.X + _stack[_stack_pos - 1];
                            _lp.Y = p2.Y + _stack[_stack_pos];
                            _sgc.BezierTo(p1, p2, _lp, true, false);
                            Clear();
                            break;
                        case 9: // – closepath (9) |-
                            //Does some bookkeeping to keep track what figures are closed or not
                            _path_closed[_n_closed] = PathClosed.Closed;
                            _open = false;
                            Clear();
                            break;
                        case 10: // subr# callsubr
                            Test(1);
                            Exe(_font.PrivateDict.Subrs[(int)Pop()], op);
                            break;
                        case 11: // - return -
                            return;
                        case 12:
                            switch (data[dpos++])
                            {
                                case 0: // - dotsection |-
                                    Clear();
                                    break;
                                case 1: // |- x0 dx0 x1 dx1 x2 dx2 vstem3 |-
                                    Test(6);
                                    Clear();
                                    break;
                                case 2: // |- y0 dy0 y1 dy1 y2 dy2 hstem3 |-
                                    Test(6);
                                    Clear();
                                    break;
                                case 6: // |- asb adx ady bchar achar seac |-
                                    Test(5);
                                    //makes an accented character from two 
                                    //other characters

                                    //Specs says the name must exist in the standar encoding.
                                    var std_enc = Pdf.Font.Enc.Standar;
                                    byte[] achar = _font.CharStrings[std_enc[(int)Pop()]];
                                    byte[] bchar = _font.CharStrings[std_enc[(int)Pop()]];

                                    var lp = _lp;
                                    var y = Pop();

                                    //Todo:
                                    //Specs are quiet about this, but adding lp.X seems to get
                                    //the accent on the right spot. I suspect that the correct
                                    //value to use would be the "lsb of the current char".
                                    //
                                    //But I don't know, so trying with lp.X first
                                    //
                                    //Formula: adx - lsb + lp.X (or perhaps base_lsb)
                                    var x = Pop() - Pop() + lp.X;
                                    //You can get the base_lsb from the hsbw and sbw commands

                                    //Renders bchar
                                    Clear();

                                    _lp = new xPoint(0, 0);

                                    Exe(bchar, _lp);

                                    //Renders achar
                                    Clear();

                                    _lp = new xPoint(x, y);

                                    Exe(achar, _lp);
                                    _lp = lp;

                                    Clear();
                                    break;
                                case 7: // |- sbx sby wx wy sbw |-
                                    Test(4);
                                    _wp.Y = Pop();
                                    _wp.X = Pop();
                                    _lp.Y = op.Y + Pop();
                                    _lp.X = op.X + Pop();
                                    Clear();
                                    break;
                                case 12: // num1 num2 div (12 12) quotient
                                    Test(2);
                                    float n2 = Pop();
                                    Push(Pop() / n2);
                                    break;
                                case 16: // arg1 . . . argn n othersubr# callothersubr
                                    Test(2);
                                    int num = (int)Pop();
                                    int n_args = (int)Pop();
                                    Test(n_args);
                                    if (_ps == null) _ps = new PSInterpreter();
                                    if (_flex_font)
                                    {
                                        if (AllowFlex)
                                        {
                                            //The first four subrutines of a flex font is known. I have
                                            //at least one font (Issue 1063) where the othersubr array
                                            //contains junk* and is useless.
                                            // * Actually it contains the script that adobe supplies
                                            //   for creating the default othersubsar, meaning they
                                            //   embeded the script itself, not the result of the script
                                            //
                                            //So, what we do here is to ignore the postscript of the
                                            //first four subrutines (0-3). 
                                            if (num == 1 && n_args == 0)
                                            {
                                                //Starting flex mode
                                                _n_flex_points = 1;
                                                _n_off_points = 0;
                                                Begin(0);

                                                //Okay, what's going on.
                                                //
                                                // Flex mode means that we'll be supplied
                                                // two bezier curves. Each bezier curve
                                                // is made up of two off curve point and
                                                // one end point.
                                                //
                                                // That's 6 points in total.
                                                //
                                                // These points will be supplied one by one
                                                // as a argument to postscript function 2.
                                                //
                                                // In addition to those 6 points is a reference
                                                // point. It is for determining whenever to draw
                                                // a flex curve or not.
                                                break;
                                            }
                                            else if (num == 2 && n_args == 0)
                                            {
                                                //Adding flex points
                                                //
                                                //The format is 7 numbers:
                                                // The first number is simply ignored. At least
                                                // for now.

                                                if (_n_flex_points > 1 && _n_flex_points < 8)
                                                {
                                                    if (_n_off_points == 2)
                                                    {
                                                        _sgc.BezierTo(_off_flex_points[0],
                                                                      _off_flex_points[1],
                                                                      _lp, true, false);
                                                        _n_off_points = 0;
                                                    }
                                                    else
                                                        _off_flex_points[_n_off_points++] = _lp;
                                                }
                                                _n_flex_points++;
                                                System.Diagnostics.Debug.Assert(_n_flex_points <= 8);
                                                break;
                                            }
                                            else if (num == 0 && n_args == 3)
                                            {
                                                if (_n_flex_points != 8)
                                                    throw new ArgumentException("Unexpected flex end");

                                                //Parameters for setcurrentpoint
                                                _ps.PushNum((float)_lp.Y);
                                                _ps.PushNum((float)_lp.X);
                                                _n_flex_points = 0;
                                                break;
                                            }
                                            else if (num == 3 && n_args == 1)
                                            {
                                                //Used for hinting. 
                                                _ps.PushNum(3);
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            if (num == 1 && n_args == 0 || num == 2 && n_args == 0)
                                            {
                                                //Do nothing
                                                _n_flex_points = 1;
                                                break;
                                            }
                                            else if (num == 3 && n_args == 1)
                                            {
                                                _ps.PushNum(3);
                                                break;
                                            }
                                            else if (num == 0 && n_args == 3)
                                            {
                                                //Pushing two arguments onto the ps stack
                                                _ps.PushNum((float)_lp.Y);
                                                _ps.PushNum((float)_lp.X);
                                                _n_flex_points = 0;
                                                break;
                                            }
                                        }
                                    }

                                    //Executes the postscript (Except for the first four runtines)
                                    var proc = _font.PrivateDict.OtherSubrs[num];

                                    //Setup
                                    _ps.Execute("systemdict");
                                    _ps.Execute("begin");
                                    _ps.PushItem(_font);
                                    _ps.Execute("begin");
                                    for (int c = 0; c < n_args; c++)
                                        _ps.PushNum(Pop());

                                    //Execution
                                    _ps.Run(proc);

                                    //Cleanup
                                    _ps.Execute("end");
                                    _ps.Execute("end");

                                    break;
                                case 17: // - pop number
                                    Test(1);
                                    Push((float)_ps.PopNum());
                                    break;
                                case 33: // |- x y setcurrentpoint |-
                                    Test(2);
                                    _lp.Y = op.Y + Pop();
                                    _lp.X = op.X + Pop();
                                    Clear();
                                    break;
                            }
                            break;
                        case 13: // |- sbx wx hsbw |-
                            Test(2);
                            _wp.X = Pop();
                            _wp.Y = 0;
                            _lsb.X = Pop();
                            _lp.X = op.X + _lsb.X;
                            _lp.Y = op.Y;
                            Clear();
                            break;
                        case 14: // - endchar |-
                            //Todo: should perhaps break out of the while loop here.
                            CheckClosed();
                            break;
                        case 15: // Undocumented op
                            Test(2);
                            Pop();
                            Pop();
                            break;
                        case 21: // |- dx dy rmoveto |-
                            Test(2);
                            _lp.Y += Pop();
                            _lp.X += Pop();
                            _begin = _n_flex_points == 0;
                            Clear();
                            break;
                        case 22: // |- dx hmoveto |-
                            Test(1);
                            _lp.X += Pop();
                            _begin = _n_flex_points == 0;
                            Clear();
                            break;
                        case 30: // |- dy1 dx2 dy2 dx3 vhcurveto |-
                            Begin(4);
                            p1.X = _lp.X;
                            p1.Y = _lp.Y + _stack[_stack_pos - 3];
                            p2.X = _lp.X + _stack[_stack_pos - 2];
                            p2.Y = p1.Y + _stack[_stack_pos - 1];
                            _lp.X = p2.X + _stack[_stack_pos];
                            _lp.Y = p2.Y;
                            _sgc.BezierTo(p1, p2, _lp, true, false);
                            Clear();
                            break;
                        case 31: // |- dx1 dx2 dy2 dy3 hvcurveto |-
                            Begin(4);
                            p1.X = _lp.X + _stack[_stack_pos - 3];
                            p1.Y = _lp.Y;
                            p2.X = p1.X + _stack[_stack_pos - 2];
                            p2.Y = _lp.Y + _stack[_stack_pos - 1];
                            _lp.X = p2.X;
                            _lp.Y = p2.Y + _stack[_stack_pos];
                            _sgc.BezierTo(p1, p2, _lp, true, false);
                            Clear();
                            break;

                        default: throw new NotImplementedException("Unknows Type1 command: " + data[dpos-1]); //break;
                    }
                }
            }

            /// <summary>
            /// Call this before drawing. Does a few checks and opens a figure
            /// as needed.
            /// </summary>
            /// <remarks>
            /// This function isn't really needed. One can
            /// remove it and insert _sgc.BeginFigure where the 
            /// begin flag is set true.
            /// 
            /// It works just as well on the few testfiles I got.
            /// 
            /// The only usefull thing this function does right now is that
            /// Debug.Assert and Test(n). 
            /// 
            /// Todo: consider getting rid of this function.
            /// </remarks>
            private void Begin(int n)
            {
                Test(n);
                if (_begin)
                {
                    //There is one function that can move _lp after
                    //a MoveTo command, without begin() being called.
                    //
                    //However I suspect this is the correct way of
                    //handeling that, i.e. start the figure from the
                    //new _lp made by that func. 
                    _begin = false;
                    _sgc.BeginFigure(_lp, true, CheckClosed());
                    _open = true;
                }
                else if (!_open)
                {
                    //Todo: throw exception?
                    System.Diagnostics.Debug.Assert(false, "Tried to draw before begin in Type1 font");
                    throw new ArgumentException();
                }
            }

            /// <summary>
            /// Checks if there's enough arguments
            /// </summary>
            /// <param name="n">Amount of arguments required on the stack</param>
            private void Test(int n)
            {
                if (_stack_pos < n - 1)
                    throw new ArgumentException("Missing parameters");
            }
        }
    }
}
