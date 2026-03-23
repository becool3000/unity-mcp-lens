using System;
using System.IO;

namespace Unity.AI.Generators.Sdk
{
    class NonDisposingStreamWrapper : Stream
    {
        readonly Stream m_InnerStream;

        public NonDisposingStreamWrapper(Stream innerStream) => m_InnerStream = innerStream;

        public override bool CanRead => m_InnerStream.CanRead;
        public override bool CanSeek => m_InnerStream.CanSeek;
        public override bool CanWrite => m_InnerStream.CanWrite;
        public override long Length => m_InnerStream.Length;

        public override long Position
        {
            get => m_InnerStream.Position;
            set => m_InnerStream.Position = value;
        }

        public override void Flush() => m_InnerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => m_InnerStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => m_InnerStream.Seek(offset, origin);

        public override void SetLength(long value) => m_InnerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => m_InnerStream.Write(buffer, offset, count);
    }
}
