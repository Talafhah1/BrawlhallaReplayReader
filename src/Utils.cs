using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlhallaReplayReader
{
	///<summary>Class <c>Utils</c> is used to store utility functions.</summary>
	internal static class Utils
	{
		///<summary>Initializes the <c>Utilities</c> class.</summary>
		static Utils() { s_json_serializer_options.Converters.Add(new JsonStringEnumConverter()); }

		///<value>The default JSON serializer options.</value>
		public static readonly JsonSerializerOptions s_json_serializer_options = new() { WriteIndented = true };

		///<summery>Decompresses a stream using the ZLib compression algorithm.</summery>
		///<param name="compressed_stream">The compressed stream to decompress.</param>
		///<returns>The bytes of the decompressed stream.</returns>
		internal static byte[] DecompressStream(Stream compressed_stream)
		{
			using MemoryStream buffer_stream = new();
			using (ZLibStream zlib_stream = new(compressed_stream, CompressionMode.Decompress)) zlib_stream.CopyTo(buffer_stream);
			byte[] buffer = buffer_stream.ToArray();
			return buffer;
		}

		///<summery>Compresses a buffer using the ZLib compression algorithm.</summery>
		///<param name="uncompressed_stream">The uncompressed stream to compress.</param>
		///<returns>The compressed buffer.</returns>
		internal static byte[] CompressBuffer(byte[] uncompressed_buffer)
		{
			using MemoryStream compressed_stream = new();
			using (ZLibStream zlib_stream = new(compressed_stream, CompressionLevel.SmallestSize))
			{
				using MemoryStream buffer_stream = new(uncompressed_buffer);
				buffer_stream.CopyTo(zlib_stream);
			}
			byte[] compressed_buffer = compressed_stream.ToArray();
			return compressed_buffer;
		}

		///<summery>Calculates the population count of a 32-bit integer.</summery>
		///<param name="value">The value to calculate the population count of.</param>
		///<returns>The population count of the value.</returns>
		internal static uint PopulationCount(uint value)
		{
			value -= value >> 1 & 0x5555_5555;
			value = (value & 0x3333_3333) + (value >> 2 & 0x3333_3333);
			return (value + (value >> 4) & 0x0F0F_0F0F) * 0x0101_0101 >> 24;
		}

		///<summery>Converts a Unix time stamp to a <c>DateTime</c>.</summery>
		///<param name="unix_time_stamp">The Unix time stamp to convert.</param>
		///<returns>The <c>DateTime</c> represented by the Unix time stamp.</returns>
		internal static DateTime UnixTimeStampToDateTime(uint unix_time_stamp)
		{
			DateTime date_time = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
			date_time = date_time.AddSeconds(unix_time_stamp).ToLocalTime();
			return date_time;
		}
	}

	///<summary>Class <c>BitStream</c> is used to read bits from a byte array.</summary>
	internal class BitStream
	{
		///<value>The buffer to read from.</value>
		private readonly byte[] m_buffer;

		///<value>The current position in the buffer.</value>
		private int m_position = 0;

		///<value>The current bit position in the buffer.</value>
		private byte m_bit_position = 0;

		///<summary>Constructor used to create a Bit Stream.</summary>
		///<param name="buffer">The buffer to read from.</param>
		internal BitStream(byte[] buffer) { m_buffer = buffer; }

		///<value>The number of bits remaining in the buffer.</value>
		internal int RemainingBytes => m_buffer.Length - m_position;

		///<summary>Reads a specified number of bits from the buffer.</summary>
		///<param name="count">The number of bits to read.</param>
		///<returns>The bits read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal int ReadBits(int count)
		{
			int result = 0;
			while (count != 0)
			{
				if (m_position >= m_buffer.Length * 8) throw new EndOfStreamException("End of stream reached");
				bool bit = (m_buffer[m_position] & (1 << (7 - m_bit_position))) != 0;
				result |= (bit ? 1 : 0) << (count - 1);
				count--;
				m_bit_position++;
				if (m_bit_position == 8)
				{
					m_position++;
					m_bit_position = 0;
				}
			}
			return result;
		}

		///<summary>Reads a single bit from the buffer.</summary>
		///<returns>The bit read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal bool ReadBool() => ReadBits(1) != 0;

		///<summary>Reads a single byte from the buffer.</summary>
		///<returns>The byte read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal byte ReadByte() => (byte)ReadBits(8);

		///<summary>Reads a specified number of bytes from the buffer.</summary>
		///<param name="count">The number of bytes to read.</param>
		///<returns>The bytes read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bytes to read is greater than the number of bytes remaining in the buffer.</exception>
		internal byte[] ReadBytes(uint count)
		{
			byte[] result = new byte[count];
			for (int i = 0; i < count; i++) result[i] = ReadByte();
			return result;
		}

		///<summary>Reads a single short from the buffer.</summary>
		///<returns>The short read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal short ReadShort() => (short)ReadBits(16);

		///<summary>Reads a single int from the buffer.</summary>
		///<returns>The int read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal int ReadInt() => ReadBits(32);

		///<summary>Reads a single unsigned int from the buffer.</summary>
		///<returns>The unsigned int read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal uint ReadUInt() => (uint)ReadInt();

		///<summary>Reads a single char from the buffer.</summary>
		///<returns>The char read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal char ReadChar() => (char)ReadBits(8);

		///<summary>Reads a string from the buffer.</summary>
		///<returns>The string read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		///<exception cref="DecoderFallbackException">Thrown when the string contains invalid characters.</exception>
		///<remarks>Strings are encoded as a short specifying the length of the string, followed by the string itself.</remarks>
		internal string ReadString() => Encoding.UTF8.GetString(ReadBytes((ushort)ReadShort()));

		///<summary>Reads a single float from the buffer.</summary>
		///<returns>The float read from the buffer.</returns>
		///<exception cref="EndOfStreamException">Thrown when the number of bits to read is greater than the number of bits remaining in the buffer.</exception>
		internal float ReadFloat() => BitConverter.ToSingle(BitConverter.GetBytes(ReadInt()), 0);
	}

	///<summary>Class <c>InvalidReplayException</c> is used to throw an exception when a replay cannot be read properly.</summary>
	public class InvalidReplayException : Exception
	{
		public InvalidReplayException() { }

		public InvalidReplayException(string message) : base(message) { }

		public InvalidReplayException(string message, Exception inner) : base(message, inner) { }
	}

	///<summary>Class <c>InvalidReplayStateException</c> is used to throw an exception when a replay is in an invalid state.</summary>
	public class InvalidReplayStateException : InvalidReplayException
	{
		public InvalidReplayStateException() { }

		public InvalidReplayStateException(string message) : base(message) { }

		public InvalidReplayStateException(string message, Exception inner) : base(message, inner) { }
	}

	///<summary>Class <c>ReplayChecksumException</c> is used to throw an exception when a replay's checksum check doesn't match the checksum stored in the header.</summary>
	public class ReplayChecksumException : InvalidReplayException
	{
		public ReplayChecksumException() { }

		public ReplayChecksumException(string message) : base(message) { }

		public ReplayChecksumException(string message, Exception inner) : base(message, inner) { }
	}

	///<summary>Class <c>ReplayVersionException</c> is used to throw an exception when a replay's version check doesn't match the version stored in the header.</summary>
	public class ReplayVersionException : InvalidReplayException
	{
		public ReplayVersionException() { }

		public ReplayVersionException(string message) : base(message) { }

		public ReplayVersionException(string message, Exception inner) : base(message, inner) { }
	}

	///<summary>Class <c>InvalidReplayDataException</c> is used to throw an exception when a replay's data is invalid.</summary>
	///<remarks>For example, when a replay's contains no entities or when each entity has a invalid number of Legends.</remarks>
	public class InvalidReplayDataException : InvalidReplayException
	{
		public InvalidReplayDataException() { }

		public InvalidReplayDataException(string message) : base(message) { }

		public InvalidReplayDataException(string message, Exception inner) : base(message, inner) { }
	}

	///<summary>Class <c>ReplayPacket8Exception</c> is used to throw an exception when a replay has packet type 8.</summary>
	///<remarks>Packet type 8 means the end of an invalid replay was reached.</remarks>
	public class ReplayPacket8Exception : InvalidReplayException
	{
		public ReplayPacket8Exception() { }

		public ReplayPacket8Exception(string message) : base(message) { }

		public ReplayPacket8Exception(string message, Exception inner) : base(message, inner) { }
	}
}