namespace GitTfs.Core.RestTfs
{
    using Microsoft.Extensions.Logging;

    internal sealed class RestChangesetScanner(IRestTfsClient client, ILogger logger)
    {
        public IEnumerable<RestChangesetReference> Scan(string repositoryPath, int lastChangesetId, int batchSize)
        {
            var cursor = lastChangesetId;
            var lastSeen = lastChangesetId;
            var inclusive = false;
            while (true)
            {
                var page = client.GetChangesets(repositoryPath, cursor, batchSize);
                if (page.Count == 0)
                    yield break;

                var highest = page.Max(reference => reference.ChangesetId);
                if (highest < cursor)
                    throw new GitTfsException("The TFVC changeset scan returned a page before its requested cursor.");
                // Detect inclusive fromId once, then advance directly rather than repeating every page.
                inclusive |= page.Any(reference => reference.ChangesetId == cursor);
                logger?.LogDebug("Changeset scan at C{Cursor} returned {Count} reference(s), through C{Highest}.",
                    cursor, page.Count, highest);
                foreach (var reference in page.OrderBy(reference => reference.ChangesetId))
                {
                    if (reference.ChangesetId <= lastSeen)
                        continue;
                    lastSeen = reference.ChangesetId;
                    yield return reference;
                }

                if (highest == int.MaxValue)
                    yield break;
                cursor = inclusive ? highest + 1 : highest;
            }
        }
    }
}
