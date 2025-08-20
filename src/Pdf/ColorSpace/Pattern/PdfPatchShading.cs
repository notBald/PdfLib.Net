using PdfLib.Pdf.Function;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using System;
using System.Collections.Generic;
using System.IO;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public abstract class PdfPatchShading : PdfStreamShading
    {
        #region Variables and properties

        public PdfTensor[] Tensors
        {
            get
            {
                var list = new List<PdfTensor>(50);
                if (ShadingType == 7)
                {
                    new TensorReader(this, _stream.DecodedStream).Read((points, ncomps) =>
                    {
                        var patch = new PdfTensor(points, ncomps);
                        list.Add(patch);
                        return patch;
                    }, true);
                }
                else
                {
                    new TensorReader(this, _stream.DecodedStream).Read((points, ncomps) =>
                    {
                        var patch = new PdfCoonsPatch(points, ncomps).Tensor;
                        list.Add(patch);
                        return patch;
                    }, false);
                }

                return list.ToArray();
            }
        }

        public PdfTensorF[] TensorsF
        {
            get
            {
                var list = new List<PdfTensorF>(50);
                if (ShadingType == 7)
                {
                    new TensorReaderF(this, _stream.DecodedStream).Read((points, ncomps) =>
                    {
                        var patch = new PdfTensorF(points, ncomps);
                        list.Add(patch);
                        return patch;
                    }, true);
                }
                else
                {
                    new TensorReaderF(this, _stream.DecodedStream).Read((points, ncomps) =>
                    {
                        var patch = new PdfCoonsPatchF(points, ncomps).Tensor;
                        list.Add(patch);
                        return patch;
                    }, false);
                }

                return list.ToArray();
            }
        }

        #endregion

        #region Init

        internal PdfPatchShading(IWStream stream)
            : base(stream)
        {  }

        protected PdfPatchShading(PdfDictionary dict, IStream stream)
            : base(dict, stream)
        {  }

        internal PdfPatchShading(IEnumerable<PdfCoonsPatch> coons, IColorSpace cs, PdfFunctionArray functions, 
            int bits_per_coordinate, int bits_per_component, int shader_type)
            : base(new WritableStream(), cs, functions, bits_per_coordinate, bits_per_component, shader_type)
        {
            var ws = (WritableStream)_stream;
            var d = ws.Elements;

            if (coons == null)
                throw new ArgumentException("No coons");
            bool has_bounds = false;
            xRect bounds = new xRect();
            foreach (var coon in coons)
            {
                if (has_bounds)
                    bounds = bounds.Enlarge(coon.Bounds);
                else
                {
                    has_bounds = true;
                    bounds = coon.Bounds;
                }
            }
            if (!has_bounds)
                throw new ArgumentException("No coons");

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

            var tw = new TensorWriter(bits_per_coordinate, bits_per_component, decode, bounds);
            ws.RawStream = tw.WriteTensors(coons, shader_type == 7);
        }

        #endregion

        internal sealed class TensorReader : CoordReader
        {
            public delegate PdfCoonsPatch CreatePatch(xPoint[] points, int ncomps);

            public TensorReader(PdfPatchShading p, byte[] data)
                : base(p, data)
            { }

            public void Read(CreatePatch create, bool is_tensor)
            {
                //The flag signals what edges the mesh shares with the previous mesh
                int flag;
                int n_points = is_tensor ? 16 : 12;

                xPoint[] previous = null;
                PdfCoonsPatch prev_tensor = null;

                //Reads as many meshes as it can. If a corrput mesh is
                //encountered, it stops reading without error.
                while (br.HasBits(bpf))
                {
                    flag = (int)(br.GetBitsU(bpf) & 0x03);

                    try
                    {
                        #region Fetch points

                        xPoint[] coords = new xPoint[n_points];

                        for (int c = flag == 0 ? 0 : 4; c < coords.Length; c++)
                            coords[c] = ReadCoord();

                        if (flag > 0)
                        {
                            if (previous == null)
                                break;

                            for (int c = flag * 4 - flag, p = 0; p < 4; c++)
                            {
                                if (c == 12)
                                    coords[p++] = previous[0];
                                else
                                    coords[p++] = previous[c];
                            }
                        }

                        #endregion

                        PdfCoonsPatch cp = create(coords, ncomps);

                        #region Fetch colors
                        // c03  c33
                        // c00  c30
                        if (flag == 0)
                        {
                            //C00
                            ReadColor(cp.Color_ll);

                            //C03
                            ReadColor(cp.Color_ul);
                        }
                        else
                        {
                            double[] c00, c03;
                            if (flag == 1)
                            {
                                c00 = prev_tensor.Color_ul;
                                c03 = prev_tensor.Color_ur;
                            }
                            else if (flag == 2)
                            {
                                c00 = prev_tensor.Color_ur;
                                c03 = prev_tensor.Color_lr;
                            }
                            else
                            {
                                c00 = prev_tensor.Color_lr;
                                c03 = prev_tensor.Color_ll;
                            }
                            Array.Copy(c00, cp.Color_ll, ncomps);
                            Array.Copy(c03, cp.Color_ul, ncomps);
                        }

                        ReadColor(cp.Color_ur);
                        ReadColor(cp.Color_lr);

                        #endregion

                        //patches.Add(cp);
                        previous = coords;
                        prev_tensor = cp;
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    br.ByteAlign();
                }

                //return patches.ToArray();
            }
        }

        internal sealed class TensorReaderF : CoordReaderF
        {
            public delegate PdfCoonsPatchF CreatePatch(xPointF[] points, int ncomps);

            public TensorReaderF(PdfPatchShading p, byte[] data)
                : base(p, data)
            { }

            public void Read(CreatePatch create, bool is_tensor)
            {
                //The flag signals what edges the mesh shares with the previous mesh
                int flag;
                int n_points = is_tensor ? 16 : 12;

                xPointF[] previous = null;
                PdfCoonsPatchF prev_tensor = null;

                //Reads as many meshes as it can. If a corrput mesh is
                //encountered, it stops reading without error.
                while (br.HasBits(bpf))
                {
                    flag = (int)(br.GetBitsU(bpf) & 0x03);

                    try
                    {
                        #region Fetch points

                        xPointF[] coords = new xPointF[n_points];

                        for (int c = flag == 0 ? 0 : 4; c < coords.Length; c++)
                            coords[c] = ReadCoord();

                        if (flag > 0)
                        {
                            if (previous == null)
                                break;

                            for (int c = flag * 4 - flag, p = 0; p < 4; c++)
                            {
                                if (c == 12)
                                    coords[p++] = previous[0];
                                else
                                    coords[p++] = previous[c];
                            }
                        }

                        #endregion

                        PdfCoonsPatchF cp = create(coords, ncomps);

                        #region Fetch colors
                        // c03  c33
                        // c00  c30
                        if (flag == 0)
                        {
                            //C00
                            ReadColor(cp.Color_ll);

                            //C03
                            ReadColor(cp.Color_ul);
                        }
                        else
                        {
                            double[] c00, c03;
                            if (flag == 1)
                            {
                                c00 = prev_tensor.Color_ul;
                                c03 = prev_tensor.Color_ur;
                            }
                            else if (flag == 2)
                            {
                                c00 = prev_tensor.Color_ur;
                                c03 = prev_tensor.Color_lr;
                            }
                            else
                            {
                                c00 = prev_tensor.Color_lr;
                                c03 = prev_tensor.Color_ll;
                            }
                            Array.Copy(c00, cp.Color_ll, ncomps);
                            Array.Copy(c03, cp.Color_ul, ncomps);
                        }

                        ReadColor(cp.Color_ur);
                        ReadColor(cp.Color_lr);

                        #endregion

                        //patches.Add(cp);
                        previous = coords;
                        prev_tensor = cp;
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    br.ByteAlign();
                }

                //return patches.ToArray();
            }
        }

        private sealed class TensorWriter : CoordWriter
        {
            public TensorWriter(int bits_per_coord, int bits_per_color, xRange[] decode, xRect bounds)
                : base(bits_per_coord, bits_per_color, decode, bounds,
                      2 + 32 * bits_per_coord + (decode.Length - 2) * bits_per_color * 4)
            { }

            public byte[] WriteTensors(IEnumerable<PdfCoonsPatch> tensors, bool write_tensor)
            {
                byte[] all_bytes = new byte[0];
                int pos = 0;

                foreach (var t in tensors)
                {
                    _bw.Position = 0;

                    //For now we wholly write out the tensors
                    _bw.Write(0, 2);
                    WriteCoord(t.P00);
                    WriteCoord(t.P01);
                    WriteCoord(t.P02);
                    WriteCoord(t.P03);
                    WriteCoord(t.P13);
                    WriteCoord(t.P23);
                    WriteCoord(t.P33);
                    WriteCoord(t.P32);
                    WriteCoord(t.P31);
                    WriteCoord(t.P30);
                    WriteCoord(t.P20);
                    WriteCoord(t.P10);
                    if (write_tensor)
                    {
                        WriteCoord(t.P11);
                        WriteCoord(t.P12);
                        WriteCoord(t.P22);
                        WriteCoord(t.P21);
                    }
                    WriteColor(t.Color_ll);
                    WriteColor(t.Color_ul);
                    WriteColor(t.Color_ur);
                    WriteColor(t.Color_lr);

                    _bw.Flush();

                    var bytes_written = (int)_bw.Position;
                    Array.Resize(ref all_bytes, all_bytes.Length + bytes_written);
                    Buffer.BlockCopy(_data, 0, all_bytes, pos, bytes_written);
                    pos += bytes_written;
                }

                return all_bytes;
            }
        }
    }
}
