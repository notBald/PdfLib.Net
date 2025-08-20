using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.Font;

namespace PdfLib.Read.CFF
{
    /// <summary>
    /// See "Type 2 Charstring format.pdf" for information on font commands
    /// </summary>
    class GlyphParser<Path> : Parser, IDisposable
    {
        #region Variables and properties

        public Util.StreamReaderEx Stream { get { return _r; } }
        IGlyph<Path> _glyp_creator;
        IGlyphDraw _sgc = null;

        /// <summary>
        /// Last point
        /// </summary>
        xPoint _lp = new xPoint();

        /// <summary>
        /// Used for temp storage
        /// </summary>
        xPoint p1 = new xPoint(), p2 = new xPoint();

        /// <summary>
        /// The hintmask commands has a mask assosiated with them that
        /// depends on the number of hints. Because of this one have to
        /// keep track of the number of hints, so that one can skip
        /// ahead whith the proper ammount every time a mask op is called.
        /// 
        /// All hints are ignored though.
        /// </summary>
        int n_hints = 0;

        /// <summary>
        /// Array used to store away values
        /// </summary>
        object[] _transient_array;

        /// <summary>
        /// Needed gpr composite glyphs
        /// </summary>
        readonly Index _glyph_table;

        /// <summary>
        /// Used for subrutines
        /// </summary>
        internal Index _lsubrutines, _gsubrutines;
        internal int _bias, _gbias;

        /// <summary>
        /// Used for the "seac" operator
        /// </summary>
        readonly CFont<Path> _font;
        Dictionary<string, int> _glyph_names;

        #endregion

        #region Init

        public GlyphParser(Util.StreamReaderEx r, Index GlyphTable, CFont<Path> parent, IGlyph<Path> glyph_creator)
            : base(r, true) { _glyph_table = GlyphTable; _font = parent; _glyp_creator = glyph_creator; }

        public void Dispose()
        {
            _r.Dispose();
        }

        #endregion

        public new Path Parse(SizePos sp)
        {
            var sg = _glyp_creator;
            _lp.X = 0;
            _lp.Y = 0;
            n_hints = 0;

            using (_sgc = sg.Open())
            {
                base.Parse(sp);

                _sgc = null;
                return sg.GetPath();
            }
        }

