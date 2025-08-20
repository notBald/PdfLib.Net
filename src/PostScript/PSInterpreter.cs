//There's at least one font that balk at seeing the internal dict (1211.6429.pdf), but there 
//may also be possible that there are fonts that balk at not seeing it. Perhaps it's better
//to fill the internal dict up with stuff the font expects to see than to hide it. 
//#define ADD_INTERNAL_DICT

//Besides the restore command, save _support will need some clever way of clearing the VM
//of data to prevent a giant memory leak. This could for instance be done every time all
//stacks are zero, or after execution ends (on the assumtions that there's never any 
//carryover between glyph subrutines (I don't belive there is)). 
//#define SAVE_SUP //<-- Not finished

using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.PostScript.Font;
using PdfLib.Util;

namespace PdfLib.PostScript
{
    /// <summary>
    /// This postscript interpreter is used for Type1 fonts. It does not do propper error handeling, 
    /// nor is it meant for general postscript execution (though it's certainly possible to expand on
    /// it towards that purpose)
    /// 
    /// It now also handles cmap and the postscript PdfFunction. 
    /// </summary>
    /// <remarks>
    /// Todo:
    ///  - Prevent functions such as "roll" from accepting suspect values (say roll int.Maxvalue)
    ///  - Make it possible to stop the interpreter
    /// </remarks>
    public class PSInterpreter
    {
        #region Variables and properties

        /// <summary>
        /// This is the operand stack. 
        /// 
        /// Names, numbers, arrays, dictionaries and procedures are pushed straight onto 
        /// this stack
        /// </summary>
        PSItem[] _operand = new PSItem[16];
        int _count = 0;

        /// <summary>
        /// A lexer is needed when executing non-tokenized postscript, and for some 
        /// LangLevel3 operands
        /// </summary>
        PSLexer _lex;

#if SAVE_SUP

        /// <summary>
        /// PostScript allows for saving and restoring the VM. This is supported by
        /// registering all composite objects (PSObjects) and saving and restoring
        /// what they again are pointing at.
        /// </summary>
        PSVM _vm;

#endif

        /// <summary>
        /// Dictionary stack
        /// </summary>
        IndexStack<PSDictionary> _dict_stack;

        /// <summary>
        /// The system dictionary is a special dictionary that contains
        /// executable operators.
        /// </summary>
        PSDictionary _system_dict;

        /// <summary>
        /// Contains fonts, and other resources. 
        /// </summary>
        PSDictionary _resources;

        /// <summary>
        /// There are three language levels. How the interpreter works differs
        /// a bit between them
        /// </summary>
        LangLevel _ll = LangLevel.One;

        public LangLevel LanguageLevel
        {
            get { return _ll; }
            set 
            {
                if (value == _ll) return;
                if (value == LangLevel.One)
                {
                    if (_ll > LangLevel.Two)
                        RemoveLevel3();
                    if (_ll > LangLevel.One)
                        RemoveLevel2();
                }
                if (value == LangLevel.Two)
                {
                    if (_ll == LangLevel.Three)
                        RemoveLevel3();
                    else
                        InitLevel2();
                }
                if (value == LangLevel.Three)
                {
                    if (_ll == LangLevel.One)
                        InitLevel2();
                    InitLevel3();
                }
                _ll = value;
            }
        }

        /// <summary>
        /// System dictionary.
        /// </summary>
        public PSDictionary SystemDict { get { return _system_dict; } }

        public PSTypeDict<PSFont> FontDictionary { get { return (PSTypeDict<PSFont>)_system_dict.Catalog["FontDirectory"]; } }

        /// <summary>
        /// The data being parsed in string format
        /// </summary>
        public string DebugString { get { return (_lex == null) ? null : _lex.ReadDebugData(); } }

        #endregion

        #region Init

        public PSInterpreter() 
        {
#if SAVE_SUP
            _vm = new PSVM();
#endif
            _resources = new PSDictionary();

            //Builds up the system dictionary
            _system_dict = new PSDictionary();
            var cat = _system_dict.Catalog;
            cat.Add("abs", new PSCallback(OP_abs));
            cat.Add("add", new PSCallback(OP_add));
            cat.Add("and", new PSCallback(OP_and));
            cat.Add("atan", new PSCallback(OP_atan));
            cat.Add("array", new PSCallback(OP_array));
            cat.Add("begin", new PSCallback(OP_begin));
            cat.Add("bitshift", new PSCallback(OP_bitshift));
            cat.Add("ceiling", new PSCallback(OP_ceiling));
            cat.Add("cleartomark", new PSCallback(OP_cleartomark));
            cat.Add("copy", new PSCallback(OP_copy));
            cat.Add("cos", new PSCallback(OP_cos));
            cat.Add("counttomark", new PSCallback(OP_counttomark));
            cat.Add("closefile", new PSCallback(OP_closefile));
            cat.Add("currentdict", new PSCallback(OP_currentdict));
            cat.Add("currentfile", new PSCallback(OP_currentfile));
            cat.Add("cvi", new PSCallback(OP_cvi));
            cat.Add("cvr", new PSCallback(OP_cvr));
            cat.Add("cvx", new PSCallback(OP_cvx));
            cat.Add("def", new PSCallback(OP_def));
            cat.Add("definefont", new PSCallback(OP_definefont));
            cat.Add("dict", new PSCallback(OP_dict));
            cat.Add("div", new PSCallback(OP_div));
            cat.Add("dup", new PSCallback(OP_dup));
            cat.Add("eexec", new PSCallback(OP_eexec));
            cat.Add("end", new PSCallback(OP_end));
            cat.Add("eq", new PSCallback(OP_eq));
            cat.Add("executeonly", new PSCallback(OP_executeonly));
            cat.Add("exch", new PSCallback(OP_exch));
            cat.Add("exec", new PSCallback(OP_exec));
            cat.Add("exp", new PSCallback(OP_exp));
            cat.Add("false", new PSBool(false));
            cat.Add("floor", new PSCallback(OP_floor));
            cat.Add("for", new PSCallback(OP_for));
            cat.Add("ge", new PSCallback(OP_ge));
            cat.Add("get", new PSCallback(OP_get));
            cat.Add("gt", new PSCallback(OP_gt));
            cat.Add("known", new PSCallback(OP_known));
            cat.Add("idiv", new PSCallback(OP_idiv));
            cat.Add("ifelse", new PSCallback(OP_ifelse));
            cat.Add("if", new PSCallback(OP_if));
            cat.Add("index", new PSCallback(OP_index));
            cat.Add("le", new PSCallback(OP_le));
            cat.Add("length", new PSCallback(OP_length));
            cat.Add("ln", new PSCallback(OP_ln));
            cat.Add("log", new PSCallback(OP_log));
            cat.Add("lt", new PSCallback(OP_lt));
            cat.Add("ne", new PSCallback(OP_ne));
            cat.Add("neg", new PSCallback(OP_neg));
            cat.Add("noaccess", new PSCallback(OP_noaccess));
            cat.Add("not", new PSCallback(OP_not));
            cat.Add("mark", new PSCallback(OP_mark));
            cat.Add("maxlength", new PSCallback(OP_maxlength));
            cat.Add("mod", new PSCallback(OP_mod));
            cat.Add("mul", new PSCallback(OP_mul));
            cat.Add("or", new PSCallback(OP_or));
            cat.Add("pop", new PSCallback(OP_pop));
            cat.Add("put", new PSCallback(OP_put));
            cat.Add("readonly", new PSCallback(OP_readonly));
            cat.Add("readstring", new PSCallback(OP_readstring));
            cat.Add("roll", new PSCallback(OP_roll));
            cat.Add("round", new PSCallback(OP_round));
#if SAVE_SUP
            cat.Add("save", new PSCallback(OP_save));
#endif
            cat.Add("sin", new PSCallback(OP_sin));
            cat.Add("sqrt", new PSCallback(OP_sqrt));
            cat.Add("string", new PSCallback(OP_string));
            cat.Add("sub", new PSCallback(OP_sub));
            cat.Add("systemdict", new PSCallback(OP_systemdict));
            cat.Add("true", new PSBool(true));
            cat.Add("truncate", new PSCallback(OP_truncate));
            cat.Add("StandardEncoding", new PSCallback(OP_std_enc));
            cat.Add("userdict", new PSCallback(OP_userdict));
            cat.Add("xor", new PSCallback(OP_xor));

            _system_dict.Access = PSAccess.ReadOnly;
            Clear();
        }

