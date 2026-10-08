namespace GitTfs.LegacyHistory
{
    using System;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.TeamFoundation.VersionControl.Client;
    using Newtonsoft.Json;

    internal sealed class LegacyHistoryHost
    {
        private TfsTeamProjectCollection collectionField;
        private VersionControlServer versionControlField;
        private string connectedServerField;

        public int Run()
        {
            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var request = JsonConvert.DeserializeObject<HistoryRequest>(line);
                        if (request == null)
                            throw new InvalidOperationException("The legacy history request was empty.");

                        EnsureConnected(request);
                        LegacyHistoryOperations.Run(versionControlField, request);
                    }
                    catch (Exception exception)
                    {
                        ResetConnection();
                        Console.WriteLine(JsonConvert.SerializeObject(new HistoryError
                        {
                            Message = exception.ToString(),
                        }));
                        Console.WriteLine(JsonConvert.SerializeObject(new HistoryComplete()));
                    }

                    Console.Out.Flush();
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                ResetConnection();
            }
        }

        private void EnsureConnected(HistoryRequest request)
        {
            if (versionControlField != null
                && string.Equals(connectedServerField, request.ServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ResetConnection();
            collectionField = LegacyHistoryConnection.Connect(request);
            versionControlField = collectionField.GetService<VersionControlServer>();
            connectedServerField = request.ServerUrl;
        }

        private void ResetConnection()
        {
            collectionField?.Dispose();
            collectionField = null;
            versionControlField = null;
            connectedServerField = null;
        }
    }
}
