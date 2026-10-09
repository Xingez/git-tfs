namespace GitTfs.LegacyHistory
{
    using System;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.VisualStudio.Services.Client;
    using Microsoft.VisualStudio.Services.Common;

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

            return new VssClientCredentials();
        }
    }
}
