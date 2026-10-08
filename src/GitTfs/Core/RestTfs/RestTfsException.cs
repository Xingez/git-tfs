namespace GitTfs.Core.RestTfs
{
    using global::GitTfs.Core;

    public sealed class RestTfsException : GitTfsException
    {
        public RestTfsException(string message, int statusCode, Uri requestUri)
            : base(message)
        {
            StatusCode = statusCode;
            RequestUri = requestUri;
        }

        public int StatusCode { get; }
        public Uri RequestUri { get; }
    }
}
