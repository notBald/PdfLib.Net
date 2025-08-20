using System;
using System.Collections.Generic;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// Currently intended to be used for drawing borders on tables
    /// </summary>
    internal class cFigureDraw : IDisposable
    {
        private cRenderState _state;
        private IDraw _draw;

        private double? _sw;
        private cBrush _sc;
        private xDashStyle? _ss;

        private bool _has_drawing, _has_style;
        private bool _has_saved;

        /// <summary>
        /// Draws out a vector if possible.
        /// </summary>
        private cVector _line;

        public bool HasStartPoint { get { return _line != null; } }

        public xPoint LastPos { get { return _line != null ? _line.End.HasValue ? new xPoint(_line.End.Value) : _line.Start : new xPoint(); } }

        public double StrokeWidth
        {
            get { return _sw.HasValue ? _sw.Value : _state.StrokeWidth; }
            set
            {
                if (value != StrokeWidth)
                {
                    Commit();
                    _sw = value;
                    _has_style = true;
                }
            }
        }

        public cBrush StrokeColor
        {
            get { return _sc != null ? _sc : _state.StrokeColor; }
            set
            {
                if (!cBrush.Equals(StrokeColor, value))
                {
                    Commit();
                    _sc = value;
                    _has_style = true;
                }
            }
        }

        public xDashStyle? StrokeStyle
        {
            get { return _ss.HasValue ? _ss : _state.dash_style; }
            set
            {
                if (StrokeStyle != value)
                {
                    Commit();
                    _ss = value;
                    _has_style = true;
                }
            }
        }

        public cRenderState State { get { return _state; } }

        #region Init and dispose

        public cFigureDraw(IDraw draw, cRenderState state)
        {
            _state = state;
            _draw = draw;
        }

        public void Dispose()
        {
            Commit();
            if (_has_saved)
                _state = _state.Restore(_draw);
        }

        #endregion

        public void LineTo(double x, double y)
        {
            if (_line == null)
                throw new PdfNotSupportedException("Can't draw line without position");

            if (_line.End != null)
            {
                //Uses the line formula to see if the point is on the line
                if (double.IsInfinity(_line.Slope) && x == _line.End.Value.X || Util.Real.Same(y, _line.Slope * x + _line.YIntersect))
                    _line.End = new xVector(x, y);
                else
                {
                    CommitVector();
                    _line = new cVector(x, y);
                }
            }
            else
            {
                _line.End = new xVector(x, y);
                _line.Slope = (_line.Start.Y - y) / (_line.Start.X - x);
                _line.YIntersect = - _line.Slope * _line.Start.X + _line.Start.Y;
            }
        }

        public void MoveTo(double x, double y)
        {
            CommitStyle();
            CommitVector();
            _line = new cVector(x, y);
        }

        private void CommitVector()
        {
            if (_line != null)
            {
                if (_line.End != null)
                {
                    //if (_has_drawing)
                    //    _draw.LineTo(_line.Start.X, _line.Start.Y);
                    //else
                    //{
                    //    CommitStyle();
                        _draw.MoveTo(_line.Start.X, _line.Start.Y);
                        _has_drawing = true;
                    //}

                    var end = _line.End.Value;
                    _draw.LineTo(end.X, end.Y);
                    _line = null;
                }
                else if (_has_drawing)
                {
                    _draw.LineTo(_line.Start.X, _line.Start.Y);
                    _line = null;
                }
                else
                    throw new PdfInternalException("Tried to draw empty vector");
            }
        }

        private void CommitStyle()
        {
            if (_has_style)
            {
                if (!_has_saved)
                {
                    _has_saved = true;
                    _state.Save(_draw);
                }

                if (_ss != null)
                {
                    _state.SetStrokeDashAr(_ss, _draw);
                    _ss = null;
                }
                if (_sc != null)
                {
                    _sc.SetColor(_state, false, _draw);
                    _sc = null;
                }
                if (_sw != null)
                {
                    _state.SetStrokeWidth(_sw.Value, _draw);
                    _sw = null;
                }
                _has_style = false;
            }
        }

        private void Commit()
        {
            CommitVector();
            if (_has_drawing)
            {
                _has_drawing = false;
                _draw.DrawPathEO(false, true, false);
            }
        }

        private class cVector
        {
            public readonly xPoint Start;
            public xVector? End;
            public double Slope;
            public double YIntersect;

            public cVector(double x, double y)
            {
                Start = new xPoint(x, y);
            }
        }
    }
}
