//#define CLUT
//#define BIG_CLUT //<-- adds more colors to the CLUT table
#define MOZILLA
//#define SPEC_CONV //<-- The algo in the specs.
using System;
using System.Reflection;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Coverts colors from CMYK to RGB
    /// </summary>
    /// <remarks_>
    /// Converting colors from CMYK to RGB isn't entirely trivial. This file
    /// offers three ways of doing it: By spec algo, an alternate algo that
    /// seemingly gives better result and a CLUT lookup algorithm
    /// 
    /// The CLUT algorithm gives the best result, but the code simply
    /// reuse the PdfSampleFunction so it's a bit inefficient as that
    /// function does more than it needs (clipping, conversions and
    /// such)
    /// 
    /// A massive speedup can probably be achived by simply caching
    /// a few values, as images often have long runs of a single color
    /// </remarks_>
    public sealed class DeviceCMYK : PdfColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// Shared instance of this color space
        /// </summary>
        private static DeviceCMYK _device_cmyk;

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public override PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// This colorspace saves itself directly
        /// </summary>
        /// <remarks>
        /// Direct isn't required, but add up the bytes
        /// for the reference, object id and entery in
        /// the XRef table and it's better to simply
        /// save this direct.
        /// </remarks>
        internal override SM DefSaveMode { get { return SM.Direct; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public override PdfColor DefaultColor { get { return new IntColor(0,0,0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public override double[] DefaultColorAr { get { return new double[] { 0, 0, 0, 1 }; } }

        /// <summary>
        /// Gets an instance of this colorspace
        /// </summary>
        public static DeviceCMYK Instance
        {
            get
            {
                if (_device_cmyk == null)
                    _device_cmyk = new DeviceCMYK();
                return _device_cmyk;
            }
        }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public override int NComponents { get { return 4; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public override double[] DefaultDecode
        {
            get { return new double[] { 0, 1, 0, 1, 0, 1, 0, 1 }; }
        }

        #endregion

        #region Init

        private DeviceCMYK() { }

        #endregion

        #region Overrides

        public override ColorConverter Converter
        {
            get { return CMYKConverter.Instance; }
        }

        internal override void Write(Write.Internal.PdfWriter write)
        {
            write.WriteName("DeviceCMYK");
        }

        public override string ToString()
        {
            return "/DeviceCMYK";
        }

        #endregion

        internal sealed class CMYKConverter : ColorConverter
        {
            static CMYKConverter _instance;
            internal static CMYKConverter Instance 
            { get { if (_instance == null) _instance = new CMYKConverter(); return _instance; } }


#if CLUT
            /// <remarks>
            /// Using this function is inefficient. Being lazy, I'm using the
            /// PdfSampleFunction as is. This means when initing, for instance, 
            /// the sample table will be split up into three smaller tables 
            /// (instead of simply having them that way from the start).
            /// 
            /// Then the samples will also be converted into doubles
            /// whenever they are used, so they could just have been
            /// doubles instead of bytes, ++ input values will be 
            /// clipped and inerpolated needlesly.
            /// 
            /// So there's good room for optimilization,
            /// 
            /// To futher increase performance, consider:
            /// http://osl.iu.edu/~tveldhui/papers/MAScThesis/node33.html
            /// (Piecewise linear interpolation)
            /// </remarks>
            static PdfSampleFunction _sampler;
            static FCalcualte _calculator;
            static double[] rgb;

            /// <summary>
            /// Constructor that creates the sample function
            /// </summary>
            private CMYKConverter()
            {
                if (_sampler == null)
                {
                    _sampler = CreateClutFunc();
                    _calculator = _sampler.Init();
                    rgb = new double[3];

                    FieldInfo fi = typeof(PdfSampleFunction).GetField("_elems", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                        fi.SetValue(_sampler, null);
                }
            }

            internal static PdfSampleFunction CreateClutFunc()
            {
                //Creates a sample table with four inputs, and three outputs
                //This table has the minium amount of samples, more samples
                //will improve the result without hurting computational cost
                byte[] samples =
                {
//A bigger sample table to see if there's any noticable improvment. To my eyes, it's a no.
#if BIG_CLUT
                    // Sample CMYK 000,000,000,000
                    255, //Output R 
                    255, //Output G
                    255, //Output B

                    // Sample CMYK 255,000,000,000
                    000, //Output R
                    173, //Output G
                    238, //Output B

                    // Sample CMYK 000,255,000,000
                    236, //Output R
                    000, //Output G
                    141, //Output B

                    // Sample CMYK 255,255,000,000
                    048, //Output R
                    049, //Output G
                    146, //Output B

                    // Sample CMYK 000,000,255,000
                    255, //Output R
                    241, //Output G
                    000, //Output B

                    // Sample CMYK 255,000,255,000
                    000, //Output R
                    166, //Output G
                    081, //Output B

                    // Sample CMYK 000,255,255,000
                    238, //Output R
                    028, //Output G
                    037, //Output B

                    // Sample CMYK 255,255,255,000
                    054, //Output R
                    054, //Output G
                    056, //Output B

                    // Sample CMYK 000,000,000,127
                    148, //Output R 
                    149, //Output G
                    153, //Output B

                    // Sample CMYK 255,000,000,127
                    000, //Output R
                    105, //Output G
                    145, //Output B

                    // Sample CMYK 000,255,000,127
                    140, //Output R
                    000, //Output G
                    082, //Output B

                    // Sample CMYK 255,255,000,127
                    021, //Output R
                    009, //Output G
                    089, //Output B

                    // Sample CMYK 000,000,255,127
                    150, //Output R
                    141, //Output G
                    000, //Output B

                    // Sample CMYK 255,000,255,127
                    000, //Output R
                    101, //Output G
                    046, //Output B

                    // Sample CMYK 000,255,255,127
                    140, //Output R
                    004, //Output G
                    004, //Output B

                    // Sample CMYK 255,255,255,127
                    023, //Output R
                    020, //Output G
                    024, //Output B

                    // Sample CMYK 000,000,000,255
                    035, //Output R
                    031, //Output G
                    032, //Output B

                    // Sample CMYK 255,000,000,255
                    000, //Output R
                    015, //Output G
                    036, //Output B

                    // Sample CMYK 000,255,000,255
                    037, //Output R
                    000, //Output G
                    000, //Output B

                    // Sample CMYK 255,255,000,255
                    000, //Output R
                    000, //Output G
                    001, //Output B

                    // Sample CMYK 000,000,255,255
                    028, //Output R
                    025, //Output G
                    000, //Output B

                    // Sample CMYK 255,000,255,255
                    000, //Output R
                    019, //Output G
                    000, //Output B

                    // Sample CMYK 000,255,255,255
                    035, //Output R
                    000, //Output G
                    000, //Output B

                    // Sample CMYK 255,255,255,255
                    000, //Output R
                    000, //Output G
                    000, //Output B
#else
                    // Sample CMYK 000,000,000,000
                    255, //Output R 
                    255, //Output G
                    255, //Output B

                    // Sample CMYK 256,000,000,000
                    000, //Output R
                    172, //Output G
                    239, //Output B

                    // Sample CMYK 000,256,000,000
                    236, //Output R
                    000, //Output G
                    139, //Output B

                    // Sample CMYK 256,256,000,000
                    046, //Output R
                    049, //Output G
                    145, //Output B

                    // Sample CMYK 000,000,256,000
                    255, //Output R
                    241, //Output G
                    000, //Output B

                    // Sample CMYK 256,000,256,000
                    000, //Output R
                    166, //Output G
                    079, //Output B

                    // Sample CMYK 000,256,256,000
                    236, //Output R
                    027, //Output G
                    036, //Output B

                    // Sample CMYK 256,256,256,000
                    054, //Output R
                    054, //Output G
                    056, //Output B

                    // Sample CMYK 000,000,000,256
                    035, //Output R
                    031, //Output G
                    032, //Output B

                    // Sample CMYK 256,000,000,256
                    000, //Output R
                    014, //Output G
                    036, //Output B

                    // Sample CMYK 000,256,000,256
                    036, //Output R
                    000, //Output G
                    000, //Output B

                    // Sample CMYK 256,256,000,256
                    000, //Output R
                    000, //Output G
                    001, //Output B

                    // Sample CMYK 000,000,256,256
                    027, //Output R
                    026, //Output G
                    000, //Output B

                    // Sample CMYK 256,000,256,256
                    000, //Output R
                    018, //Output G
                    000, //Output B

                    // Sample CMYK 000,256,256,256
                    033, //Output R
                    000, //Output G
                    000, //Output B

                    // Sample CMYK 256,256,256,256
                    000, //Output R
                    000, //Output G
                    000, //Output B
#endif
                };

                //Remeber, if you increase the number of, say, "black/K" samples,
                //inc the size variable for black, say new int[] { 2, 2, 2, 3 } and
                //insert samples for XXX.XXX.XXX.128 in the middle of the table
                var sf = new PdfSampleFunction(
#if BIG_CLUT
                    new int[] { 2, 2, 2, 3 }, //n Samples in each input dimension (C, M, Y, K)
#else
                    new int[] { 2, 2, 2, 2 }, //n Samples in each input dimension (C, M, Y, K)
#endif
                    xRange.Create(new double[] { 0, 1, 0, 1, 0, 1, 0, 1 }),
                    null,
                    xRange.Create(new double[] { 0, 1, 0, 1, 0, 1 }), 
                    8, samples);

                return sf;
            }
#else
            private CMYKConverter() { }
#endif

            /// <summary>
            /// Converts from CMYK to RGB
            /// </summary>
            public override DblColor MakeColor(double[] comps)
            {
#if CLUT
                _calculator.Calculate(comps, rgb);
                return new DblColor(rgb[0], rgb[1], rgb[2]);
#else

#if SPEC_CONV
                //This is what the specs say, though it isn't how Adobe does it
                double c = comps[0], m = comps[1], y = comps[2], k = comps[3];
                double red = 1 - Math.Min(1d, c + k);
                double green = 1 - Math.Min(1d, m + k);
                double blue = 1 - Math.Min(1d, y + k);

                return new DblColor(red, green, blue);
#else
                //Not to the specs, but gives better results.
                double C = comps[0], M = comps[1], Y = comps[2], K = comps[3];
#if MOZILLA
                /* Copyright 2012 Mozilla Foundation
                 *
                 * Licensed under the Apache License, Version 2.0 (the "License");
                 * you may not use this file except in compliance with the License.
                 * You may obtain a copy of the License at
                 *
                 *     http://www.apache.org/licenses/LICENSE-2.0
                 *
                 * Unless required by applicable law or agreed to in writing, software
                 * distributed under the License is distributed on an "AS IS" BASIS,
                 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
                 * See the License for the specific language governing permissions and
                 * limitations under the License.
                 * 
                 * https://github.com/mozilla/pdf.js/blob/master/src/core/colorspace.js
                 */
                double r = 1 + (
                  C *
                    (-4.387332384609988 * C +
                      54.48615194189176 * M +
                      18.82290502165302 * Y +
                      212.25662451639585 * K +
                      -285.2331026137004) +
                  M *
                    (1.7149763477362134 * M -
                      5.6096736904047315 * Y +
                      -17.873870861415444 * K -
                      5.497006427196366) +
                  Y *
                    (-2.5217340131683033 * Y - 21.248923337353073 * K + 17.5119270841813) +
                  K * (-21.86122147463605 * K - 189.48180835922747)) / 255;

                double g =
                  1 + (
                  C *
                    (8.841041422036149 * C +
                      60.118027045597366 * M +
                      6.871425592049007 * Y +
                      31.159100130055922 * K +
                      -79.2970844816548) +
                  M *
                    (-15.310361306967817 * M +
                      17.575251261109482 * Y +
                      131.35250912493976 * K -
                      190.9453302588951) +
                  Y * (4.444339102852739 * Y + 9.8632861493405 * K - 24.86741582555878) +
                  K * (-20.737325471181034 * K - 187.80453709719578)) / 255;

                double b =
                  1 +
                  (C *
                    (0.8842522430003296 * C +
                      8.078677503112928 * M +
                      30.89978309703729 * Y -
                      0.23883238689178934 * K +
                      -14.183576799673286) +
                  M *
                    (10.49593273432072 * M +
                      63.02378494754052 * Y +
                      50.606957656360734 * K -
                      112.23884253719248) +
                  Y *
                    (0.03296041114873217 * Y +
                      115.60384449646641 * K +
                      -193.58209356861505) +
                  K * (-22.33816807309886 * K - 180.12613974708367)) / 255;

#else
                double r = Range(1 - (C * (1 - K) + K));
                double g = Range(1 - (M * (1 - K) + K));
                double b = Range(1 - (Y * (1 - K) + K));
#endif
                return new DblColor(r, g, b);
#endif
#endif
            }

            /// <summary>
            /// Converts from RGB to CMYK
            /// </summary>
            /// <remarks>
            /// This simple conversion could naturally be improved,
            /// but in my testing it gives descent enough result. 
            /// </remarks>
            public override double[] MakeColor(PdfColor col)
            {
                //Converts to CMY
                double cyan = 1 - col.Red;
                double magenta = 1 - col.Green;
                double yellow = 1 - col.Blue;
                double black = Math.Min(Math.Min(cyan, magenta), yellow);

                if (black == 1.0) //<-- Prevents NaN
                    return new double[] { 0, 0, 0, 1};
                else
                {
                    //Converts to CMYK
                    double rblack = 1 - black;
                    return new double[] {(cyan - black) / rblack, (magenta - black) / rblack, (yellow - black) / rblack, black };
                }
            }

            private double Range(double val)
            { return (val < 0) ? 0d : (1 < val) ? 1d : val; }
        }
    }
}
