namespace GitTfs.Core
{
    public class DelimitedReader
    {
        private readonly TextReader readerField;

        public DelimitedReader(TextReader reader)
        {
            readerField = reader;
            Delimiter = "\0";
        }

        public string Delimiter { get; set; }

        public string Read()
        {
            if (-1 == readerField.Peek()) return null;
            var nextString = "";
            int nextChar;
            while (-1 != (nextChar = readerField.Read()))
            {
                nextString = nextString + (char)nextChar;
                if (nextString.EndsWith(Delimiter, StringComparison.Ordinal))
                {
                    return nextString.Substring(0, nextString.Length - Delimiter.Length);
                }
            }
            return nextString;
        }
    }
}
