using System;
using System.Collections.Generic;
using System.IO;

namespace PdfLib.Util
{
    internal class ApacheLZW
    {
        #region Apache 2.0 License code

        /// <summary>
        /// The LZW clear table code.
        /// </summary>
        public const long CLEAR_TABLE = 256;
        /// <summary>
        /// The LZW end of data code.
        /// </summary>
        public const long EOD = 257;

        private byte[] _source;
        private int _source_pos;
        private byte _chunk;
        private int _bits_in_chunk;
        private int _n_bits_to_fetch;

        private int read()
        {
            int ret = 0;
            for (int i = 0; i < _n_bits_to_fetch && ret != -1; i++)
            {
                if (_bits_in_chunk == 0)
                {
                    if (_source_pos < _source.Length)
                    {
                        _chunk = _source[_source_pos++];
                        _bits_in_chunk = 8;
                    }
                    else
                    {
                        ret = -1;
                        break;
                    }
                }

                ret <<= 1;
                ret |= ((_chunk >> (_bits_in_chunk - 1)) & 0x1);
                _bits_in_chunk--;
            }
            return ret;
        }

        #endregion

        public byte[] Decode(byte[] data)
        {
            //log.debug("decode( )");
            _source = data;
            _n_bits_to_fetch = 9;
            _bits_in_chunk = 0;
            _source_pos = 0;
            _chunk = 0;
            LZWDictionary dic = new LZWDictionary();
            byte firstByte = 0;
            long nextCommand = 0;

            MemoryStream result = new MemoryStream();

            while ((nextCommand = read()) != EOD)
            {
                // log.debug( "decode - nextCommand=" + nextCommand + ", bitsInChunk: " + in.getBitsInChunk());

                if (nextCommand == CLEAR_TABLE)
                {
                    _n_bits_to_fetch = 9;
                    dic = new LZWDictionary();
                }
                else
                {
                    data = dic.getData(nextCommand);
                    if (data == null)
                    {
                        dic.visit(firstByte);
                        data = dic.getData(nextCommand);
                        dic.clear();
                    }
                    if (data == null)
                    {
                        throw new Exception("Error: data is null");
                    }
                    dic.visit(data);

                    //log.debug( "decode - dic.getNextCode(): " + dic.getNextCode());

                    if (dic.getNextCode() >= 2047)
                    {
                        _n_bits_to_fetch = 12;
                    }
                    else if (dic.getNextCode() >= 1023)
                    {
                        _n_bits_to_fetch = 11;
                    }
                    else if (dic.getNextCode() >= 511)
                    {
                        _n_bits_to_fetch = 10;
                    }
                    else
                    {
                        _n_bits_to_fetch = 9;
                    }
                    /*
                    if( in.getBitsInChunk() != dic.getCodeSize() )
                    {
                        in.unread( nextCommand );
                        in.setBitsInChunk( dic.getCodeSize() );
                        System.out.print( "Switching " + nextCommand + " to " );
                        nextCommand = in.read();
                        System.out.println( "" +  nextCommand );
                        data = dic.getData( nextCommand );
                    }*/
                    firstByte = data[0];
                    result.Write(data, 0, data.Length);
                }
            }

            return result.ToArray();
        }

        #region Helperclasses to the Apache 2.0 code

        /**
         * This is the used for the LZWDecode filter.  This represents the dictionary mappings
         * between codes and their values.
         *
         * @author <a href="mailto:ben@benlitchfield.com">Ben Litchfield</a>
         * @version $Revision: 1.4 $
         */
        class LZWDictionary
        {
            private Dictionary<long, byte[]> codeToData = new Dictionary<long, byte[]>();
            private LZWNode root = new LZWNode();

            private MemoryStream buffer = new MemoryStream();
            private long nextCode = 258;
            private int codeSize = 9;

            /**
             * constructor.
             */
            public LZWDictionary()
            {
                for (long i = 0; i < 256; i++)
                {
                    LZWNode node = new LZWNode();
                    node.setCode(i);
                    root.setNode((byte)i, node);
                    codeToData.Add(i, new byte[] { (byte)i });
                }
            }

            /**
             * This will get the value for the code.  It will return null if the code is not
             * defined.
             *
             * @param code The key to the data.
             *
             * @return The data that is mapped to the code.
             */
            public byte[] getData(long code)
            {
                byte[] ret;
                codeToData.TryGetValue(code, out ret);
                return ret;
            }

