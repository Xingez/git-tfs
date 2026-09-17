
namespace GitTfs.Util
{
    using global::System.Diagnostics;
    public class TemporaryFileStream : FileStream
    {
        public static TemporaryFileStream Acquire()
        {
            var temp = Path.GetTempFileName();
            return new TemporaryFileStream(temp);
        }

        private string filenameField;

        public TemporaryFileStream(string filename)
            : base(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
        {
            // no need to check filename for null as base constructor would have thrown already in this case
            filenameField = filename;
        }

        public string Filename => filenameField;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (filenameField == null) return;

            // doing the same both on disposing and finalizing
            try
            {
                File.Delete(filenameField);
                filenameField = null;
            }
            catch (IOException e)
            {
                Trace.WriteLine("Unable to delete temp file: " + e);
                // ignore!
            }
            catch (UnauthorizedAccessException e)
            {
                Trace.WriteLine("Unable to delete temp file - unauthorized access: " + e);
                // ignore!
            }
            // other exceptions indicate bugs so shouldn't be catched
        }
    }
}
