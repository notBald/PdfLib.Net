#define OpenJpeg
//#define LibJPEGv9
#define USE_PIXEL_COVERTER
using System;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Filter;
using PdfLib.Util;
using PdfLib.Img;
using PdfLib.Img.Internal;
using PdfLib.Img.Png;
#if OpenJpeg
using OpenJpeg;
using OpenJpeg.Internal;
using OpenJpeg.Util;
using System.Windows.Media.Animation;
using System.Windows.Controls;
#else
using OpenJpeg2;
using OpenJpeg2.Internal;
using OpenJpeg2.Util;
#endif
#if LibJPEGv9
using BitMiracle.LibJpeg.Classic;
#else
using LibJpeg.Classic;
#endif

namespace PdfLib.Pdf
{
    public sealed class PdfImage : PdfXObject
    {
#region Variables and properties

        /// <summary>
        /// How this image is encoded
        /// </summary>
        /// <remarks>Only looks at the last filter.</remarks>
        public ImageFormat Format
        {
            get
            {
                var fa = _stream.Filter;
                if (fa == null) return ImageFormat.RAW;
                var filter = fa[fa.Length - 1];
                if (filter is PdfDCTFilter)
                    return ImageFormat.JPEG;
                if (filter is PdfFaxFilter)
                    return ImageFormat.CCITT;
                if (filter is PdfJPXFilter)
                    return ImageFormat.JPEG2000;
                if (filter is PdfJBig2Filter)
                    return ImageFormat.JBIG2;
                return ImageFormat.RAW;
            }
        }

        /// <summary>
        /// Returns if the image has a color space
        /// </summary>
        /// <remarks>
        /// Usefull when working with J2K images, as they need not have a color space.
        /// </remarks>
        public bool HasColorSpace { get { return _elems.Contains("ColorSpace"); } }

        /// <summary>
        /// The image's color space
        /// </summary>
        /// <remarks>Required for images, except those that use the 
        /// JPXDecode filter; not allowed for image masks</remarks>
        public IColorSpace ColorSpace 
        { 
            get 
            {
                var cs = (IColorSpace)_elems.GetPdfType("ColorSpace", PdfType.ColorSpace, IntMsg.NoMessage, null);
                if (cs == null)
                {
                    if (ImageMask) return DeviceGray.Instance;
                    if (Format == ImageFormat.JPEG2000)
                    {
                        var ii = new ImageInfo(_stream.RawStream);

                        //Note that J2K do support more colorspaces than these, but they are not
                        //equivalent with PDF color spaces.
                        switch (ii.ColorSpace)
                        {
                            case ImageInfo.COLORSPACE.sRGB:
                                return DeviceRGB.Instance;
                            case ImageInfo.COLORSPACE.GRAY:
                                return DeviceGray.Instance;
                            case ImageInfo.COLORSPACE.CMYK:
                                return DeviceRGB.Instance;
                        }
                    }
                    throw new PdfReadException(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.Missing);
                }
                return cs;
            }
            set
            {
                if (ImageMask) throw new PdfNotSupportedException();
                _elems.SetItem("ColorSpace", (PdfItem) value, (value is IRef));

                //Should perhaps do a sanity check on the decode array, if there is one.
            }
        }

        /// <summary>
        /// Bits per image component
        /// </summary>
        /// <remarks>
        /// Optional for JPXDecode 
        /// The value shall be 1, 2, 4, 8, or (in PDF 1.5) 16
        /// 
        /// For ImageMask, CCITTFaxDecode and JBIG2Decode the value must be 1
        /// For RunLengthDecode and DCTDecode the value must be 8
        /// For LZWDecode and FlateDecode it must correspond with predictor
        /// </remarks>
        public int BitsPerComponent
        {
            get
            {
                if (ImageMask) return 1;
                return _elems.GetUIntEx("BitsPerComponent");
            }
        }

        /// <summary>
        /// Number of components in the image data
        /// </summary>
        public int NComponents
        {
            get
            {
                if (ImageMask) return 1;
                return ColorSpace.NComponents;
            }
        }

