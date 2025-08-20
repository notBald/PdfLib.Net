using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Read;
using PdfLib.Read.Parser;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Render;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Annotation;
using PdfLib.Compose;
using PdfLib.Compose.Text;
using PdfLib.Compile.Analyze;

namespace PdfLib.Compile
{
    public class PdfContentAnalyze
    {
        /// <remarks>
        /// This stack contains parameters for commands 
        /// </remarks>
        private Stack<object> _stack;

        /// <summary>
        /// Current state
        /// </summary>
        private PdfState _cs = new PdfState();
        Stack<PdfState> _state_stack = new Stack<PdfState>(32);

        /// <remarks>
        /// This stack contains marked contents information 
        /// </remarks>
        private Stack<MCInfo> _mc_stack;

        /// <summary>
        /// Used for parsing objects.
        /// </summary>
        private IIParser _parse;

        /// <summary>
        /// Used for parsing command keywords.
        /// </summary>
        private Lexer _snize;

        /// <summary>
        /// Current graphic state
        /// </summary>
        private GS _gs;

        /// <summary>
        /// Precision detected during compiling
        /// </summary>
        private int _detected_precision;

        /// <summary>
        /// Includes a copy of all state information before
        /// the command is to be executed.
        /// </summary>
        private bool _include_state = true;

        public void Analyze(PdfContentArray content)
        {
            if (content == null) throw new ArgumentNullException();

            _snize = new Lexer(content.Contents);
            _parse = new Parser(null, _snize, null);

            Parse(new List<AnalyzeCMD>(1024));
        }

        public void Analyze(PdfContent content)
        {
            if (content == null) throw new ArgumentNullException();

            _snize = new Lexer(content.Content);
            _parse = new Parser(null, _snize, null);

            Parse(new List<AnalyzeCMD>(1024));
        }

        private void Parse(IList<AnalyzeCMD> cmds)
        {
            PdfState? state = null;
            _stack = new Stack<object>(16);

            while (true)
            {
                if (_include_state)
                    state = _cs;

                AnalyzeCMD cmd = parseCommand();

                if (cmd == null)
                    break;

                if (_include_state)
                    cmd.State = state;

                cmds.Add(cmd);
            }

            cmds[1].ToString();
        }

        private AnalyzeCMD parseCommand()
        {
            PdfType tok;
            //_ws = WorkState.Build;
            while ((tok = _snize.SetNextToken()) != PdfType.EOF)
            {
                switch (tok)
                {
                    case PdfType.Keyword:
                        int tok_length = _snize.TokenLength;
                        if (tok_length >= 4)
                        {
                            var tok_str = _snize.Token;
                            if ("true".Equals(tok_str)) _stack.Push(true);
                            else if ("false".Equals(tok_str)) _stack.Push(null);
                            else if ("null".Equals(tok_str)) _stack.Push(null);
                            else goto default;
                        }
                        else
                        {
                            //_ws = WorkState.Exe;
                            return CreateCommand();
                            //if (cmd != null) return cmd;
                            //_ws = WorkState.Build;
                        }
                        continue;

                    case PdfType.Integer:
                        _stack.Push(_snize.GetInteger());
                        continue;

                    case PdfType.Real:
                        _stack.Push(GetReal());
                        continue;

                    case PdfType.Name:
                        _stack.Push(new PdfName(_snize.GetName()));
                        continue;

                    case PdfType.String:
                        _stack.Push(new PdfString(_snize.RawToken, false));
                        continue;

                    case PdfType.HexString:
                        _stack.Push(new PdfString(_snize.RawToken, true));
                        continue;

                    //Not using the parser for reading arrays as only simple
                    //objects are allowed.
                    case PdfType.BeginArray:
                        //Should nested arrays be supported?
                        List<object> ar = new List<object>();
                        while ((tok = _snize.SetNextToken()) != PdfType.EndArray)
                        {
                            //Todo: Should I get integers as integers?
                            if (tok == PdfType.Integer || tok == PdfType.Real)
                                ar.Add(GetReal());
                            else if (tok == PdfType.String)
                                ar.Add(new PdfString(_snize.RawToken, false));
                            else if (tok == PdfType.HexString)
                                ar.Add(new PdfString(_snize.RawToken, true));
                            else if (tok == PdfType.Name)
                                ar.Add(new PdfName(_snize.GetName()));
                            else if (tok == PdfType.Keyword)
                            {
                                switch (_snize.Token)
                                {
                                    case "true":
                                        ar.Add(true); break;
                                    case "false":
                                        ar.Add(false); break;
                                    case "null":
                                        ar.Add(null); break;

                                    default:
                                        throw new PdfLogException(ErrSource.Compiler, PdfType.Keyword, ErrCode.Illegal);
                                }
                            }
                            else
                            {
                                throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                            }
                        }
                        _stack.Push(ar);
                        continue;

                    case PdfType.BeginDictionary:
                        _stack.Push(_parse.ReadItem(PdfType.BeginDictionary));
                        continue;

                    default:
                        throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                }
            }

            return null;
        }

        /// <summary>
        /// Parses commands
        /// </summary>
        /// <remarks>
        /// See 8.2 in the specs.
        /// </remarks>
        private AnalyzeCMD CreateCommand()
        {
            //Page 111 in the specs
            var cmd_str = _snize.Token;
            switch (cmd_str)
            {
                #region Special graphics state

                case "q": //Push state
                    _state_stack.Push(_cs);
                    if (_stack.Count != 0 || _gs != GS.Page)
                        return new q_CMD(ToArray());
                    return new q_CMD();

                case "Q": //Pop state
                    if (_state_stack.Count == 0)
                        return new Q_CMD(_stack.ToArray());
                    _cs = _state_stack.Pop();
                    if (_stack.Count != 0 || _gs != GS.Page)
                        return new Q_CMD(ToArray());
                    return new Q_CMD();

                #endregion

                default:
                    return new UnknownCMD(cmd_str, ToArray());
            }
        }

        private object[] ToArray()
        {
            var obj = _stack.ToArray();
            _stack.Clear();
            return obj;
        }

        public static void Analyze(PdfPage page)
        {
            var ca = page.Contents;
            if (ca != null)
               new PdfContentAnalyze().Analyze(ca);
        }


        private double GetReal()
        {
            var token = _snize.Token;
            var i = token.IndexOf('.');
            if (i != -1)
            {
                var dp = token.Length - i - 1;
                if (dp > _detected_precision)
                    _detected_precision = dp;
            }
            return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            //return _snize.GetReal();
        }

        /// <summary>
        /// Used to collect Marked Contents data
        /// </summary>
        struct MCInfo
        {
            public string Property;
            public PdfDictionary Dict;
            public int CmdPos;
            public GS GState;
            public MCInfo(string n, PdfDictionary d, int p, GS g)
            { Property = n; Dict = d; CmdPos = p; GState = g; }
        }
    }
}