        /// <summary>
        /// Resets the interpreter to it's default state
        /// </summary>
        public void Clear()
        {
#if SAVE_SUP
            //Inits the VM
            _vm.Clear();
            _vm.Add(_system_dict);
            _vm.Add(_resources);
#endif

            _count = 0;
            _dict_stack = new IndexStack<PSDictionary>(8);

            //The system dictionary is always at the bottom
            _dict_stack.Push(_system_dict);

            //Then comes the user dictionary
#if SAVE_SUP
            _dict_stack.Push(_vm[new PSDictionary()]);
#else
            _dict_stack.Push(new PSDictionary());
#endif

            //Preps resources
            _resources.Catalog.Clear();
            AddLevel1Res();
            if (_ll > LangLevel.One) AddLevel2Res();
            if (_ll > LangLevel.Two) AddLevel3Res();
        }

        private void AddLevel1Res()
        {
            _system_dict.Catalog.Remove("FontDirectory");
#if ADD_INTERNAL_DICT
            _system_dict.Catalog.Remove("internaldict");
#endif

#if SAVE_SUP
            _system_dict.Catalog.Add("FontDirectory", _vm[new PSTypeDict<PSFont>(new PSDictionary(), PSFont.Create)]);

            //Referenced by some Type1 fonts. 
#if ADD_INTERNAL_DICT
            _system_dict.Catalog.Add("internaldict", _vm[new PSDictionary()]);
#endif
#else
            _system_dict.Catalog.Add("FontDirectory", new PSTypeDict<PSFont>(new PSDictionary(), PSFont.Create));
#if ADD_INTERNAL_DICT
            _system_dict.Catalog.Add("internaldict", new PSDictionary());
#endif
#endif
        }

        private void RemoveLevel2()
        {
            //Removes ops
            var cat = _system_dict.Catalog;
            cat.Remove("findresource");
            cat.Remove("defineresource");

            //Removes res
            cat = _resources.Catalog;
            cat.Remove("ProcSet");

            //Remove global dict
            _dict_stack.Remove(_dict_stack.Count - 2);
        }

        private void InitLevel2()
        {
            //Adds level 2 commands
            var cat = _system_dict.Catalog;
            cat.Add("findresource", new PSCallback(OP_findresource));
            cat.Add("defineresource", new PSCallback(OP_defineresource));

            //A "GlobalDict" must be put in position 2 of the dict stack.
#if SAVE_SUP
            _dict_stack.Insert(_dict_stack.Count - 2, _vm[new PSDictionary()]);
#else
            _dict_stack.Insert(_dict_stack.Count - 2, new PSDictionary());
#endif

            AddLevel2Res();
        }

        private void AddLevel2Res()
        {
            var cat = _resources.Catalog;
#if SAVE_SUP
            cat.Add("ProcSet", _vm[new PSDictionary()]);
#else
            cat.Add("ProcSet", new PSDictionary());
#endif
        }

        private void RemoveLevel3()
        {
            //Removes res
            var cat = _resources.Catalog;
            cat.Remove("CMap");
            cat = ((PSDictionary)_resources["ProcSet"]).Catalog;
            cat.Remove("CIDInit");
        }

        private void InitLevel3()
        {
            AddLevel3Res();
        }

        private void AddLevel3Res()
        {
            var cat = _resources.Catalog;
#if SAVE_SUP
            cat.Add("CMap", _vm[new PSDictionary()]);
#else
            cat.Add("CMap", new PSDictionary());
#endif

            cat = ((PSDictionary)_resources["ProcSet"]).Catalog;
            cat.Add("CIDInit", CreateCIDInit());
        }

        /// <summary>
        /// Procedure for building CID fonts and CMap dictionaries
        /// </summary>
        /// <returns></returns>
        private PSDictionary CreateCIDInit()
        {
#if SAVE_SUP
            var ps = _vm[new PSDictionary()];
#else
            var ps = new PSDictionary();
#endif

            var cat = ps.Catalog;

            cat.Add("begincmap", new PSCallback(OP_begincmap));
            cat.Add("endcmap", new PSCallback(OP_endcmap));
            cat.Add("begincodespacerange", new PSCallback(OP_begincodespacerange));
            cat.Add("begincidrange", new PSCallback(OP_begincidrange));
            cat.Add("beginnotdefrange", new PSCallback(OP_beginnotdefrange));
            cat.Add("beginnotdefchar", new PSCallback(OP_beginnotdefchar));
            cat.Add("begincidchar", new PSCallback(OP_begincidchar));
            cat.Add("usecmap", new PSCallback(OP_usecmap));
            //ToUnicode mapping function:
            cat.Add("beginbfchar", new PSCallback(OP_beginbfchar));
            cat.Add("beginbfrange", new PSCallback(OP_beginbfrange));

            return ps;
        }

        #endregion

        #region Running

        /// <summary>
        /// Runs a stream
        /// </summary>
        public void Run(System.IO.Stream s)
        { Run(new PSLexer(s, (int) s.Length)); }

        /// <summary>
        /// Runs lexer data. Note, use "Clear" when reusing the interpreter
        /// </summary>
        /// <param name="lex">Lexer to execute</param>
        public void Run(PSLexer lex)
        {
            if (_lex != null) throw new ArgumentException("This function is not renetrant");
            _lex = lex;

            while (true)
            {
                PSType type = MoveDataToStack();

                if (type == PSType.EOF)
                {
                    _lex = null;

                    //There can be data on the stack

                    return;
                }

                Execute(_lex.Token);
            }
        }

