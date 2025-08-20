using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;

//Todo: Memleaks are quite likely, needs to be profiled/looked for.
//A potentially better alternative can be found here: https://docs.microsoft.com/en-us/xamarin/xamarin-forms/user-interface/graphics/skiasharp/
namespace PdfLib.Render.CairoLib
{
    /// <summary>
    /// This class wraps the cairo rendering libary
    /// </summary>
    public class Cairo : IDisposable
    {
        #region Variables and properties

        /// <summary>
        /// The drawing surface
        /// </summary>
        cSurface _surface;

        /// <summary>
        /// The cairo context
        /// </summary>
        IntPtr _cr;

        /// <summary>
        /// Shape cairo
        /// </summary>
        IntPtr _shape_cr;
        int _shape_depth;

        /// <summary>
        /// Transparency information
        /// </summary>
        Stack<PatAndMat> _mask_stack = new Stack<PatAndMat>(8);
        cPattern _mask;

        /// <summary>
        /// One must know the matrix of the time the mask was set.
        /// </summary>
        /// <remarks>This because one must set this matrix before drawing with a mask</remarks>
        cMatrix _mask_matrix;

        /// <summary>
        /// Whenever to destroy the mask or not
        /// </summary>
        bool _mask_dispose;

        /// <summary>
        /// Cairo libary version
        /// </summary>
        public string Version 
        { 
            get 
            {
                //This string is likely a constant, meaning it is not to be released. 
                return  Marshal.PtrToStringAnsi(W32.version_string()); 
            } 
        }

        /// <summary>
        /// Whenever cairo is recording the shape being drawn.
        /// </summary>
        public bool IsTrackingShape 
        { 
            get { return _shape_cr != IntPtr.Zero; }
            set
            {
                if (value)
                {
                    if (!IsTrackingShape)
                    {
                        _shape_depth = 0;
                        _shape_cr = CreateNewClipImpl(Enums.Content_type.CAIRO_CONTENT_ALPHA);
                        var i = cMatrix.Identity;
                        W32.set_matrix(_shape_cr, ref i);
                    }
                }
                else if (IsTrackingShape)
                {
                    //Todo: Does the surface need be destroyed? I assume so since it was created by
                    //"CreateNew".
                    cSurface.W32.destroy(ShapeSurface);
                    W32.destroy(_shape_cr);
                    _shape_cr = IntPtr.Zero;
                }
            }
        }

        private IntPtr ShapeSurface { get { return IsTrackingShape ? W32.get_target(_shape_cr) : IntPtr.Zero; } }
#if DEBUG        
        private Cairo ShapeCairo
        {
            get 
            {
                //Note, this property likely breaks surface ref count, resulting in a Assert message box. 
                if (!IsTrackingShape) return null;
                var cairo = new Cairo(ShapeSurface, _shape_cr);
                cairo._shape_depth = int.MinValue; //Cheat to prevent assert errors
                return cairo;
            }
        }
#endif
        public cPattern Shape
        {
            get
            {
                if (!IsTrackingShape) return null;
                return new cPattern(cPattern.W32.create_for_surface(W32.get_target(_shape_cr)));
            }
        }

        /// <summary>
        /// An offset that is added to the device coordinates determined by the CTM when drawing to surface.
        /// </summary>
        public cPoint DeviceOffset
        {
            get { return _surface.DeviceOffset; }
            set { _surface.DeviceOffset = value; }
        }

        /// <summary>
        /// Stride of the surface
        /// </summary>
        public int Stride
        {
            get { return _surface.Stride; }
        }

        /// <summary>
        /// Height of the surface
        /// </summary>
        public int Height
        {
            get { return _surface.Height; }
        }

        /// <summary>
        /// Width of the surface
        /// </summary>
        public int Width
        {
            get { return _surface.Width; }
        }

        /// <summary>
        /// Format of the surface
        /// </summary>
        public Enums.Format Format
        {
            get { return _surface.Format; }
        }

        public Enums.SurfaceType SurfaceType
        {
            get { return _surface.SurfaceType; }
        }

        public Enums.FillRule FillRule
        { 
            get { return W32.get_fill_rule(_cr); }
            set 
            { 
                W32.set_fill_rule(_cr, value);
                if (IsTrackingShape)
                    W32.set_fill_rule(_shape_cr, value);
            }
        }

        public double MiterLimit
        {
            get { return W32.get_miter_limit(_cr); }
            set { W32.set_miter_limit(_cr, value); }
        }

        public double LineWidth
        {
            get { return W32.get_line_width(_cr); }
            set { W32.set_line_width(_cr, value); }
        }

        public Enums.LineCap LineCap
        {
            get { return W32.get_line_cap(_cr); }
            set { W32.set_line_cap(_cr, value); }
        }

        public Enums.LineJoin LineJoin
        {
            get { return W32.get_line_join(_cr); }
            set { W32.set_line_join(_cr, value); }
        }

        public xDashStyle DashStyle
        {
            set 
            { 
                W32.set_dash(_cr, value.Dashes, value.Dashes.Length, value.Phase); 
                if (IsTrackingShape)
                    W32.set_dash(_shape_cr, value.Dashes, value.Dashes.Length, value.Phase); 
            }
            get
            {
                int n = W32.get_dash_count(_cr);
                if (n == 0) return new xDashStyle();
                var da = new double[n];
                double off = 0;
                W32.get_dash(_cr, da, ref off);
                return new xDashStyle(off, da);
            }
        }

        public xPoint CurrentPoint
        {
            get
            {
                if (W32.has_current_point(_cr))
                {
                    double x = 0, y = 0;
                    W32.get_current_point(_cr, ref x, ref y);
                    return new xPoint(x, y);
                }
                return new xPoint();
            }
        }

        public Enums.AntialiasMethod Antialias
        { 
            get { return W32.get_antialias(_cr); }
            set 
            { 
                W32.set_antialias(_cr, value); 
                if (IsTrackingShape)
                    W32.set_antialias(_shape_cr, value); 
            }
        }

        public Enums.BlendMode BlendMode
        {
            get { return W32.get_operator(_cr); }
            /*set { W32.set_operator(_cr, value); }*/
        }

        /// <summary>
        /// Status of the last executed command
        /// </summary>
        public Enums.Status Status
        {
            get { return W32.status(_cr); }
        }

