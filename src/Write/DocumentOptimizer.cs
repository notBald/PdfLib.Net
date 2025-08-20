using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;
using PdfLib.Compile;

namespace PdfLib.Write
{
    /// <summary>
    /// Utility class for optimizing resource sharing usage 
    /// </summary>
    public class DocumentOptimizer
    {
        #region Variables and properties

        private readonly WritableDocument _doc;
        private readonly ResTracker _tracker;
        private readonly Dictionary<string, object> _cache = new Dictionary<string,object>(7);
        private readonly List<KeyValuePair<string, object>> _update = new List<KeyValuePair<string, object>>(10);

        #endregion

        #region Init

        /// <summary>
        /// Constructs a DocumentOptimizer
        /// </summary>
        /// <param name="doc">Document one wish to optimize</param>
        public DocumentOptimizer(WritableDocument doc)
        {
            _doc = doc;
            _tracker = _doc.Catalog.Pages.Elements.Tracker;
        }

        #endregion

        private List<CacheItem<T>> GetCache<T>(string key)
        {
            object val;
            if (!_cache.TryGetValue(key, out val))
            {
                val = new List<CacheItem<T>>(20);
                _cache[key] = val;
            }
            return (List<CacheItem<T>>)val; 
        }

        public void OptimizePage(PdfPage page)
        {
            if (!((IKRef)page).IsOwner(_tracker))
                throw new PdfNotSupportedException("Page must be owned by the document");
            OptimizePageImpl(page);
        }

        private void OptimizePageImpl(PdfPage page)
        {
            OptimizeResources(page.Resources, new HashSet<PdfResources>());
        }

        private void OptimizeResources(PdfResources res, HashSet<PdfResources> optz_resources)
        {
            //To prevent any circular resource references (PdfPage->res1->XObject->res1)
            optz_resources.Add(res);

            //Fix: Font comparison
            OptimizeResouce(res.XObject, GetCache<PdfXObject>("XObject"), optz_resources);
            OptimizeResouce<PdfPattern>(res.Pattern, GetCache<PdfPattern>("Pattern"));
            OptimizeResouce(res.ColorSpace, GetCache<IColorSpace>("ColorSpace"));
            //Todo: ExtGState always sets as direct
            OptimizeResouce(res.ExtGState, GetCache<PdfGState>("ExtGState"));
            OptimizeResouce(res.Font, GetCache<PdfFont>("Font"));
        }

        private void Update<T>(TypeDict<T> res)
            where T:PdfObject
        {
            if (_update.Count > 0)
            {
                foreach (var kp in _update)
                {
                    var cache_item = (CacheItem<T>)kp.Value;
                    res[kp.Key] = cache_item.Value;
                    if (cache_item.Container != null)
                    {
                        ((TypeDict<T>)cache_item.Container)[cache_item.Key] = cache_item.Value;
                        cache_item.Container = null;
                    }
                }
                _update.Clear();
            }
        }

        private void OptimizeResouce(XObjectElms res, List<CacheItem<PdfXObject>> cache, HashSet<PdfResources> optz_resources)
        {
            try
            {
                var cs = GetCache<IColorSpace>("ColorSpace");

                foreach (var kp in res)
                {
                    var val = kp.Value;

                    for (int c = cache.Count - 1; c >= 0; c--)
                    {
                        var cache_item = cache[c];
                        var obj = cache_item.Value;
                        //if (obj.HasReference)
                        {
                            if (obj.Equivalent(val))
                            {
                                _update.Add(new KeyValuePair<string, object>(kp.Key, cache_item));
                                continue;
                            }
                        }
                    }

                    if (val is PdfForm)
                    {
                        var form = ((PdfForm)val).Resources;
                        if (!optz_resources.Contains(form))
                            OptimizeResources(form, optz_resources);
                    }

                    // Direct objects can be made indirect if it's used more than once.
                    // but if it's already indirect, then nothing needs doing.
                    if (val.HasReference)
                        cache.Add(new CacheItem<PdfXObject> { Value = val });
                    else
                        cache.Add(new CacheItem<PdfXObject> { Key = kp.Key, Value = val, Container = res });
                }

                Update<PdfXObject>(res);
            }
            catch (Exception) { /* Do nothing, simply can't optimize this document. */ _update.Clear(); }
        }

        private void OptimizeResouce(FontElms res, List<CacheItem<PdfFont>> cache)
        {
            try
            {
                //var cs = GetCache<IColorSpace>("ColorSpace");

                foreach (var kp in res)
                {
                    var val = kp.Value;

                    for (int c = cache.Count - 1; c >= 0; c--)
                    {
                        var cache_item = cache[c];
                        var obj = cache_item.Value;
                        //if (obj.HasReference)
                        {
                            if (obj.Equivalent(val))
                            {
                                _update.Add(new KeyValuePair<string, object>(kp.Key, cache_item));
                                continue;
                            }
                        }
                    }

                    // Direct objects can be made indirect if it's used more than once.
                    // but if it's already indirect, then nothing needs doing.
                    if (val.HasReference)
                        cache.Add(new CacheItem<PdfFont> { Value = val });
                    else
                        cache.Add(new CacheItem<PdfFont> { Key = kp.Key, Value = val, Container = res });
                }

                Update<PdfFont>(res);
            }
            catch (Exception) { /* Do nothing, simply can't optimize this document. */ _update.Clear(); }
        }

