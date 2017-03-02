// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;

namespace Microsoft.AspNetCore.Sockets.Formatters
{
    public static class MessageFormatter
    {
        public static bool TryFormatMessage(Message message, IOutput output, MessageFormat format)
        {
            if (!message.EndOfMessage)
            {
                // This is a truely exceptional condition since we EXPECT callers to have already
                // buffered incomplete messages and synthesized the correct, complete message before
                // giving it to us. Hence we throw, instead of returning false.
                throw new InvalidOperationException("Cannot format message where endOfMessage is false using this format");
            }

            return format == MessageFormat.Text ?
                TextMessageFormatter.TryWriteMessage(message, output) :
                BinaryMessageFormatter.TryWriteMessage(message, output);
        }

        public static bool TryParseMessage(ReadOnlySpan<byte> buffer, MessageFormat format, out Message message, out int bytesConsumed)
        {
            return format == MessageFormat.Text ?
                TextMessageFormatter.TryParseMessage(buffer, out message, out bytesConsumed) :
                BinaryMessageFormatter.TryParseMessage(buffer, out message, out bytesConsumed);
        }
    }
}
