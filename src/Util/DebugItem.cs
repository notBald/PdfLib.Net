using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Util
{
    /*Test notes for ResTracker "MakeCopy"
            //Situation
            // Array[0] -> Ref -> Direct object -> ref_itm
            // Array[1] -> Direct object -> ref_itm
            //The Ar[1] direct object will be copied from the
            //org. direct object, and the "ref_itm" will be in the
            //ref_dict.
     * */

    /// <summary>
    /// Only used for testing ResTracker.MakeCopy
    /// </summary>
    public class DebugItem : PdfObject, IKRef, IEnumRef
    {
        #region Variables and properties

        public PdfItem[] ARRAY = new PdfItem[7];

        public ResTracker Tracker = new ResTracker();

        internal WritableDictionary Dictonary;

        public PdfDictionary DICTIONARY { get { return Dictonary; } }

        internal override PdfType Type
        {
            get { return PdfType.Array; }
        }

        public PdfItem Item { get { return Dictonary.GetItem("Item"); } }

        #endregion

        #region Init

        public DebugItem()
        {
            //Gets around a debug.assert
            Tracker.Doc = new Write.WritableDocument();

            Dictonary = new WritableDictionary(Tracker);

            for (int c = 0; c < ARRAY.Length; c++)
                ARRAY[c] = PdfNull.Value;
        }

        public DebugItem(PdfItem[] items)
        { ARRAY = items; }

        public DebugItem(PdfItem[] items, ResTracker t)
        { ARRAY = items; Tracker = t; }

        #endregion

        #region Debug trobule

        public void CreateRefRect()
        {
            var items = new PdfItem[4];
            items[0] = new PdfInt(5);
            items[1] = Tracker.CreateWRef(new PdfInt(5));
            items[2] = new PdfInt(5);
            items[3] = new PdfInt(5);
            ARRAY[1] = new PdfRectangle(items);
        }

        public void SlipInto(PdfObject obj)
        {
            if (obj is ICRef)
            {
                var icref = (ICRef)obj;
                var contents = icref.GetChildren();
                if (contents is Catalog)
                    ((Catalog)contents).Add("DEBUG", this);
                else if (contents is PdfItem[])
                {
                    var ia = (PdfItem[])contents;
                    if (ia.Length > 0)
                        ia[0] = this;
                }
            }
        }

        public void CreateTrouble()
        {
            ARRAY[0] = new PdfInt(15);
            ARRAY[1] = PdfNull.Value;
            ARRAY[2] = new TempReference(this);
            ARRAY[3] = new TempReference(ARRAY[0]);
            ARRAY[4] = new PdfName("A name");
            ARRAY[5] = ARRAY[2];
            ARRAY[6] = new TempReference(new SealedArray());

            //Even more trouble.
            var cat1 = new Catalog();
            var cat2 = new Catalog();
            var cat3 = new Catalog();
            var dict1 = new SealedDictionary(cat1);
            var dict2 = new SealedDictionary(cat2);
            var dict3 = new SealedDictionary(cat3);
            cat1.Add("ToCat2", dict2);
            cat2.Add("ToCat3", new TempReference(dict3));
            cat3.Add("Back", new TempReference(this));
            ARRAY[0] = dict1;//*/

            //to test fast copy. (Assumes "Even more trouble")
            cat2.Remove("ToCat3");
            cat3.Remove("Back");
            cat1.Add("ToCat3a", dict3);
            cat1.Add("ToCat3b", new SealedDictionary());
            cat1.Add("ToCat3c", dict3);
            ARRAY[1] = dict1;
            ARRAY[0] = new TempReference(PdfNull.Value);//PdfNull.Value;
            ARRAY[2] = PdfNull.Value;
            ARRAY[3] = PdfNull.Value;
            ARRAY[6] = PdfNull.Value;//*/

            //More trouble
            /*var cat = new Catalog();
            var dict = new SealedDictionary(cat);
            cat.Add("Home", this);
            ARRAY[1] = new TempReference(dict);
            cat.Add("Self", new TempReference(dict));//*/

            //Invalid trouble
            //items1->items2->items3->items4->items2
            //Current algo will allow this to be coped, but it should throw
            //as files saved with a structure like this will be endlessly large
            /*var items1 = new PdfItem[1]; var a1 = new PdfArray(items1);
            var items2 = new PdfItem[1]; var a2 = new PdfArray(items2);
            var items3 = new PdfItem[1]; var a3 = new PdfArray(items3);
            var items4 = new PdfItem[1]; var a4 = new PdfArray(items4);
            items1[0] = a2;
            items2[0] = a3;
            items3[0] = a4;
            items4[0] = a2;
            ARRAY[0] = new TempReference(a1);*/
        }

        #endregion

        #region IKRef

        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        bool IKRef.IsDummy { get { return Tracker.Doc == null; } }
        public bool IsOwner(ResTracker tracker) { return tracker == Tracker; }
        public bool Adopt(ResTracker tracker) { return IsOwner(tracker); }
        public bool Adopt(ResTracker tracker, object state) { return IsOwner(tracker); }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return ARRAY; }

        ICRef ICRef.MakeCopy(object data, ResTracker t) { return new DebugItem((PdfItem[])data, t); }

        void ICRef.LoadResources(HashSet<object> check)
        {

        }

        bool ICRef.Follow { get { return false; } }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are children in this dictionary
        /// </summary>
        public bool HasChildren
        { get { return ARRAY.Length != 0; } }

        /// <summary>
        /// Writable references
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return new IEnumRefEnImpl(ARRAY.GetEnumerator()); } }

        #endregion

        #region IRef

        public void RemoveReference() { ((IRef)this).Reference = null; }

        // <summary>
        // Auto lets PdfLib figure out how to save
        // </summary>
        internal override SM DefSaveMode { get { return SM.Auto; } }

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference { get { return ((IRef)this).Reference != null; } }

        /// <summary>
        /// This object's reference.
        /// </summary>
        WritableReference IRef.Reference { get; set; }

        #endregion 

        /// <summary>
        /// Will throw if refcount is zero
        /// </summary>
        public void CheckDictRefCount()
        {
            CheckRefCount((IEnumRef)this.Dictonary);
        }

        private void CheckRefCount(IEnumRef ier)
        {
            foreach (var iemn in ier.RefEnumerable)
            {
                if (iemn is PdfReference)
                {
                    var wr = (WritableReference) iemn;
                    if (wr.RefCount == 0)
                        throw new Exception("Refcount fail");
                    if (wr.Deref() is IEnumRef)
                        CheckRefCount((IEnumRef)wr.Deref());
                }
                else
                    CheckRefCount(iemn);
            }
        }

        public PdfArray MakeArray()
        {
            CreateRefRect();
            return new SealedArray(ARRAY);
        }

        public object TestCopy() 
        {
            var ret = Tracker.MakeCopy(this, true);
            var ret2 = new ResTracker(new Write.WritableDocument()).MakeCopy((ICRef) ret, true);
            return ret2; 
        }

        public static PdfLib.Pdf.Font.PdfFont GetFont(PdfLib.Compose.cFont cfont)
        {
            cfont.GetGlyphMetrix('A');
            return cfont.MakeWrapper();
        }

        internal override void Write(PdfWriter write)
        {
            Console.WriteLine("Saving the debug item to disk");
            write.WriteArray(ARRAY);
        }
    }
}
