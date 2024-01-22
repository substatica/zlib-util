using Ionic.Zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace zlib_util
{
    class Program
    {
        public enum Mode
        {
            None,
            Extract,
            Compress
        }

        public class Options
        {
            [Option('x', "extract", Required = false, HelpText = "Path to file to extract")]
            public string Extract { get; set; }

            [Option('c', "compress", Required = false, HelpText = "Path to file to compress")]
            public string Compress { get; set; }

            [Option('r', "reference", Required = false, HelpText = "Path to compressed reference file")]
            public string Reference { get; set; }

            [Option('l', "level", Required = false, Default = 6, HelpText = "Compression level 0 = none, 1 = best speed, 2 = slower, (...), 6 = default, (...) , 9 = slowest/best compression")]
            public int CompressionLevel { get; set; }
        }

        static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
        {
            var helpText = HelpText.AutoBuild(result, h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.Heading = "  substatica";
                h.Copyright = "  youtube.com/substatica";
                h.AdditionalNewLineAfterOption = true;
                return HelpText.DefaultParsingErrorsHandler(result, h);
            }, e => e);
            Console.WriteLine(helpText);
        }

        static byte[] chunk_header_bytes = new byte[8];
        static int max_chunk_size = 0;

        static Mode mode = Mode.None;
        static Ionic.Zlib.CompressionLevel compression_level = (Ionic.Zlib.CompressionLevel)6;

        static void Main(string[] args)
        {
            var parser = new CommandLine.Parser(with => with.HelpWriter = null);
            var parserResult = parser.ParseArguments<Options>(args);

            string filename = "";
            string reference = "";

            parserResult
                .WithNotParsed(errs => DisplayHelp(parserResult, errs))
                .WithParsed<Options>(o =>
                {
                    if (!String.IsNullOrEmpty(o.Extract) && !String.IsNullOrEmpty(o.Compress))
                    {
                        Console.WriteLine("One command only please. Extract or compress.");
                        System.Environment.Exit(1);
                    }

                    if (!String.IsNullOrEmpty(o.Extract))
                    {
                        filename = o.Extract;
                        mode = Mode.Extract;
                    }
                    else if (!String.IsNullOrEmpty(o.Compress) && String.IsNullOrEmpty(o.Reference))
                    {

                    }
                    else if (!String.IsNullOrEmpty(o.Compress) && !String.IsNullOrEmpty(o.Reference))
                    {
                        reference = o.Reference;
                        filename = o.Compress;
                        mode = Mode.Compress;
                    }

                    compression_level = (Ionic.Zlib.CompressionLevel)o.CompressionLevel;
                });

            if (!File.Exists(filename) || (mode == Mode.Compress && !File.Exists(reference)))
            {
                Console.WriteLine("  Error: Could not read file");
                System.Environment.Exit(1);
            }

            if (mode == Mode.Extract)
            {
                var binary_reader = new BinaryReader(File.Open(filename, FileMode.Open));
                byte[] file_header_bytes = binary_reader.ReadBytes(8);

                List<byte[]> decompressed_chunks = new List<byte[]>();

                byte[] decompressed_file = new byte[0];

                while (binary_reader.BaseStream.Position < binary_reader.BaseStream.Length)
                {
                    var uncompressed_chunk = DecompressChunk(binary_reader);
                    decompressed_file = decompressed_file.Concat(uncompressed_chunk).ToArray();
                    decompressed_chunks.Add(uncompressed_chunk);
                }

                binary_reader.Close();

                File.WriteAllBytes(filename + ".extracted", decompressed_file);
            }
            else if (mode == Mode.Compress)
            {
                // extract one chunk from reference to populate max chunk size and headers
                var binary_reader = new BinaryReader(File.Open(reference, FileMode.Open));
                byte[] file_header_bytes = binary_reader.ReadBytes(8);
                DecompressChunk(binary_reader);
                binary_reader.Close();

                byte[] uncompressed_file = File.ReadAllBytes(filename);
                List<byte[]> uncompressed_chunks = new List<byte[]>();

                // even chunks
                for (int i = 0; i + max_chunk_size < uncompressed_file.Length; i += max_chunk_size)
                {
                    byte[] localchunk = new byte[max_chunk_size];
                    Array.Copy(uncompressed_file, i, localchunk, 0, max_chunk_size);
                    uncompressed_chunks.Add(localchunk);
                }
                // remainder
                int remainder = uncompressed_file.Length % max_chunk_size;
                byte[] chunk = new byte[remainder];
                Array.Copy(uncompressed_file, uncompressed_file.Length - remainder, chunk, 0, remainder);
                uncompressed_chunks.Add(chunk);

                byte[] compressed_file = file_header_bytes;
                foreach (var uncompressed_chunk in uncompressed_chunks)
                {
                    // chunk header
                    compressed_file = compressed_file.Concat(chunk_header_bytes).ToArray();

                    // max chunk size
                    compressed_file = compressed_file.Concat(BitConverter.GetBytes(max_chunk_size)).ToArray();
                    compressed_file = compressed_file.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToArray();

                    byte[] compressed_chunk = ZlibCodecCompress(uncompressed_chunk);

                    // compressed size
                    compressed_file = compressed_file.Concat(BitConverter.GetBytes(compressed_chunk.Length)).ToArray();
                    compressed_file = compressed_file.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToArray();
                    // decompressed size
                    compressed_file = compressed_file.Concat(BitConverter.GetBytes(uncompressed_chunk.Length)).ToArray();
                    compressed_file = compressed_file.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToArray();

                    // repeat
                    compressed_file = compressed_file.Concat(BitConverter.GetBytes(compressed_chunk.Length)).ToArray();
                    compressed_file = compressed_file.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToArray();
                    compressed_file = compressed_file.Concat(BitConverter.GetBytes(uncompressed_chunk.Length)).ToArray();
                    compressed_file = compressed_file.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToArray();
                    compressed_file = compressed_file.Concat(compressed_chunk).ToArray();
                }

                File.WriteAllBytes(filename + ".compressed", compressed_file);
            }            
        }

        static byte[] DecompressChunk(BinaryReader binary_reader)
        {
            // Skip UAsset Bytes and 0 Int
            chunk_header_bytes = binary_reader.ReadBytes(8);

            max_chunk_size = ReadInt16(binary_reader);

            var compressed_chunksize = ReadInt16(binary_reader);
            var uncompressed_chunksize = ReadInt16(binary_reader);

            // Skip second compressed size and uncompressed size
            binary_reader.BaseStream.Seek(16, SeekOrigin.Current);

            var compressed_chunk = new Byte[compressed_chunksize];
            binary_reader.BaseStream.Read(compressed_chunk, 0, compressed_chunksize);
            return ZlibCodecDecompress(compressed_chunk);
        }

        static int ReadInt16(BinaryReader binary_reader)
        {
            var bytes = binary_reader.ReadBytes(8);
            return BitConverter.ToInt32(bytes, 0);
        }

        // https://github.com/eropple/dotnetzip/blob/master/Examples/C%23/ZLIB/ZlibDeflateInflate.cs
        private static byte[] ZlibCodecDecompress(byte[] compressed)
        {
            int outputSize = 2048;
            byte[] output = new Byte[outputSize];

            // If you have a ZLIB stream, set this to true.  If you have
            // a bare DEFLATE stream, set this to false.
            bool expectRfc1950Header = true;

            using (MemoryStream ms = new MemoryStream())
            {
                ZlibCodec compressor = new ZlibCodec();
                compressor.InitializeInflate(expectRfc1950Header);

                compressor.InputBuffer = compressed;
                compressor.AvailableBytesIn = compressed.Length;
                compressor.NextIn = 0;
                compressor.OutputBuffer = output;

                foreach (var f in new FlushType[] { FlushType.None, FlushType.Finish })
                {
                    int bytesToWrite = 0;
                    do
                    {
                        compressor.AvailableBytesOut = outputSize;
                        compressor.NextOut = 0;
                        compressor.Inflate(f);

                        bytesToWrite = outputSize - compressor.AvailableBytesOut;
                        if (bytesToWrite > 0)
                            ms.Write(output, 0, bytesToWrite);
                    }
                    while ((f == FlushType.None && (compressor.AvailableBytesIn != 0 || compressor.AvailableBytesOut == 0)) ||
                           (f == FlushType.Finish && bytesToWrite != 0));
                }

                compressor.EndInflate();

                return ms.ToArray();
            }
        }

        private static byte[] ZlibCodecCompress(byte[] uncompressed)
        {
            int outputSize = 2048;
            byte[] output = new Byte[outputSize];
            int lengthToCompress = uncompressed.Length;

            // If you want a ZLIB stream, set this to true.  If you want
            // a bare DEFLATE stream, set this to false.
            bool wantRfc1950Header = true;

            using (MemoryStream ms = new MemoryStream())
            {
                ZlibCodec compressor = new ZlibCodec();
                compressor.InitializeDeflate(Ionic.Zlib.CompressionLevel.Default, wantRfc1950Header);

                compressor.InputBuffer = uncompressed;
                compressor.AvailableBytesIn = lengthToCompress;
                compressor.NextIn = 0;
                compressor.OutputBuffer = output;

                foreach (var f in new FlushType[] { FlushType.None, FlushType.Finish })
                {
                    int bytesToWrite = 0;
                    do
                    {
                        compressor.AvailableBytesOut = outputSize;
                        compressor.NextOut = 0;
                        compressor.Deflate(f);

                        bytesToWrite = outputSize - compressor.AvailableBytesOut;
                        if (bytesToWrite > 0)
                            ms.Write(output, 0, bytesToWrite);
                    }
                    while ((f == FlushType.None && (compressor.AvailableBytesIn != 0 || compressor.AvailableBytesOut == 0)) ||
                           (f == FlushType.Finish && bytesToWrite != 0));
                }

                compressor.EndDeflate();

                ms.Flush();
                return ms.ToArray();
            }
        }

        static bool ReplaceBytes(byte[] sourceBytes, byte[] patternArray, int valueOffset, int newValue, int startOffset = 0)
        {
            int offset = GetPositionAfterMatch(sourceBytes, patternArray, startOffset);

            if (offset < 0)
            {
                return false;
            }

            byte[] intBytes = BitConverter.GetBytes(newValue);
            for(int i = 0; i < intBytes.Length;i++)
            {
                sourceBytes[offset + valueOffset + i] = intBytes[i];
            }
            
            return true;
        }

        static string BytesToHexString(byte[] bytes)
        {
            string result = "{ ";
            foreach (byte b in bytes)
            {
                result += $"0x{ b:x2}, ";
            }
            result += " }";
            return result;
        }

        static int GetPositionAfterMatch(byte[] data, byte[] pattern, int startOffest = 0)
        {
            try
            {
                for (int i = startOffest; i < data.Length - pattern.Length; i++)
                {
                    bool match = true;
                    for (int k = 0; k < pattern.Length; k++)
                    {
                        if (data[i + k] != pattern[k])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return i + pattern.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
            }
            return -1;
        }
    }
}
