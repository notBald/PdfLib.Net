namespace PdfLib.Img.Tiff
{
    /// <summary>
    /// Represents a base exception for TIFF-related errors in the PDF library.
    /// </summary>
    public class TiffException : PdfLibException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TiffException"/> class.
        /// </summary>
        public TiffException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public TiffException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when there is an error reading a TIFF file.
    /// </summary>
    public class TiffReadException : TiffException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TiffReadException"/> class.
        /// </summary>
        public TiffReadException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffReadException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public TiffReadException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when the end of TIFF data is reached unexpectedly.
    /// </summary>
    public class EndOfTiffDataException : TiffException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EndOfTiffDataException"/> class.
        /// </summary>
        public EndOfTiffDataException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="EndOfTiffDataException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public EndOfTiffDataException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when invalid TIFF data is encountered.
    /// </summary>
    public class InvalidTiffDataException : TiffException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTiffDataException"/> class.
        /// </summary>
        public InvalidTiffDataException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTiffDataException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public InvalidTiffDataException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when a TIFF feature is not implemented.
    /// </summary>
    public class TiffNotImplementedException : TiffException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TiffNotImplementedException"/> class.
        /// </summary>
        public TiffNotImplementedException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffNotImplementedException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public TiffNotImplementedException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when an old JPEG format is encountered in a TIFF file.
    /// </summary>
    /// <remarks>
    /// Old jpeg refers to "TIFF JPEG Compression Type 6"
    /// 
    /// This format was an early attempt to integrate JPEG compression into TIFF files but was never fully standardized, 
    /// leading to various compatibility issues.
    /// </remarks>
    public class TiffIsOldJpegException : TiffNotImplementedException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TiffIsOldJpegException"/> class with a default error message.
        /// </summary>
        public TiffIsOldJpegException() : this("Old jpeg is not supported") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffIsOldJpegException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public TiffIsOldJpegException(string msg)
            : base(msg)
        { }
    }

    /// <summary>
    /// Represents an exception that is thrown when a non-uniform TIFF format is encountered.
    /// Currently, this is only thrown when trying to save an image that has different amounts of bits per color channel.
    /// You can still save these images by packing them into a JPEG 2000 container, though this is poorly supported by TIFF viewers.
    /// </summary>
    public class TiffIsNonUniformException : TiffNotImplementedException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TiffIsNonUniformException"/> class with a default error message.
        /// </summary>
        public TiffIsNonUniformException() : this("Non-uniform tiff is poorly supported") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffIsNonUniformException"/> class with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public TiffIsNonUniformException(string msg)
            : base(msg)
        { }
    }
}
