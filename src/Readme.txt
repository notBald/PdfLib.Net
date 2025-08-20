The folder struture

PdfLib
| Root namespace. Contains the classes you're most likely to use, like
| "PdfFile" used for opening PdfDocuments.
|\
| -Compile
| | Classes for tokenizing, altering and analyzing the drawing commands of a PDF document.
| \
|  -Analyze
|  | Helper classes used when analyzing drawing command.
|\
| -Compose
| | Classes that are easier to work with when creating new documents.
| |\
| | -Font
| | | Font classes that is used when embedding text into PDF documents.
| |\
| | -Layout
| | | Classes used to lay out a PDF document.
|  \
|   -Text
|   | Classes used for laying out text.
|\
| -Encryption
| | CRC32 and RC4 encrypion algorithms.
|\
| -Img
| | Convinience classes for working with various image formats.
| |\
| | -Internal
| | | Mostly quantizier algorithms, like NeuQuant and WUQuant.
| |  \
| |  -QWU
| |  | Implementation of WuColor Quantizer
| |\
| | -Png
| | | Png parser and converter
|  \
|   -Tiff
|   | A fully fledged Tiff libary. PdfLib was originaly a Tiff libary with a few PDF features.
|\
| -Pdf
| | Contains the high level classes libary users will generaly interact with, like PdfImage.
| |\
| | -Annotation
| | | PDF documents can be annotated by text, drawings and such.
| |\
| | -ColorSpace
| | | The PDF standard supports many different ways to represent color.
| |  \
| |   -Pattern
| |   | Shaders for use with PatternCS is found here.
| |\
| | -Encryption
| | | Security handles for encrypted PDF documents. Note, PdfLib can not create encrypted documents.
| |\
| | -Filter
| | | Here you find compression and image filters. These are used in conjunction with PDF streams.
| |\
| | -Font
| | | Implementations of all PDF fonts. Can be of interest to users of the libary, but 
| | | "compose fonts" are easier to work with as they handle all the peculiarities with
| | | encoding text, embeding glyphs, and such without the user having to think about it.
| |\
| | -Form
| | | PdfLib have some limited support for PDF forms. These are meant to be filled out after
| | | the document have been created.
| |\
| | -Function
| | | Implementation of all PDF functions. These are often used with colors and patterns.
| |\
| | -Internal
| | | Classes that are generaly uninteresting to outsides of the libary.
| |  \
| |   -Minor
| |   | To avoid polluting the Internal namespace, some classes are put here.
| |\
| | -Optional
| | | Classes used for working with optional meta data about the PDF document.
| |\
| | -Primitives
| | | PdfPrimitives + xStructs. Here you find the building blocks for the PDF
| | | document. It's possible to create a PDF document from scratch using these,
| | | but you'll have to be very familiar with the PDF spesification to do so.
| | | "xStructs" are used when native implementations for matrix, points, vectors
| | | and such is not avalible.
|  \
|   -Transparency
|   | Soft masks, alpha masks, transparency groups and such is found here.
|\
| -PostScript
| | A post script interpeter used for all post script in PDF documents. It's worth
| | noting that no other PDF libary does this, instead they use heuristics.
| | However, heuristics is tricky to get right and I've yet to encounter a PDF
| | file that has invalid PostScript that this interpeter can't handle. They no
| | doubt exist, though.
| | This is used of CMaps, Type3 fonts and PostScript functions.
|\
| -Read
| | Low level classes for parsing and reading PDF files.
| |\
| | -CFF
| | | Parser for the Compact Font Format
| |\
| | -Parser
| | | Parser for PDF documents.
|  \
|   -TrueType
|   | A TrueType parser. Note there are two TrueType parsers in this libary, the one
|   | you probably want to use is in the "PdfLib.Compose.Font" namespace.
|\
| -Render
| | Classes for rendering PDF documents.
| |\
| | -CairoLib
| | | Renders using the Cairo vector libary.
| |\
| | -Commmands
| | | All PDF render commands have a class here.
| |\
| | -Font
| | | Support classes used for rendering of text.
| |\
| | -GDI
| | | Renders PDF to a GDI surface.
| |\
| | -PDF
| | | Renders a PDF document to a PDF document. This can be useful if you, 
| | | for instance, want to remove unsued resources or remove a drawing
| | | layer. Can also be used to create new pdf documents, but it's
| | | reccomended to use the "Compose" classes. You have to be quite
| | | familiar with the PDF specs to use this correctly.
|  \
|   -WPF
|   | Renders the document to a Windows Presentation Fundation surface.
|   | Note that WPF can be quite slow to draw pages with lots of vectors,
|   | but there's little I can do about it, it's a consequence of how
|   | WPF works. Mind, font rendering can be massivly optimized, however
|   | it is rarely a bottleneck.
|\
| -Res
| | Here you find resources, and a convenience class for loading resources
| |\
| | -Cmap
| | | Character maps in postscript format. Run them through the PostScript
| | | interperter if you wish to use them.
| |\
| | -Font
| | | Glyph name tables for various standard PDF fonts and unicode.
| | |\
| | | -AFM
| | | | Adobe Font Metrics for the standard PDF fonts.
| | |\
| | | -cff
| | | | Compact font format fonts for the standard PDF fonts.
| | |\
| | | -Droid
| | | | A convenient fallback font used when no suitable font is found.
| |\
| | -Shader
| | | Pixel shaders that can be used with Shadermodel 2.0.
|  \
|   -Text
|   | Text messages used when analyzing PDF files for errors.
|\
| -Util
| | A varity of utility classes.
 \-Write
  | For creating new PDF documents and writing them to disk
   \
    -Internal
    | Write related classes that a user of the libary probably do not
    | wish to use. Putting them in a "internal" namespace is done
    | to reduce clutter, not because they are marked "internal".
