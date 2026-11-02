using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Alta.Map;
using Alta.Networking.Servers;
using Alta.Utilities;
using HarmonyLib;
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
        private readonly Dictionary<InfoBoard, LoadedBoard> trackedBoards = new Dictionary<InfoBoard, LoadedBoard>();
        private readonly List<LoadedBoard> loadedBoards = new List<LoadedBoard>();
        private readonly InfoBoardConfigurationCache configurationCache = new InfoBoardConfigurationCache();
        private static readonly MethodInfo CloneSaveData = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InfoBoardDataField = typeof(InfoBoard).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Core current;
        private string configurationPath;
        private float nextCheckAt;
        private bool discoverySeeded;
        private string lastInvalidContent;

        private sealed class LoadedBoard
        {
            internal InfoBoard Board;
            internal InfoBoardSaveData Data;
            internal uint Id;
            internal string Key;
            internal string RegisteredKey;
            internal bool DeletionTracked;
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
            InstallBoardLifecycleHooks();
            current = this;
            configurationPath = Path.Combine(MelonEnvironment.UserDataDirectory, ConfigurationFileName);
            EnsureExampleConfiguration();
            LoggerInstance.Msg("Live Info Boards watches " + configurationPath + ".");
        }

        private void InstallBoardLifecycleHooks()
        {
            MethodInfo initialize = typeof(InfoBoard).GetMethod(nameof(InfoBoard.Initialize), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            MethodInfo destroy = typeof(InfoBoard).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (initialize == null || destroy == null)
                throw new InvalidOperationException("InfoBoard lifecycle methods are unavailable; board tracking cannot initialize.");
            HarmonyInstance.Patch(initialize, postfix: new HarmonyMethod(typeof(Core), nameof(BoardInitialized)));
            HarmonyInstance.Patch(destroy, prefix: new HarmonyMethod(typeof(Core), nameof(BoardDestroying)));
        }

        private static void BoardInitialized(InfoBoard __instance)
        {
            if (current != null) current.TrackBoard(__instance);
        }

        private static void BoardDestroying(InfoBoard __instance)
        {
            if (current != null) current.ForgetBoard(__instance);
        }

        private void TrackBoard(InfoBoard board)
        {
            if (board == null || trackedBoards.ContainsKey(board)) return;
            trackedBoards.Add(board, new LoadedBoard { Board = board });
        }

        private void ForgetBoard(InfoBoard board)
        {
            LoadedBoard loaded;
            // OnDestroy runs while the Unity object still exists. Keep the
            // reference check here so a fake-null destroyed key can be removed too.
            if (ReferenceEquals(board, null) || !trackedBoards.TryGetValue(board, out loaded)) return;
            trackedBoards.Remove(board);
            placementTracker.Forget(loaded.Id);
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
            Dictionary<string, InfoBoardDefinition> definitions;
            Dictionary<string, string> legacy;
            string error;
            bool valid;
            try { valid = configurationCache.TryRead(configurationPath, out definitions, out legacy, out error); }
            catch (FileNotFoundException) { EnsureExampleConfiguration(); return; }
            catch (Exception exception) { LoggerInstance.Error("Could not read " + configurationPath + ": " + exception.Message); return; }

            if (!valid)
            {
                if (!string.Equals(lastInvalidContent, configurationCache.Content, StringComparison.Ordinal))
                {
                    lastInvalidContent = configurationCache.Content;
                    LoggerInstance.Warning("Live Info Boards ignored invalid configuration: " + error);
                }
                return;
            }

            RefreshLoadedBoards();
            if (legacy != null)
            {
                definitions = MigrateLegacyConfiguration(legacy);
                if (definitions.Count == 0) return;
                WriteConfiguration(definitions);
                LoggerInstance.Msg("Live Info Boards migrated the legacy channel JSON to entity-keyed board entries.");
            }

            if (UsesNumericKeys(definitions))
            {
                if (loggedBoards.Add("numeric-registry"))
                    LoggerInstance.Warning("Live Info Boards cannot safely match runtime entity IDs after a reboot. Back up InfoBoards.json and replace numeric keys with the board positions from the log. No entries were changed.");
                return;
            }

            lastInvalidContent = null;
            bool added = ReconcileBoards(definitions);
            if (added) WriteConfiguration(definitions);
            ApplyDefinitions(definitions);
        }

        private Dictionary<string, InfoBoardDefinition> MigrateLegacyConfiguration(Dictionary<string, string> legacy)
        {
            var definitions = new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
            foreach (LoadedBoard loaded in loadedBoards)
            {
                string channel = string.IsNullOrEmpty(loaded.Data.identifier) ? GeneratedChannel(loaded.Id) : loaded.Data.identifier;
                if (ContainsChannel(definitions, channel)) channel = UniqueChannel(definitions);
                string text;
                if (!legacy.TryGetValue(channel, out text)) text = Placeholder(loaded.Id);
                if (definitions.ContainsKey(loaded.Key))
                {
                    WarnConflict(loaded.Key);
                    return new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
                }
                definitions.Add(loaded.Key, new InfoBoardDefinition(channel, text));
            }
            return definitions;
        }

        private bool ReconcileBoards(Dictionary<string, InfoBoardDefinition> definitions)
        {
            bool added = false;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LoadedBoard board in loadedBoards)
                counts[board.Key] = counts.ContainsKey(board.Key) ? counts[board.Key] + 1 : 1;
            foreach (LoadedBoard loaded in loadedBoards)
            {
                InfoBoardDefinition definition;
                // A rounded position is not unique when boards overlap. Never let
                // discovery order decide which board owns the saved text.
                if (counts[loaded.Key] > 1)
                {
                    WarnConflict(loaded.Key);
                    continue;
                }
                if (!string.IsNullOrEmpty(loaded.RegisteredKey) && loaded.RegisteredKey != loaded.Key && definitions.TryGetValue(loaded.RegisteredKey, out definition))
                {
                    if (definitions.ContainsKey(loaded.Key))
                    {
                        WarnConflict(loaded.Key);
                        continue;
                    }
                    definitions.Remove(loaded.RegisteredKey);
                    definitions.Add(loaded.Key, definition);
                    added = true;
                    LoggerInstance.Msg("Moved infoboard registration from " + loaded.RegisteredKey + " to " + loaded.Key + ".");
                }
                if (!definitions.TryGetValue(loaded.Key, out definition))
                {
                    string channel = string.IsNullOrEmpty(loaded.Data.identifier) ? GeneratedChannel(loaded.Id) : loaded.Data.identifier;
                    if (ContainsChannel(definitions, channel)) channel = UniqueChannel(definitions);
                    definition = new InfoBoardDefinition(channel, Placeholder(loaded.Id));
                    definitions.Add(loaded.Key, definition);
                    added = true;
                    LoggerInstance.Msg("Registered infoboard entity " + loaded.Id + " on channel '" + channel + "'.");
                }

                loaded.RegisteredKey = loaded.Key;
                if (!string.Equals(loaded.Data.identifier, definition.Channel, StringComparison.Ordinal))
                {
                    loaded.Data.identifier = definition.Channel;
                    // The channel may have been written before this chunk loaded.
                    registryAuthority.Forget(loaded.Key);
                }
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

        private void RefreshLoadedBoards()
        {
            loadedBoards.Clear();
            if (InfoBoardDataField == null) return;
            if (!discoverySeeded)
            {
                // Include dormant scene objects once so activation is observed
                // without a broad OnEnable hook or recurring scene discovery.
                foreach (InfoBoard board in UnityEngine.Object.FindObjectsOfType<InfoBoard>(true)) TrackBoard(board);
                discoverySeeded = true;
            }

            foreach (LoadedBoard loaded in new List<LoadedBoard>(trackedBoards.Values))
            {
                InfoBoard board = loaded.Board;
                if (board == null)
                {
                    ForgetBoard(board);
                    continue;
                }
                if (!board.gameObject.activeInHierarchy || board.Entity == null) continue;
                var data = InfoBoardDataField.GetValue(board) as InfoBoardSaveData;
                if (data == null) continue;
                var position = board.transform.position;
                // Save data can originate from a shared template/cache. Detach before
                // changing its channel so another board cannot inherit our mutation.
                if (!ReferenceEquals(loaded.Data, data))
                {
                    data = (InfoBoardSaveData)CloneSaveData.Invoke(data, null);
                    InfoBoardDataField.SetValue(board, data);
                    loaded.Data = data;
                }
                loaded.Id = board.Entity.Identifier;
                string line = InfoBoardDiagnosticFormatter.Format(loaded.Id, position.x, position.y, position.z, data.identifier ?? string.Empty);
                if (loggedBoards.Add(line)) LoggerInstance.Msg(line);
                string settledKey;
                string movedFromKey;
                if (!placementTracker.TryGetSettledKey(loaded.Id, PositionKey(position), Time.unscaledTime, out settledKey, out movedFromKey)) continue;
                loaded.Key = settledKey;
                loadedBoards.Add(loaded);
                TrackDeletion(loaded);
            }
            loadedBoards.Sort((left, right) => left.Id.CompareTo(right.Id));
        }

        private void TrackDeletion(LoadedBoard loaded)
        {
            if (loaded.DeletionTracked) return;
            loaded.DeletionTracked = true;
            loaded.Board.Entity.DestroyedByScene += (entity, isUnload) =>
            {
                ForgetBoard(loaded.Board);
                if (!isUnload) RemoveDeletedBoard(loaded.RegisteredKey, loaded.Data.identifier);
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
            finally { configurationCache.Invalidate(); }
        }

        private static bool ContainsChannel(Dictionary<string, InfoBoardDefinition> definitions, string channel)
        {
            foreach (InfoBoardDefinition definition in definitions.Values)
                if (string.Equals(definition.Channel, channel, StringComparison.OrdinalIgnoreCase)) return true;
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

        private void WarnConflict(string key)
        {
            if (loggedBoards.Add("conflict:" + key))
                LoggerInstance.Warning("Live Info Boards preserved conflicting registrations at " + key + ". Separate overlapping boards; existing text has not been overwritten.");
        }

        private static string UniqueChannel(Dictionary<string, InfoBoardDefinition> definitions)
        {
            string channel;
            do { channel = "board-" + Guid.NewGuid().ToString("N").Substring(0, 14); }
            while (ContainsChannel(definitions, channel));
            return channel;
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
