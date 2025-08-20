using System;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Transparency
{
    public enum PdfBlendModes
    {
        Normal,
        Compatible,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion
    }

    internal static class PdfBlendMode
    {
        internal static PdfBlendModes Convert(PdfItem itm)
        {
            if (itm != null)
            {
                itm = itm.Deref();
                if (itm is PdfArray)
                {
                    var ar = (PdfArray)itm;
                    for (int c = 0; c < ar.Length; c++)
                    {
                        itm = ar[c];
                        var mode = Convert(itm.GetString());
                        if (mode != PdfBlendModes.Compatible)
                            return mode;
                    }
                }
                else if(itm != null)
                {
                    var mode = Convert(itm.GetString());
                    if (mode != PdfBlendModes.Compatible)
                        return mode;
                }
            }

            return PdfBlendModes.Normal;
        }

        public static PdfBlendModes Convert(string name)
        {
            switch (name)
            {
                case "Compatible":
                case "Normal": return PdfBlendModes.Normal;
                case "Multiply": return PdfBlendModes.Multiply;
                case "Screen": return PdfBlendModes.Screen;
                case "Overlay": return PdfBlendModes.Overlay;
                case "Darken": return PdfBlendModes.Darken;
                case "Lighten": return PdfBlendModes.Lighten;
                case "ColorDodge": return PdfBlendModes.ColorDodge;
                case "ColorBurn": return PdfBlendModes.ColorBurn;
                case "HardLight": return PdfBlendModes.HardLight;
                case "SoftLight": return PdfBlendModes.SoftLight;
                case "Difference": return PdfBlendModes.Difference;
                case "Exclusion": return PdfBlendModes.Exclusion;

                default:
                    return PdfBlendModes.Compatible;
            }
        }

        public static string Convert(PdfBlendModes mode)
        {
            switch (mode)
            {
                case PdfBlendModes.Compatible:
                case PdfBlendModes.Normal: return "Normal";
                case PdfBlendModes.Multiply: return "Multiply";
                case PdfBlendModes.Screen: return "Screen";
                case PdfBlendModes.Overlay: return "Overlay";
                case PdfBlendModes.Darken: return "Darken";
                case PdfBlendModes.Lighten: return "Lighten";
                case PdfBlendModes.ColorDodge: return "ColorDodge";
                case PdfBlendModes.ColorBurn: return "ColorBurn";
                case PdfBlendModes.HardLight: return "HardLight";
                case PdfBlendModes.SoftLight: return "SoftLight";
                case PdfBlendModes.Difference: return "Difference";
                case PdfBlendModes.Exclusion: return "Exclusion";
                default: throw new PdfNotSupportedException();
            }
        }
    }
}
