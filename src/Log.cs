using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Render;

namespace PdfLib
{
    public abstract class Log
    {
        #region Variables and properties

        private static Dictionary<int, Log> _logs = new Dictionary<int, Log>();
        private static Log _adapter = new LogAdapter();

        private static global::System.Resources.ResourceManager resourceMan;
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("PdfLib.Res.LogText", typeof(Log).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        #endregion

        #region Init

        private Log() { }

#if DEBUG
        static Log()
        {
            StartLogging();
        }

        public static void TestLog()
        {
            StartLogging();
            var log = GetLog();
            Info(ErrSource.File, LogEvent.Open, "Test.pdf");
            Log.Info(ErrSource.File, LogEvent.Encrypted);
            log.IgnoredCMD("Tf");
            Warn(GS.Page, "TJ");

            //General
            log.Add(ErrSource.General, PdfType.None, ErrCode.General);
            log.Add(ErrSource.Filter, PdfType.None, ErrCode.General);

            //Invalid
            log.Add(ErrSource.Header, PdfType.None, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.ColorSpace, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.Array, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.RealArray, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.NameArray, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.XObject, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.Date, ErrCode.Invalid);
            log.Add(ErrSource.General, PdfType.Name, ErrCode.Invalid);
            log.Add(ErrSource.Lexer, PdfType.Integer, ErrCode.Invalid);
            log.Add(ErrSource.Parser, PdfType.Stream, ErrCode.Invalid);
            log.Add(ErrSource.Xref, PdfType.Keyword, ErrCode.Invalid);

            //Missing
            log.Add(ErrSource.Header, PdfType.None, ErrCode.Missing);
            log.Add(ErrSource.Compiler, PdfType.XObject, ErrCode.Missing);
            log.Add(ErrSource.Compiler, PdfType.Pattern, ErrCode.Missing);
            log.Add(ErrSource.Compiler, PdfType.Shading, ErrCode.Missing);
            log.Add(ErrSource.Compiler, PdfType.Font, ErrCode.Missing);
            log.Add(ErrSource.General, PdfType.Item, ErrCode.Missing);
            log.Add(ErrSource.General, PdfType.String, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.DictArray, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.Array, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.Item, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.String, ErrCode.Missing);
            log.Add(ErrSource.Dictionary, PdfType.Integer, ErrCode.Missing);
            log.Add(ErrSource.Stream, PdfType.Item, ErrCode.Missing);
            log.Add(ErrSource.Xref, PdfType.Trailer, ErrCode.Missing);
            log.Add(ErrSource.General, PdfType.Trailer, ErrCode.Missing);
            log.Add(ErrSource.General, PdfType.FontFile3, ErrCode.Missing);
            

            //Wrong
            log.Add(ErrSource.General, PdfType.ColorSpace, ErrCode.Wrong);
            log.E(ErrSource.Filter, ErrCode.Wrong, "Mystical error");
            log.Add(ErrSource.Cast, PdfType.Destination, ErrCode.Wrong);
            log.Add(ErrSource.Cast, PdfType.XObject, ErrCode.Wrong);
            log.Add(ErrSource.Xref, PdfType.Keyword, ErrCode.Wrong);
            log.Add(ErrSource.Parser, PdfType.Keyword, ErrCode.Wrong);

            //Illigal
            log.Add(ErrSource.Compiler, PdfType.Keyword, ErrCode.Illegal);

            //OutOfRange
            log.Add(ErrSource.Lexer, PdfType.Integer, ErrCode.OutOfRange);
            log.Add(ErrSource.General, PdfType.ColorSpace, ErrCode.OutOfRange);
            log.Add(ErrSource.General, PdfType.Integer, ErrCode.OutOfRange);
            log.Add(ErrSource.General, PdfType.CIDFont, ErrCode.OutOfRange);
            log.Add(ErrSource.General, PdfType.Name, ErrCode.OutOfRange);
            log.Add(ErrSource.Filter, PdfType.Integer, ErrCode.OutOfRange);

            //Unknown
            log.Add(ErrSource.Compiler, PdfType.Pattern, ErrCode.Unknown);
            log.Add(ErrSource.Cast, PdfType.Cmap, ErrCode.Unknown);

            //UnexpectedObject
            log.Add(ErrSource.Parser, PdfType.Item, ErrCode.UnexpectedObject);
            log.Add(ErrSource.Pages, PdfType.Item, ErrCode.UnexpectedObject);
            log.Add(ErrSource.Xref, PdfType.Item, ErrCode.UnexpectedObject);

            //IsCorrupt
            log.Add(ErrSource.General, PdfType.Trailer, ErrCode.IsCorrupt);
            log.Add(ErrSource.General, PdfType.XObject, ErrCode.IsCorrupt);
            log.Add(ErrSource.General, PdfType.Pages, ErrCode.IsCorrupt);
            log.Add(ErrSource.General, PdfType.Array, ErrCode.IsCorrupt);
            log.Add(ErrSource.General, PdfType.RealArray, ErrCode.IsCorrupt);
            log.Add(ErrSource.General, PdfType.Rectangle, ErrCode.IsCorrupt);

            //EOD
            log.Add(ErrSource.Compiler, PdfType.XObject, ErrCode.UnexpectedEOD);
            log.Add(ErrSource.ColorSpace, PdfType.ColorSpace, ErrCode.UnexpectedEOD);
            log.Add(ErrSource.Filter, PdfType.None, ErrCode.UnexpectedEOD);
            log.Add(ErrSource.Stream, PdfType.None, ErrCode.UnexpectedEOD);
            log.Add(ErrSource.XObject, PdfType.None, ErrCode.UnexpectedEOD);
            log.Add(ErrSource.XObject, PdfType.XObject, ErrCode.UnexpectedEOD);

            //Char
            Err(ErrSource.Lexer, ErrCode.UnexpectedChar, "#");
            Err(ErrSource.Lexer, ErrCode.UnexpectedChar, "a (Expected whitespace)");

            //Negative
            log.Add(ErrSource.Numeric, PdfType.Integer, ErrCode.UnexpectedNegative);

            //Corrupt token
            log.Add(ErrSource.Lexer, PdfType.Stream, ErrCode.CorruptToken);
            log.Add(ErrSource.ColorSpace, PdfType.Number, ErrCode.CorruptToken);
            log.Add(ErrSource.Filter, PdfType.Stream, ErrCode.CorruptToken);
            
            //EOF
            log.Add(ErrSource.General, PdfType.None, ErrCode.UnexpectedEOF);
            log.Add(ErrSource.Lexer, PdfType.String, ErrCode.UnexpectedEOF);
            log.Add(ErrSource.Lexer, PdfType.HexString, ErrCode.UnexpectedEOF);
            log.Add(ErrSource.Parser, PdfType.EOF, ErrCode.UnexpectedEOF);

            //WrongType
            log.Add(ErrSource.Compiler, PdfType.Name, ErrCode.WrongType);
            log.Add(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
            log.Add(ErrSource.ColorSpace, PdfType.DNCSAttrib, ErrCode.WrongType);
            log.Add(ErrSource.Filter, PdfType.Stream, ErrCode.WrongType);
            log.Add(ErrSource.Font, PdfType.Stream, ErrCode.WrongType);
            log.Add(ErrSource.Array, PdfType.Name, ErrCode.WrongType);
            log.Add(ErrSource.Array, PdfType.Pages, ErrCode.WrongType);

            //RealToInt
            log.Add(ErrSource.Numeric, PdfType.Integer, ErrCode.RealToInt);

            //Not object
            log.Add(ErrSource.Xref, PdfType.Integer, ErrCode.NotObject);

            Warn(WarnType.PackbitsNoEOD);

            foreach (var str in log.LogMessages)
                Console.WriteLine(str);
        }
#endif

        #endregion

        /// <summary>
        /// Starts logging on this thread. 
        /// </summary>
        public static void StartLogging()
        {
            lock (_logs)
            {
                var id = System.Threading.Thread.CurrentThread.ManagedThreadId;
                if (!_logs.ContainsKey(id))
                    _logs.Add(id, new LogImpl());
            }
        }

        internal static void Info(ErrSource source, LogEvent evt, string message)
        {
            GetLog().I(source, evt, message);
        }

        internal static void Info(ErrSource source, LogEvent evt)
        {
            GetLog().I(source, evt, null);
        }

        internal static void Warn(GS gfx_state, string cmd)
        {
            GetLog().W(gfx_state, cmd);
        }

        internal static void Warn(ErrSource source, WarnType warning)
        {
            GetLog().W(source, warning, null);
        }

        internal static void Warn(WarnType warning)
        {
            GetLog().W(warning, null);
        }

        internal static void Err(ErrSource source, ErrCode code)
        {
            GetLog().E(source, code, null);
        }

        internal static void Err(ErrSource source, ErrCode code, string msg)
        {
            GetLog().E(source, code, msg);
        }

        internal abstract void Add(ErrSource source, PdfType type, ErrCode code);
        internal abstract void E(ErrSource source, ErrCode code, string msg);
        internal abstract void W(GS gfx_state, string cmd);
        internal abstract void W(WarnType warning, string cmd);
        internal abstract void W(ErrSource source, WarnType warning, string cmd);
        internal abstract void I(ErrSource source, LogEvent evt, string message);
        internal abstract void IgnoredCMD(string cmd);
        public abstract IEnumerable<string> LogMessages { get; }
        public abstract string LastMessage { get; }

        public static Log GetLog()
        {
            Log l;
            lock (_logs)
            {
                _logs.TryGetValue(System.Threading.Thread.CurrentThread.ManagedThreadId, out l);
            }
            if (l == null)
                return _adapter;
            return l;
        }

        /// <summary>
        /// Does nothing
        /// </summary>
        private sealed class LogAdapter : Log
        {
            internal override void Add(ErrSource source, PdfType type, ErrCode code) { }
            internal override void W(GS gfx_state, string cmd) { }
            internal override void W(WarnType warning, string cmd) { }
            internal override void W(ErrSource source, WarnType warning, string cmd) { }
            internal override void IgnoredCMD(string cmd) { }
            internal override void I(ErrSource source, LogEvent evt, string message) { }
            internal override void E(ErrSource source, ErrCode code, string msg) { }
            public override IEnumerable<string> LogMessages
            {
                get { foreach( var str in new string[0]) yield return str; }
            }
            public override string LastMessage { get { return null; } }
        }

        /// <summary>
        /// A bit of a mess curently.
        /// </summary>
        private sealed class LogImpl : Log
        {
            /// <summary>
            /// Start time for when the log was created
            /// </summary>
            private DateTime _start_time = DateTime.Now;

            public override IEnumerable<string> LogMessages
            {
                get 
                { 
                    foreach (var evt in _events)
                    {
                        if ((evt.Type != EventType.Error || evt.Error.Type == PdfType.None) && evt.Type != EventType.Warning)
                            yield return string.Format(evt.Caption, RetriveString(evt.MSG));
                        else yield return evt.Caption;
                    }
                }
            }
            public override string LastMessage 
            { 
                get 
                {
                    if (_events.Count > 0)
                    {
                        var evt = _events[_events.Count - 1];
                        if ((evt.Type != EventType.Error || evt.Error.Type == PdfType.None) && evt.Type != EventType.Warning)
                            return string.Format(evt.Caption, RetriveString(evt.MSG));
                        else return evt.Caption;
                    }
                    return null; 
                } 
            }

            /// <summary>
            /// A list over error messages, in the order they were recived
            /// </summary>
            private List<OneEvent> _events = new List<OneEvent>(64);

            internal List<string> _strings = new List<string>(64);

            internal override void Add(ErrSource source, PdfType type, ErrCode code)
            {
                _events.Add(OneEvent.MakeError(new Error(source, type, code)));
            }

            /// <summary>
            /// Commands that are ignored.
            /// </summary>
            internal override void IgnoredCMD(string cmd)
            {
                _events.Add(OneEvent.MakeWarn(Warning.Ignore(IgnoredType.Command), StringToInt(cmd)));
            }

            /// <summary>
            /// For warning about state issues in the command stream
            /// </summary>
            /// <param name="gfx_state">State the compiler was in</param>
            /// <param name="cmd">Command that triggered the warning</param>
            internal override void W(GS gfx_state, string cmd)
            {
                _events.Add(OneEvent.MakeWarn(Warning.CState(gfx_state), StringToInt(cmd)));
            }

            internal override void W(WarnType warning, string cmd) 
            {
                _events.Add(OneEvent.MakeWarn(Warning.Create(warning), StringToInt(cmd)));
            }

            internal override void W(ErrSource source, WarnType warning, string cmd) 
            {
                _events.Add(OneEvent.MakeWarn(Warning.Create(source, warning), StringToInt(cmd)));
            }

            internal override void I(ErrSource source, LogEvent evt, string message)
            { 
                _events.Add(OneEvent.MakeInfo(Information.Create(evt, source), StoreString(message)));
            }

            internal override void E(ErrSource source, ErrCode code, string msg) 
            {
                _events.Add(OneEvent.MakeError(new Error(source, PdfType.None, code), StoreString(msg)));
            }

            int StoreString(string msg)
            {
                if (msg == null || "" == msg)
                    return -1;
                var r = _strings.Count;
                _strings.Add(msg);
                return r;
            }
            string RetriveString(int msg)
            {
                if (msg == -1) return "";
                return _strings[msg];
            }

            static int StringToInt(string str)
            {
                if (str == null) return -1;
                int r = 0;
                if (str.Length > 0)
                    r = ((byte) str[0]) << 24;
                if (str.Length > 1)
                    r |= ((byte)str[1]) << 16;
                if (str.Length > 2)
                    r |= ((byte)str[2]) << 8;
                if (str.Length > 3)
                    r |= ((byte)str[2]);
                return r;
            }
            static string IntToString(int i)
            {
                var sb = new StringBuilder(4);
                if (i > 0)
                {
                    sb.Append((char)(i >> 24));
                    i &= 0x00FFFFFF;

                    if (i > 0)
                    {
                        sb.Append((char)(i >> 16));
                        i &= 0x0000FFFF;

                        if (i > 0)
                        {
                            sb.Append((char)(i >> 8));
                            i &= 0x000000FF;

                            if (i > 0)
                                sb.Append((char)i);
                        }
                    }
                }
                return sb.ToString();
            }

            /// <summary>
            /// Records an event.
            /// </summary>
            [DebuggerDisplay("{Caption}")]
            [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 8)]
            struct OneEvent
            {
                [FieldOffset(0)]
                public EventType Type;

                [FieldOffset(0)]
                public Error Error;

                [FieldOffset(0)]
                public Warning Warning;

                [FieldOffset(0)]
                public Information Information;

                [FieldOffset(4)]
                public int MSG;

                public string Caption
                {
                    get
                    {
                        var caption = ResourceManager.GetString(Key + "C");
                        if (caption == null) return Key + "C";
                        if (Type == EventType.Error)
                        {
                            if (Error.Type != PdfType.None)
                                return string.Format(caption, Error.Type.ToString());
                        }
                        else if (Type == EventType.Warning)
                        {
                            if (Warning.Type == WarnType.Ignored)
                                return string.Format(caption, IntToString(MSG));
                            if (Warning.Type == WarnType.Command)
                                return string.Format(caption, IntToString(MSG), Warning.GfxState.ToString().ToLower());
                        }
                        return caption;
                    }
                }

                public string Body
                {
                    get
                    {
                        var body = ResourceManager.GetString(Key + "B");
                        return body;
                    }
                }

                public string Key
                {
                    get 
                    {
                        if (Type == EventType.Error)
                            return Error.Key;
                        if (Type == EventType.Warning)
                            return Warning.Key;
                        if (Type == EventType.Info)
                            return Information.Key;
                        return "";
                    }
                }

                public static OneEvent MakeError(Error err, int msg)
                {
                    var oe = new OneEvent();
                    oe.Error = err;
                    oe.MSG = msg;
                    oe.Type = EventType.Error;
                    return oe;
                }

                public static OneEvent MakeError(Error err)
                {
                    return MakeError(err, -1);
                }

                public static OneEvent MakeWarn(Warning wr, int msg)
                {
                    var oe = new OneEvent();
                    oe.Warning = wr;
                    oe.Type = EventType.Warning;
                    oe.MSG = msg;
                    return oe;
                }

                public static OneEvent MakeInfo(Information inf, int msg)
                {
                    var oe = new OneEvent();
                    oe.Information = inf;
                    oe.Type = EventType.Info;
                    oe.MSG = msg;
                    return oe;
                }
            }

            enum EventType : byte
            {
                /// <summary>
                /// Information
                /// </summary>
                Info,

                /// <summary>
                /// A correctable problem or possible issue
                /// </summary>
                Warning,

                /// <summary>
                /// An uncorrectable problem
                /// </summary>
                Error,

                /// <summary>
                /// Major error
                /// </summary>
                Fail
            }

            enum IgnoredType : byte
            {
                Command
            }

            [DebuggerDisplay("{Source} - {Type}")]
            [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
            struct Information
            {
                /// <summary>
                /// Type of information
                /// </summary>
                [FieldOffset(2)]
                public LogEvent Type;

                /// <summary>
                /// Source of the error message
                /// </summary>
                [FieldOffset(1)]
                public ErrSource Source;

                public string Key
                {
                    get { return string.Format("I{0:X4}", ((int)Source << 8) | ((int)Type)); }
                }

                public static Information Create(LogEvent t, ErrSource s)
                {
                    var i = new Information();
                    i.Type = t;
                    i.Source = s;
                    return i;
                }
            }

            [DebuggerDisplay("{Type} - {Ingored}")]
            [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
            struct Warning
            {
                /// <summary>
                /// Error code
                /// </summary>
                [FieldOffset(1)]
                public WarnType Type;

                [FieldOffset(2)]
                public IgnoredType Ingored;

                [FieldOffset(2)]
                public ErrSource Source;

                [FieldOffset(2)]
                public GS GfxState;

                public string Key
                {
                    get { return string.Format("W{0:X4}", ((int)Type << 8) | ((int)Ingored)); } 
                }

                public static Warning Create(WarnType t)
                {
                    var w = new Warning();
                    w.Type = t;
                    return w;
                }

                public static Warning Create(ErrSource source, WarnType t)
                {
                    var w = new Warning();
                    w.Type = t;
                    w.Source = source;
                    return w;
                }

                public static Warning CState(GS state)
                {
                    var w = new Warning();
                    w.Type = WarnType.Command;
                    w.GfxState = state;
                    return w;
                }

                public static Warning Ignore(IgnoredType t)
                {
                    var w = new Warning();
                    w.Type = WarnType.Ignored;
                    w.Ingored = t;
                    return w;
                }
            }

            [DebuggerDisplay("{Source} - {Type} - {Code}")]
            [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
            struct Error
            {
                /// <summary>
                /// Source of the error message
                /// </summary>
                [FieldOffset(3)]
                public readonly ErrSource Source;

                /// <summary>
                /// What type of object does this error concern
                /// </summary>
                [FieldOffset(2)]
                public readonly PdfType Type;

                /// <summary>
                /// Error code
                /// </summary>
                [FieldOffset(1)]
                public readonly ErrCode Code;

                /// <summary>
                /// The full value of the struct
                /// </summary>
                /// <remarks>Thinking about using this for sorting</remarks>
                public string Key 
                {
                    get { return string.Format("E{0:X4}", ((int)Source << 8) | ((int)Code)); } 
                }

                public Error(ErrSource source, PdfType type, ErrCode code)
                { Type = type; Code = code; Source = source; }
            }
        }
    }

    public enum LogEvent : byte
    {
        Open,
        Encrypted
    }

    public enum WarnType : byte
    {
        /// <summary>
        /// This is a warning concerning a draw command
        /// </summary>
        Command,

        /// <summary>
        /// Something was ignored
        /// </summary>
        Ignored,

        /// <summary>
        /// Packbits missing eod
        /// </summary>
        PackbitsNoEOD,

        /// <summary>
        /// There was a lone Cariage Return (should be \r\n, not just \r)
        /// </summary>
        SolitaryCR
    }
}
