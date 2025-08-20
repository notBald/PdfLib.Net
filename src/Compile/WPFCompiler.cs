#region Imports and defines
#define TurnOfAAForSmalImages
#define TurnOfAAForLargeImages //Assumes TurnOfAAForSmalImages
//#define NeverAAonSmallImages

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using PdfLib.Read;
using PdfLib.Read.Parser;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Render;
using PdfLib.Render.WPF;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
#endregion

namespace PdfLib.Compile
{
    /// <summary>
    /// Experimental "fast" compiler. Will not be usable for "saving"
    /// as it creates a "DisplayList" instead of 1-1 list of PDF commands
    /// </summary>
    /// <remarks>
    /// 
    /// Working arround WPF quirks.
    /// 
    /// Problem 1: Lots of tiny images right next to eachoter.
    ///  Problem 1.b: A few larger images drawn next to eachoter 
    ///  (see: tiger-rgb-tile-planar-08.tif)
    /// 
    /// Common Pattern:
    /// Image draw commands interspersed with cm and clip commands
    /// 
    /// Solution:
    /// Detect when images are drawn next to eachother, mark them
    /// as a image chain. When drawing the images, create a AA less 
    /// DV and draw the images to that DV. 
    /// 
    /// Consideration:
    /// Allow for state changes between image draws, clip changes,
    /// but not any text or painting.
    /// 
    /// Examples of patterns that do work:
    /// q cm BT ET q cm q d0 Q Q q cm q d0 Q Q BT ET Q
    /// 
    /// In this pattern the AA less dv will be created for the 
    /// first image, and commited for the last. The text is 
    /// unproblematic.
    /// 
    /// Examples of patterns that don't work.
    /// 
    /// q cm BT ET q cm q d0 Q Q q cm q d0 Q BT ET Q Q
    /// 
    /// In this case a transform is pushed onto the AA less dv, 
    /// but not popped before text is written. Supporting 
    /// patterns like this quickly becomes complicated as one 
    /// has to adjust the cm of the AA dv after commiting the 
    /// AA less dv. 
    /// 
    /// Issue:
    ///  Need to know the lowest common q state for a image 
    ///  series.
    ///  
    ///  Solution: Count the number of Qpops after drawing
    ///  an image.
    ///  
    /// 
    /// Notes on alpha support
    ///  - It must be possible to transform clips down to device space. This
    ///    can be made possible by not downsizing the points array until a 
    ///    state it popped.
    ///  - It must be possible to switch between the alpha and non-alpha renderer.
    ///    However when entering alpha mode, that must be the main rendered. Then
    ///    if a non-alpha draw is detected, draw with the non-alpha renderer, convert
    ///    to pixels, and copy it into the back buffer
    ///     - The number of non-alpha draws can be detected, and if it's bellow some
    ///       treshold don't switch into non-alpha mode.
    /// </remarks>
    public class WPFCompiler
    {
        #region Variables and properties

        /// <remarks>
        /// This stack contains parameters for commands 
        /// </remarks>
        private Stack<object> _stack;

        /// <summary>
        /// Resources for the page being compiled
        /// </summary>
        private PdfResources _res;

        /// <summary>
        /// Current compiler state
        /// </summary>
        private CompilerState _cs;
        private Stack<CompilerState> _state_stack;

        /// <summary>
        /// For convinience, the cm changes are tracked
        /// </summary>
        /// <remarks>
        /// Note that these CMs have been prepended with the CTM, so
        /// they can be used directly as one would the CTM.
        /// </remarks>
        private Matrix[] _cm_stack;

        /// <summary>
        /// Current graphic state
        /// </summary>
        private GS _gs;

        /// <summary>
        /// Used for generating error messages and recovering
        /// from exceptions.
        /// </summary>
        private WorkState _ws;

        /// <summary>
        /// Whenever unrecognized commands should cause erors or not.
        /// </summary>
        private int _comp_mode;

        /// <summary>
        /// Used for parsing objects.
        /// </summary>
        private IIParser _parse;

        /// <summary>
        /// Used for parsing command keywords.
        /// </summary>
        private Lexer _snize;

        /// <summary>
        /// The commands being generated is pushed onto this list
        /// </summary>
        List<WPFCommand> _cmds;

        /// <summary>
        /// Points making up a path
        /// </summary>
        PathPoint[] _points;
        //int _points_pos;

        /// <summary>
        /// The current drawing point
        /// </summary>
        /// <remarks>
        /// Specs insists this must be set before use, this impl. ignores that.
        /// This may result in some invalid PDF documents rendering differently
        /// as this impl. will allow an invalid draw command through.
        /// </remarks>
        Point _current_point;

        /// <summary>
        /// Position of the last point that opened a graph
        /// </summary>
        int _last_start_point;

        /// <summary>
        /// Used to detect chains of images being drawn
        /// </summary>
        SegCollection[] _gems;
        int _gems_count;

        /// <summary>
        /// From how I'm reading the specs, the W and W*
        /// commands does not end the path. They just mark
        /// the path for use with clipping.
        /// </summary>
        private bool _clip = false;
        private FillRule _clip_rule;

        /// <summary>
        /// Insures all images of the same object instance gets the
        /// same cache
        /// </summary>
        private Dictionary<PdfImage, Do_WPF> _img_cache;

        #endregion

        #region Init

        #endregion

        #region Parse functions

        public WPFPage Compile(PdfPage page)
        {
            //The page's content, which will be parsed
            var content = page.Contents;

            //Resources are needed for parsing.
            _res = page.Resources;

            var start = DateTime.Now;
            Start();

            //content can be null, in which case the page is blank
            if (content != null)
            {
                Parse(content);
            }

            var wpf_page = new WPFPage(page, _cmds.ToArray());
            Clean();
            var end = DateTime.Now;

            Debug.WriteLine("Compile Time: " + (end - start).TotalSeconds.ToString());

            return wpf_page;
        }