        protected override void ParseCMD1()
        {
            bool tmp; int tmp_num; long pos;

            //Todo: See Type 2 Charstring note 4. Basically the
            //"dx" to "default width", but since PDF files
            //usualy implement widths I've not done anything
            //with this yet.
            double dwidth;

            //Argument explanation:
            // First |- means the arguments are to be taken
            // from the bottom of the stack
            // { }+ is for arguments executed several times
            //    * simply means it can be 0
            // Last |- means the the stack is to be cleared

            //All but the "call subrutine" commands are implemented
            switch (_number)
            {
                case 1: //|- y dy {dya dyb}*  hstem |-
                case 3: //|- x dx {dxa dxb}*  vstem |-
                    //Not implemented so one only needs to clear the stack
                    if ((Count & 1) != 0) dwidth = GetDouble(0);
                    n_hints += Count / 2;
                    break;
                case 4: //|- dy1 vmoveto (4) |-
                    if (Count > 1)
                    {
                        dwidth = GetDouble(0);
                        _lp.Y += GetDouble(1);
                    }
                    else
                        _lp.Y += GetDouble(0);
                    _sgc.BeginFigure(_lp, true, true);
                    break;
                case 5: // |- {dxa dya}+  rlineto |-
                    Debug.Assert((Count & 1) == 0 && Count >= 2);
                    for (int c = 0; c < Count; )
                    {
                        _lp.X += GetDouble(c++);
                        _lp.Y += GetDouble(c++);
                        _sgc.LineTo(_lp, true, false);
                    }
                    break;

                //appends a horizontal line of length dx1 to the current point. 
                case 6: // |- dx1 {dya dxb}*  hlineto |- or |- {dxa dyb}+  hlineto |-
                case 7: // |- dy1 {dxa dyb}*  vlineto |- or |- {dya dxb}+  vlineto |-
                    Debug.Assert(Count > 0);
                    tmp = (_number == 7);
                    for (int c = 0; c < Count; c++)
                    {
                        //I don't see a reason to special case dx1/dy1
                        if (tmp)
                            _lp.Y += GetDouble(c);
                        else
                            _lp.X += GetDouble(c);

                        _sgc.LineTo(_lp, true, false);
                        tmp = !tmp;
                    }
                    break;
                case 8:  //|- {dxa dya dxb dyb dxc dyc}+  rrcurveto |-
                    Debug.Assert(Count >= 6);
                    for (int c = 0; c < Count; )
                    {
                        p1.X = _lp.X + GetDouble(c++);
                        p1.Y = _lp.Y + GetDouble(c++);
                        p2.X = p1.X + GetDouble(c++);
                        p2.Y = p1.Y + GetDouble(c++);
                        _lp.X = p2.X + GetDouble(c++);
                        _lp.Y = p2.Y + GetDouble(c++);
                        _sgc.BezierTo(p1, p2, _lp, true, false);
                    }
                    break;

                case 10: //subr# callsubr (10) –
                    //subr# is the subr number plus the subroutine bias number,
                    Debug.Assert(Count >= 1);
                    tmp_num = PopInt() + _bias;

                    //Executes the subrutine
                    pos = _r.Position;
                    base.Parse(_lsubrutines.GetSizePos(tmp_num, _r));
                    _r.Position = pos;

                    //Using "return" so to not reset the stack
                    return;

                case 11: //return
                    //Should anything be done here? This only works if return is
                    //the very last command.

                    //Using "return" so to not reset the stack
                    return;

                case 14: //– endchar (14) |–
                    tmp_num = 0;
                    if ((Count & 1) != 0)
                    {
                        dwidth = GetDouble(0);
                        tmp_num = 1;
                    }
                    if (Count - tmp_num == 4)
                    {
                        //endchar may have four extra arguments that correspond 
                        //exactly to the last four arguments of the Type 1 
                        //charstring command “seac”
                        // adx ady bchar achar seac
                        ///Todo: Prevent nesting of seac (Don't want stack overflow)

                        //The char values are in relation to the standar encoding (SID)
                        var std_enc = Pdf.Font.Enc.Standar;
                        int achar_gid = GetGIDFromName(std_enc[PopInt()]);
                        int bchar_gid = GetGIDFromName(std_enc[PopInt()]);

                        var achar = _glyph_table.GetSizePos(achar_gid, _r);
                        var bchar = _glyph_table.GetSizePos(bchar_gid, _r);

                        var lp = _lp;
                        var tab = _transient_array;
                        var hints = n_hints;
                        var y = PopDouble();
                        var x = PopDouble() + lp.X;

                        //Renders bchar
                        Clear();
                        _lp = new xPoint(0, 0);

                        _transient_array = null;
                        n_hints = 0;
                        base.Parse(bchar);

                        //Renders achar
                        Clear();
                        _lp = new xPoint(x, y);

                        _transient_array = null;
                        n_hints = 0;
                        base.Parse(achar);

                        //Restore state (Not really needed)
                        _lp = lp;
                        _transient_array = tab;
                        n_hints = hints;
                        //_r.Position now points at "some location"
                    }

                    //Terminates the character
                    _r.Position = _r.Length;
                    break;

                case 23: // |- x dx {dxa dxb}*  vstemhm |-
                case 18: // |- y dy {dya dyb}*  hstemhm |-
                    if ((Count & 1) != 0) dwidth = GetDouble(0);
                    n_hints += Count / 2; //<-- Count may be one too high, but that will always be rounded away
                    break;
                case 19: // |- hintmask (19 + mask) |-
                case 20: // |- cntrmask (20 + mask) |-
                    if ((Count & 1) != 0) dwidth = GetDouble(0);
                    n_hints += Count / 2;
                    //The mask data bytes are defined as follows:
                    //  one bit per hint
                    //This means we have to move 1 byte ahead for
                    //every 8 hints. 
                    _r.Position += (n_hints + 7) / 8;
                    //Note that position will throw an exception if we
                    //move too far
                    break;

                case 21: // |- dx1 dy1 rmoveto |-
                    Debug.Assert(Count >= 2);
                    tmp_num = 0;
                    if (Count > 2)
                    {
                        dwidth = GetDouble(0);
                        tmp_num = 1;
                    }

                    _lp.X += GetDouble(0 + tmp_num);
                    _lp.Y += GetDouble(1 + tmp_num);
                    _sgc.BeginFigure(_lp, true, true);
                    break;

                case 22: // |- dx1 hmoveto |-
                    Debug.Assert(Count >= 1);
                    if (Count > 1)
                    {
                        dwidth = GetDouble(0);
                        _lp.X += GetDouble(1);
                    }
                    else
                        _lp.X += GetDouble(0);
                    _sgc.BeginFigure(_lp, true, true);
                    break;

                case 24: // |- {dxa dya dxb dyb dxc dyc}+  dxd dyd rcurveline |-
                    Debug.Assert(Count >= 8 && (Count & 1) == 0);
                    for (int c = 0; c < Count - 2; )
                    {
                        p1.X = _lp.X + GetDouble(c++); //dxa + LastPoint.X
                        p1.Y = _lp.Y + GetDouble(c++); //dya + LastPoint.Y
                        p2.X = p1.X + GetDouble(c++); //dxb + LastPoint.X
                        p2.Y = p1.Y + GetDouble(c++); //dyb + LastPoint.Y
                        _lp.X = p2.X + GetDouble(c++); //dxc + LastPoint.X
                        _lp.Y = p2.Y + GetDouble(c++); //dyc + LastPoint.Y
                        _sgc.BezierTo(p1, p2, _lp, true, false);
                    }
                    _lp.X += GetDouble(Count - 2);
                    _lp.Y += GetDouble(Count - 1);
                    _sgc.LineTo(_lp, true, false);
                    break;

                case 25: // |- {dxa dya}+  dxb dyb dxc dyc dxd dyd rlinecurve |-
                    Debug.Assert(Count >= 8 && (Count & 1) == 0);
                    tmp_num = 0;
                    while (tmp_num < Count - 6)
                    {
                        _lp.X += GetDouble(tmp_num++);
                        _lp.Y += GetDouble(tmp_num++);
                        _sgc.LineTo(_lp, true, false);
                    }
                    p1.X = _lp.X + GetDouble(tmp_num++);
                    p1.Y = _lp.Y + GetDouble(tmp_num++);
                    p2.X = p1.X + GetDouble(tmp_num++);
                    p2.Y = p1.Y + GetDouble(tmp_num++);
                    _lp.X = p2.X + GetDouble(tmp_num++);
                    _lp.Y = p2.Y + GetDouble(tmp_num++);
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    break;

                case 26: // |- dx1? {dya dxb dyb dyc}+  vvcurveto |-
                    Debug.Assert(Count >= 4);
                    //If the argument count is odd, the first curve does 
                    //not begin with a vertical tangent.
                    tmp_num = 0;
                    if ((Count & 1) == 1) _lp.X += GetDouble(tmp_num++);

                    while (tmp_num < Count)
                    {
                        p1.X = _lp.X;
                        p1.Y = _lp.Y + GetDouble(tmp_num++);
                        p2.X = p1.X + GetDouble(tmp_num++);
                        p2.Y = p1.Y + GetDouble(tmp_num++);
                        _lp.X = p2.X;
                        _lp.Y = p2.Y + GetDouble(tmp_num++);
                        _sgc.BezierTo(p1, p2, _lp, true, false);
                    }
                    break;

                case 27: // |- dy1? {dxa dxb dyb dxc}+ hhcurveto |-
                    tmp_num = 0;
                    if ((Count & 1) == 1) _lp.Y += GetDouble(tmp_num++);

                    while (tmp_num < Count)
                    {
                        p1.X = _lp.X + GetDouble(tmp_num++);
                        p1.Y = _lp.Y;
                        p2.X = p1.X + GetDouble(tmp_num++);
                        p2.Y = p1.Y + GetDouble(tmp_num++);
                        _lp.X = p2.X + GetDouble(tmp_num++);
                        _lp.Y = p2.Y;
                        _sgc.BezierTo(p1, p2, _lp, true, false);
                    }
                    break;

                case 29: //globalsubr# callgsubr (29) –
                    //globalsubr# is the subr number plus the subroutine bias number,
                    Debug.Assert(Count >= 1);
                    tmp_num = PopInt() + _gbias;

                    //Executes the subrutine
                    pos = _r.Position;
                    base.Parse(_gsubrutines.GetSizePos(tmp_num, _r));
                    _r.Position = pos;

                    //Returns since arguments could have been passed onto the stack
                    return;

                case 30: // |- dy1 dx2 dy2 dx3 {dxa dxb dyb dyc dyd dxe dye dxf}* dyf? vhcurveto |-
                // |- {dya dxb dyb dxc dxd dxe dye dyf}+ dxf? vhcurveto |- 
                case 31: // |- dx1 dx2 dy2 dy3 {dya dxb dyb dxc dxd dxe dye dyf}* dxf? hvcurveto |-
                    // |- {dxa dxb dyb dyc dyd dxe dye dxf}+ dyf? hvcurveto |-
                    Debug.Assert(Count >= 4);
                    tmp = (_number == 30);
                    for (int c = 0; c < Count; )
                    {
                        p1.Y = _lp.Y + ((tmp) ? GetDouble(c++) : 0); //dy1
                        p1.X = _lp.X + ((!tmp) ? GetDouble(c++) : 0); //dx1
                        p2.X = p1.X + GetDouble(c++); //dx2
                        p2.Y = p1.Y + GetDouble(c++); //dy2
                        _lp.X = p2.X + ((tmp) ? GetDouble(c++) : 0); //dx3
                        _lp.Y = p2.Y + ((!tmp) ? GetDouble(c++) : 0); //dy3

                        //If we're on the last pos
                        if (Count == c + 1)
                        {
                            if (!tmp) _lp.X += GetDouble(c++);
                            else _lp.Y += GetDouble(c++);
                        }

                        _sgc.BezierTo(p1, p2, _lp, true, false);
                        tmp = !tmp;
                    }
                    break;

                default:
                    Debug.Assert(false, "Unimplimented command");
                    break;
            }

            Clear();

            //Debug
            //var X = _lp.X * 65536;
            //var Y = _lp.Y * 65536;
        }

