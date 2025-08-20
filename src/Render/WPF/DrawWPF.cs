//
// This class is currently in experimental form.
//
//#define ExperimentalCode
//#define UseGuidelinesOnImages
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLib.Pdf;
using PdfLib.Util;
using PdfLib.Compile;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Function;

namespace PdfLib.Render.WPF
{
    internal class DrawWPF
    {
        #region Variables and properties

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        Stack<State> _state_stack = new Stack<State>();

        /// <summary>
        /// The "from device space" contains an inverted
        /// matrix that can be applied to CTM when one
        /// only want to transform up or down from the
        /// user space.
        /// </summary>
        private Matrix _from_device_space;

        /// <summary>
        /// The drawing visual used for rendering
        /// </summary>
        MyDrawingVisual _dv, _main_dv;
        DrawingContext _dc, _main_dc;

#if ExperimentalCode

        /// <summary>
        /// If drawing is to be adjusted to whole pixels
        /// </summary>
        bool _adj_to_px = true;

        /// <summary>
        /// The inverse of the CTM. Only up to date if _adj_to_px is set
        /// </summary>
        Matrix _iCTM;

        /// <summary>
        /// Adjustment matrix. Never set this as rotated (M12, M21)
        /// </summary>
        Matrix _adj_mat;

#endif

        internal bool AntiAlasing
        {
            get { return _dv.EdgeMode != EdgeMode.Aliased; }
            set
            {
                if (value)
                    CommitAliasedDv();
                else
                {
                    if (_dv.EdgeMode != EdgeMode.Aliased && _main_dv == null)
                    {
                        _main_dv = _dv;
                        _dv = new MyDrawingVisual() { EdgeMode = EdgeMode.Aliased };
                        _main_dc = _dc;
                        _dc = _dv.RenderOpen();
                    }
                }
            }
        }

        #endregion

        #region Init and finishing code

        public DrawWPF(PdfRectangle MediaBox, PdfRectangle ClipBox,
                         double output_width, double output_height,
                         bool respect_aspect, int rotate)
        {
            //Calcs the size of the user space. Negative size is intended.
            //
            // The user space is the coordinate system PDF use by default.
            // The size of the user space is in essence the size of the page.
            double user_space_width = MediaBox.URx - MediaBox.LLx;
            double user_space_heigth = MediaBox.LLy - MediaBox.URy;
            double abs_us_width = Math.Abs(user_space_width);
            double abs_us_height = Math.Abs(user_space_heigth);

            //Corrects for aspect ratio. This is so one don't have to bother
            //getting the height/width parameters correct in relation to a page's
            //size. I.e. one can input the maximum render size.
            xSize output = PdfRender.CalcDimensions(MediaBox, output_width, output_height, respect_aspect, 0);

            //Sets up mapping from the defualt PDF user space to WPF device space.
            //
            //   PDF: 0.1---1.1    WPF: 0.0---1.0   Scale matrix: Sx--0    M11--M12
            //         |     |           |     |                  |   |     |    |
            //        0.0---1.0         0.1---1.1                 0--Sy    M21--M22
            double device_width = output.Width;
            double device_height = output.Height;
            double scale_x = device_width / user_space_width;
            double scale_y = device_height / user_space_heigth;
            Matrix from_userspace_to_device = new Matrix(scale_x, 0, 0,
                                                         scale_y,
                                                  (scale_x < 0) ? output.Width : 0,
                                                  (scale_y < 0) ? output.Height : 0);

            //Translates so that the media box starts at 0,0
            from_userspace_to_device.TranslatePrepend(-MediaBox.LLx, -MediaBox.LLy);

            //Resets all state information
            _cs.Reset();

            //It's important that the "rotation" matrix don't end up on the CTM, as
            //that will affect "automatic stroke width adjustments"
            _cs.CTM = from_userspace_to_device;

            //Rotates the page
            if (rotate != 0)
            {
                from_userspace_to_device.Rotate(rotate);
                //Assumes angular rotation.
                if (rotate == 90)
                    from_userspace_to_device.Translate(output.Height, 0);
                else if (rotate == 180)
                    from_userspace_to_device.Translate(output.Width, output.Height);
                else if (rotate == 270)
                    from_userspace_to_device.Translate(0, output.Width);
            }

            //There are times one want to remove the "from_userspace_to_device"
            //matrix from the CTM.
            _from_device_space = from_userspace_to_device;
            _from_device_space.Invert();

            //Unlike DrawDC, DrawWPF wants to manage the DV itself. This is with
            //thought to future support of blend modes.
            _dv = new MyDrawingVisual();
            //Todo: Allow user to set AA and BitmapScaling modes
            //_dv.EdgeMode = EdgeMode.Aliased;

            //Todo: IDisposable so that the Using pattern can be used.
            _dc = _dv.RenderOpen();
            var mt = new MatrixTransform(from_userspace_to_device);
            mt.Freeze();
            _dc.PushTransform(mt);

            //Fills the whole page white
            var page_rect = XtoWPF.ToRect(MediaBox);
            _dc.DrawRectangle(Brushes.White, null, page_rect);
#if ExperimentalCode
            if (_adj_to_px)
            {
                _iCTM = _cs.CTM;
                _iCTM.Invert();
            }
#endif
            //Prevents drawing outside the page (can show up if one
            //place the dv straight onto the screen)
            page_rect = XtoWPF.ToRect(ClipBox);
            PushClip(new RectangleGeometry(page_rect), page_rect);
        }

