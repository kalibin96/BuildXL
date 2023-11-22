// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;

#nullable enable
#pragma warning disable CS3008 // CLS

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Content identification hash.
    /// </summary>
    public readonly struct ContentHash : IEquatable<ContentHash>, IComparable<ContentHash>
    {
        /// <summary>
        ///     Number of hash bytes serialized.
        /// </summary>
        public enum SerializeHashBytesMethod
        {
            /// <summary>
            ///     Only the actual bytes required are serialized
            /// </summary>
            Trimmed,

            /// <summary>
            ///     The full buffer is serialized, wasting some space but faster.
            /// </summary>
            Full
        }

        /// <summary>
        ///     Maximum hash bytes size, in bytes.
        /// </summary>
        public const int MaxHashByteLength = ReadOnlyFixedBytes.MaxLength;

        /// <summary>
        ///     Size, in bytes, of serialized form.
        /// </summary>
        public const int SerializedLength = MaxHashByteLength + 1;

        /// <summary>
        /// HashType.
        /// </summary>
        public readonly HashType _hashType;

        internal const char SerializedDelimiter = ':';

        private readonly ReadOnlyFixedBytes _bytes;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from bytes
        /// </summary>
        private ContentHash(HashType hashType, ReadOnlyFixedBytes bytes)
        {
            _hashType = hashType;
            _bytes = bytes;
        }

        /// <summary>
        ///     Uninitialized content hash used with errors.
        /// </summary>
        public ContentHash(HashType hashType)
            : this()
        {
            _hashType = hashType;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from string
        /// </summary>
        public ContentHash(string serialized)
        {
            if (!TryParse(serialized, out this))
            {
                throw new ArgumentException($"{serialized} is not a recognized content hash");
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from byte array
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public ContentHash(HashType hashType, byte[] buffer, int offset = 0)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            Contract.Requires(hashType != HashType.Unknown);

            int hashBytesLength = HashInfoLookup.Find(hashType).ByteLength;
            if (buffer.Length < (hashBytesLength + offset))
            {
                throw new ArgumentException($"Buffer undersized length=[{buffer.Length}] for hash type=[{hashType}]");
            }

            _hashType = hashType;
            _bytes = new ReadOnlyFixedBytes(buffer, hashBytesLength, offset);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from byte array
        /// </summary>
        public ContentHash(HashType hashType, ReadOnlySpan<byte> buffer)
        {
            Contract.Requires(hashType != HashType.Unknown);

            int hashBytesLength = HashInfoLookup.Find(hashType).ByteLength;
            if (buffer.Length < hashBytesLength)
            {
                throw new ArgumentException($"Buffer undersized length=[{buffer.Length}] for hash type=[{hashType}]");
            }

            _hashType = hashType;
            _bytes = new ReadOnlyFixedBytes(buffer.Slice(start: 0, length: hashBytesLength));
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from byte array
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public ContentHash(byte[] buffer, int offset = 0, SerializeHashBytesMethod serializeMethod = SerializeHashBytesMethod.Trimmed)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            _hashType = (HashType)buffer[offset++];
            var length = serializeMethod == SerializeHashBytesMethod.Trimmed
                ? HashInfoLookup.Find(_hashType).ByteLength
                : MaxHashByteLength;
            _bytes = new ReadOnlyFixedBytes(buffer, length, offset);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from binary reader
        /// </summary>
        public ContentHash(BinaryReader reader)
        {
            _hashType = (HashType)reader.ReadByte();
            _bytes = ReadOnlyFixedBytes.ReadFrom(reader, ReadOnlyFixedBytes.MaxLength);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHash" /> struct from binary reader
        /// </summary>
        public ContentHash(HashType hashType, BinaryReader reader)
            : this()
        {
            Contract.Requires(hashType != HashType.Unknown);

            _hashType = hashType;
            _bytes = ReadOnlyFixedBytes.ReadFrom(reader, ReadOnlyFixedBytes.MaxLength);
        }

        /// <summary>
        /// Gets whether a ContentHash is valid.
        /// </summary>
        /// <remarks>
        /// ContentHash is a structure whose default initial state is invalid.
        /// </remarks>
        public bool IsValid => HashType != HashType.Unknown && _bytes.Length != 0;

        /// <summary>
        ///     Gets hashType
        /// </summary>
        public HashType HashType => _hashType;

        /// <summary>
        ///     Gets number of bytes used for the hash.
        /// </summary>
        public int ByteLength => HashType == HashType.Unknown ? 0 : HashInfoLookup.Find(HashType).ByteLength;

        /// <summary>
        ///     Gets number of characters used for the hash string form.
        /// </summary>
        public int StringLength => HashType == HashType.Unknown ? 0 : HashInfoLookup.Find(HashType).StringLength;

        /// <summary>
        ///     Returns the lengths of the underlying byte array.
        /// </summary>
        public int Length => _bytes.Length;

        /// <summary>
        ///     Return hash byte at zero-based position.
        /// </summary>
        public byte this[int index] => _bytes[index];

        /// <summary>
        ///     Gets get the hash value packed in a minimally-sized byte array.
        /// </summary>
        public byte[] ToHashByteArray()
        {
            var buffer = new byte[ByteLength];
            SerializeHashBytes(buffer);
            return buffer;
        }

        /// <summary>
        ///     Gets the value packed in a FixedBytes.
        /// </summary>
        public ReadOnlyFixedBytes ToFixedBytes()
        {
            return _bytes;
        }

        /// <inheritdoc />
        public bool Equals([AllowNull]ContentHash other)
        {
            return _bytes.Equals(other._bytes) && ((int)_hashType).Equals((int)other._hashType);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            return ((int)_hashType, _bytes.GetHashCode()).GetHashCode();
        }

        /// <inheritdoc />
        public int CompareTo([MaybeNull]ContentHash other)
        {
            var compare = _bytes.CompareTo(other._bytes);
            return compare != 0 ? compare : ((int)_hashType).CompareTo((int)other._hashType);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Serialize();
        }

        /// <summary>
        /// Produces shorter string representation of the hash.
        /// </summary>
        public string ToShortString(bool includeHashType = true)
        {
            return AsShortHash().ToString(includeHashType);
        }

        /// <nodoc />
        public ShortHash AsShortHash() => new ShortHash(this);

        /// <summary>
        ///     Give the hash bytes as a hex string.
        /// </summary>
        public string ToHex(int? stringLength = null)
        {
            // _bytes.ToHex takes 'length' argument that represents the number of bytes that needs to be converted to a final string.
            // But 'stringLength' represents the final size of the string, not the number of bytes converted.
            // So if the requested string length is an odd number, then we still have to remove one character from the final string.
            stringLength ??= StringLength;
            Contract.Requires(stringLength <= StringLength, $"stringLength is incorrect. stringLength = {stringLength}");

            var result = _bytes.ToHex((stringLength.Value + 1) / 2);
            if (stringLength.Value % 2 != 0)
            {
                // The requested string length is odd
                result = result.Substring(0, stringLength.Value);
            }

            return result;
        }

        /// <summary>
        ///     Serialize to a string.
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public string Serialize(char delimiter = SerializedDelimiter)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            return HashType.Serialize() + delimiter + ToHex();
        }

        /// <summary>
        ///     Serialize to a string.
        /// </summary>
        public string SerializeReverse(char delimiter = SerializedDelimiter)
        {
            return ToHex() + delimiter + HashType.Serialize();
        }

        /// <summary>
        ///     Serialize to a buffer.
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public void Serialize(byte[] buffer, int offset = 0, SerializeHashBytesMethod serializeMethod = SerializeHashBytesMethod.Trimmed)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            unchecked
            {
                buffer[offset++] = (byte)_hashType;
            }

            var length = serializeMethod == SerializeHashBytesMethod.Trimmed ? ByteLength : MaxHashByteLength;
            _bytes.Serialize(buffer, length, offset);
        }

        /// <summary>
        ///     Serialize to a span.
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public int Serialize(Span<byte> buffer, int offset = 0, SerializeHashBytesMethod serializeMethod = SerializeHashBytesMethod.Trimmed)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            var length = serializeMethod == SerializeHashBytesMethod.Trimmed ? ByteLength : MaxHashByteLength;
            return Serialize(buffer, offset, length);
        }

        /// <summary>
        ///     Serialize to a span.
        /// </summary>
        public int Serialize(Span<byte> buffer, int offset, int length)
        {
            unchecked
            {
                buffer[offset++] = (byte)_hashType;
            }

            return _bytes.Serialize(buffer.Slice(offset), length) + 1;
        }

        /// <summary>
        ///     Serialize whole value to a target span.
        /// </summary>
        public int Serialize(Span<byte> targetSpan)
        {
            unchecked
            {
                targetSpan[0] = (byte)_hashType;
            }

            return _bytes.Serialize(targetSpan.Slice(1)) + 1;
        }

        /// <summary>
        ///     Serialize hash type and hash to buffer.
        ///     Consider using ContentHashExtensions.ToPooledByteArray instead to avoid extra allocations.
        /// </summary>
        public byte[] ToByteArray(SerializeHashBytesMethod serializeMethod = SerializeHashBytesMethod.Trimmed)
        {
            // First byte is hash type
            var length = 1 + (serializeMethod == SerializeHashBytesMethod.Trimmed ? ByteLength : MaxHashByteLength);
            var buffer = new byte[length];
            buffer[0] = (byte)_hashType;
            _bytes.Serialize(buffer, length - 1, offset: 1);
            return buffer;
        }

        /// <nodoc />
        public static ContentHash FromFixedBytes(HashType hashType, ReadOnlyFixedBytes bytes)
        {
            return new ContentHash(hashType, bytes);
        }

        /// <summary>
        /// Creates an instance of <see cref="ContentHash"/> from a sequence of bytes.
        /// </summary>
        public static ContentHash FromSpan(ReadOnlySpan<byte> source)
        {
            Contract.Requires(!source.IsEmpty);
            var hashType = (HashType)source[0];
            var fixedBytes = ReadOnlyFixedBytes.FromSpan(source.Slice(start: 1));
            return FromFixedBytes(hashType, fixedBytes);
        }

        /// <summary>
        ///     Serialize only the hash bytes to a buffer.
        /// </summary>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public void SerializeHashBytes(byte[] buffer, int offset = 0)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            _bytes.Serialize(buffer, ByteLength, offset);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            using var handle = ContentHashExtensions.ContentHashBytesArrayPool.Get();
            writer.Write((byte)_hashType);
            // For some weird reason this method always writes 33 bytes
            // when SerializeHashBytes writes ByteLength that can be less then 33 bytes.
            // This behavior is important and can't be broken because of binary compatibility.
            _bytes.Serialize(writer, handle.Value);
        }

        /// <summary>
        ///     Serialize only the hash bytes to a binary writer.
        /// </summary>
        /// <remarks>
        ///     Unlike <see cref="Serialize(BinaryWriter)"/> method that writes <see cref="SerializedLength"/> number of bytes,
        ///     this method only writes <see cref="ByteLength"/> number of bytes that can be smaller for some hash types.
        /// </remarks>
#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters
        public void SerializeHashBytes(BinaryWriter writer, byte[]? buffer = null)
#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters
        {
            if (buffer is null)
            {
                using var handle = ContentHashExtensions.ContentHashBytesArrayPool.Get();
                _bytes.Serialize(writer, handle.Value, 0, ByteLength);
            }
            else
            {
                _bytes.Serialize(writer, buffer, 0, ByteLength);
            }
        }

        /// <nodoc />
        public static bool operator ==(ContentHash left, ContentHash right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ContentHash left, ContentHash right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(ContentHash left, ContentHash right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <nodoc />
        public static bool operator >(ContentHash left, ContentHash right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static ContentHash Random(HashType hashType = HashType.Vso0)
        {
            var hashInfo = HashInfoLookup.Find(hashType);
            var bytes = ThreadSafeRandom.GetBytes(hashInfo.ByteLength);
            if (hashInfo is TaggedHashInfo taggedHashInfo)
            {
                bytes[bytes.Length - 1] = taggedHashInfo.AlgorithmId;
            }

            return new ContentHash(hashType, bytes);
        }

        /// <summary>
        ///     Attempt to create from a known type string.
        /// </summary>
        public static bool TryParse(string serialized, out ContentHash contentHash)
        {
            return TryParse(serialized, out contentHash, isShortHash: false);
        }

        /// <summary>
        ///     Attempt to create from a known type and string (without type).
        /// </summary>
        public static bool TryParse(HashType hashType, string serialized, out ContentHash contentHash)
        {
            return TryParse(hashType, serialized, out contentHash, isShortHash: false);
        }

        /// <summary>
        ///     Attempt to create from a known type string.
        /// </summary>
        internal static bool TryParse(string serialized, out ContentHash contentHash, bool isShortHash)
        {
            var x = serialized.IndexOf(SerializedDelimiter);

            if (x < 1 || x >= serialized.Length)
            {
                contentHash = default(ContentHash);
                return false;
            }

            var segment0 = serialized.Substring(0, x);
            var segment1 = serialized.Substring(x + 1);

            string hash;

            if (segment0.Deserialize(out var hashType))
            {
                hash = segment1;
            }
            else if (segment1.Deserialize(out hashType))
            {
                hash = segment0;
            }
            else
            {
                contentHash = default(ContentHash);
                return false;
            }

            return TryParse(hashType, hash, out contentHash, isShortHash);
        }

        /// <summary>
        ///     Attempt to create from a known type and string (without type).
        /// </summary>
        internal static bool TryParse(HashType hashType, string serialized, out ContentHash contentHash, bool isShortHash)
        {
            if (serialized.Length != (isShortHash ? ShortHash.HashStringLength : HashInfoLookup.Find(hashType).StringLength))
            {
                contentHash = default(ContentHash);
                return false;
            }

            if (!ReadOnlyFixedBytes.TryParse(serialized, out var bytes, out _))
            {
                contentHash = default(ContentHash);
                return false;
            }

            contentHash = new ContentHash(hashType, bytes);

            if (!isShortHash)
            {
                if (HashInfoLookup.Find(hashType) is TaggedHashInfo && !AlgorithmIdHelpers.IsHashTagValid(contentHash))
                {
                    contentHash = default(ContentHash);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Returns a 64-bit signed integer formed by the least significant bytes of the hash.
        /// </summary>
        public long LeastSignificantLong()
        {
            return _bytes.LeastSignificantLong(ByteLength);
        }
    }
}
