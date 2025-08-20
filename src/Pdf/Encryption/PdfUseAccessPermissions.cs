using System;

namespace PdfLib.Pdf.Encryption
{
    /// <summary>
    /// Permissions for use of this document
    /// </summary>
    [Flags()]
    public enum PdfUseAccessPermissions
    {
        /// <summary>
        /// Print the document, may not be at full quality
        /// </summary>
        PRINT = 0x4,

        /// <summary>
        /// Modify things not affected by other permissions
        /// </summary>
        MODIFY_OTHER = 0x8,

        /// <summary>
        /// Copy text and graphics from the document
        /// </summary>
        COPY = 0x10,

        /// <summary>
        /// Add or modify annotations and forms.
        /// </summary>
        ADDORMODIFY = 0x20,


        /// <summary>
        /// Fill in forms
        /// </summary>
        FILLFORM = 0x100,

        /// <summary>
        /// Extract text and graphics
        /// </summary>
        EXTRACT = 0x200,

        /// <summary>
        /// Rotate pages, remove pages, insert pages
        /// </summary>
        ASSEMBLE = 0x400,

        /// <summary>
        /// Print at high quality
        /// </summary>
        PRINTHQ = 0x800
    }
}