        private void OptimizeResouce(GStateElms res, List<CacheItem<PdfGState>> cache)
        {
            try
            {
                //var cs = GetCache<IColorSpace>("ColorSpace");

                foreach (var kp in res)
                {
                    var val = kp.Value;

                    for (int c = cache.Count - 1; c >= 0; c--)
                    {
                        var cache_item = cache[c];
                        var obj = cache_item.Value;
                        //if (obj.HasReference)
                        {
                            if (obj.Equivalent(val))
                            {
                                _update.Add(new KeyValuePair<string, object>(kp.Key, cache_item));
                                continue;
                            }
                        }
                    }

                    // Direct objects can be made indirect if it's used more than once.
                    // but if it's already indirect, then nothing needs doing.
                    if (val.HasReference)
                        cache.Add(new CacheItem<PdfGState> { Value = val });
                    else
                        cache.Add(new CacheItem<PdfGState> { Key = kp.Key, Value = val, Container = res });
                }

                Update<PdfGState>(res);
            }
            catch (Exception) { /* Do nothing, simply can't optimize this document. */ _update.Clear(); }
        }

        private void OptimizeResouce<T>(TypeDict<T> res, List<CacheItem<T>> cache)
            where T:PdfObject, IERef
        {
            try
            {
                foreach (var kp in res)
                {
                    var val = (T)kp.Value;

                    for (int c = cache.Count - 1; c >= 0; c--)
                    {
                        var cache_item = cache[c];
                        var obj = cache_item.Value;
                        //if (obj.HasReference)
                        {
                            if (ReferenceEquals(obj, val) || obj.IsLike(val) == Equivalence.Identical)
                            {
                                _update.Add(new KeyValuePair<string, object>(kp.Key, cache_item));
                                continue;
                            }
                        }
                    }

                    // Direct objects can be made indirect if it's used more than once.
                    // but if it's already indirect, then nothing needs doing.
                    if (val.HasReference)
                        cache.Add(new CacheItem<T> { Value = val });
                    else
                        cache.Add(new CacheItem<T> { Key=kp.Key, Value = val, Container = res });
                }

                Update<T>(res);
            }
            catch (Exception) { /* Do nothing, simply can't optimize this document. */ _update.Clear(); }
        }

        private void OptimizeResouce(PdfColorSpaceElms res, List<CacheItem<IColorSpace>> cache)
        {
            try
            {
                foreach (var kp in res)
                {
                    var val = kp.Value;

                    for (int c = cache.Count - 1; c >= 0; c--)
                    {
                        var cache_item = cache[c];
                        var obj = cache_item.Value;
                        if (obj is IRef && ((IRef)obj).HasReference)
                        {
                            if (obj.Equals(val))
                            {
                                _update.Add(new KeyValuePair<string, object>(kp.Key, cache_item));
                                continue;
                            }
                        }
                    }

                    // Direct objects can be made indirect if it's used more than once.
                    // but if it's already indirect, then nothing needs doing.
                    if (!(val is IRef) || ((IRef) val).HasReference)
                        cache.Add(new CacheItem<IColorSpace> { Value = val, Key = kp.Key });
                    else
                        cache.Add(new CacheItem<IColorSpace> { Key = kp.Key, Value = val, Container = res });
                }
            }
            catch (Exception) { /* Do nothing, simply can't optimize this document. */ }

            if (_update.Count > 0)
            {
                foreach (var kp in _update)
                {
                    var cache_item = (CacheItem<IColorSpace>)kp.Value;
                    res[kp.Key] = cache_item.Value;
                    if (cache_item.Container != null)
                        ((PdfColorSpaceElms)cache_item.Container)[cache_item.Key] = cache_item.Value;
                }
                _update.Clear();
            }
        }

        /// <summary>
        /// Optimizes the whole document
        /// </summary>
        public void Optimize()
        {
            //This function, as a side effect, loads objects into
            //their full form (so instead of dictionary and array, you get, say, colorspace and xobject),
            //but I don't think thise side effect is made use of, so this call can be dropped.
            _doc.Catalog.GetPdfVersion(true);

            foreach (var page in _doc)
                OptimizePageImpl(page);
        }

        /// <summary>
        /// Holds an item in the cache
        /// </summary>
        private class CacheItem<T>
        {
            /// <summary>
            /// Original key
            /// </summary>
            public string Key { get; set; }

            /// <summary>
            /// Cached object
            /// </summary>
            public T Value { get; set; }

            /// <summary>
            /// Original container
            /// </summary>
            public PdfObject Container { get; set; }
        }
    }
}