        /// <summary>
        /// Parses a contents stream
        /// </summary>
        /// <remarks>This function is not reentrant</remarks>
        internal void Parse(PdfContentArray content)
        {
            _snize = new Lexer(content.Contents);
            _parse = new Parser(null, _snize, null);

            while (true)
            {
                try
                {
                    parseCommand();
                }
                catch (Exception e)
                {
                    #region Exception handeling

                    Debug.WriteLine(e);
                    switch (_ws)
                    {
                        case WorkState.Build:
                            //Rapport: There was an error building a PDF command. This indicates a flaw
                            //         in the command stream. 
                            break;
                        case WorkState.Exe:
                            //Rapport: Failed to execute PDF command. Command that failed to execute: XX
                            break;
                        case WorkState.Fetch:
                            //Rapport: There was an error fetching data from the PDF document. This
                            //         indicates a flaw in the PDF file's structure. Command that failed to execute was: Do
                            break;
                    }

                    //Todo: Stuff here

                    _stack.Clear();
                    continue;

                    #endregion
                }

                //Execution can stop at any point, so commiting for AA compensation here.
                CommitAAData();

                return;
            }
        }

        /// <summary>
        /// Parses code into tokenized commands that can later be
        /// executed.
        /// </summary>
        /// <remarks>
        /// Using the lexer directly for most of the parsing. This
        /// because the parser returns "objects", while I pretty
        /// much just want raw values.
        /// </remarks>
        private void parseCommand()
        {
            PdfType tok;
            _ws = WorkState.Build;
            while ((tok = _snize.SetNextToken()) != PdfType.EOF)
            {
                switch (tok)
                {
                    case PdfType.Keyword:
                        int tok_length = _snize.TokenLength;
                        if (tok_length >= 4)
                        {
                            var tok_str = _snize.Token;
                            if ("true".Equals(tok_str)) _stack.Push(true);
                            else if ("false".Equals(tok_str)) _stack.Push(null);
                            else if ("null".Equals(tok_str)) _stack.Push(null);
                            else goto default;
                        }
                        else
                        {
                            _ws = WorkState.Exe;
                            CreateCommand();

                            //Check for "stop executing" here

                            _ws = WorkState.Build;
                        }
                        continue;

                    case PdfType.Integer:
                        _stack.Push(_snize.GetInteger());
                        continue;

                    case PdfType.Real:
                        _stack.Push(_snize.GetReal());
                        continue;

                    case PdfType.Name:
                        _stack.Push(new PdfName(_snize.GetName()));
                        continue;

                    case PdfType.String:
                        _stack.Push(new PdfString(_snize.RawToken, false));
                        continue;

                    case PdfType.HexString:
                        _stack.Push(new PdfString(_snize.RawToken, true));
                        continue;

                    //Not using the parser for reading arrays as only simple
                    //objects are allowed.
                    case PdfType.BeginArray:
                        //Should nested arrays be supported?
                        List<object> ar = new List<object>();
                        while ((tok = _snize.SetNextToken()) != PdfType.EndArray)
                        {
                            //Todo: Should I get integers as integers?
                            if (tok == PdfType.Integer || tok == PdfType.Real)
                                ar.Add(_snize.GetReal());
                            else if (tok == PdfType.String)
                                ar.Add(new PdfString(_snize.RawToken, false));
                            else if (tok == PdfType.HexString)
                                ar.Add(new PdfString(_snize.RawToken, true));
                            else if (tok == PdfType.Name)
                                ar.Add(new PdfName(_snize.GetName()));
                            else if (tok == PdfType.Keyword)
                            {
                                switch (_snize.Token)
                                {
                                    case "true":
                                        ar.Add(true); break;
                                    case "false":
                                        ar.Add(false); break;
                                    case "null":
                                        ar.Add(null); break;

                                    default:
                                        throw new PdfLogException(ErrSource.Compiler, PdfType.Keyword, ErrCode.Illegal);
                                }
                            }
                            else
                            {
                                throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                            }
                        }
                        _stack.Push(ar);
                        continue;

                    case PdfType.BeginDictionary:
                        _stack.Push(_parse.ReadItem(PdfType.BeginDictionary));
                        continue;

                    default:
                        throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                }
            }

            //return null;
        }

        #endregion

