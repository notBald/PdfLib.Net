using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Pdf.Primitives
{
    public enum PdfPageLayout
    {
        SinglePage,
        OneColumn,
        TwoColumnLeft,
        TwoColumnRight,
        TwoPageLeft,
        TwoPageRight
    }

    internal static class PdfPageLayoutConv
    {
        public static string Convert(PdfPageLayout layout)
        {
            switch (layout)
            {
                case PdfPageLayout.OneColumn: return "OneColumn";
                case PdfPageLayout.TwoColumnLeft: return "TwoColumnLeft";
                case PdfPageLayout.TwoColumnRight: return "TwoColumnRight";
                case PdfPageLayout.TwoPageLeft: return "TwoPageLeft";
                case PdfPageLayout.TwoPageRight: return "TwoPageRight";
                default: return "SinglePage";
            }
        }

        public static PdfPageLayout Convert(string layout)
        {
            switch (layout)
            {
                case "OneColumn": return PdfPageLayout.OneColumn;
                case "TwoColumnLeft": return PdfPageLayout.TwoColumnLeft;
                case "TwoColumnRight": return PdfPageLayout.TwoColumnRight;
                case "TwoPageLeft": return PdfPageLayout.TwoPageLeft;
                case "TwoPageRight": return PdfPageLayout.TwoPageRight;
                default: return PdfPageLayout.SinglePage;
            }
        }
    }
}
