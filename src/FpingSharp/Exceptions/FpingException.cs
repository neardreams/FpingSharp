using System;

namespace FpingSharp.Exceptions
{
    public class FpingException : Exception
    {
        public FpingException() { }
        public FpingException(string message) : base(message) { }
        public FpingException(string message, Exception innerException) : base(message, innerException) { }
    }
}
