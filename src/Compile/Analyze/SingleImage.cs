using System;
using System.Linq;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf;
using PdfLib.Render.Commands;

namespace PdfLib.Compile.Analyze
{
    public class SingleImage
    {
        public xSize RenderSize
        {
            get
            {
                int l = _data.Children.Length;
                int pos = l == 2 ? 0 : l - 2;
                var c = (cm_RND)_data.Children[pos];
                return new xSize(c.Matrix.M11, c.Matrix.M22);
            }
            set
            {
                int l = _data.Children.Length;
                int pos = l == 2 ? 0 : l - 2;
                var c = (cm_RND)_data.Children[pos];
                var m = c.Matrix;
                _data.Children[pos] = new cm_RND(new xMatrix(value.Width, m.M12, m.M21, value.Height, m.OffsetX, m.OffsetY));
                
            }
        }

        public xSize? ClipSize
        {
            get
            {
                if (_data.Children[0] is re_CMD)
                {
                    var re = (re_CMD)_data.Children[0];
                    return new xSize(re.Width, re.Height);
                }

                return null;
            }
            set
            {
                if (value == null)
                {
                    if (_data.Children[0] is re_CMD)
                        _data.Children = _data.Children.Skip(1).ToArray();
                    return;
                };
                var val = value.Value;

                if (_data.Children[0] is re_CMD)
                {
                    var re = (re_CMD)_data.Children[0];
                    _data.Children[0] = new re_CMD(val.Height, val.Width, re.Y, re.X);
                }
                else
                {
                    var pos = Position;
                    var c = new RenderCMD[]
                    {
                        new re_CMD(val.Height, val.Width, pos.Y, pos.X),
                        new n_CMD(),
                        _data.Children[0],
                        _data.Children[1]
                    };
                }
            }
        }

        public xPoint? ClipPosition
        {
            get
            {
                if (_data.Children[0] is re_CMD)
                {
                    var re = (re_CMD)_data.Children[0];
                    return new xPoint(re.X, re.Y);
                }

                return null;
            }
            set
            {
                if (value == null)
                {
                    if (_data.Children[0] is re_CMD)
                        _data.Children = _data.Children.Skip(1).ToArray();
                    return;
                };
                var val = value.Value;

                if (_data.Children[0] is re_CMD)
                {
                    var re = (re_CMD)_data.Children[0];
                    _data.Children[0] = new re_CMD(re.Height, re.Width, val.Y, val.X);
                }
                else
                {
                    var size = RenderSize;
                    var c = new RenderCMD[]
                    {
                        new re_CMD(size.Height, size.Width, val.Y, val.X),
                        new n_CMD(),
                        _data.Children[0],
                        _data.Children[1]
                    };
                }
            }
        }

        public xPoint Position
        {
            get
            {
                var c = (cm_RND)_data.Children[_data.Children.Length - 2];
                return new xPoint(c.Matrix.OffsetX, c.Matrix.OffsetY);
            }
            set
            {
                var c = (cm_RND)_data.Children[_data.Children.Length - 2];
                var m = c.Matrix;
                _data.Children[_data.Children.Length - 2] = new cm_RND(new xMatrix(m.M11, m.M12, m.M21, m.M22, value.X, value.Y));
            }
        }

        public PdfImage Image
        {
            get { return ((Do_CMD)_data.Children[_data.Children.Length - 1]).Image; }
            set
            {
                if (value == null) throw new ArgumentNullException();
                _data.Children[_data.Children.Length - 1] = new Do_CMD(value);
            }
        }

        private readonly AnalyzeBlock _data;

        internal SingleImage(AnalyzeBlock block)
        {
            _data = block;
        }
    }
}