        /// <summary>
        /// This call signals that the drawing is finished. No
        /// more drawing can be done after this.
        /// </summary>
        /// <returns>The finsihed drawing</returns>
        public MyDrawingVisual Finish()
        {
            CommitAliasedDv();
            _dc.Close();
            _dc = null;
            var ret = _dv;
            _dv = null;
            //Todo: Clear cached images.
            return ret;
        }

        private void CommitAliasedDv()
        {
            if (_main_dv != null)
            {
                _dc.Close();
                var vb = new VisualBrush(_dv);
                _dc = _main_dc;
                _dc.DrawRectangle(vb, null, _dv.ContentBounds);
                _dv = _main_dv;
                _main_dv = null;
                _main_dc = null;
            }
        }

        #endregion

        #region State

        /// <summary>
        /// Use this when pusinh clip, alternativly save and
        /// restore the current clip path
        /// </summary>
        /// <remarks>Remove Freeze? Bounds?</remarks>
        internal void PushClip(Geometry gem, Rect bounds)
        {
#if ExperimentalCode
            if (_adj_to_px)
            {
                Adjust(bounds);
                gem = gem.Clone();
                gem.Transform = new MatrixTransform(_adj_mat);
                gem.Transform.Freeze();
            }
#endif

            gem.Freeze();
            _dc.PushClip(gem);
            _cs.dc_pos++;
            //if (_cs.dc_pos > 1)
            //    _dc.DrawRectangle(Brushes.Red, null, gem.Bounds);
            //Intersect with the existing clip
            bounds.Transform(_cs.CTM);
            _cs.Current_clip_path.Intersect(bounds);
        }

        /// <summary>
        /// See 8.4 in the specs.
        /// </summary>
        struct State
        {
            /// <summary>
            /// How deep this state has incrementet the dc stack.
            /// </summary>
            public int dc_pos;

            /// <summary>
            /// Current transform matrix. 
            /// </summary>
            /// <remarks>
            /// The current transform matrix should not be
            /// precalculated by the compiler. 
            /// 
            /// The reason for this is that one might want to
            /// adjust the render size, flip pages, etc, without 
            /// having to recompile.
            /// </remarks>
            public Matrix CTM;

            /// <summary>
            /// Todo: Not fullt implemented.
            /// </summary>
            /// <remarks>
            /// This clip path is on the device level. Any code that changes the
            /// device level will also have to adjust this.
            /// </remarks>
            public Rect Current_clip_path; //<-- Is this needed?

            /// <summary>
            /// Resets the state back to default values
            /// </summary>
            public void Reset()
            {
                dc_pos = 0;
                CTM = Matrix.Identity;

                //Sets the clip path so that there's no clipping.
                Current_clip_path = new Rect(new Point(double.MinValue, double.MinValue),
                             new Point(double.MaxValue, double.MaxValue));
            }
        }

