using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BtSnoozDecoder
{
    class Program
    {
        private static readonly byte[] SnoopHeader = {(byte) 'b', (byte) 't', (byte) 's', (byte) 'n', (byte) 'o', (byte) 'o', (byte) 'p', 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x03, 0xea};

        static void Main(string[] args)
        {
            try
            {
                Run(args);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }    
        }
        
        private static void Run(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: BtSnoozDecoder errorlog.txt btsnoop.cap");
                return;
            }
            var base64 = FindSnoopData(args[0]);
            var snooz = Convert.FromBase64String(base64);
            using var stream = new FileStream(args[1], FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
            using var writer = new BinaryWriter(stream);
            DecodeSnooz(snooz, writer);
        }

        private static string FindSnoopData(string filename)
        {
            using var reader = File.OpenText(filename);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("--- BEGIN:BTSNOOP_LOG_SUMMARY")) return ReadSnoopData(reader);
            }
            throw new FormatException("BTSNOOP data not found in file");
        }

        private static string ReadSnoopData(StreamReader reader)
        {
            var buffer = new StringBuilder();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("--- END:BTSNOOP_LOG_SUMMARY")) return buffer.ToString();
                buffer.Append(line);
            }
            throw new FormatException("File truncated early, BTSNOOP trailer not found");
        }
        
        private static void DecodeSnooz(byte[] data, BinaryWriter writer)
        {
            var version = data[0];
            var lastTimestamp = BitConverter.ToUInt64(data, 1);
            // 9 offset, 2 more to skip the zlib header which .NET DeflateStream is unable to handle
            var decompressed = Decompress(data, 9 + 2);
            writer.Write(SnoopHeader);
            switch (version)
            {
                case 1:
                    DecodeVersion1(decompressed, lastTimestamp, writer);
                    break;
                case 2:
                    DecodeVersion2(decompressed, lastTimestamp, writer);
                    break;
                default:
                    throw new FormatException("Invalid data or unsupported version");
            }
        }

        private static byte[] Decompress(byte[] data, int offset)
        {
            using var stream = new MemoryStream(data, offset, data.Length - offset, false);
            //using var decompressingStream = new InflaterInputStream(stream);
            using var decompressingStream = new DeflateStream(stream, CompressionMode.Decompress);
            using var buffer = new MemoryStream((data.Length - offset) * 2);
            decompressingStream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static void DecodeVersion1(byte[] decompressed, ulong lastTimestampMs, BinaryWriter writer)
        {
            throw new NotImplementedException("Version 1 not supported yet");
        }

        private static void DecodeVersion2(byte[] decompressed, ulong lastTimestampMs, BinaryWriter writer)
        {
            // An unfortunate consequence of the file format design: we have to do a
            // pass of the entire file to determine the timestamp of the first packet.
            var firstTimestampMs = lastTimestampMs + 0x00dcddb30f2f8000UL;
            var offset = 0;
            while (offset < decompressed.Length)
            {
                var length = BitConverter.ToUInt16(decompressed, offset);
                // var packetLength = BitConverter.ToUInt16(decompressed, offset + 2);
                var deltaTimeMs = BitConverter.ToUInt32(decompressed, offset + 4);
                // var snoozType = decompressed[offset + 8];
                offset += 9 + length - 1;
                firstTimestampMs -= deltaTimeMs;
            }
            // Second pass does the actual writing out to stdout.
            offset = 0;
            while (offset < decompressed.Length)
            {
                uint length = BitConverter.ToUInt16(decompressed, offset);
                uint packetLength = BitConverter.ToUInt16(decompressed, offset + 2);
                uint deltaTimeMs = BitConverter.ToUInt32(decompressed, offset + 4);
                var snoozType = decompressed[offset + 8];
                firstTimestampMs += deltaTimeMs;
                offset += 9;
                writer.Write(SwapBytes(packetLength));
                writer.Write(SwapBytes(length));
                writer.Write(SwapBytes(TypeToDirection(snoozType)));
                writer.Write((uint) 0);
                writer.Write(SwapBytes((uint) (firstTimestampMs >> 32)));
                writer.Write(SwapBytes((uint) (firstTimestampMs & 0xFFFFFFFF)));
                writer.Write(TypeToHci(snoozType));
                writer.Write(decompressed, offset, (int) length - 1);
                offset += (int) length - 1;
            }
        }

        // Enumeration of the values the 'type' field can take in a btsnooz
        // header. These values come from the Bluetooth stack's internal
        // representation of packet types.
        private const byte TypeInEvt = 0x10;
        private const byte TypeInAcl = 0x11;
        private const byte TypeInSco = 0x12;
        private const byte TypeOutCmd = 0x20;
        private const byte TypeOutAcl = 0x21;
        private const byte TypeOutSco = 0x22;

        /// Returns the inbound/outbound direction of a packet given its type.
        /// 0 = sent packet
        /// 1 = received packet
        private static uint TypeToDirection(byte type)
        {
            return (type == TypeInEvt || type == TypeInAcl || type == TypeInSco)
                ? 1U
                : 0U;
        }

        /// <summary>
        /// Returns the HCI type of a packet given its btsnooz type.
        /// </summary>
        private static byte TypeToHci(byte type)
        {
            switch (type)
            {
                case TypeOutCmd:
                    return 0x01;
                case TypeInAcl:
                case TypeOutAcl:
                    return 0x02;
                case TypeInSco:
                case TypeOutSco:
                    return 0x03;
                case TypeInEvt:
                    return 0x04;
                default:
                    throw new FormatException("Unsupported packet type");
            }
        }

        private static ushort SwapBytes(ushort x) => (ushort) ((ushort) ((x & 0xff) << 8) | ((x >> 8) & 0xff));

        private static uint SwapBytes(uint x) =>
            ((x & 0x000000ff) << 24) +
            ((x & 0x0000ff00) << 8) +
            ((x & 0x00ff0000) >> 8) +
            ((x & 0xff000000) >> 24);
    }
}