        /// <summary>
        /// An array of numbers describing how to map image samples into 
        /// the range of values appropriate for the image’s colour space
        /// </summary>
        public xRange[] Decode 
        { 
            get 
            { 
                var ret = (RealArray) _elems.GetPdfType("Decode", PdfType.RealArray);
                if (ret == null)
                {
                    if (ImageMask) return xRange.Create(new double[] {0, 1});
                    var cs = ColorSpace;
                    if (cs is IndexedCS)
                        return xRange.Create(new double[] { 0, (1 << BitsPerComponent) - 1 });
                    return xRange.Create(cs.DefaultDecode);
                }
                if (ret.Length != NComponents * 2)
                {
                    if (ret.Length < NComponents * 2)
                        throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                    var da = new double[NComponents * 2];
                    for (int c = 0; c < da.Length; c++)
                        da[c] = ret[c];
                    return xRange.Create(da);
                }
                return xRange.Create(ret.ToArray());
            }
            set
            {
                if (value == null)
                {
                    _elems.Remove("Decode");
                    return;
                }
                var cs = ColorSpace;
                if (value.Length != cs.NComponents)
                    throw new PdfNotSupportedException("Array is the wrong length");
                var da = xRange.ToArray(value);
                var def_decode = cs is IndexedCS ? new double[] { 0, (1 << BitsPerComponent) - 1 } : cs.DefaultDecode;
                if (Util.ArrayHelper.ArraysEqual<double>(da, def_decode))
                {
                    _elems.Remove("Decode");

                    //Consider removing the colorspace as well on J2P images.
                }
                else
                {
                    _elems.SetItem("Decode", new RealArray(da), false);
                    if (!HasColorSpace && !ImageMask)
                    {
                        //J2K files can have a embeded colorspace, but decode is ignored
                        //unless the colorspace is explicitly set, so we do that.
                        ColorSpace = cs;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a lookup table that converts raw image data "ushorts",
        /// interpolated into the decode range.
        /// 
        /// ranges sould be between 0 and 1, but Adobe accepts ranges beyond
        /// this with some odd results. Will have to experiment on this.
        ///  - Seems like Adobe clips, i.e. anything above 1 becomes 1.
        /// </summary>
        /// <returns>The lookup table</returns>
        internal double[,] DecodeLookupTable
        {
            get
            {
                return CreateDLT(Decode, 0, BitsPerComponent, ColorSpace.NComponents);
            }
        }

        /// <summary>
        /// The width of the image, in pixels
        /// </summary>
        public int Width { get { return _elems.GetUIntEx("Width"); } }

        /// <summary>
        /// The height of the image, in pixels
        /// </summary>
        public int Height { get { return _elems.GetUIntEx("Height"); } }

        /// <summary>
        /// If this image is an image mask
        /// </summary>
        public bool ImageMask 
        { 
            get { return _elems.GetBool("ImageMask", false); }
            set
            {
                if (value && !ImageMask && ColorSpace != DeviceGray.Instance && BitsPerComponent != 1)
                    throw new PdfNotSupportedException("To set as image mask, the image must be grayscale and 1 bits per component");
                 
                _elems.SetBool("ImageMask", value, false);
                if (value)
                    _elems.Remove("ColorSpace");
                else
                    ColorSpace = DeviceGray.Instance;
            }
        }

        /// <summary>
        /// Returns the pixels are a series of shorts. Each short is a color
        /// component of a pixel.
        /// </summary>
        /// <remarks>
        /// These components can be used with the DecodeLookupTable to get the
        /// color value. 
        /// </remarks>
        public ushort[] PixelComponents
        {
            get
            {
                //Fetches the raw pixel data
                var bytes = Stream.DecodedStream;

                //Output array
                int ncomp = NComponents;
                int bpc = BitsPerComponent;
                int width = Width, height = Height;
                var components = new ushort[ncomp * width * height];
                int stride = (width * ncomp * bpc + 7) / 8;

                //Note, images can have "junk" data.
                if (stride * height > bytes.Length)
                {
                    //Silently pads the data
                    //Todo: Log padding
                    //Debug.Assert(false, "Padding image");
                    Console.WriteLine("Padding image");
                    Array.Resize<byte>(ref bytes, stride * height);
                }
                if (width == 0 || height == 0 || bpc == 0)
                    throw new PdfReadException(PdfType.XObject, ErrCode.IsCorrupt);

                if (bpc > 16 || bpc < 1) 
                    throw new PdfNotSupportedException();
                var str = new Util.BitStream(bytes);

                //Must include the components in the width
                width *= NComponents;

                for (int c = 0; c < components.Length; )
                {
                    for (int i = 0; i < width; i++)
                        components[c++] = (ushort)str.FetchBits(bpc);
                    str.ByteAlign();
                }

                return components;
            }
        }

        /// <summary>
        /// Similar to PixelComponents, except the pixels are placed
        /// into sepperate arrays
        /// </summary>
        public int[][] SepPixelComponents
        {
            get
            {
                return GetSepPixelComponents(BitsPerComponent);
            }
        }

        internal int[][] GetSepPixelComponents(int bpc)
        {
            //Fetches the raw pixel data
            var bytes = Stream.DecodedStream;

            //Output array
            int ncomp = NComponents;
            int width = Width, height = Height;
            var components = new int[ncomp][];
            int stride = (width * ncomp * bpc + 7) / 8;
            int component_size = width * height;
            for (int c = 0; c < components.Length; c++)
                components[c] = new int[component_size];

            //Note, images can have "junk" data or be trunktuated.
            if (stride * height > bytes.Length)
            {
                //Silently pads the data
                //Todo: Log padding
                Debug.Assert(false, "Padding image");
                Array.Resize<byte>(ref bytes, stride * height);
            }
            if (width == 0 || height == 0 || bpc == 0)
                throw new PdfReadException(PdfType.XObject, ErrCode.IsCorrupt);

            if (bpc > Util.BitStream.MAX || bpc < 1) throw new PdfNotSupportedException();
            var str = new Util.BitStream(bytes);

            for (int c = 0; c < height; c++)
            {
                for (int i = 0; i < width; i++)
                    for (int k = 0; k < components.Length; k++)
                        components[k][c * width + i] = str.FetchBits(bpc);
                str.ByteAlign();
            }

            if (str.HasBits(1))
            {
                //Todo: Log this instead.
                //Debug.Assert(false, "Image has junk data");
            }

            return components;
        }

        /// <summary>
        /// Transparency mask assosiated with this image. Either
        /// a int[] with min, max for each channel or a PdfImage
        /// </summary>
        [PdfVersion("1.3")]
        public object Mask
        {
            get
            {
                var itm = _elems["Mask"];
                if (itm == null) return null;
                if (itm is PdfArray)
                    itm = ((IntArray)_elems.GetPdfType("Mask", PdfType.IntArray));
                if (itm is IntArray)
                {
                    var ia = ((IntArray)itm).ToArray();
                    if (ia.Length != NComponents * 2) 
                        throw new PdfReadException(PdfType.IntArray, ErrCode.Invalid);
                    return ia;
                }
                itm = _elems.GetPdfType("Mask", PdfType.XObject);
                if (!(itm is PdfImage))
                    throw new PdfCastException(PdfType.XObject, ErrCode.Wrong);
                var img = (PdfImage)itm;
                if (!img.ImageMask)
                    throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                return itm; 
            }
            set
            {
                if (value is int[])
                {
                    var ia = (int[])value;
                    if (ia.Length != NComponents * 2)
                        throw new PdfNotSupportedException("Int[] must be of length: 2 x NComponents");
                    _elems.SetItem("Mask", new IntArray(ia), false);
                }
                else if (value is PdfImage)
                {
                    var img = (PdfImage)value;
                    if (!img.ImageMask)
                        throw new PdfNotSupportedException("Colorspace must be DeviceGray");
                    _elems.SetItem("Mask", img, true);
                }
                else if (value == null)
                    _elems.Remove("Mask");
                else
                    throw new PdfNotSupportedException("Must be PdfImage or Int[]");
            }
        }

        /// <summary>
        /// Only relevant on images with SMasks. Note that if this entery is
        /// set the SMask must be the same size of the image. 
        /// </summary>
        public double[] Matte
        {
            get
            {
                var smask = SMask;
                if (smask == null) return null;
                var ret = (RealArray) smask._elems.GetPdfType("Matte", PdfType.RealArray);
                if (ret == null) return null;
                if (ret.Length != NComponents)
                    throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                if (ret != null) return ret.ToArray();
                return null;
            }
            set
            {
                var smask = SMask;
                if (smask == null)
                {
                    if (value == null) return;
                    throw new PdfNotSupportedException("Must have SMask to set Matte");
                }
                if (value == null) 
                {
                    smask._elems.Remove("Matte");
                    return;
                }
                if (value.Length != NComponents)
                    throw new PdfNotSupportedException("Matte must comply with the color space");
                if (smask.Width != Width || smask.Height != Height)
                    throw new PdfNotSupportedException("SMask must be the same size as the image");
                smask._elems.SetItem("Matte", new RealArray(value), false);
            }
        }

        /// <summary>
        /// Soft mask assosiated with this image.
        /// </summary>
        /// <remarks>
        /// Overrides the Mask entery if present.
        /// </remarks>
        [PdfVersion("1.4")]
        public PdfImage SMask 
        { 
            get 
            {
                var xobject = _elems.GetPdfType("SMask", PdfType.XObject, IntMsg.AssumeImage, null);
                if (xobject == null) return null;
                if (!(xobject is PdfImage))
                    throw new PdfCastException(PdfType.XObject, ErrCode.Wrong);
                var img = (PdfImage)xobject;
                if (img.ColorSpace != DeviceGray.Instance)
                    throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                if (img._elems.Contains("Matte")) 
                {
                    if (img.Width != Width || img.Height != Height)
                        throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                    var ra = (RealArray)img._elems.GetPdfType("Matte", PdfType.RealArray);
                    if (ra.Length != NComponents)
                        throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                }
                
                return img;
            }
            set
            {
                if (value == null)
                {
                    _elems.Remove("SMask");
                    return;
                }
                if (value.ColorSpace != DeviceGray.Instance)
                    throw new PdfNotSupportedException("ColorSpace must be DeviceGray");
                if (value.ImageMask)
                    throw new PdfNotSupportedException("Use \"Mask\" property for explicit ImageMasks");
                if (value._elems.Contains("Matte"))
                {
                    if (value.Width != Width || value.Height != Height)
                        throw new PdfNotSupportedException("SMask must be the same size as the image");
                    var ra = (RealArray)value._elems.GetPdfType("Matte", PdfType.RealArray);
                    if (ra.Length != NComponents)
                        throw new PdfNotSupportedException("Matte must comply with the color space");
                }
                _elems.SetItem("SMask", value, true);
            }
        }

        /// <summary>
        /// specifies whether soft-mask information packaged with the image samples shall be used
        /// </summary>
        /// <remarks>
        /// If SMaskInData is nonzero, there shall be only one opacity channel in the JPEG2000 data 
        /// and it shall apply to all colour channels.
        /// </remarks>
        public PdfAlphaInData SMaskInData { get { return (PdfAlphaInData) _elems.GetInt("SMaskInData", 0); } }

        /// <summary>
        /// The image data
        /// </summary>
        public IStream Stream 
        { 
            get { return _stream; }
        }

#endregion

#region Init

        /// <summary>
        /// Makes a temporary image that can be modified before adding it to a document
        /// </summary>
        /// <param name="PixelData">Raw pixel data</param>
        /// <param name="cs">Color Space</param>
        /// <param name="decode">Optional decode array</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="bpc">Bits per component</param>
        /// <param name="alpha">Alpha in data, only relevant for J2K images. </param>
        public PdfImage(NewStream image_data, IColorSpace cs, xRange[] decode, int width, int height, int bpc, PdfAlphaInData alpha)
            : base(image_data, image_data.Elements)
        {
            _elems.SetName("Subtype", "Image");
            _elems.SetInt("Width", width);
            _elems.SetInt("Height", height);
            if (cs != null)
                _elems.SetNewItem("ColorSpace", (PdfItem) cs, true);
            if (decode != null)
            {
                if (cs != null && decode.Length != cs.NComponents)
                    throw new PdfNotSupportedException("Array is the wrong length");
                _elems.SetNewItem("Decode", new RealArray(xRange.ToArray(decode)), false);
            }
            if (bpc > 0)
                _elems.SetInt("BitsPerComponent", bpc);
            if (alpha > 0)
                _elems.SetInt("SMaskInData", (int) alpha);
        }

        /// <summary>
        /// Makes a new image that can be added to a document
        /// </summary>
        /// <param name="PixelData">Raw pixel data</param>
        /// <param name="cs">Color Space</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="bpc">Bits per color component</param>
        public PdfImage(byte[] PixelData, IColorSpace cs, int width, int height, int bpc)
            : this(new TemporaryDictionary(), PixelData)
        {
            int bpp = (cs != null) ? cs.NComponents * bpc : bpc;
            int stride = (width * bpp + 7) / 8;
            if (PixelData.Length != stride * height)
                throw new ArgumentException("PixelData is of incorect length");

            _elems.SetName("Subtype", "Image");
            _elems.SetInt("Width", width);
            _elems.SetInt("Height", height);
            if (cs == DeviceGray.Instance)
                _elems.SetName("ColorSpace", "DeviceGray");
            else if (cs == DeviceRGB.Instance)
                _elems.SetName("ColorSpace", "DeviceRGB");
            else if (cs == DeviceCMYK.Instance)
                _elems.SetName("ColorSpace", "DeviceCMYK");
            else
                _elems.SetItem("ColorSpace", (PdfItem) cs, true);

            _elems.SetInt("BitsPerComponent", bpc);
        }

        /// <summary>
        /// Creates an image mask.
        /// </summary>
        /// <param name="BitData">Raw pixel data</param>
        /// <param name="cs">Color Space</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="bpp">Bits per pixel</param>
        public PdfImage(byte[] BitData, int width, int height)
            : this(new TemporaryDictionary(), BitData)
        {
            int stride = (width + 7) / 8;
            if (BitData.Length != stride * height)
                throw new ArgumentException("PixelData is of incorect length");

            _elems.SetName("Subtype", "Image");
            _elems.SetInt("Width", width);
            _elems.SetInt("Height", height);
            _elems.SetNewItem("ImageMask", PdfBool.True, false);
        }

        internal PdfImage(IWStream stream)
            : base(stream)
        {
            //Thumbnails do not require subtype (but if it has one, it must be image)
            //var ret = _elems.GetName("Subtype");
            //if ((ret != null) && !ret.Equals("Image"))
            //    throw new PdfReadException(SR.WrongType);
        }

        /// <summary>
        /// Used when moving the image to a different document
        /// </summary>
        /// <remarks>Don't truly need a separate constructor for this, but I want
        /// to stay consistent with other classes</remarks>
        internal PdfImage(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        { }

        private PdfImage(PdfDictionary dict, byte[] pixeldata)
            : base(new WritableStream(pixeldata, dict), dict) { }

#endregion

#region IERef

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is PdfImage)
                return IsLike((PdfXObject)obj) == (int)Equivalence.Identical;
            return false;
        }

        protected override int IsLike(PdfXObject obj)
        {
            var img = (PdfImage)obj;
            if (this.Width == img.Width && this.Height == img.Height &&
                this.BitsPerComponent == img.BitsPerComponent &&
                this.NComponents == img.NComponents &&
                Util.ArrayHelper.ArraysEqual<xRange>(this.Decode,  img.Decode) &&
                this.SMaskInData == img.SMaskInData &&
                Util.ArrayHelper.ArraysEqual<double>(this.Matte, img.Matte) &&
                this.ColorSpace.Equals(img.ColorSpace) &&
                this.ImageMask == img.ImageMask &&
                PdfStream.Equals(_stream, img._stream))
            {
                var smask = SMask;
                if (smask != null)
                    return (int) (smask.Equivalent(img.SMask) ? Equivalence.Identical : Equivalence.Similar);
                else
                {
                    if (img.SMask == null)
                    {
                        var mask = Mask;
                        if (mask == null) return (int)(img.Mask == null ? Equivalence.Identical : Equivalence.Similar);
                        var img_mask = img.Mask;
                        if (mask is int[])
                        {
                            if (img_mask is int[] && Util.ArrayHelper.ArraysEqual<int>((int[])mask, (int[])img_mask))
                                return (int) Equivalence.Identical;
                        }
                        else if (((PdfImage) mask).Equivalent(img_mask))
                            return (int)Equivalence.Identical;  
                    }
                    return (int) Equivalence.Similar;
                }
            }

            return (int)Equivalence.Different;
        }

#endregion

#region Image decoding functions

#if !USE_PIXEL_COVERTER

        /// <summary>
        /// Decodes an image into a single channel
        /// </summary>
        /// <param name="align">Set true if the resulting image is to be 4 byte aligned</param>
        /// <returns>Bytes in gray8 format</returns>
        /// <remarks>Can probably be removed in favor of a pixel_converter impl.</remarks>
        internal byte[] DecodeGrayImage(bool align, IPixelConverter pixel_converter)
        {
            if (Format == ImageFormat.JPEG2000)
                return DecodeGrayImage2K(align, pixel_converter);

            //Gray images can not have alpha data, SMask and such is ignored.

            //Fetches the original data, currently in some unknown
            //format.
            var comps = PixelComponents;

            //Creates an array large enough to hold one pixel.
            var pixel = new double[NComponents];

            //The decode lookup table is used to transform data from
            //the "unknown" format to one the color converter understands 
            var decode = DecodeLookupTable;

            //The color converter is used to convert colors from their
            //original colorspace to a RGB colorspace. 
            var cs = ColorSpace.Converter;

            //An array large enough to hold all pixels after conversion
            int height = Height;
            int stride = Width, width = stride;
            if (align) stride = (int) (Math.Ceiling(width / 4d) * 4);
            var bytes = new byte[height * stride];

            //Reads out one pixel at a time and converts it to Gray8
            for (int y = 0, c = 0; y < height; y++)
            {
                int row = y * stride;
                for (int j = 0; j < width && c < comps.Length; )
                {
                    //Fetches data and converts it into a pixel
                    for (int i = 0; i < pixel.Length; i++)
                        pixel[i] = decode[i, comps[c++]];

                    //Converts the pixel to a RGB color
                    var col = cs.MakeGrayColor(pixel);

                    //Converts the color to Gray8. This step
                    //reduces precision to 8BPP
                    bytes[row + j++] = Clip((int)(col.Gray * byte.MaxValue));
                }
            }

            return bytes;
        }

        private byte[] DecodeGrayImage2K(bool align, IPixelConverter pixel_converter)
        {
#region Step 1. Fetches the raw jpx data

            var raw_jpx_data = _stream.DecodeTo<PdfJPXFilter>();
            JPXImage jpx = JPX.Decompress(raw_jpx_data);

#endregion

#region Step 2. Determine color space

            var cs = (IColorSpace)_elems.GetPdfType("ColorSpace", PdfType.ColorSpace, IntMsg.NoMessage, null);
            bool use_decode = false;

            if (cs != null)
                use_decode = true;
            else
                cs = JPX.ResolveJPXColorSpace(jpx);

            //Assumption: There's only one alpha channel
            //Also, the OpenJpeg libary will change the order of the channels so that
            //they go along the color space, with the alpha channel comming last.
            bool alpha = SMaskInData != 0;
            int n_channels = cs.NComponents + (alpha ? 1 : 0);
            if (n_channels != jpx.NumberOfComponents)
                if (!jpx.HasAlpha || n_channels == jpx.NumberOfComponents - 1)
                    throw new PdfInternalException("JPX image has a unexpected number of colors");

#endregion

#region Step 3. Resize channels
            //I've not yet investigated what impact dx,dy and different resolutions
            //have on the channels. 

            //Gets the full image resolution
            int width = jpx.Width, height = jpx.Height;

            //Resize channels if needed
            var comps = jpx.Components;
            var cc = new int[comps.Length - (alpha ? 1 : 0)][];
            for (int c = 0; c < cc.Length; c++)
            {
                var comp = comps[c];
                cc[c] = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }
            int[] alpha_channel = null;
            if (alpha)
            {
                var comp = comps[cc.Length];
                alpha_channel = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }

#endregion

#region Step 4. Creates alpha channel

            if (!alpha)
            {
                var smask = SMask;
                if (smask != null)
                {
                    //smasks, matte, etc, are handled elsewhere (in this function)

                }
                else
                {
                    var mask = Mask;
                    if (mask is int[])
                    {
                        //Creates an alpha channel for color key masks
                        alpha_channel = new int[cc[0].Length];
                        var imask = (int[])mask;
                        int max_value = 1;
                        for (int c = 0; c < alpha_channel.Length; c++)
                        {
                            for (int i = 0, t = 0; i < cc.Length; i++)
                            {
                                var r = cc[i][c];

                                //If any of the components are outside the range,
                                //the pixel is opague.
                                if (!(imask[t++] <= r && r <= imask[t++]))
                                {
                                    //Setting bpp to 1 does not yeild good result.
                                    //Not on filesize or blending
                                    alpha_channel[c] = max_value;
                                    break;
                                }
                            }
                        }

                        //We sneak in a new component so that the alpha lut will be created
                        Array.Resize<ImageComp>(ref comps, comps.Length + 1);
                        comps[comps.Length - 1] = new ImageComp(1, 1, false, 1, 1, width, height, alpha_channel);
                        alpha = true;
                        n_channels++;
                    }
                }
            }

#endregion

#region Step 5. Create lookup tables

            double[][] dlut = new double[n_channels][];
            xRange[] ranges = use_decode ? Decode : xRange.Create(cs.DefaultDecode);
            if (alpha)
            {
                //To take advantage of the little "last_bpp cache" we slip the alpha lut
                //in instead of simply computing it explicitly.
                Array.Resize<xRange>(ref ranges, n_channels);
                ranges[n_channels - 1] = new xRange(0, 1);
            }

            for (int c = 0, last_bpp = 0; c < comps.Length; c++)
            {
                var comp = comps[c];
                int bpp = comp.Prec;
                if (last_bpp == bpp && ranges[c] == ranges[c - 1])
                    dlut[c] = dlut[c - 1]; //<-- Using old LUT instead of making a new one
                else
                {
                    dlut[c] = CreateDLT(ranges[c], bpp);
                    last_bpp = bpp;
                }
            }

#endregion

#region Step 6. Convert to Gray8
            int stride = width;
            if (align) stride = (int) (Math.Ceiling(width / 4d) * 4);
            var bytes = new byte[height * stride];
            var pixel = new double[cs.NComponents];
            var color_converter = cs.Converter;
            int size = cc[0].Length;

            //Step 6.1 undo matte
            var matte = Matte;
            if (matte != null)
            {
                //Note. Step 7 i
                var smask = SMask;
                var mask_comps = smask.PixelComponents;
                var mask_decode = smask.DecodeLookupTable;

                //Reads out one pixel at a time and converts it to Gray8
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int c = 0, j = 0; j < width && c < size; c++)
                    {
                        //Converting the alpha to a double using it's lookup table
                        var a = mask_decode[0, mask_comps[c]];
                        for (int i = 0; i < pixel.Length; i++)
                        {
                            //c = (c' - m) / a + m
                            var m = matte[i];

                            //First the integer component is converted to a double using the "dlut",
                            //then a bit of math with the matte and alpha values..
                            pixel[i] = (dlut[i][cc[i][c]] - m) / a + m;
                        }

                        //Converts it to a RGB color
                        var col = color_converter.MakeGrayColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[row + j++] = Clip((int)(col.Gray * byte.MaxValue));
                    }
                }
            }
            else
            {
                //Reads out one pixel at a time and converts it to Gray8
                for (int y = 0, c = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int j = 0; j < width && c < size; c++)
                    {
                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][cc[i][c]];

                        //Converts it to a RGB color
                        var col = color_converter.MakeGrayColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[row + j++] = Clip((int)(col.Gray * byte.MaxValue));
                    }
                }
            }

#endregion

            return bytes;
        }
#endif

        /// <summary>
        /// Decodes an image into bgra32 format.
        /// </summary>
        /// <remarks>
        /// Pipeline
        /// raw pixel data -> decode -> ColspaceToRGB -> return
        /// </remarks>
        /// <returns>Bytes in bgra32 format</returns>
        public byte[] DecodeImage(ConvertPixels pixel_converter = null)
        {
            if (Format == ImageFormat.JPEG2000)
                return DecodeImage2K(pixel_converter);

            if (_elems.Contains("SMask"))
            {
                //Images with softmask may have been premultiplied
                //with alpha. Undoing that.
                var matte = Matte;
                if (matte != null)
                    return DecodeMatteImage(matte, pixel_converter);
            }
            else
            {
                var mask = Mask;
                if (mask != null)
                {
                    if (mask is int[])
                        return DecodeKeyedImage((int[])mask, pixel_converter);
                }
            }

            //Fetches the original data, currently in some unknown
            //format.
            var comps = PixelComponents;

            //The decode lookup table is used to transform data from
            //the "unknown" format to one the color converter understands 
            var decode = DecodeLookupTable;

            //Creates an array large enough to hold one pixel.
            var pixel = new double[NComponents];

#if USE_PIXEL_COVERTER
            if (pixel_converter == null)
                pixel_converter = BGRA32.Convert;

            int c = 0;

            return pixel_converter(pixel, false, Width, Height, ColorSpace.Converter, new FetchColorData(() =>
            {
                //Fetches data and converts it into a pixel
                for (int i = 0; i < pixel.Length; i++)
                    pixel[i] = decode[i, comps[c++]];

                return null;
            }));
#else
            //The color converter is used to convert colors from their
            //original colorspace to a RGB colorspace. 
            var cs = ColorSpace.Converter;

            //An array large enough to hold all pixels after conversion
            var bytes = new byte[4 * Height * Width];

            //Reads out one pixel at a time and converts it to BGRA32
            for (int c = 0, j=0; c < comps.Length; )
            {
                //Fetches data and converts it into a pixel
                for (int i = 0; i < pixel.Length; i++)
                    pixel[i] = decode[i, comps[c++]];
               
                //Converts the pixel to a RGB color
                var col = cs.MakeColor(pixel);

                //Converts the color to bgra32. This step
                //reduces precision to 8BPP
                bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                bytes[j++] = byte.MaxValue;
            }

            return bytes;
#endif
        }

