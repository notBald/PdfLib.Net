using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
// See: http://www.codeproject.com/KB/recipes/LZWCompression.aspx to impl. LZW
//      http://www.codeproject.com/KB/recipes/VB_LZW.aspx
namespace PdfLib.Pdf.Filter
{
    public sealed class FlateParams : PdfFilterParms
    {
        #region Variables and properties

        /// <summary>
        /// Code for which predicor to use. 1 means no predicor
        /// </summary>
        public Predictor Predictor { get { return (Predictor) _elems.GetUInt("Predictor", 1); } }

        /// <summary>
        /// The number of interleaved colour components per sample.
        /// </summary>
        /// <remarks>Version is 1.3 if over 4</remarks>
        [PdfVersion("1.3")]
        public int Colors { get { return _elems.GetUInt("Colors", 1); } }

        /// <summary>
        /// The number of bits used to represent each colour component in a sample.
        /// </summary>
        /// <remarks>Version is 1.5 if over 8</remarks>
        [PdfVersion("1.5")]
        public int BitsPerComponent { get { return _elems.GetUInt("BitsPerComponent", 8); } }

        /// <summary>
        /// The number of samples in each row.
        /// </summary>
        public int Columns { get { return _elems.GetUInt("Columns", 1); } }

        /// <summary>
        /// Whenever to change lzw codesize one bit early. Only relevant for LZW filters.
        /// </summary>
        public bool EarlyChange => _elems.GetInt("EarlyChange", 1) == 1;

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="predicor">Which predicor to use</param>
        /// <param name="n_samples">Number of components</param>
        /// <param name="bits_per_component">Bits per component</param>
        /// <param name="colums">Stride/Wdith</param>
        public FlateParams(Predictor predicor, int n_samples, int bits_per_component, int colums)
            : base(new TemporaryDictionary())
        {
            if (predicor > Pdf.Filter.Predictor.None)
            {
                _elems.SetInt("Predictor", (int)predicor);
                if (bits_per_component != 8)
                    _elems.SetInt("BitsPerComponent", bits_per_component);
                if (colums != 1)
                    _elems.SetInt("Columns", colums);
                if (n_samples != 1)
                    _elems.SetInt("Colors", n_samples);
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="early_change">Whenever to change lzw codesize one bit early. Only relevant for LZW filters.</param>
        public FlateParams(bool early_change)
            : base (new TemporaryDictionary())
        {
            if (!early_change)
            {
                _elems.SetInt("EarlyChange", 0);
            }
        }

        internal FlateParams(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new FlateParams(elems);
        }
    }

    public enum Predictor
    {
        None = 1,
        Tiff2,
        PNG_None = 10,
        PNG_Sub,
        PNG_Up,
        PNG_Avg,
        PNG_Paeth,
        PNG_Opt
    }
}
