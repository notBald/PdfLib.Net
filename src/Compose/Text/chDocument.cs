using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// Compose horizontal document.
    /// 
    /// Do note that layout related information is stored in this document structure.
    /// This includes:
    ///  - Width of lines. Both true width and bounding box width
    ///  - Height/Ascent/Descent information
    ///  
    /// The end result of this is that if you hand the document to a textbox, hand 
    /// it to another textbox, then render the first textbox, it won't work.
    /// You'll have to render the first textbox before handing the document to the
    /// next.
    /// </summary>
    public class chDocument : IEnumerable<chParagraph>
    {
        #region Variables and properties

        List<chParagraph> _par = new List<chParagraph>();

        public int ParagraphCount { get { return _par.Count; } }

        public int Length
        {
            get
            {
                int length = 0;
                foreach (var par in _par)
                    length += par.Length;
                return length - 1 + _par.Count;
            }
        }

        #endregion

        #region Init

        public chDocument() { }

        public chDocument(string text)
        {
            foreach(var paragraph in text.Replace("\r\n", "\n").Split('\n'))
                AddParagraph(paragraph);
        }

        public chDocument(string text, bool dbl_newline)
        {
            if (!dbl_newline)
            {
                foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
                    AddParagraph(paragraph);
            }
            else
            {
                foreach (var paragraph in text.Replace("\r\n", "\n").Replace("\n\n", "\r").Split('\r'))
                    AddParagraph(paragraph);
            }
        }

        #endregion

        #region Event

        /// <summary>
        /// For handeling document changes
        /// </summary>
        /// <param name="par">Paragraph that was changed</param>
        /// <param name="index">Index of the paragraph in the document</param>
        /// <param name="added">If this paragraph was newly added</param>
        /// <param name="layout">If paragraph layout changed</param>
        public delegate void ParagraphChangedHandler(chParagraph par, int index, bool added, bool layout);

        public event ParagraphChangedHandler OnParagraphChanged;

        #endregion

        #region Text

        public void AddParagraph(string text)
        {
            AddParagraph(new chParagraph(text));
        }

        public void AddParagraph(chParagraph par)
        {
            int index = _par.Count;
            _par.Add(par);
            par.OnParagraphChanged += new chParagraph.ParagraphChangedHandler(par_OnParagraphChanged);
            if (OnParagraphChanged != null)
                OnParagraphChanged(par, index, true, true);
        }

        void par_OnParagraphChanged(chParagraph par, bool layout)
        {
            if (OnParagraphChanged != null)
                OnParagraphChanged(par, _par.IndexOf(par), false, layout);            
        }

        #endregion

        #region Style

        private delegate void StyleFunction(int start, int end, chParagraph ch);

        private void SetStyle(int start, int end, StyleFunction set)
        {
            int start_ch = 0, end_ch = 0;
            foreach (var par in _par)
            {
                end_ch += par.Length;

                if (start >= end_ch)
                {
                    start_ch = ++end_ch;
                    continue;
                }

                if (end < start_ch)
                    return;

                set(Math.Max(0, start - start_ch), end - start_ch, par);
                start_ch = ++end_ch;
            }
        }

        /// <summary>
        /// Sets font on a section of text
        /// </summary>
        public void SetTextRise(int start, int end, double? size, double? text_rise)
        {
            SetStyle(start, end, (s, e, p) => { p.SetTextRise(start, end, size, text_rise); });
        }

        public void SetFont(int start, int end, cFont font, double? size)
        {
            SetStyle(start, end, (s, e, p) => { p.SetFont(s, e, font, size); });
        }

        public void SetColor(int start, int end, cBrush fill, cBrush stroke)
        {
            SetStyle(start, end, (s, e, p) => { p.SetColor(s, e, fill, stroke); });
        }

        public void SetUnderline(int start, int end, double? underline)
        {
            SetStyle(start, end, (s, e, p) => { p.SetUnderline(s, e, underline); });
        }

        #endregion

        #region Indexing and enum

        /// <summary>
        /// Retrive paragraph by index
        /// </summary>
        /// <param name="index">Index of the page</param>
        /// <returns>The page at the index</returns>
        public chParagraph this[int index]
        {
            get
            {
                return _par[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <summary>
        /// Enumerator for itterating over the paragraphs in the document
        /// </summary>
        public IEnumerator<chParagraph> GetEnumerator()
        { return _par.GetEnumerator(); }

        #endregion
    }
}
