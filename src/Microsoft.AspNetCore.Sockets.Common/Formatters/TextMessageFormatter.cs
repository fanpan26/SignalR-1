// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Binary;
using System.Buffers;
using System.Text;
using System.Text.Formatting;

namespace Microsoft.AspNetCore.Sockets.Formatters
{
    internal static class TextMessageFormatter
    {
        private const char FieldDelimiter = ':';
        private const char MessageDelimiter = ';';
        private const char TextTypeFlag = 'T';
        private const char BinaryTypeFlag = 'B';
        private const char CloseTypeFlag = 'C';
        private const char ErrorTypeFlag = 'E';

        internal static bool TryWriteMessage(Message message, IOutput output)
        {
            // Calculate the length, it's the number of characters for text messages, but number of base64 characters for binary
            var length = message.Payload.Length;
            if (message.Type == MessageType.Binary)
            {
                length = Base64.ComputeEncodedLength(length);
            }

            // Get the type indicator
            if (!TryGetTypeIndicator(message.Type, out var typeIndicator))
            {
                return false;
            }

            // Write the length as a string
            output.Append(length, TextEncoder.Utf8);

            // Write the field delimiter ':'
            output.Append(FieldDelimiter, TextEncoder.Utf8);

            // Write the type
            output.Append(typeIndicator, TextEncoder.Utf8);

            // Write the field delimiter ':'
            output.Append(FieldDelimiter, TextEncoder.Utf8);

            // Write the payload
            if(!TryWritePayload(message, output, length))
            {
                return false;
            }

            // Terminator
            output.Append(MessageDelimiter, TextEncoder.Utf8);
            return true;
        }

        private static bool TryWritePayload(Message message, IOutput output, int length)
        {
            // Payload
            if (message.Type == MessageType.Binary)
            {
                // TODO: Base64 writer that works with IOutput would be amazing!
                var arr = new byte[Base64.ComputeEncodedLength(message.Payload.Length)];
                Base64.Encode(message.Payload, arr);
                return output.TryWrite(arr);
            }
            else
            {
                return output.TryWrite(message.Payload);
            }
        }

        internal static bool TryParseMessage(ReadOnlyBytes buffer, out Message message, out int bytesConsumed)
        {
            // Read until the first ':' to find the length
            var consumedForHeader = 0;
            var colonIndex = buffer.IndexOf((byte)FieldDelimiter);
            if (colonIndex < 0)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }
            consumedForHeader += colonIndex;
            var lengthRange = buffer.Slice(0, colonIndex);
            buffer = buffer.Slice(colonIndex);

            // Parse the length
            if (!PrimitiveParser.TryParseInt32(lengthRange.ToSingleSpan(), out var length, out var consumedByLength, encoder: TextEncoder.Utf8) || consumedByLength < lengthRange.Length)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }

            consumedForHeader += consumedByLength;

            // Check if there's enough space in the buffer to even bother continuing
            // There are at least 4 characters we still expect to see: ':', type flag, ':', ';', plus the (encoded) payload length.
            if (buffer.Length < length + 4)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }

            // Load the rest of the header into a single span, it's worth the (possible) copy and it's a fixed size
            var headerBuffer = buffer.Slice(0, 4).ToSingleSpan();
            buffer = buffer.Slice(4);

            // Verify that we have the ':' after the type flag.
            if (headerBuffer[0] != FieldDelimiter)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }

            // We already know that index 0 is the ':', so next is the type flag at index '1'.
            if (!TryParseType(headerBuffer[1], out var messageType))
            {
                message = default(Message);
                bytesConsumed = 0;
            }

            // Verify that we have the ':' after the type flag.
            if (headerBuffer[2] != FieldDelimiter)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }

            consumedForHeader += 4;

            // Load the payload into an array, since it either needs to be stuffed into
            // Message, or decoded, and either option requires we load the whole thing up
            var payload = new byte[length];
            buffer.Slice(0, length).CopyTo(payload);
            buffer = buffer.Slice(length);

            if (messageType == MessageType.Binary && payload.Length > 0)
            {
                // Determine the output size
                // Every 4 Base64 characters represents 3 bytes
                var decodedLength = (int)((payload.Length / 4) * 3);

                // Subtract padding bytes
                if (payload[payload.Length - 1] == '=')
                {
                    decodedLength -= 1;
                }
                if (payload.Length > 1 && payload[payload.Length - 2] == '=')
                {
                    decodedLength -= 1;
                }

                // Allocate a new buffer to decode to
                var decodeBuffer = new byte[decodedLength];
                if (Base64.Decode(payload, decodeBuffer) != decodedLength)
                {
                    message = default(Message);
                    bytesConsumed = 0;
                    return false;
                }
                payload = decodeBuffer;
            }

            // Verify the trailer
            if (buffer.Length < 1 || buffer.First.Span[0] != MessageDelimiter)
            {
                message = default(Message);
                bytesConsumed = 0;
                return false;
            }

            message = new Message(payload, messageType);
            bytesConsumed = consumedForHeader + length;
            return true;
        }

        private static bool TryParseType(byte type, out MessageType messageType)
        {
            switch ((char)type)
            {
                case TextTypeFlag:
                    messageType = MessageType.Text;
                    return true;
                case BinaryTypeFlag:
                    messageType = MessageType.Binary;
                    return true;
                case CloseTypeFlag:
                    messageType = MessageType.Close;
                    return true;
                case ErrorTypeFlag:
                    messageType = MessageType.Error;
                    return true;
                default:
                    messageType = default(MessageType);
                    return false;
            }
        }

        private static bool TryGetTypeIndicator(MessageType type, out char typeIndicator)
        {
            switch (type)
            {
                case MessageType.Text:
                    typeIndicator = TextTypeFlag;
                    return true;
                case MessageType.Binary:
                    typeIndicator = BinaryTypeFlag;
                    return true;
                case MessageType.Close:
                    typeIndicator = CloseTypeFlag;
                    return true;
                case MessageType.Error:
                    typeIndicator = ErrorTypeFlag;
                    return true;
                default:
                    typeIndicator = '\0';
                    return false;
            }
        }
    }
}