        #endregion

        #region Special graphics state

        /// <summary>
        /// Saves the state
        /// </summary>
        /// <remarks>
        /// Beware: Not all state information is saved. 
        /// </remarks>
        public void Save()
        {
            _state_stack.Push(_cs);
            _cs.dc_pos = 0;
        }

        /// <summary>
        /// Restores the previous state
        /// </summary>
        public void Restore()
        {
            //Rewinds the stacks
            while (_cs.dc_pos > 0)
            {
                _dc.Pop();
                _cs.dc_pos--;
            }

            //Todo: if (state.Count) == 0 throw new Exception...
            _cs = _state_stack.Pop();
            //_new_pen = true;
            //_clip = false;
#if ExperimentalCode
            if (_adj_to_px)
            {
                _iCTM = _cs.CTM;
                _iCTM.Invert();
            }
#endif
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        /// <remarks>Todo: use MatrixTransform? as input</remarks>
        public void PrependCM(Matrix m)
        {
            var mt = new MatrixTransform(m);
            mt.Freeze();
            _dc.PushTransform(mt);
            _cs.dc_pos++;
            _cs.CTM.Prepend(m);
            //_new_pen = true;
#if ExperimentalCode
            if (_adj_to_px)
            {
                _iCTM = _cs.CTM;
                _iCTM.Invert();
            }
#endif
        }
#if ExperimentalCode
        private void Adjust(Rect bounds)
        {
            Adjust(bounds.TopLeft, bounds.BottomRight);
        }

        private void Adjust(Point LL, Point UR)
        {
            var ll = _cs.CTM.Transform(LL);
            var ur = _cs.CTM.Transform(UR);

            var ll__test_back = _iCTM.Transform(ll);
            var ur__test_back = _iCTM.Transform(ur);

            //Enlarges to pixel boundaries.
            bool left = true;
            if (ll.X < ur.X)
            {
                ll.X = Math.Floor(ll.X);
                ur.X = Math.Ceiling(ur.X);
            }
            else
            {
                left = false;
                ll.X = Math.Ceiling(ll.X);
                ur.X = Math.Floor(ur.X);
            }

            bool down = true;
            if (ll.Y < ur.Y)
            {
                ll.Y = Math.Floor(ll.Y);
                ur.Y = Math.Ceiling(ur.Y);
            }
            else
            {
                down = false;
                //ll.Y = Math.Ceiling(ll.Y);
                //ur.Y = Math.Floor(ur.Y);
            }

            //Transforms back.
            var ll_back = _iCTM.Transform(ll);
            var ur_back = _iCTM.Transform(ur);

            //Offset down, scale up.
            if (left)
            {
                _adj_mat.OffsetX = ll_back.X - LL.X;
                _adj_mat.M11 = (ll_back.X - ur_back.X) / (LL.X - UR.X);
                //^ Signs should always be the same
            }

            if (down)
            {
                _adj_mat.OffsetY = ll_back.Y - LL.Y;
                _adj_mat.M22 = (ll_back.Y - ur_back.Y) / (LL.Y - UR.Y);
            }
            else
            {
                _adj_mat.OffsetY = LL.Y - ll_back.Y;
                _adj_mat.M22 = (ll_back.Y - ur_back.Y) / (LL.Y - UR.Y);
            }

            //Test points
            var ctm = _cs.CTM;
            ctm.Prepend(_adj_mat);
            var test_ll = ctm.Transform(LL);
            var test_ur = ctm.Transform(UR);
        }
#endif
        #endregion

        #region XObject

        /// <summary>
        /// Draws an image
        /// </summary>
        /// <remarks>
        /// Todo: don't impl more before deciding on csCS issues
        /// 
        /// Also, handle caching of images in a sane manner.
        /// </remarks>
        public void DrawImage(Do_WPF cmd)
        {
            PdfImage img = cmd.Img;
            BitmapSource bs;
            if (cmd.Source == null)
                cmd.Source = rDCImage.DecodeImage(img);
            bs = cmd.Source;
            if (bs == null)
            {
                //ToDo: Report
                return;
            }

            // Note 1: that I flip the y axis (-1d). Otherwise the image gets drawn upside
            // down.
            //
            // Note 2: This is a bit wrong. Should use the width/height set in the
            // metadata of the image. (PDF image) Though I doubt it matters.
            //
            // Note 3: OffsetY is "1" to move the image up by its full height.
            var mt = new MatrixTransform(1d / bs.PixelWidth, 0, 0, -1d / bs.PixelHeight, 0, 1);
#if ExperimentalCode
            if (_adj_to_px)
            {
                var size = _cs.CTM.Transform(new Point(1, 1));
                size.X = (int)(size.X);
                size.Y = (int)(size.Y);
                size = _iCTM.Transform(size);
                mt = new MatrixTransform((size.X) / bs.PixelWidth, 0, 0, -size.Y / bs.PixelHeight, 0, size.Y);
            }
#endif
            mt.Freeze();

#if UseGuidelinesOnImages
            var gls = new GuidelineSet(new double[] { 0, 1 },
                                       new double[] { 0, 1 });
            _dc.PushGuidelineSet(gls);
#endif

            // According to the specs the images are always drawn 1-1 to user 
            // coordinates. Not sure what the specs mean by that as an image's
            // size is decided by the CTM.
            //
            // What I'm doing is scaling the image down to 1x1 pixels, then
            // letting the CTM scale it back up to size. 
            _dc.PushTransform(mt);

            //Note, this method only handles solid brushes. Non-solid brushes must be
            //handeled manualy
            if (img.ImageMask)
            {
                ImageBrush opacity_mask = new ImageBrush();
                opacity_mask.ImageSource = bs;
                _dc.PushOpacityMask(opacity_mask);

                //if (_cs.fillCS is CSPattern)
                //{
                //    var CTM = _cs.CTM;
                //    try
                //    {
                //        _cs.CTM.Prepend(mt.Matrix);
                //        //FillPattern(new RectangleGeometry(new Rect(0, 0, bs.PixelWidth, bs.PixelHeight)), FillRule.Nonzero);
                //    }
                //    finally
                //    {
                //        _cs.CTM = CTM;
                //    }
                //}
                //else
                //{
                //    _dc.DrawRectangle(_cs.fill, null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                //}

                _dc.Pop(); //Pops opacity mask
                _dc.Pop(); //Pops mt
#if UseGuidelinesOnImages
                _dc.Pop(); //Pops guidelines
#endif
                return;
            }
            else
            {
                //SMask. Decode into alpha format. Note that masks are not cached.
                var smask = img.SMask;
                BitmapSource mask_img = null;
                if (smask != null)
                    mask_img = rDCImage.DecodeSMask(smask);
                else
                {
                    var mask = img.Mask;
                    if (mask is PdfImage)
                        mask_img = rDCImage.DecodeImage(((PdfImage)mask));
                }
                if (mask_img != null)
                {
                    //Bilinear of Fant scaled alpha-masks look ugly, so scale
                    //it up to image size with nearest neighbor
                    ImageBrush opacity_mask = new ImageBrush();
                    opacity_mask.ImageSource = Img.IMGTools.ChangeImageSize(mask_img,
                                                    BitmapScalingMode.NearestNeighbor,
                                                    bs.PixelWidth, bs.PixelHeight);

                    //Draws masked image. WPF will scale the image and alpha 
                    //(to output size) using the default method (Bilinear on Net 4.0)
                    _dc.PushOpacityMask(opacity_mask);
                    _dc.DrawImage(bs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                    _dc.Pop(); // Pops opacity mask
                }
                else
                {
                    //if (_cs.transfer != null)
                    //{
                    //    bs = rImage.Transfer(bs, _cs.transfer);
                    //}
                    _dc.DrawImage(bs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                }
                _dc.Pop(); // Pops mt
#if UseGuidelinesOnImages
                _dc.Pop(); //Pops guidelines
#endif
            }
        }

        #endregion
    }
}
