using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compile;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    public abstract class PdfPattern : Elements, IERef
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.Pattern; } }

        public xMatrix Matrix
        {
            set
            {
                if (value == null || value.IsIdentity)
                    _elems.Remove("Matrix");
                else
                    _elems.SetItem("Matrix", value.ToArray(), false);
            }
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Matrix", PdfType.RealArray);
                if (ar == null) return xMatrix.Identity;
                return new xMatrix(ar);
            }
        }

        #endregion

        #region Init

        protected PdfPattern(PdfDictionary dict)
            : base(dict) { _elems.CheckType("Pattern"); }

        #endregion

        #region IERef

        Equivalence IERef.IsLike(IERef obj)
        {
            if (obj.GetType() == GetType())
            {
                var pat = (PdfPattern)obj;
                if (Matrix.Equals(pat.Matrix))
                    return (Equivalence)IsLike(pat);
            }
            return Equivalence.Different;
        }

        protected abstract int IsLike(PdfPattern obj);

        #endregion
    }

    /// <summary>
    /// A dictionary over patterns
    /// </summary>
    public sealed class PatternElms : TypeDict<PdfPattern>
    {
        //Need to do reverse lookups for "render.DrawPage" to
        //function in a sensible manner
        Dictionary<PdfItem, string> _reverse;

        internal override PdfType Type { get { return PdfType.PatternElms; } }

        internal PatternElms(PdfDictionary dict)
            : base(dict, PdfType.Pattern, null) { }
        
        /// <summary>
        /// Adds a pattern resource
        /// </summary>
        /// <param name="key">Name of the resource</param>
        /// <param name="pat">Pattern to add</param>
        protected override void SetT(string key, PdfPattern pat)
        {
            Add(key, pat);
        }

        // <summary>
        // Adds a pattern resource
        // </summary>
        // <param name="name">Name of the resource</param>
        // <param name="pat">Pattern to add</param>
        private void Add(string name, PdfPattern pat)
        {
            _elems.SetItem(name, pat, true);

            if (_reverse == null) 
                _reverse = _elems.GetReverseDict();
            else
                _reverse.Add(pat, name);
        }


        /// <summary>
        /// Adds a pattern and generates a name for that pattern
        /// </summary>
        /// <param name="pat">Pattern to add</param>
        /// <returns>The name to use for the pattern</returns>
        internal string Add(PdfPattern pat)
        {
            //We need to find the name of this pattern. For that we
            //mantain a reverse lookup dictionary
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            PdfItem key = (pat.HasReference) ? ((IRef)pat).Reference : (PdfItem)pat;

            string id;
            if (!_reverse.TryGetValue(key, out id))
            {
                if (ReferenceEquals(key, pat) || !_reverse.TryGetValue(pat, out id))
                {
                    //Creates a new name for the pattern
                    id = GetNewName();

                    //Adds this pattern to the elements
                    _elems.SetItem(id, pat, true);

                    //Updates the reverse lookup dictionary
                    _reverse.Add(pat, id);
                }
            }

            return id;
        }

        /// <summary>
        /// Function for creating a new tiling pattern
        /// </summary>
        /// <param name="BBox">Bounding box</param>
        /// <param name="matrix">Transform</param>
        /// <param name="XStep">Horizontal tile size</param>
        /// <param name="YStep">Vertical tile size</param>
        /// <param name="pt">Whenever this pattern is colored or not</param>
        /// <param name="tl">Rendering hint</param>
        /// <param name="pat">The new tile pattern is placed here</param>
        /// <returns>The name of the pattern</returns>
        internal string CreatePattern(PdfRectangle BBox, xMatrix matrix, 
            double XStep, double YStep, PdfPaintType pt, PdfTilingType tl,
            out PdfTilingPattern pat)
        {
            if (!IsWritable)
                throw new PdfNotWritableException(); 
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            pat = PdfTilingPattern.CreateWritable(_elems.Tracker, BBox, matrix, XStep, YStep, pt, tl);
            var name = GetNewName();
            _elems.SetNewItem(name, pat, true);
            _reverse.Add(pat, name);
            return name;
        }

        string GetNewName()
        {
            int c = _reverse.Count;
            var sb = new StringBuilder(4);
            do
            {
                sb.Length = 0;
                sb.Append("p");
                sb.Append(++c);
            } while (_elems.Contains(sb.ToString()));
            return sb.ToString();
        }

        protected override void DictChanged()
        {
            _reverse = null;
        }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfPattern> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new PatternElms(elems);
        }
    }
}