        /// <summary>
        /// Parses commands
        /// </summary>
        /// <remarks>
        /// See 8.2 in the specs.
        /// </remarks>
        private void CreateCommand()
        {
            Matrix m;

            //Page 111 in the specs
            switch (_snize.Token)
            {
                #region Special graphics state

                case "q": //Push state
                    if (_stack.Count != 0 || _gs != GS.Page)
                    {//q/Q is too significant to skip.
                        _stack.Clear();
                        Log.Warn(_gs, "q");
                    }
                    _state_stack.Push(_cs);
                    _cmds.Add(new q_WPF());
                    break;

                case "Q": //Pop state
                    if (_state_stack.Count == 0) goto default;
                    if (_stack.Count != 0 || _gs != GS.Page)
                    {//q/Q is too significant to skip.
                        _stack.Clear();
                        Log.Warn(_gs, "Q");
                    }
                    _cs = _state_stack.Pop();
                    _cmds.Add(new Q_WPF());
                    break;

                case "cm": //Prepend matrix to CTM
                    //I'm allowing CM in text mode. It's common enough and relativly
                    //harmless. 
                    if (_stack.Count != 6 || _gs != GS.Page && _gs != GS.Text) goto default;

                    m = PopMatrix();
                    _cs.CTM.Prepend(m);
                    if (++_cs.NumCTMs == _cm_stack.Length)
                        Array.Resize<Matrix>(ref _cm_stack, _cm_stack.Length * 2);
                    _cm_stack[_cs.NumCTMs] = _cs.CTM;

                    _cmds.Add(new cm_WPF(m));
                    break;

                #endregion

                #region XObjects

                case "Do": //Draw Object
                    if (_stack.Count != 1 || _gs != GS.Page) goto default;
                    string named_object = PopName();
                    _ws = WorkState.Fetch;
                    var xobject = _res.XObject[named_object];
                    if (xobject is PdfImage)
                    {
                        EmitImage((PdfImage)xobject);
                    }
                    else if (xobject is PdfForm)
                    {
                        throw new NotImplementedException();
                        //IForm cf;
                        //var xform = (PdfForm)xobject;
                        //if (_cached_forms == null)
                        //    _cached_forms = new Util.WeakCache<IPage, IForm>(4);
                        //if (!_cached_forms.TryGetValue(xform, out cf))
                        //{
                        //    PdfResources res = xform.GetResources();
                        //    if (res == null)
                        //    {
                        //        //If the resources dictionary is omitted, the page's resource
                        //        //dictionary is implied.
                        //        res = _res;
                        //    }
                        //    cf = new CompiledForm(new PdfCompiler(_cached_forms).Compile(xform.Contents, res), xform.BBox, xform.Matrix); ;
                        //}
                        //return new Do_FORM((CompiledForm)cf);
                    }
                    else
                        throw new PdfReadException(ErrSource.Compiler, PdfType.XObject, ErrCode.Missing);
                    break;

                case "BI":
                    EmitImage(MakeBI());
                    break;

                #endregion

                #region Path construction

                case "m": //MoveTo new subpath
                    if (_stack.Count != 2 || _gs != GS.Page && _gs != GS.Path)
                    { _gs = GS.Path; goto default; }
                    _gs = GS.Path;
                    SetCurrentPoint();
                    _last_start_point = _cs.NumPoints;
                    Push(new PathPoint(_current_point, PointType.Start_open));
                    break;

                case "h": //Close path
                    if (_stack.Count != 0 || _gs != GS.Path) goto default;
                    _points[_last_start_point].Type = PointType.Start_closed;
                    break;

                case "l": //LineTo
                    if (_stack.Count != 2 || _gs != GS.Path) goto default;
                    SetCurrentPoint();
                    Push(new PathPoint(_current_point, PointType.Line));
                    break;

                #endregion

                #region Path painting
                //Implementation note:
                //Don't simply remove the "_gs != GS.Path" restriction. It's how I know
                //there's a figure being constucted. (

                case "n": //End path without filling or stroking
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    MakeGeometry(_clip_rule, false, false);
                    break;

                #endregion

                #region Clipping path

                case "W":
                    if (_stack.Count != 0 || _gs != GS.Path && _gs != GS.Page) goto default;
                    _clip = true;
                    _clip_rule = FillRule.Nonzero;
                    break;

                case "W*":
                    if (_stack.Count != 0 || _gs != GS.Path && _gs != GS.Page) goto default;
                    _clip = true;
                    _clip_rule = FillRule.EvenOdd;
                    break;

                #endregion

                #region Compatibility

                case "BX":
                    _comp_mode++;
                    break;

                case "EX":
                    _comp_mode--;
                    if (_comp_mode < 0) _comp_mode = 0;
                    break;

                #endregion

                default:
                    if (_comp_mode != 0)
                        Debug.WriteLine("Ignored command in compability mode");
                    //Throw exception

                    //But, for now, instead of exception we use
                    _gs = GS.Page;

                    _stack.Clear();
                    break;
            }
        }

        void EmitImage(PdfImage img)
        {
            //Quickly check the final pixel size
            Point p1 = _cs.CTM.Transform(new Point(0, 0));
            Point p4 = _cs.CTM.Transform(new Point(1, 1));
#if TurnOfAAForSmalImages
#if !TurnOfAAForLargeImages
            if (Math.Abs(p4.Y - p1.Y) < 1 || Math.Abs(p4.X - p1.X) < 1)
#endif
            {
                //When lots of small images are drawn next or on top
                //of eachother, things can start looking ugly.
                //Thise code tries to compensate for that by disabling
                //AA for those images.

                //We get the four final rendersize points by
                //transforming them down to the device level
                Point p2 = _cs.CTM.Transform(new Point(1, 0));
                Point p3 = _cs.CTM.Transform(new Point(0, 1));

                //When the create a Path that describes the
                //shape of the image
                var ip = new SegCollection(_cmds.Count,
                    new Line(p1, p3), new Line(p1, p2),
                    new Line(p2, p4), new Line(p3, p4));

#if NeverAAonSmallImages
                //This is just a quick hack, but it works
                if (_gems_count != 0)
                    _gems[0].AddClose(ip);
#else
                if (_gems_count != 0)
                {
                    //If there's other small images drawn before this,
                    //check if this image is close to them
                    for (int c = _gems_count - 1; c >= 0; c--)
                    {
                        //Takes the top figure first, as
                        //it's more likley that they mach.
                        var gem = _gems[c];

                        //Future optimizing:
                        //Right now each gem has a bounds rectangle that
                        //i uses to see if a closer look is needed.
                        //
                        //One could expand this bounds rectangle every
                        //time a gem was added to a group (gem.AddClose)
                        //so that one can check the entire group at once.

                        //Now check line by line.
                        if (gem.IsClose(ip))
                        {
                            //Mark them as close
                            gem.AddClose(ip);
                            break;
                        }
                    }
                }
#endif

                //Push the image onto a stack so that one
                //can compare it with other small images.
                Push(ip);
            }
#if !TurnOfAAForLargeImages
            else
            {
                CommitAAData();
            }
#endif
#endif

            //All image commands share a "bitmap source" cache that's
            //identical for all equal images. That way when an image
            //is drawn multiple times on the screen one can reuse the
            //bitmap source instead of decoding the image again.
            Do_WPF cache;
            if (!_img_cache.TryGetValue(img, out cache))
            {
                cache = new Do_WPF(img);
                _img_cache.Add(img, cache);
            }

            _cmds.Add(cache);
        }