        /// <summary>
        /// Runs a script. Note, use "Clear" when reusing the interpreter
        /// </summary>
        public void Run(byte[] raw_data )
        { Run(new PSLexer(raw_data)); }

        PSType MoveDataToStack()
        {
            var type = _lex.SetNextToken();

            while (type != PSType.Keyword && type != PSType.EOF)
            {
                Push(ReadType(type));

                type = _lex.SetNextToken();
            }

            return type;
        }

        /// <summary>
        /// Executes a procedure
        /// </summary>
        public void Run(PSObject obj)
        {
            if (obj.Access == PSAccess.None)
                throw new NotSupportedException("No access");

            if (obj is PSProcedure)
                Run(((PSProcedure)obj).Items);
            else if (obj is PSArray)
                Run(((PSArray)obj).GetAllItems());
                //Strings and files are to be executed byte by byte,
                //so create a new PSLexer for them and call Run(lexer)
            else throw new NotImplementedException(obj.ToString());
        }

        private void Run(PSItem[] items)
        {
            for (int c = 0; c < items.Length; c++)
                Execute(items[c]);
        }

        /// <summary>
        /// Execute an item
        /// </summary>
        private void Execute(PSItem itm)
        {
            if (itm is PSOperator)
                Execute(((PSOperator)itm).Operator);
            else if (itm is PSCallback && itm.Executable) //<- todo should PDCallbacks be allowed to be literal?
                ((PSCallback)itm).Action();
            else if (itm is PSObject)
            {
                //Must check access rights
                if (((PSObject)itm).Access > PSAccess.ReadOnly)
                    throw new PSReadException(PSType.None, ErrCode.Illegal);

                //Not sure if one should try to execute non-readable objects,
                //but I assume no. Pushing them onto the stack.
                Push(itm);
            }
            else
            {
                //Null is to be completly ignored
                if (itm == PSNull.Value) return;

                //Note that everything can be "executed", but
                //when literal objects are executed they are
                //to be put onto the operand stack.
                //
                //I.e. executing an executable name, effectivly
                //     retrives that value from the dictionary
                //     and puts it on the operand stack
                //
                //See: Specs 3.3
                Push(itm);
            }
        }

        #endregion

        #region Built in operators

        #region Level 1 operators

        #region Stack Manipulation

        /// <summary>
        /// Pop item of the stack
        /// any pop -
        /// </summary>
        void OP_pop()
        {
            Pop();
        }

        /// <summary>
        /// Duplicates an object. Note, there's simple and
        /// compisite objects and they are to be treated
        /// differently. AFAICT simple objects are to be
        /// copied, while composite objects are a pointer.
        ///
        /// Not sure about issues such as making a copy
        /// and then setting "readonly" on the copy. 
        /// 
        /// any dup any any
        /// </summary>
        void OP_dup()
        {
            var val = Peek();
            /*if (val is PSObject)
            {
                if (val is PSDictionary)
                    Push(((PSDictionary)val).MakeShallowCopy());
                else
                    goto default;
            }
            else*/
            Push(val);
        }

        /// <summary>
        /// Copy n operands on the stack
        /// any1  …  anyn  n copy any1  …  anyn  any1  …  anyn
        /// </summary>
        void OP_copy()
        {
            int n = PopInt(), pos = _count - n;
            for (int c = 0; c < n; c++)
                Push(_operand[pos + c]);
        }

        /// <summary>
        /// anyn … any0 n index anyn … any0 anyn
        /// </summary>
        void OP_index()
        {
            int n = PopUInt();
            var val = _operand[_count - n - 1];
            Push(val);
        }

        /// <summary>
        /// Sets a mark object on the operand stack
        /// </summary>
        void OP_mark()
        {
            Push(PSMark.Instance);
        }


        /// <summary>
        /// Returns the capacity of a containter
        /// 
        /// dict length  int
        /// </summary>
        void OP_maxlength()
        {
            //On LangLevel 1 this is suppose to be the value that created the dictionary.
            //On LangLevel 2 it's some unspecified value >= count.
            //
            if (LanguageLevel == LangLevel.One)
                PushNum(PopDict().Catalog.Count + 100); // <-- This is a quick "good enough" hack as capacity isn't tracked 
            else
                PushNum(PopDict().Catalog.Count + 100);
        }

        void OP_cleartomark()
        {
            while (!(Pop() is PSMark))
                ;
        }

        void OP_counttomark()
        {
            int count = 0;
            while (count < _count && _operand[_count - 1 - count] != PSMark.Instance)
                count++;
            if (count == _count) throw new NotImplementedException("unmatchedmark error");
            Push(new PSInt(count - 1));
        }

        /// <summary>
        /// exchanges the top two elements on the operand stack.
        /// any1  any2 exch any2  any1
        /// </summary>
        void OP_exch()
        {
            var val = Pop();
            var tmp = Pop();
            Push(val);
            Push(tmp);
        }

        /// <summary>
        /// anyn−1  …  any0  n  j  roll  any (j−1) mod n  …  any0  anyn−1  …  anyj mod n
        /// </summary>
        void OP_roll()
        {
            int j = PopInt();
            int n = PopInt();
            if (n == 0 || j == 0) return;
            if (n < 0) throw new NotImplementedException("rangecheck error");
            int start = _count - n;
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
            var last = _operand[_count - 1];

            //Copies array one element up (-1 to not copy the last item)
            Array.Copy(_operand, start, _operand, start + 1, _count - start - 1);

            //Inserts last in the first pos
            _operand[start] = last;
        }

        void backwards_roll(int start)
        {
            var first = _operand[start];
            Array.Copy(_operand, start + 1, _operand, start, _count - start - 1);
            _operand[_count - 1] = first;
        }

        #endregion

        #region Arithmetic operators

        void OP_abs()
        {
            PushNum(Math.Abs(PopNum()));
        }

        void OP_add()
        {
            PushNum(PopNum() + PopNum());
        }

        void OP_atan()
        {
            var den = PopNum();
            var angel = (Math.Atan2(PopNum(), den) * 180 / Math.PI) % 360;
            if (angel < 0) angel += 360;
            PushNum(angel);
        }

        void OP_ceiling()
        {
            PushNum(Math.Ceiling(PopNum()));
        }

        void OP_cos()
        {
            Push(new PSReal(Math.Cos(PopNum())));
        }

        void OP_cvi()
        {
            var itm = Pop();
            if (itm is PSInt)
                Push(itm);
            else if (itm is PSReal)
            {
                var r = ((PSReal)itm).Value;
                if (r < int.MinValue || r > int.MaxValue)
                    throw new NotImplementedException("rangecheck");
                Push(new PSInt((int)r));
            }
            else if (itm is PSString)
            {
                var str = ((PSString)itm).GetString();
                int i;
                if (!int.TryParse(str, out i))
                    throw new NotImplementedException("undefinedresult");
                Push(new PSInt(i));
            }
            else
            {
                throw new NotImplementedException("typecheck");
            }

        }