        /// <summary>
        /// Decodes a Jpeg 2000 image
        /// </summary>
        /// <remarks>
        /// I assume that the Jpeg 2000 filter is always the final filter*.
        /// Conceptially one can compress any data with the Jpeg2000 filter,
        /// and use it as part of a chain of filters.
        /// 
        /// However Adobe special cases the J2K filter, so you can't for 
        /// instance deflate compress a J2K image like you can a jpeg image. 
        /// 
        /// I've not tested A85 filtered J2K but that would presumably work.
        /// 
        /// Nor have I tested using the J2K filter as a general filter, say
        /// compress the stream trailer with J2K like how it sometimes
        /// is PNG compressed by some PDF writers. J2K supports loosless 
        /// compression, and can be used for compressing any data, but I 
        /// suspect Adobe will bark at such use.
        /// 
        /// * It is possible to put deflated data into a J2K, and
        ///   set up a filter chain to inflate that data after J2K 
        ///   decompression. This means PDFLib does not support that.
        ///   (Nor, do I suspect, Adobe)
        /// </remarks>
        internal byte[] DecodeImage2K(ConvertPixels pixel_converter = null)
        {
#region Step 1. Fetches the raw jpx data

            var raw_jpx_data = _stream.DecodeTo<PdfJPXFilter>();
            JPXImage jpx = JPX.Decompress(raw_jpx_data, true);

#endregion

#region Step 2. Determine color space

            var cs = (IColorSpace)_elems.GetPdfType("ColorSpace", PdfType.ColorSpace, IntMsg.NoMessage, null);
            bool use_decode = false;

            if (cs != null)
                use_decode = true;
            else
            {
                //When there's no color space supplied, use the one specified in the JPX header.
                jpx.ApplyIndex();
                cs = JPX.ResolveJPXColorSpace(jpx);
            }

            //Assumption: There's only one alpha channel
            //Also, the OpenJpeg libary will change the order of the channels so that
            //they go along the color space, with the alpha channel comming last.
            bool alpha = SMaskInData != 0;
            int n_channels = cs.NComponents + (alpha ? 1 : 0);
            if (n_channels != jpx.NumberOfComponents)
                if (!jpx.HasAlpha || n_channels == jpx.NumberOfComponents - 1)
                    throw new PdfInternalException("JPX image has a unexpected number of colors");

#endregion

#region Step 3. Resize channels
            //I've not yet investigated what impact dx,dy and different resolutions
            //have on the channels. 

            //Gets the full image resolution
            int width = jpx.Width, height = jpx.Height;

            //Resize channels if needed
            var comps = jpx.Components;
            var cc = new int[comps.Length - (alpha ? 1 : 0)][];
            for (int c = 0; c < cc.Length; c++)
            {
                var comp = comps[c];
                cc[c] = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }
            int[] alpha_channel = null;
            if (alpha)
            {
                var comp = comps[cc.Length];
                alpha_channel = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }

#endregion

#region Step 4. Creates alpha channel

            if (!alpha)
            {
                var smask = SMask;
                if (smask != null)
                {
                    //smasks, matte, etc, are handled elsewhere (in this function)

                }
                else
                {
                    var mask = Mask;
                    if (mask is int[])
                    {
                        //Creates an alpha channel for color key masks
                        alpha_channel = new int[cc[0].Length];
                        var imask = (int[])mask;
                        int max_value = 1;
                        for (int c = 0; c < alpha_channel.Length; c++)
                        {
                            for (int i = 0, t = 0; i < cc.Length; i++)
                            {
                                var r = cc[i][c];

                                //If any of the components are outside the range,
                                //the pixel is opague.
                                if (!(imask[t++] <= r && r <= imask[t++]))
                                {
                                    //Setting bpp to 1 does not yeild good result.
                                    //Not on filesize or blending
                                    alpha_channel[c] = max_value;
                                    break;
                                }
                            }
                        }

                        //We sneak in a new component so that the alpha lut will be created
                        Array.Resize<ImageComp>(ref comps, comps.Length + 1);
                        comps[comps.Length - 1] = new ImageComp(1, 1, false, 1, 1, width, height, alpha_channel);
                        alpha = true;
                        n_channels++;
                    }
                }
            }

#endregion

#region Step 5. Create lookup tables

            double[][] dlut = new double[n_channels][];
            xRange[] ranges = use_decode ? Decode : xRange.Create(cs.DefaultDecode);
            if (alpha)
            {
                //To take advantage of the small "last_bpp cache" we slip the alpha lut
                //in instead of simply computing it explicitly.
                Array.Resize<xRange>(ref ranges, n_channels);
                ranges[n_channels - 1] = new xRange(0, 1);
            }
     
            for (int c=0, last_bpp = 0; c < comps.Length; c++)
            {
                var comp = comps[c];
                int bpp = comp.Prec;
                if (last_bpp == bpp && ranges[c] == ranges[c-1])
                    dlut[c] = dlut[c - 1]; //<-- Using old LUT instead of making a new one
                else
                {
                    dlut[c] = CreateDLT(ranges[c], bpp);
                    last_bpp = bpp;
                }
            }

#endregion

#region Step 6. Convert to BGRA32
#if USE_PIXEL_COVERTER
            if (pixel_converter == null)
                pixel_converter = BGRA32.Convert;
            int px = 0;
            byte[] bytes;
#else
            var bytes = new byte[4 * height * width];
            
            var color_converter = cs.Converter;
#endif
            int size = cc[0].Length;
            var pixel = new double[cs.NComponents];

            if (alpha)
            {
                //The alpha mask could altenativly be applied during the
                //rendering, as is the case when she SMask is set on the image
                var alpha_lut = dlut[cc.Length];

                if (SMaskInData == PdfAlphaInData.PreblendedAlpha)
                {
                    var alpha_max = (1 << comps[cc.Length].Prec) - 1;

#if USE_PIXEL_COVERTER
                    bytes = pixel_converter(pixel, true, Width, Height, cs.Converter, new FetchColorData(() =>
                    {
                        //All this lookup table does is convert this value
                        //to a double. Similar to a / amax.
                        var a = alpha_lut[alpha_channel[px]];

                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][Clip((int)(cc[i][px] / a), alpha_max)];

                        return alpha_lut[alpha_channel[px++]];
                    }));
#else
                    //The alpha colors have been preblended, undoing that and
                    //converts straight to BGRA32 while we're at it
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        //All this lookup table does is convert this value
                        //to a double. Similar to a / amax.
                        var a = alpha_lut[alpha_channel[c]];

                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][Clip((int)(cc[i][c] / a), alpha_max)];

                        //Converts it to a RGB color
                        var col = color_converter.MakeColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = (byte)(a * byte.MaxValue);
                    }
#endif
                }
                else
                {
#if USE_PIXEL_COVERTER
                    bytes = pixel_converter(pixel, true, Width, Height, cs.Converter, new FetchColorData(() =>
                    {
                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][cc[i][px]];

                        return alpha_lut[alpha_channel[px++]];
                    }));
#else
                    //Reads out one pixel at a time and converts it to BGRA32
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][cc[i][c]];

                        //Converts it to a RGB color
                        var col = color_converter.MakeColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = (byte)(alpha_lut[alpha_channel[c]] * byte.MaxValue);
                    }
#endif
                }
            }
            else
            {
                //Step 6.1 undo matte
                var matte = Matte;
                if (matte != null)
                {
                    //Note. Step 7 i
                    var smask = SMask;
                    var mask_comps = smask.PixelComponents;
                    var mask_decode = smask.DecodeLookupTable;
#if USE_PIXEL_COVERTER
                    bytes = pixel_converter(pixel, false, Width, Height, cs.Converter, new FetchColorData(() =>
                    {
                        //Converting the alpha to a double using it's lookup table
                        var a = mask_decode[0, mask_comps[px]];
                        for (int i = 0; i < pixel.Length; i++)
                        {
                            //c = (c' - m) / a + m
                            var m = matte[i];

                            //First the integer component is converted to a double using the "dlut",
                            //then a bit of math with the matte and alpha values..
                            pixel[i] = (dlut[i][cc[i][px]] - m) / a + m;
                        }

                        px++;
                        return null; //The actual SMask is applied elsewhere
                    }));
#else
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        //Converting the alpha to a double using it's lookup table
                        var a = mask_decode[0, mask_comps[c]];
                        for (int i = 0; i < pixel.Length; i++)
                        {
                            //c = (c' - m) / a + m
                            var m = matte[i];

                            //First the integer component is converted to a double using the "dlut",
                            //then a bit of math with the matte and alpha values..
                            pixel[i] = (dlut[i][cc[i][c]] - m) / a + m;
                        }

                        //Converts it to a RGB color
                        var col = color_converter.MakeColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = byte.MaxValue; //The actual SMask is applied elsewhere
                    }
#endif
                }
                else
                {
#if USE_PIXEL_COVERTER
                    bytes = pixel_converter(pixel, false, Width, Height, cs.Converter, new FetchColorData(() =>
                    {
                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][cc[i][px]];

                        px++;
                        return null;
                    }));
#else
                    //Reads out one pixel at a time and converts it to BGRA32
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        //Fills the pixel with data
                        for (int i = 0; i < pixel.Length; i++)
                            pixel[i] = dlut[i][cc[i][c]];

                        //Converts it to a RGB color
                        var col = color_converter.MakeColor(pixel);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = byte.MaxValue;
                    }
#endif
                }
            }

#endregion

            return bytes;
        }

        /// <summary>
        /// Decodes SMask into bgra32
        /// </summary>
        internal byte[] DecodeSMaskImage()
        {
            //SMasks have no color key and colorspace is gray
            var comps = PixelComponents;
            double pixel;
            var decode = DecodeLookupTable;
            var cs = ColorSpace;
            var bytes = new byte[4 * Height * Width];

            if (cs != DeviceGray.Instance)
                throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);

            //Reads out one pixel at a time and converts it to BGRA32
            for (int c = 0, j = 0; c < comps.Length; )
            {
                //Color value will be used as is
                pixel = decode[0, comps[c++]];

                //Converts the color to bgra32. 
                j += 3;
                bytes[j++] = Clip((int)(pixel * byte.MaxValue));
            }

            return bytes;
        }

        /// <summary>
        /// Decodes a image and blends it with a matte color, as explained
        /// in: 11.6.5.3 Soft-Mask Images
        /// </summary>
        /// <remarks>
        /// Pipeline
        /// raw pixel data -> decode -> Add matte -> ColspaceToRGB -> return
        /// </remarks>
        private byte[] DecodeMatteImage(double[] matte, ConvertPixels pixel_converter)
        {
            //Fetches the original data, currently in some unknown
            //format.
            var comps = PixelComponents;

            //Creates an array large enough to hold one pixel.
            var pixel = new double[NComponents];

            //The decode lookup table is used to transform data from
            //the "unknown" format to one the color converter understands 
            var decode = DecodeLookupTable;

            //We need the alpha components of the image to undo the
            //premultiplied alpha (Matte)
            var mask_comps = SMask.PixelComponents;
            var mask_decode = SMask.DecodeLookupTable;

#if USE_PIXEL_COVERTER
            if (pixel_converter == null)
                pixel_converter = BGRA32.Convert;

            int c = 0, k = 0;

            return pixel_converter(pixel, false, Width, Height, ColorSpace.Converter, new FetchColorData(() =>
            {
                //Fetches the alpha component of the pixel
                var a = mask_decode[0, mask_comps[k++]];

                //Fetches the pixel and undoes the premultipled alpha
                for (int i = 0; i < pixel.Length; i++)
                {
                    //c = (c' - m) / a + m
                    // c = color component
                    // c' = color component with premultipled alpha
                    // m = matte component
                    // a = alpha component
                    var m = matte[i];

                    //First the integer component is converted to a double using the "dlut",
                    //then a bit of math with the matte and alpha values.
                    pixel[i] = (decode[i, comps[c++]] - m) / a + m;
                }

                return null; //<-- the SMask is applied during rendering
            }));
#else
            //The color converter is used to convert colors from their
            //original colorspace to a RGB colorspace. 
            var cs = ColorSpace.Converter;

            //An array large enough to hold all pixels after conversion
            var bytes = new byte[4 * Height * Width];

            //Reads out one pixel at a time and converts it to BGRA32
            for (int c = 0, j = 0, k = 0; c < comps.Length;)
            {
                //Fetches the alpha component of the pixel
                var a = mask_decode[0, mask_comps[k++]];

                //Fetches the pixel and undoes the premultipled alpha
                for (int i = 0; i < pixel.Length; i++)
                {
                    //c = (c' - m) / a + m
                    // c = color component
                    // c' = color component with premultipled alpha
                    // m = matte component
                    // a = alpha component
                    var m = matte[i];

                    //First the integer component is converted to a double using the "dlut",
                    //then a bit of math with the matte and alpha values.
                    pixel[i] = (decode[i, comps[c++]] - m) / a + m;

#region Old formula
                    ////c = (c' - m + a x m) / a
                    //var mi = matte[i];
                    //pixel[i] = (decode[i, comps[c++]] - mi + a * mi) / a;
#endregion
                }

                //Converts it to a RGB color
                var col = cs.MakeColor(pixel);

                //Converts the color to bgra32. 
                bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                bytes[j++] = byte.MaxValue; //<-- the SMask is applied during rendering

            }

            return bytes;
#endif
        }

        /// <summary>
        /// Renders with color key. Note that one can't use
        /// a color key with SMask or Mask images, so it need
        /// not be supported.
        /// </summary>
        /// <remarks>
        /// Pipeline
        /// raw pixel data -> color key -> decode -> ColspaceToRGB -> return
        /// </remarks>
        private byte[] DecodeKeyedImage(int[] mask, ConvertPixels pixel_converter)
        {
            var comps = PixelComponents;
            
            //Holds a pixel's data after decode
            var pixel = new double[NComponents];
            
            //Holds a pixel's data before decode
            var raw = new int[pixel.Length];

            var decode = DecodeLookupTable;

#if USE_PIXEL_COVERTER
            if (pixel_converter == null)
                pixel_converter = BGRA32.Convert;

            int c = 0;

            return pixel_converter(pixel, true, Width, Height, ColorSpace.Converter, new FetchColorData(() =>
            {
                //Fetches data for one pixel and checks if that pixel
                //is transparent
                bool transparent = true;
                for (int i = 0, t = 0; i < pixel.Length; i++)
                {
                    var r = comps[c++];
                    raw[i] = r;

                    //If any of the components are outside the range,
                    //the pixel is opague.
                    if (transparent)
                        transparent = (mask[t++] <= r && r <= mask[t++]);
                }

                //Looks up the pixel's values in the decode lookup table.
                // (one could simply do j += 4 for transparant pixels though)
                for (int i = 0; i < pixel.Length; i++)
                    pixel[i] = decode[i, raw[i]];

                return transparent ? 0 : 1;
            }));
#else

            var cs = ColorSpace.Converter;

            //An array large enough to hold all pixels after conversion
            var bytes = new byte[4 * Height * Width];

            //Reads out one pixel at a time and converts it to BGRA32
            for (int c = 0, j = 0; c < comps.Length; )
            {
                //Fetches data for one pixel and checks if that pixel
                //is transparent
                bool transparent = true;
                for (int i = 0, t=0; i < pixel.Length; i++)
                {
                    var r = comps[c++];
                    raw[i] = r;

                    //If any of the components are outside the range,
                    //the pixel is opague.
                    if (transparent)
                        transparent = (mask[t++] <= r && r <= mask[t++]);
                }

                //Looks up the pixel's values in the decode lookup table.
                // (one could simply do j += 4 for transparant pixels though)
                for (int i = 0; i < pixel.Length; i++)
                    pixel[i] = decode[i, raw[i]];

                //Converts the pixel data to a RGB color object
                var col = cs.MakeColor(pixel);

                //Converts the color to bgra32 format. 
                bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                bytes[j++] = (transparent) ? byte.MinValue : byte.MaxValue;
            }

            return bytes;
#endif
        }

        /// <summary>
        /// Clips an integer value to a byte. This is not the same as (byte), as here
        /// there integer is set to 0 if it's smaller, and max if it's larger
        /// </summary>
        /// <param name="val">Value to clip to 0 - 255</param>
        /// <returns>Clipped value</returns>
        /// <remarks>
        /// Adobe clips "out of range" color values. 
        /// </remarks>
        public static byte Clip(int val) { return (val < 0) ? byte.MinValue : (val > byte.MaxValue) ? byte.MaxValue : (byte)val; }
        private int Clip(int val, int max) { return (val < 0) ? 0 : (val > max) ? max : val; }

