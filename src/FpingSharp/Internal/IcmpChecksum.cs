namespace FpingSharp.Internal
{
    internal static class IcmpChecksum
    {
        // Compute Internet checksum over buffer[offset..offset+length-1]
        // Returns checksum in network byte order (big-endian)
        public static ushort Compute(byte[] buffer, int offset, int length)
        {
            uint sum = 0;
            int i = offset;
            int end = offset + length;

            // Sum 16-bit words
            while (i + 1 < end)
            {
                sum += (uint)((buffer[i] << 8) | buffer[i + 1]);
                i += 2;
            }

            // If odd byte, pad with zero
            if (i < end)
            {
                sum += (uint)(buffer[i] << 8);
            }

            // Fold 32-bit sum to 16 bits
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)(~sum & 0xFFFF);
        }

        // Convenience overload for whole buffer
        public static ushort Compute(byte[] buffer)
        {
            return Compute(buffer, 0, buffer.Length);
        }

        // Verify checksum - returns true if valid (result should be 0)
        public static bool Verify(byte[] buffer, int offset, int length)
        {
            return Compute(buffer, offset, length) == 0;
        }
    }
}
