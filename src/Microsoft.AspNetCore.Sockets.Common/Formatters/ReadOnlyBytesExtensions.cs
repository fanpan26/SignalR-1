using System;
using System.Buffers;

namespace Microsoft.AspNetCore.Sockets.Formatters
{
    internal static class ReadOnlyBytesExtensions
    {
        public static ReadOnlySpan<byte> ToSingleSpan(this ReadOnlyBytes self)
        {
            if(self.Rest == null)
            {
                return self.First.Span;
            }
            else
            {
                return self.ToSpan();
            }
        }
    }
}