#endregion

#region Required overrides

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfImage(stream,  dict);
        }

#endregion

#region Inline image support

        /// <summary>
        /// Writes an inline image.
        /// </summary>
        internal void WriteInline(PdfWriter write, PdfResources res)
        {
            write.WriteKeyword("BI");

            //Writes out the dictionary in a inline format.
            foreach (var kp in _elems)
            {
                if ("Length".Equals(kp.Key) || kp.Value == PdfNull.Value)
                    continue;

                write.WriteName(ContractBIKeyName(kp.Key));

                if ("Filter".Equals(kp.Key))
                {
                    //Reduces the size of the filter names
                    var filter_ar = _stream.Filter;
                    if (filter_ar.Length == 1)
                        write.WriteName(ShortFilterName(filter_ar[0].Name));
                    else
                    {
                        var filters = new PdfItem[filter_ar.Length];
                        for (int c = 0; c < filters.Length; c++)
                            filters[c] = new PdfName(ShortFilterName(filter_ar[c].Name));
                        write.WriteArray(filters);
                    }
                }
                else if ("ColorSpace".Equals(kp.Key))
                {
                    var cs = ColorSpace;
                    var short_name = ShortColorSpaceName(PdfColorSpace.Create(cs));
                    if (short_name != null)
                        write.WriteName(short_name);
                    // I must have misunderstood the specs as this does not render in adobe:
                    // It's also inefficient if the color space is used more than once. 
                    //
                    // Note, there may be a limit to what color space can be used with an
                    // inline image. I've not done any experimentation here.
                    /*else if (cs is IndexedCS)
                    {
                        //Indexed colorspace must use a Device colorspace as the underlying cs
                        //and a bytestring for the palatte. (Which is a PDF v.1.2 feature BTW)
                        var idx = (IndexedCS)cs;
                        var indexed = new PdfItem[4];
                        indexed[0] = new PdfName("I");
                        short_name = ShortColorSpaceName(PdfColorSpace.Create(idx.Base));
                        if (short_name == null) 
                            throw new PdfNotSupportedException("Color space must be of type \"Device\"");
                        indexed[1] = new PdfName(short_name);
                        indexed[2] = new PdfInt(idx.Hival);
                        indexed[3] = new PdfString(idx.Lookup, false, false);
                        write.WriteArray(indexed);
                    }//*/
                    else
                    {
                        //Puts the colorspace in the page's resources.
                        if (write.HeaderVersion < PdfVersion.V12)
                            throw new PdfNotSupportedException("Requires Pdf v.1.2");
                        write.WriteName(res.ColorSpace.Add(cs));
                    }
                }
                else kp.Value.Write(write);
            }

            write.WriteKeyword("ID\n");

            //Writes raw binary data. No escaping, or anything of that sort.
            write.WriteRaw(Stream.RawStream);

            if (write.PaddMode == PM.None)
                write.WriteRaw("EI");
            else
                write.WriteKeyword("EI");
        }

        internal static string ShortColorSpaceName(string str)
        {
            switch (str)
            {
                case "DeviceGray": return "G";
                case "DeviceRGB": return "RGB";
                case "DeviceCMYK": return "CMYK";
                case "Indexed": return "I";
            }
            return null;
        }

        internal static string ShortFilterName(string str)
        {
            switch (str)
            {
                case "ASCIIHexDecode": return "AHx";
                case "ASCII85Decode": return "A85";
                case "LZWDecode": return "LZW";
                case "FlateDecode": return "Fl";
                case "RunLengthDecode": return "RL";
                case "CCITTFaxDecode": return "CCF";
                case "DCTDecode": return "DCT";
            }

            return str;
        }

        /// <summary>
        /// Expands inline image name
        /// </summary>
        /// <param name="itm">Name or item</param>
        /// <returns>Expanded item or the original item</returns>
        internal static PdfItem ExpandBIName(PdfItem itm)
        {
            if (itm.Type != PdfType.Name)
            {
                //May be an indexed colorspace
                if (itm is PdfArray)
                {
                    var ar = (PdfArray)itm;
                    if (ar.Length == 4)
                    {
                        var ar0 = ar.GetName(0);
                        if (ar0 != null && ("I".Equals(ar0.Value) || "Indexed".Equals(ar0.Value)))
                        {
                            //An indexed colorspace must be remade.
                            var indexed_cs = new PdfItem[4];
                            indexed_cs[0] = new PdfName("Indexed");
                            indexed_cs[1] = ExpandBIName(ar[1]);
                            indexed_cs[2] = ar[2];
                            indexed_cs[3] = ar[3];
                            if (ar.IsWritable)
                            {
                                var tracker = ar.Tracker;
                                if (tracker == null)
                                    return new IndexedCS(new TemporaryArray(indexed_cs));
                                return new IndexedCS(new WritableArray(indexed_cs, tracker));
                            }
                            return new IndexedCS(new SealedArray(indexed_cs));
                        }
                    }
                }

                return itm;
            }

            switch (itm.GetString())
            {
                case "G": return new PdfName("DeviceGray");
                case "RGB": return new PdfName("DeviceRGB");
                case "CMYK": return new PdfName("DeviceCMYK");
                case "AHx": return new PdfName("ASCIIHexDecode");
                case "A85": return new PdfName("ASCII85Decode");
                case "LZW": return new PdfName("LZWDecode");
                case "Fl": return new PdfName("FlateDecode");
                case "RL": return new PdfName("RunLengthDecode");
                case "CCF": return new PdfName("CCITTFaxDecode");
                case "DCT": return new PdfName("DCTDecode");
            }

            return itm;
        }

        internal static string ContractBIKeyName(string name)
        {
            switch (name)
            {
                case "BitsPerComponent": return "BPC";
                case "ColorSpace": return "CS";
                case "Decode": return "D";
                case "DecodeParms": return "DP";
                case "Filter": return "F";
                case "Height": return "H";
                case "ImageMask": return "IM";
                case "Interpolate": return "I";
                case "Width": return "W";
            }

            return name;
        }

        internal static string ExpandBIKeyName(string name)
        {
            switch (name)
            {
                case "BPC": return "BitsPerComponent";
                case "CS": return "ColorSpace";
                case "D": return "Decode";
                case "DP": return "DecodeParms";
                case "F": return "Filter";
                case "H": return "Height";
                case "IM": return "ImageMask";
                case "I": return "Interpolate";
                case "W": return "Width";
            }

            return name;
        }

#endregion

#region LookupTable functions

        internal int[][] CreateIntDecodeTable()
        {
            return CreateIntDLT(Decode, 0, BitsPerComponent, ColorSpace.NComponents);
        }

        /// <summary>
        /// Creates a lookup table for decode values
        /// </summary>
        /// <remarks>
        /// Note that this function clips values. Use CreateDLT if that's undesired.
        /// </remarks>
        internal static int[][] CreateIntDLT(xRange[] decode, int off, int bpc, int ncomps)
        {
            // Calculates the maximum raw color value a pixel can have
            int max_value = (1 << bpc) - 1;

            //Precalcs values used for interpolation.
            // mins holds the "decode[min]"s
            // ranges holds "(decode[max] - decode[min]) / max_value"
            var mins = new int[ncomps];
            var ranges = new double[ncomps];
            for (int c = off, i = 0; c < decode.Length; c++, i++)
            {
                var min = (int)Img.Internal.LinearInterpolator.Interpolate(decode[c].Min, 0, 1, 0, max_value);
                var max = (int)Img.Internal.LinearInterpolator.Interpolate(decode[c].Max, 0, 1, 0, max_value);
                ranges[i] = (max - min) / (double) max_value;
                mins[c] = min;
            }

            //The lookup table
            var lt = new int[ncomps][];

            //Itterates through the lookup table, calcing the color value for each 
            //possible color.
            for (int c = 0; c < ncomps; c++)
            {
                var t = new int[max_value + 1];
                lt[c] = t;
                var min = mins[c]; var range = ranges[c];
                for (int i = 0; i <= max_value; i++)
                {
                    var val = (int)(i * range) + min;

                    //Clips the value
                    if (val < 0) val = 0;
                    else if (val > max_value) val = max_value;
                    
                    t[i] = val;
                }
            }

            return lt;
        }

        /// <summary>
        /// Creates a decode lookup table
        /// </summary>
        /// <param name="decode">Decode array to base this LUT on</param>
        /// <param name="off">Offset into that array</param>
        /// <param name="bpc">Bits per component</param>
        /// <param name="ncomps">Number of components</param>
        /// <returns>Decode Lookup Table</returns>
        internal static double[,] CreateDLT(xRange[] decode, int off, int bpc, int ncomps)
        {
            // Calculates the maximum raw color value a pixel can have
            int max_value = (1 << bpc) - 1;

            //  As far as I can see the decode is applied before the color lookup
            //  I.e. rawvalue -> decode -> colorlookup

            //Interploation formula is:
            // (x - x_min) * (y_max - y_min) / (x_max - x_min) + y_min;
            //
            //Where: x_max = max_value, y_max = decode[max]
            //       x_min = 0         y_min = decode[min]
            //
            //So: (x - 0) * (decode[max] - decode[min]) / (max_value - 0) + decode[min];
            //becomes: x * "range" / max_value + decode[min]

            //Precalcs values used for interpolation.
            // mins holds the "decode[min]"s
            // ranges holds "(decode[max] - decode[min]) / max_value"
            var mins = new double[ncomps];
            var ranges = new double[ncomps];
            for (int c = off, i = 0; c < decode.Length; c++, i++ )
            {
                var min = decode[c].Min;
                mins[i] = min;
                ranges[i] = (decode[c].Max - min) / max_value;
            }

            //The lookup table
            var lt = new double[ncomps, max_value + 1];

            //Itterates through the lookup table, calcing the color value for each 
            //possible color.
            for (int c = 0; c < ncomps; c++)
            {
                var min = mins[c]; var range = ranges[c];
                for (int i = 0; i <= max_value; i++)
                    lt[c, i] = i * range + min;
            }

            return lt;
        }

        /// <summary>
        /// Creates a decode lookup table
        /// </summary>
        /// <param name="decode">Decode to base this LUT on</param>
        /// <param name="bpc">Bits per component</param>
        /// <returns>Decode Lookup Table</returns>
        internal static double[] CreateDLT(xRange decode, int bpc)
        {
            // Calculates the maximum raw color value a pixel can have
            int max_value = (1 << bpc) - 1;

            //Precalcs values used for interpolation.
            double min = decode.Min;
            double range = (decode.Max - min) / max_value;

            //The lookup table
            var lt = new double[max_value + 1];

            //Itterates through the lookup table, calcing the color value for each 
            //possible color.
            for (int i = 0; i <= max_value; i++)
                lt[i] = i * range + min;

            return lt;
        }

#endregion

