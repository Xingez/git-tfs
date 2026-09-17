
namespace GitTfs.VsCommon
{
    using global::System;
    using global::System.Diagnostics;
    using global::System.Globalization;
    using global::System.Threading;
    using global::Microsoft.TeamFoundation.Client.Channels;

    internal sealed class TfsRequestLoggingChannelFactory : ITfsRequestChannelFactory
    {
        private readonly ITfsRequestChannelFactory innerFactoryField;

        public TfsRequestLoggingChannelFactory(ITfsRequestChannelFactory innerFactory)
        {
            innerFactoryField = innerFactory;
        }

        public ITfsRequestChannel CreateChannel(ITfsRequestChannel channel)
        {
            var loggingChannel = new TfsRequestLoggingChannel(channel);

            return innerFactoryField == null
                ? loggingChannel
                : innerFactoryField.CreateChannel(loggingChannel);
        }
    }

    internal sealed class TfsRequestLoggingChannel : ITfsRequestChannel
    {
        private readonly ITfsRequestChannel innerChannelField;

        public TfsRequestLoggingChannel(ITfsRequestChannel innerChannel)
        {
            innerChannelField = innerChannel;
        }

        public Uri Uri => innerChannelField.Uri;

        public Microsoft.VisualStudio.Services.Common.VssCredentials Credentials => innerChannelField.Credentials;

        public CultureInfo Culture => innerChannelField.Culture;

        public Guid SessionId => innerChannelField.SessionId;

        public TfsRequestSettings Settings => innerChannelField.Settings;

        public TfsHttpClientState State => innerChannelField.State;

        public void Abort() => innerChannelField.Abort();

        public IAsyncResult BeginRequest(TfsMessage message, AsyncCallback callback, object state)
        {
            LogRequestUrl(message);
            return innerChannelField.BeginRequest(message, callback, state);
        }

        public IAsyncResult BeginRequest(TfsMessage message, TimeSpan timeout, AsyncCallback callback, object state)
        {
            LogRequestUrl(message);
            return innerChannelField.BeginRequest(message, timeout, callback, state);
        }

        public TfsMessage EndRequest(IAsyncResult result) => innerChannelField.EndRequest(result);

        public TfsMessage Request(TfsMessage message)
        {
            LogRequestUrl(message);
            return innerChannelField.Request(message);
        }

        public TfsMessage Request(TfsMessage message, TimeSpan timeout)
        {
            LogRequestUrl(message);
            return innerChannelField.Request(message, timeout);
        }

        private void LogRequestUrl(TfsMessage message)
        {
            Trace.WriteLine("TFS request URL: " + (message?.To ?? Uri));
        }
    }
}
