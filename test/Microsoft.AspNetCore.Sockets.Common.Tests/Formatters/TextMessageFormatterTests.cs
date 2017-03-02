﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Sockets.Tests;
using Microsoft.AspNetCore.Sockets.Tests.Formatters;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Formatters.Tests
{
    public class TextMessageFormatterTests
    {
        [Fact]
        public void WriteMultipleMessages()
        {
            const string expectedEncoding = "0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;";
            var messages = new[]
            {
                MessageTestUtils.CreateMessage(new byte[0]),
                MessageTestUtils.CreateMessage("Hello,\r\nWorld!",MessageType.Text),
                MessageTestUtils.CreateMessage("A", MessageType.Close),
                MessageTestUtils.CreateMessage("Server Error", MessageType.Error)
            };

            var output = new ArrayOutput(chunkSize: 8); // Use small chunks to test Advance/Enlarge and partial payload writing
            foreach (var message in messages)
            {
                Assert.True(MessageFormatter.TryFormatMessage(message, output, MessageFormat.Text));
            }

            Assert.Equal(expectedEncoding, Encoding.UTF8.GetString(output.ToArray()));
        }

        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        [InlineData("8:B:q83vEjQ=;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34 })]
        [InlineData("8:B:q83vEjRW;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56 })]
        public void WriteBinaryMessage(string encoded, byte[] payload)
        {
            var message = MessageTestUtils.CreateMessage(payload);
            var output = new ArrayOutput(chunkSize: 8); // Use small chunks to test Advance/Enlarge and partial payload writing

            Assert.True(MessageFormatter.TryFormatMessage(message, output, MessageFormat.Text));

            Assert.Equal(encoded, Encoding.UTF8.GetString(output.ToArray()));
        }

        [Theory]
        [InlineData("0:T:;", MessageType.Text, "")]
        [InlineData("3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("0:C:;", MessageType.Close, "")]
        [InlineData("17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("0:E:;", MessageType.Error, "")]
        [InlineData("12:E:Server Error;", MessageType.Error, "Server Error")]
        public void WriteTextMessage(string encoded, MessageType messageType, string payload)
        {
            var message = MessageTestUtils.CreateMessage(payload, messageType);
            var output = new ArrayOutput(chunkSize: 8); // Use small chunks to test Advance/Enlarge and partial payload writing

            Assert.True(MessageFormatter.TryFormatMessage(message, output, MessageFormat.Text));

            Assert.Equal(encoded, Encoding.UTF8.GetString(output.ToArray()));
        }

        [Fact]
        public void WriteInvalidMessages()
        {
            var message = new Message(new byte[0], MessageType.Binary, endOfMessage: false);
            var output = new ArrayOutput(chunkSize: 8); // Use small chunks to test Advance/Enlarge and partial payload writing
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MessageFormatter.TryFormatMessage(message, output, MessageFormat.Text));
            Assert.Equal("Cannot format message where endOfMessage is false using this format", ex.Message);
        }

        [Theory]
        [InlineData("0:T:;", MessageType.Text, "")]
        [InlineData("3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("0:C:;", MessageType.Close, "")]
        [InlineData("17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("0:E:;", MessageType.Error, "")]
        [InlineData("12:E:Server Error;", MessageType.Error, "Server Error")]
        public void ReadTextMessage(string encoded, MessageType messageType, string payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            MessageTestUtils.AssertMessage(message, messageType, payload);
        }

        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        [InlineData("8:B:q83vEjQ=;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34 })]
        [InlineData("8:B:q83vEjRW;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56 })]
        public void ReadBinaryMessage(string encoded, byte[] payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            MessageTestUtils.AssertMessage(message, MessageType.Binary, payload);
        }

        [Fact]
        public void ReadMultipleMessages()
        {
            const string encoded = "0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;";
            var buffer = (Span<byte>)Encoding.UTF8.GetBytes(encoded);

            var messages = new List<Message>();
            var consumedTotal = 0;
            while (MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed))
            {
                messages.Add(message);
                consumedTotal += consumed;
                buffer = buffer.Slice(consumed);
            }

            Assert.Equal(consumedTotal, Encoding.UTF8.GetByteCount(encoded));

            Assert.Equal(4, messages.Count);
            MessageTestUtils.AssertMessage(messages[0], MessageType.Binary, new byte[0]);
            MessageTestUtils.AssertMessage(messages[1], MessageType.Text, "Hello,\r\nWorld!");
            MessageTestUtils.AssertMessage(messages[2], MessageType.Close, "A");
            MessageTestUtils.AssertMessage(messages[3], MessageType.Error, "Server Error");
        }

        [Theory]
        [InlineData("")]
        [InlineData("ABC")]
        [InlineData("1230450945")]
        [InlineData("12ab34:")]
        [InlineData("1:asdf")]
        [InlineData("1::")]
        [InlineData("1:AB:")]
        [InlineData("5:T:A")]
        [InlineData("5:T:ABCDE")]
        [InlineData("5:T:ABCDEF")]
        [InlineData("5:X:ABCDEF")]
        [InlineData("1029348109238412903849023841290834901283409128349018239048102394:X:ABCDEF")]
        public void ReadInvalidMessages(string encoded)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);
            Assert.False(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(0, consumed);
        }
    }
}