#region Create image function

        public static PdfImage Create(string path)
        {
            using (var file = System.IO.File.OpenRead(path))
            {
                var image = Create(file);
                //(image.Stream as IWStream).LoadResources();
                return image;
            }
        }

        /// <summary>
        /// Create an image from a raw image file, if you wish
        /// to create an image from raw pixel data use the
        /// PdfImage constructor instead.
        /// </summary>
        /// <param name="data">The raw image data</param>
        /// <returns>The PdfImage</returns>
        public static PdfImage Create(byte[] data, bool ignore_gamma = false)
        {
            return Create(new System.IO.MemoryStream(data), false, ignore_gamma);
        }

        /// <summary>
        /// Create an image from a raw image file, if you wish
        /// to create an image from raw pixel data use the
        /// PdfImage constructor instead.
        /// </summary>
        /// <param name="data">The raw image data, beware that you may have to keep the stream open for jp2 and jpeg files</param>
        /// <param name="load_data_into_memory">When true, loads jp2 and jpeg data into memory</param>
        /// <returns>The PdfImage</returns>
        public static PdfImage Create(System.IO.Stream data, bool load_data_into_memory = true, bool ignore_gamma = false)
        {
            var ii = new ImageInfo(data);
            data.Position = 0;
            switch (ii.Format)
            {
                case Img.ImageInfo.FORMAT.JP2:
                case Img.ImageInfo.FORMAT.J2K:
                {
                    var image = CreateFromJPXData(data, null, null); /* Needs the stream to stay open. */
                    if (load_data_into_memory)
                        (image.Stream as IWStream).LoadResources();
                    return image;
                }

                case Img.ImageInfo.FORMAT.JPEG:
                {
                    var image = CreateFromJPGData(data, null); /* Needs the stream to stay open. */
                    if (load_data_into_memory)
                        (image.Stream as IWStream).LoadResources();
                    return image;
                }

                case Img.ImageInfo.FORMAT.PGM:
                case Img.ImageInfo.FORMAT.PPM:
                    return CreateFromPPMData(data, null, null/*, true*/);

                case Img.ImageInfo.FORMAT.PNG:
                    return CreateFromPngData(PngImage.Open(data), ignore_gamma ? 0 : (double?) null);

                case Img.ImageInfo.FORMAT.BIG_TIF:
                case Img.ImageInfo.FORMAT.TIF:
                    return CreateFromTiffData(data);

                case Img.ImageInfo.FORMAT.BMP_OS2:
                case Img.ImageInfo.FORMAT.BMP:
                    return CreateFromBMPData(Img.Bmp.Open(data));

                default:
                    throw new NotSupportedException(ii.FormatExtension);
            }
        }

        /// <summary>
        /// Creates an image from bgra32 data
        /// </summary>
        /// <param name="raw_data"></param>
        /// <param name="height"></param>
        /// <param name="stride"></param>
        /// <returns></returns>
        public static PdfImage CreateFromBGRA32(byte[] raw_data, int height, int width, int stride, bool include_alpha)
        {
            byte[] rgb = new byte[height * width * 3];
            byte[] alpha = new byte[height * width];
            int rgb_stride = width * 3;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                int pos_a = y * width, pos_rgb = y * rgb_stride;
                for (int x = 0, rgb_x = 0; x < stride; x += 4, rgb_x += 3)
                {
                    rgb[pos_rgb + rgb_x + 2] = raw_data[row + x];
                    rgb[pos_rgb + rgb_x + 1] = raw_data[row + x + 1];
                    rgb[pos_rgb + rgb_x + 0] = raw_data[row + x + 2];
                    alpha[pos_a + x/4] = raw_data[row + x + 3];
                }
            }

            var image = new PdfImage(rgb, DeviceRGB.Instance, width, height, 8);
            if (include_alpha)
                image.SMask = new PdfImage(alpha, DeviceGray.Instance, width, height, 8);
            return image;
        }

        /// <summary>
        /// Creates an image from bgra32 data
        /// </summary>
        /// <param name="raw_data">Raw byte values</param>
        /// <param name="height">Height of the image</param>
        /// <param name="width">Width og image</param>
        /// <param name="stride">Number of bytes per row</param>
        /// <param name="include_alpha">If the alpha is to be included as an SMask</param>
        /// <returns>PdfImage</returns>
        public static PdfImage CreateFromPremulRGBA32(byte[] raw_data, int height, int width, int stride, bool include_alpha)
        {
            byte[] rgb = new byte[height * width * 3];
            byte[] alpha = new byte[height * width];
            int rgb_stride = width * 3;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                int pos_a = y * width, pos_rgb = y * rgb_stride;
                for (int x = 0, rgb_x = 0; x < stride; x += 4, rgb_x += 3)
                {
                    byte ba = raw_data[row + x + 3];
                    alpha[pos_a + x / 4] = ba;
                    var a = (float) ba;

                    rgb[pos_rgb + rgb_x + 0] = (byte) (raw_data[row + x] / a * 255);
                    rgb[pos_rgb + rgb_x + 1] = (byte) (raw_data[row + x + 1] / a * 255);
                    rgb[pos_rgb + rgb_x + 1] = (byte) (raw_data[row + x + 2] / a * 255);
                }
            }

            var image = new PdfImage(rgb, DeviceRGB.Instance, width, height, 8);
            if (include_alpha)
                image.SMask = new PdfImage(alpha, DeviceGray.Instance, width, height, 8);
            return image;
        }

        /// <summary>
        /// Converts the first image in a tiff file to PDF
        /// </summary>
        /// <param name="stream">Raw data</param>
        /// <returns>First image</returns>
        public static PdfImage CreateFromTiffData(System.IO.Stream stream)
        {
            using (var ts = Img.Tiff.TiffReader.Open(stream, false, false))
            {
                foreach (var image in ts)
                    return (PdfImage)image.ConvertToXObject(true);
            }
            throw new IndexOutOfRangeException();
        }

        public static PdfImage CreateFromTGAData(TGA tga)
        {
            IColorSpace cs = tga.HasColor ? DeviceRGB.Instance : (IColorSpace) DeviceGray.Instance;
            bool argb_order = false; byte[] px = tga.Pixels;
            if (tga.BitsPerComponent == 5 && cs.NComponents == 3)
            {
                if ((px.Length % 2) != 0) throw new NotSupportedException("Uneven number of bytes in a 16 bpp image");

                //For some reason this is a byteswapped ARGB 1555 format
                byte[] swapped = new byte[px.Length];
                for(int c=0; c < px.Length; )
                {
                    swapped[c++] = px[c];
                    swapped[c] = px[c++ - 1];
                }
                px = swapped;
                argb_order = true;
            }
            var pal = tga.Palette;
            if (pal != null)
                return CreateFromRawData(pal, tga.BitsPerPixel, tga.BitsPerComponent, px, cs, tga.Width, tga.Height, !tga.HasAlpha);
            else
                return CreateFromRawData(tga.BitsPerPixel, tga.BitsPerComponent, px, cs, tga.Width, tga.Height, tga.HasAlpha, !tga.HasAlpha, argb_order);
        }

#if CELESTE
        internal static PdfImage CreateFromCelesteData(Celeste cel)
        {
            IColorSpace cs = DeviceRGB.Instance;
            return CreateFromRawData(cel.BitsPerPixel, cel.BitsPerComponent, cel.Pixels, cs, cel.Width, cel.Height, cel.HasAlpha, !cel.HasAlpha, false);
        }