        /// <summary>
        /// Makes a inline image
        /// </summary>
        /// <remarks>
        /// This implementation always decodes the image, so as to find the ending.
        /// This means inline images are decode twice when displayed.
        /// 
        /// Note that the current implementation threats the content streams as a single
        /// stream. For BI images I belive they're not allowed overflow content streams,
        /// so it could be an idea to only fetch data from the current content stream.
        /// </remarks>
        private PdfImage MakeBI()
        {
            //This "readitem(type)" work-alike code bypasses caching in the parser.
            // (By not calling readitem(type) duh)
            //
            //However, if there's an indirect reference in the data
            //being read, the cache will be filled and there will be
            //a "PdfInternalException" next time the function is called
            //
            //This isn't a huge problem, as inline images are not 
            //allowed to have indirect references. The only nag is
            //that the upstack exception handeler is not able to clean
            //up after a failed "BI" command. 
            var type = _snize.SetNextToken();
            var cat = new Catalog();
            while (type != PdfType.Keyword && !"ID".Equals(_snize.Token))
            {
                if (type == PdfType.EOF)
                    throw new PdfReadException(PdfType.EOF, ErrCode.UnexpectedEOF);
                if (type != PdfType.Name)
                    throw new PdfReadException(ErrSource.Compiler, PdfType.Name, ErrCode.WrongType);

                var key = PdfImage.ExpandBIKeyName(_snize.GetName());

                type = _snize.SetNextToken();
                if (type == PdfType.EOF)
                    throw new PdfReadException(PdfType.EOF, ErrCode.UnexpectedEOF);
                //Small bug: Keywords can be placed in the dictionary. This bug may
                //           be present in the parser as well.

                PdfItem item;
                if ("Filter".Equals(key) || "ColorSpace".Equals(key))
                    item = PdfImage.ExpandBIName(_parse.ReadItem(type));
                else
                    item = _parse.ReadItem(type);

                //AddOrReplace the value. "null" keys are to be ignored
                if (item != PdfNull.Value)
                    cat[key] = item;

                type = _snize.SetNextToken();
            }

            //The dictionary of an inline image will always be sealed. The data of this
            //image is inside a compressed content stream, and can therefore not be edited
            //directly.
            var dict = new SealedDictionary(cat);

            //Fetches colorspace if needed
            IColorSpace cspace = null;
            bool imageMask = dict.GetBool("ImageMask", false);
            if (!imageMask)
            {

                if (dict["ColorSpace"] is PdfObject)
                {
                    //According to the specs there's some sort of special reduced indexed CS support,
                    cspace = (IColorSpace)dict.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace);
                    if (!(cspace is IndexedCS))
                        throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
                }
                else
                {
                    var cs = dict.GetNameEx("ColorSpace");
                    if (!"DeviceGray".Equals(cs) && !"DeviceRGB".Equals(cs) && !"DeviceCMYK".Equals(cs) && !"Indexed".Equals(cs))
                        cat["ColorSpace"] = (PdfItem)_res.ColorSpace[cs];
                    cspace = (IColorSpace)dict.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace);
                }
            }

            //Reads out the binary data.
            //Problem: All stream implementations expects the length of the stream
            //to be a known quantity, but there's no way to know with absolute certainty
            //how big the stream really is. 
            //
            //So for this to be a proper implementation you will have to decode the image
            //to see how long the stream is, but at the same time, my filters need to know
            //how long the stream is before decoding the image. 
            //
            //What we do then is first to guess how long the image is, try to decode,
            //and if the decode fails "due to lack of data" we guess again.

            //Figures out the size of the decoded data
            int width = dict.GetUIntEx("Width");
            int height = dict.GetUIntEx("Height");
            int bpc, nComps;
            if (imageMask)
            {
                bpc = 1;
                nComps = 1;
            }
            else
            {
                bpc = dict.GetUIntEx("BitsPerComponent");
                nComps = cspace.NComponents;
            }
            int stride = (width * bpc * nComps + 7) / 8;
            int size = stride * height;

            //There's required to be at least one whitespace character after id.
            //(Potentially two in case of \r\n)
            _snize.SkipWhite(1, true);

            //Tries finding the ending "quickly" first.
            WritableStream stream = WritableStream.SearchForEnd(size, dict, cat, _snize);

            //Tries to find the end of the stream. Note that "EOF" is treated as 
            //a valid way to end the stream.
            _snize.ReadToEI();
            //Should perhaps throw if EI isn't found. Adobe does not render BI without EI
            //so it would be consistant with that.
            //
            //Perhaps also check if there's "Junk data" from current to EI, and emit warning
            //if there is.

            if (stream == null)
            {
                var img_data = _snize.RawToken;

                //Despite the name, this stream isn't writable because the dict inside is
                //sealed.
                cat.Add("Length", new PdfInt(img_data.Length));
                stream = new WritableStream(img_data, dict, false);
                bool eod = false;

                while (true)
                {
                    //Tries to decode the data. It's possible that the decode fails due to
                    //junk data the filter can't handle. There will almost always be a litte
                    //whitespace junk, though I belive that all the filters can handle that.
                    int decoded_data_length;
                    try
                    {
                        //CCITT encoded images are a bit tricky. They can spit out a valid image
                        //even when there's not enough data decoded.
                        //
                        //The current workaround is to have the filter notify the caller if a
                        //EOD was the reason decoding ended.
                        //
                        //For some filters EOD can be found through simpler means:
                        //  Asci85: Search for ~>
                        //  RunLengthEncode: Search for byte value 128
                        //  HexFilter: Never encodes EI
                        //  No filter: Size of image data
                        //  JBig2: Not allowed inline
                        //  JP2K: Not allowed inline
                        //
                        //For Zip and LZW it's probably possible to find the ending in a
                        //quicker way too. 

                        decoded_data_length = stream.DecodeStream(out eod).Length;
                    }
                    //Some of the filters may throw at trunctuated data. Unfortunatly, swallowing
                    //these exceptions will result in the rest of the content stream not being 
                    //rendered if it throws for any other reason than data trunctuation.
                    catch (Exception) { decoded_data_length = 0; }

                    //Checks if the final size is agreeable. 
                    if (decoded_data_length >= size)
                    {
                        //CCITT may give the desired size even when the image isn't fully
                        //decoded. So we check if the decoding ended with EOD, and if
                        //there's data left then continue decoding.
                        //
                        //atec-2008-01.pdf demonstrates this quite well.
                        if (!eod || _snize.IsEOF)
                            break;

                        //Tries decoding CCITT stream again, as decoding ended
                        //in the middle of a row.
                        Debug.Assert(size == decoded_data_length);
                    }

                    //One can alternativly pad the image data instead of throwing this exception
                    if (_snize.IsEOF)
                        throw new PdfReadException(ErrSource.Compiler, PdfType.XObject, ErrCode.UnexpectedEOD);

                    //Tries to find "EI" again.
                    _snize.ReadToEI();

                    //Combines the data arrays.
                    img_data = Util.ArrayHelper.Concat(img_data, _snize.RawToken);

                    //If raw data is four times the size of expected output data it's
                    //in all likelihood due to some bug in the decode filter, so
                    //we break of the search.
                    if (img_data.Length > size && img_data.Length > 4 * size)
                        throw new PdfFilterException(ErrCode.Invalid);

                    //And gives it another go
                    cat["Length"] = new PdfInt(img_data.Length);
                    stream = new WritableStream(img_data, dict, false);
                }
            }

