using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// Used to gather statistics about text related
    /// state parameters
    /// </summary>
    public class cTextStat
    {
        private Dictionary<object, int> _counts;
        
        private int _color_count = 0, _font_count = 0;
        private FontAndSize _font;

        public cBrush Color { get; private set; }
        public cFont Font { get { return _font != null ? _font.Font : null; } }
        public cSize? FontSize { get { return _font != null ? _font.Size : null; } }

        public bool HasText { get { return _counts != null; } }

        public double MaxDecent { get; private set; }

        public void ResetDecent() { MaxDecent = 0; }

        public void AddColor(cBrush color)
        {
            if (color == null) return;

            int count = CountObject(color);
            if (count > _color_count)
            {
                _color_count = count;
                Color = color;
            }
        }

        public void AddFont(cFont font, cSize? size)
        {
            if (font == null || size == null) return;
            var fs = new FontAndSize(font, size.Value);

            int count = CountObject(fs);
            if (count > _font_count)
            {
                _font_count = count;
                _font = fs;
            }

            var s = size.Value;
            if (s.IsFirm)
            {
                //We ignore font sized with percentage as I don't support font
                //sized in percentages. 
                double descent = font.Descent * s.Value;
                MaxDecent = Math.Max(MaxDecent, descent);
            }
        }

        private int CountObject(object obj)
        {
            if (_counts == null)
            {
                _counts = new Dictionary<object, int>();
                _counts.Add(obj, 1);
                return 1;
            }

            int count;
            if (_counts.TryGetValue(obj, out count))
            {
                _counts[obj] = ++count;
                return count;
            }
            _counts[obj] = 1;
            return 1;
        }

        class FontAndSize
        {
            public readonly cFont Font;
            public readonly cSize? Size;

            public FontAndSize(cFont font, cSize? size)
            {
                Font = font;
                Size = size;
            }

            public override bool Equals(object obj)
            {
                if (obj is FontAndSize)
                {
                    var f = (FontAndSize) obj;
                    return ReferenceEquals(Font, f.Font) && Size == f.Size;
                }
                return false;
            }

            public override int GetHashCode()
            {
                int hash = 17;

                hash = hash * 23 + Font.GetHashCode();
                hash = hash * 23 + Size.GetHashCode();

                return hash;
            }
        }
    }
}