        /// <summary>
        /// The bounding box (in user coordinate)s covering the area that would be affected, 
        /// (the "inked" area), by a fill operation given the current path and fill parameters
        /// </summary>
        public xRect FillBounds
        {
            get
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                W32.fill_extents(_cr, ref x1, ref y1, ref x2, ref y2);
                return new xRect(x1, y1, x2, y2);
            }
        }

        /// <summary>
        /// The bounding box (in user coordinates) covering the area that would be affected, 
        /// (the "inked" area), by a stroke operation given the current path and stroke 
        /// parameters.
        /// </summary>
        public xRect StrokeBounds
        {
            get
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                W32.stroke_extents(_cr, ref x1, ref y1, ref x2, ref y2);
                return new xRect(x1, y1, x2, y2);
            }
        }

        /// <summary>
        /// The bounding box (in user-space coordinates) covering the points on the current 
        /// path. Stroke parameters, fill rule, surface dimensions and clipping are not taken 
        /// into account. 
        /// </summary>
        public xRect PathBounds
        {
            get
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                W32.path_extents(_cr, ref x1, ref y1, ref x2, ref y2);
                return new xRect(x1, y1, x2, y2);
            }
        }

        /// <summary>
        /// The bounding box (in user coordinates) covering the area inside the current clip
        /// </summary>
        public xRect ClipBounds
        {
            get
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                W32.clip_extents(_cr, ref x1, ref y1, ref x2, ref y2);
                return new xRect(x1, y1, x2, y2);
            }
        }

        /// <summary>
        /// Current transform matrix
        /// </summary>
        public cMatrix CTM
        {
            get
            {
                var cm = new cMatrix();
                W32.get_matrix(_cr, ref cm);
                return cm;
            }
            set
            {
                W32.set_matrix(_cr, ref value);
                if (IsTrackingShape)
                    W32.set_matrix(_shape_cr, ref value);
            }
        }

        #endregion

        #region Init

        public Cairo(double width, double height)
        {
            width = Math.Floor(width);
            height = Math.Floor(height);

            _surface = new cSurface(W32.image_surface_create(Enums.Format.ARGB32, (int) width, (int) height));

            // For drawing we need a cairo context.
            _cr = W32.create(_surface.Surface);
        }

        private Cairo(IntPtr surface)
        {
            //This works since the surface has been created by CreateNew,
            //and we therefore have the ref count in order
            _surface = new cSurface(surface);
            _cr = W32.create(surface);
        }

        private Cairo(IntPtr surface, IntPtr cairo)
        {
            //This works since the surface has been created by CreateNew,
            //and we therefore have the ref count in order
            _surface = new cSurface(surface);
            _cr = cairo;
        }

        ~Cairo()
        {
            Deconstruct();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Deconstruct();
        }

        private void Deconstruct()
        {
            if (_surface.Surface != IntPtr.Zero && _shape_depth != int.MinValue)
            {
                if (_mask_dispose && _mask != null)
                    _mask.Dispose();

                _surface.Dispose();
                W32.destroy(_cr);
                if (IsTrackingShape)
                {
                    cSurface.W32.destroy(ShapeSurface);
                    W32.destroy(_shape_cr);
                }
                _surface = new cSurface(IntPtr.Zero);
            }
        }

        #endregion

        /// <summary>
        /// Creates a new image surface
        /// </summary>
        /// <remarks>
        /// It will be faster to use :
        /// 
        /// var image_data = new byte[..];
        /// GCHandle pinnedArray = GCHandle.Alloc(image_data, GCHandleType.Pinned);
        /// cairo_image_surface_create_for_data(pinnedArray.AddrOfPinnedObject(), ...)
        /// pinnedArray.Free();
        /// 
        /// As then one have the data without copying it.
        /// </remarks>
        public Cairo CreateNewImage(int width, int height)
        {
            return new Cairo(cSurface.W32.create_similar_image(_surface.Surface, Enums.Format.ARGB32, Math.Max(1, width), Math.Max(1, height)));
        }

        /// <summary>
        /// Creates a surface as large as the clipping region sutiable for creating
        /// soft mask clips
        /// </summary>
        public Cairo CreateNewClip()
        {
            var cai = CreateNewClip(Enums.Content_type.CAIRO_CONTENT_ALPHA);

            //By setting an opague color all draw command made on this cairo
            //becomes opague.
            cai.SetColor(0, 0, 0);
            return cai;
        }

        /// <summary>
        /// Creates a surface as the clipping region
        /// </summary>
        public Cairo CreateNewClip(Enums.Content_type contents)
        {
            var cairo = CreateNewClipImpl(contents);
            return new Cairo(W32.get_target(cairo), cairo);
        }

        /// <summary>
        /// Creates a surface as the clipping region
        /// </summary>
        /*public Cairo CreateNewClip(Enums.Content_type contents)
        {
            var bounds = ClipBounds;
            var device_bounds = Transform(bounds);
            int width = (int)Math.Ceiling(Math.Abs(device_bounds.Width));
            int height = (int)Math.Ceiling(Math.Abs(device_bounds.Height));

            var surface = W32.surface_create_similar(_surface, contents, width, height);
            var cai = new Cairo(surface);
            var deo = DeviceOffset;
            cai.DeviceOffset = new cPoint(deo.X - device_bounds.LowerLeft.X, deo.Y - device_bounds.UpperRight.Y);
            cai.CTM = CTM;

            return cai;
        }*/

        private IntPtr CreateNewClipImpl(Enums.Content_type contents)
        {
            //Calculates the size of the clipping region
            var bounds = ClipBounds;
            var device_bounds = Transform(bounds);
            int width = (int)Math.Ceiling(Math.Abs(device_bounds.Width));
            int height = (int)Math.Ceiling(Math.Abs(device_bounds.Height));

            //Creates a surface at the size of the clipping region
            var surface = cSurface.W32.create_similar(_surface.Surface, contents, width, height);
            var cairo = W32.create(surface);

            //Adjust the offset so that painting happens inside the clipping region
            var deo = DeviceOffset;
            cSurface.W32.set_device_offset(surface, deo.X - device_bounds.LowerLeft.X, deo.Y - deo.Y - device_bounds.UpperRight.Y);

            //Must have the same CTM
            var ctm = CTM;
            W32.set_matrix(cairo, ref ctm);

            return cairo;
        }

        /// <summary>
        /// Returns the rendered data in bgra32 format
        /// </summary>
        public byte[] GetRenderedData()
        {
            //Gets the data into managed memory
            cSurface.W32.flush(_surface.Surface);
            IntPtr data = cSurface.W32.image_surface_get_data(_surface.Surface);
            int size = Height * Stride;
            var ba = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(data, ba, 0, size);

            return ba;
        }

        public rImage GetImage()
        {
            return new rImage(GetRenderedData(), Stride, Width, Height, Format);
        }

        /// <summary>
        /// Saves the state and prepends the matrix
        /// </summary>
        /// <param name="matrix">Matrix to prepend</param>
        /// <remarks>
        /// Work alike to DrawingContext.PushTransform
        /// </remarks>
        public void PushTransform(cMatrix matrix)
        {
            Save();
            Transform(matrix);
        }

        /// <summary>
        /// Prepends the matrix
        /// </summary>
        /// <param name="matrix">Matrix to prepend</param>
        public void Transform(cMatrix matrix)
        {
            W32.transform(_cr, ref matrix);
            if (IsTrackingShape)
                W32.transform(_shape_cr, ref matrix);
        }

        public void Transform(xMatrix matrix)
        {
            var cm = new cMatrix(matrix);
            W32.transform(_cr, ref cm);
            if (IsTrackingShape)
                W32.transform(_shape_cr, ref cm);
        }

        /// <summary>
        /// Sets the soft mask
        /// </summary>
        public void SetMask(cPattern pat, bool dispose)
        {
            _mask = pat;
            _mask_matrix = CTM;
            _mask_dispose = dispose;
        }

        /// <summary>
        /// Pushes a clip path onto the stack
        /// </summary>
        /// <remarks>
        /// Work alike to DrawingContext.PushTransform
        /// </remarks>
        public void PushClip()
        {
            W32.save(_cr);
            throw new NotImplementedException();
        }

        public void SetPattern(cPattern pat)
        {
            W32.set_source(_cr, pat.Pattern);
        }

        public void SetColor(double r, double g, double b)
        {
            W32.set_source_rgb(_cr, r, g, b);
        }

        public void SetColor(DblColor c)
        {
            W32.set_source_rgb(_cr, c.R, c.G, c.B);
        }

        /// <summary>
        /// Creates a rectangle
        /// </summary>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Width of the rectangle</param>
        /// <param name="height">Height of the rectangle</param>
        public void RectangleAt(double x, double y, double width, double height)
        {
            W32.rectangle(_cr, x, y, width, height);
            if (IsTrackingShape)
                W32.rectangle(_shape_cr, x, y, width, height);
        }

        public void RectangleAt(xRect r)
        {
            RectangleAt(r.X, r.Y, r.Width, r.Height);
        }

        public void DrawRectangle(xRect r)
        {
            DrawRectangle(r.X, r.Y, r.Width, r.Height);
        }

        /// <summary>
        /// Draws a rectangle
        /// </summary>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Width of the rectangle</param>
        /// <param name="height">Height of the rectangle</param>
        /// <remarks>
        /// This function is intended for quickly filling the backround. Nothing
        /// else really.
        /// </remarks>
        public void DrawRectangle(double x, double y, double width, double height)
        {
            W32.rectangle(_cr, x, y, width, height);
            if (_mask == null)
                W32.fill(_cr);
            else
                MaskFill(_cr, false);
            if (IsTrackingShape)
            {
                W32.rectangle(_shape_cr, x, y, width, height);
                if (_mask == null)
                    W32.fill(_shape_cr);
                else
                    MaskFill(_shape_cr, false);
            }
        }

        /// <summary>
        /// Clips a rectangle
        /// </summary>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Width of the rectangle</param>
        /// <param name="height">Height of the rectangle</param>
        public void ClipRectangle(double x, double y, double width, double height)
        {
            W32.rectangle(_cr, x, y, width, height);
            W32.clip(_cr);
            if (IsTrackingShape)
            {
                W32.rectangle(_cr, x, y, width, height);
                W32.clip(_cr);
            }
        }

        /// <summary>
        /// Pops the state
        /// </summary>
        public void Pop()
        {
            Restore();
        }

        public void Clip()
        {
            W32.clip(_cr);
            if (IsTrackingShape)
                W32.clip(_shape_cr);
        }

        /// <summary>
        /// Resets clip, but not mask
        /// </summary>
        public void ClipReset()
        {
            ResetClip();
        }

        public void Fill()
        {
            if (_mask == null)
                W32.fill(_cr);
            else
                MaskFill(_cr, false);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.fill(_shape_cr);
                else
                    MaskFill(_shape_cr, false);
            }
        }

        private void MaskFill(IntPtr cairo, bool preserv)
        {
            W32.push_group(cairo);
            if (preserv)
                W32.fill_preserve(cairo);
            else
                W32.fill(cairo);
            W32.pop_group_to_source(cairo);
            W32.save(cairo);
            W32.set_matrix(cairo, ref _mask_matrix);
            W32.mask(cairo, _mask.Pattern);
            W32.restore(cairo);
        }

        public void Stroke()
        {
            if (_mask == null)
                W32.stroke(_cr);
            else
                MaskStroke(_cr, false);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.stroke(_shape_cr);
                else
                    MaskStroke(_shape_cr, false);
            }
        }

        private void MaskStroke(IntPtr cairo, bool preserv)
        {
            W32.push_group(cairo);
            if (preserv)
                W32.stroke_preserve(cairo);
            else
                W32.stroke(cairo);
            W32.pop_group_to_source(cairo);
            W32.save(cairo);
            W32.set_matrix(cairo, ref _mask_matrix);
            W32.mask(cairo, _mask.Pattern);
            W32.restore(cairo);
        }

        public void FillPreserve()
        {
            if (_mask == null)
                W32.fill_preserve(_cr);
            else
                MaskFill(_cr, true);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.fill_preserve(_shape_cr);
                else
                    MaskFill(_shape_cr, true);
            }
        }

        public void StrokePreserve()
        {
            if (_mask == null)
                W32.stroke_preserve(_cr);
            else
                MaskStroke(_cr, true);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.stroke_preserve(_shape_cr);
                else
                    MaskStroke(_shape_cr, true);
            }
        }

        public void ClipPreserve()
        {
            W32.clip_preserve(_cr);
            if (IsTrackingShape)
                W32.clip_preserve(_shape_cr);
        }

        public void MoveTo(double x, double y)
        {
            W32.move_to(_cr, x, y);
            if (IsTrackingShape)
                W32.move_to(_shape_cr, x, y);
        }

        public void LineTo(double x, double y)
        {
            W32.line_to(_cr, x, y);
            if (IsTrackingShape)
                W32.line_to(_shape_cr, x, y);
        }

        /// <summary>
        /// Quadratic Curve to
        /// </summary>
        /// <remarks>
        /// See: http://web.archive.org/web/20020209100930/http://www.icce.rug.nl/erikjan/bluefuzz/beziers/beziers/node2.html
        /// </remarks>
        public void CurveTo(double x1, double y1, double x2, double y2)
        {
            double x3 = x2, y3 = y2;
            var cp = CurrentPoint;

            x2 = x1 + (x2 - x1) / 3;
            y2 = y1 + (y2 - y1) / 3;
            x1 = cp.X + 2 * (x1 - cp.X) / 3;
            y1 = cp.Y + 2 * (y1 - cp.Y) / 3;

            W32.curve_to(_cr, x1, y1, x2, y2, x3, y3);
            if (IsTrackingShape)
                W32.curve_to(_shape_cr, x1, y1, x2, y2, x3, y3);
        }

        /// <summary>
        /// Cubic curve to
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            W32.curve_to(_cr, x1, y1, x2, y2, x3, y3);
            if (IsTrackingShape)
                W32.curve_to(_shape_cr, x1, y1, x2, y2, x3, y3);
        }

        public void ClosePath()
        {
            W32.close_path(_cr);
            if (IsTrackingShape)
                W32.close_path(_shape_cr);
        }

        public void Save()
        {
            W32.save(_cr);
            _mask_stack.Push(new PatAndMat(_mask, _mask_matrix, _mask_dispose));
            _mask_dispose = false;
            if (IsTrackingShape)
            {
                W32.save(_shape_cr);
                _shape_depth++;
            }
        }

        public void Restore()
        {
            W32.restore(_cr);
            if (_mask_dispose)
                _mask.Dispose();
            var pam = _mask_stack.Pop();
            _mask = pam.Pattern;
            _mask_matrix = pam.Matrix;
            _mask_dispose = pam.Destroy;
            if (IsTrackingShape)
            {
                if (_shape_depth == 0)
                    IsTrackingShape = false;
                else
                {
                    _shape_depth--;
                    W32.restore(_shape_cr);
                }
            }
        }

        public void DrawImage(rImage image, xRect r)
        {
            DrawImage(image, r.LowerLeft.X, r.LowerLeft.Y, r.Width, r.Height);
        }

        public void DrawImage(rImage image, double x, double y, double w, double h)
        {
            //Must save as I set the source
            Save();

            GCHandle gch = GCHandle.Alloc(image.RawData, GCHandleType.Pinned);
            var imgptr = gch.AddrOfPinnedObject();
            var surface = W32.image_surface_create_for_data(imgptr, (Enums.Format) image.Format, image.Width, image.Height, image.Stride);
            W32.set_source_surface(_cr, surface, x, y);
            if (IsTrackingShape)
                W32.set_source_surface(_shape_cr, surface, x, y);
            DrawRectangle(x, y, w, h);
            Restore();
            cSurface.W32.destroy(surface);
            gch.Free();
        }

        public void DrawImage(rImage image, double x, double y, double w, double h, Enums.Filter filter)
        {
            //Must save as I set the source
            Save();

            GCHandle gch = GCHandle.Alloc(image.RawData, GCHandleType.Pinned);
            var imgptr = gch.AddrOfPinnedObject();
            var surface = W32.image_surface_create_for_data(imgptr, (Enums.Format) image.Format, image.Width, image.Height, image.Stride);
            W32.set_source_surface(_cr, surface, 0, 0);
            cPattern.W32.set_filter(W32.get_source(_cr), Enums.Filter.NEAREST);
            if (IsTrackingShape)
            {
                W32.set_source_surface(_shape_cr, surface, x, y);
                cPattern.W32.set_filter(W32.get_source(_shape_cr), Enums.Filter.NEAREST);
            }
            DrawRectangle(x, y, w, h);

            Restore();
            cSurface.W32.destroy(surface);
            gch.Free();
        }

        public void MaskImage(rImage image, cPattern pat)
        {
            if (_mask != null)
                //1. PushGroup, draw as normal with "pat"
                //2. PopGroupToSource and then call Fill() or a fixed up "Paint"
                throw new NotImplementedException();

            //Must save as I set the source
            Save();

            GCHandle gch = GCHandle.Alloc(image.RawData, GCHandleType.Pinned);
            var imgptr = gch.AddrOfPinnedObject();
            var surface = W32.image_surface_create_for_data(imgptr, (Enums.Format) image.Format, image.Width, image.Height, image.Stride);
            W32.set_source_surface(_cr, surface, 0, 0);
            W32.mask(_cr, pat.Pattern);
            if (IsTrackingShape)
            {
                W32.set_source_surface(_shape_cr, surface, 0, 0);
                W32.mask(_shape_cr, pat.Pattern);
            }
            Restore();
            cSurface.W32.destroy(surface);
            gch.Free();
        }

        public void PushGroupAlpha()
        {
            W32.push_group_with_content(_cr, Enums.Content_type.CAIRO_CONTENT_ALPHA);
            if (IsTrackingShape)
            {
                _shape_depth++;
                W32.push_group(_shape_cr);
            }
        }

        public void PushGroupWithAlpha()
        {
            W32.push_group_with_content(_cr, Enums.Content_type.CAIRO_CONTENT_COLOR_ALPHA);
            if (IsTrackingShape)
            {
                _shape_depth++;
                W32.push_group(_shape_cr);
            }
        }

        /// <summary>
        /// Temporarily redirects drawing to an intermediate surface known as a group
        /// </summary>
        public void PushGroup()
        {
            W32.push_group(_cr);
            if (IsTrackingShape)
            {
                _shape_depth++;
                W32.push_group(_shape_cr);
            }
        }

        public cPattern PopGroup()
        {
            if (IsTrackingShape)
            {
                throw new NotImplementedException();
            }
            return new cPattern(W32.pop_group(_cr));
        }

        public void PopGroupAndSetSource()
        {
            W32.pop_group_to_source(_cr);
            if (IsTrackingShape)
            {
                throw new NotImplementedException();
            }
        }

        public void Mask(cPattern pat)
        {
            if (_mask == null)
                W32.mask(_cr, pat.Pattern);
            else
                MaskMask(_cr, pat);
            W32.new_path(_cr);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.mask(_shape_cr, pat.Pattern);
                else
                    MaskMask(_shape_cr, pat);
                W32.new_path(_shape_cr);
            }
        }

        private void MaskMask(IntPtr cairo, cPattern pat)
        {
            W32.push_group(cairo);
            W32.mask(cairo, pat.Pattern);
            W32.pop_group_to_source(cairo);
            W32.save(cairo);
            W32.set_matrix(cairo, ref _mask_matrix);
            W32.mask(cairo, _mask.Pattern);
            W32.restore(cairo);
        }

        public void Paint()
        {
            if (_mask == null)
                W32.paint(_cr);
            else
                W32.mask(_cr, _mask.Pattern);
            if (IsTrackingShape)
            {
                if (_mask == null)
                    W32.paint(_shape_cr);
                else
                    W32.mask(_shape_cr, _mask.Pattern);
            }
        }

        /// <summary>
        /// Transforms a recatangle to device space
        /// </summary>
        public xRect Transform(xRect r)
        {
            var p1 = Transform(r.LowerLeft);
            var p2 = Transform(r.UpperRight);
            return new xRect(p1.X, p1.Y, p2.X, p2.Y);
        }

        /// <summary>
        /// Transforms a recatangle from device to user space
        /// </summary>
        public xRect TransformFromDevice(xRect r)
        {
            var p1 = TransformFromDevice(r.LowerLeft);
            var p2 = TransformFromDevice(r.UpperRight);
            return new xRect(p1.X, p1.Y, p2.X, p2.Y);
        }

        /// <summary>
        /// Transforms a point to device metrics
        /// </summary>
        /// <param name="p">The point to transform</param>
        /// <returns>Transformed point</returns>
        public xPoint Transform(xPoint p)
        {
            double x = p.X, y = p.Y;
            W32.user_to_device(_cr, ref x, ref y);
            return new xPoint(x, y);
        }

        /// <summary>
        /// Transforms a point from device to user space
        /// </summary>
        /// <param name="p">The point to transform</param>
        /// <returns>Transformed point</returns>
        public xPoint TransformFromDevice(xPoint p)
        {
            double x = p.X, y = p.Y;
            W32.device_to_user(_cr, ref x, ref y);
            return new xPoint(x, y);
        }

        /// <summary>
        /// Transforms a point to device metrics, ignores offsets
        /// </summary>
        /// <param name="p">The point to transform</param>
        /// <returns>Transformed point</returns>
        public xPoint TransformDistance(xPoint p)
        {
            double x = p.X, y = p.Y;
            W32.user_to_device_distance(_cr, ref x, ref y);
            return new xPoint(x, y);
        }

        public xPoint TransformFromDeviceDistance(xPoint p)
        {
            double x = p.X, y = p.Y;
            W32.device_to_user_distance(_cr, ref x, ref y);
            return new xPoint(x, y);
        }

        public xVector TransformFromDeviceDistance(xVector p)
        {
            double x = p.X, y = p.Y;
            W32.device_to_user_distance(_cr, ref x, ref y);
            return new xVector(x, y);
        }

        internal static void DestroyPattern(cPattern pat)
        {
            W32.pattern_destroy(pat.Pattern);
        }

        internal static void DestroyPath(cPath pat)
        {
            W32.path_destroy(pat.Path);
        }

        public cPath CopyPath()
        {
            return new cPath(W32.copy_path(_cr));
        }

        public void NewPath()
        {
            W32.new_path(_cr);
            if (IsTrackingShape)
                W32.new_path(_shape_cr);
        }

        public void AppendPath(cPath path)
        {
            W32.append_path(_cr, path.Path);
            if (IsTrackingShape)
                W32.append_path(_shape_cr, path.Path);
        }

        internal void DrawLine(xPoint from, xPoint to, double width)
        {
            MoveTo(from.X, from.Y);
            LineTo(to.X, to.Y);
            W32.set_line_width(_cr, width);
            W32.stroke(_cr);
            if (IsTrackingShape)
            {
                W32.set_line_width(_shape_cr, width);
                W32.stroke(_shape_cr);
            }
        }

        internal void DrawCircle(double x, double y, double radius)
        {
            W32.arc(_cr, x, y, radius, 0, 2 * Math.PI);
            W32.fill(_cr);
            if (IsTrackingShape)
            {
                W32.arc(_shape_cr, x, y, radius, 0, 2 * Math.PI);
                W32.fill(_shape_cr);
            }
        }

        internal void CircleAt(double x, double y, double radius)
        {
            W32.arc(_cr, x, y, radius, 0, 2 * Math.PI);
            if (IsTrackingShape)
                W32.arc(_shape_cr, x, y, radius, 0, 2 * Math.PI);
        }

        /// <summary>
        /// Removes the clipping region
        /// </summary>
        public void ResetClip()
        {
            W32.reset_clip(_cr);
            if (IsTrackingShape)
                W32.reset_clip(_shape_cr);
        }

        public cRadiousPattern CreateRadialPattern(double x0, double y0, double radius0, double x1, double y1, double radius1)
        {
            return new cRadiousPattern(W32.pattern_create_radial(x0, y0, radius0, x1, y1, radius1));
        }

        public cLinearPattern CreateRadialPattern(double x0, double y0, double x1, double y1)
        {
            return new cLinearPattern(x0, y0, x1, y1);
        }

        #region Helper classes

        struct PatAndMat
        {
            public cPattern Pattern;
            public cMatrix Matrix;
            public bool Destroy;
            public PatAndMat(cPattern p, cMatrix m, bool d)
            { Pattern = p; Matrix = m; Destroy = d; }
        }

        #endregion

        #region W32

        static class W32
        {
            //static W32()
            //{
            //    var myPath = new Uri(typeof(W32).Assembly.CodeBase).LocalPath;
            //    var myFolder = System.IO.Path.GetDirectoryName(myPath);

            //    var is64 = IntPtr.Size == 8;
            //    var subfolder = is64 ? "\\win64\\" : "\\win32\\";

            //    LoadLibrary(myFolder + subfolder + "libcairo-2.dll");
            //}

            static W32()
            {
                var myPath = new Uri(typeof(W32).Assembly.CodeBase).LocalPath;
                var myFolder = System.IO.Path.GetDirectoryName(myPath);

                var is64 = IntPtr.Size == 8;
                var subfolder = is64 ? "\\win64\\" : "\\win32\\";

                bool ok = SetDllDirectory(myFolder + subfolder);
                if (!ok) throw new System.ComponentModel.Win32Exception();
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool SetDllDirectory(string path);

            [DllImport("kernel32.dll")]
            private static extern IntPtr LoadLibrary(string dllToLoad);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_paint", CallingConvention = CallingConvention.Cdecl)]
            public static extern void paint(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_paint_with_alpha", CallingConvention = CallingConvention.Cdecl)]
            public static extern void paint_with_alpha(IntPtr cr, double alpha);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_create_radial", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr pattern_create_radial(double cx0, double cy0, double radius0, double cx1, double cy1, double radius1);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_reset_clip", CallingConvention = CallingConvention.Cdecl)]
            public static extern void reset_clip(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_arc", CallingConvention = CallingConvention.Cdecl)]
            public static extern void arc(IntPtr cr, double xc, double yc, double radius, double angle1, double angle2);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_clip_extents", CallingConvention = CallingConvention.Cdecl)]
            public static extern void clip_extents(IntPtr cr, ref double x1, ref double y1, ref double x2, ref double y2);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_append_path", CallingConvention = CallingConvention.Cdecl)]
            public static extern void append_path(IntPtr cr, IntPtr path);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_new_path", CallingConvention = CallingConvention.Cdecl)]
            public static extern void new_path(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_path_destroy", CallingConvention = CallingConvention.Cdecl)]
            public static extern void path_destroy(IntPtr path);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_copy_path", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr copy_path(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_device_to_user", CallingConvention = CallingConvention.Cdecl)]
            public static extern void device_to_user(IntPtr cr, ref double dx, ref double dy);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_device_to_user_distance", CallingConvention = CallingConvention.Cdecl)]
            public static extern void device_to_user_distance(IntPtr cr, ref double dx, ref double dy);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_user_to_device_distance", CallingConvention = CallingConvention.Cdecl)]
            public static extern void user_to_device_distance(IntPtr cr, ref double dx, ref double dy);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_user_to_device", CallingConvention = CallingConvention.Cdecl)]
            public static extern void user_to_device(IntPtr cr, ref double x, ref double y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_stroke_extents", CallingConvention = CallingConvention.Cdecl)]
            public static extern void stroke_extents(IntPtr cr, ref double x1, ref double y1, ref double x2, ref double y2);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_fill_extents", CallingConvention = CallingConvention.Cdecl)]
            public static extern void fill_extents(IntPtr cr, ref double x1, ref double y1, ref double x2, ref double y2);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_path_extents", CallingConvention = CallingConvention.Cdecl)]
            public static extern void path_extents(IntPtr cr, ref double x1, ref double y1, ref double x2, ref double y2);

            //Return Type: cairo_surface_t*
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_target", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr get_target(IntPtr cr);

            //Return Type: cairo_pattern_t*
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_source", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr get_source(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_status", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.Status status(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_antialias", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_antialias(IntPtr cr, Enums.AntialiasMethod antialias);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_antialias", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.AntialiasMethod get_antialias(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_mask", CallingConvention = CallingConvention.Cdecl)]
            public static extern void mask(IntPtr cr, IntPtr pattern);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_mask_surface", CallingConvention = CallingConvention.Cdecl)]
            public static extern void mask_surface(IntPtr cr, IntPtr surface, double surface_x, double surface_y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_destroy", CallingConvention = CallingConvention.Cdecl)]
            public static extern void pattern_destroy(IntPtr pattern);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_push_group", CallingConvention = CallingConvention.Cdecl)]
            public static extern void push_group(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_push_group_with_content", CallingConvention = CallingConvention.Cdecl)]
            public static extern void push_group_with_content(IntPtr cr, Enums.Content_type content);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pop_group", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr pop_group(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pop_group_to_source", CallingConvention = CallingConvention.Cdecl)]
            public static extern void pop_group_to_source(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_has_current_point", CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAsAttribute(UnmanagedType.I1)]
            public static extern bool has_current_point(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_current_point", CallingConvention = CallingConvention.Cdecl)]
            public static extern void get_current_point(IntPtr cr, ref double x, ref double y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_close_path", CallingConvention = CallingConvention.Cdecl)]
            public static extern void close_path(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_curve_to", CallingConvention = CallingConvention.Cdecl)]
            public static extern void curve_to(IntPtr cr, double x1, double y1, double x2, double y2, double x3, double y3);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_dash", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_dash(IntPtr cr, double[] dashes, int num_dashes, double offset);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_dash_count", CallingConvention = CallingConvention.Cdecl)]
            public static extern int get_dash_count(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_dash", CallingConvention = CallingConvention.Cdecl)]
            public static extern void get_dash(IntPtr cr, double[] dashes, ref double offset);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_line_cap", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.LineCap get_line_cap(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_line_cap", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_line_cap(IntPtr cr, Enums.LineCap cap);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_line_join", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.LineJoin get_line_join(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_line_join", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_line_join(IntPtr cr, Enums.LineJoin cap);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_line_width", CallingConvention = CallingConvention.Cdecl)]
            public static extern double get_line_width(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_miter_limit", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_miter_limit(IntPtr cr, double miter);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_miter_limit", CallingConvention = CallingConvention.Cdecl)]
            public static extern double get_miter_limit(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_line_width", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_line_width(IntPtr cr, double width);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_operator", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_operator(IntPtr cr, Enums.BlendMode op);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_operator", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.BlendMode get_operator(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_stroke", CallingConvention = CallingConvention.Cdecl)]
            public static extern void stroke(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_stroke_preserve", CallingConvention = CallingConvention.Cdecl)]
            public static extern void stroke_preserve(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_line_to", CallingConvention = CallingConvention.Cdecl)]
            public static extern void line_to(IntPtr cr, double x, double y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_move_to", CallingConvention = CallingConvention.Cdecl)]
            public static extern void move_to(IntPtr cr, double x, double y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_fill_rule", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_fill_rule(IntPtr cr, Enums.FillRule fill_rule);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_fill_rule", CallingConvention = CallingConvention.Cdecl)]
            public static extern Enums.FillRule get_fill_rule(IntPtr cr);

            //[DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_stride", CallingConvention = CallingConvention.Cdecl)]
            //public static extern int image_surface_get_stride(IntPtr surface);

            //[DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_data", CallingConvention = CallingConvention.Cdecl)]
            //public static extern IntPtr image_surface_get_data(IntPtr surface);

            //[DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_width", CallingConvention = CallingConvention.Cdecl)]
            //public static extern int image_surface_get_width(IntPtr surface);

            //[DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_height", CallingConvention = CallingConvention.Cdecl)]
            //public static extern int image_surface_get_height(IntPtr surface);

            //[DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_format", CallingConvention = CallingConvention.Cdecl)]
            //public static extern Enums.Format image_surface_get_format(IntPtr surface);

            /// <summary>
            /// Creates a cairo context
            /// </summary>
            /// <param name="target">Surface</param>
            /// <returns>The cairo context</returns>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_create", CallingConvention = CallingConvention.Cdecl)]
            //public static extern System.IntPtr cairo_create(ref cairo_surface_t target);
            public static extern IntPtr create(IntPtr target);

            /// <summary>
            /// Destroys a cairo context
            /// </summary>
            /// <param name="cr">The context</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_destroy", CallingConvention = CallingConvention.Cdecl)]
            //public static extern void cairo_destroy(ref cairo_t cr);
            public static extern void destroy(IntPtr cr);

            /// <summary>
            /// Creates a drawable surface of the desired format
            /// </summary>
            /// <param name="format">Format of the cairo surface</param>
            /// <param name="width">Width in pixels</param>
            /// <param name="height">Height in pixels</param>
            /// <returns>A pointer to a cairo_surface type object</returns>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_create", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr image_surface_create(Enums.Format format, int width, int height);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_create_for_data", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr image_surface_create_for_data(IntPtr data, Enums.Format format, int width, int height, int stride);

            /// <summary>
            /// Saves the current state onto a stack
            /// </summary>
            /// <param name="cr">cairo_t</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_save", CallingConvention = CallingConvention.Cdecl)]
            public static extern void save(IntPtr cr);

            /// <summary>
            /// Restores the state back to the previous state
            /// </summary>
            /// <param name="cr">cairo_t</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_restore", CallingConvention = CallingConvention.Cdecl)]
            public static extern void restore(IntPtr cr);

            /// <summary>
            /// Transforms the CTM
            /// </summary>
            /// <param name="cr">Graphics object</param>
            /// <param name="matrix">Matrix to prepend</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_transform", CallingConvention = CallingConvention.Cdecl)]
            public static extern void transform(IntPtr cr, ref cMatrix matrix);
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_transform", CallingConvention = CallingConvention.Cdecl)]
            public static extern void transform(IntPtr cr, IntPtr matrix);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_get_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void get_matrix(IntPtr cr, ref cMatrix matrix);

            /// <summary>
            /// Sets the CTM regardless of previous settings
            /// </summary>
            /// <param name="cr">Graphics object</param>
            /// <param name="matrix">Matrix to set</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_matrix(IntPtr cr, ref cMatrix matrix);
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_matrix(IntPtr cr, IntPtr matrix);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_rectangle", CallingConvention = CallingConvention.Cdecl)]
            public static extern void rectangle(IntPtr cr, double x, double y, double width, double height);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_clip", CallingConvention = CallingConvention.Cdecl)]
            public static extern void clip(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_clip_preserve", CallingConvention = CallingConvention.Cdecl)]
            public static extern void clip_preserve(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_fill", CallingConvention = CallingConvention.Cdecl)]
            public static extern void fill(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_fill_preserve", CallingConvention = CallingConvention.Cdecl)]
            public static extern void fill_preserve(IntPtr cr);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_source", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_source(IntPtr cr, IntPtr source);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_source_surface", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_source_surface(IntPtr cr, IntPtr surface, double x, double y);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_source_rgba", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_source_rgba(IntPtr cr, double red, double green, double blue, double alpha);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_set_source_rgb", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_source_rgb(IntPtr cr, double red, double green, double blue);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_version_string", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr version_string();
        }

        public static class Enums
        {
            public enum Status
            {
                SUCCESS = 0,
                NO_MEMORY,
                INVALID_RESTORE,
                INVALID_POP_GROUP,
                NO_CURRENT_POINT,
                INVALID_MATRIX,
                INVALID_STATUS,
                NULL_POINTER,
                INVALID_STRING,
                INVALID_PATH_DATA,
                READ_ERROR,
                WRITE_ERROR,
                SURFACE_FINISHED,
                SURFACE_TYPE_MISMATCH,
                PATTERN_TYPE_MISMATCH,
                INVALID_CONTENT,
                INVALID_FORMAT,
                INVALID_VISUAL,
                FILE_NOT_FOUND,
                INVALID_DASH,
                INVALID_DSC_COMMENT,
                INVALID_INDEX,
                CLIP_NOT_REPRESENTABLE,
                TEMP_FILE_ERROR,
                INVALID_STRIDE,
                FONT_TYPE_MISMATCH,
                USER_FONT_IMMUTABLE,
                USER_FONT_ERROR,
                NEGATIVE_COUNT,
                INVALID_CLUSTERS,
                INVALID_SLANT,
                INVALID_WEIGHT,
                INVALID_SIZE,
                USER_FONT_NOT_IMPLEMENTED,
                DEVICE_TYPE_MISMATCH,
                DEVICE_ERROR,
                INVALID_MESH_CONSTRUCTION,
                DEVICE_FINISHED,
                LAST_STATUS,
            }

            public enum SurfaceType
            {
                CAIRO_SURFACE_TYPE_IMAGE,
                CAIRO_SURFACE_TYPE_PDF,
                CAIRO_SURFACE_TYPE_PS,
                CAIRO_SURFACE_TYPE_XLIB,
                CAIRO_SURFACE_TYPE_XCB,
                CAIRO_SURFACE_TYPE_GLITZ,
                CAIRO_SURFACE_TYPE_QUARTZ,
                CAIRO_SURFACE_TYPE_WIN32,
                CAIRO_SURFACE_TYPE_BEOS,
                CAIRO_SURFACE_TYPE_DIRECTFB,
                CAIRO_SURFACE_TYPE_SVG,
                CAIRO_SURFACE_TYPE_OS2,
                CAIRO_SURFACE_TYPE_WIN32_PRINTING,
                CAIRO_SURFACE_TYPE_QUARTZ_IMAGE,
                CAIRO_SURFACE_TYPE_SCRIPT,
                CAIRO_SURFACE_TYPE_QT,
                CAIRO_SURFACE_TYPE_RECORDING,
                CAIRO_SURFACE_TYPE_VG,
                CAIRO_SURFACE_TYPE_GL,
                CAIRO_SURFACE_TYPE_DRM,
                CAIRO_SURFACE_TYPE_TEE,
                CAIRO_SURFACE_TYPE_XML,
                CAIRO_SURFACE_TYPE_SKIA,
                CAIRO_SURFACE_TYPE_SUBSURFACE,
                CAIRO_SURFACE_TYPE_COGL
            }

            public enum cPathDataType
            {

                CAIRO_PATH_MOVE_TO,
                CAIRO_PATH_LINE_TO,
                CAIRO_PATH_CURVE_TO,
                CAIRO_PATH_CLOSE_PATH,
            }

            public enum LineJoin
            {
                CAIRO_LINE_JOIN_MITER,
                CAIRO_LINE_JOIN_ROUND,
                CAIRO_LINE_JOIN_BEVEL,
            }

            public enum LineCap
            {
                CAIRO_LINE_CAP_BUTT,
                CAIRO_LINE_CAP_ROUND,
                CAIRO_LINE_CAP_SQUARE,
            }

            public enum ClipMode
            {
                CAIRO_CLIP_MODE_PATH,
                CAIRO_CLIP_MODE_REGION,
                CAIRO_CLIP_MODE_MASK,
            }

            public enum cairo_path_op
            {
                /// CAIRO_PATH_OP_MOVE_TO -> 0
                CAIRO_PATH_OP_MOVE_TO = 0,

                /// CAIRO_PATH_OP_LINE_TO -> 1
                CAIRO_PATH_OP_LINE_TO = 1,

                /// CAIRO_PATH_OP_CURVE_TO -> 2
                CAIRO_PATH_OP_CURVE_TO = 2,

                /// CAIRO_PATH_OP_CLOSE_PATH -> 3
                CAIRO_PATH_OP_CLOSE_PATH = 3,
            }

            public enum cairo_subpixel_order_t
            {
                CAIRO_SUBPIXEL_ORDER_DEFAULT,
                CAIRO_SUBPIXEL_ORDER_RGB,
                CAIRO_SUBPIXEL_ORDER_BGR,
                CAIRO_SUBPIXEL_ORDER_VRGB,
                CAIRO_SUBPIXEL_ORDER_VBGR,
            }

            public enum cairo_hint_style_t
            {
                CAIRO_HINT_STYLE_DEFAULT,
                CAIRO_HINT_STYLE_NONE,
                CAIRO_HINT_STYLE_SLIGHT,
                CAIRO_HINT_STYLE_MEDIUM,
                CAIRO_HINT_STYLE_FULL,
            }

            public enum cairo_hint_metrics_t
            {
                CAIRO_HINT_METRICS_DEFAULT,
                CAIRO_HINT_METRICS_OFF,
                CAIRO_HINT_METRICS_ON,
            }

            public enum Format
            {
                INVALID = -1,
                ARGB32 = 0,
                RGB24 = 1,
                A8 = 2,
                A1 = 3,
                RGB16_565 = 4,
                RGB30 = 5,
            }

            public enum cairo_font_slant_t
            {
                CAIRO_FONT_SLANT_NORMAL,
                CAIRO_FONT_SLANT_ITALIC,
                CAIRO_FONT_SLANT_OBLIQUE,
            }

            public enum cairo_font_weight_t
            {
                CAIRO_FONT_WEIGHT_NORMAL,
                CAIRO_FONT_WEIGHT_BOLD,
            }

            public enum Filter
            {
                FAST,
                GOOD,
                BEST,
                NEAREST,
                BILINEAR,

                /// <summary>
                /// Not implemented
                /// </summary>
                GAUSSIAN,
            }

            public enum ExtendMethod
            {
                NONE,
                REPEAT,
                REFLECT,
                PAD
            }

            public enum PatternType
            {
                SOLID,
                SURFACE,
                LINEAR,
                RADIAL,
            }

            public enum FillRule
            {
                WINDING,
                EVEN_ODD,
            }

            public enum AntialiasMethod
            {
                DEFAULT,
                NONE,
                GRAY,
                SUBPIXEL,
                FAST,
                GOOD,
                BEST
            }

            public enum BlendMode
            {
                CLEAR,
                SOURCE,
                OVER,
                IN,
                OUT,
                ATOP,
                DEST,
                DEST_OVER,
                DEST_IN,
                DEST_OUT,
                DEST_ATOP,
                XOR,
                ADD,
                SATURATE,
                MULTIPLY,
                SCREEN,
                OVERLAY,
                DARKEN,
                LIGHTEN,
                COLOR_DODGE,
                COLOR_BURN,
                HARD_LIGHT,
                SOFT_LIGHT,
                DIFFERENCE,
                EXCLUSION,
                HSL_HUE,
                HSL_SATURATION,
                HSL_COLOR,
                HSL_LUMINOSITY,
            }

            public enum Content_type
            {
                /// CAIRO_CONTENT_COLOR -> 0x1000
                CAIRO_CONTENT_COLOR = 4096,

                /// CAIRO_CONTENT_ALPHA -> 0x2000
                CAIRO_CONTENT_ALPHA = 8192,

                /// CAIRO_CONTENT_COLOR_ALPHA -> 0x3000
                CAIRO_CONTENT_COLOR_ALPHA = 12288,
            }

            public enum cairo_int_status
            {
                /// CAIRO_INT_STATUS_DEGENERATE -> 1000
                CAIRO_INT_STATUS_DEGENERATE = 1000,
                CAIRO_INT_STATUS_UNSUPPORTED,
                CAIRO_INT_STATUS_NOTHING_TO_DO,
            }
        }

        #endregion
    }
}
