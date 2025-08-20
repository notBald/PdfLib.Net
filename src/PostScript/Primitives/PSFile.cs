using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a file
    /// </summary>
    /// <remarks>
    /// For now this is just used to implement the eexec command, there's
    /// no real support for files.
    /// </remarks>
    public sealed class PSFile : PSObject
    {
        PSLexer _file;

        public bool DecryptFile
        {
            get { return _file.DecryptStream; }
            set { _file.DecryptStream = value; }
        }

        public string DebugString { get { return _file.ReadDebugData(); } }

        public PSFile(PSLexer file) { _file = file; }

        /// <summary>
        /// Reads a single byte from the file
        /// </summary>
        public int ReadByte() { return _file.ReadByte(); }

        /// <summary>
        /// Closes the file
        /// </summary>
        public void Close()
        {
            _file.Close();
        }

        public override PSItem ShallowClone()
        {
            //Todo: Save the position and Executable/Access attributes
            return new PSFile(_file);
        }

        internal override void Restore(PSItem obj)
        {
            throw new NotImplementedException();
        }
    }
}