        protected override void ParseCMD2()
        {
            double temp_num, temp_num2; object tmp;

            // For two byte commands
            switch (_number)
            {
                case 0: //NOP
                    break;

                case 3: //num1 num2 and 1_or_0 
                    temp_num = PopDouble();
                    Push((PopDouble() != 0 && temp_num != 0) ? 1 : 0);
                    return;

                case 4: //num1 num2 or 1_or_0 
                    temp_num = PopDouble();
                    Push((PopDouble() != 0 || temp_num != 0) ? 1 : 0);
                    return;

                case 5: //num1 not 1_or_0
                    Push((PopDouble() == 0) ? 1 : 0);
                    return;

                case 9: //num abs num2
                    PushNum(Math.Abs(PopDouble()));
                    return;

                case 10: //num1 num2 add sum
                    PushNum(PopDouble() + PopDouble());
                    return;

                case 11: //num1 num2 sub sum
                    PushNum(-PopDouble() + PopDouble());
                    return;

                case 12: //num1 num2 div 1_or_0
                    temp_num = PopDouble();
                    PushNum(PopDouble() / temp_num);
                    return;

                case 14: //num neg num2
                    PushNum(-PopDouble());
                    return;

                case 15: //num1 num2 eq 1_or_0
                    Push((PopDouble() == PopDouble()) ? 1 : 0);
                    return;

                case 18: //num drop
                    Pop();
                    return;

                case 20: // val i put
                    if (_transient_array == null)
                        _transient_array = new object[32];
                    _transient_array[PopInt()] = Pop();
                    return;

                case 21: // i get val
                    if (_transient_array == null)
                        _transient_array = new object[32];
                    Push(_transient_array[PopInt()]);
                    return;

                case 22: //s1 s2 v1 v2 ifelse (12 22) s1_or_s2
                    if (PopDouble() >= PopDouble())
                    {
                        //Leaves s1 on the stack
                        Pop();
                    }
                    else
                    {
                        //Leaves s2 on the stack
                        tmp = Pop(); Pop();
                        Push(tmp);
                    }
                    return;

                case 23: //random num2 (Range "(0,1]")
                    temp_num = new Random().NextDouble(); //Using "new Random" is probably a poor idea
                    if (temp_num == 0) temp_num += 0.0001;
                    Push(temp_num);
                    return;

                case 24: //num1 num2 mul product
                    PushNum(PopDouble() * PopDouble());
                    return;

                case 26: //num sqrt num2
                    PushNum(Math.Sqrt(PopDouble()));
                    return;

                case 27: //any dup any any
                    Push(Peek());
                    return;

                case 28: //num1 num2 exch num2 num1
                    tmp = _stack[Count - 1];
                    _stack[Count - 1] = _stack[Count - 2];
                    _stack[Count - 2] = tmp;
                    return;

                case 29: //numX ... num0 i index numX ... num0 numi
                    Push(_stack[Count - 2 - PopInt()]);
                    return;

                case 30: //roll
                    Roll(PopInt(), PopInt());
                    return;

                case 34: // |- dx1 dx2 dy2 dx3 dx4 dx5 dx6 hflex (12 34) |-
                    temp_num = _lp.Y;
                    p1.X = _lp.X + GetDouble(0);
                    p1.Y = _lp.Y;
                    p2.X = p1.X + GetDouble(1);
                    p2.Y = p1.Y + GetDouble(2);
                    _lp.X = p2.X + GetDouble(3);
                    _lp.Y = p2.Y;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    p1.X = _lp.X + GetDouble(4);
                    p1.Y = _lp.Y;
                    p2.X = p1.X + GetDouble(5);
                    p2.Y = temp_num;
                    _lp.X = p2.X + GetDouble(6);
                    _lp.Y = temp_num;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    break;

                case 35: // |- dx1 dy1 dx2 dy2 dx3 dy3 dx4 dy4 dx5 dy5 dx6 dy6 fd flex |-
                    p1.X = _lp.X + GetDouble(0);
                    p1.Y = _lp.Y + GetDouble(1);
                    p2.X = p1.X + GetDouble(2);
                    p2.Y = p1.Y + GetDouble(3);
                    _lp.X = p2.X + GetDouble(4);
                    _lp.Y = p2.Y + GetDouble(5); ;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    p1.X = _lp.X + GetDouble(6);
                    p1.Y = _lp.Y + GetDouble(7);
                    p2.X = p1.X + GetDouble(8);
                    p2.Y = p1.Y + GetDouble(9);
                    _lp.X = p2.X + GetDouble(10);
                    _lp.Y = p2.Y + GetDouble(11);
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    //Arg 12 ignored
                    break;

                case 36: // |- dx1 dy1 dx2 dy2 dx3 dx4 dx5 dy5 dx6 hflex1 |-
                    temp_num = _lp.Y;
                    p1.X = _lp.X + GetDouble(0);
                    p1.Y = _lp.Y + GetDouble(1);
                    p2.X = p1.X + GetDouble(2);
                    p2.Y = p1.Y + GetDouble(3);
                    _lp.X = p2.X + GetDouble(4);
                    _lp.Y = p2.Y;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    p1.X = _lp.X + GetDouble(5);
                    p1.Y = _lp.Y;
                    p2.X = p1.X + GetDouble(6);
                    p2.Y = p1.Y + GetDouble(7);
                    _lp.X = p2.X + GetDouble(8);
                    _lp.Y = temp_num;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    break;

                case 37: // |- dx1 dy1 dx2 dy2 dx3 dy3 dx4 dy4 dx5 dy5 d6 flex1 |-
                    temp_num = _lp.Y; temp_num2 = _lp.X;
                    p1.X = _lp.X + GetDouble(0);
                    p1.Y = _lp.Y + GetDouble(1);
                    p2.X = p1.X + GetDouble(2);
                    p2.Y = p1.Y + GetDouble(3);
                    _lp.X = p2.X + GetDouble(4);
                    _lp.Y = p2.Y + GetDouble(5); ;
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    p1.X = _lp.X + GetDouble(6);
                    p1.Y = _lp.Y + GetDouble(7);
                    p2.X = p1.X + GetDouble(8);
                    p2.Y = p1.Y + GetDouble(9);

                    //Last argument is determined by the sum of the former
                    if (Math.Abs(p2.Y - temp_num) < Math.Abs(p2.X - temp_num2))
                    {
                        _lp.X = p2.X + GetDouble(10);
                        _lp.Y = temp_num;
                    }
                    else
                    {
                        _lp.X = temp_num2;
                        _lp.Y = p2.Y + GetDouble(10);
                    }
                    _sgc.BezierTo(p1, p2, _lp, true, false);
                    break;

                default:
                    Debug.Assert(false, "Unimplimented command");
                    break;
            }

            Clear();
        }

        void Roll(int j, int n)
        {
            if (n == 0 || j == 0) return;
            if (n < 0) throw new NotSupportedException();
            int start = Count - n;
            if (j > 0)
            {
                for (int c = 0; c < j; c++)
                    forwards_roll(start);
            }
            if (j < 0)
            {
                for (int c = 0; c > j; c--)
                    backwards_roll(start);
            }
        }

        void forwards_roll(int start)
        {
            //Stores away the last element.
            var last = _stack[Count - 1];

            //Copies array one element up (-1 to not copy the last item)
            Array.Copy(_stack, start, _stack, start + 1, Count - start - 1);

            //Inserts last in the first pos
            _stack[start] = last;
        }

        void backwards_roll(int start)
        {
            var first = _stack[start];
            Array.Copy(_stack, start + 1, _stack, start, Count - start - 1);
            _stack[Count - 1] = first;
        }

        int GetGIDFromName(string name)
        {
            if (_glyph_names == null)
            {
                var names = _font.GlyphNames;
                _glyph_names = new Dictionary<string, int>(names.Length);
                for (int c = 0; c < names.Length; c++)
                    _glyph_names.Add(names[c], c);
            }
            return _glyph_names[name];
        }
    }
}
