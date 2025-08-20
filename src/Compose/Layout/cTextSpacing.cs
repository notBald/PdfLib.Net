using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// Determines how words and characters are spaced and horizontally scaled
    /// </summary>
    /// <remarks>
    /// Supported by cText and cTextArea
    /// </remarks>
    public struct cTextSpacing
    {
        /// <summary>
        /// A number, in precentages. 80 = 80 % of normal.
        /// </summary>
        public readonly float HorizontalScale;

        /// <summary>
        /// Distance between words
        /// </summary>
        public readonly float WordSpacing;

        /// <summary>
        /// Distance between characters
        /// </summary>
        public readonly float CharacterSpacing;

        /// <summary>
        /// Raises text over the baseline.
        /// </summary>
        public readonly float TextRise;

        /// <summary>
        /// Makes a cTextSpace instance
        /// </summary>
        /// <param name="Tc">Character spacing</param>
        /// <param name="Tw">Word spacing</param>
        /// <param name="Th">Horizontal scaling</param>
        /// <param name="Tr">Text Rise</param>
        public cTextSpacing(float Tc, float Tw, float Th, float Tr = 0)
        {
            CharacterSpacing = Tc;
            WordSpacing = Tw;
            HorizontalScale = Th;
            TextRise = 0;
        }

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is based on Josh Bloch's Effective Java.
        /// </summary>
        public override int GetHashCode() 
        {
            int hash = 17;

            hash = hash * 23 + HorizontalScale.GetHashCode();
            hash = hash * 23 + WordSpacing.GetHashCode();
            hash = hash * 23 + CharacterSpacing.GetHashCode();
            hash = hash * 23 + TextRise.GetHashCode();

            return hash;
        }
        public override bool Equals(object obj)
        {
            return (obj is cTextSpacing) && this == (cTextSpacing)obj;
        }
        public static bool operator ==(cTextSpacing p1, cTextSpacing p2)
        {
            return p1.HorizontalScale == p2.HorizontalScale && 
                   p1.WordSpacing == p2.WordSpacing && 
                   p1.CharacterSpacing == p2.CharacterSpacing &&
                   p1.TextRise == p2.TextRise;
        }
        public static bool operator !=(cTextSpacing p1, cTextSpacing p2)
        {
            return p1.HorizontalScale != p2.HorizontalScale || 
                   p1.WordSpacing != p2.WordSpacing || 
                   p1.CharacterSpacing != p2.CharacterSpacing ||
                   p1.TextRise != p2.TextRise;
        }
        public override string ToString()
        {
            return string.Format("Tc: {0}, Tw: {1}, Th: {2}, Tr: {3}", CharacterSpacing, WordSpacing, HorizontalScale, TextRise);
        }

        #endregion
    }
}
