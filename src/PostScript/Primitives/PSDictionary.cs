using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using System.Collections.Generic;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// A PostScript Dictionary
    /// </summary>
    /// <remarks>
    /// Not using PdfDictionary as it does resource tracking. Res tracking
    /// is not needed for PostScript. PSDict also exposes more implemetation
    /// details, such as the "Catalog" being public, for better or for worse.
    /// </remarks>
    public class PSDictionary : PSObject
    {
        #region Variables and properties

        /// <summary>
        /// Contains all dictionary enteries
        /// </summary>
        public /*readonly*/ Dictionary<string, PSItem> Catalog;

        /// <summary>
        /// Gets an item from the catalog
        /// </summary>
        /// <param name="key">Name of the item</param>
        /// <returns>The item or null of the item was not found</returns>
        /// <remarks>Will change PSNull into null pointer</remarks>
        public PSItem this[string key]
        {

            get
            {
                PSItem ret;
                if (Catalog.TryGetValue(key, out ret) && ret != PSNull.Value)
                    return ret;
                return null;
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// This constructor is used for creating higher level dictionaries.
        /// </summary>
        internal PSDictionary(PSDictionary dict) { Catalog = dict.Catalog; Access = dict.Access; Executable = dict.Executable; }

        public PSDictionary(Dictionary<string, PSItem> cat) { Catalog = cat; }
        public PSDictionary(int capacity) { Catalog = new Dictionary<string, PSItem>(capacity); }
        public PSDictionary() { Catalog = new Dictionary<string, PSItem>(); }

        #endregion

        #region Methods

        /// <summary>
        /// Adds a value to the dictionary, for use for code that inserts
        /// value from PostScript. Existing values are overwritten
        /// </summary>
        public void Add(PSName key, PSItem val)
        {
            if (Access != PSAccess.Unlimited)
                throw new PSParseException(PSType.None, ErrCode.Illegal);

            Catalog[key.Value] = val;
        }

        /// <summary>
        /// Gets value from the dictionary, for use for code that fetches into
        /// PostScript
        /// </summary>
        public PSItem Get(string key)
        {
            if (Access > PSAccess.ReadOnly)
                throw new PSParseException(PSType.None, ErrCode.Illegal);
            return Catalog[key];
        }

        public override PSItem ShallowClone() { return MakeShallowCopy(); }

        public PSDictionary MakeShallowCopy()
        {
            var cat = new Dictionary<string, PSItem>(Catalog.Count);
            foreach (var val in Catalog)
                cat.Add(val.Key, val.Value);
            var dict = new PSDictionary(cat);
            dict.Executable = Executable;
            return dict;
        }

        internal override void Restore(PSItem obj)
        {
            var d = (PSDictionary)obj;
            Access = d.Access;
            Catalog = d.Catalog;
        }

        #endregion

        #region Helper methods

        public PSArray GetArray(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (!(ret is PSArray)) throw new PSCastException();
            return (PSArray)ret;
        }

        public PSArray GetArrayEx(string key)
        {
            var ret = GetArray(key);
            if (ret == null)
                throw new PSReadException(ErrSource.Dictionary, PSType.None, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets a name
        /// </summary>
        public string GetName(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (!(ret is PSName)) throw new PSCastException();
            return ((PSName)ret).Value;
        }

        public string GetNameEx(string key)
        {
            var ret = GetName(key);
            if (ret == null)
                throw new PSReadException(ErrSource.Dictionary, PSType.Name, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets a string
        /// </summary>
        public string GetStr(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (!(ret is PSString)) throw new PSCastException();
            return ((PSString)ret).GetString();
        }

        public string GetStrEx(string key)
        {
            var ret = GetStr(key);
            if (ret == null)
                throw new PSReadException(ErrSource.Dictionary, PSType.String, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets a number
        /// </summary>
        /// <param name="key">The number's key</param>
        public double? GetNumber(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret is PSInt) return ((PSInt)ret).Value;
            if (ret is PSReal) return ((PSReal)ret).Value;

            throw new PSReadException(ErrSource.Numeric, PSType.Number, ErrCode.WrongType);
        }

        /// <summary>
        /// Gets a positive integer 
        /// </summary>
        /// <param name="key">Integer key</param>
        /// <param name="default_value">Value returned if key don't exist</param>
        public int GetUInt(string key, int default_value)
        {
            var ret = this[key];
            if (ret == null) return default_value;
            if (!(ret is PSInt)) throw new PSCastException();
            var num = ((PSInt) ret).Value;
            if (num < 0)
                throw new PSReadException(ErrSource.Numeric, PSType.Integer, ErrCode.UnexpectedNegative);
            return num;
        }

        /// <summary>
        /// Gets a positive integer 
        /// </summary>
        /// <param name="key">Integer key</param>
        public int GetUIntEx(string key)
        {
            var ret = this[key];
            if (ret == null) throw new PSReadException(ErrSource.Dictionary, PSType.Integer, ErrCode.Missing);
            if (!(ret is PSInt)) throw new PSCastException();
            var num = ((PSInt)ret).Value;
            if (num < 0)
                throw new PSReadException(ErrSource.Numeric, PSType.Integer, ErrCode.UnexpectedNegative);
            return num;
        }

        /// <summary>
        /// Gets a PSObject of the wanted type, if any.
        /// </summary>
        /// <remarks>
        /// Used for changing dictionary objects into
        /// higher level objects.
        /// </remarks>
        /// <typeparam name="T">Target type</typeparam>
        /// <typeparam name="O">Original object</typeparam>
        internal T GetPSObj<T, O>(string key, PSCreate<T, O> create)
            where T:PSObject
            where O:PSObject
        {
            //Fetches the object
            var ret = this[key];
            if (ret == null) return null;

            //Object is of the correct type
            if (ret is T) return (T)ret;

            //Object is of the wrong type.
            if (!(ret is O)) throw new PSCastException();
            var ret_obj = create((O)ret);

            //UpdateS the dictionary
            Catalog[key] = ret_obj;

            return ret_obj;
        }

        /// <summary>
        /// Gets a object of the wanted type, throws
        /// if it does not exist
        /// </summary>
        internal T GetPSObjEx<T, O>(string key, PSCreate<T, O> create)
            where T : PSObject
            where O : PSObject
        {
            var ret = GetPSObj<T, O>(key, create);
            if (ret == null)
                throw new PSReadException(ErrSource.Dictionary, PSType.None, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets a PSDictionary decendant object
        /// </summary>
        /// <typeparam name="T">Target type</typeparam>
        internal T GetPSDict<T>(string key, PSCreate<T, PSDictionary> create)
            where T : PSDictionary
        {
            //Fetches the object
            var ret = this[key];
            if (ret == null) return null;

            //Object is of the correct type
            if (ret is T) return (T)ret;

            //Object is of the wrong type.
            if (!(ret is PSDictionary)) throw new PSCastException();
            var ret_obj = create((PSDictionary)ret);

            //UpdateS the dictionary
            Catalog[key] = ret_obj;

            return ret_obj;
        }

        /// <summary>
        /// Gets a dictionary of the wanted type, throws
        /// if it does not exist
        /// </summary>
        internal T GetPSDictEx<T>(string key, PSCreate<T, PSDictionary> create)
            where T : PSDictionary
        {
            var ret = GetPSDict<T>(key, create);
            if (ret == null)
                throw new PSReadException(ErrSource.Dictionary, PSType.None, ErrCode.Missing);
            return ret;
        }

        #endregion
    }

    /// <summary>
    /// Delegate that knows how to create objects from other objects
    /// </summary>
    public delegate T PSCreate<T, O>(O obj);
}
