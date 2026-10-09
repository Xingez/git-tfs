namespace GitTfs.Core.RestTfs
{
    using GitTfs.Core;

    public sealed class RestTfsException : GitTfsException
    {
        public RestTfsException(string message, int statusCode, Uri requestUri, string serverErrorType = null)
            : base(message)
        {
            StatusCode = statusCode;
            RequestUri = requestUri;
            ServerErrorType = serverErrorType;
        }

        public int StatusCode { get; }
        public Uri RequestUri { get; }
        public string ServerErrorType { get; }
    }
}
