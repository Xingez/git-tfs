using System.Text;

using GitTfs.Core;


namespace GitTfs.Test.Core
{
    [TestClass]
    public class DelimitedReaderTests : BaseTest
    {
        [TestMethod]
        public void ShouldParseTwoNullTerminatedStrings()
        {
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes("abc"));
            bytes.Add(0);
            bytes.AddRange(Encoding.ASCII.GetBytes("def"));
            bytes.Add(0);
            var reader = new DelimitedReader(new StreamReader(new MemoryStream(bytes.ToArray())));
            Assert.Equal("abc", reader.Read());
            Assert.Equal("def", reader.Read());
            Assert.Null(reader.Read());
        }

        [TestMethod]
        public void ShouldParseWhenLastStringHasNoTerminator()
        {
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes("abc"));
            bytes.Add(0);
            bytes.AddRange(Encoding.ASCII.GetBytes("def"));
            var reader = new DelimitedReader(new StreamReader(new MemoryStream(bytes.ToArray())));
            Assert.Equal("abc", reader.Read());
            Assert.Equal("def", reader.Read());
            Assert.Null(reader.Read());
        }
    }
}
