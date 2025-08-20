using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Function;

namespace PdfLib.Render
{
    /// <summary>
    /// For extracting information from fonts.
    /// </summary>
    internal static class InfoFF
    {
        public static FontFactory<InfoPath> CreateFactory() => new FontFactory<InfoPath>(new InfoGlyphFactory());

        class InfoGlyphFactory : IGlyphFactoryCache<InfoPath>
        {
            IGlyphCache IGlyphFactoryCache<InfoPath>.CreateGlyphCache() => new NullGlyphCache();

            /// <summary>
            /// Attach a Matrix transform to the path
            /// </summary>
            void IGlyphFactory<InfoPath>.SetTransform(InfoPath path, xMatrix transform)
            {
                path.Transform = transform;
            }

            /// <summary>
            /// Creates a path without anything in it
            /// </summary>
            InfoPath IGlyphFactory<InfoPath>.CreateEmptyPath()
            {
                return new InfoPath();
            }

            rGlyph IGlyphFactory<InfoPath>.CreateGlyph(InfoPath[] outline, xPoint advance, xPoint origin, xMatrix mt, bool freeze, bool is_space_char)
            {
                //var ar = new InfoPath[outline.Length];
                //for (int c = 0; c < ar.Length; c++)
                //    ar[c] = (InfoPath)outline[c];
                return new rGlyphInfo(advance, origin, mt, is_space_char);
            }

            IGlyph<InfoPath> IGlyphFactory<InfoPath>.GlyphRenderer()
            {
                return new GlyphCreator();
            }

            class GlyphCreator : IGlyph<InfoPath>, IGlyphDraw
            {
                public GlyphCreator()
                { }

                public IGlyphDraw Open()
                {
                    return this;
                }

                void IGlyphDraw.BeginFigure(xPoint startPoint, bool isFilled, bool isClosed)
                {
                }
                void IGlyphDraw.QuadraticBezierTo(xPoint point1, xPoint point2, bool isStroked, bool isSmoothJoin)
                {
                }
                void IGlyphDraw.LineTo(xPoint point, bool isStroked, bool isSmoothJoin)
                {
                }
                void IGlyphDraw.BezierTo(xPoint point1, xPoint point2, xPoint point3, bool isStroked, bool isSmoothJoin)
                {
                }

                public void Dispose()
                {
                    //Todo: Perhaps restore the path being constructed
                }

                public InfoPath GetPath()
                {
                    return new InfoPath();
                }
            }
        }
    }

    internal class InfoPath
    {
        public xMatrix Transform;
    }

    internal class rGlyphInfo : rGlyph
    {
        /// <summary>
        /// Movement from this glyph after painting it
        /// </summary>
        public readonly xPoint MoveDist;

        /// <summary>
        /// Offset from where to draw the glyph
        /// </summary>
        public readonly xPoint Origin;

        /// <summary>
        /// Transform for this glyph that scales it down to 1x1
        /// </summary>
        public readonly xMatrix Transform;

        /// <summary>
        /// Space glyphs are used to sepperate words.
        /// </summary>
        public readonly bool IsSpace;

        public rGlyphInfo(xPoint advance, xPoint origin, xMatrix mt, bool is_space_char)
        {
            MoveDist = advance;
            Transform = mt;

            IsSpace = is_space_char;
            Origin = origin;
        }
    }
}