        void OP_cvr()
        {
            var itm = Pop();
            if (itm is PSReal)
                Push(itm);
            else if (itm is PSInt)
                Push(new PSReal(((PSInt)itm).Value));
            else if (itm is PSString)
            {
                var str = ((PSString)itm).GetString();
                double d;
                if (!double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
                    throw new NotImplementedException("undefinedresult");
                Push(new PSReal(d));
            }
            else
            {
                throw new NotImplementedException("typecheck");
            }

        }

        void OP_cvx()
        {
            var itm = Pop();
            itm.Executable = true;
            Push(itm);
        }

        void OP_div()
        {
            var d = PopNum();
            if (d == 0) throw new NotImplementedException("undefinedresult");
            Push(new PSReal(PopNum() / d));
        }

        void OP_exp()
        {
            var exp = PopNum();
            Push(new PSReal(Math.Pow(PopNum(), exp)));
        }

        void OP_floor()
        {
            PushNum(Math.Floor(PopNum()));
        }

        void OP_idiv()
        {
            var d = PopInt();
            if (d == 0) throw new NotImplementedException("undefinedresult");
            Push(new PSInt(PopInt() / d));
        }

        void OP_ln()
        {
            Push(new PSReal(Math.Log(PopNum())));
        }

        void OP_log()
        {
            Push(new PSReal(Math.Log10(PopNum())));
        }

        void OP_mod()
        {
            var d = PopInt();
            if (d == 0) throw new NotImplementedException("undefinedresult");
            Push(new PSReal(PopInt() % d));
        }

        void OP_mul()
        {
            PushNum(PopNum() * PopNum());
        }

        void OP_neg()
        {
            var n = Pop();
            if (n is PSInt)
            {
                var v = ((PSInt)n).Value;
                Push(new PSInt((v < 0) ? -v : v));
            }
            else if (n is PSReal)
            {
                var v = ((PSReal)n).Value;
                Push(new PSReal((v < 0) ? -v : v));
            }
            else
                throw new NotImplementedException("typecheck");
        }

        void OP_round()
        {
            var num = PopNum();
            var inum = (int) num;
            var fract = num - inum;
            if (fract >= 0.5)
                Push(new PSInt(inum + 1));
            else
                Push(new PSInt(inum));
        }
#if SAVE_SUP
        void OP_save()
        {
            //Todo: Save object should also go in the VM
            Push(_vm.Save());
        }
#endif

        void OP_sin()
        {
            Push(new PSReal(Math.Sin(PopNum() * Math.PI / 180)));
        }

        void OP_sqrt()
        {
            var num = PopNum();
            if (num < 0) throw new NotImplementedException("rangecheck");
            Push(new PSReal(Math.Sqrt(PopNum())));
        }

        void OP_sub()
        {
            var v = PopNum();
            PushNum(PopNum() - v);
        }

        void OP_truncate()
        {
            Push(new PSInt((int)PopNum()));
        }

        #endregion

        #region Array Operators

        /// <summary>
        /// Creates an array of null objects
        /// </summary>
        void OP_array()
        {
#if SAVE_SUP
            Push(_vm[new PSArray(new PSItem[PopInt()])]);
#else
            Push(new PSArray(new PSItem[PopInt()]));
#endif
        }

        /// <summary>
        /// array index get any
        /// packedarray index get any
        /// dict key get any
        /// string index get int
        /// </summary>
        void OP_get()
        {
            PSItem i = Pop(); //Index or key
            PSItem s = Pop();

            if (s is PSDictionary)
                Push(((PSDictionary)s).Get(((PSName)i).Value));
            else
                throw new NotImplementedException("OP_get");
        }

        /// <summary>
        /// array index any put –
        /// dict key any put –
        /// string index int put –
        /// </summary>
        void OP_put()
        {
            var val = Pop(); // "any" or int
            var tmp = Pop(); // index or key
            var obj = Pop(); // array, dict or string
            if (obj is PSArray)
            {
                if (!(tmp is PSInt)) throw new IndexOutOfRangeException();
                ((PSArray)obj).SetPS(((PSInt)tmp).Value, val);
            }
            else if (obj is PSDictionary)
            {
                if (!(tmp is PSName)) throw new IndexOutOfRangeException();
                ((PSDictionary)obj).Add((PSName)tmp, val);
            }
            else
                throw new NotImplementedException("put into string");
        }

        #endregion

        #region Boolean and bitwise operators

        void OP_and()
        {
            var itm = Pop();
            if (itm is PSInt)
            {
                var i = ((PSInt)itm).Value;
                Push(new PSInt(PopInt() & i));
            }
            else if (itm is PSBool)
            {
                var b = ((PSBool)itm).Value;
                Push(new PSBool(b && PopBool()));
            }
            else
            {
                throw new NotImplementedException("typecheck");
            }
        }

        void OP_bitshift()
        {
            int bit = PopInt();
            Push(new PSInt(PopInt() << bit));
        }

        void OP_eq()
        {
            //Equals are overriden to behave as the specs want
            Push(new PSBool(Pop().Equals(Pop())));
        }

        void OP_ge()
        {
            var second = Pop();
            if (second is PSInt)
                Push(new PSBool(PopNum() >= ((PSInt)second).Value));
            else if (second is PSReal)
                Push(new PSBool(PopNum() >= ((PSReal)second).Value));
            else if (second is PSString)
                Push(new PSBool(PopStrObj().GreaterThan((PSString)second) >= 0));
            else
                throw new NotImplementedException("typecheck");
        }

        /// <summary>
        /// num1  num2 gt  bool
        /// string1  string2 gt  bool
        /// </summary>
        void OP_gt()
        {
            var second = Pop();
            if (second is PSInt)
                Push(new PSBool(PopNum() > ((PSInt)second).Value));
            else if (second is PSReal)
                Push(new PSBool(PopNum() > ((PSReal)second).Value));
            else if (second is PSString)
                Push(new PSBool(PopStrObj().GreaterThan((PSString)second) > 0));
            else
                throw new NotImplementedException("typecheck");
        }

        void OP_le()
        {
            var second = Pop();
            if (second is PSInt)
                Push(new PSBool(PopNum() <= ((PSInt)second).Value));
            else if (second is PSReal)
                Push(new PSBool(PopNum() <= ((PSReal)second).Value));
            else if (second is PSString)
                Push(new PSBool(PopStrObj().GreaterThan((PSString)second) <= 0));
            else
                throw new NotImplementedException("typecheck");
        }

        /// <summary>
        /// Returns the length of a containter
        /// 
        /// array length  int
        /// packedarray length  int
        /// dict length  int
        /// string length  int
        /// name length  int
        /// </summary>
        void OP_length()
        {
            var itm = Pop();
            if (itm is PSArray)
                PushNum(((PSArray)itm).Length);
            else if (itm is PSDictionary)
                PushNum(((PSDictionary)itm).Catalog.Count);
            else if (itm is PSString)
                PushNum(((PSString)itm).Length);
            else if (itm is PSName)
                PushNum(((PSName)itm).Value.Length);
            else
                throw new NotImplementedException("packedarray length  int");
        }

        void OP_lt()
        {
            var second = Pop();
            if (second is PSInt)
                Push(new PSBool(PopNum() < ((PSInt)second).Value));
            else if (second is PSReal)
                Push(new PSBool(PopNum() < ((PSReal)second).Value));
            else if (second is PSString)
                Push(new PSBool(PopStrObj().GreaterThan((PSString)second) < 0));
            else
                throw new NotImplementedException("typecheck");
        }

        void OP_ne()
        {
            Push(new PSBool(!Pop().Equals(Pop())));
        }

        /// <summary>
        /// returns the logical negation of the operand if it is boolean. If the operand is an
        /// integer, not returns the bitwise complement (ones complement) of its binary rep-
        /// resentation. 
        /// </summary>
        void OP_not()
        {
            var val = Pop();
            if (val is PSBool)
                Push(new PSBool(!((PSBool)val).Value));
            else if (val is PSInt)
                Push(new PSInt(~((PSInt)val).Value));
            else throw new NotSupportedException("typecheck");
        }

        void OP_or()
        {
            var itm = Pop();
            if (itm is PSInt)
            {
                var i = ((PSInt)itm).Value;
                Push(new PSInt(PopInt() | i));
            }
            else if (itm is PSBool)
            {
                var b = ((PSBool)itm).Value;
                Push(new PSBool(b || PopBool()));
            }
            else
            {
                throw new NotImplementedException("typecheck");
            }
        }

        void OP_xor()
        {
            var itm = Pop();
            if (itm is PSInt)
            {
                var i = ((PSInt)itm).Value;
                Push(new PSInt(PopInt() ^ i));
            }
            else if (itm is PSBool)
            {
                var b = ((PSBool)itm).Value;
                Push(new PSBool(b ^ PopBool()));
            }
            else
            {
                throw new NotImplementedException("typecheck");
            }
        }

        #endregion

        #region Control Operators

        /// <summary>
        /// bool  proc if  –
        /// </summary>
        void OP_if()
        {
            var proc = PopProc();
            if (PopBool())
                Run(proc);
        }

        /// <summary>
        /// bool  proc1  proc2  ifelse
        /// </summary>
        void OP_ifelse()
        {
            var proc2 = PopProc();
            var proc1 = PopProc();

            if (PopBool())
                Run(proc1);
            else
                Run(proc2);
        }

        /// <summary>
        /// initial increment limit proc for –
        /// </summary>
        void OP_for()
        {
            PSProcedure proc = (PSProcedure)Pop();
            int end = PopInt();
            int inc = PopInt();
            int start = PopInt();
            if (inc == 0) throw new NotSupportedException();
            if (inc > 0)
            {
                while (start <= end)
                {
                    Push(new PSInt(start));

                    Run(proc);

                    start += inc;
                }
            }
            else
            {
                while (start >= end)
                {
                    Push(new PSInt(start));

                    Run(proc);

                    start -= inc;
                }
            }
        }

        #endregion

        #region Dictionary operators

        /// <summary>
        /// Pushes the current dict onto the dict stack
        /// </summary>
        void OP_begin()
        {
            _dict_stack.Push((PSDictionary)Pop());
        }

        /// <summary>
        /// Pushes the current dictionary onto the op stack
        /// </summary>
        void OP_currentdict()
        {
            Push(_dict_stack.Peek());
        }

        /// <summary>
        /// Puts item in dictionary
        /// </summary>
        void OP_def()
        {
            var val = Pop();
#if DEBUG
            if (!(Peek() is PSName))
                ToString();
#endif
            _dict_stack.Peek().Add((PSName)Pop(), val);
        }

        /// <summary>
        /// Creates a new dictionary with a capacity given by the parameter
        /// </summary>
        void OP_dict()
        {
#if SAVE_SUP
            Push(_vm[new PSDictionary(PopInt())]);
#else
            Push(new PSDictionary(PopInt()));
#endif
        }

        /// <summary>
        /// Pops a dictionary
        /// </summary>
        void OP_end()
        {
            if (_dict_stack.Count <= 3)
            {
                if (_ll == LangLevel.One && _dict_stack.Count <= 2 ||
                    _ll > LangLevel.One)
                    //It's not allowed to pop the user dict off the stack
                    throw new NotSupportedException();
            }
            _dict_stack.Pop();
        }

        /// <summary>
        /// returns true if there is an entry in the dictionary dict whose key is key
        /// </summary>
        void OP_known()
        {
            //dict  key  known  bool
            var key = (PSName)Pop();
            var dict = (PSDictionary)Pop();

            Push(new PSBool((dict.Catalog.ContainsKey(key.Value))));
        }

        /// <summary>
        /// Pushes systemdict onto the OP stack
        /// </summary>
        void OP_systemdict()
        {
            Push(_system_dict);
        }

        /// <summary>
        /// Pushes the user dictionary onto the OP stack
        /// </summary>
        void OP_userdict()
        {
            Push(_dict_stack[_dict_stack.Count - ((_ll == LangLevel.One) ? 2 : 3)]);
        }

        #endregion

        #region File operators

        /// <summary>
        /// Closes a file
        /// </summary>
        void OP_closefile()
        {
            //Files are not supported by this interpreter, so we
            //just turn off encryption
            PSFile file = (PSFile)Pop();
            file.DecryptFile = false;
            //var test = file.DebugString;

            file.ReadByte(); //<-- Note that the spec allows for "overconsumption" of encrypted data.
                             //    so it's better to read too much than too little. (Keep in mind that
                             //    two bytes of is cached, so we clear that out at least.)
            file.ReadByte();

            //Ends the system dictionary
            OP_end();


            //Should be file be closed propperly? Some logic must be added to return EoD in that case,
            //as one only get null pointer or closed file exceptions otherwise.
            //file.Close();
        }

        void OP_currentfile()
        {
            //Todo. Also I suppose save/restore should restore the position in the stream
            Push(new PSFile(_lex));
        }

        /// <summary>
        /// Turns on decryption
        /// </summary>
        void OP_eexec()
        {
            var val = Pop();
            if (val is PSFile)
            {
                //The specs say that a new file with the decrypted content is
                //to be created. Then when that file is closed, execution resumes
                //from the org file (after the encrypted data)
                var file = (PSFile)val;
                //file.ReadByte(); //<-- Skips padding character. (Todo: Not 100% sure about this)
                file.DecryptFile = true;
                _dict_stack.Push(_system_dict);
            }
            else if (val is PSString)
            {
                //Decrypt string and execute it
                throw new NotImplementedException("String decryption");
            }
            else
                throw new NotSupportedException();
        }

        /// <summary>
        /// Executes an abitrary item
        /// </summary>
        void OP_exec()
        {
            Execute(Pop());
        }

        /// <summary>
        /// file string readstring substring bool
        /// </summary>
        void OP_readstring()
        {
            var str = (PSString)Pop();
            var file = (PSFile)Pop();

            //Removes padding after operator
            //file.ReadByte();

            bool done = true;
            for (int c = 0; c < str.Length; c++)
            {
                int b = file.ReadByte();
                if (b == -1)
                {
                    //Todo: Set rangecheck error
                    done = false;
                }

                str[c] = (char)b;
            }

            Push(str);
            Push(new PSBool(done));
        }

        #endregion

        #region Glyph and Font operators

        /// <summary>
        /// key font definefont font
        /// key cidfont definefont cidfont
        /// </summary>
        void OP_definefont()
        {
            var font = (PSDictionary) Pop();
            var key = Pop();

            //definefont distinguishes between a CIDFont and a font by the presence or absence
            //of a CIDFontType entry.
            if (font.Catalog.ContainsKey("CIDFontType"))
                throw new NotImplementedException("CIDFontType");

            if (font.Access == PSAccess.None)
                throw new NotSupportedException();

            //Inserts a fontID object. I simply use a number, for now at least.
            font.Add(new PSName("fontID"), new PSInt(font.Catalog.Count));
            font.Access = PSAccess.ReadOnly;

            //Adds the font to the font dict
            FontDictionary.Add((PSName)key, font);;
            Push(font);
        }

        void OP_std_enc()
        {
#if SAVE_SUP
            Push(_vm[new PSArray(Pdf.Font.Enc.Standar, true)]);
#else
            Push(new PSArray(Pdf.Font.Enc.Standar, true));
#endif
        }

        #endregion

        #region Type, Attribute, and Conversion operators

        /// <summary>
        /// array executeonly array
        /// packedarray executeonly packedarray
        /// file executeonly file
        /// string executeonly string
        /// </summary>
        void OP_executeonly()
        {
            var val = Pop();
            if (!(val is PSObject) || val is PSDictionary) throw new NotSupportedException();

#if SAVE_SUP
            var obj = _vm[(PSObject)val.ShallowClone()];
#else
            var obj = (PSObject)val.ShallowClone();
#endif
            if (obj.Access != PSAccess.None)
                obj.Access = PSAccess.ExecuteOnly;
            Push(obj);
        }

        /// <summary>
        /// Sets the none flag on arrays, strings, etc
        /// </summary>
        void OP_noaccess()
        {
            var val = Pop();
            if (!(val is PSObject)) throw new NotSupportedException();

            //For non-dict objects the attribute is only to be set on a cloned
            //object (a dictionary’sa ccess attribute is a property of the value, 
            //so multiple dictionary objects sharing the same value have the same 
            //access attribute.

            if (!(val is PSDictionary))
#if SAVE_SUP
                val = _vm[(PSObject)val.ShallowClone()];
#else
                val = (PSObject)val.ShallowClone();
#endif

            var obj = (PSObject)val;
            obj.Access = PSAccess.None;
            Push(obj);
        }

        /// <summary>
        /// Sets the readonly flag on arrays, strings, etc
        /// </summary>
        void OP_readonly()
        {
            var val = Pop();
            if (!(val is PSObject)) throw new NotSupportedException();

            //For non-dict objects the attribute is only to be set on a cloned
            //object (a dictionary’sa ccess attribute is a property of the value, 
            //so multiple dictionary objects sharing the same value have the same 
            //access attribute.

            if (!(val is PSDictionary))
#if SAVE_SUP
                val = _vm[(PSObject) val.ShallowClone()];
#else
                val = (PSObject) val.ShallowClone();
#endif

            var obj = (PSObject)val;
            if (obj.Access == PSAccess.Unlimited)
                obj.Access = PSAccess.ReadOnly;
            Push(obj);
        }

        #endregion

        #region String operators

        /// <summary>
        /// creates a string of length int
        /// </summary>
        void OP_string()
        {
            Push(new PSString(new char[PopInt()]));
        }

        #endregion

        #endregion

        #region Level 2 operators

        void OP_findresource()
        {
            //Gets the catagory
            var cat = (PSDictionary) _resources[PopName()];

            //Gets the key
            var key = cat[PopName()];

            //Push result onto the operand stack
            Push(key);
        }

        void OP_defineresource()
        {
            var cat = (PSDictionary)_resources[PopName()];
            var instance = Pop();
            var key = (PSName) Pop();
            cat.Add(key, instance);
            Push(instance);
        }

        #endregion

        #region CMap operators

        /// <summary>
        /// Starts a CMap definition
        /// </summary>
        void OP_begincmap()
        {
            //Makes the top dict into a PSCmap
            _dict_stack.Push(new PSCMap(_dict_stack.Pop()));
        }

        /// <summary>
        /// Ends a CMap definition
        /// </summary>
        void OP_endcmap()
        {
            var cmap = (PSCMap) _dict_stack.Peek();
            cmap.Init();
        }

        /// <summary>
        /// Defines the valid input ranges. Codes outside
        /// these ranges are rejected.
        /// </summary>
        void OP_begincodespacerange()
        {
            var ranges = new CodeRange[PopInt()];

            //Reads in the ranges
            for (int c = 0; c < ranges.Length; c++)
                ranges[c] = new CodeRange(_lex.ReadHex(), _lex.ReadHex());
            

            //Must be followed with "endcodespacerange"
            CheckNextKeyword("endcodespacerange");

            //Assumption, the top dict is the same
            //dict that was on the top of the stack
            //when OP_begincmap was run. If this
            //assumption proves wrong, store away
            //the begincmap dictionary somewhere.
            var cmap = _dict_stack.Peek();

            //There can be multiple codespaceranges, but
            //PSCodeMap handles that
            PSCodeMap.Add(cmap, ranges);
        }

        /// <summary>
        /// Defines mapping from character codes to character ids
        /// </summary>
        void OP_begincidrange()
        {
            var ranges = new CidRange[PopInt()];

            for (int c = 0; c < ranges.Length; c++)
                ranges[c] = new CidRange(_lex.ReadHex(), _lex.ReadHex(), _lex.ReadInt());

            CheckNextKeyword("endcidrange");

            //There can be multiple cidranges, but they all go together
            var cmap = _dict_stack.Peek();
            PSCodeMap.Add(cmap, ranges);
        }

        /// <summary>
        /// Defines mapping from character codes to unicode character codes or names
        /// </summary>
        void OP_beginbfrange()
        {
            var ranges = new CharRange[PopInt()];

            for (int c = 0; c < ranges.Length; c++)
            {
                byte[] from = _lex.ReadHex(), to = _lex.ReadHex();
                string name = null; byte[] num = null;
                switch (_lex.SetNextToken())
                {
                    case PSType.Name:
                        name = _lex.GetName();
                        break;
                    case PSType.HexString:
                        num = _lex.GetHex();
                        break;

                    case PSType.BeginArray:
                        {
                            //The number of enteries:
                            int ifrom = PSCodeMap.BaToInt(from), size = PSCodeMap.BaToInt(to);

                            //Adds one entery per character.
                            Array.Resize<CharRange>(ref ranges, ranges.Length + size - 1);

                            for (int k = 0; k < size; k++)
                            {
                                switch (_lex.SetNextToken())
                                {
                                    case PSType.Name:
                                        name = _lex.GetName();
                                        break;
                                    case PSType.HexString:
                                        num = _lex.GetHex();
                                        break;

                                    default: throw new PSReadException(PSType.None, ErrCode.WrongType);
                                }

                                ranges[c++] = new CharRange(ifrom, from.Length, ifrom, num, name);
                                ifrom++;
                            }

                            if (_lex.SetNextToken() != PSType.EndArray)
                                throw new PSReadException(PSType.EndArray, ErrCode.WrongType);
                        }
                        continue;

                    default: throw new PSReadException(PSType.None, ErrCode.WrongType);
                }   

                ranges[c] = new CharRange(from, to, num, name);
            }

            CheckNextKeyword("endbfrange");

            //There can be multiple cidranges, but they all go together
            var cmap = _dict_stack.Peek();
            PSCodeMap.Add(cmap, ranges);
        }

        /// <summary>
        /// Defines .notdef mappings from character codes to CID
        /// </summary>
        void OP_beginnotdefrange()
        {
            var ranges = new CidRange[PopInt()];
            for (int c = 0; c < ranges.Length; c++)
                ranges[c] = new CidRange(_lex.ReadHex(), _lex.ReadHex(), _lex.ReadInt());
            CheckNextKeyword("endnotdefrange");
            var cmap = _dict_stack.Peek();
            PSCodeMap.AddNDef(cmap, ranges);
        }

        /// <summary>
        /// Maps from character codes to character code (or name). in the assosiated font.
        /// 
        /// I.e. mapping is "Charcode" -> "Charcode or name" -> into font
        /// </summary>
        void OP_beginbfchar()
        {
            var chars = new CharToCharMap[PopInt()];
            for (int c = 0; c < chars.Length; c++)
            {
                var charcode = _lex.ReadHex();
                if (_lex.SetNextToken() == PSType.Name)
                    chars[c] = new CharToCharMap(charcode, _lex.GetName());
                else
                    chars[c] = new CharToCharMap(charcode, _lex.GetHex());
            }
            CheckNextKeyword("endbfchar");
            var cmap = _dict_stack.Peek();
            PSCodeMap.Add(cmap, chars);
        }

        /// <summary>
        /// Mapping of an induvidual character code to CID
        /// </summary>
        void OP_begincidchar()
        {
            var chars = new CidChar[PopInt()];
            for (int c = 0; c < chars.Length; c++)
                chars[c] = new CidChar(_lex.ReadHex(), _lex.ReadInt());
            CheckNextKeyword("endcidchar");
            var cmap = _dict_stack.Peek();
            PSCodeMap.Add(cmap, chars);
        }

        /// <summary>
        /// Mapping of induvidual CID .notdef chars
        /// </summary>
        void OP_beginnotdefchar()
        {
            var chars = new CidChar[PopInt()];
            for (int c = 0; c < chars.Length; c++)
                chars[c] = new CidChar(_lex.ReadHex(), _lex.ReadInt());
            CheckNextKeyword("endnotdefchar");
            var cmap = _dict_stack.Peek();
            PSCodeMap.AddNDef(cmap, chars);
        }

        /// <summary>
        /// Incorporates the code mappings from another CMap resource
        /// </summary>
        void OP_usecmap()
        {
            var name = PopName();

            //Presumably only built in cmaps can be fetched this way
            try
            {
                using (var cmap = PdfLib.Res.StrRes.GetBinResource("Cmap." + name))
                {
                    if (cmap != null)
                    {
                        var ps = new PSInterpreter() { LanguageLevel = LangLevel.Three };
                        ps.Run(cmap);

                        var pscmap = ps.GetCMap(name);
                        var main_cmap = _dict_stack.Peek();
                        PSCodeMap.Add(main_cmap, pscmap);

                        return;
                    }
                }
            }
            catch (System.IO.FileNotFoundException) { }

            throw new PSCastException("Could not create CMap: "+name);
        }

        void CheckNextKeyword(string keyword)
        {
            if (_lex.SetNextToken() != PSType.Keyword)
                throw new PSParseException(PSType.Keyword, ErrCode.WrongType);
            if (!keyword.Equals(_lex.Token))
                throw new PSParseException(PSType.Keyword, ErrCode.Wrong);
        }

        #endregion

        #endregion

        #region Methods only used externaly

        /// <summary>
        /// Gets a CMap out of the interpretor
        /// </summary>
        public PSCMap GetCMap(string name)
        {
            Debug.Assert(_ll == LangLevel.Three);
            var cdict = (PSDictionary)_resources.Catalog["CMap"];
            return (PSCMap) cdict.Catalog[name];
        }

        /// <summary>
        /// Searches for a cmap and returns that
        /// </summary>
        public PSCMap GetCMap()
        {
            Debug.Assert(_ll == LangLevel.Three);
            var cdict = (PSDictionary)_resources.Catalog["CMap"];
            foreach (var kp in cdict.Catalog)
                if (kp.Value is PSCMap)
                    return (PSCMap)kp.Value;
            throw new PSReadException(PSType.BeginDictionary, ErrCode.Missing);
        }

        /// <summary>
        /// Pushes a number onto the operand stack. Will be converted
        /// to integer when appropriate.
        /// </summary>
        public void PushNum(double num)
        {
            var inum = (int)num;
            if (num == inum)
                Push(new PSInt(inum));
            else
                Push(new PSReal(num));
        }

        /// <summary>
        /// Pops a procedure of the stack
        /// </summary>
        public PSProcedure PopProc()
        {
            var ret = Pop();
            if (ret is PSProcedure)
            {
                var proc = (PSProcedure)ret;
                return proc;
            }
            throw new PSCastException();
        }

        /// <summary>
        /// Replaces PSOperands with their callbacks, allowing
        /// for speedier execution, but this will break on
        /// scripts that alters dictionaries.
        /// </summary>
        /// <remarks>Used by PostScriptFunctions</remarks>
        public void MakeFast(PSProcedure proc)
        {
            var items = proc.Items;
            for (int c = 0; c < items.Length; c++)
            {
                var item = items[c];
                if (item is PSOperator)
                    items[c] = GetExecuteObj((PSOperator)item);
                else if (item is PSProcedure)
                    MakeFast((PSProcedure)item);
            }
        }

        /// <summary>
        /// Pops a number of the operand stack
        /// </summary>
        public double PopNum()
        {
            var ret = Pop();
            if (ret is PSInt)
                return ((PSInt)ret).Value;
            if (ret is PSReal)
                return ((PSReal)ret).Value;
            /*if (ret is PSBool)
                return ((PSBool)ret).Value ? 1 : 0;*/
            throw new PSCastException();
        }

        /// <summary>
        /// Pops a boolean of the stack
        /// </summary>
        public bool PopBool()
        {
            var ret = Pop();
            if (ret is PSBool)
                return ((PSBool)ret).Value;
            throw new PSCastException();
        }

        public PSObject PopObject()
        {
            var ret = Pop();
            if (ret is PSObject)
                return ((PSObject)ret);
            throw new PSCastException();
        }

        /// <summary>
        /// Pushes an item onto the op stack
        /// </summary>
        public void PushItem(PSItem itm)
        {
            Push(itm);
        }

        #endregion

        /// <summary>
        /// Executes a PostScript operator
        /// </summary>
        public void Execute(string str)
        {
            //Looks through the dictionary stack until command is found.
            for (int c = 0; c < _dict_stack.Count; c++)
            {
                var dict = _dict_stack[c];
                
                PSItem itm;
                if (dict.Catalog.TryGetValue(str, out itm))
                {
                    //Note that items pulled out of a dictionary is to be executed in
                    //a different way to items going through the "Execute" function
                    if (itm.Executable)
                    {
                        if (itm is PSCallback)
                            ((PSCallback)itm).Action();
                        else if (itm is PSObject)
                            Run((PSObject)itm);
                        else
                            throw new NotImplementedException("Executable non-objects");
                    }
                    else
                        Execute(itm);
                    return;
                }
            }

            throw new NotImplementedException("Command: " + str);
        }

        /// <summary>
        /// Fetches the object the object a PSOperator would execute
        /// </summary>
        /// <remarks>
        /// Object to execute, or the original op if no object was found.
        /// </remarks>
        public PSItem GetExecuteObj(PSOperator op)
        {
            //Looks through the dictionary stack until command is found.
            for (int c = 0; c < _dict_stack.Count; c++)
            {
                var dict = _dict_stack[c];

                PSItem itm;
                if (dict.Catalog.TryGetValue(op.Operator, out itm))
                    return itm;
            }

            return op;
        }

        #region Tokenizing

        private PSItem ReadType(PSType t)
        {
            switch (t)
            {
                case PSType.Integer:
                    return new PSInt(_lex.GetInteger());

                case PSType.Real:
                    return new PSReal(_lex.GetReal());

                case PSType.String:
                    return new PSString(_lex.RawToken, false);
                case PSType.HexString:
                    return new PSString(_lex.RawToken, true);

                case PSType.Name:
                    return new PSName(_lex.GetName());
#if SAVE_SUP
                case PSType.BeginArray:
                    return _vm[ReadArrayImpl()];

                case PSType.BeginProcedure:
                    return _vm[ReadProcedureImpl()];
#else
                case PSType.BeginArray:
                    return ReadArrayImpl();

                case PSType.BeginProcedure:
                    return ReadProcedureImpl();

                case PSType.BeginDictionary:
                    return ReadDictImpl();
#endif
                case PSType.EOF:
                    throw new PSReadException(PSType.EOF, ErrCode.UnexpectedEOF);

                default:
                    throw new PSReadException(t, ErrCode.UnexpectedToken);
            }
        }

        /// <summary>
        /// Reads a dictionary from the stream
        /// </summary>
        private PSDictionary ReadDictImpl()
        {
            var ret = new Dictionary<string, PSItem>();
            PSType t;
            while ((t = _lex.SetNextToken()) != PSType.EndDictionary)
            {
                //First read the key
                if (t != PSType.Name) throw new PSReadException(t, ErrCode.UnexpectedToken);
                string key = _lex.GetName();

                //Then the object or value. Note will throw a
                //unexpected token error if it's a EOF token
                t = _lex.SetNextToken();
                if (t == PSType.Keyword)
                    ret.Add(key, new PSOperator(_lex.Token));
                else
                    ret.Add(key, ReadType(t));
            }
            return new PSDictionary(ret);
        }

        /// <summary>
        /// Reads an array from the stream
        /// </summary>
        private PSArray ReadArrayImpl()
        {
            var ret = new List<PSItem>();
            PSType t;
            while ((t = _lex.SetNextToken()) != PSType.EndArray)
                if (t == PSType.Keyword)
                    ret.Add(new PSOperator(_lex.Token));
                else
                    ret.Add(ReadType(t));
            return new PSArray(ret);
        }

        /// <summary>
        /// Reads a procedure from the stream
        /// </summary>
        private PSProcedure ReadProcedureImpl()
        {
            var ret = new List<PSItem>();
            PSType t;
            while ((t = _lex.SetNextToken()) != PSType.EndProcedure)
                if (t == PSType.Keyword)
                    ret.Add(new PSOperator(_lex.Token));
                else
                    ret.Add(ReadType(t));
            return new PSProcedure(ret);
        }

        #endregion

        #region Stack

        private PSItem Pop() { return _operand[--_count]; }
        private PSItem Peek() { return _operand[_count - 1]; }

        private PSString PopStrObj()
        {
            var ret = Pop();
            if (ret is PSString) return (PSString)ret;
            throw new NotImplementedException("typecheck");
        }

        private void Push(PSItem itm)
        {
            if (_operand.Length == _count)
                Array.Resize<PSItem>(ref _operand, _count + 1);
            _operand[_count++] = itm;
        }

        /// <summary>
        /// Pops a name off the stack
        /// </summary>
        [DebuggerStepThrough()]
        internal string PopName()
        {
            return ((PSName)Pop()).Value;
        }

        /// <summary>
        /// Pops a int off the stack
        /// </summary>
        [DebuggerStepThrough()]
        internal int PopInt()
        {
            return ((PSInt)Pop()).Value;
        }

        /// <summary>
        /// Pops a positive int off the stack
        /// </summary>
        [DebuggerStepThrough()]
        internal int PopUInt()
        {
            var ret = ((PSInt)Pop()).Value;
            if (ret >= 0) return ret;
            throw new IndexOutOfRangeException();
        }

        /// <summary>
        /// Pops a dictionaru off the stack
        /// </summary>
        [DebuggerStepThrough()]
        internal PSDictionary PopDict()
        {
            return (PSDictionary) Pop();
        }

        #endregion
    }

    public enum LangLevel
    {
        None,

        /// <summary>
        /// This is the default language level
        /// </summary>
        One,

        Two,

        /// <summary>
        /// Used for Cmaps, among other things.
        /// </summary>
        Three
    }
}

