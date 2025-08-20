using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf;

namespace PdfLib.Render
{
    /// <summary>
    /// Implemented by page and pages
    /// </summary>
    public interface IInherit
    {
        PdfResources Resources { get; }
        PdfRectangle MediaBox { get; }
        PdfRectangle CropBox { get; }
    }

    /// <summary>
    /// Objects that can be rendered implements this
    /// interface
    /// </summary>
    public interface IPage
    {
        PdfResources Resources { get; }
    }

    /// <summary>
    /// Objects that can have their content stream
    /// updated implements this interface.
    /// </summary>
    /// <remarks>
    /// PdfType3 fonts implements IPage but is not
    /// writable, so differenceing them with this
    /// interface.
    /// </remarks>
    public interface IWPage : IPage
    {

    }

    /// <summary>
    /// Render objects that can be created implements
    /// this interface
    /// </summary>
    internal interface IWSPage : IWPage
    {
        void SetContents(byte[] contents);
    }
}
