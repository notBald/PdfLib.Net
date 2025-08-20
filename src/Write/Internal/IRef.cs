using System.Collections.Generic;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Objects with this interface can register in the restracker for
    /// a save notification.
    /// </summary>
    public interface INotify
    {
        void Notify(NotifyMSG msg);
    }

    public enum NotifyMSG
    {
        PrepareForSave,
        SaveComplete
    }

    /// <summary>
    /// This interface is for mutable objects. All such objects
    /// must implement this interface. This includes objects that
    /// maintains some sort of cached data, or anything that is
    /// document spesific and cause trouble when the object is
    /// moved to another document.
    /// 
    /// immutable objects can make do with IEnumRef and ICref (if they
    /// contain references, and can forgo all that if not).
    /// </summary>
    /// <remarks>
    /// Full name is "Interface Kontains Ref" as it was originaly just
    /// an empty interface to slowly combine ICRef and IRef.. but it's
    /// purpose have change a bit since then. 
    /// 
    /// Instead this interface is now used to get rid of the dependency
    /// on "Elements". 
    /// 
    /// IOW, Perhaps rename this to IMutable?
    /// </remarks>
    internal interface IKRef : ICRef, IRef
    {
        /// <summary>
        /// The save mode of this item
        /// </summary>
        SM DefSaveMode { get; }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IsDummy { get; }

        /// <summary>
        /// Adopt this object.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adopt</param>
        /// <returns>Will return false if the adoption failed.</returns>
        /// <remarks>
        /// In retrospect this is a bit silly.
        /// 
        /// Instead make a tracker.Adobt(IKRef) function. Inside there
        /// call a IKRef.GetTempContainer function that returns null
        /// if there's no such container. Then that function below 
        /// carrying state information can be dropped.
        /// </remarks>
        bool Adopt(ResTracker tracker);

        /// <summary>
        /// To prevent circular references. Only for use by the tracker itself.
        /// </summary>
        /// <param name="tracker">The tracker</param>
        /// <param name="state">State information related to the adoption</param>
        /// <returns>true if the adoption was a success</returns>
        /// <remarks>
        /// Note that while adoption may succeed for this item, it may have failed
        /// for one of it's children. Not that it really matters as said item will 
        /// be silently copied instead.
        /// </remarks>
        bool Adopt(ResTracker tracker, object state);

        /// <summary>
        /// Checks if a tracker owns an object
        /// </summary>
        /// <remarks>
        /// This is used by the tracker to recognize its own objects. You
        /// can call "Adopt" instead but in some cases I prefere to have
        /// an exception thrown as the object is expected to be owned by
        /// the tracker.
        /// 
        /// Hmm. But why didn't I put this method on the tracker itself? 
        /// Oh yes.
        ///  1. I don't want to expose the internal tracker, less I fall
        ///     to the temptation to (ab)use it.
        ///  2. Tracker owned objects need not have references, so using
        ///     Tracker.Owns(...) is not an alternative.
        /// </remarks>
        bool IsOwner(ResTracker tracker);
    }

    /// <summary>
    /// Long name: Interface Contains References
    /// </summary>
    /// <remarks>
    /// Any object that can contain references must implement this interface.
    /// It is used when changing document ownership of an object.
    /// 
    /// This interface sorts of makes IEnumRef redundant, as that interface also
    /// gives access to the children, but in that case only the references.
    /// However this inteface can not be implemented by references, while IEnumRef
    /// can.
    /// </remarks>
    internal interface ICRef : IEnumRef
    {
        /// <summary>
        /// Return data as an item array, or a Catalog
        /// </summary>
        /// <remarks>Unlike IENumRef this returns all children</remarks>
        object GetChildren();

        /// <summary>
        /// Implementors must make a copy of itself, using the
        /// supplied data.
        /// </summary>
        ICRef MakeCopy(object data, ResTracker tracker);

        /// <summary>
        /// Implementors must load all external data into local memory
        /// </summary>
        /// <param name="check">
        /// To avoid circular references. Implementors
        /// must put a recognizable key into the hashset,
        /// and return if it's there.
        /// </param>
        void LoadResources(HashSet<object> check);

        /// <summary>
        /// LoadResources should not be called on classes where this
        /// is set false, unless you know that you want that object
        /// to load its resources for certain.
        /// </summary>
        /// <remarks>Naturally, for dictionaries that has yet to
        /// be promoted to a higher level object, this will always
        /// return true. Fortunatly the objects we want to avoid
        /// are always loaded into memory unless explicitly purged
        /// so this works out.
        /// </remarks>
        bool Follow { get; }
    }

    /// <summary>
    /// For objects that can be compared with themselves
    /// (Interface Equals reference)
    /// </summary>
    [System.Obsolete("use PdfItem.Equivalent( )")]
    internal interface IERef : IRef
    {
        Equivalence IsLike(IERef obj);
    }

    /// <summary>
    /// A couple of my base classes implements ICRef, while at the
    /// same time I don't want at least one of the subclasses to be
    /// copied. (PdfPages)
    /// 
    /// Not entierly sure what's the best way to solve the problem.
    /// My current quickfix is this glue interface. Only objects
    /// explicitly implementing this interface will be copied.
    /// 
    /// Other solutions
    ///  - A don't copy method on the ICRef interface
    ///  - Returning null on the GetChildren method
    ///  - Supplying a list of "no follow" references
    ///  - Setting a flag on the reference
    ///  - Manipulating the objects containing "don't copy" references before the copy
    ///  - Create "non copy" base objects that can be inherited from instead
    ///  
    ///  - Current solution, except that when a non-copy ICRef is encountered,
    ///    don't not create a TempReference, then prune away "lost" keys after 
    ///    the algo has run (I want to avoid removing/adding items while doing 
    ///    a copy, as it can cause things to go out of synch.)
    /// </summary>
    /// <remarks>
    /// This isn't a good solution. Non copied data have to be pruned away,
    /// and don't use the normal method calls for this as that will mess
    /// up the refcounting. 
    /// 
    /// Also, this approach makes it possible to make PdfFiles PDfLib will be
    /// unable to save by simply having a ref to the pages object somewhere,
    /// also, this works on the assumption that the pages object is parsed
    /// when opening a pdf file. This is not nesesarily the case, though is
    /// handeled through the "TempHack" by doing an abrubt delete.
    /// </remarks>
    //internal interface CopyObj : ICRef { } Useless

    /// <summary>
    /// Objects can be passed around from page to page, and from document
    /// to document.
    /// 
    /// To make this painless(less painfull) most "higer" objects implements 
    /// the IRef interface. This interface allows the object to carry along
    /// a reference, which is then used to determine who owns the object,
    /// and how it should be added to a dictionary/array, without bothering
    /// the user with these details.
    /// 
    /// Note that all objects that implements IRef also implements ICRef.
    /// I've concidered combining the interfaces, but WritableObjStream
    /// prevents this.
    /// </summary>
    internal interface IRef //: ICRef <-- Pretend this is the case
    {
        /// <summary>
        /// If this object has a reference. 
        /// </summary>
        /// <remarks>
        /// I imagine that this method will be public.
        /// 
        /// Note that this property does not tell if this object
        /// is direct or indirect. I.e. not having a reference
        /// does not mean it's direct, nor does having a reference
        /// mean it's indirect.
        /// </remarks>
        bool HasReference { get; }

        /// <summary>
        /// How this object should be saved unless some other
        /// condition overrides this
        /// </summary>
        /// <remarks>
        /// Originally this was intended to truly be just a default
        /// but has morphed into something more important. There
        /// are a varity of limitations to how PDF objects are to
        /// be saved, so PdfLib will very much respect what is
        /// set here.
        /// 
        /// Unless one specify auto.
        /// 
        /// If an object must be indirect, you can also often
        /// pick "compressed". Only stream objects and some
        /// stuff in the trailer can't be compressed.
        /// </remarks>
        //SM DefSaveMode { get; }

        /// <summary>
        /// The writable reference for this object
        /// </summary>
        /// <remarks>This method should not be public</remarks>
        WritableReference Reference { get; set; }
    }

    //Default impel of IRef
    /*
        #region IRef
     
        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference { get { return ((IRef)this).Reference != null; } }

        /// <summary>
        /// This object can be adopted
        /// </summary>
        bool IRef.IsOrphan { get { return false; } set { } }
     
        /// <summary>
        /// This object's reference.
        /// </summary>
        WritableReference IRef.Reference { get; set; }

        #endregion 
     */
}
