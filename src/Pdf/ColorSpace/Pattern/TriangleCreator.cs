using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using System;
using System.Collections.Generic;
using PdfLib.Compose;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <summary>
    /// For drawing gouraud shaded triangles.
    /// 
    /// When drawing using this method, using the NewFromAtoC method, 
    /// the triangle will in effect pop out on the opposite side of 
    /// where it popped out last. 
    /// </summary>
    public class PdfGouraudShadingCreator
    {
        private TriangleCreator _tc;
        private IColorSpace _cs;

        public PdfGouraudShadingCreator(xPoint VA, xPoint VB, xPoint VC,
            IColorSpace cs, cColor COLOR_VA, cColor COLOR_VB, cColor COLOR_VC)
        {
            if (cs is CSPattern)
                throw new PdfNotSupportedException("Pattern color space");
            _cs = cs;

            _tc = new TriangleCreator(VA, VB, VC,
                COLOR_VA.ConvertTo(cs).ToArray(),
                COLOR_VB.ConvertTo(cs).ToArray(),
                COLOR_VC.ConvertTo(cs).ToArray());
        }

        public void NewTriangle(xPoint VA, xPoint VB, xPoint VC,
            cColor COLOR_VA, cColor COLOR_VB, cColor COLOR_VC)
        {
            _tc.New(VA, VB, VC,
                COLOR_VA.ConvertTo(_cs).ToArray(),
                COLOR_VB.ConvertTo(_cs).ToArray(),
                COLOR_VC.ConvertTo(_cs).ToArray());
        }

        public void NewFromAtoC(xPoint VD, cColor COLOR_VD)
        {
            _tc.NewAtoC(VD, COLOR_VD.ConvertTo(_cs).ToArray());
        }

        public void NewFromBtoC(xPoint VD, cColor COLOR_VD)
        {
            _tc.NewBtoC(VD, COLOR_VD.ConvertTo(_cs).ToArray());
        }

        public PdfGouraudShading CreateShader(int bits_per_coordinate, int bits_per_color_component = 8)
        {
            return new PdfGouraudShading(_tc.ToArray(), bits_per_coordinate, _cs, null, bits_per_color_component);
        }
    }

    internal class TriangleCreator
    {
        xPoint va, vb, vc;
        double[] c_va, c_vb, c_vc;
        List<PdfTriangle> triangles = new List<PdfTriangle>(50);

        public TriangleCreator(xPoint VA, xPoint VB, xPoint VC,
            double[] COLOR_VA, double[] COLOR_VB, double[] COLOR_VC)
        {
            New(VA, VB, VC, COLOR_VA, COLOR_VB, COLOR_VC);
        }
        
        public void New(xPoint VA, xPoint VB, xPoint VC,
            double[] COLOR_VA, double[] COLOR_VB, double[] COLOR_VC)
        {
            va = VA;
            c_va = COLOR_VA;
            vb = VB;
            c_vb = COLOR_VB;
            vc = VC;
            c_vc = COLOR_VC;
            triangles.Add(new PdfTriangle(VA, VB, VC, COLOR_VA, COLOR_VB, COLOR_VC));
        }

        public void NewAtoC(xPoint VD, double[] COLOR_VD)
        {
            New(vb, vc, VD, c_vb, c_vc, COLOR_VD);
        }

        public void NewBtoC(xPoint VD, double[] COLOR_VD)
        {
            New(va, vc, VD, c_va, c_vc, COLOR_VD);
        }

        public PdfTriangle[] ToArray() => triangles.ToArray();
    }
}