#endif

        public static PdfImage CreateFromIFFData(IFF iff)
        {
            if (iff.CMap != null)
            {
                var pdf_cmap = new IndexedCS(iff.CMap);
                var img = new PdfImage(iff.Body, pdf_cmap, iff.Width, iff.Height, iff.BitsPerPixel);
                if (iff.AlphaMask != null)
                    img.Mask = new PdfImage(iff.AlphaMask, iff.Width, iff.Height);
                return img;
            }
            if (iff.BitsPerPixel == 24) 
                return new PdfImage(iff.Body, DeviceRGB.Instance, iff.Width, iff.Height, 8);
            if (iff.BitsPerPixel == 12)
                return new PdfImage(iff.Body, DeviceRGB.Instance, iff.Width, iff.Height, 4);

            throw new NotImplementedException();
        }

        public static PdfImage CreateFromBMPData(Bmp bmp)
        {
            var pixels = bmp.GetPixels();
            if (pixels is Bmp.ChunkyPixels cp)
                return CreateFromBMPData(cp);
            if (pixels is Bmp.IndexedPixels ip)
                return CreateFromBMPData(ip);
            if (pixels is Bmp.Planes pl)
            {
                var jpx_comps = new ImageComp[pl.Components.Length];
                for (int c = 0; c < pl.Components.Length; c++)
                    jpx_comps[c] = new ImageComp(pl.Components[c].bpc, pl.Components[c].bpc, false, 1, 1, bmp.Width, bmp.Height, pl.Components[c].Subpixels);
                var jpx = new JPXImage(0, pl.Width, 0, pl.Height, jpx_comps, COLOR_SPACE.sRGB);
                var bytes = JPX.Compress(jpx, false, 100);
                return CreateFromJPXData(bytes, null, null);
            }
            if (pixels is Bmp.ChunkyWithAlphaPlane cpa)
            {
                var img = CreateFromBMPData(cpa.Colors);
                int bpc = cpa.Alpha.bpc;
                int stride_8 = ((bpc + 7) / 8) * cpa.Width;
                var alpha = new byte[stride_8 * cpa.Height];
                var bw = new BitWriter(alpha);
                var d = cpa.Alpha.Subpixels;

                for(int c=0, x = 0; c < d.Length; c++)
                {
                    bw.Write(d[c], bpc);
                    if (x++ == cpa.Width)
                    {
                        x = 0;
                        bw.Align();
                    }

                }

                bw.Flush();

                if (cpa.Alpha.bpc == 1)
                    img.Mask = new PdfImage(alpha, cpa.Width, cpa.Height);
                else
                    img.SMask = new PdfImage(alpha, DeviceGray.Instance, cpa.Width, cpa.Height, bpc);

                return img;
            }

            throw new NotImplementedException();
        }
        public static PdfImage CreateFromBMPData(Bmp.IndexedPixels bmp, bool compatability = true)
        {
            //Note, method checks for Alpha itself, so setting has_alpha to false.
            return CreateFromRawData(bmp.Palette, bmp.BitsPerPixels, bmp.Palette.BPC, bmp.RawPixels, bmp.Palette.HasColor ? (IColorSpace) DeviceRGB.Instance : DeviceGray.Instance, bmp.Width, bmp.Height, false, compatability);
        }
        public static PdfImage CreateFromBMPData(Bmp.ChunkyPixels bmp, bool compatability = true)
        {
            return CreateFromRawData(bmp.BitsPerPixels, bmp.BitsPerComponent, bmp.RawPixels, 
                bmp.Order != Bmp.ColorMode.Gray ? (IColorSpace)DeviceRGB.Instance : DeviceGray.Instance, 
                bmp.Width, bmp.Height, bmp.Alpha != Bmp.ColorModeAlpha.None, false, false, compatability);
        }

        private static PdfImage CreateFromRawData(int bpp, int bpc, byte[] pixels, IColorSpace cs, int width, int height, bool has_alpha, bool ignore_alpha, bool argb_order, bool compatability = true)
        {
            int[] pdf_ck = null; PdfImage smask = null;

            if (cs.NComponents == 1)
            {
                if (has_alpha)
                {
                    byte[] mask_data = new byte[(bpc * width + 7) / 8 * height];
                    byte[] gray_data = new byte[mask_data.Length];
                    var br = new Util.BitStream(pixels);
                    Util.BitWriter bw_mask = new Util.BitWriter(mask_data), bw_gray = new Util.BitWriter(gray_data);
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            bw_gray.Write(br.FetchBits(bpc), bpc);
                            bw_mask.Write(br.FetchBits(bpc), bpc);
                        }
                        br.ByteAlign();
                        bw_mask.Align();
                        bw_gray.Align();
                    }

                    pixels = gray_data;
                    if (!Util.ArrayHelper.Same(mask_data, 0xff))
                        smask = new PdfImage(mask_data, DeviceGray.Instance, width, height, bpc);
                }
            }
            else if (cs.NComponents == 3)
            {
                if (has_alpha || bpp == 32)
                {
                    //Assumes (BGRA)
                    int alpha_bpc = 3 * bpc; alpha_bpc = alpha_bpc / 8 + (alpha_bpc % 8 != 0 ? 1 : 0); alpha_bpc = alpha_bpc * 8 - 3 * bpc;
                    if (alpha_bpc == 0) alpha_bpc = bpc;
                    byte[] mask_data = new byte[(alpha_bpc * width + 7) / 8 * height];
                    byte[] rgb_data = new byte[(bpc * 3 * width + 7) / 8 * height];
                    var br = new Util.BitStream(pixels);
                    Util.BitWriter bw_mask = new Util.BitWriter(mask_data), bw_rgb = new Util.BitWriter(rgb_data);

                    if (argb_order)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                bw_mask.Write(br.FetchBits(alpha_bpc), alpha_bpc);

                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                            }

                            bw_mask.Align();
                            bw_rgb.Align();
                        }
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int b = br.FetchBits(bpc), g = br.FetchBits(bpc), r = br.FetchBits(bpc);
                                bw_rgb.Write(r, bpc);
                                bw_rgb.Write(g, bpc);
                                bw_rgb.Write(b, bpc);
                                bw_mask.Write(br.FetchBits(bpc), bpc);
                            }
                            br.ByteAlign();
                            bw_mask.Align();
                            bw_rgb.Align();
                        }
                    }

                    pixels = rgb_data;
                    if (!ArrayHelper.Same(mask_data, 0xff))
                        smask = new PdfImage(mask_data, DeviceGray.Instance, width, height, alpha_bpc);
                }
                else
                {
                    byte[] rgb_data = new byte[(bpc * 3 * width + 7) / 8 * height];
                    var br = new BitStream(pixels);
                    BitWriter bw_rgb = new BitWriter(rgb_data);

                    if (argb_order)
                    {
                        int clear = 3 * bpc; clear = clear / 8 + (clear % 8 != 0 ? 1 : 0); clear = clear * 8 - 3 * bpc;
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                br.Skip(clear);

                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                                bw_rgb.Write(br.FetchBits(bpc), bpc);
                            }

                            bw_rgb.Align();
                        }
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int b = br.FetchBits(bpc), g = br.FetchBits(bpc), r = br.FetchBits(bpc);
                                bw_rgb.Write(r, bpc);
                                bw_rgb.Write(g, bpc);
                                bw_rgb.Write(b, bpc);

                                br.ByteAlign();
                            }

                            bw_rgb.Align();
                        }
                    }

                    pixels = rgb_data;
                }
            }
            else
            {
                throw new NotImplementedException();
            }

            var img = new PdfImage(pixels, cs, width, height, bpc);

            //This is done for compatibility with PDF viewers that do not like 555 images.
            if (bpc == 5 && compatability) img = ChangeBPC(img, 8, false);

            if (pdf_ck != null)
                img.Mask = pdf_ck;
            if (smask != null && has_alpha && !ignore_alpha)
            {
                if (smask.BitsPerComponent == 5 && compatability)
                    smask = ChangeBPC(smask, 8, false);
                img.SMask = smask;
            }
            return img;
        }

        /// <summary>
        /// Creates an image from a byte array where every pixel is byte aligned. 
        /// 
        /// You probably want to use the PdfImage constructor instead of this function.
        /// </summary>
        private static PdfImage CreateFromRawData(Bmp.CLUT pal, int bpp, int bpc, byte[] pixels, IColorSpace cs, int width, int height, bool ignore_alpha, bool compatability = true)
        {
            int[] pdf_ck = null; PdfImage smask = null;


            byte[] pal_bytes = new byte[pal.NColors * cs.NComponents];
            int pos = 0;

            //PDF files requires 8bpc for indexed color space. So we scale the values to the 8bpc space
            int max_value = (1 << bpc) - 1;
            double mul = 255d / max_value;

            if (cs.NComponents == 3)
            {
                foreach (IntColor col in pal)
                {
                    uint cc = col.Color;
                    pal_bytes[pos++] = (byte)Math.Round(((cc >> 16) & 0xFF) * mul);
                    pal_bytes[pos++] = (byte)Math.Round(((cc >> 8) & 0xFF) * mul);
                    pal_bytes[pos++] = (byte)Math.Round((cc & 0xFF) * mul);
                }
            }
            else
            {
                foreach (IntColor col in pal)
                {
                    uint cc = col.Color;
                    pal_bytes[pos++] = (byte)Math.Round((cc & 0xFF) * mul);
                }
            }
            cs = new IndexedCS(pal_bytes, cs);

            var alpha = pal.AlphaColors;
            bool has_alpha = false;
            bool has_opague = false;
            for (int c = 0; c < alpha.Length; c++)
            {
                var a = alpha[c];
                if (a.Alpha > 0)
                    has_opague = true;
                if (a.Alpha < 1)
                    has_alpha = true;
            }
            if (!ignore_alpha && has_opague && has_alpha)
            {
                if (alpha.Length == 1 && alpha[0].Alpha == 0)
                {
                    //This can be emulated with a color key mask,
                    int col_pos = 0;
                    foreach (IntColor col in pal)
                    {
                        if (col.Alpha == 0)
                            break;
                        col_pos++;
                    }
                    pdf_ck = new int[] { col_pos, col_pos };
                }
                else
                {
                    //Creates a soft mask
                    byte[] mask_data = new byte[width * height];
                    var br = new Util.BitStream(pixels);

                    //Fills out the mask
                    for (int c = 0; c < mask_data.Length;)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            var index = br.FetchBits(bpp);
                            mask_data[c++] = (byte)(pal[index].Color >> 24);
                        }
                        br.ByteAlign();
                    }

                    smask = new PdfImage(mask_data, DeviceGray.Instance, width, height, 8);
                }
            }

            // In PDF documents, the Bits per component referes to the amount of data for each pixel. For indexed that
            // will probably be 8bpp.
            bpc = bpp;
            

            var img = new PdfImage(pixels, cs, width, height, bpc);

            //This is done for compatibility with PDF viewers that do not like 555 images.
            //if (bpc == 5) img = ChangeBPC(img, 8, false);

            if (pdf_ck != null)
                img.Mask = pdf_ck;
            if (smask != null && has_alpha && !ignore_alpha)
            {
                if (smask.BitsPerComponent == 5 && compatability)
                    smask = ChangeBPC(smask, 8, false);
                img.SMask = smask;
            }
            return img;
        }

        /// <summary>
        /// Creates a PdfImage from a PNG file. To remain PNG compressed, the file must not 
        /// have alpha, nor can it be interlaced.
        /// 
        /// This because PDF support for alpha colors in images is very different from
        /// how PNG supports it. For alpha, you need to decode the image and reformat the
        /// data.
        /// 
        /// Same for interlaced. Image must be decoded and reformated.
        /// 
        /// (Images will automatically be PNG compressed again, but the compressor is not
        ///  particulary good.)
        ///   
        /// Note, indexed PNG files with alpha is not implemented. (@see the NotImplementedException)
        /// </summary>
        /// <param name="stream">Raw PNG image</param>
        /// <param name="gamma">
        /// Desired gamma value. 
        /// 
        /// Set to null and gamma inside file with be used.
        /// Set to 0 and gamma will be ignored.
        /// Set to a number and it will be used instead of file gamma
        /// 
        /// File gamma is always returned.
        /// </param>
        /// <returns>A PNG formated PDF image</returns>
        /// <remarks>
        /// Implemented from: http://www.w3.org/TR/PNG-Chunks.html
        /// </remarks>
        public static PdfImage CreateFromPngData(PngImage png, double? gamma)
        {
            //Reads the header signature
            if (!png.Valid)
                throw new PdfLib.PdfNotSupportedException("Can't open invalid PNG file");
            PdfImage img, mask = null;
            double? set_gamma;
            if (gamma == null)
                set_gamma = png.Gamma;
            else if (gamma.Value == 0)
                set_gamma = null;
            else
                set_gamma = gamma;
            //gamma = png.Gamma;

            IColorSpace cs;
            if (png.HasColorProfile)
            {
                cs = new ICCBased(png.HasColor ? DeviceRGB.Instance : (PdfColorSpace) DeviceGray.Instance, png.CompressedICC, true);
            }
            else if (png.HasColor)
            {
                //CalRGB is not right for gamma, as it assumes sRGB color space
                cs = set_gamma == null ? DeviceRGB.Instance : (IColorSpace)new CalRGBCS(1 / set_gamma.Value);
            }
            else
            {
                cs = set_gamma == null ? DeviceGray.Instance : (IColorSpace)new CalGrayCS(1/set_gamma.Value);
            }
            int[] pdf_ck = null;

            if (png.HasAlpha)
            {
                if (!png.Indexed)
                {
                    var raw = png.ExtractAlpha();
                    if (raw == null)
                        throw new PdfNotSupportedException("Can't open invalid PNG file");

                    var color_img = new PdfImage(raw.Data, (IColorSpace)cs, png.Width, png.Height, png.BitsPerComponent);
                    try
                    {
                        var new_img = CreateFromPngData(PngConverter.Convert(color_img), null);
                        color_img = new_img.Stream.Length < color_img.Stream.Length ? new_img : color_img;
                    }
                    catch { /* Bug in PNG compression */ }
                    color_img.SMask = new PdfImage(raw.Alpha, DeviceGray.Instance, png.Width, png.Height, png.BitsPerComponent);
                    try
                    {
                        var new_img = CreateFromPngData(PngConverter.Convert(color_img.SMask), null);
                        if (new_img.Stream.Length < color_img.SMask.Stream.Length)
                            color_img.SMask = new_img;
                    }
                    catch { /* Bug in PNG compression */ }
                    return color_img;
                }
                //AFAICT indexed images with alpha does not have the HasAlpha flag set
            }
            else
            {
                var ck = png.ColorKeys;
                if (ck != null)
                {
                    int ncomps = png.NComponents;
                    if (ck.Length == ncomps && png.BitsPerComponent != 16)
                    {
                        pdf_ck = new int[ncomps * 2];
                        for (int c = 0, pos = 0; c < ck.Length; c++)
                            pdf_ck[pos++] = pdf_ck[pos++] = ck[c];

                        //Workaround for bug in Adobe:
                        //Color keys only seems to work on indexed and 8-bit images. It's possible to
                        //make color key work on sub 8-bit grayscale, but that breaks in other viewers
                        //
                        //So we convert the color space to a palette
                        if (png.BitsPerComponent < 8 && !png.Indexed)
                        {
                            //Note: Bitsize 1, 2, 4 is only allowed on grayscale and indexed.
                            int n_colors = (int) Math.Pow(2, png.BitsPerComponent);
                            var colors = new byte[n_colors];
                            int inc = 255 / (n_colors - 1);
                            for (int c = 0, pos = 0; c < colors.Length; c++)
                                colors[pos++] = (byte)(c * inc);
                            cs = new IndexedCS(colors, cs);
                        }
                    }
                    else
                    {
                        //Creates a 1-bit image mask.
                        //
                        //PDF does not support multiple color keys, so we emulate it by using a 1-bit image mask.

                        //Fetches image data and splits it into sepperate components
                        var fa_s = new FilterArray(new PdfFlateFilter());
                        var fp_s = new FlateParams(Predictor.PNG_Opt, cs.NComponents, png.BitsPerComponent, png.Width);
                        var ss = new WritableStream(png.RawData, fa_s, new FilterParmsArray(new PdfItem[] { fp_s }, fa_s));
                        img = new PdfImage(ss, cs, null, png.Width, png.Height, png.BitsPerComponent, 0);
                        var pixels = img.PixelComponents;

                        //Splits up the color keys into arrays, where each array represent one color
                        int n_comps = png.NComponents, n_colors = ck.Length / n_comps;
                        int[][] cks = new int[n_colors][];
                        for (int c = 0, pos = 0; c < n_colors; c++)
                        {
                            var ar = new int[ncomps];
                            for (int k = 0; k < ar.Length; k++)
                                ar[k] = ck[pos++];
                            cks[c] = ar;
                        }

                        //Creates the mask
                        int mask_stride = (png.Width + 7) / 8;
                        byte[] mask_data = new byte[mask_stride * png.Height];
                        var bw = new Util.BitWriter(mask_data);
                        int[] color = new int[n_comps];
                        for (int row = 0, pos = 0; row < png.Height; row++)
                        {
                            for (int col = 0; col < png.Width; col++)
                            {
                                bool transparent = false;

                                //Fetches a color
                                for (int j = 0; j < color.Length; j++)
                                    color[j] = pixels[pos++];

                                //Looks through all the color keys
                                for (int j = 0; j < cks.Length; j++)
                                {
                                    //If a color matches a key, it's set transparent.
                                    if (Util.ArrayHelper.ArraysEqual<int>(color, cks[j]))
                                    {
                                        transparent = true;
                                        break;
                                    }
                                }
                                bw.WriteBit(transparent ? 0 : 1);
                            }

                            //Start of rows must align to byte boundaries.
                            bw.Align();
                        }
                        //Not needed due to bw.Align();
                        //bw.Flush();

                        mask = new PdfImage(mask_data, png.Width, png.Height);
                    }
                }

                var pal = png.AlphaPalette;
                if (pal != null && png.Indexed)
                {
                    //PDF does not support alpha colors in the palette. So we simulate it using a soft mask
                    if (pal.Length == 1 && pal[0] == 0)
                    {
                        //In this common special case, we can use a color key
                        pdf_ck = new int[2];
                    }
                    else
                    {
                        //Fetches image data and splits it into sepperate components
                        var fa_s = new FilterArray(new PdfFlateFilter());
                        var fp_s = new FlateParams(Predictor.PNG_Opt, 1, png.BitsPerComponent, png.Width);
                        var ss = new WritableStream(png.RawData, fa_s, new FilterParmsArray(new PdfItem[] { fp_s }, fa_s));
                        img = new PdfImage(ss, DeviceGray.Instance, null, png.Width, png.Height, png.BitsPerComponent, 0);
                        var pixels = img.PixelComponents;

                        //Creates the mask
                        byte[] mask_data = new byte[png.Width * png.Height];

                        //Fills out the mask
                        for (int c = 0; c < mask_data.Length; c++)
                        {
                            var index = pixels[c];
                            mask_data[c] = (index < pal.Length) ? pal[index] : (byte)255;
                        }

                        mask = new PdfImage(mask_data, DeviceGray.Instance, png.Width, png.Height, 8);
                    }
                }
            }

            if (png.Interlaced)
            {
                var raw = png.Decode();
                if (png.Indexed)
                {
                    cs = new IndexedCS(png.RawPaletteData);

                    if (png.HasAlpha)
                    {
                        throw new NotImplementedException("Opening indexed PNG files with alpha");
                    }
                }

                img = new PdfImage(raw, cs, png.Width, png.Height, png.BitsPerComponent);
                if (pdf_ck != null)
                    img.Mask = pdf_ck;
                else if (mask != null)
                {
                    if (mask.ImageMask)
                        img.Mask = mask;
                    else
                        img.SMask = mask;
                }
                return img;
            }

            var fa = new FilterArray(new PdfFlateFilter());
            if (png.Indexed)
            {
                if (png.HasAlpha)
                {
                    //Data must first be decompressed. Then the alpha channel must be sepperated
                    //from the color data.
                    throw new NotImplementedException("Opening indexed PNG files with alpha");
                    //Note: Alpha in PNG files is either stored in the image data or the tRNS chunk
                }

                //The PDF palette is defined to be one byte per color, ranging from 0 to 255. This is
                //the same as for PNG images. 
                var clut = png.RawPaletteData;

                //Note: It is possible to reduce the palette size when:
                // 1. Image is grayscale
                // 2. All enteries in the palette has the same values for R, G, B
                //(But this isn't done)
                
                cs = (set_gamma == null) ? DeviceRGB.Instance : (IColorSpace) new CalRGBCS(1 / set_gamma.Value);
                    
                //Checks if the pallete can be shrunk
                bool differs = false;
                for (int len = clut.Length / 3, c = 0; len > 0; len--)
                {
                    byte r = clut[c++];
                    byte g = clut[c++];
                    byte b = clut[c++];
                    if (r != g || g != b)
                    {
                        differs = true;
                        break;
                    }
                }
                if (!differs)
                {
                    cs = (set_gamma == null) ?  DeviceGray.Instance : (IColorSpace) new CalGrayCS(1 / set_gamma.Value);
                    var new_clut = new byte[clut.Length / 3];
                    for (int c = 0, len = 0; c < new_clut.Length; c++, len += 3)
                    {
                        new_clut[c] = clut[len];
                    }
                    clut = new_clut;
                }
                
                cs = new IndexedCS(clut, cs);
            }

            var fp = new FlateParams(Predictor.PNG_Opt, cs.NComponents, png.BitsPerComponent, png.Width);
            var data = new WritableStream(png.RawData, fa, new FilterParmsArray(new PdfItem[] { fp }, fa));

            img = new PdfImage(data, cs, null, png.Width, png.Height, png.BitsPerComponent, 0);
            if (pdf_ck != null)
                img.Mask = pdf_ck;
            else if (mask != null)
            {
                if (mask.ImageMask)
                    img.Mask = mask;
                else
                    img.SMask = mask;
            }
            return img;
        }

        /// <summary>
        /// Creates an image from PPM or PGM data
        /// </summary>
        /// <param name="stream">A readable stream</param>
        /// <param name="decode">Optional decode array</param>
        /// <param name="color_space">Optional color space</param>
        /// <returns>A PdfImage</returns>
        public static PdfImage CreateFromPPMData(System.IO.Stream stream, xRange[] decode, IColorSpace color_space/*, bool big_endian*/)
        {
            var tr = new Util.StreamTokenizer(stream);
            var header = tr.ReadToken();

            if (header == "P7")
                return CreateFromPAMData(tr, stream, decode, color_space);

            if (header != "P6" && header != "P5") throw new PdfNotSupportedException("Wrong header marker in PPM file");

            if (color_space == null)
            {
                if (header == "P6")
                {
                    //By the specs;
                    //http://netpbm.sourceforge.net/doc/ppm.html
                    //http://books.google.no/books?id=ra1lcAwgvq4C&pg=PA251&lpg=PA251&dq=RGB+to+XYZ+D65+709&source=bl&ots=bPkaFDUQ2c&sig=NB2R2CQ5veMxzCH2Za6r3fATBx0&hl=en&sa=X&ei=cVyVUcegKOSR4ASDqYCoCw&redir_esc=y#v=onepage&q=RGB%20to%20XYZ%20D65%20709&f=false
                    //color_space = new CalRGBCS(new LabPoint(0.9505, 1, 1.089), new LabPoint(2.2, 2.2, 2.2),
                    //                            new x3x3Matrix(0.412453, 0.357580, 0.180423,
                    //                                           0.212671, 0.715160, 0.072169,
                    //                                           0.019334, 0.119193, 0.950227));

                    //By the real world
                    color_space = DeviceRGB.Instance;
                }
                else
                {
                    color_space = new CalGrayCS(new LabPoint(0.9505, 1, 1.089), 2.2);
                }
            }

            //Removes decode if it's not needed
            if (decode != null)
            {
                bool default_decode = xRange.Compare(decode, xRange.Create(color_space.DefaultDecode));
                if (default_decode)
                    decode = null;
            }

            int width = ReadNum(tr);
            int height = ReadNum(tr);
            int maxval = ReadNum(tr);
            int BitsPerComp = 0;
            int n_comps = color_space.NComponents;

            for (int i = 0; i < 25; i++)
            {
                if (maxval < (1 << (i + 1)))
                {
                    BitsPerComp = i + 1;
                    break;
                }
            }

            if (BitsPerComp > 16)
            {
                //This is the max the format supports.
                BitsPerComp = 16;
            }

            if (width <= 0 || height <= 0 || BitsPerComp <= 0)
                throw new PdfNotSupportedException("Invalid PPM header");

            //The stream now sits on the right position for a write

            int stride = (width * (BitsPerComp * 3) + 7) / 8;
            var image_data = new System.IO.MemoryStream(stride * height);
            var bw = new BitWriter(image_data);
            int bits_to_write = Math.Min(8, BitsPerComp);
            //var bytes = new int[(BitsPerComp + 7) / 8];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int k = 0; k < n_comps; k++)
                    {
                        for (int c = 0/*, i=0*/; c < BitsPerComp; c += 8)
                        {
                            var one_byte = stream.ReadByte();
                            if (one_byte == -1)
                                throw new PdfReadException(ErrSource.XObject, PdfType.None, ErrCode.UnexpectedEOD);
                            bw.Write(one_byte, Math.Min(bits_to_write, BitsPerComp - c));
                            //bytes[i++] = one_byte;
                        }
                        /*if (big_endian)
                        {
                            for (int c = 0; c < bytes.Length; c++)
                                bw.Write(bytes[c], bits_to_write);
                        }
                        else
                        {
                            for (int c = bytes.Length - 1; c >= 0; c--)
                                bw.Write(bytes[c], bits_to_write);
                        }*/
                    }
                }
                bw.Align();
            }
            //Not needed due to bw.Align();
            //bw.Dispose();

            image_data.Position = 0;

            var ws = new WrMemStream(image_data, null);
            return new PdfImage(ws, color_space, decode, width, height, BitsPerComp, 0);
        }

        /// <remarks>
        /// v7: https://netpbm.sourceforge.net/doc/pam.html
        /// </remarks>
        private static PdfImage CreateFromPAMData(StreamTokenizer tr, System.IO.Stream inn, xRange[] decode, IColorSpace color_space/*, bool big_endian*/)
        {
            int width = 0, height = 0, depth = 0, max_val = 0;
            int has_alpha = 0;
            string token;


            //Reads the first token on a line, which line dosn't matter
            while ((token = tr.ReadToken()) != null)
            {
                switch(token)
                {
                    case "WIDTH":
                        if (width != 0 || !int.TryParse(tr.ReadLineToken(), out width) || width < 1 || !SkipPAMLine(tr))
                            throw new PdfNotSupportedException("Invalid PPM header");
                        continue;
                    case "HEIGHT":
                        if (height != 0 || !int.TryParse(tr.ReadLineToken(), out height) || height < 1 || !SkipPAMLine(tr))
                            throw new PdfNotSupportedException("Invalid PPM header");
                        continue;
                    case "DEPTH":
                        if (depth != 0 || !int.TryParse(tr.ReadLineToken(), out depth) || depth < 1 || !SkipPAMLine(tr))
                            throw new PdfNotSupportedException("Invalid PPM header");
                        continue;
                    case "MAXVAL":
                        if (max_val != 0 || !int.TryParse(tr.ReadLineToken(), out max_val) || max_val < 1 || max_val > 65535 || !SkipPAMLine(tr))
                            throw new PdfNotSupportedException("Invalid PPM header");
                        continue;
                    case "TUPLTYPE":
                        token = tr.ReadLineToken();
                        if (token == null || token.Length == 0 || !SkipPAMLine(tr))
                            throw new PdfNotSupportedException("Invalid PPM header");
                        if (color_space == null)
                        {
                            var tokens = token.Split('_');
                            switch(tokens[0])
                            {
                                case "BLACKANDWHITE":
                                case "GRAYSCALE":
                                    color_space = DeviceGray.Instance;
                                    break;
                                case "RGB":
                                    color_space = DeviceRGB.Instance;
                                    break;
                            }

                            if (tokens.Length > 1)
                            {
                                if (tokens[1] != "ALPHA" || tokens.Length > 2)
                                    color_space = null;
                                else
                                    has_alpha = 1;
                            }
                        }
                        continue;
                    case "ENDHDR":
                        if (width == 0 || height == 0 || depth == 0 || max_val == 0 || depth > 4)
                            throw new PdfNotSupportedException("Invalid PPM header");

                        if (color_space == null)
                        {
                            switch(depth - has_alpha)
                            {
                                case 1: color_space = DeviceGray.Instance; break;
                                case 3: color_space = DeviceRGB.Instance; break;
                                default: throw new PdfNotSupportedException("Invalid PPM header");
                            }
                        }

                        //Removes decode if it's not needed
                        if (decode != null)
                        {
                            bool default_decode = xRange.Compare(decode, xRange.Create(color_space.DefaultDecode));
                            if (default_decode)
                                decode = null;
                        }

                        int bpc = (int)Math.Log(max_val, 2) + 1;
                        int n_bytes = bpc > 8 ? 2 : 1;
                        depth -= has_alpha;

                        byte[] color_data = new byte[((width * depth * bpc + 7) / 8) * height], alpha_data = null;
                        BitWriter bw = new BitWriter(color_data), bwa = null;
                        byte[] num = new byte[n_bytes];


                        if (has_alpha != 0)
                        {
                            alpha_data = new byte[((width * 1 * bpc + 7) / 8) * height];
                            bwa = new BitWriter(alpha_data);
                        }

                        for(int y=0; y < height; y++)
                        {
                            for(int x=0; x < width; x++)
                            {
                                for (int comp = 0; comp < depth; comp++)
                                {
                                    if (inn.Read(num, 0, n_bytes) != n_bytes)
                                        throw new PdfReadException(PdfType.None, ErrCode.UnexpectedEOD);
                                    int val = num[0];
                                    if (n_bytes == 2)
                                        val = val << 8 | num[1];
                                    bw.Write(val, bpc);
                                }

                                if (bwa != null)
                                {
                                    if (inn.Read(num, 0, n_bytes) != n_bytes)
                                        throw new PdfReadException(PdfType.None, ErrCode.UnexpectedEOD);
                                    int val = num[0];
                                    if (n_bytes == 2)
                                        val = val << 8 | num[1];
                                    bwa.Write(val, bpc);
                                }
                            }

                            bw.Align();

                            if (bwa != null)
                                bwa.Align();
                        }

                        //There's no need to flush, as Align also flushes.
                        //bw.Flush();
                        //if (bwa != null)
                        //    bwa.Flush();

                        var img = new PdfImage(color_data, color_space, width, height, bpc) { Decode = decode };
                        if (alpha_data != null)
                            img.SMask = new PdfImage(alpha_data, DeviceGray.Instance, width, height, bpc) { Decode = decode };

                        return img;
                }

                //Skips comments and unknown header enteries
                tr.SkipLine();
            }

            //Header ended too soon.
            throw new PdfNotSupportedException("Invalid PPM header");
        }

        private static bool SkipPAMLine(StreamTokenizer tr)
        {
            var token = tr.ReadLineToken();
            if (token != null && token.Length > 0)
            {
                if (token[0] != '#')
                    return false;
                tr.SkipLine();
            }

            return true;
        }

        private static int ReadNum(StreamTokenizer stream)
        {
            var token = ReadToken(stream);
            if (token == null) throw new PdfReadException(ErrSource.XObject, PdfType.Integer, ErrCode.UnexpectedEOD);
            return int.Parse(token);
        }

        private static string ReadToken(StreamTokenizer stream)
        {
            while (true)
            {
                string token = stream.ReadToken();
                if (token == null) return null;
                if (token[0] == '#')
                {
                    if (!stream.SepWasLineFeed)
                        stream.SkipLine();
                    continue;
                }
                return token;
            }
        }

        /// <summary>
        /// Turns an image gray.
        /// </summary>
        /// <param name="img">Image to grayscale</param>
        /// <returns>Grayscaled image</returns>
        public static PdfImage GrayScale(PdfImage img)
        {
            var jpx = JPX.PdfImageToJPX(img, JPX.PDFtoJP2Options.APPLY_DECODE);
            jpx = JPX.Grayscale(jpx);
            var bytes = jpx.ToArray();
            var td = img._elems.TempClone();
            td.Remove("Filter");
            td.SetItem("ColorSpace", DeviceGray.Instance, false);
            return new PdfImage(td, bytes);
        }

        /// <summary>
        /// Turns an image gray.
        /// </summary>
        /// <param name="img">Image to grayscale</param>
        /// <param name="ithreshold">Trehold values. Use null or -2 for auto, -1 for none</param>
        /// <returns>Grayscaled image</returns>
        public static PdfImage Threshold(PdfImage img, params double[] threshold)
        {
            var jpx = JPX.PdfImageToJPX(img, JPX.PDFtoJP2Options.APPLY_DECODE);
            if (!jpx.UniformSize)
                throw new NotImplementedException("Tresholding non-uniform images");
            var comps = jpx.Components;
            var ithreshold = new int[comps.Length];
            if (threshold != null)
            {
                if (threshold.Length != ithreshold.Length)
                {
                    Array.Resize<double>(ref threshold, ithreshold.Length);
                    Util.ArrayHelper.Fill(threshold, threshold[0]);
                }

                for (int c = 0; c < ithreshold.Length; c++)
                {
                    var num = threshold[c];
                    if (num >= 0)
                        ithreshold[c] = (int)(((1 << comps[c].Prec) - 1) * num);
                    else if (num == -2)
                        ithreshold[c] = JPX.OtsuThreshold(comps[c]);
                    else
                        ithreshold[c] = (int)num;
                }
            }
            else
            {
                for (int c = 0; c < ithreshold.Length; c++)
                    ithreshold[c] = JPX.OtsuThreshold(comps[c]);
            }

            var length = comps[0].Data.Length;
            for (int pos = 0; pos < length; pos++)
            {
                bool zero = true;
                for (int compno = 0; compno < comps.Length; compno++)
                {
                    if (comps[compno].Data[pos] > ithreshold[compno])
                    {
                        zero = false;
                        break;
                    }
                }
                if (zero)
                {
                    for (int compno = 0; compno < comps.Length; compno++)
                        comps[compno].Data[pos] = 0;
                }
            }
            var bytes = jpx.ToArray();
            var td = img._elems.TempClone();
            td.Remove("Filter");
            return new PdfImage(td, bytes);
        }

        /// <summary>
        /// Changes the width/height of an image, but not the width/height of the mask or smask of that image.
        /// Will likely work badly on indexed images.
        /// </summary>
        /// <param name="img">Image to change</param>
        /// <param name="new_width">Width of the new image</param>
        /// <param name="new_height">Height of the new image.</param>
        /// <returns>A new image with the given size</returns>
        public static PdfImage ChangeSize(PdfImage img, int new_width, int new_height)
        {
            int w = img.Width, h = img.Height;
            if (w == new_width && h == new_height)
                return img;
            if (w <= 0 || h <= 0)
                throw new ArgumentException();

            var comps = img.SepPixelComponents;
            var ic = new ImageComp[comps.Length];
            for(int c=0; c < comps.Length; c++)
            {
                var ints = Scaler.Rezise(comps[c], w, h, new_width, new_height);
                ic[c] = new ImageComp(img.BitsPerComponent, img.BitsPerComponent, false, 1, 1, new_width, new_height, ints);
            }

            var jpx = new JPXImage(0, new_width, 0, new_height, ic, COLOR_SPACE.UNKNOWN);
            var bytes = jpx.ToArray();
            var td = img._elems.TempClone();
            td.SetInt("Width", new_width);
            td.SetInt("Height", new_height);
            td.Remove("Filter");
            return new PdfImage(td, bytes);
        }

        /// <summary>
        /// Compresses image with CCITT compression, but only if it becomes smaller
        /// </summary>
        /// <param name="img">Image to compress</param>
        /// <param name="try_g3">If G3 compression is to be attempted</param>
        /// <returns>1BPP image that is either G4/G3 compressed or uncompressed</returns>
        public static PdfImage CCITTCompress(PdfImage img, bool try_g3)
        {
            //Should create some sort of JPEG2000 to non-compliant PdfImage, so that
            //one don't need speical case code for this stuff.
            if (img.Format == ImageFormat.JPEG2000)
                throw new NotImplementedException();

            if (img.BitsPerComponent != 1 || img.ColorSpace.NComponents != 1)
                img = MakeBILevel(img, true, -2);
            //IndexedCS with 1bpp goes through, but that does not matter

            if (img.Format == ImageFormat.CCITT)
                return img;
                        
            var data = img.Stream.DecodedStream;
            var white_data = Img.CCITTEncoder.EncodeG4(data, img.Width, img.Height, false, false, false);
            var black_data = Img.CCITTEncoder.EncodeG4(data, img.Width, img.Height, true, false, false);
            bool white;
            byte[] dest_data;
            if (white_data.Length <= black_data.Length)
            {
                white = true;
                dest_data = white_data;
            }
            else
            {
                white = false;
                dest_data = black_data;
            }

            PdfFaxParms param = new PdfFaxParms(-1, img.Width, img.Height, false, false, false, !white, null);
            if (try_g3)
            {
                white_data = Img.CCITTEncoder.EncodeG31D(data, img.Width, img.Height, false, false);
                black_data = Img.CCITTEncoder.EncodeG31D(data, img.Width, img.Height, true, false);
                bool g3_white;
                byte[] g3_data;
                if (white_data.Length <= black_data.Length)
                {
                    g3_white = true;
                    g3_data = white_data;
                }
                else
                {
                    g3_white = false;
                    g3_data = black_data;
                }

                if (g3_data.Length <= dest_data.Length)
                {
                    dest_data = g3_data;
                    param = new PdfFaxParms(0, img.Width, img.Height, false, false, false, !g3_white, null);
                }
            }
            if (dest_data.Length >= data.Length)
                return img;

            var temp = img._elems.TempClone();
            var fa = new FilterArray(new PdfFaxFilter());
            temp.SetItem("Filter", fa, true);
            temp.SetItem("DecodeParms", new FilterParmsArray(fa, param), true);

            var wr = new WritableStream(dest_data, temp);
            return new PdfImage(wr);
        }

        /// <summary>
        /// Makes an image into a 1bpp image
        /// </summary>
        /// <param name="img">Image to convert. Will be grayscaled first</param>
        /// <param name="dither">Whenever to dither or not</param>
        /// <param name="threshold">Threshold for B/W conversion (0 to 1), use -2 for auto</param>
        /// <returns>BW image</returns>
        public static PdfImage MakeBILevel(PdfImage img, bool dither, double threshold)
        {
            var jpx = JPX.PdfImageToJPX(img, JPX.PDFtoJP2Options.APPLY_DECODE);

            int bpc = jpx.MaxBPC;
            if (bpc == 1 && jpx.NumberOfOpagueComponents == 1)
                return img;
            int max = (1 << bpc) - 1;
            int ithreshold = (int)(threshold >= 0 ? (max * threshold) : threshold);

            int width = jpx.Width, height = jpx.Height;
            if (dither)
            {
                var diffuser = new AtkinsonDitheringDithering(width, height, img.BitsPerComponent);
                
                if (img.ColorSpace == DeviceGray.Instance)
                {
                    if (ithreshold == -2)
                        ithreshold = JPX.OtsuThreshold(jpx.Components[0]);

                    var jpx_comp = jpx.Components[0];
                    var comp = jpx_comp.Data;
                    for (int y = 0, pos = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++, pos++)
                        {
                            int org_pixel = comp[pos];
                            int new_pixel = org_pixel > ithreshold ? max : 0;
                            comp[pos] = new_pixel;
                            diffuser.Dither(comp, org_pixel, new_pixel, x, y);
                        }
                    }
                }
                else
                {
                    jpx = JPX.MakeRGB(jpx);
                    var comp = new int[jpx.Components.Length][];
                    int[] pixel = new int[comp.Length];
                    int[] thresholds = new int[comp.Length];
                    ArrayHelper.Fill(thresholds, ithreshold);
                    for (int c = 0; c < thresholds.Length; c++)
                    {
                        comp[c] = jpx.Components[c].Data;
                        if (thresholds[c] == -2)
                            thresholds[c] = JPX.OtsuThreshold(jpx.Components[c]);
                    }
                    if (ithreshold == -2)
                        ithreshold = (int) (0.299 * thresholds[0] + 0.587 * thresholds[1] + 0.114 * thresholds[2]);

                    for (int y = 0, pos = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++, pos++)
                        {
                            for (int c = 0; c < pixel.Length; c++)
                                pixel[c] = comp[c][pos];
                            int new_pixel = (int) (0.299 * pixel[0] + 0.587 * pixel[1] + 0.114 * pixel[2]);
                            new_pixel = new_pixel > ithreshold ? max : 0;
                            for (int c = 0; c < comp.Length; c++)
                            {
                                var cmp = comp[c];
                                cmp[pos] = new_pixel;
                                diffuser.Dither(cmp, pixel[c], new_pixel, x, y);
                            }
                        }
                    }
                }
            }
            else
            {
                jpx = JPX.Grayscale(jpx);
                if (ithreshold == -2)
                    ithreshold = JPX.OtsuThreshold(jpx.Components[0]);
                jpx.Components[0].MakeBILevel(ithreshold);
            }
            byte[] ba = Img.IMGTools.PlanarToChunky(new int[][] { jpx.Components[0].Data }, 1, width, height);
            var new_img = img._elems.TempClone();
            new_img.SetInt("BitsPerComponent", 1);
            new_img.Remove("Filter");
            new_img.Remove("Decode");
            new_img.SetItem("ColorSpace", DeviceGray.Instance, false);
            return new PdfImage(new_img, ba);
        }

        /// <summary>
        /// Make an index image out of an image
        /// </summary>
        /// <param name="img">Source image</param>
        /// <returns>A new indexed image, or the same image if it was indexed already</returns>
        public static PdfImage MakeIndexed(PdfImage img, int color_count = 256)
        {
            if (img.ColorSpace is IndexedCS) return img;
            if (color_count > 256)
                throw new PdfNotSupportedException("An indexed image can't have more than 256 colors");
            if (color_count < 2)
                throw new PdfNotSupportedException("Color count must be higher than 1");

            byte[] color_map, idx8;
            if (color_count == 256)
            {
                var bgr24 = img.DecodeImage(new ToBGR24Converter().Convert);

                //NeuQuant is both fast and gives good result.
                var nq = new NeuQuant(bgr24);

                //Creates the color palette the image will use
                color_map = nq.Process();

                //Maps each pixel to the palette
                idx8 = new byte[img.Width * img.Height];
                for (int c = 0, k = 0; c < idx8.Length; c++)
                    idx8[c] = (byte)nq.Map(bgr24[k++], bgr24[k++], bgr24[k++]);
                
            }
            else if(color_count > 2)
            {
                var bgr24 = img.DecodeImage(new ToBGR24Converter().Convert);

                //WU provides excellent result and can work with less than 256 colors
                //but is slower than NeuQuant. In comparison wu is more color accurate,
                //while NeuQant less grainy. 
                var wu = WUQuant.CreateBGR24Quant();
                var res = wu.Quantize(bgr24, color_count);
                color_map = res.Palette;
                idx8 = res.Bytes;
            }
            else
            {
                //While WU can handle 2 color images, it can ultimatly only pick the palette. 
                //One might be able to use that to calculate a better threshold for a BI-level image.
                //In any case we fall back to the old BI-level impl. It does an okay job. 
                return MakeBILevel(img, true, -2);
            }

            var cs = new IndexedCS(color_map);
            return new PdfImage(idx8, cs, img.Width, img.Height, 8);
        }

        /// <summary>
        /// Creates a new PDF image with the bits per component changed into the new range. Does
        /// not change the BPP of of any image masks
        /// </summary>
        /// <param name="img">Image to convert</param>
        /// <param name="new_bpc">New bits per component value</param>
        /// <param name="dither">Whenever to dither the image or not</param>
        /// <param name="interpolate">If the pixels should be scaled to the new value</param>
        /// <returns>A new image with the given bpc</returns>
        public static PdfImage ChangeBPC(PdfImage img, int new_bpc, bool dither, bool interpolate = true)
        {
            int width = img.Width;
            byte[] ba;
            if (dither && new_bpc < img.BitsPerComponent)
            {
                //Palleteralization needs a different approach. 
                if (img.ColorSpace is IndexedCS)
                    throw new NotImplementedException("Dithering an indexed image");
                int height = img.Height;
                var diffuser = new AtkinsonDitheringDithering(width, height, img.BitsPerComponent);
                var comps = img.SepPixelComponents;
                int shift = img.BitsPerComponent - new_bpc, imask = ~((1 << shift) - 1);
                for (int c = 0; c < comps.Length; c++)
                {
                    var comp = comps[c];
                    for (int y = 0, pos = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++, pos++)
                        {
                            int org_pixel = comp[pos];
                            //int new_pixel = (int)Math.Round(ip.Interpolate(org_pixel));
                            //comp[pos] = new_pixel;
                            //new_pixel = new_pixel << shift;
                            int new_pixel = org_pixel & imask;
                            comp[pos] = new_pixel >> shift;
                            diffuser.Dither(comp, org_pixel, new_pixel, x, y);
                        }
                    }
                }

                ba = Img.IMGTools.PlanarToChunky(comps, new_bpc, width, height);
            }
            else if (new_bpc != img.BitsPerComponent)
            {
                var pixels = img.PixelComponents;
                int new_stride = (new_bpc * img.NComponents * width + 7) / 8;
                ba = new byte[new_stride * img.Height];
                var bw = new BitWriter(ba);
                width *= img.NComponents;
                if (interpolate)
                {
                    var ip = new LinearInterpolator(0, Math.Pow(2, img.BitsPerComponent) - 1, 0, Math.Pow(2, new_bpc) - 1);
                    for (int pix = 0; pix < pixels.Length;)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            int color = (int)Math.Round(ip.Interpolate(pixels[pix++]));
                            bw.Write(color, new_bpc);
                        }
                        bw.Align();
                    }
                }
                else
                {
                    for (int pix = 0; pix < pixels.Length;)
                    {
                        for (int i = 0; i < width; i++)
                            bw.Write(pixels[pix++], new_bpc);
                        bw.Align();
                    }
                }
                //Not needed due to bw.Align();
                //bw.Flush();
            }
            else
                return img;
            var new_img = img._elems.TempClone();
            new_img.SetInt("BitsPerComponent", new_bpc);
            new_img.Remove("Filter");
            return new PdfImage(new_img, ba);
        }

        /// <summary>
        /// Creates a PDF image from raw jpeg data
        /// </summary>
        /// <param name="data">the jpeg data</param>
        /// <param name="decode">Optional decode array</param>
        /// <returns>A PDF image</returns>
        public static PdfImage CreateFromJPGData(byte[] data, xRange[] decode)
        {
            return CreateFromJPGData(new System.IO.MemoryStream(data), decode);
        }

        /// <summary>
        /// Creates a PDF image from raw jpeg data
        /// </summary>
        /// <param name="data">the jpeg data (Assumes start is pos=0)</param>
        /// <param name="decode">Optional decode array</param>
        /// <returns>A PDF image</returns>
        /// <remarks>
        /// Issue: Does not exctract the ICC profile
        /// </remarks>
        public static PdfImage CreateFromJPGData(System.IO.Stream data, xRange[] decode)
        {
            var cinfo = new jpeg_decompress_struct();
            cinfo.jpeg_stdio_src(data);
            if (cinfo.jpeg_read_header(true) != ReadResult.JPEG_HEADER_OK)
                throw new PdfReadException(Internal.PdfType.Stream, ErrCode.IsCorrupt);
            int bpc = cinfo.Data_precision;

            //FYI. Libjpg determines the color space through guess work.
            IColorSpace color_space;
            switch (cinfo.Out_color_space)
            {
                case J_COLOR_SPACE.JCS_GRAYSCALE:
                    color_space = DeviceGray.Instance;
                    break;

                case J_COLOR_SPACE.JCS_YCbCr:
                case J_COLOR_SPACE.JCS_RGB:
                    color_space = DeviceRGB.Instance;
                    break;

                case J_COLOR_SPACE.JCS_YCCK:
                case J_COLOR_SPACE.JCS_CMYK:
                    color_space = DeviceCMYK.Instance;
                    break;

                default:
                    color_space = null;
                    break;
            }

            //Creates the DCT stream
            data.Position = 0;
            var ws = new WrMemStream(data, new FilterArray(new PdfItem[] { new PdfDCTFilter() }));

            //Removes decode if it's not needed
            if (decode != null && color_space != null)
            {
                bool default_decode = xRange.Compare(decode, xRange.Create(color_space.DefaultDecode));
                if (default_decode)
                    decode = null;
            }
            else
                decode = null;

            return new PdfImage(ws, color_space, decode, cinfo.Image_width, cinfo.Image_height, bpc, 0);
        }

        /// <summary>
        /// Creates a PdfImage from Jpeg2000 data
        /// </summary>
        /// <param name="data">The raw jpeg 2000 data</param>
        /// <param name="cs">Optional colorspace</param>
        /// <param name="decode">Optional decode, ignored if colorspace is null</param>
        /// <param name="alpha_in_data">Todo: Read this from the header instead of depending on the caller to check</param>
        /// <returns>The PdfImage</returns>
        public static PdfImage CreateFromJPXData(byte[] data, IColorSpace cs, xRange[] decode)
        {
            return CreateFromJPXData(new System.IO.MemoryStream(data), cs, decode);
        }

        /// <summary>
        /// Creates a PdfImage from Jpeg2000 data
        /// </summary>
        /// <param name="data">The raw jpeg 2000 data, do note that the stream will be used so if you close it, the image will not render</param>
        /// <param name="cs">Optional colorspace</param>
        /// <param name="decode">Optional decode, ignored if colorspace is null</param>
        /// <param name="alpha_in_data">Todo: Read this from the header instead of depending on the caller to check</param>
        /// <returns>The PdfImage</returns>
        public static PdfImage CreateFromJPXData(System.IO.Stream data, IColorSpace cs, xRange[] decode)
        {
            //First we check if the data really is what we exepct it to be
            var ii = new ImageInfo(data);
            if (ii.Format != Img.ImageInfo.FORMAT.J2K && ii.Format != Img.ImageInfo.FORMAT.JP2)
                throw new PdfInternalException("Invalid data. Must be in JP2 or J2K format");
            bool alpha_in_data = ii.HasAlpha;

            //Creates the J2K/JP2 stream
            data.Position = 0;
            var ws = new WrMemStream(data, new FilterArray(new PdfItem[] { new PdfJPXFilter() }));

            //alpha_in_data could be deduced by looking at the colorspace of the JP2 file, then at the
            //number of components. Won't work for J2K files, of course, as they don't provide a color space.

            //Removes decode if it's not needed
            if (cs != null && decode != null)
            {
                bool default_decode = xRange.Compare(decode, xRange.Create(cs.DefaultDecode));
                if (default_decode)
                    decode = null;
            }
            else
                decode = null;

            if (cs != null && ii.Format == Img.ImageInfo.FORMAT.JP2 && ii.ColorSpace > Img.ImageInfo.COLORSPACE.UNKNOWN)
            {
                //Removes the color space if it's not needed (i.e. if it's embeded in the JP2).
                if (cs is DeviceRGB && ii.ColorSpace == Img.ImageInfo.COLORSPACE.sRGB ||
                    cs is DeviceGray && ii.ColorSpace == Img.ImageInfo.COLORSPACE.GRAY ||
                    cs is DeviceCMYK && ii.ColorSpace == Img.ImageInfo.COLORSPACE.CMYK)
                    cs = null;
                //JPXLab and PDFLab color spaces aren't quite the same, so they should be left as is.
            }

            return new PdfImage(ws, cs, decode, ii.Width, ii.Height, 0, alpha_in_data ? PdfAlphaInData.Alpha : PdfAlphaInData.None);
        }

