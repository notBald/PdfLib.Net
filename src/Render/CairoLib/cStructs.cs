using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.CairoLib
{
    [StructLayoutAttribute(LayoutKind.Sequential)]
    public struct cMatrix
    {
        public static cMatrix Identity { get { return new cMatrix(1, 0, 0, 1, 0, 0); } }

        public double XX;
        public double YX;
        public double XY;
        public double YY;
        public double X0;
        public double Y0;

        public double M11 { get { return XX; } }
        public double M12 { get { return YX; } }
        public double M21 { get { return XY; } }
        public double M22 { get { return YY; } }
        public double OffsetX { get { return X0; } }
        public double OffsetY { get { return Y0; } }

        /// <summary>
        /// Creates a scale matrix
        /// </summary>
        public cMatrix(double scale_x, double scale_y)
        {
            XX = scale_x;
            YX = 0;
            XY = 0;
            YY = scale_y;
            X0 = 0;
            Y0 = 0;
        }

        /// <summary>
        /// Creates an matrix of the spesific values
        /// </summary>
        public cMatrix(double xx, double yx, double xy, double yy, double x0, double y0)
        {
            XX = xx;
            YX = yx;
            XY = xy;
            YY = yy;
            X0 = x0;
            Y0 = y0;
        }

        public cMatrix(xMatrix m)
            : this(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY)
        { }

        /// <summary>
        /// Transforms a recatangle to device space
        /// </summary>
        public xRect Transform(xRect r)
        {
            var p1 = Transform(r.LowerLeft);
            var p2 = Transform(r.UpperRight);
            var p3 = Transform(new xPoint(r.LowerLeft.X, r.UpperRight.Y));
            var p4 = Transform(new xPoint(r.UpperRight.X, r.LowerLeft.Y));
            var low_Y = Math.Min(p1.Y, Math.Min(p2.Y, Math.Min(p3.Y, p4.Y)));
            var low_x = Math.Min(p1.X, Math.Min(p2.X, Math.Min(p3.X, p4.X)));
            var high_Y = Math.Max(p1.Y, Math.Max(p2.Y, Math.Max(p3.Y, p4.Y)));
            var high_x = Math.Max(p1.X, Math.Max(p2.X, Math.Max(p3.X, p4.X)));
            return new xRect(low_x, low_Y, high_x, high_Y);
        }

        public void Prepend(cMatrix mat)
        {
            W32.multiply(ref this, ref mat, ref this);
        }

        /// <summary>
        /// Transforms a point to device metrics
        /// </summary>
        /// <param name="p">The point to transform</param>
        /// <returns>Transformed point</returns>
        public xPoint Transform(xPoint p)
        {
            double x = p.X, y = p.Y;
            W32.transform_point(ref this, ref x, ref y);
            return new xPoint(x, y);
        }

        /// <summary>
        /// Transforms a point to device metrics
        /// </summary>
        /// <param name="p">The point to transform</param>
        /// <returns>Transformed point</returns>
        public cPoint Transform(cPoint p)
        {
            double x = p.X, y = p.Y;
            W32.transform_point(ref this, ref x, ref y);
            return new cPoint(x, y);
        }

        public void TranslatePrepend(double x, double y)
        {
            Prepend(new cMatrix(1, 0, 0, 1, x, y));
        }

        public void Translate(double x, double y)
        {
            W32.translate(ref this, x, y);
        }

        public void Translate(xPoint p)
        {
            W32.translate(ref this, p.X, p.Y);
        }

        public void Invert()
        {
            if (W32.invert(ref this) != Cairo.Enums.Status.SUCCESS)
                throw new PdfInternalException("Matrix inversion failed");
        }

        public cMatrix Clone()
        {
            return new cMatrix(XX, YX, XY, YY, X0, Y0);
        }

        public override string ToString()
        {
            return string.Format("{0:0.##};{1:0.##};{2:0.##};{3:0.##};{4:0.##};{5:0.##}", XX, YX, XY, YY, X0, Y0);
        }

        static class W32
        {
            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_matrix_translate", CallingConvention = CallingConvention.Cdecl)]
            public static extern void translate(ref cMatrix matrix, double tx, double ty);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_matrix_transform_point", CallingConvention = CallingConvention.Cdecl)]
            public static extern void transform_point(ref cMatrix matrix, ref double x, ref double y);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_matrix_invert", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.Status invert(ref cMatrix matrix);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_matrix_multiply", CallingConvention = CallingConvention.Cdecl)]
            public static extern void multiply(ref cMatrix result, ref cMatrix a, ref cMatrix b);
        }
    }

    /// <summary>
    /// Don't want plain IntPtr outside "Cairo"
    /// </summary>
    public class cPath : IDisposable
    {
        public readonly IntPtr Path;

        public cPathPoints Data
        {
            get
            {
                return (cPathPoints)Marshal.PtrToStructure(Path, typeof(cPathPoints));
            }
        }

        internal cPath(IntPtr path)
        { Path = path; }

        ~cPath()
        {
            Destruct();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Destruct();
        }

        private void Destruct()
        {
            Cairo.DestroyPath(this);
        }
    }

    [StructLayoutAttribute(LayoutKind.Sequential)]
    public struct cPathPoints
    {
        public Cairo.Enums.Status status;

        public IntPtr data;
        public int num_data;

        public cPathData[] Points
        {
            get
            {
                var d = new cPathData[num_data];
                int size = Marshal.SizeOf(typeof(cPathData));
                long pos = data.ToInt64();
                for(int c=0; c < num_data; c++)
                    d[c] = (cPathData)Marshal.PtrToStructure(new IntPtr(pos + size * c), typeof(cPathData));
                return d;
            }
        }
    }

    [StructLayoutAttribute(LayoutKind.Sequential)]
    public struct cPathDataLength
    {
        public Cairo.Enums.cPathDataType type;
        public int length;
    }

    [StructLayoutAttribute(LayoutKind.Sequential)]
    public struct cPoint
    {
        public double X;
        public double Y;
        public cPoint(double x, double y) { X = x; Y = y; }
        public override string ToString()
        {
            return string.Format("{0} - {1}", X, Y);
        }
    }

    [StructLayoutAttribute(LayoutKind.Explicit)]
    public struct cPathData
    {
        [FieldOffsetAttribute(0)]
        public cPathDataLength header;

        [FieldOffsetAttribute(0)]
        public cPoint point;

        public override string ToString()
        {
            //This is just a guess
            if (Cairo.Enums.cPathDataType.CAIRO_PATH_MOVE_TO <= header.type && header.type <= Cairo.Enums.cPathDataType.CAIRO_PATH_CLOSE_PATH)
                return header.type.ToString();
            else
                return string.Format("{0} - {1}", point.X, point.Y);
        }
    }

    /// <summary>
    /// Don't want plain IntPtr outside "Cairo"
    /// </summary>
    public class cPattern : IDisposable
    {
        public IntPtr Pattern;

        /// <summary>
        /// How many references there are for this pattern
        /// </summary>
        public uint RefCount { get { return W32.get_reference_count(Pattern); } }

        /// <summary>
        /// The pattern's matrix
        /// </summary>
        public cMatrix Matrix
        {
            get
            {
                var cm = new cMatrix();
                W32.get_matrix(Pattern, ref cm);
                return cm;
            }
            set
            {
                var cm = value;
                W32.set_matrix(Pattern, ref cm);
            }
        }

        public Cairo.Enums.Filter Filter
        {
            get { return W32.get_filter(Pattern); }
            set { W32.set_filter(Pattern, value); }
        }

        /// <summary>
        /// Type of pattern
        /// </summary>
        public Cairo.Enums.PatternType Type
        { get { return W32.get_type(Pattern); } }

        public Cairo.Enums.ExtendMethod Extend
        {
            get { return W32.get_extend(Pattern); }
            set { W32.set_extend(Pattern, value); }
        }

        internal cPattern(IntPtr pattern)
        { Pattern = pattern; }

        ~cPattern()
        {
            Destruct();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Destruct();
        }

        private void Destruct()
        {
            if (Pattern != IntPtr.Zero)
            {
                Cairo.DestroyPattern(this);
                Pattern = IntPtr.Zero;
            }
        }

        internal static class W32
        {
            //Returns cPattern
            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_create_for_surface", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr create_for_surface(IntPtr surface);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_set_extend", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_extend(IntPtr pattern, Cairo.Enums.ExtendMethod extend);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_get_extend", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.ExtendMethod get_extend(IntPtr pattern);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_get_type", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.PatternType get_type(IntPtr pattern);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_add_color_stop_rgb", CallingConvention = CallingConvention.Cdecl)]
            public static extern void add_color_stop_rgb(IntPtr pattern, double offset, double red, double green, double blue);

            [DllImportAttribute("libcairo-2.dll", EntryPoint = "cairo_pattern_add_color_stop_rgba", CallingConvention = CallingConvention.Cdecl)]
            public static extern void add_color_stop_rgba(IntPtr pattern, double offset, double red, double green, double blue, double alpha);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_set_filter", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_filter(IntPtr pattern, Cairo.Enums.Filter filter);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_get_filter", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.Filter get_filter(IntPtr pattern);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_set_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_matrix(IntPtr pattern, IntPtr matrix);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_set_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_matrix(IntPtr pattern, ref cMatrix matrix);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_get_matrix", CallingConvention = CallingConvention.Cdecl)]
            public static extern void get_matrix(IntPtr pattern, ref cMatrix matrix);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_get_reference_count", CallingConvention = CallingConvention.Cdecl)]
            public static extern uint get_reference_count(IntPtr pattern);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_pattern_create_linear", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr create_linear(double x0, double y0, double x1, double y1);
        }
    }

    public class cRadiousPattern : cPattern
    {
        internal cRadiousPattern(IntPtr pattern)
            : base(pattern)
        { }

        public void AddColorStop(double red, double offset, double green, double blue)
        { W32.add_color_stop_rgb(Pattern, offset, red, green, blue); }

        public void AddColorStop(double red, double offset, double green, double blue, double alpha)
        { W32.add_color_stop_rgba(Pattern, offset, red, green, blue, alpha); }
    }

    public class cLinearPattern : cPattern
    {
        internal cLinearPattern(IntPtr pattern)
            : base(pattern)
        { }

        /// <summary>
        /// The coordinates here are in pattern space. For a new pattern, pattern space is identical to user space.
        /// </summary>
        /// <param name="x0">Start point</param>
        /// <param name="y0">Start point</param>
        /// <param name="x1">End point</param>
        /// <param name="y1">End poing</param>
        internal cLinearPattern(double x0, double y0, double x1, double y1)
            : base(W32.create_linear(x0, y0, x1, y1))
        { }

        public void AddColorStop(double offset, double red, double green, double blue)
        { W32.add_color_stop_rgb(Pattern, offset, red, green, blue); }

        public void AddColorStop(double offset, double red, double green, double blue, double alpha)
        { W32.add_color_stop_rgba(Pattern, offset, red, green, blue, alpha); }
    }

    public class cSurface : IDisposable
    {
        public IntPtr Surface;

        /// <summary>
        /// The reference count for a surface.
        /// </summary>
        internal int RefCount
        { 
            get 
            {
                if (Surface == IntPtr.Zero)
                    return 0;
                return W32.get_reference_count(Surface); 
            } 
        }

        /// <summary>
        /// Stride of the surface
        /// </summary>
        public int Stride
        {
            get { return cSurface.W32.image_surface_get_stride(Surface); }
        }

        /// <summary>
        /// Height of the surface
        /// </summary>
        public int Height
        {
            get { return W32.image_surface_get_height(Surface); }
        }

        /// <summary>
        /// Width of the surface
        /// </summary>
        public int Width
        {
            get { return W32.image_surface_get_width(Surface); }
        }

        /// <summary>
        /// Format of the surface
        /// </summary>
        public Cairo.Enums.Format Format
        {
            get { return W32.image_surface_get_format(Surface); }
        }

        public Cairo.Enums.SurfaceType SurfaceType
        {
            get { return W32.get_type(Surface); }
        }

        /// <summary>
        /// An offset that is added to the device coordinates determined by the CTM when drawing to surface.
        /// </summary>
        public cPoint DeviceOffset
        {
            get
            {
                double ox = 0, oy = 0;
                cSurface.W32.get_device_offset(Surface, ref ox, ref oy);
                return new cPoint(ox, oy);
            }
            set
            {
                cSurface.W32.set_device_offset(Surface, value.X, value.Y);
            }
        }

        internal cSurface(IntPtr surface)
        { Surface = surface; }

        ~cSurface()
        {
            Destroy();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Destroy();
        }

        private void Destroy()
        {
            if (Surface != IntPtr.Zero)
            {
                W32.destroy(Surface);
                Surface = IntPtr.Zero;
            }
        }

        internal static class W32
        {
            /// <summary>
            /// Do any pending drawing for the surface 
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_flush", CallingConvention = CallingConvention.Cdecl)]
            public static extern void flush(IntPtr surface);

            /// <summary>
            /// Frees memory assosiated with a surface
            /// </summary>
            /// <param name="surface">The surface object to free</param>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_destroy", CallingConvention = CallingConvention.Cdecl)]
            public static extern void destroy(IntPtr surface);

            /// <summary>
            /// The type of backend surface
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_get_type", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.SurfaceType get_type(IntPtr surface);

            /// <summary>
            /// Create a new image surface that is as compatible as possible for uploading 
            /// to and the use in conjunction with an existing surface. 
            /// 
            /// Initially the surface contents are all 0 (transparent if contents have 
            /// transparency, black otherwise.) 
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_create_similar", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr create_similar(IntPtr surface, Cairo.Enums.Content_type content, int width, int height);

            /// <summary>
            /// Create a new surface that is as compatible as possible with an existing surface.
            /// 
            /// Initially the surface contents are all 0 (transparent if contents have 
            /// transparency, black otherwise.) 
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_create_similar_image", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr create_similar_image(IntPtr surface, Cairo.Enums.Format format, int width, int height);

            /// <summary>
            /// Sets an offset that is added to the device coordinates determined by the CTM when drawing to surface. One use case for this 
            /// function is when we want to create a cairo_surface_t that redirects drawing for a portion of an onscreen surface to an 
            /// offscreen surface in a way that is completely invisible to the user of the cairo API.
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_set_device_offset", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_device_offset(IntPtr surface, double x_offset, double y_offset);

            /// <summary>
            /// This function returns the previous device offset set by cairo_surface_set_device_offset(). 
            /// </summary>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_get_device_offset", CallingConvention = CallingConvention.Cdecl)]
            public static extern void get_device_offset(IntPtr surface, ref double x_offset, ref double y_offset);

            /// </summary>
            /// <param name="surface">a cairo_surface_t</param>
            /// <returns>the current reference count of surface. If the object is a nil object, 0 will be returned</returns>
            [DllImport("libcairo-2.dll", EntryPoint = "cairo_surface_get_reference_count", CallingConvention = CallingConvention.Cdecl)]
            public static extern int get_reference_count(IntPtr surface);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_stride", CallingConvention = CallingConvention.Cdecl)]
            public static extern int image_surface_get_stride(IntPtr surface);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_data", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr image_surface_get_data(IntPtr surface);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_width", CallingConvention = CallingConvention.Cdecl)]
            public static extern int image_surface_get_width(IntPtr surface);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_height", CallingConvention = CallingConvention.Cdecl)]
            public static extern int image_surface_get_height(IntPtr surface);

            [DllImport("libcairo-2.dll", EntryPoint = "cairo_image_surface_get_format", CallingConvention = CallingConvention.Cdecl)]
            public static extern Cairo.Enums.Format image_surface_get_format(IntPtr surface);
        }
    }
}
