using System;
using System.IO;
using System.Text;
using System.Xml;
using PortCMIS.Binding.AtomPub;
using Xunit;

namespace PortCmisNS.Tests
{
    public class AtomPubParserCloseInputTests
    {
        private const string SampleServiceDocumentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<app:service xmlns:app=\"http://www.w3.org/2007/app\" " +
            "xmlns:atom=\"http://www.w3.org/2005/Atom\" " +
            "xmlns:cmis=\"http://docs.oasis-open.org/ns/cmis/core/200908/\" " +
            "xmlns:cmisra=\"http://docs.oasis-open.org/ns/cmis/restatom/200908/\">" +
            "<app:workspace>" +
            "<atom:title>Main Repository</atom:title>" +
            "</app:workspace>" +
            "</app:service>";

        private const string SampleAtomEntryXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<atom:entry xmlns:atom=\"http://www.w3.org/2005/Atom\">" +
            "<atom:id>urn:uuid:1234</atom:id>" +
            "<atom:title>Test Document</atom:title>" +
            "<atom:link rel=\"self\" href=\"https://example.org/entry/1\" />" +
            "</atom:entry>";

        [Fact]
        public void CreateParser_SetsCloseInputFalse_AndDisposingReaderDoesNotDisposeStream()
        {
            using (var stream = new StrictReadAfterDisposeStream(SampleServiceDocumentXml))
            {
                using (XmlReader reader = XmlUtils.CreateParser(stream))
                {
                    Assert.False(reader.Settings.CloseInput);
                    Assert.True(reader.Read());
                }

                // Disposing XmlReader must NOT dispose the underlying stream.
                Assert.Equal(0, stream.DisposeCount);
                Assert.False(stream.ReadAfterDispose);
            }
        }

        [Fact]
        public void Parse_ValidServiceDocumentWithTrailingBytes_DrainsBeforeDisposingAndNeverReadsAfterDispose()
        {
            // Trailing whitespace/bytes after </app:service> simulate unread chunked-encoding
            // tail data left on ChunkedEncodingReadStream when XmlReader stops at the root end tag.
            string payload = SampleServiceDocumentXml + "\r\n   \r\n";
            var stream = new StrictReadAfterDisposeStream(payload);

            var parser = new AtomPubParser(stream);
            parser.Parse();

            AtomBase result = parser.GetResults();
            Assert.IsType<ServiceDoc>(result);
            Assert.Single(((ServiceDoc)result).Workspaces);

            // IOUtils.ConsumeAndClose must drain the remaining bytes before disposing once,
            // and never call Read() after Dispose() (which would race with SocketsHttpHandler's
            // DrainOnDisposeAsync on SslStream and throw NotSupportedException).
            Assert.True(stream.FullyDrained);
            Assert.False(stream.ReadAfterDispose);
            Assert.Equal(1, stream.DisposeCount);
        }

        [Fact]
        public void Parse_ValidAtomEntryWithTrailingBytes_DrainsBeforeDisposingAndNeverReadsAfterDispose()
        {
            string payload = SampleAtomEntryXml + "\r\n   \r\n";
            var stream = new StrictReadAfterDisposeStream(payload);

            var parser = new AtomPubParser(stream);
            parser.Parse();

            AtomBase result = parser.GetResults();
            AtomEntry entry = Assert.IsType<AtomEntry>(result);
            AtomElement linkElement = Assert.Single(entry.Elements);
            Assert.IsType<AtomLink>(linkElement.Object);

            Assert.True(stream.FullyDrained);
            Assert.False(stream.ReadAfterDispose);
            Assert.Equal(1, stream.DisposeCount);
        }

        [Fact]
        public void Parse_MalformedXmlWithTrailingBytes_DrainsAndDisposesStreamWithoutReadingAfterDispose()
        {
            string malformedPayload = "<app:service xmlns:app=\"http://www.w3.org/2007/app\"><broken>" + new string(' ', 128);
            var stream = new StrictReadAfterDisposeStream(malformedPayload);

            var parser = new AtomPubParser(stream);

            Assert.Throws<XmlException>(() => parser.Parse());

            // Even when parsing fails, the stream must be drained and disposed cleanly once
            // without triggering a Read() after Dispose().
            Assert.True(stream.FullyDrained);
            Assert.False(stream.ReadAfterDispose);
            Assert.Equal(1, stream.DisposeCount);
        }

        /// <summary>
        /// Stream wrapper that mimics .NET SocketsHttpHandler's ChunkedEncodingReadStream over SslStream:
        /// calling Read() after Dispose() throws NotSupportedException ("This method may not be called
        /// when another read operation is pending."), which IOUtils.ConsumeAndClose() does not swallow.
        /// </summary>
        private sealed class StrictReadAfterDisposeStream : Stream
        {
            private readonly MemoryStream inner;
            private bool disposed;

            public StrictReadAfterDisposeStream(string content)
            {
                inner = new MemoryStream(Encoding.UTF8.GetBytes(content));
            }

            public int DisposeCount { get; private set; }
            public bool ReadAfterDispose { get; private set; }
            public bool FullyDrained => inner.Position == inner.Length;

            public override bool CanRead => !disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (disposed)
                {
                    ReadAfterDispose = true;
                    throw new NotSupportedException("This method may not be called when another read operation is pending.");
                }

                return inner.Read(buffer, offset, count);
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                    disposed = true;
                }

                base.Dispose(disposing);
            }
        }
    }
}