#endregion

#region PixelConverters

        /// <summary>
        /// This function is supplied to the IPixelConverter interface by the decodeimage function,
        /// and is used to fetch the next pixel's color data. Format is RGBA
        /// </summary>
        /// <param name="px"></param>
        /// <returns>Alpha, or null if there's no alpha</returns>
        public delegate double? FetchColorData();

        /// <summary>
        /// The function called on the IPixelConverter interface.
        /// </summary>
        /// <param name="px">An array to supply to the fcd function</param>
        /// <param name="has_alpha">If alpha data will be supplied</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="cc">Color converter</param>
        /// <param name="fcd">Fetch color data function</param>
        /// <returns>The finished bytes</returns>
        public delegate byte[] ConvertPixels(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd);

        private static readonly IPixelConverter BGRA32 = new BGRA328888();

        /// <summary>
        /// For converting floating point pixels into bytes
        /// </summary>
        /// <remarks>
        /// Gray conversion is only used internally, and the usage can probably be covered
        /// by a slight revision to this interface.
        /// </remarks>
        public interface IPixelConverter
        {
            /// <summary>
            /// Fetch color data width * height times from fcd. Run the doubles through the color converter
            /// and convert into bytes.
            /// </summary>
            /// <param name="px">An array to supply to the fcd function</param>
            /// <param name="has_alpha">If alpha data will be supplied</param>
            /// <param name="width">Width of the image</param>
            /// <param name="height">Height of the image</param>
            /// <param name="cc">Color converter</param>
            /// <param name="fcd">Fetch color data function</param>
            /// <returns>The finished bytes</returns>
            byte[] Convert(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd);
        }

        /// <summary>
        /// Converts image to an RGB 24-bit format. 
        /// </summary>
        public class ToRGB24Converter : IPixelConverter
        {
            public byte[] Convert(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd)
            {
                int size = width * height;
                byte[] bytes = new byte[size * 3];

                for (int c = 0, j = 0; c < size; c++)
                {
                    fcd();

                    var col = cc.MakeColor(px);

                    //Converts the color to rgb24. This step
                    //reduces precision to 8BPP
                    bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                    bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                    bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                }

                return bytes;
            }
        }

        /// <summary>
        /// Converts image to an BGR 24-bit format. 
        /// </summary>
        public class ToBGR24Converter : IPixelConverter
        {
            public byte[] Convert(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd)
            {
                int size = width * height;
                byte[] bytes = new byte[size * 3];

                for (int c = 0, j = 0; c < size; c++)
                {
                    fcd();

                    var col = cc.MakeColor(px);

                    //Converts the color to rgb24. This step
                    //reduces precision to 8BPP
                    bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                    bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                    bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                }

                return bytes;
            }
        }

        internal class GRAY8 : IPixelConverter
        {
            public byte[] Convert(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd)
            {
                int stride = (int)(Math.Ceiling(width / 4d) * 4);
                /* For unaligned, simply set stride to width */
                var bytes = new byte[height * stride];
                //int size = width * height;

                //Reads out one pixel at a time and converts it to Gray8
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int j = 0; j < width;)
                    {
                        fcd();

                        //Converts the pixel to a RGB color
                        var col = cc.MakeGrayColor(px);

                        //Converts the color to Gray8. This step
                        //reduces precision to 8BPP
                        bytes[row + j++] = Clip((int)(col.Gray * byte.MaxValue));
                    }
                }

                return bytes;
            }
        }

        private class BGRA328888 : IPixelConverter
        {
            public byte[] Convert(double[] px, bool has_alpha, int width, int height, ColorConverter cc, FetchColorData fcd)
            {
                int size = width * height;
                byte[] bytes = new byte[size * 4];

                if (has_alpha)
                {
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        var alpha = fcd();

                        var col = cc.MakeColor(px);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = (byte)(alpha.Value * byte.MaxValue); // Alpha is always 0 -> 1
                    }
                }
                else
                {
                    for (int c = 0, j = 0; c < size; c++)
                    {
                        fcd();

                        var col = cc.MakeColor(px);

                        //Converts the color to bgra32. This step
                        //reduces precision to 8BPP
                        bytes[j++] = Clip((int)(col.B * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.G * byte.MaxValue));
                        bytes[j++] = Clip((int)(col.R * byte.MaxValue));
                        bytes[j++] = byte.MaxValue;
                    }
                }

                return bytes;
            }
        }

#endregion
    }
}
