using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Alta.Map;
using Alta.Networking.Servers;
using Alta.Utilities;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace LiveInfoBoards
{
    public sealed class Core : MelonMod
    {
        private const float CheckIntervalSeconds = 0.5f;
        private const string ConfigurationFileName = "InfoBoards.json";
        private const string ExampleConfiguration = "{\n  \"boards\": {}\n}\n";
        private readonly InfoBoardRegistryAuthority registryAuthority = new InfoBoardRegistryAuthority();
        private readonly InfoBoardPlacementTracker placementTracker = new InfoBoardPlacementTracker(3f);
        private readonly Dictionary<string, PendingWrite> pendingWrites = new Dictionary<string, PendingWrite>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> rotationStartedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> rotationStates = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> loggedBoards = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<InfoBoard> trackedBoards = new HashSet<InfoBoard>();
        private static readonly FieldInfo InfoBoardDataField = typeof(InfoBoard).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);
        private string configurationPath;
        private float nextCheckAt;
        private string lastInvalidContent;

        private sealed class LoadedBoard
        {
            internal InfoBoard Board;
            internal InfoBoardSaveData Data;
            internal uint Id;
            internal string Key;
            internal string MovedFromKey;
        }

        private sealed class PendingWrite
        {
            internal InfoBoardRegistryChange Change;
            internal IAltaFile File;
            internal float VerifyAfter;
        }

        public override void OnInitializeMelon()
        {
            if (!Application.isBatchMode)
            {
                LoggerInstance.Warning("Live Info Boards is server-only and will remain disabled on this client.");
                return;
            }
            configurationPath = Path.Combine(MelonEnvironment.UserDataDirectory, ConfigurationFileName);
            EnsureExampleConfiguration();
            LoggerInstance.Msg("Live Info Boards watches " + configurationPath + ".");
        }

        public override void OnUpdate()
        {
            if (!Application.isBatchMode || string.IsNullOrEmpty(configurationPath) || Time.unscaledTime < nextCheckAt) return;
            nextCheckAt = Time.unscaledTime + CheckIntervalSeconds;
            ApplyConfiguration();
        }

        private void EnsureExampleConfiguration()
        {
            if (File.Exists(configurationPath)) return;
            try { File.WriteAllText(configurationPath, ExampleConfiguration); }
            catch (Exception exception) { LoggerInstance.Error("Could not create " + configurationPath + ": " + exception.Message); }
        }

        private void ApplyConfiguration()
        {
            string json;
            try { json = File.ReadAllText(configurationPath); }
            catch (FileNotFoundException) { EnsureExampleConfiguration(); return; }
            catch (Exception exception) { LoggerInstance.Error("Could not read " + configurationPath + ": " + exception.Message); return; }

            Dictionary<string, InfoBoardDefinition> definitions;
            string error;
            if (!InfoBoardRegistryDocument.TryParse(json, out definitions, out error))
            {
                Dictionary<string, string> legacy;
                if (InfoBoardDocument.TryParse(json, out legacy, out error))
                {
                    definitions = MigrateLegacyConfiguration(legacy);
                    if (definitions.Count == 0) return;
                    WriteConfiguration(definitions);
                    LoggerInstance.Msg("Live Info Boards migrated the legacy channel JSON to entity-keyed board entries.");
                }
                else
                {
                    if (!string.Equals(lastInvalidContent, json, StringComparison.Ordinal))
                    {
                        lastInvalidContent = json;
                        LoggerInstance.Warning("Live Info Boards ignored invalid configuration: " + error);
                    }
                    return;
                }
            }

            if (UsesNumericKeys(definitions))
            {
                Dictionary<string, InfoBoardDefinition> migrated = MigrateDynamicRegistry(definitions);
                if (migrated.Count > 0)
                {
                    definitions = migrated;
                    WriteConfiguration(definitions);
                    LoggerInstance.Msg("Live Info Boards migrated runtime entity IDs to stable position keys.");
                }
            }

            lastInvalidContent = null;
            bool added = ReconcileBoards(definitions);
            if (added) WriteConfiguration(definitions);
            ApplyDefinitions(definitions);
        }

        private Dictionary<string, InfoBoardDefinition> MigrateLegacyConfiguration(Dictionary<string, string> legacy)
        {
            var definitions = new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
            foreach (LoadedBoard loaded in GetLoadedBoards())
            {
                string channel = string.IsNullOrEmpty(loaded.Data.identifier) ? GeneratedChannel(loaded.Id) : loaded.Data.identifier;
                if (ContainsChannel(definitions, channel)) channel = GeneratedChannel(loaded.Id);
                string text;
                if (!legacy.TryGetValue(channel, out text)) text = Placeholder(loaded.Id);
                definitions.Add(loaded.Key, new InfoBoardDefinition(channel, text));
            }
            return definitions;
        }

        private bool ReconcileBoards(Dictionary<string, InfoBoardDefinition> definitions)
        {
            bool added = false;
            foreach (LoadedBoard loaded in GetLoadedBoards())
            {
                InfoBoardDefinition definition;
                if (!string.IsNullOrEmpty(loaded.MovedFromKey) && definitions.TryGetValue(loaded.MovedFromKey, out definition))
                {
                    definitions.Remove(loaded.MovedFromKey);
                    definitions[loaded.Key] = definition;
                    added = true;
                    LoggerInstance.Msg("Moved infoboard registration from " + loaded.MovedFromKey + " to " + loaded.Key + ".");
                }
                if (!definitions.TryGetValue(loaded.Key, out definition))
                {
                    string channel = string.IsNullOrEmpty(loaded.Data.identifier) ? GeneratedChannel(loaded.Id) : loaded.Data.identifier;
                    if (ContainsChannel(definitions, channel)) channel = GeneratedChannel(loaded.Id);
                    definition = new InfoBoardDefinition(channel, Placeholder(loaded.Id));
                    definitions.Add(loaded.Key, definition);
                    added = true;
                    LoggerInstance.Msg("Registered infoboard entity " + loaded.Id + " on channel '" + channel + "'.");
                }

                if (!string.Equals(loaded.Data.identifier, definition.Channel, StringComparison.Ordinal)) loaded.Data.identifier = definition.Channel;
            }
            return added;
        }

        private void ApplyDefinitions(Dictionary<string, InfoBoardDefinition> definitions)
        {
            ConfirmPendingWrites();
            var effectiveDefinitions = new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, InfoBoardDefinition> entry in definitions)
            {
                InfoBoardDefinition definition = entry.Value;
                if (definition.Rotation == null || !definition.Rotation.Enabled)
                {
                    rotationStartedAt.Remove(entry.Key);
                    rotationStates.Remove(entry.Key);
                    effectiveDefinitions[entry.Key] = definition;
                    continue;
                }
                string rotationState = definition.Channel + "\0" + definition.Rotation.Interval + "\0" + definition.Rotation.Unit + "\0" + string.Join("\0", definition.Rotation.Messages.ToArray());
                if (!rotationStates.TryGetValue(entry.Key, out string previousState) || !string.Equals(previousState, rotationState, StringComparison.Ordinal))
                {
                    rotationStates[entry.Key] = rotationState;
                    rotationStartedAt[entry.Key] = Time.unscaledTime;
                }
                string text = definition.Rotation.TextAt(Time.unscaledTime - rotationStartedAt[entry.Key], definition.Text);
                effectiveDefinitions[entry.Key] = new InfoBoardDefinition(definition.Channel, text, definition.Rotation);
            }
            IReadOnlyList<InfoBoardRegistryChange> changes;
            if (!registryAuthority.GetChanges(effectiveDefinitions, out changes)) return;
            foreach (InfoBoardRegistryChange change in changes)
            {
                PendingWrite pending;
                if (pendingWrites.TryGetValue(change.Key, out pending))
                {
                    if (SameChange(pending.Change, change)) continue;
                    pending.File.QueueUnload();
                    pendingWrites.Remove(change.Key);
                }
                try
                {
                    StartWrite(change);
                }
                catch (NullReferenceException)
                {
                    // The game's save service is unavailable during early world startup; retry on the next poll.
                }
                catch (Exception exception)
                {
                    LoggerInstance.Error("Live Info Boards could not start update for position " + change.Key + " channel '" + change.Channel + "': " + exception.Message);
                }
            }
        }

        private void StartWrite(InfoBoardRegistryChange change)
        {
            IAltaFile file = ServerHandler.Current.SaveUtility.SaveFolder.GetSubfolder("InfoBoard").GetFile(change.Channel);
            file.Content = new TextFileFormat { Text = change.Text };
            file.WriteAsync();
            pendingWrites[change.Key] = new PendingWrite { Change = change, File = file, VerifyAfter = Time.unscaledTime + CheckIntervalSeconds };
        }

        private void ConfirmPendingWrites()
        {
            var keys = new List<string>(pendingWrites.Keys);
            foreach (string key in keys)
            {
                PendingWrite pending = pendingWrites[key];
                if (Time.unscaledTime < pending.VerifyAfter || pending.File.IsWriting || pending.File.IsReading) continue;
                try
                {
                    string savedText = File.ReadAllText(pending.File.FullName);
                    if (!string.Equals(savedText, pending.Change.Text, StringComparison.Ordinal))
                    {
                        pending.File.Content = new TextFileFormat { Text = pending.Change.Text };
                        pending.File.WriteAsync();
                        pending.VerifyAfter = Time.unscaledTime + CheckIntervalSeconds;
                        LoggerInstance.Warning("Live Info Boards is retrying position " + pending.Change.Key + " channel '" + pending.Change.Channel + "' because its native file did not retain the requested text.");
                        continue;
                    }
                    InfoBoard.ForceRefresh(pending.Change.Channel);
                    pending.File.QueueUnload();
                    registryAuthority.MarkApplied(pending.Change);
                    pendingWrites.Remove(key);
                    LoggerInstance.Msg("Live Info Boards confirmed position " + pending.Change.Key + " channel '" + pending.Change.Channel + "'.");
                }
                catch (Exception exception)
                {
                    pending.VerifyAfter = Time.unscaledTime + CheckIntervalSeconds;
                    LoggerInstance.Warning("Live Info Boards is waiting to confirm position " + pending.Change.Key + " channel '" + pending.Change.Channel + "': " + exception.Message);
                }
            }
        }

        private Dictionary<string, InfoBoardDefinition> MigrateDynamicRegistry(Dictionary<string, InfoBoardDefinition> definitions)
        {
            var migrated = new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
            foreach (LoadedBoard loaded in GetLoadedBoards())
            {
                InfoBoardDefinition definition;
                if (!definitions.TryGetValue(loaded.Id.ToString(CultureInfo.InvariantCulture), out definition))
                    definition = new InfoBoardDefinition(GeneratedChannel(loaded.Id), Placeholder(loaded.Id));
                migrated[loaded.Key] = definition;
            }
            return migrated;
        }

        private List<LoadedBoard> GetLoadedBoards()
        {
            var result = new List<LoadedBoard>();
            if (InfoBoardDataField == null) return result;
            foreach (InfoBoard board in UnityEngine.Object.FindObjectsOfType<InfoBoard>())
            {
                if (board == null || board.Entity == null) continue;
                var data = InfoBoardDataField.GetValue(board) as InfoBoardSaveData;
                if (data == null) continue;
                var position = board.transform.position;
                var loaded = new LoadedBoard { Board = board, Data = data, Id = board.Entity.Identifier, Key = PositionKey(position) };
                string line = InfoBoardDiagnosticFormatter.Format(board.Entity.Identifier, position.x, position.y, position.z, data.identifier ?? string.Empty);
                if (loggedBoards.Add(line)) LoggerInstance.Msg(line);
                string settledKey;
                string movedFromKey;
                if (!placementTracker.TryGetSettledKey(loaded.Id, loaded.Key, Time.unscaledTime, out settledKey, out movedFromKey)) continue;
                loaded.Key = settledKey;
                loaded.MovedFromKey = movedFromKey;
                result.Add(loaded);
                TrackDeletion(loaded);
            }
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        private void TrackDeletion(LoadedBoard loaded)
        {
            if (!trackedBoards.Add(loaded.Board)) return;
            loaded.Board.Entity.DestroyedByScene += (entity, isUnload) =>
            {
                trackedBoards.Remove(loaded.Board);
                if (!isUnload) RemoveDeletedBoard(loaded.Key, loaded.Data.identifier);
            };
        }

        private void RemoveDeletedBoard(string key, string channel)
        {
            try
            {
                Dictionary<string, InfoBoardDefinition> definitions;
                string error;
                if (!InfoBoardRegistryDocument.TryParse(File.ReadAllText(configurationPath), out definitions, out error))
                {
                    LoggerInstance.Warning("Live Info Boards could not remove deleted board at " + key + ": configuration is invalid (" + error + ").");
                    return;
                }

                bool channelUnused;
                if (!InfoBoardRegistryCleanup.Remove(definitions, key, channel, out channelUnused)) return;
                pendingWrites.Remove(key);
                registryAuthority.Forget(key);
                WriteConfiguration(definitions);
                if (channelUnused) DeleteNativeChannel(channel);
                LoggerInstance.Msg("Removed deleted infoboard at " + key + " channel '" + channel + "'.");
            }
            catch (Exception exception)
            {
                LoggerInstance.Error("Live Info Boards could not clean up deleted board at " + key + ": " + exception.Message);
            }
        }

        private static void DeleteNativeChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            IAltaFile file = ServerHandler.Current.SaveUtility.SaveFolder.GetSubfolder("InfoBoard").GetFile(channel);
            file.Delete(true);
        }

        private void WriteConfiguration(Dictionary<string, InfoBoardDefinition> definitions)
        {
            try { File.WriteAllText(configurationPath, InfoBoardRegistryDocument.Serialize(definitions)); }
            catch (Exception exception) { LoggerInstance.Error("Could not populate " + configurationPath + ": " + exception.Message); }
        }

        private static bool ContainsChannel(Dictionary<string, InfoBoardDefinition> definitions, string channel)
        {
            foreach (InfoBoardDefinition definition in definitions.Values)
                if (string.Equals(definition.Channel, channel, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool UsesNumericKeys(Dictionary<string, InfoBoardDefinition> definitions)
        {
            if (definitions.Count == 0) return false;
            foreach (string key in definitions.Keys)
            {
                uint ignored;
                if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out ignored)) return false;
            }
            return true;
        }

        private static string GeneratedChannel(uint id) { return "board-" + id; }
        private static string Placeholder(uint id) { return "New board " + id + " — edit InfoBoards.json"; }
        private static string PositionKey(Vector3 position)
        {
            return Math.Round(position.x, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "," +
                Math.Round(position.y, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "," +
                Math.Round(position.z, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool SameChange(InfoBoardRegistryChange left, InfoBoardRegistryChange right)
        {
            return string.Equals(left.Channel, right.Channel, StringComparison.Ordinal) && string.Equals(left.Text, right.Text, StringComparison.Ordinal);
        }
    }
}
