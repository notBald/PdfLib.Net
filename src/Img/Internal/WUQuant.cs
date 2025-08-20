using PdfLib.Img.Internal.QWU;

namespace PdfLib.Img.Internal
{
    internal static class WUQuant
    {
        public static IColorQuantizer CreateBGR24Quant()
        {
            return new WuColorQuantizer();
        }
    }
}
