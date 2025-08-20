# PdfLib - A C# libary for working with PDF files

PdfLib is a libary for editing and extracting data from PDF files. It also have a limited ability to render PDF files, this intended for use with thumbnails.

There are many libraries for working with PDF files. For C# you have, PdfSharp, PDF Clown, iTextSharp, PdfFileWriter, PDF File Analyzer, and more.

So PdfLib is just another library among many, but perhaps it fits your needs. 

PdfLib can be used for:
 * Read pdf documents
 * Repair pdf documents
 * Render pages
 * Create new pdf documents
 * Modify existing pdf documents
 * Move pages and resources between pdf documents
 * Embed true type fonts
 * Draw and lay out text
 * Convert and compress images using PNG, CCITT, J2K and Jpeg.
 * Execute postscript (very limited support)
 * Annotate documents
 * Color transformation (CMYK -> RGB, LAB -> RGB, etc.)
 * Create Type3 fonts
 * Optimize and compress pdf documents
 * TIFF to PDF / PDF to TIFF conversion
 * Opening images in these formats: BMP, PNG, JPEG, J2K, TIFF, IFF, TGA, PPM

 Not all pdf features are supported. This includes:
 * Forms and other interactive content
 * Transparency. That is to say, PdfLib can not render transparency
 * Not all forms of encryption
 * Encrypting documents
 * Embedded files. 
 * Saving pdf documents in linear mode
 * Color correction using ICC profiles
 * 3D content
 * Lattice Mesh pattern (I've yet to see this in the wild)
 * Creating tagged pdf
 * Appending changes to existing documents
 * Text extraction (It can, but it's poorly tested)
 * Layers and structured contents
 * XML
 * JavaScript

**The library is not multithreading safe.**

## Licence

[See this file for more licences](https://github.com/notBald/PdfLib.Net/blob/main/src/Licences.txt)

How is all this code licensed? Well, it's a bit of a mess.

Anything I've written goes into the public domain. Use the code as you please, however liability falls onto you, not me. There's also no need to credit me.

But not everything is written by me:
  * zlib.net – BSD like attribution license. 
  * OpenJpeg.net - BSD like attribution license.
  * LibJpeg.net - BSD like attribution license.
  * JBig2.net – BSD like attribution license.

In addition, in the PdfLib.Util namspace there is a few classes I'm unsure of the licence:
  * FlattenHierarchyProxy (Not used by the libary, can be deleted.)
  * MultiStream (Used to combine contents streams into a single stream)
  * WeakReference (Used to implement WeakCache and WeakList)

There are also non-code resources. Files in the "res/cmap" and  "res/fonts/amf" directories are under an Adobe license.

One font (DroidSansFallback.ttf in "res/font/droid) is under the Apache 2.0 license.

Finally, there's fourteen fonts that aren't outright required, but nice to have. Without these fonts, PdfLib will use windows fonts instead. There are two packs:
 * SIL OPEN FONT LICENSE by (URW)++ Design & Development.
 * GNU GENERAL PUBLIC LICENSE Version 2 by (URW)++ Design & Development.

By default, the SIL fonts are used. The GPL fonts are older and has smaller files, probably because they are exactly the same fonts with less glyphs. This does not matter as these glyphs are not used anyway. To switch what fonts are used, simply unzip into the Res/Font/cff directory.

## History

This library began as a Tiff read/write library. This was before LibTiff.net poppet up, and it supported decoding and displaying common tiff files. I also had a need to work with PDF files, so support for reading PDF files was added by using PdfSharp by Stefan Lange.

Now PdfSharp is an excellent library, but it's very much oriented towards creating PDF documents. I needed to read documents quickly and generate thumbnails, something PdfSharp can do, but I had big problems with running out of memory all the time.

To combat memory usage, I streamlined the tiff part so that only the active data needs to be in memory. The tiff code can read a single image from the tiff file and then write that single image out to another tiff file.

PdfSharp in contrast reads the entire document into memory and constructs the entire document in memory before saving. There was no easy way to fix this, as it's simply how the library is written.

My solution was to write a new library for reading and writing PDF files. This library only reads in data into memory on demand, and you can save a PDF document onto disk, page by page. 

# Working with PDF files

This part is about PDF files in general. Skip to [About PdfLib.Net](#about-pdflibnet) for how the library itself is built up, or [Working with PdfLib.net](#working-with-pdflibnet) for examples how to use the libary.

## What is a PDF file?

Portable Document Format was introduced by Adobe Systems™ in 1993. They intended for it to be a platform independent document format that users couldn't directly edit.

The advantage of a format that's not editable is that information on how the document is to be reflowed after editing need not be included. This makes it simpler to convert documents into PDF format and simultaneously make PDF files easier to render. 

In 2008, the PDF format was made into an open standard (ISO 32000-1:2008).

PDF Specification: [PDF32000_2008.pdf](www.adobe.com/devnet/acrobat/pdfs/PDF32000_2008.pdf)

## Parsing a PDF file

The PDF specification does a good job at detailing the layout of PDF files (See 7.5 in the specification). Instead of repeating what is written there, we'll jump straight to parsing. Think of it as learning on the job.

If this is uninteresting, you can skip to "Working with PdfLib"

### Quick overview

A PDF document consists of a header, a trailer and a variety of objects located between the header and the trailer. When you open a PDF document, you first look at the header, then jump to the end of the file to look at the trailer.

### The header

How do you know that the file you're trying to read actually is a PDF document? If you open a PDF document in, for instance, WordPad you'll see, they all start with the string: %PDF

![Header](/img/header.png?raw=true)

This is called the header, and it's required to be at the start of any PDF document.

The PDF header consists of three parts. A short string identifying it as a PDF file (%PDF) a version number (-1.4) and an optional "Binary marker" (for instance %âãÏÓ – though other binary characters can be used instead)

Unfortunately, the header does not always start at the first byte and a good number of PDF files have mangled headers. For instance "%  PDF  -1.7"  instead of "%PDF-1.7".

This means we can't simply look at the first 8 bytes to determine if we got a PDF file. 
PdfLib's approach is to look into the first 512 bytes. The search is limited so that the library can be used to quickly check if a file is a PDF document or not.

### PDF versions

The PDF file format has had seven revisions since 1993. Each new revision is fully compatible with older revisions, so that a PDF viewer that fully supports version 1.7 has no trouble opening a version 1.1 document.

In my testing, version numbers are unimportant. For instance, by default PdfLib always marks the documents it creates as version 1.7. This since even if a PDF viewer does not support 1.7, it will open the document as long as no 1.7 features are actually used.

There are also three "Archive" standards for PDF documents. These prohibit the use of features that can cause issues in the future, such as external fonts. 

### Parsing damaged or broken PDF documents

Like touched upon above, PDF files don't always comply with the specs. The header can be mangled, there may be junk data, and structures in the document can be damaged in various ways. Even so, PDF viewers are able to open these documents.

How?

There are two means used by PDF readers. One is to allow some minor but common forms of document breakage, the other is to run a repair algorithm on the document if certain forms of corruption is discovered.

Examples of minor corruption supported by PdfLib:
 * Can open streams that are +- 1k from where they're supposed to be.
 * Assumes SMasks to be images if no subtype is given or if subtype is wrong.
 * CR instead of LF on stream keywords
 * Little endian unicode
 * Short filter names on non BI streams
 * W* and W commands can be used in the page state
 * All text commands can be used in the page state
 * Junk data after image
 * BDC/EMC can start in a page state and end in a text state
 * No FontDescriptor type (in a FontDescriptor dictionary)
 * Direct page objects
 * Duplicated dictionary enteries (will use the last entery)
 * Missing space in XRef table

One of the most common forms of corruption is issues with the trailer. When such issues are detected, viewers will run an algorithm that in effects rebuilds the trailer. This is possible because the various objects in a PDF document has metadata that duplicates the information found in the trailer.

_PdfLib do actually implement a repair algorithm, but as of yet it doesn't run this algorithm automatically outside for major issues like the trailer being missing altogether._

### Finding the trailer
Implementation source file: _PdfLib.Pdf.Internal.PdfTrailer.cs (function FindOffset)_

PDF documents are required to have a trailer. This trailer contains metadata about the document, such as who created it, how it was encrypted, as well as a list of object locations.

The list of object locations is used to quickly find objects in the PDF file. It's possible to find the objects by a searching through the file, but with files potentially being gigabytes in size, that can be time consuming.

This is why all PDF viewers will attempt to locate the trailer before opening a document.
The trailer is typically placed towards the end of the file, with a reduced trailer often put at the start. A viewer can use the reduced trailer to begin rendering the first page of the document before it has fully downloaded.

To aid readers in finding the trailer, PDF files has a keyword towards the end of the file called _startxref_

![startxref](/img/startxref.png?raw=true)

By convention, you are to search for the last startxref in the file and use that to find the trailer. This because PDF files can be appended after creation, meaning there can be multiple occurrences of startxref.

Once the startxref keyword is found, the next token is the offset to the start of the trailer.

_But what is a token?_

### Lexical analysis of PDF documents
Implementation source file: _PdfLib.Read.Lexer.cs_

Up to now, we've treated the document as a string of characters. However, as like normal writing, these characters make up words, numbers, and operators.

We call these words, numbers, and operators "tokens". The PDF specification sets down clearly defined rules for how to separate tokens from each other. Just like with writing, this is done using whitespace and certain "punctuation like" characters we call operators.

Example:

```
<</Type/Pages/Kids[2 0 R]/Count 1/MediaBox[0 0 595.22 842]/Resources<<>>>>
```

Here we have a string with operators, names and numbers. Each operator is one or two characters long.

Words and numbers are separated either by an operator or by whitespace. Operators are all of a certain length and need not be separated using whitespace.

Now that we know how to recognize the document's tokens, we can go one-step further. The operators you see here means:

```
<<  -> Begin dictionary
/   -> Begin name
[   -> Begin array
]   -> End array
>>  -> End dictionary
```

Instead of treating the document like a stream of characters, we can collect characters into words, numbers and operators. This is easier to work with and is exactly what PdfLib does through lexical analysis.

The Lexer always reads enough characters to determine a token's type and length. This way we can read the document, token by token, instead of character for character.

#### Basic data types
Implementation source files: _PdfLib.Pdf.Primitives.*_

The PDF specification defines seven basic data types.
 * Null: No value. Is to be treated as if it doesn't exist.
 * Bool: true or false
 * Integers: Numbers without fraction
 * Real: Numbers with fraction
 * Name: A string with various restrictions on what characters can be used
 * Literal string: Text that's encoded with escape characters for certain letters
 * Hex string: Text encoded to a hexadecimal format

PdfLib's Lexer recognizes these data types.

In addition to these data types, there's date, rectangles, name trees and number trees. They are created through use of the basic data types and not recognized by PdfLib's Lexer. A date, for instance, is just a hex or literal string to the Lexer.

Names are similar to strings but has more restriction on what characters they can contain. They are required to start with the slash operator (/). Literal strings start with parenthesis and hex strings with the angle bracket.

I.e. `/Name` for names and `(literal string)` or `<hexencodedstring>` for strings.

Using the lexer we can now parse an Xref table.

### The Xref table

Implementation source files: 
 * _PdfLib.Pdf.Internal.PdfTrailer.cs (CreateFromTable)_
 * _PdfLib.Pdf.Internal.XRefStream.cs (Init)_

Having found the offset of the trailer, we can now find the table. This is a table that describes the location of every object in a PDF file, saving us the effort of searching for them.
The PDF specifications define two different forms of Xref tables. 

```
Xref Table:			         Xref Stream:
Xref				         stream
0 8				             00   00 00   00
0000000000 65535 f		     02   00 05   00
0000000015 00000 n		     02   00 05   02
0000000066 00000 n		     01   8E D5   00
0000001244 00000 n		     02   00 05   01
0000018605 00000 n		     01   00 0F   00
0000032266 00000 n		     01   05 34   00
0000034963 00000 n		     01   49 05   00
0000036477 00000 n		     01   7E 62   00
				             01   88 EB   00
				             endstream

For a detailed explanation look at 7.5.4 and 7.5.8 in the PDF specs
```

The two tables look different, but in this example, they describe the same document.

The Xref table is a feature of the 1993 PDF 1.0 format. It is made to be friendly to computers with little memory. Xref streams came with PDF 1.5. Streams can be compressed to take less space, allowing for smaller documents.

Note that a PDF document can have both Xref tables and Xref streams at the same time. This is used to hide unsupported features from old PDF viewers.

The parsing of these tables if fairly straight forwards. Each entry but the first refers to the location of an object. Entry one is for object 1, entry two is for object 2 and so on. In the example there are seven different objects in the Xref table. In the xref stream, there is additional stream objects, which is the reason why that table has more entries.

Now that we know the location of objects, it's time to parse the trailer. The trailer will tell us which of these objects we should ignore, and which object is the document's "Catalog" object. 
Which brings us over to the topic of parsing.

### The Parser
Source file: _PdfLib.Read.Parser.cs_

The Lexer does the job of figuring out the tokens/symbols, and then it's the Parser job of making sense of them. In many ways, the parser works just like the Lexer, just with tokens instead of characters.

Where from the Lexer you get names, numbers, and operators, you will get arrays, dictionaries, streams, and objects from the parser.
 * Array: [ value1 value2 etc… ]
 * Dictionary: << /key1 value1 /key2 value2 etc… >>
 * Stream: << /Length value1 >> stream value2 endstream
 * Object: 1 0 obj value1 endobj
 * Reference: 1 0 R

The Parser recognizes these structures and assembles them together. A dictionary, for instance, starts with token operator <<. When such an operator is encountered, the parser start reading out tokens in key/value pairs until an operator >> is encountered.

#### The PdfDictionary and PdfArray

Arrays and dictionaries are objects that can contain other objects. They function very much like an `object[]` and `Dictionary<object>` in C#. I.e. they can contain "everything", including other arrays and dictionaries.

An example dictionary that includes an array: 

```
<< /key1 (string1) /key2 13.7 /key3 [ (string2) (string3) ]>>
```

Keys are defined to be `/names` in the PDF specs. This is a dictionary with two keys. Key1 is a string, Key2 is a number, key3 is an array with two strings.

All keys are required to be unique and can with that be used to look up the value.

#### Parsing the Trailer
Source file: _PdfLib.Pdf.Internal.PdfTrailer.cs (function CreateFromFile)_

The trailer is basically a PDF dictionary where certain key/value pairs are expected to be present. This includes metadata about who created the document, the size of the XRef table or stream, links to previous XRef tables or streams, and the root reference of the document.

By having references to previous XRef tables, it's possible to append documents with a new trailer without completely rewriting the old trailer. Newer information always takes precedence over old information, so that way we can replace only the data that has changed.

Example of a trailer:

```
<<
/Root 1 0 R
/Info 4 0 R
/Size 307
>>
```

This example trailer has a reference to the root node of the document, a reference to metadata and the highest object ID in the Xref table (any object with a higher ID than given in /Size is to be ignored). 

_But now that we have the trailer, what do we do with it?_

#### The PdfReference
Source file: _PdfLib.Pdf.Primitives.PdfReference.cs_

Notice that one entry in the trailer above is called /Root. This entry always contains the location of the document's Catalog. In this case, the trailer say that the /Root is "1 0 R". This is called a reference.

It wasn't without reason that we went to the trouble of parsing the Xref table earlier. That table describes the position of every object in the PDF document, whereas a reference only says what object you should retrieve.

Image if you have an array of values: [(one) (two) 3 0 R]

To read the values "one and "two" you only need to retrieve them straight out of the array. Value "three" however is a PdfReference. It tells us what object to retrieve when reading this reference, in this case object "3 0".

To find that object, we make a lookup in the Xref table for its position. Then instructs the Lexer to start reading from that position. Assuming the Xref table is filled out correctly, the Lexer will encounter a stream of tokens looking something like this:

```
3 0 Obj
(three)
endobj
```

"3 0 Obj" tells us that this is object id nr. 3 generation 0, and from that we know that we have the correct object. This is followed by the data, and then the object is always ended by "endobj".

#### Binary streams
Source file: _PdfLib.Pdf.Internal.PdfStream.cs_

A PDF document can contain images, embedded fonts, and other objects that can't be represented with arrays and dictionaries. These objects are put into "streams".

A stream is merely a chunk of data with a known length. To make sense of that data, the stream objects are required to provide a list of "filters". These filters can decompress, decode or decrypt the data into an expected format.

In the raw data, a stream will look like this:

```
1 0 Obj
<</Length 100>>
stream
…
endstream
endobj
```

The dictionary must contain a length entry. Then it must be followed by a stream keyword, with an endstream keyword denoting the end of the stream.

Streams are required to be "referenced". That means no PdfDictionary or PdfArray can contain a stream, but they may contain a reference to a stream. To read a stream one must with that:

1.	Read the reference
2.	Read the offset out of the Xref table for said reference 
3.	Parse the object located on that offset. If said object is a dictionary, check if it's followed by a "stream" keyword.
4.	If the stream keyword is found, look up the length entry of the dictionary, then read out the data.

### Standard dictionaries and arrays

A dictionary can be one of several things. It can be a Catalog, an Image, a Font, and much more. Similarly, arrays can be color spaces, annotations, and so on.

For instance, take the Page dictionary.

A page dictionary represents a single page in the document. All contents that is displayed on a page is stored in this dictionary. This includes fonts, images, annotations, and the commands that paints the document.

To paint a page, a Pdf Viewer will execute the commands found in the "Contents" entry of the page. The commands will refer to images, fonts, color spaces, and such, by name. These resources are then found by looking up the names in the page's resources dictionary. 

The page also has other properties, like the "MediaBox", which defines the size of the page, and the "CropBox," which defines the visible portions of the page. The MediaBox is intended for printing, while CropBox for on screen display.

### Example: Fetching the first page

After parsing the trailer, the next natural step is to find the document's Catalog. The trailer only contains metadata, the document id and encryption information. The actual document is placed in an object called the "catalog".


To find the catalog the library follows the "Root" entry of the trailer. That entry is always a reference and always points at the Catalog.  Thus, what happens before a Pdf viewer display the first page is this:
1.	The root entry in the trailer dictionary is read.
2.	This must always be a reference, so the location of the reference is read from the trailer.
3.	The viewer moves its reading position to the location of the reference
4.	The Object id is read in. If this id is wrong, an error occurs. This is true for all pdf readers I've tested.
5.	The object is parsed, in this case it's the catalog dictionary

The catalog contains an entry that points at a page tree. There may also be additional tree structures with more information about the pages, like how they are to be numbered/indexed, bookmarked and such.

These structures are not entirely trivial to work with, as they are made to be friendly for search algorithms. This is likely helpful for slower computers. Regardless, by following the lowest nodes, we eventually reach the first page in the document.

## The document structure
A Pdf document is arranged in a hierarchal structure. We'll take a quick gander at key Pdf objects. Basically, a short summary of chapter 7.7 in the specification.

![doc](/img/doc.png?raw=true)

The catalog contains the whole document, the Page Tree contains all the pages, and so on. To get to grips with the document structure, you can try opening a PDF file while debugging with PdfLib, then browse it with the debugger.

![debug](/img/debug.png?raw=true)

# About PdfLib.Net

## Design

This library is laid out in a somewhat similar fashion as PdfSharp.
 * To avoid namespace clashes, most files are prefixed with "Pdf".
 * There are "Internal" namespaces for classes that are likely uninteresting. The classes themselves aren't internal; they are just placed there to be out of the way.

PdfLib does not give you access to "raw" pdf objects. This can be a problem if you wish to do something unusual, like making a corrupt or non-conformant PDF file.

![classes](/img/classes.png?raw=true)

As you can see, everything inherits from PdfItem. All objects inherit from PdfObject, and utility classes inherit Elements or ItemsArray (depending on whenever they wrap a dictionary or array).

## Parsing a page's contents
Pdf pages contains images, figures, and text. When a pdf viewer draws a page, it does this by executing commands found in the "Contents" property of the page. You can think of these commands as a simple programing language.

For instance, the commands on a page can be "Draw a line from A to B", followed by, "Draw text at position P"

The command stream follows the same lexical rules as the document, so the same lexer.cs class is used for the command stream as for the document structure.

### The compiler
Despite the name, _PdfCompiler.cs_ is not a true compiler. All it does is collect the tokens found by the lexer into whole pdf command objects. So for instance, the tokens "15 7 m" becomes a "MoveTo(15,7)" command.

There's no particular advantage to doing this, as "compilation" is very fast, compared to rendering, but I've found having command objects to be convenient.

### Page resources
Pdf files can have resources, like images, patterns, and fonts, which are used during the rendering of the page.

The command stream that are used to draw the page, does not referee to these resources directly, instead they are addressed by name. 

For instance, you can have a command that say, "/Font_nr_1 20 Tf", which is an instruction to draw text using the font named Font_nr_1, at the size of 20. The name of the resource is arbitrary, so to find "Font_nr_1" we must make a lookup in the page's resources for a pointer to that font.

### Color Space
Source files: _PdfLib.Pdf.ColorSpace.*_

With a strong focus on the print world, PDF files has wide support for ways of describing colors. You may be familiar with the common Red/Green/Blue color model. RGB is the standard of computer graphics, and within the limits of computer displays, it works very well.

However, the RGB model can't describe every single color. Nor does it describe characteristics such as, how reflective a color is, how the color behaves in different light conditions, etc. For instance, there's no way for RGB to describe a metallic color.

Printers are less limited, and may in fact have the ability to print metalic colors, and most certainly colors a typical computer monitor is incapalbe of reproducing.

To this end, all colors in a PDF document, including those of images, is assosiated with a color space. 

Pdf supports several color spaces, included embedded ICC profiles. Pdflib supports all non-ICC color spaces and uses alternate color spaces when it encounters ICC profiles. 

### Images
Source files: _PdfLib.Pdf.PdfImage.cs and PdfLib.Img.*_

PDF documents supports three image formats.
 * Raw image data: How to interpret this data is described in the PdfImage dictionary. It can be compressed using CCITT, JBig2 and deflate/lzw filters.
 * Jpeg images: Jpeg images can be inserted, as is, into PDF documents. These images can be saved right out to disk and viewed in most image editors. The only oddity is support for a non-standard Adobe jpeg tag. Images with that tag will not look right in non-Adobe compatible image viewers.
 * Jpeg 2000 images: Similarly to jpeg images, these can be inserted, as is, into PDF documents. Preferably, you should insert them wrapped into a JP2 container, but this is not required.

 ### Compressing

 PdfLib supports reading for all formats, but can only compress images using CCITT, Jpeg, Jpeg 2000 and deflate.

 ### Rendering images
 Source file: _PdfLib.Render.rImage.cs_

 PdfLib renders images to the specs. Even the special handling Jpeg 2000 images get is by the spec.

 This means, that to decode an image, PdfLib does all of this:

 1.	Each pixel component in the image is converted to a double number. This simplifies the algorithm a little. It would be better to only convert the pixel you're working on.
 2.	These double numbers are in an "unknown" format. To get them into a known format, pdf images supplies a Decode array. The decode array is used to transform the pixels into a format understood by the supplied color space.
 3.	Then the color space is used to convert the pixels into a bgra32 format.

 \+ There's additional code to handle pre-multiplied alpha and transparency masks. 

Now, this is a rather inefficient and memory intensive process. There's significant room for optimization, like not converting the whole image into doubles right away, but this slow algorithm is surprisingly fast. Instead of optimizing it, I added a "FastImageDecoding" path that handles the common cases more quickly. 

### Form
Source file: _PdfLib.Pdf.PdfForm.cs_

These dictionaries are very similar to page dictionaries. The main difference is that forms have a matrix transformation and a bounding box, instead of mediabox and the rotation properties.

The forms can be drawn onto a page and allows contents such as text and vector drawings to be reused.

### Patterns
Patterns can be thought of as colors with shape to them. They can be made using mathematical formulas, images, and vector drawings. PdfLib supports all but lattice mesh patterns.

The code is entirely unoptimized, and thus slow. This is particularly true for tiling patterns, where Pdflib will redraw every tile.

### Fonts
Source files: _PdfLib.Pdf.Font.*_

All fonts are described using a PdfFont dictionary. These dictionaries do not contain the glyphs themselves, they just describe the character encoding and all relevant glyph metrics needed for rendering.

PDF documents can contain five types of fonts:
 * Adobe Type1: This is a font format originally intended for PostScript printers. It's constructed in such a way that, in theory, you can upload the font straight to a printer, unmodified.
 * Adobe Type1c: A compact binary variant of Type1 fonts. The original Type1 format is very verbose, Type1c contains the same data but in a more compact form.
 * Adobe Type3: This font paints its glyphs using PDF draw commands.
 * Apple TrueType/OpenType: A development on Quickdraw spline fonts, this is a very simple format. Glyphs are just a series of points on and off a curve. The on points define start and stop, while the off points defines curvature. OpenType is basically the same format but can optionally contain an Adobe Type1c font instead of an Apple spline font.
 * External: A font need not be included in the PDF document. It's in this case up to the PDF viewer to supply the font. The specs define 14 such fonts, but many documents also use standard Windows fonts.

 There are further two ways of describing fonts in PDF documents:
  * Simple fonts
  * CID fonts

The simple fonts and CID fonts use the same underlying font formats (Type 1, TrueType, etc.), the difference lay in how glyph indexes are determined.

#### Finding glyphs

The trickiest part of understanding how fonts work is in how the raw data of a string is translated into a glyph. The confusion largely stems from PDF having multiple ways to find glyphs.

##### Simple fonts

These fonts are set up so that one byte of data is equivalent with one glyph.

To find a glyph, take the byte value of a character and find its char code value by using one of four standardized encoding tables. 

The next step depends on the font. Some fonts include Unicode to Glyph index tables, others has char code to GlyphIndex table. In case of the former, the char code must be translated to Unicode before doing a lookup in the font's internal table. This is done by using the ISOAdobe table. 

Depending on what cmap tables the font includes, you can then retrieve the correct glyph index using either Unicode or char code.

##### CID Fonts

One byte per character is not ideal for languages with large number of characters. CID fonts allow characters to be one or more bytes long. This leads to an additional complication over simple fonts, as now we need to find out how many bytes to read from the data to get one character.

Most CID fonts are set up to use two bytes per character, but it's possible to have fonts where the amount of bytes each character needs varies. To determine how much to read, CID font contains a PostScript character map (cmap) that tells how many bytes to read.

PostScript in this case is a programming language. What PdfLib does is execute this PostScript, and the end result is a cmap table. 

Once a char code has been read, the glyph index is found by either using a supplied PostScript cmap table (but not the same one mentioned earlier), or a CIDtoGID table. The latter is a tad simpler but serve the same function. The resulting value is a Glyph Index (GID) that can be used directly with the font.

Unlike with simple fonts, the font file itself need not contain any character mapping tables.

#### How fonts are in dealt with in PdfLib

PdfLib's handling of fonts can be a tad confusing. There are three main types of fonts in PdfLib, all which serve different purposes.

##### PdfLib.Pdf.Font.*

These fonts describe fonts as they are stored in the PDF document. They are not easy to work with, so they are typically transformed to either a rFont or a cFont.

##### PdfLib.Compose.cFont
Font embedding isn't entirely trivial. You don't have to use compose fonts, but they take care of tasks like kerning. Currently only TrueType/Opentype and the built in 14 fonts are supported.

When drawing text using a cFont, the font will automatically record which characters to embed, and from that decide what character encoding strategy to use. Note, this font type is a new addition to the library. There are likely functions that don't yet accept cFonts as input. In that case, just call cFont.MakeWrapper(). That's what the library does internally anyway.

## PostScript

Pdf files can also have postscript embedded. This is a feature rich programing language suited for printers. PdfLib is probably the only pdf library that goes to the trouble of actually executing this postscript, as others use heuristics to figure out what the postscript intents to do.

This ultimately means that documents with faulty postscript will render fine in Adobe Reader and fail in odd ways with PdfLib.

# Working with PdfLib.net

## Opening a Pdf document and extracting images

In this example, we will open a pdf document and iterate over the document's images and save them to disk.

```
// A string with the file path and name of the PdfDocument
var pdf_file = "c:/temp/test.pdf";

// Gets the filename of the pdf document
var filename = System.IO.Path.GetFileNameWithoutExtension(pdf_file);

// Creates a folder for where to store extracted images
var folder = string.Format("C:/temp/dump/{0}/", filename);
System.IO.Directory.CreateDirectory(folder);

// Opens the pdf file in read only mode. This file cannot be modified.
using(var pdf = PdfFile.OpenRead(pdf_file))
{
  // The library comes with an "AllImages" property that does all
  // the heavy lifting with finding images. (This list does not include
  // inline images).
  int c = 0;
  foreach (var image in pdf_file.AllImages)
  {
    // jpeg and jpeg 2000 images are stored in a complete form. That
    // means they can be stored straight out to disk.
    if (image.Format == PdfLib.Pdf.Internal.ImageFormat.JPEG ||
        image.Format == PdfLib.Pdf.Internal.ImageFormat.JPEG2000)
    {
      // Determines the name of the image
      var ext = "jpg";
      if (image.Format == PdfLib.Pdf.Internal.ImageFormat.JPEG)
        ext = "j2k";
      var sf = string.Format("{0}{1:00}.{2}", folder, ++c, ext);

      // Saves the image to disk
      File.WriteAllBytes(sf, image.Stream.RawStream);

      // Do note that pdf images has properties such as "Decode" and 
      // "Matte", which affects how an image looks in the document. By
      // saving the image raw, these properties are not taken into account.
    }
    else
    {
      // Saves all other images as 32-bit png images.
      var ext = "png";
      var sf = string.Format("{0}{1:00}.{2}", folder, ++c, ext);
      using (FileStream fs = File.Open(sf, FileMode.Create, FileAccess.Write))
      {
        // The plain DecodeImage function will spit out an image that 
        // conforms to how the image should look in a pdf viewer. 
        var decoded_image = rImage.DecodeImage(image);

        PngBitmapEncoder png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(decoded_image));
        png.Save(fs);
      }
    }
  }
}
```

## Creating a new Pdf document

```
//You don't need to create the document first, but this way saves
//us the trouble of adding pages to the document later, as now we
//can call NewPage.
var pdf = new WritableDocument();

//Creates a page in WritableDocument that has a size of 100,50
var page = pdf.NewPage(100,50);

//Draws a string to the first page
using(var draw = new cDraw(page))
{
  draw.DrawString(10, 10, "Hello world");
}

//Draws a textbox on page 2
using(var draw = new cDraw(pdf.NewPage()))
{
  draw.DrawString(10, 10, "Hello world");
}

//Saves the document
pdf.WriteTo("c:/temp/hello.pdf");
```

## Rotating pages in an existing document

```
// A string with the file path and name of the PdfDocument
var pdf_file = "c:/temp/test.pdf";

// Opens the pdf file in write mode. This file can be modified.
using(var pdf = PdfFile.OpenWrite(pdf_file))
{
	// Iterates over the pages
	foreach (var page in pdf)
	{
		// Rotates the page clockwise
		page.Rotation += 90;
	}

	// Problem, if we're to save over an existing document, we need
	// to make sure everything is in memory. A better approach is to
	// save to a temp file, then when the save succeeded, switch it
	// with the original file. That way you won't lose the data if
	// power goes out in the middle of a save.
	pdf.LoadAndCloseFile();

	// Overwrites the old file. 
	pdf.WriteTo(pdf_file);
}
```