            //Removes "EI"
            _snize.SetNextToken();

            //It's conceivable that there will be junk data beyond this point, though
            //highly unlikely. It should only happen if an image got junk data with a 
            //"EI" combination, and there's not really any good way to deal with that.

            return new PdfImage(stream, dict);
        }

        void MakeAAGeometry(FillRule fl, bool clip, bool stroke, bool fill)
        {
            #region AA notes
            // aa_compensation == fill, both stroke and clip is "ignored"
            // 
            // Clip geometry can be built straigt away. If you both fill
            // and clip it possible the geometry can be shared, so make
            // sure the clip gem is accsesible later of this is the case
            //
            // Stroke geometry. While AA compensation isn't out of the
            // question for strokes, one have to make sure there's no mixing
            // of paths that strokes and paths that fills*.
            //  - Paths that strokes can be mixed with paths that strokes
            //    and fills, as long as the fill itself is not AA 
            //    compensated
            //  - Any changes to stroke width must disable AA comps
            // So, for now, strokes are dealt with like clips.
            //
            // * Scratch that, use the IsFilled/IsStroked flags when building
            //
            // Fills are where the fun is to be had. To avoid having to
            // transform geometry all AA comps fills must happen on the
            // same q/Q level. I don't think there's any point of 
            // supporting up and down changes between q/Q levels between
            // path calls, the only likly scenario for that would be images
            // mixed with paths.
            //
            // OTOH, text drawing between paths is harmles.
            //
            // What isn't harmless is blend modes and alpha. In theory, as
            // long as there isn't any overdraw in the painting itself one
            // can cheat around the issue by painting onto a DV, then blending
            // that against the background. But this quickly becomes 
            // complicated, so IOW any alpha disables AA compensation.
            #endregion
        }

        /// <summary>
        /// Makes geometry without AA compensation
        /// </summary>
        /// <param name="fl">FillRule</param>
        /// <param name="clip">Pushes the geometry onto the clip</param>
        /// <param name="stroke">Strokes the geometry</param>
        /// <param name="fill">Fills the geometry</param>
        void MakeGeometry(FillRule fl, bool stroke, bool fill)
        {
            //We don't want to build the geometry more than we have to,
            //so we keep finished geometry here
            StreamGeometry sg = null;

            if (_clip)
            {
                _clip = false;

                //Creates the geometry or switches clip rule
                if (sg == null)
                {
                    sg = new StreamGeometry() { FillRule = _clip_rule };
                    var sgc = sg.Open();
                    BuildGeometry(_last_start_point, _cs.NumPoints, true, false, sgc);
                    sgc.Close();
                    sg.Freeze();
                }
                else if (sg.FillRule != _clip_rule)
                {
                    sg = sg.Clone();
                    sg.FillRule = _clip_rule;
                    sg.Freeze();
                }

                _cmds.Add(new clip_WPF(sg));
            }
        }

        /// <summary>
        /// Constucts a geometry figure
        /// </summary>
        void BuildGeometry(int start, int stop, bool IsFilled, bool IsStroked, StreamGeometryContext sgc)
        {
            for (; start < stop; start++)
            {
                var pp = _points[start];
                if (pp.Type == PointType.Line)
                    sgc.LineTo(pp.P, IsStroked, false);
                else if (pp.Type == PointType.OnCurve)
                    sgc.BezierTo(_points[start - 2].P, _points[start - 1].P, pp.P, IsStroked, false);
                else if (pp.Type == PointType.Start_closed)
                    sgc.BeginFigure(pp.P, IsFilled, true);
                else if (pp.Type == PointType.Start_open)
                    sgc.BeginFigure(pp.P, IsFilled, false);
            }
        }

        /// <summary>
        /// A function that looks over the data collected for AA compensation
        /// and commits the result.
        /// </summary>
        void CommitAAData()
        {
            //This code is for small images. Constraints for images include:
            // - Images can be drawn on different CTM and Qq levels
            // - No drawing allowed between the images
            // - The CTM before and after drawing the images, must be the same.
            // - Stack pos before and after drawing must be the same
            // - During drawing the stack pos may not go bellow the start stack pos
            // - Clip path may change during drawing, but must be the same before/after
            //
            // Potential cheats. 
            // - If the CTM chages during drawing, one can actually allow that as long 
            //   as the parent DV is updated with the correct CTM data.
            // - Like the CTM, clip paths may also be applied to the parrent DV
            //   after drawing.
            //
            // Remember.
            // - The _gems holds the images in the order they will be drawn.
            if (_gems_count > 1)
            {
                //Every time we insert commands in the _cmds list, the 
                //CmdPos markers must be offset with two.
                int adr_offs = 0;
                int start_pos = 0;

                do
                {
                    //First we find the all the images in a potential group.
                    int endpos;
                    var start_gem = _gems[start_pos];
                    var list = start_gem.Close;
                    if (list == null)
                        endpos = start_pos;
                    else
                    {
                        for (endpos = start_pos + 1; endpos < _gems_count; endpos++)
                        {
                            if (_gems[endpos].Close != list)
                                break;
                        }
                        endpos--;
                    }

                    if (endpos > start_pos)
                    {
                        //We have a chain of images that can be drawn without AA.
                        //But first check for the lowest stack point we can get away
                        //with before the first image.

                        //The only allowed commands are clip, cm and q. No Q or anything else
                        int stack_height_start = 0;
                        int start_cmdpos = start_gem.CmdPos + adr_offs;
                        for (int cmd_pos = start_cmdpos - 1; cmd_pos >= 0; cmd_pos--)
                        {
                            var cmd = _cmds[cmd_pos];
                            if (cmd is q_WPF) stack_height_start--;
                            else if (!(cmd is clip_WPF || cmd is cm_WPF))
                                break; ;
                        }

                        //Then we look at the end. Same principle, except looking for Q instead
                        var end_gem = _gems[endpos];
                        int stack_height_end = 0;
                        int end_cmdpos = end_gem.CmdPos + adr_offs;
                        for (int cmd_pos = end_cmdpos + 1; cmd_pos < _cmds.Count; cmd_pos++)
                        {
                            var cmd = _cmds[cmd_pos];
                            if (cmd is Q_WPF) stack_height_end--;
                            else if (!(cmd is clip_WPF || cmd is cm_WPF))
                                break; ;
                        }

                        int stack_height = (stack_height_start > stack_height_end) ? stack_height_start : stack_height_end;

                        //Then a quick check between the command. If the stack height ever dips bellow the stack_height,
                        //we don't bother with AA. It's possible to split into multiple non-AA calls, but this is a rare
                        //exceptional case so we're not bothering.
                        bool AA = true;
                        for (int cmd_pos = start_gem.CmdPos + 1, height = 0; cmd_pos < end_cmdpos; cmd_pos++)
                        {
                            var cmd = _cmds[cmd_pos];
                            if (cmd is Q_WPF)
                            {
                                height--;
                                if (height < stack_height)
                                {
                                    AA = false;
                                    break;
                                }
                            }
                            else if (cmd is q_WPF)
                                height++;
                        }

                        if (AA)
                        {
                            //Finds the last position for inserting a "AA off" command
                            int cmd_pos_start = start_cmdpos - 1;
                            for (int stack = 0; stack > stack_height; cmd_pos_start--)
                            {
                                //We know there's no Q or other problematic commands
                                if (_cmds[cmd_pos_start] is q_WPF) stack--;
                            }

                            //Finds the first position for inserting a "AA on" command
                            int cmd_pos_end = end_cmdpos + 1;
                            for (int stack = 0; stack > stack_height; cmd_pos_end++)
                            {
                                //We know there's no q or other problematic commands
                                if (_cmds[cmd_pos_end] is Q_WPF) stack--;
                            }

                            //From my understanding of the docs, Insert will not throw if
                            //pos == Count (a.k.a after the very last command)
                            _cmds.Insert(cmd_pos_start + 1, new AA_WPF(false));
                            _cmds.Insert(cmd_pos_end + 1, new AA_WPF(true));

                            adr_offs += 2;
                        }
                    }

                    start_pos = endpos + 1;
                } while (start_pos < _gems_count); //<-- note, less than (instead of <=) is not a bug
            }

            _gems_count = 0;
        }

        #region Stack functions

        /// <summary>
        /// Pops a name off the stack
        /// </summary>
        [DebuggerStepThrough()]
        private string PopName()
        {
            var o = _stack.Pop();
            if (o is PdfName) return ((PdfName)o).Value;
            throw new PdfReadException(ErrSource.Compiler, PdfType.Name, ErrCode.WrongType);
        }

        private Matrix PopMatrix()
        {
            Matrix m = new Matrix();
            m.OffsetY = PopDouble();
            m.OffsetX = PopDouble();
            m.M22 = PopDouble();
            m.M21 = PopDouble();
            m.M12 = PopDouble();
            m.M11 = PopDouble();
            return m;
        }

        private void SetCurrentPoint()
        {
            _current_point.Y = PopDouble();
            _current_point.X = PopDouble();
        }

        private Point PopPoint()
        {
            Point p = new Point();
            p.Y = PopDouble();
            p.X = PopDouble();
            return p;
        }

        /// <summary>
        /// Pops a double off the stack
        /// </summary>
        [DebuggerStepThrough()]
        private double PopDouble()
        {
            var o = _stack.Pop();
            if (o is double) return (double)o;
            return (int)o;
        }

        /// <summary>
        /// Pushes on a point for a path
        /// </summary>
        void Push(PathPoint p)
        {
            if (_points.Length == _cs.NumPoints)
                Array.Resize<PathPoint>(ref _points, _cs.NumPoints * 2);
            _points[_cs.NumPoints++] = p;
        }

        void Push(SegCollection ip)
        {
            if (_gems.Length == _gems_count)
                Array.Resize<SegCollection>(ref _gems, _gems_count * 2);
            _gems[_gems_count++] = ip;
        }

        #endregion

        #region Start and Clean

        /// <summary>
        /// Readies the compiler
        /// </summary>
        private void Start()
        {
            _cmds = new List<WPFCommand>(512);
            _points = new PathPoint[256];
            _cs.NumPoints = 0;
            _last_start_point = -1;
            _stack = new Stack<object>(12);
            _comp_mode = 0;
            _gs = GS.Page;
            _ws = WorkState.Compile;
            _state_stack = new Stack<CompilerState>(4);
            _cs = new CompilerState();
            _cs.cs = DeviceGray.Instance;
            _cs.CS = DeviceGray.Instance;
            _cs.CTM = Matrix.Identity;
            _cm_stack = new Matrix[16];
            _cm_stack[0] = _cs.CTM;

            //AA compensation
            _gems = new SegCollection[4];
            _gems_count = 0;

            //Caching
            _img_cache = new Dictionary<PdfImage, Do_WPF>(10);
        }

        /// <summary>
        /// Cleans. Not really needed, but the compiler object can
        //  sit arround and this avoids it holding any pages in memory.
        /// </summary>
        private void Clean()
        {
            _stack = null;
            //_mc_stack = null;
            _state_stack = null;
            _cs = new CompilerState();
            _img_cache = null;
            _gems = null;
            _cm_stack = null;
        }

        #endregion

        /// <summary>
        /// For tracking needed state information.
        /// </summary>
        struct CompilerState
        {
            /// <summary>
            /// Current color space
            /// </summary>
            public IColorSpace cs;
            public IColorSpace CS;

            /// <summary>
            /// Current transform matrix. 
            /// </summary>
            public Matrix CTM;

            /// <summary>
            /// The number of points in the points array.
            /// </summary>
            public int NumPoints;

            /// <summary>
            /// The number of CTM changes
            /// </summary>
            public int NumCTMs;
        }

        class GemPath
        {

        }

        [DebuggerDisplay("{P} - {Type}")]
        class PathPoint
        {
            public PointType Type;
            public readonly Point P;
            public PathPoint(Point p, PointType t)
            { P = p; Type = t; }
        }

        class SegCollection
        {
            /// <summary>
            /// Lines int this collection. Need not be continious
            /// </summary>
            internal readonly Line[] Lines;

            /// <summary>
            /// Bounds rectangle enclosing this image.
            /// </summary>
            internal readonly Rect Bounds;

            /// <summary>
            /// The command location for this place holder
            /// </summary>
            internal readonly int CmdPos;

            /// <summary>
            /// SegCollections known to be close to this
            /// collection
            /// </summary>
            public List<SegCollection> Close = null;

            /// <remarks>
            /// Since images can be rotated I want all points. It's possible to
            /// calculate the missing points if one have just ll and ur, but that's
            /// annoying.
            /// </remarks>
            internal SegCollection(int pos, params Line[] lines)
            {
                CmdPos = pos; //LL = ll; LR = lr; UL = ul; UR = ur;

                //The lines arround the image. Sorts them so
                //they come in a predictable order. 
                //(Lower leftmost -> Upper rightmost)
                Lines = lines;

                //Figures out the normalized bounds for the entire collection.
                double[] X = new double[Lines.Length * 2];
                for (int c = 0, k = 0; k < Lines.Length; k++)
                {
                    var line = Lines[k];
                    X[c++] = line.P1.X;
                    X[c++] = line.P2.X;
                }
                Array.Sort<double>(X);
                double x = X[0], width = X[X.Length - 1] - x;
                double[] Y = X;
                for (int c = 0, k = 0; c < Y.Length; k++)
                {
                    var line = Lines[k];
                    Y[c++] = line.P1.Y;
                    Y[c++] = line.P2.Y;
                }
                Array.Sort<double>(Y);
                double y = Y[0], height = Y[Y.Length - 1] - y;

                //Increases the bounds with half a pixel in all
                //directions.
                Bounds = new Rect(x - 0.5, y - 0.5, width + 1, height + 1);
            }

            public bool IsClose(SegCollection seg)
            {
                if (!Bounds.IntersectsWith(seg.Bounds))
                    return false;

                var lines = seg.Lines;
                for (int c = 0; c < Lines.Length; c++)
                {
                    var a_line = Lines[c];

                    for (int k = 0; k < lines.Length; k++)
                    {
                        if (a_line.IsClose(lines[k]))
                            return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Registers segments in the close collections
            /// and merges them.
            /// </summary>
            public void AddClose(SegCollection seg)
            {
                if (Close == null)
                {
                    if (seg.Close != null)
                    {
                        Close = seg.Close;
                        Close.Add(this);
                    }
                    else
                    {
                        Close = new List<SegCollection>(4);
                        seg.Close = Close;
                        Close.Add(this);
                        Close.Add(seg);
                    }
                }
                else if (seg.Close == null)
                {
                    seg.Close = Close;
                    Close.Add(seg);
                }
                else if (seg.Close != Close)
                {
                    //Merges the collections
                    var other_close = seg.Close;
                    for (int c = 0; c < other_close.Count; c++)
                    {
                        var elm = other_close[c];

                        //Since lists are shared "Close" will 
                        //never contain the element being added
                        //from the other list
                        Close.Add(elm);

                        //All shared elements are to share the same list
                        elm.Close = Close;
                    }
                    Close.Add(seg);
                }
                //No need to add when seg.Close == Close 
                //(as it's already added)
            }
        }

        [DebuggerDisplay("{P1} - {P2}")]
        internal class Line
        {
            public readonly Point P1;
            public readonly Point P2;
            public readonly double Slope;
            public double XMax { get { return P1.X > P2.X ? P1.X : P2.X; } }
            public double YMax { get { return P1.Y > P2.Y ? P1.Y : P2.Y; } }
            public double XMin { get { return P1.X < P2.X ? P1.X : P2.X; } }
            public double YMin { get { return P1.Y < P2.Y ? P1.Y : P2.Y; } }
            public Line(Point p1, Point p2)
            {
                P1 = p1; P2 = p2;

                //Slope formula:                    Line formula
                //
                //    (x2,y2)       (y1 - y2)
                //     /        m = ---------       y = mx + b
                //    /             (x1 - x2)
                // (x1,y1)

                //Order this is done isn't important, only that
                //it's consistent between X and Y
                double x = p1.X - p2.X;

                //It does not matter whenever the slope goes down
                //ot up, so insure that -1/0 and 1/0 gives the
                //same value. 
                Slope = (x == 0) ? Double.PositiveInfinity : (p1.Y - p2.Y) / x;
            }

            internal bool IsClose(Line line)
            {
                double dy;

                if (Slope != line.Slope)
                    return false;

                if (Slope == double.PositiveInfinity)
                {
                    //We have vertical lines. I.e. only
                    //the x axis is interesing.
                    var dx = P1.X - line.P1.X;
                    if (dx > 1 || dx < -1) return false;

                    //Final check to see if there's any overlap
                    double ymax1 = YMax, ymax2 = line.YMax;
                    double ymin1 = YMin, ymin2 = line.YMin;
                    if (ymax1 < ymin2)
                    {
                        if (ymin2 - ymax1 > 1)
                            return false;
                    }
                    else if (ymin1 > ymax2)
                    {
                        if (ymin1 - ymax2 > 1)
                            return false;
                    }

                    return true;
                }

                if (Slope == 0)
                {
                    //We have a horizontal line. I.e. only the y
                    //axis is interesting
                    dy = P1.Y - line.P1.Y;
                    if (dy > 1 || dy < -1) return false;

                    //Final check to see if there's any overlap
                    double xmax1 = XMax, xmax2 = line.XMax;
                    double xmin1 = XMin, xmin2 = line.XMin;
                    if (xmax1 < xmin2)
                    {
                        if (xmin2 - xmax1 > 1)
                            return false;
                    }
                    else if (xmin1 > xmax2)
                    {
                        if (xmin1 - xmax2 > 1)
                            return false;
                    }

                    return true;
                }

                //First check if they are close on the y axis. This
                //is done by solving the line formula for b, and comparing
                //them.
                // b = y - mx;
                var b1 = P1.Y - Slope * P1.X;
                var b2 = line.P1.Y - line.Slope * line.P1.X;
                var db = b1 - b2;
                if (db > 1 || db < -1) return false;

                //Now we check if the lines are close on the x axis.
                //This is done by plugging y=0 into the now know line
                //formula.
                // x = (b - y) / m
                var y1 = 1 / Slope;
                var y2 = 1 / line.Slope;
                dy = y1 - y2;
                if (dy > 1 || dy < -1) return false;

                //Todo: Final check. See if there's any overlap

                return true;
            }


        }

        enum PointType
        {
            /// <summary>
            /// This is the first point of a open graph
            /// </summary>
            Start_open,

            /// <summary>
            /// This is the first point of a closed graph
            /// </summary>
            Start_closed,

            /// <summary>
            /// This point is on the curve
            /// </summary>
            OnCurve,

            /// <summary>
            /// This point is off the curve
            /// </summary>
            OffCurve,

            /// <summary>
            /// This point is part of a line streaching from
            /// the last point.
            /// </summary>
            /// <remarks>
            /// Can also use "OnCurve", but this makes it slightly
            /// easier to sepperate lines from curves.
            /// </remarks>
            Line,
        }

        enum WorkState
        {
            Compile,
            Build,
            Exe,
            Fetch
        }
    }

    abstract class WPFCommand
    {
        internal abstract void Execute(DrawWPF draw);
    }

    /// <remarks>
    /// WPF caches all image data before rendering,
    /// so there's no point in not keeping already
    /// decoded images arround until the rendering
    /// is done. Will speed up the "draws 100's of
    /// small images PDF files.
    /// </remarks>
    class Do_WPF : WPFCommand
    {
        internal readonly PdfImage Img;
        internal BitmapSource Source = null;

        internal Do_WPF(PdfImage img)
        { Img = img; }

        internal override void Execute(DrawWPF draw)
        {
            draw.DrawImage(this);
        }
    }

    sealed class clip_WPF : WPFCommand
    {
        readonly StreamGeometry _clip;
        public clip_WPF(StreamGeometry clip)
        { _clip = clip; }
        internal override void Execute(DrawWPF draw)
        {
            draw.PushClip(_clip, _clip.Bounds);
        }
    }

    sealed class cm_WPF : WPFCommand
    {
        readonly Matrix _m;
        public cm_WPF(Matrix m) { _m = m; }
        internal override void Execute(DrawWPF draw)
        {
            draw.PrependCM(_m);
        }
    }

    sealed class q_WPF : WPFCommand
    {
        internal override void Execute(DrawWPF draw)
        {
            draw.Save();
        }
    }

    sealed class Q_WPF : WPFCommand
    {
        internal override void Execute(DrawWPF draw)
        {
            draw.Restore();
        }
    }

    sealed class AA_WPF : WPFCommand
    {
        readonly bool _aa;
        public AA_WPF(bool aa) { _aa = aa; }
        internal override void Execute(DrawWPF draw)
        {
            draw.AntiAlasing = _aa;
        }
    }
}

//Idea: Calc the slope of each line.
//
//Slope formula:                    Line formula
//
//    (x2,y2)       (y1 - y2)
//     /        m = ---------       y = mx + b
//    /             (x1 - x2)
// (x1,y1)
//
//I want to find parallel lines. Only lines with the same slope are
//parallel. This includes 0 for horz. lines and NaN for vert. lines
//
//When lines are parallel they might be touching. For images at least
//we don't need to worry about FillOrder or anything else, just check
//if lines are touching. 
//
// Observation: No mather how the CTM is set up, transformations will
// be linear (right) i.e. two and two lines in a rectangle will share 
// slopes. I.e. only two slopes need computing.
//
// On rectangles one don't always have to compare all four lines, but
// only if one normilize the rects so that xmin is on the left, etc.
//
//So, let's compare two lines first. Forget rectangles for the moment.
//
// Step 1. Compute slope for both lines (m1 and m2)
// Step 2. if (m1 == m2)
// Step 3. Compute b1 and b2
// Step 4. if (b1 ~= b2)
// Step 5. Plug x=0 into both line formulas to get the y offset from 0
// Step 6. if (yx1=0 ~= yx2=0)
//
// At this point the lines are close enough to possibly touch. However,
// for this to be the case the x and y values of the lines must share
// ranges. Computing this is relativly simple, just check that x of
// one point of the line is between x1 and x2 of the other line, repeat
// for each x and y value. A bit annoying, but should do the trick. 
//
// Another idea. Treat the lines as rectangles and check if they overlap.
// only if they overlap is there a need for a closer look. Only problem
// is vertical and horizontal lines, this approach won't work for them
// (but this needs to be special cased anyhow thanks to NaN and such)

//Tricks of the Windows Game Programming Gurus by Andre LaMothe
//
//
//// Returns 1 if the lines intersect, otherwise 0. In addition, if the lines 
//// intersect the intersection point may be stored in the floats i_x and i_y.
//char get_line_intersection(float p0_x, float p0_y, float p1_x, float p1_y, 
//    float p2_x, float p2_y, float p3_x, float p3_y, float *i_x, float *i_y)
//{
//    float s1_x, s1_y, s2_x, s2_y;
//    s1_x = p1_x - p0_x;     s1_y = p1_y - p0_y;
//    s2_x = p3_x - p2_x;     s2_y = p3_y - p2_y;

//    float s, t;
//    s = (-s1_y * (p0_x - p2_x) + s1_x * (p0_y - p2_y)) / (-s2_x * s1_y + s1_x * s2_y);
//    t = ( s2_x * (p0_y - p2_y) - s2_y * (p0_x - p2_x)) / (-s2_x * s1_y + s1_x * s2_y);

//    if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
//    {
//        // Collision detected
//        if (i_x != NULL)
//            *i_x = p0_x + (t * s1_x);
//        if (i_y != NULL)
//            *i_y = p0_y + (t * s1_y);
//        return 1;
//    }

//    return 0; // No collision
//}