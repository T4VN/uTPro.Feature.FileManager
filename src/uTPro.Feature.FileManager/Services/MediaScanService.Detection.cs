using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core;

namespace uTPro.Feature.FileManager.Services;

/// <summary>
/// Reference / "unused" detection helpers for <see cref="MediaScanService"/>.
/// </summary>
internal partial class MediaScanService
{
    /// <summary>
    /// Resolves — in bulk — which of the supplied media keys are referenced by anything (via Umbraco's
    /// tracked references). Returns the set of keys that ARE referenced; the caller treats every key NOT
    /// in this set as "unused".
    ///
    /// This replaces the previous N+1 pattern (one <c>GetPagedRelationsForItemAsync</c> per media item)
    /// with a handful of batched queries. Semantics are identical: a key is considered referenced iff it
    /// participates in at least one relation (the same condition as <c>refs.Total &gt; 0</c> per item),
    /// because both APIs match on the same relation side.
    ///
    /// Keys are queried in chunks (to bound the SQL <c>IN(...)</c> list) and each chunk is paged through.
    /// If a chunk query fails, every key in that chunk is treated as referenced — matching the old
    /// per-item behaviour where an item whose references could not be resolved was never flagged unused.
    /// </summary>
    private async Task<HashSet<Guid>> GetReferencedMediaKeysAsync(IReadOnlyCollection<Guid> candidateKeys)
    {
        var referencedKeys = new HashSet<Guid>();
        if (candidateKeys.Count == 0)
            return referencedKeys;

        foreach (var chunk in candidateKeys.Chunk(RelationQueryBatchSize))
        {
            var keySet = new HashSet<Guid>(chunk);
            try
            {
                long skip = 0;
                while (true)
                {
                    var page = await trackedReferencesService
                        .GetPagedItemsWithRelationsAsync(keySet, skip, RelationQueryPageSize, false);

                    var count = 0;
                    foreach (var relation in page.Items)
                    {
                        referencedKeys.Add(relation.NodeKey);
                        count++;
                    }

                    skip += RelationQueryPageSize;
                    if (count == 0 || skip >= page.Total)
                        break;
                }
            }
            catch (Exception ex)
            {
                // Preserve the old per-item behaviour on failure: an item whose references can't be
                // resolved must NOT be reported as unused, so treat the whole failed chunk as referenced.
                logger.LogWarning(ex, "Media scan: could not resolve references for a batch of {Count} media items", keySet.Count);
                foreach (var key in keySet)
                    referencedKeys.Add(key);
            }
        }

        return referencedKeys;
    }

    /// <summary>
    /// Returns true if any (non-folder) media item currently references the given media-file-system
    /// relative path. Used as a fresh safety check before deleting an orphaned file, so it deliberately
    /// re-reads the live media (never a cached scan) and short-circuits on the first match.
    /// </summary>
    private bool IsFileReferenced(IFileSystem fs, string rel)
    {
        foreach (var media in GetAllMedia())
        {
            if (string.Equals(media.ContentType.Alias, Constants.Conventions.MediaTypes.Folder, StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = GetMediaFilePath(media);
            if (filePath is null) continue;

            if (string.Equals(NormalizeRelative(fs, filePath), rel, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
