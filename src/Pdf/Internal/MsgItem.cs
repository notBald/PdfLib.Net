using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A Message Item class, for sending messages along with the item.
    /// </summary>
    /// <remarks>
    /// Making it a PdfItem for convenience. This class is never part
    /// of the Pdf Document, just used for message passing.
    /// </remarks>
    /*public sealed class MsgItem : PdfItem
    {
        /// <summary>
        /// The item sent along with the message. Can be null.
        /// </summary>
        public readonly object Item;

        /// <summary>
        /// The message
        /// </summary>
        public readonly Msg Msg;

        /// <summary>
        /// This is not a pdf type
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.None; }
        }

        public MsgItem(Msg msg, object item)
        {
            Item = item; Msg = msg;
        }

        #region Overrides

        public sealed override int GetInteger() { return ((PdfItem) Item).GetInteger(); }
        public sealed override double GetReal() { return ((PdfItem)Item).GetReal(); }
        public sealed override string GetString() { return ((PdfItem)Item).GetString(); }
        public sealed override PdfItem Deref() { return (PdfItem) Item; }
        internal override PdfObject ToType(PdfType type, PdfItem msg)
        { return ((PdfItem)Item).ToType(type, msg); }
        internal override void Write(Write.Internal.PdfWriter write)
        { throw new NotSupportedException(); }


        #endregion
    }*/

    /// <summary>
    /// Internal messages
    /// </summary>
    public enum IntMsg
    {
        NoMessage,
        /// <summary>
        /// Used for type array and type dict
        /// </summary>
        Message,
        Thumbnail,
        ResTracker,
        Special,
        PdfColorSpaceElms,

        //Not really used
        Filter,
        Resources,

        //Tell objects not to change while saving
        DoNotChange,

        //Used by PdfOutlines
        RootOutline,

        //Used by object streams
        Owner,

        /// <summary>
        /// Assume an xObject to be a form
        /// </summary>
        AssumeForm,

        /// <summary>
        /// Assume an xObject to be an image
        /// </summary>
        AssumeImage
    }
}
