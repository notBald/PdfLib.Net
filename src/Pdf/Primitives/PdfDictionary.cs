using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Read.Parser;

// using System.ComponentModel;
//[EditorBrowsable(EditorBrowsableState.Never)]

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Parent class for dictionary objects type objects.
    /// </summary>
    /// <remarks>
    /// The object hirarcy is:
    /// PdfDictionary
    /// |-SealedDictionary
    /// |-WritableDictionary
    ///
    /// "Elements" has become the base class for pretty much everything. It's possible to
    /// inherit from PdfDictionary, but you must then impl. IEnumRef and IRef as appropriate.
    /// </remarks>
    public abstract class PdfDictionary : PdfObject, ICRef, IEnumRef, IDictRepair, IEnumerable<KeyValuePair<string, PdfItem>>
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Dictionary
        /// </summary>
        internal sealed override PdfType Type { get { return PdfType.Dictionary; } }

        /// <summary>
        /// Dictionary
        /// </summary>
        protected readonly Catalog _catalog;

        /// <summary>
        /// Gets a res tracker, null if not avalible.
        /// </summary>
        internal abstract ResTracker Tracker { get; }

        /// <summary>
        /// If this dictionary is writable or not
        /// </summary>
        public abstract bool IsWritable { get; }

        #endregion

        #region Init

        /// <summary>
        /// Creates a empty catalog
        /// </summary>
        protected PdfDictionary() { _catalog = new Catalog(); }

        /// <summary>
        /// Creates a filled catalog
        /// </summary>
        /// <param name="cat">The catalog</param>
        protected PdfDictionary(Catalog cat) { _catalog = cat; }

        #endregion

        #region Abstract Set methods

        #region Direct values
        //These methods will always set direct values

        /// <summary>
        /// Sets a direct name in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key. Is technically a name too</param>
        /// <param name="name">A plain name (no slash)</param>
        /// <param name="version">Required version for this field</param>
        public abstract void SetName(string key, string name);

        public void SetNameEx(string key, string name)
        {
            if (name == null || name.Length == 0)
                throw new ArgumentNullException();
            SetName(key, name);
        }

        /// <summary>
        /// Sets the type key of the the dictionary
        /// </summary>
        /// <param name="name">A plain name (no slash)</param>
        public abstract void SetType(string name);

        /// <summary>
        /// Sets a direct integer in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        public abstract void SetInt(string key, int num);

        /// <summary>
        /// Sets a direct unsigned integer in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        public void SetUInt(string key, uint num) { SetInt(key, unchecked((int)num)); }

        /// <summary>
        /// Sets a direct unsigned integer in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        /// <param name="default_value">The default value</param>
        public void SetUInt(string key, uint num, uint default_value) 
        {
            if (num == default_value)
                Remove(key);
            else
                SetUInt(key, num);
        }

        /// <summary>
        /// Sets a direct real in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        public abstract void SetReal(string key, double num);

        /// <summary>
        /// Sets a direct real in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        public void SetReal(string key, double? num)
        {
            if (num.HasValue)
                SetReal(key, num.Value);
            else
                Remove(key);
        }

        /// <summary>
        /// Sets a direct real in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key.</param>
        /// <param name="name">A direct number</param>
        /// <param name="default_value">A default value. If the value equals the default, the entery will be removed</param>
        public void SetReal(string key, double num, double default_value)
        {
            if (num == default_value)
                Remove(key);
            else
                SetReal(key, num);
        }

        /// <summary>
        /// Set a nullable int. If the number is null, the
        /// key is removed.
        /// </summary>
        /// <remarks>
        /// Setting a dictionary key to null is the
        /// same as removing it entierly (7.3.9)
        /// </remarks>
        public void SetInt(string key, int? num)
        {
            if (num == null) Remove(key);
            SetInt(key, num.Value);
        }

        /// <summary>
        /// Sets a direct boolean
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="boolean">Value</param>
        public abstract void SetBool(string key, bool boolean);

        /// <summary>
        /// Sets a direct boolean
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="boolean">Value</param>
        /// <param name="def_value">Default value</param>
        public void SetBool(string key, bool boolean, bool def_value)
        {
            if (boolean == def_value)
                Remove("key");
            else
                SetBool(key, boolean);
        }

        #endregion

        /// <summary>
        /// Sets a key to an item
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="item">Item</param>
        //public abstract void SetDirectItem(string key, PdfItem item);

        /// <summary>
        /// Sets a key to an item
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="item">Item</param>
        //public abstract void SetItem(string key, PdfItem item);

        /// <summary>
        /// This function is only to be used to set items into a
        /// class that is being built up. I.e. if you're not calling
        /// this on a newly created PdfDictionary you're doing it 
        /// wrong.
        /// </summary>
        internal abstract void SetNewItem(string key, PdfItem item, bool reference);

        /// <summary>
        /// Helper function that makes it more clear what I'm doing
        /// </summary>
        /*public abstract void SetNewItem(string key, Elements elem);//*/

        /// <summary>
        /// Sets a key to an item
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="item">Item</param>
        //public abstract void SetIndirectItem(string key, PdfReference item);

        /// <summary>
        /// Sets a item as an indirect item. 
        /// </summary>
        /// <remarks>Not named SetIndirectItem to avoid disambiguation</remarks>
        //public abstract void SetItemIndirect(string key, PdfItem item);

        /// <summary>
        /// Sets an elements
        /// </summary>
        /// <param name="key">Key to set the element at</param>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If the item should be set as a reference</param>
        public abstract void SetItem(string key, PdfItem item, bool reference);

        /// <summary>
        /// Requires the item to be present.
        /// </summary>
        public void SetItemEx(string key, PdfItem item, bool reference)
        {
            if (item == null || item == PdfNull.Value) 
                throw new ArgumentNullException("Value can not be zero");
            SetItem(key, item, reference);
        }

        /// <summary>
        /// Sets a unicode encoded string
        /// </summary>
        public void SetUnicodeString(string key, string str)
        {
            SetItem(key, new PdfString(Read.Lexer.CreateASCIIorUTF(str), false, false), false);
        }

        /// <summary>
        /// Sets a unicode encoded string
        /// </summary>
        /// Todo: Test if unicode is needed.
        public void SetUnicodeStringEx(string key, string str)
        {
            if (str == null)
                throw new ArgumentNullException("Value can not be zero");
            SetItem(key, new PdfString(Read.Lexer.CreateASCIIorUTF(str), false, false), false);
        }

        public void SetASCIIString(string key, string value)
        {
            if (value == null)
                Remove(key);
            else
                SetItem(key, new PdfString(Read.Lexer.CreateASCIIString(value), false, false), false);
        }

        /// <summary>
        /// Removes a item from the dictionary
        /// </summary>
        public abstract void Remove(string key);

        #endregion

        #region Direct methods

        /// <summary>
        /// Sets an item straight into the catalog of the dictionary.
        /// This can potentially set the object into an inconsistent state
        /// </summary>
        /// <remarks>
        /// Intended for use when creating dictionaries in constructors.
        /// Will throw if the item exists.
        /// </remarks>
        internal virtual void DirectSet(string key, PdfItem item)
        {
            throw new PdfNotWritableException();
        }

        /// <summary>
        /// This function is for storing internal implementation
        /// relevant data in the dictionary. 
        /// </summary>
        /// <param name="key">Key to the internal data</param>
        /// <param name="data">Any data object</param>
        /// <remarks>Data saved with this method will not
        /// be saved to disk</remarks>
        internal void InternalSet(string key, PdfItem data)
        {
            //The trick is to prefix internal data with a character
            //that does not exist in PDF names.
            key = 'Ĭ' + key;
            lock (_catalog)
            {
                if (_catalog.ContainsKey(key))
                    _catalog[key] = data;
                else
                    _catalog.Add(key, data);
            }
        }

        /// <summary>
        /// Tests if a internal bool flag is set
        /// </summary>
        internal bool InternalGetBool(string key)
        {
            lock (_catalog)
            {
                return GetBool('Ĭ' + key, false);
            }
        }

        /// <summary>
        /// Sets an item straight into the catalog of the dictionary. Allows for
        /// modifying an otherwise non-writable dictionary.
        /// 
        /// This method is not related to the other In Internalget/set
        /// methods.
        /// </summary>
        internal void InternalReplace(string key, PdfItem item)
        {
            lock (_catalog)
            {
                _catalog[key] = item;
            }
        }

        #endregion

        #region Indexing/Get methods

        /// <summary>
        /// Returns a real number
        /// </summary>
        public double GetRealEx(string key) { return GetItemEx(key).GetReal(); }

        /// <summary>
        /// Returns a real number
        /// </summary>
        public double GetReal(string key, double default_value) 
        {
            var ret = this[key];
            if (ret == null) return default_value;
            return ret.GetReal(); 
        }

        /// <summary>
        /// Fetches a boolean key
        /// </summary>
        /// <param name="key">Name of key</param>
        /// <param name="default_value">Defualt value if key is not set</param>
        /// <returns>Boolean value</returns>
        public bool GetBool(string key, bool default_value)
        {
            var ret = this[key];
            if (ret == null) return default_value;
            return ((PdfBool)ret.Deref()).Value;
        }
        public bool? GetBool(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            return ((PdfBool)ret.Deref()).Value;
        }

        /// <summary>
        /// Gets a positive integer 
        /// </summary>
        public int GetUInt(string key)
        {
            var ret = this[key];
            if (ret == null) return 0;
            var num = ret.GetInteger();
            if (num < 0) 
                throw new PdfReadException(ErrSource.Numeric, PdfType.Integer, ErrCode.UnexpectedNegative);
            return num;
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
            var obj = ret.Deref();
            if (obj is PdfLong)
                return unchecked((int)uint.Parse(obj.ToString()));
            var num = obj.GetInteger();
            if (num < 0)
                throw new PdfReadException(ErrSource.Numeric, PdfType.Integer, ErrCode.UnexpectedNegative);
            return num;
        }

        /// <summary>
        /// Gets a positive integer object
        /// </summary>
        public PdfInt GetUIntObj(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            var obj = ret.Deref();
            if (obj is PdfLong)
                return new PdfInt(unchecked((int)uint.Parse(obj.ToString())));
            var num = (PdfInt) obj;
            if (num.Value < 0) 
                throw new PdfReadException(ErrSource.Numeric, PdfType.Integer, ErrCode.UnexpectedNegative);
            return num;
        }

        /// <summary>
        /// Gets a positive integer, throws if it does not exist
        /// </summary>
        public int GetUIntEx(string key)
        {
            var ret = this[key];
            if (ret == null) throw new PdfReadException(ErrSource.Dictionary, PdfType.Integer, ErrCode.Missing);
            var obj = ret.Deref();
            if (obj is PdfLong)
                return unchecked((int)uint.Parse(obj.ToString()));
            var num = obj.GetInteger();
            if (num < 0) throw new PdfReadException(ErrSource.Numeric, PdfType.Integer, ErrCode.UnexpectedNegative);
            return num;
        }

        /// <summary>
        /// Gets the bytes of a number
        /// </summary>
        public byte[] GetRawLongEx(string key)
        {
            var ret = this[key];
            if (ret == null) throw new PdfReadException(ErrSource.Dictionary, PdfType.Integer, ErrCode.Missing);
            var obj = ret.Deref();
            if (obj is PdfLong)
                return Read.Lexer.GetBytes(obj.ToString());
            return Read.Lexer.GetBytesLE(obj.GetInteger());
        }

        /// <summary>
        /// Gets a integer 
        /// </summary>
        /// <param name="key">Integer key</param>
        /// <param name="default_value">Value returned if key don't exist</param>
        /// <returns></returns>
        public int GetInt(string key, int default_value)
        {
            var ret = this[key];
            if (ret == null) return default_value;
            var num = ret.GetInteger();
            return num;
        }

        /// <summary>
        /// Gets a positive integer, throws if it does not exist
        /// </summary>
        public int GetIntEx(string key)
        {
            var ret = this[key];
            if (ret == null) throw new PdfReadException(ErrSource.Dictionary, PdfType.Integer, ErrCode.Missing);
            return ret.GetInteger();
        }

        /// <summary>
        /// Gets a integer object
        /// </summary>
        public PdfInt GetIntObj(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            var num = (PdfInt)((ret is PdfReference) ? ret.Deref() : ret);
            return num;
        }

        /// <summary>
        /// Gets a real or integer number object
        /// </summary>
        public double? GetNumber(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            ret = ret.Deref();
            if (ret is INumber) return ((INumber)ret).GetReal();
            if (ret == null) return null;
            throw new PdfReadException(ErrSource.Dictionary, PdfType.Number, ErrCode.UnexpectedToken);
        }

        public PdfString GetStringObj(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret.Type != PdfType.String)
                throw new PdfReadException(PdfType.String, ErrCode.UnexpectedToken);// SR.ExpectedName);
            return (PdfString)((ret is PdfReference) ? ret.Deref() : ret);
        }

        /// <summary>
        /// Gets a PDF string. Don't use for "name".
        /// </summary>
        public string GetString(string key)
        {
            var ret = GetStringObj(key);
            if (ret == null) return null;
            return ret.Value;
        }

        /// <summary>
        /// Gets a PDF date
        /// </summary>
        public PdfDate GetDate(string key)
        {
            return (PdfDate) GetPdfType(key, PdfType.Date);
        }

        /// <summary>
        /// For retriving text that can be either streams or strings.
        /// </summary>
        public string GetTextStreamOrString(string key)
        {
            var str = this[key];
            if (str == null) return null;
            if (str is PdfString)
                return ((PdfString)str).UnicodeString;
            if (str is PdfStream)
            {
                var strm = (PdfStream)str;
                return new PdfString(strm.DecodedStream, false, true).UnicodeString;
            }
            throw new PdfReadException(PdfType.String, ErrCode.UnexpectedToken);
        }

        /// <summary>
        /// Gets a PDF unicode string.
        /// </summary>
        public string GetUnicodeString(string key)
        {
            var ret = GetStringObj(key);
            if (ret == null) return null;
            return ret.UnicodeString;
        }

        /// <summary>
        /// Gets a PDF unicode string.
        /// </summary>
        public string GetUnicodeStringEx(string key)
        {
            var ret = GetStringObj(key);
            if (ret == null)
                throw new PdfReadException(PdfType.String, ErrCode.Missing);
            return ret.UnicodeString;
        }

        /// <summary>
        /// Gets a name string.
        /// 
        /// Throws if it does not exist
        /// </summary>
        public string GetStringEx(string key)
        {
            var ret = GetStringObj(key);
            if (ret == null)
                throw new PdfReadException(PdfType.String, ErrCode.Missing);
            return ret.Value;
        }

        /// <summary>
        /// Gets a name string.
        /// 
        /// Throws if it does not exist
        /// </summary>
        public PdfString GetStringObjEx(string key)
        {
            var ret = GetStringObj(key);
            if (ret == null)
                throw new PdfReadException(PdfType.String, ErrCode.UnexpectedToken);
            return ret;
        }

        /// <summary>
        /// Gets a PdfName object
        /// </summary>
        public PdfName GetNameObj(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret.Type != PdfType.Name)
                throw new PdfReadException(PdfType.Name, ErrCode.UnexpectedToken);// SR.ExpectedName);
            return (PdfName) ((ret is PdfReference) ? ret.Deref() : ret);
        }

        /// <summary>
        /// Gets a name string.
        /// </summary>
        public string GetName(string key)
        {
            var ret = GetNameObj(key);
            if (ret == null) return null;
            return ret.Value;
        }

        /// <summary>
        /// Gets a name string.
        /// </summary>
        public string GetName(string key, string default_value)
        {
            var ret = GetNameObj(key);
            if (ret == null) return default_value;
            return ret.Value;
        }

        /// <summary>
        /// Gets a name string.
        /// 
        /// Throws if it does not exist
        /// </summary>
        public string GetNameEx(string key)
        {
            var ret = GetNameObj(key);
            if (ret == null)
                throw new PdfReadException(PdfType.Name, ErrCode.UnexpectedToken);
            return ret.Value;
        }

        /// <summary>
        /// Gets an array from the catalog
        /// </summary>
        public PdfArray GetArray(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret is PdfReference) ret = ret.Deref();
            if (ret is PdfArray) return (PdfArray)ret;
            throw new PdfReadException(ErrSource.Dictionary, PdfType.Array, ErrCode.UnexpectedToken);
        }

        /// <summary>
        /// Gets a dictionary, throws if it does not exist
        /// </summary>
        public PdfArray GetArrayEx(string key)
        {
            var ret = GetArray(key);
            if (ret == null)
                throw new PdfReadException(ErrSource.Dictionary, PdfType.Array, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets a dictionary from the catalog
        /// </summary>
        public PdfDictionary GetDictionary(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret is PdfReference) ret = ret.Deref();
            if (ret is PdfDictionary)
                return (PdfDictionary)ret;
            if (ret is PdfStream)
                return ((PdfStream)ret).Elements;
            throw new PdfReadException(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.UnexpectedToken);
        }

        /// <summary>
        /// Gets a stream dictionary from the catalog
        /// </summary>
        /// <remarks>
        /// Plain streams do not need any "ToType" calls,
        /// they are already stream objects when comming
        /// out of the parser.
        /// </remarks>
        public IStream GetStream(string key)
        {
            var ret = this[key];
            if (ret == null) return null;
            if (ret is PdfReference) ret = ret.Deref();
            if (ret is IStream) return (IStream)ret;
            throw new PdfReadException(ErrSource.Dictionary, PdfType.Stream, ErrCode.UnexpectedToken);
        }

        /// <summary>
        /// Gets a dictionary, throws if it does not exist
        /// </summary>
        public PdfDictionary GetDictionaryEx(string key)
        {
            var ret = GetDictionary(key);
            if (ret == null)
                throw new PdfReadException(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.Missing);
            return ret;
        }

        /// <summary>
        /// Gets an item from the dictionary, throws if
        /// the item does not exists
        /// </summary>
        public PdfItem GetItemEx(string key)
        {
            lock (_catalog)
            {
                PdfItem ret;
                if (!_catalog.TryGetValue(key, out ret) || ret == PdfNull.Value)
                    throw new PdfReadException(ErrSource.Dictionary, PdfType.Item, ErrCode.Missing);// SR.MissingItem);
                return ret;
            }
        }

        /// <summary>
        /// Gets a PdfObject of the wanted type, if any.
        /// Will always deref the object it returns.
        /// </summary>
        /// <remarks>
        /// Used for changing dictionary objects into
        /// higher level objects.
        /// 
        /// This function strips reference objects from the
        /// dictionary. The ref object contains the object id, 
        /// so that will be lost by doing this, but one can get
        /// the id back by doing a reverse lookup based on the
        /// object reference. See PdfXRefTable.cs for details
        /// </remarks>
        internal PdfObject GetPdfType(string key, PdfType type)
        {
            //Locks the entire opperation to prevent object duplication. The alternate would be
            //to check if the catalog has been updated before inserting a new object.
            lock (_catalog)
            {
                //Fetches the object
                var ret = this[key];
                if (ret == null) return null;

                //Object is of the correct type, deref and return
                if (ret.Type == type) return (PdfObject)ret.Deref();

                //Object is of the wrong type. This also effectivly
                //derefs the object
                var ret_obj = ret.ToType(type);
                Debug.Assert(ret_obj != null, "Null must never be returned!");

                //Update the dictionary if it's not a reference.
                // (Writable references must never be removed as
                //  they are used when writing a dictionary)
                if (!(ret is PdfReference))
                    _catalog[key] = ret_obj;
            
                Debug.Assert(ret_obj.Type == type);
                return (PdfObject)ret_obj;
            }
        }

        /// <summary>
        /// A number of properties can return two types: 
        ///  A plain object, or an array/dictionary.
        ///  
        /// To hide this complexity PdfLib will return a container even
        /// if there's not one in the document.
        /// 
        /// This leads to a change in the "in memory" structure of the PDF
        /// document, so care must be taken when preforming these operations
        /// so that the document don't break.
        /// </summary>
        /// <param name="key">Keyfield</param>
        /// <param name="container">Type of container</param>
        /// <param name="org_container">Original container type. Note
        /// that it can not be the same as the child's type.</param>
        /// <param name="always">Whenever to return null or not</param>
        /// <param name="msg">Message used during casting</param>
        /// <param name="msg_item">Message item</param>
        /// <remarks>
        /// Will set the container as a direct object, unless its already
        /// indirect.
        /// </remarks>
        internal PdfObject GetPdfType(string key, 
                                      PdfType container, 
                                      PdfType org_container, 
                                      bool always,
                                      IntMsg msg,
                                      object msg_item)
        {
            //Locks the entire opperation to prevent object duplication. The alternate would be
            //to check if the catalog has been updated before inserting a new object.
            lock (_catalog)
            {
                //Checks if the object is in the correct form already.
                var org_data = this[key];
                if (org_data == null)
                {
                    if (!always) return null;
                    PdfObject ret;
                    if (org_container == PdfType.Dictionary)
                    {
                        ResTracker t = Tracker;
                        var dict = (!IsWritable) ? new SealedDictionary() :
                            (t == null) ? new TemporaryDictionary() :
                                 (PdfDictionary)new WritableDictionary(t);
                        ret = Parser.CreateContainer(container, dict, msg, msg_item);
                    }
                    else
                        ret = Parser.CreateContainer(container, null, msg, msg_item);
                    _catalog[key] = ret;
                    return ret;
                }
                var org_type = org_data.Type;
                if (org_type == container) return (PdfObject)org_data.Deref();

                //Checks if it's an array/dictionary and takes proper action.
                //(This will break if the child and container is of the same
                //original type.)
                if (org_type == org_container)
                    return GetPdfType(key, container, msg, msg_item);

                //Dictionary types will always throw at this point. Maybe make that explicit.
                var ret_obj = Parser.CreateContainer(container, org_data, msg, msg_item);
                Debug.Assert(ret_obj.Type == container);

                //Note, ret_obj will be set as a direct object over any existing reference.
                //      the existing reference has, after all, been moved into the container
                //      so refcount or internal state in that object must be kept the same.
                _catalog[key] = ret_obj;

                return ret_obj;
            }
        }

        /// <summary>
        /// Gets a PdfObject of the wanted type, if any.
        /// Will always deref the object it returns.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="type">Desired type</param>
        /// <param name="def_obj">Defualt object</param>
        /// <returns></returns>
        internal PdfObject GetPdfType(string key, PdfType type, PdfObject def_obj)
        {
            Debug.Assert(def_obj == null || def_obj.Type == type);
            var ret = GetPdfType(key, type);
            if (ret == null) return def_obj;
            return ret;
        }

        /// <summary>
        /// Gets a PdfObject cast into wanted type.
        /// </summary>
        /// <param name="key">Key of object</param>
        /// <param name="replace">Replaces sealed references</param>
        /// <param name="type">Desired type</param>
        /// <param name="msg">Message</param>
        /// <returns>Dereferenced object</returns>
        /// <remarks>With replace false, this method is harmless
        /// and could be made public</remarks>
        internal PdfObject GetPdfType(string key, PdfType type, IntMsg msg, object obj)
        {
            //Locks the entire opperation to prevent object duplication. The alternate would be
            //to check if the catalog has been updated before inserting a new object.
            lock (_catalog)
            {
                var ret = this[key];
                if (ret == null) return null;
                if (ret.Type == type) return (PdfObject)ret.Deref();
                var tret = ret.ToType(type, msg, obj); //<- Effectivly a "Deref"
                if (!(ret is PdfReference)) _catalog[key] = tret;
                return (PdfObject)tret;
            }
        }

        /// <summary>
        /// A number of properties can return two types: 
        ///  A plain object, or an array/dictionary.
        ///  
        /// This function handles those situations
        /// </summary>
        /// <remarks>
        /// Will set the container as a direct object, unless its already
        /// indirect.
        /// </remarks>
        internal PdfObject GetPdfTypeEx(string key,
                                      PdfType container,
                                      PdfType org_container,
                                      IntMsg msg,
                                      object msg_item)
        {
            //Locks the entire opperation to prevent object duplication. The alternate would be
            //to check if the catalog has been updated before inserting a new object.
            lock (_catalog)
            {
                //Checks if the object is in the correct form already.
                var org_data = this[key];
                if (org_data == null)
                    throw new PdfReadException(ErrSource.Dictionary, container, ErrCode.Missing);
                var org_type = org_data.Type;
                if (org_type == container) return (PdfObject)org_data.Deref();

                //Checks if it's an array/dictionary and takes proper action.
                //(This will break if the child and container is of the same
                //original type.)
                if (org_type == org_container)
                    return GetPdfType(key, container, msg, msg_item);

                //Dictionary types will always throw at this point. Maybe make that explicit.
                var ret_obj = Parser.CreateContainer(container, org_data, msg, msg_item);
                Debug.Assert(ret_obj.Type == container);

                //Note, ret_obj will be set as a direct object over any existing reference.
                //      the existing reference has, after all, been moved into the container
                //      so refcount or internal state in that object must be kept the same.
                _catalog[key] = ret_obj;

                return ret_obj;
            }
        }

        /// <summary>
        /// Gets a DictCatalog of the wanted type, throws
        /// if it does not exist
        /// </summary>
        internal PdfObject GetPdfTypeEx(string key, PdfType type)
        {
            var ret = GetPdfType(key, type);
            if (ret == null)
                throw new PdfReadException(ErrSource.Dictionary, type, ErrCode.Missing);
            return ret;
        }

        internal PdfObject GetPdfTypeEx(string key, PdfType type, IntMsg msg, object obj)
        
         {

            //Locks the entire opperation to prevent object duplication. The alternate would be
            //to check if the catalog has been updated before inserting a new object.
            lock (_catalog)
            {
                var ret = this[key];
                if (ret == null)
                    throw new PdfReadException(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.Missing);
                if (ret.Type == type) return (PdfObject)ret.Deref();
                var tret = ret.ToType(type, msg, obj); //<- Effectivly a "Deref"
                if (!(ret is PdfReference)) _catalog[key] = tret;
                return (PdfObject)tret;
            }
        }

        /// <summary>
        /// Number of items in the catalog
        /// </summary>
        public int Count { get { lock (_catalog) { return _catalog.Count; } } }

        /// <summary>
        /// If the catalog contain the key
        /// </summary>
        public bool Contains(string key) { lock (_catalog) { return _catalog.ContainsKey(key); } }

        /// <summary>
        /// For doing reverse lookups in the dictionary
        /// </summary>
        /// <remarks>
        /// This dictionary must be maintained by the caller.
        /// </remarks>
        public abstract Dictionary<PdfItem, string> GetReverseDict();

        /// <summary>
        /// Gets an item from the catalog
        /// </summary>
        /// <param name="key">Name of the item</param>
        /// <returns>The item or null of the item was not found</returns>
        /// <remarks>Will change PdfNull into null pointer</remarks>
        public PdfItem this[string key]
        {

            get 
            {
                lock (_catalog)
                {
                    PdfItem ret;
                    if (_catalog.TryGetValue(key, out ret) && ret != PdfNull.Value)
                        return ret;
                    return null;
                }
            }
        }

        /// <summary>
        /// Returns the raw object, including PdfNull objects, and
        /// throws if the key does not exist.
        /// </summary>
        public PdfItem GetItem(string key)
        {
            lock (_catalog)
            {
                return _catalog[key];
            }
        }

        /// <summary>
        /// For use for functions that can handle PdfNull objects
        /// </summary>
        /// <remarks>
        /// When a PdfNull object is return it means that it was
        /// set null in the pdf stream itself, while if a simple
        /// null is return then there's no key in the pdf stream
        /// </remarks>
        protected PdfItem itm(string key)
        {
            lock (_catalog)
            {
                PdfItem ret;
                if (_catalog.TryGetValue(key, out ret))
                    return ret;
                return null;
            }
        }

        /// <summary>
        /// Sets a series of bytes as a string
        /// </summary>
        public void SetByteString(string key, byte[] str)
        {
            SetItem(key, new PdfString(str), false);
        }

        #endregion

        #region Check methods

        /// <summary>
        /// For checking the type of subclasses
        /// </summary>
        internal void CheckType(string type)
        {
            var ret = GetNameObj("Type");
            if (ret != null && !ret.Value.Equals(type))
                throw new PdfCastException(ErrSource.General, PdfType.Dictionary, ErrCode.IsCorrupt);
        }

        /// <summary>
        /// For checking the type of subclasses
        /// </summary>
        internal bool IsType(string type)
        {
            var ret = GetNameObj("Type");
            return (ret != null && ret.Value.Equals(type));
        }

        /// <summary>
        /// For checking the type of subclasses.
        /// 
        /// Throws if the type doesn't exist
        /// </summary>
        internal void CheckTypeEx(string type)
        {
            var ret = GetNameObj("Type");
            if (ret == null || !ret.Value.Equals(type))
                throw new PdfCastException(ErrSource.General, PdfType.Dictionary, ErrCode.IsCorrupt);
        }

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <summary>
        /// Returns an enumerator that iterates through the catalog.
        /// </summary>
        /// <remarks>There may be "PdfNull" values</remarks>
        public IEnumerator<KeyValuePair<string, PdfItem>> GetEnumerator()
        {
            lock(_catalog)
            {
                foreach (var el in _catalog)
                    yield return el;
            }
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Compares two dictonaries for equivalence
        /// </summary>
        internal override bool Equivalent(object obj)
        {
            if (!ReferenceEquals(this, obj))
            {
                if (obj is PdfDictionary)
                {
                    lock (_catalog)
                    {
                        var cat = ((PdfDictionary)obj)._catalog;
                        foreach (var kp in _catalog)
                        {
                            var val = kp.Value.Deref();
                            if (val == null) return false; //<-- Null is not allowed
                            PdfItem val2;
                            if (!cat.TryGetValue(kp.Key, out val2) ||
                                !val.Equivalent(val2.Deref()))
                                return false;
                        }
                    }
                }
                else 
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A more thorugh search for version information
        /// </summary>
        internal override sealed PdfVersion GetPdfVersion(bool fetch, HashSet<object> set)
        {
            var var = base.PdfVersion;
            set.Add(this);

            //Checks the children. 
            foreach (var kp in _catalog)
            {
                if (set.Contains(kp.Value)) continue;
                var v = (fetch) ? kp.Value.GetPdfVersion(true, set) : kp.Value.PdfVersion;
                if (v > var) var = v;
            }

            return var;            
        }

        /// <summary>
        /// Writes itself to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.WriteDictionary(_catalog);
        }

        /// <summary>
        /// Creates a dict catalog based on this
        /// dictionary, and the given type
        /// </summary>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            if (type == Type) return this;
            return Parser.CreatePdfType(this, type, msg, obj);
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("<<");
            lock (_catalog)
            {
                foreach (KeyValuePair<string, PdfItem> kp in _catalog)
                {
                    sb.Append("{ /");
                    sb.Append(kp.Key);
                    sb.Append(" , ");
                    var str = kp.Value.ToString();
                    if (str.Length > 12)
                    {
                        str = str.Substring(0, 12);
                        sb.Append(str);
                        sb.Append('…');
                    }
                    else
                        sb.Append(str);
                    sb.Append(" }");
                }
            }
            sb.Append(">>");
            return sb.ToString();
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are children in this dictionary
        /// </summary>
        public bool HasChildren
        { get { lock(_catalog) return _catalog.Count != 0; } }

        /// <summary>
        /// Writable references
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return new DictRefEnum(_catalog); } }

        class DictRefEnum : IEnumRefEnumerable
        {
            IEnumRef _current;
            IEnumerator<KeyValuePair<string, PdfItem>> _items;
            readonly Catalog _cat;

            public DictRefEnum(/*IEnumerator<KeyValuePair<string, PdfItem>> items*/ Catalog cat) { _cat = cat; _items = cat.GetEnumerator(); }
            object IEnumerator.Current { get { return _current; } }
            public IEnumRef Current { get { return _current; } }
            public bool MoveNext()
            {
                while (_items.MoveNext())
                {
                    var cur = _items.Current.Value;
                    if (cur is IEnumRef)
                    {
                        _current = (IEnumRef)cur;
                        return true;
                    }
                }
                return false;
            }
            public void Reset() { /* _items.Reset() */ Dispose(); _items = _cat.GetEnumerator(); }
            public void Dispose() { _items.Dispose(); }
            IEnumerator IEnumerable.GetEnumerator() { return this; }
            IEnumerator<IEnumRef> IEnumerable<IEnumRef>.GetEnumerator()
            { return this; }
        }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return _catalog; }

        ICRef ICRef.MakeCopy(object data, ResTracker t) { return (ICRef) MakeCopy((Catalog) data, t); }

        protected abstract PdfDictionary MakeCopy(Catalog data, ResTracker tracker);

        //Keep this method in sync with BaseArray.LoadResources
        void ICRef.LoadResources(HashSet<object> check)
        {
            if (check.Contains(this))
                return;
            check.Add(this);

            Monitor.Enter(_catalog);
            bool has_lock = true;

            try
            {
                foreach (var kp in _catalog)
                {
                    var val = kp.Value;
                    if (val is PdfReference)
                        val = ((PdfReference)val).Deref();

                    if (val is ICRef icref)
                    {
                        if (icref.Follow)
                        {
                            Monitor.Exit(_catalog);
                            has_lock = false;

                            icref.LoadResources(check);

                            Monitor.Enter(_catalog);
                            has_lock = true;
                        }
                    }
                }
            } finally { if (has_lock) Monitor.Exit(_catalog); }
        }

        bool ICRef.Follow { get { return true; } }

        #endregion

        #region Cloning

        /// <summary>
        /// Convinience method for adopting a item before adding it to the dictionary.
        /// </summary>
        /// <param name="obj">Object to adopt</param>
        /// <returns>The same object or a clone of the object</returns>
        internal PdfObject Adopt(IKRef obj)
        {
            var tracker = Tracker;
            if (!IsWritable)
                throw new PdfNotWritableException();
            if (tracker == null && obj.IsDummy)
            {
                //If this is a dummy tracker we try to avoid adoption.
                //Basic rule, if a dummy object tries to adopt a dummy
                //object, we leave well enough alone, even if that dummy
                //object contains resources from other documents.
                return (PdfObject)obj;
            }

            if (!obj.Adopt(tracker))
                return (PdfObject)tracker.MakeCopy(obj, true);
            return (PdfObject)obj;
        }

        /// <summary>
        /// Creates a sealed clone of this dictionary
        /// </summary>
        /// <returns>Note, may contain writable resources</returns>
        internal SealedDictionary SealedClone()
        {
            lock ( _catalog)
                return new SealedDictionary(_catalog.Clone());
        }

        /// <summary>
        /// Creates a temp clone of this dictionary
        /// </summary>
        /// <returns>Note, may contain writable resources</returns>
        internal TemporaryDictionary TempClone()
        {
            lock (_catalog)
                return new TemporaryDictionary(_catalog.Clone());
        }


        #endregion

        #region IRepair

        void IDictRepair.RepairSet(string key, PdfItem val)
        {
            if (IsWritable)
                SetItem(key, val, false);
            else
                _catalog[key] = val;
        }

        void IDictRepair.RepairRemove(string key)
        {
            if (IsWritable)
                Remove(key);
            else
                _catalog.Remove(key);
        }

        #endregion

        #region Notification

        /// <summary>
        /// Reigsters an object for notification
        /// </summary>
        /// <param name="elm">Element to register</param>
        internal virtual void Notify(INotify elm, bool register)
        {
            //Only relevant for writable dictionaries. 
        }

        #endregion
    }

    /// <summary>
    /// Object that describes how the catalog is built up
    /// </summary>
    /// <remarks>Saves me from making changes several
    /// places if this needs to be altered.
    /// 
    /// Note that ConcurrentDictionary.GetEnumerator().Reset() is not supported
    /// </remarks>
    public sealed class Catalog : ConcurrentDictionary<string, PdfItem>, ICloneable
    {
        public Catalog() { }
        internal Catalog(IDictionary<string, PdfItem> cat) 
            : base(cat)
        { }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Catalog(Catalog cat)
        {
            foreach (var kp in cat)
                //Add(kp.Key, (kp.Value is PdfObject) ? kp.Value.Clone() : kp.Value);
                TryAdd(kp.Key, kp.Value);
        }

        public Catalog(int capacity) : base(1, capacity) { }

        object ICloneable.Clone() { return new Catalog(this); }

        //todo: Memberwise clone does not work for some reason. Seems it clones more than I'd expect, or less. 
        //      Investigate when time allows.
        // public Catalog Clone() { return (Catalog)MemberwiseClone(); }
        //^The "TestStreamSave()" triggers the "already owns this object" exception
        public Catalog Clone() { return new Catalog(this); }

        /// <summary>
        /// Implemets a function similar to Dictionary.Add
        /// </summary>
        /// <param name="key"></param>
        /// <param name="item"></param>
        public void Add(string key, PdfItem item)
        {
            if (!TryAdd(key, item))
                throw new ArgumentException("Key already exists in the dictionary", key);
        }

        public void Remove(string key)
        {
            TryRemove(key, out _);
        }
    }

    /// <summary>
    /// With thought towards future multithreading support, any manipulations of
    /// the dictionary's internal datastructure must be funneled through this
    /// interface, so that there's an opertunity to lock or whatever.
    /// 
    /// Neither should these functions pollute the normal namespace. This
    /// interface effectivly hides them. 
    /// </summary>
    internal interface IDictRepair
    {
        void RepairSet(string key, PdfItem val);
        void RepairRemove(string key);
    }
}
