using System.Text;
using System.Globalization;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compile.Analyze
{
    public abstract class AnalyzeCMD
    {
        public object[] Parameters;

        public bool IsValid { get { return Parameters != null; } }

        /// <summary>
        /// Text representation of the command
        /// </summary>
        public abstract string CMD { get; }

        internal abstract CMDType Type { get; }

        /// <summary>
        /// Rendering state, before this command was executed
        /// </summary>
        public PdfState? State;

        protected AnalyzeCMD()
        {

        }

        protected AnalyzeCMD(object[] parameters)
        {
            Parameters = parameters;
        }

        public override string ToString()
        {
            if (Parameters != null)
                return CMD + " - Invalid";
            return CMD;
        }

        /// <summary>
        /// The type of command.
        /// </summary>
        /// <remarks>
        /// See page 111 in the specs for how the specs defines the types. Sufficient to say
        /// those aren't followed here.
        /// 
        /// Currently only used by "CompiledPage.cs" to present statistics on command use. 
        /// </remarks>
        internal enum CMDType
        {
            /// <summary>
            /// This command manipulates the general or
            /// special state, including fonts and
            /// path building
            /// </summary>
            State,

            /// <summary>
            /// This command sets a color or a pattern
            /// </summary>
            Texture,

            /// <summary>
            /// This command draws an image
            /// </summary>
            Image,

            /// <summary>
            /// This command draws a form or an inline image
            /// </summary>
            Form,

            /// <summary>
            /// This command draws text
            /// </summary>
            Text,

            /// <summary>
            /// This command draws a path or set a clip
            /// </summary>
            Path,

            /// <summary>
            /// A commands that help mark up the contents
            /// </summary>
            Markup,

            /// <summary>
            /// Used for the BDC command. Note, CompiledPage.cs assumes this.
            /// </summary>
            Special
        }
    }

    #region Special graphics state

    /// <summary>
    /// Saves the state
    /// </summary>
    public class q_CMD : AnalyzeCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        public override string CMD { get { return "q"; } }
        public q_CMD(object[] parameters) : base(parameters) { }
        public q_CMD() { }
    }

    /// <summary>
    /// Restores the state
    /// </summary>
    public class Q_CMD : AnalyzeCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        public override string CMD { get { return "Q"; } }
        public Q_CMD(object[] parameters) : base(parameters) { }
        public Q_CMD() { }
    }

    public class cm_CMD : AnalyzeCMD
    {
        public xMatrix Matrix;
        public override string CMD { get { return "cm"; } }
        internal override CMDType Type { get { return CMDType.State; } }
        public cm_CMD(xMatrix m)
        { Matrix = m; }
        public override string ToString()
        {
            return Matrix.ToString() + " cm" + ((IsValid) ? "" : " - Invalid");
        }

    }

    #endregion

    public sealed class UnknownCMD : AnalyzeCMD
    {
        readonly string _cmd;

        internal override AnalyzeCMD.CMDType Type
        {
            get { throw new System.NotImplementedException(); }
        }

        public override string CMD { get { return _cmd; } }

        public UnknownCMD(string cmd, object[] parameters)
            : base(parameters)
        { _cmd = cmd; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (Parameters.Length > 0)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", Parameters[Parameters.Length - 1]);
                for (int c = Parameters.Length - 2; c >= 0; c--)
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0}", Parameters[c]);
                sb.Append(' ');
            }
            sb.Append(_cmd);
            sb.Append(" - Unknown");
            return sb.ToString();
        }
    }
}