            /**
             * This will take a visit from a byte[].  This will create new code entries as
             * necessary.
             *
             * @param data The byte to get a visit from.
             *
             * @throws IOException If there is an error visiting this data.
             */
            public void visit(byte[] data)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    visit(data[i]);
                }
            }

            /**
             * This will take a visit from a byte.  This will create new code entries as
             * necessary.
             *
             * @param data The byte to get a visit from.
             *
             * @throws IOException If there is an error visiting this data.
             */
            public void visit(byte data)
            {
                buffer.WriteByte(data);
                byte[] curBuffer = buffer.ToArray();
                LZWNode previous = null;
                LZWNode current = root;
                bool createNewCode = false;
                for (int i = 0; i < curBuffer.Length && current != null; i++)
                {
                    previous = current;
                    current = current.getNode(curBuffer[i]);
                    if (current == null)
                    {
                        createNewCode = true;
                        current = new LZWNode();
                        previous.setNode(curBuffer[i], current);
                    }
                }
                if (createNewCode)
                {
                    long code = nextCode++;
                    current.setCode(code);
                    codeToData.Add(code, curBuffer);

                    /*
                    System.out.print( "Adding " + code + "='" );
                    for( int i=0; i<curBuffer.length; i++ )
                    {
                        String hex = Integer.toHexString( ((curBuffer[i]+256)%256) );
                        if( hex.length() <=1 )
                        {
                            hex = "0" + hex;
                        }
                        if( i != curBuffer.length -1 )
                        {
                            hex += " ";
                        }
                        System.out.print( hex.toUpperCase() );
                    }
                    System.out.println( "'" );
                    */
                    buffer.SetLength(0);
                    buffer.WriteByte(data);
                    resetCodeSize();
                }
            }

            /**
             * This will get the next code that will be created.
             *
             * @return The next code to be created.
             */
            public long getNextCode()
            {
                return nextCode;
            }

            /**
             * This will get the size of the code in bits, 9, 10, or 11.
             *
             * @return The size of the code in bits.
             */
            public int getCodeSize()
            {
                return codeSize;
            }

            /**
             * This will determine the code size.
             */
            private void resetCodeSize()
            {
                if (nextCode >= 2048)
                {
                    codeSize = 12;
                }
                else if (nextCode >= 1024)
                {
                    codeSize = 11;
                }
                else if (nextCode >= 512)
                {
                    codeSize = 10;
                }
                else
                {
                    codeSize = 9;
                }
            }

            /**
             * This will crear the internal buffer that the dictionary uses.
             */
            public void clear()
            {
                buffer.SetLength(0);
            }

            /**
             * This will folow the path to the data node.
             *
             * @param data The path to the node.
             *
             * @return The node that resides at that path.
             */
            public LZWNode getNode(byte[] data)
            {
                return root.getNode(data);
            }
        }

        /**
         * This is the used for the LZWDecode filter.
         *
         * @author <a href="mailto:ben@benlitchfield.com">Ben Litchfield</a>
         * @version $Revision: 1.4 $
         */
        class LZWNode
        {
            private long code;
            private Dictionary<byte, LZWNode> subNodes = new Dictionary<byte, LZWNode>();

            /**
             * This will get the number of children.
             *
             * @return The number of children.
             */
            public int childCount()
            {
                return subNodes.Count;
            }

            /**
             * This will set the node for a particular byte.
             *
             * @param b The byte for that node.
             * @param node The node to add.
             */
            public void setNode(byte b, LZWNode node)
            {
                subNodes.Add(b, node);
            }

            /**
             * This will get the node that is a direct sub node of this node.
             *
             * @param data The byte code to the node.
             *
             * @return The node at that value if it exists.
             */
            public LZWNode getNode(byte data)
            {
                LZWNode ret;
                subNodes.TryGetValue(data, out ret);
                return ret;
            }

            /**
             * This will traverse the tree until it gets to the sub node.
             * This will return null if the node does not exist.
             *
             * @param data The path to the node.
             *
             * @return The node that resides at the data path.
             */
            public LZWNode getNode(byte[] data)
            {
                LZWNode current = this;
                for (int i = 0; i < data.Length && current != null; i++)
                {
                    current = current.getNode(data[i]);
                }
                return current;
            }

            /** Getter for property code.
             * @return Value of property code.
             */
            public long getCode()
            {
                return code;
            }

            /** Setter for property code.
             * @param codeValue New value of property code.
             */
            public void setCode(long codeValue)
            {
                code = codeValue;
            }

        }

        #endregion
    }
}
