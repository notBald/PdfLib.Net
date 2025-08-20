using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Util;
using System;
using System.Collections.Generic;
using System.IO;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public sealed class PdfGouraudShading : PdfStreamShading
    {
        #region Variables and properties

        public PdfTriangle[] Triangles => new TriangleReader(this, _stream.DecodedStream).Read();

        #endregion

        #region init

        internal PdfGouraudShading(IWStream stream)
            : base(stream) { }

        private PdfGouraudShading(PdfDictionary dict, IStream stream)
            : base(dict, stream) { }

        public PdfGouraudShading(PdfTriangle[] triangles, int bits_per_coordinate, IColorSpace cs,
            PdfFunctionArray functions = null, int bits_per_component = 8)
            : base(new WritableStream(), cs, functions, bits_per_coordinate, bits_per_component, 4)
        {
            var ws = (WritableStream)_stream;
            var d = ws.Elements;

            if (triangles == null || triangles.Length == 0)
                throw new ArgumentException("No triangles");

            xRect bounds = triangles[0].Bounds;
            for (int c = 1; c < triangles.Length; c++)
                bounds = bounds.Enlarge(triangles[c].Bounds);

            //Not required, so maybe leave it out? Though, it might be convinient for the calle 
            //to have it.
            d.SetItem("BBox", new PdfRectangle(bounds), false);

            double[] def;
            if (cs is IndexedCS)
            {
                if (functions != null)
                    throw new PdfNotSupportedException("Function with indexed color space");
                def = new double[] { 0, (1 << bits_per_component) - 1 };
            }
            else if (cs is PatternCS)
                throw new PdfNotSupportedException("Pattern with coon or tensor shading");
            else
                def = functions != null ? new double[] { 0, 1 } : cs.DefaultDecode;

            xRange[] decode = new xRange[2 + def.Length / 2];
            decode[0] = new xRange(bounds.Left, bounds.Right);
            decode[1] = new xRange(bounds.Bottom, bounds.Top);

            for (int c = 2, k = 0; c < decode.Length; c++)
                decode[c] = new xRange(def[k++], def[k++]);
            d.SetItem("Decode", new RealArray(xRange.ToArray(decode)), false);

            var tw = new TriangleWriter(bits_per_coordinate, bits_per_component, decode, bounds);
            ws.RawStream = tw.WriteTriangles(triangles);
        }

        #endregion

        #region Boilerplate

        protected override PdfStreamShading MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfGouraudShading(dict, stream);
        }

        #endregion

        private sealed class TriangleReader : CoordReader
        {
            public TriangleReader(PdfGouraudShading p, byte[] data)
                : base(p, data)
            { }

            public PdfTriangle[] Read()
            {
                //The flag signals what edges the mesh shares with the previous mesh
                int flag;
                var triangles = new List<PdfTriangle>(50);

                PdfTriangle prev_trinagle = null;

                //Reads as many triangles as it can. If a corrput triangle is
                //encountered, it stops reading without error.
                while (br.HasBits(bpf))
                {
                    flag = (int)(br.GetBitsU(bpf) & 0x03);

                    try
                    {
                        var trig = new PdfTriangle(ncomps);

                        xPoint new_point = ReadCoord();
                        double[] new_color = new double[ncomps];

                        if (flag == 0 || prev_trinagle == null)
                        {
                            trig.VA = new_point;
                            ReadColor(trig.C_VA);

                            br.ByteAlign();
                            br.FetchBits(bpf);

                            trig.VB = ReadCoord();
                            ReadColor(trig.C_VB);

                            br.ByteAlign();
                            br.FetchBits(bpf);

                            trig.VC = ReadCoord();
                            ReadColor(trig.C_VC);
                        }
                        else if (flag == 1)
                        {
                            trig.VA = prev_trinagle.VB;
                            for (int c = 0; c < ncomps; c++)
                                trig.C_VA[c] = prev_trinagle.C_VB[c];

                            trig.VB = prev_trinagle.VC;
                            for (int c = 0; c < ncomps; c++)
                                trig.C_VB[c] = prev_trinagle.C_VC[c];

                            trig.VC = new_point;
                            ReadColor(trig.C_VC);
                        }
                        else if (flag == 2)
                        {
                            trig.VA = prev_trinagle.VA;
                            for (int c = 0; c < ncomps; c++)
                                trig.C_VA[c] = prev_trinagle.C_VA[c];

                            trig.VB = prev_trinagle.VC;
                            for (int c = 0; c < ncomps; c++)
                                trig.C_VB[c] = prev_trinagle.C_VC[c];

                            trig.VC = new_point;
                            ReadColor(trig.C_VC);
                        }
                        else
                        {
                            //Unkown flag 3
                            break;
                        }

                        triangles.Add(trig);
                        prev_trinagle = trig;

                        br.ByteAlign();
                    } 
                    catch (EndOfStreamException) { break; }
                }

                return triangles.ToArray();
            }
        }

        private sealed class TriangleWriter : CoordWriter
        {
            const int BITS_PER_FLAG = 2;

            public TriangleWriter(int bits_per_coord, int bits_per_color, xRange[] decode, xRect bounds)
                : base(bits_per_coord, bits_per_color, decode, bounds,
                      ((BITS_PER_FLAG + 2 * (bits_per_coord) + (decode.Length - 2) * bits_per_color + 7) / 8) * 3 * 8)
            { }

            public byte[] WriteTriangles(IEnumerable<PdfTriangle> triangles)
            {
                byte[] all_bytes = new byte[0];
                int pos = 0;

                PdfTriangle previous = null;

                foreach (var t in triangles)
                {
                    _bw.Position = 0;

                    int flag = 0;
                    if (previous != null && t.VB == previous.VC)
                    {
                        if (t.VA == previous.VB)
                        {
                            if (ArrayHelper.ArraysEqual(t.C_VA, previous.C_VB) &&
                                ArrayHelper.ArraysEqual(t.C_VB, previous.C_VC))
                                flag = 1;
                        }
                        else if(t.VA == previous.VA)
                        {
                            if (ArrayHelper.ArraysEqual(t.C_VA, previous.C_VA) &&
                                ArrayHelper.ArraysEqual(t.C_VB, previous.C_VC))
                                flag = 2;
                        }
                    }

                    _bw.Write(flag, BITS_PER_FLAG);
                    if (flag == 0)
                    {
                        WriteCoord(t.VA);
                        WriteColor(t.C_VA);
                        _bw.Flush();
                        _bw.Write(0, BITS_PER_FLAG);
                        WriteCoord(t.VB);
                        WriteColor(t.C_VB);
                        _bw.Flush();
                        _bw.Write(0, BITS_PER_FLAG);
                    }                       
                        
                    WriteCoord(t.VC);
                    WriteColor(t.C_VC);

                    _bw.Flush();

                    var bytes_written = (int)_bw.Position;
                    Array.Resize(ref all_bytes, all_bytes.Length + bytes_written);
                    Buffer.BlockCopy(_data, 0, all_bytes, pos, bytes_written);
                    pos += bytes_written;
                    previous = t;
                }

                return all_bytes;
            }
        }
    }

    public class PdfTriangle
    {
        public xPoint VA, VB, VC;
        public readonly double[] C_VA, C_VB, C_VC;

        internal xRect Bounds
        {
            get
            {
                double x_min = VA.X, x_max = VA.X,
                       y_min = VA.Y, y_max = VA.Y;

                if (VB.X < x_min)
                    x_min = VB.X;
                else if (VB.X > x_max)
                    x_max = VB.X;
                if (VB.Y < y_min)
                    y_min = VB.Y;
                else if (VB.Y > y_max)
                    y_max = VB.Y;

                if (VC.X < x_min)
                    x_min = VC.X;
                else if (VC.X > x_max)
                    x_max = VC.X;
                if (VC.Y < y_min)
                    y_min = VC.Y;
                else if (VC.Y > y_max)
                    y_max = VC.Y;

                return new xRect(x_min, y_min, x_max, y_max);
            }
        }

        internal PdfTriangle(int ncomps)
        {
            C_VA = new double[ncomps];
            C_VB = new double[ncomps];
            C_VC = new double[ncomps];
        }

        internal PdfTriangle(xPoint VA, xPoint VB, xPoint VC,
            double[] COLOR_VA, double[] COLOR_VB, double[] COLOR_VC)
        {
            this.VA = VA;
            this.VB = VB;
            this.VC = VC;
            C_VA = COLOR_VA;
            C_VB = COLOR_VB;
            C_VC = COLOR_VC;
        }
    }
}
