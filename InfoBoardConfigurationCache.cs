using System;
using System.Collections.Generic;
using System.IO;

public sealed class InfoBoardConfigurationCache
{
    private bool hasRevision;
    private DateTime modifiedAt;
    private long length;
    private Dictionary<string, InfoBoardDefinition> definitions;
    private Dictionary<string, string> legacy;
    private bool valid;
    private string error;

    public string Content { get; private set; }

    public bool TryRead(string path, out Dictionary<string, InfoBoardDefinition> boards, out Dictionary<string, string> legacyBoards, out string parseError)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Configuration file is missing.", path);
            DateTime observedModifiedAt = file.LastWriteTimeUtc;
            long observedLength = file.Length;
            if (!hasRevision || modifiedAt != observedModifiedAt || length != observedLength)
            {
                Content = File.ReadAllText(path);
                legacy = null;
                valid = InfoBoardRegistryDocument.TryParse(Content, out definitions, out error);
                if (!valid) valid = InfoBoardDocument.TryParse(Content, out legacy, out error);
                // Keep the metadata from before the read. An edit during the read is
                // then seen on the next poll rather than cached as the newer revision.
                modifiedAt = observedModifiedAt;
                length = observedLength;
                hasRevision = true;
            }
            boards = definitions;
            legacyBoards = legacy;
            parseError = error;
            return valid;
        }
        catch
        {
            Invalidate();
            throw;
        }
    }

    public void Invalidate() { hasRevision = false; }
}
