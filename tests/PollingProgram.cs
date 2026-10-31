using System;
using System.Collections.Generic;
using System.IO;
using Alta.Map;
using Alta.Networking.Servers;
using Alta.Utilities;
using LiveInfoBoards;
using MelonLoader.Utils;
using UnityEngine;

internal static class PollingProgram
{
    private static string root;
    private static string configuration;
    private static TestSaveFolder saveFolder;

    private static int Main()
    {
        int failures = 0;
        Action[] tests = { UnchangedConfigurationUsesCachedDefinitions, DiscoveryIsBoundedAndMovementStaysFast,
            RotationAndPendingVerificationStayFast, MissingAndInvalidConfigurationRecover,
            InvalidRevisionIsCached, UnloadPreservesTextAndForgetsPlacement,
            DeletionUsesCurrentSettledPosition, LegacyMigrationStillDiscoversBoards, RuntimeIdsAreNotTrustedAfterReboot, StartupPositionsDoNotStealText, OccupiedMovePreservesBothEntries, RoundedPositionCollisionIsNotAssigned, ReusedIdsGetUniqueChannels, SharedSaveDataIsDetached, RebootWithReversedIdsPreservesText };
        foreach (Action test in tests)
        {
            root = Path.Combine(Path.GetTempPath(), "liveinfoboards-polling-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            configuration = Path.Combine(root, "InfoBoards.json");
            MelonEnvironment.UserDataDirectory = root;
            Time.unscaledTime = 0f;
            UnityEngine.Object.SceneObjects.Clear();
            UnityEngine.Object.SceneScans = 0;
            InfoBoard.Refreshes = 0;
            saveFolder = new TestSaveFolder { Root = root };
            ServerHandler.Current = new ServerHandler { SaveUtility = new TestSaveUtility { SaveFolder = saveFolder } };
            try { test(); Console.WriteLine("PASS: " + test.Method.Name); }
            catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL: " + test.Method.Name + ": " + exception.Message); }
            finally { Directory.Delete(root, true); }
        }
        return failures == 0 ? 0 : 1;
    }

    private static Core Start(string json)
    {
        File.WriteAllText(configuration, json);
        // Mono truncates sub-microsecond timestamps when setting them; use an
        // integral timestamp so unchanged-metadata fixtures are exact.
        File.SetLastWriteTimeUtc(configuration, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var core = new Core();
        core.OnInitializeMelon();
        if (UnityEngine.Object.SceneObjects.Count > 0)
        {
            typeof(Core).GetField("nextCheckAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(core, -3f);
            typeof(Core).GetField("nextDiscoveryAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(core, -3f);
            Tick(core, -3f);
        }
        Tick(core, 0f);
        typeof(Core).GetField("nextDiscoveryAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(core, 5f);
        return core;
    }

    private static void Tick(Core core, float now) { Time.unscaledTime = now; core.OnUpdate(); }
    private static string Registry(string key, string channel, string text)
    {
        return "{\"boards\":{\"" + key + "\":{\"channel\":\"" + channel + "\",\"text\":\"" + text + "\"}}}";
    }
    private static InfoBoard AddBoard(uint id, string channel, float x)
    {
        var board = new InfoBoard(id, channel, new Vector3(x, 2f, 3f));
        UnityEngine.Object.SceneObjects.Add(board);
        return board;
    }
    private static Dictionary<string, InfoBoardDefinition> ReadDefinitions()
    {
        Dictionary<string, InfoBoardDefinition> definitions;
        string error;
        Assert(InfoBoardRegistryDocument.TryParse(File.ReadAllText(configuration), out definitions, out error), error);
        return definitions;
    }

    private static void UnchangedConfigurationUsesCachedDefinitions()
    {
        Core core = Start(Registry("1.0,2.0,3.0", "welcome", "One"));
        DateTime originalStamp = File.GetLastWriteTimeUtc(configuration);
        // Both metadata values are unchanged: the second poll must reuse its parsed revision.
        File.WriteAllText(configuration, Registry("1.0,2.0,3.0", "welcome", "Two"));
        File.SetLastWriteTimeUtc(configuration, originalStamp);
        Tick(core, 0.5f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "One", "An unchanged file stamp and length must reuse the cached parsed definitions.");
        File.SetLastWriteTimeUtc(configuration, originalStamp.AddSeconds(10));
        Tick(core, 1f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "Two", "A timestamp edit must be applied on the fast poll.");
        File.WriteAllText(configuration, Registry("1.0,2.0,3.0", "welcome", "Longer"));
        File.SetLastWriteTimeUtc(configuration, originalStamp.AddSeconds(10));
        Tick(core, 1.5f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "Longer", "A length edit must reload even if the timestamp is unchanged.");
    }

    private static void DiscoveryIsBoundedAndMovementStaysFast()
    {
        InfoBoard board = AddBoard(10, "welcome", 1f);
        Core core = Start("{\"boards\":{}}");
        Assert(UnityEngine.Object.SceneScans == 1, "The first poll must scan the scene only once.");
        AddBoard(20, "other", 9f);
        board.transform.position = new Vector3(4f, 2f, 3f);
        for (int i = 1; i <= 7; i++) Tick(core, i * 0.5f);
        Dictionary<string, InfoBoardDefinition> definitions = ReadDefinitions();
        Assert(definitions.ContainsKey("4.0,2.0,3.0") && !definitions.ContainsKey("1.0,2.0,3.0"), "Cached boards must settle movement after three seconds on fast polls.");
        Assert(!definitions.ContainsKey("9.0,2.0,3.0") && UnityEngine.Object.SceneScans == 1, "New scene discovery must wait for its own cadence.");
        Tick(core, 5f);
        Tick(core, 8f);
        Assert(ReadDefinitions().ContainsKey("9.0,2.0,3.0") && UnityEngine.Object.SceneScans == 2, "New boards must be discovered at the five-second poll.");
    }

    private static void RotationAndPendingVerificationStayFast()
    {
        Core core = Start("{\"boards\":{\"1.0,2.0,3.0\":{\"channel\":\"welcome\",\"text\":\"Base\",\"rotation\":{\"enabled\":true,\"interval\":1,\"unit\":\"seconds\",\"messages\":[\"One\",\"Two\"]}}}}");
        TestFile file = saveFolder.Files["welcome"];
        File.WriteAllText(file.FullName, "Overwritten");
        Tick(core, 0.5f);
        Assert(file.Writes == 2 && File.ReadAllText(file.FullName) == "One", "A failed native save must retry at half a second.");
        Tick(core, 1f);
        Assert(InfoBoard.Refreshes == 1 && File.ReadAllText(file.FullName) == "Two", "Pending confirmation and a one-second rotation must run between scene discoveries.");
        Tick(core, 1.5f);
        Assert(InfoBoard.Refreshes == 2 && file.Unloads == 2, "The rotated save must also be confirmed on the next half-second poll.");
        Tick(core, 2f);
        Assert(File.ReadAllText(file.FullName) == "One", "A cached schedule must retain its start time and wrap.");
        File.WriteAllText(configuration, "{\"boards\":{\"1.0,2.0,3.0\":{\"channel\":\"welcome\",\"text\":\"Edited\"}}}");
        Tick(core, 2.5f);
        Assert(File.ReadAllText(file.FullName) == "Edited", "Live schedule removal must take effect on the next fast poll.");
    }

    private static void MissingAndInvalidConfigurationRecover()
    {
        Core core = Start(Registry("1.0,2.0,3.0", "welcome", "One"));
        DateTime stamp = File.GetLastWriteTimeUtc(configuration);
        File.WriteAllText(configuration, "broken");
        Tick(core, 0.5f);
        Tick(core, 1f);
        Assert(core.LoggerInstance.Warnings.Count == 1, "An unchanged invalid revision must not repeatedly warn.");
        File.WriteAllText(configuration, Registry("1.0,2.0,3.0", "welcome", "Two"));
        File.SetLastWriteTimeUtc(configuration, stamp);
        Tick(core, 1.5f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "Two", "A valid revision after invalid content must reload.");
        File.Delete(configuration);
        Tick(core, 2f);
        Assert(File.Exists(configuration), "A missing configuration must recreate the example.");
        File.WriteAllText(configuration, Registry("1.0,2.0,3.0", "welcome", "New"));
        File.SetLastWriteTimeUtc(configuration, stamp);
        Tick(core, 2.5f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "New", "A recreated file with the old metadata must not reuse removed content.");
    }

    private static void InvalidRevisionIsCached()
    {
        Core core = Start(Registry("1.0,2.0,3.0", "welcome", "One"));
        DateTime invalidStamp = File.GetLastWriteTimeUtc(configuration).AddSeconds(10);
        string valid = Registry("1.0,2.0,3.0", "welcome", "Bad");
        File.WriteAllText(configuration, "!" + valid.Substring(1));
        File.SetLastWriteTimeUtc(configuration, invalidStamp);
        Tick(core, 0.5f);
        File.WriteAllText(configuration, valid);
        File.SetLastWriteTimeUtc(configuration, invalidStamp);
        Tick(core, 1f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "One", "An unchanged invalid file revision must reuse its parse result.");
        Assert(core.LoggerInstance.Warnings.Count == 1, "An unchanged invalid revision must warn only once.");
        File.SetLastWriteTimeUtc(configuration, invalidStamp.AddSeconds(10));
        Tick(core, 1.5f);
        Assert(File.ReadAllText(saveFolder.Files["welcome"].FullName) == "Bad", "Editing an invalid revision's timestamp must retry parsing.");
    }

    private static void UnloadPreservesTextAndForgetsPlacement()
    {
        InfoBoard board = AddBoard(10, "welcome", 1f);
        Core core = Start(Registry("1.0,2.0,3.0", "welcome", "Saved"));
        Tick(core, 0.5f);
        UnityEngine.Object.SceneObjects.Remove(board);
        board.Entity.Destroy(true);
        Assert(ReadDefinitions()["1.0,2.0,3.0"].Text == "Saved" && File.Exists(saveFolder.Files["welcome"].FullName), "A scene unload must preserve registered text and native data.");
        AddBoard(10, "newboard", 4f);
        Tick(core, 5f);
        Tick(core, 8f);
        Assert(ReadDefinitions().ContainsKey("4.0,2.0,3.0"), "A reused entity ID must start a new placement observation after unload.");
    }

    private static void DeletionUsesCurrentSettledPosition()
    {
        InfoBoard board = AddBoard(10, "welcome", 1f);
        Core core = Start(Registry("1.0,2.0,3.0", "welcome", "Persisted"));
        board.transform.position = new Vector3(4f, 2f, 3f);
        for (int i = 1; i <= 7; i++) Tick(core, i * 0.5f);
        Assert(ReadDefinitions()["4.0,2.0,3.0"].Text == "Persisted", "Movement must preserve persisted text.");
        UnityEngine.Object.SceneObjects.Remove(board);
        board.Entity.Destroy(false);
        Assert(ReadDefinitions().Count == 0 && !File.Exists(saveFolder.Files["welcome"].FullName), "Deletion must remove the board's current settled key and native channel.");
        Tick(core, 4f);
        Assert(ReadDefinitions().Count == 0 && !File.Exists(saveFolder.Files["welcome"].FullName), "An internal deletion write must invalidate cached definitions so the next poll cannot recreate text.");
    }

    private static void LegacyMigrationStillDiscoversBoards()
    {
        Core core = Start("{\"welcome\":\"Legacy\"}");
        AddBoard(10, "welcome", 1f);
        Tick(core, 5f);
        Tick(core, 8f);
        Assert(ReadDefinitions()["1.0,2.0,3.0"].Text == "Legacy", "Legacy configuration must remain eligible for migration when boards load later.");
        Assert(UnityEngine.Object.SceneScans == 2, "Migration and reconciliation must share each discovery result.");
    }

    private static void RuntimeIdsAreNotTrustedAfterReboot()
    {
        AddBoard(10, "welcome", 1f);
        Start(Registry("10", "welcome", "Saved"));
        Assert(ReadDefinitions()["10"].Text == "Saved", "Old runtime IDs must be retained for manual recovery.");
        Assert(saveFolder.Files.Count == 0, "Recycled IDs must not assign or write another board's text.");
    }

    private static string TwoBoards()
    {
        var definitions = new Dictionary<string, InfoBoardDefinition>();
        definitions.Add("1.0,2.0,3.0", new InfoBoardDefinition("first", "First text"));
        definitions.Add("4.0,2.0,3.0", new InfoBoardDefinition("second", "Second text"));
        return InfoBoardRegistryDocument.Serialize(definitions);
    }

    private static void StartupPositionsDoNotStealText()
    {
        InfoBoard first = AddBoard(10, "first", 0f);
        InfoBoard second = AddBoard(20, "second", 0f);
        File.WriteAllText(configuration, TwoBoards());
        var core = new Core(); core.OnInitializeMelon(); Tick(core, 0f);
        Assert(ReadDefinitions().Count == 2, "Temporary shared spawn positions must not be registered.");
        first.transform.position = new Vector3(1f, 2f, 3f);
        second.transform.position = new Vector3(4f, 2f, 3f);
        Tick(core, .5f); Tick(core, 3.5f);
        Assert(first.Channel == "first" && second.Channel == "second", "Settled boards must retain their own channels.");
        Assert(ReadDefinitions().Count == 2, "Loading must not introduce placeholder entries.");
    }

    private static void OccupiedMovePreservesBothEntries()
    {
        InfoBoard first = AddBoard(10, "first", 1f);
        Core core = Start(TwoBoards());
        first.transform.position = new Vector3(4f, 2f, 3f);
        Tick(core, .5f); Tick(core, 3.5f);
        Assert(ReadDefinitions()["1.0,2.0,3.0"].Text == "First text" && ReadDefinitions()["4.0,2.0,3.0"].Text == "Second text", "Moving onto an unloaded registered board must not overwrite either entry.");
        Assert(first.Channel == "first", "A rejected move must not adopt the other channel.");
        first.transform.position = new Vector3(7f, 2f, 3f);
        Tick(core, 4f); Tick(core, 7f);
        Assert(ReadDefinitions()["7.0,2.0,3.0"].Text == "First text", "The original registration must follow a later safe move.");
    }

    private static void RoundedPositionCollisionIsNotAssigned()
    {
        InfoBoard first = AddBoard(10, "original-a", 1.01f);
        InfoBoard second = AddBoard(20, "original-b", 1.02f);
        Start(TwoBoards());
        Assert(first.Channel == "original-a" && second.Channel == "original-b", "Boards sharing a rounded key must not adopt a shared channel.");
        first.Entity.Destroy(false);
        Assert(ReadDefinitions().Count == 2, "An unassigned board must not delete another registration.");
    }

    private static void ReusedIdsGetUniqueChannels()
    {
        InfoBoard board = AddBoard(10, "first", 7f);
        var definitions = new Dictionary<string, InfoBoardDefinition>();
        definitions.Add("1.0,2.0,3.0", new InfoBoardDefinition("first", "First text"));
        definitions.Add("4.0,2.0,3.0", new InfoBoardDefinition("board-10", "Second text"));
        Start(InfoBoardRegistryDocument.Serialize(definitions));
        Assert(board.Channel != "first" && board.Channel != "board-10", "A reused ID must not collide with existing native files.");
        Assert(File.ReadAllText(saveFolder.Files["board-10"].FullName) == "Second text", "Existing channel text must remain intact.");
    }

    private static void SharedSaveDataIsDetached()
    {
        InfoBoard first = AddBoard(10, "shared", 1f);
        InfoBoard second = AddBoard(20, "shared", 4f);
        var field = typeof(InfoBoard).GetField("data", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.SetValue(second, field.GetValue(first));
        Core core = Start(TwoBoards());
        Tick(core, .5f);
        Assert(first.Channel == "first" && second.Channel == "second", "Shared loaded save objects must not share channel mutations.");
    }

    private static void RebootWithReversedIdsPreservesText()
    {
        AddBoard(10, "first", 1f); AddBoard(20, "second", 4f);
        Core core = Start(TwoBoards()); Tick(core, .5f);
        string saved = File.ReadAllText(configuration);
        UnityEngine.Object.SceneObjects.Clear();
        InfoBoard second = AddBoard(10, "second", 4f);
        InfoBoard first = AddBoard(20, "first", 1f);
        core = Start(saved); Tick(core, .5f);
        Assert(first.Channel == "first" && second.Channel == "second", "Reboot discovery order and recycled IDs must not swap channels.");
        Assert(File.ReadAllText(saveFolder.Files[first.Channel].FullName) == "First text" && File.ReadAllText(saveFolder.Files[second.Channel].FullName) == "Second text", "Text must survive reboot independently.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
