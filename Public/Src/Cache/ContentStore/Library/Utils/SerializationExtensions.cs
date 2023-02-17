﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Utils
{
    public static class SerializationExtensions
    {
        public static void Write(this BuildXLWriter writer, UnixTime value)
        {
            writer.WriteCompact(value.Value);
        }

        public static UnixTime ReadUnixTime(this BuildXLReader reader)
        {
            return new UnixTime(reader.ReadInt64Compact());
        }

        public static void Write(ref this SpanWriter writer, UnixTime value)
        {
            writer.WriteCompact(value.Value);
        }

        public static UnixTime ReadUnixTime(ref this SpanReader reader)
        {
            return new UnixTime(reader.ReadInt64Compact());
        }
    }
}
