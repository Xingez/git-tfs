namespace GitTfs.LegacyHistory
{
    using System;
    using System.Net;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.VisualStudio.Services.Client;
    using Microsoft.VisualStudio.Services.Common;

    using VssWindowsCredential = Microsoft.VisualStudio.Services.Common.WindowsCredential;

    internal static class LegacyHistoryConnection
    {
        public static TfsTeamProjectCollection Connect(HistoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ServerUrl))
                throw new ArgumentException("The TFS server URL is required.");

            var serverUri = new Uri(request.ServerUrl, UriKind.Absolute);
            var credentials = CreateCredentials(request);
            var collection = new TfsTeamProjectCollection(serverUri, credentials);
            collection.EnsureAuthenticated();
            return collection;
        }

        private static VssCredentials CreateCredentials(HistoryRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.Pat))
                return new VssBasicCredential(string.Empty, request.Pat);

            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                return new VssClientCredentials(
                    new VssWindowsCredential(CreateNetworkCredential(request.Username, request.Password)));
            }

            return new VssClientCredentials();
        }

        private static NetworkCredential CreateNetworkCredential(string username, string password)
        {
            var separator = username.IndexOf('\\');
            if (separator > 0)
                return new NetworkCredential(username.Substring(separator + 1), password, username.Substring(0, separator));
            return new NetworkCredential(username, password);
        }
    }
}
