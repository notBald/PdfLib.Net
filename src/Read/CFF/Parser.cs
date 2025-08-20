using System;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Read.CFF
{
    abstract class Parser
    {
        protected Util.StreamReaderEx _r;

        /// <summary>
        /// Number last read from stream
        /// </summary>
        protected int _number;

        /// <summary>
        /// Floating point number (Real number)
        /// </summary>
        double _rnum;

        /// <summary>
        /// Type read from the stream
        /// </summary>
        TokenType _type;

        /// <summary>
        /// If this is a GlyphParser (need to skip some code)
        /// </summary>
        readonly bool _glyph;

        public Parser(Util.StreamReaderEx r, bool glyph) { _r = r; _glyph = glyph; }

        public void Parse(SizePos sp)
        {
            _r.Position = sp.start;
            int end = Math.Min((int)_r.Length, sp.End);
            //^ Math min is to prevent eternal looping if
            //  end for some reason is set after the stream

            while (_r.Position < end)
            {
                //if (this is GlyphParser && _r.Position > 563)
                //    Console.Write("hi");

                Parse();
                if (_type == TokenType.Command)
                { ParseCMD1(); }
                else if (_type == TokenType.Command2)
                { ParseCMD2(); }
                else if (_type == TokenType.Number)
                    Push(_number);
                else
                    Push(_rnum);
            }
        }

        protected abstract void ParseCMD1();
        protected abstract void ParseCMD2();

        private void Parse()
        {
            _type = TokenType.Number;
            _number = _r.ReadByte();

            if (!_glyph)
            {
                //Number is int
                if (_number == 29)
                {
                    _number = _r.ReadInt();
                    return;
                }

                //Number is floating point
                if (_number == 30)
                {
                    ReadRnum();
                    return;
                }
            }

            //Number is short
            if (_number == 28)
                _number = _r.ReadShort();

            //Number is two byte command
            else if (_number == 12)
            { _number = _r.ReadByte(); _type = TokenType.Command2; }
            else if (_number < 32) //Note: 22-27 is reserved
                _type = TokenType.Command;
            else if (32 <= _number && _number <= 246)
                _number -= 139;
            else if (247 <= _number && _number <= 250)
                _number = (_number - 247) * 256 + _r.ReadByte() + 108;
            else if (251 <= _number && _number <= 254)
                _number = -(_number - 251) * 256 - _r.ReadByte() - 108;
            else
            {
                if (!_glyph)
                    throw new NotSupportedException();
                _rnum = _r.ReadShort() + _r.ReadUShort() / 65536d;
                _type = TokenType.Real;
            }

        }

        /// <summary>
        /// A real number operand is provided in addition to integer operands. 
        /// This operand begins with a byte value of 30 followed by a 
        /// variable-length sequence of bytes. Each byte is composed of two 
        /// 4-bit nibbles. The first nibble of a pair is stored in the most 
        /// significant 4 bits of a byte and the second nibble of a pair is 
        /// stored in the least significant 4 bits of
        /// 
        /// Nibble  Represents
        /// 0-9     0-9
        /// a       .
        /// b       E
        /// c       E-
        /// d       (reserved)
        /// e       -
        /// f       End
        /// 
        /// A real number is terminated by one (or two) 0xf nibbles so that 
        /// it is always padded to a full byte. Thus, the value –2.25 is 
        /// encoded by the byte sequence (1e e2 a2 5f) and the value 
        /// 0.140541E–3 by the sequence (0a 14 05 41 c3 ff).
        /// </summary>
        void ReadRnum()
        {
            _rnum = 0; bool neg = false, des = false, first = true;
            int div = 10, exp = 0, exp_data = 0;
            byte two_nibbles = _r.ReadByte();
            int nibble = two_nibbles >> 4;
            _type = TokenType.Real;

            if (nibble == 14)
            {
                first = false;
                neg = true;
                nibble = two_nibbles & 0x0F;
            }

            while (true)
            {
                if (nibble < 10)
                {
                    if (exp != 0)
                        exp_data = exp_data * 10 + nibble;
                    else if (des)
                    {
                        if (nibble != 0) _rnum += nibble / (double)div;
                        div *= 10;
                    }
                    else
                        _rnum = _rnum * 10 + nibble;
                }
                else if (nibble == 10)
                    des = true;
                else if (nibble == 11)
                    exp = 1;
                else if (nibble == 12)
                    exp = -1;
                else if (nibble == 15)
                    break;

                if (first)
                {
                    nibble = two_nibbles & 0x0F;
                    first = false;
                }
                else
                {
                    two_nibbles = _r.ReadByte();
                    nibble = two_nibbles >> 4;
                    first = true;
                }
            }

            if (exp != 0)
                _rnum *= Math.Pow(10, exp_data * exp);
            if (neg) _rnum *= -1;
        }

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
        protected object[] _stack = new object[8];
        int _stack_pos = -1;

        protected object Pop() { return _stack[_stack_pos--]; }
        protected object Peek() { return _stack[_stack_pos]; }
        protected void Push(object val)
        {
            if (++_stack_pos == _stack.Length)
                Array.Resize<object>(ref _stack, _stack_pos + 1);
            _stack[_stack_pos] = val;
        }
        public void PushNum(double num)
        {
            var inum = (int)num;
            if (num == inum)
                Push(inum);
            else
                Push(num);
        }
        protected void Clear() { _stack_pos = -1; }

        protected int PopInt() { return (int)Pop(); }

        protected double PopDouble()
        {
            var o = Pop();
            if (o is int)
                return (int)o;
            return (double)o;
        }

        protected double GetDouble(int i)
        {
            var o = _stack[i];
            if (o is int)
                return (int)o;
            return (double)o;
        }

        protected object[] StackToArray()
        {
            var oa = new object[_stack_pos + 1];
            Array.Copy(_stack, oa, _stack_pos + 1);
            _stack_pos = 0;
            return oa;
        }

        protected int Count { get { return _stack_pos + 1; } }

        #endregion

        protected xMatrix CreateMatrix()
        {
            double OffsetY = 0, OffsetX = 0;
            if (Count == 6)
            {
                OffsetY = PopDouble();
                OffsetX = PopDouble();
            }
            var M22 = PopDouble();
            var M21 = PopDouble();
            var M12 = PopDouble();
            var M11 = PopDouble();

            return new xMatrix(M11, M12, M21, M22, OffsetX, OffsetY);
        }

        protected enum TokenType
        {
            Command,
            Command2,
            Number,
            Real,
            Operand
        }
    }
}
