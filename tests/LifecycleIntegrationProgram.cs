using System;
using System.Collections.Generic;
using System.IO;
using Alta.Map;
using Alta.Networking.Servers;
using Alta.Utilities;
using LiveInfoBoards;
using MelonLoader.Utils;
using UnityEngine;

// Runs the actual game-provided Harmony implementation over the Unity boundary
// fixture, so hook dispatch is exercised in addition to polling logic.
internal static class LifecycleIntegrationProgram
{
    static string configuration;
    static void Tick(Core core, float time) { Time.unscaledTime = time; core.OnUpdate(); }
    static Dictionary<string, InfoBoardDefinition> Definitions()
    {
        Dictionary<string, InfoBoardDefinition> result;
        string error;
        if (!InfoBoardRegistryDocument.TryParse(File.ReadAllText(configuration), out result, out error)) throw new Exception(error);
        return result;
    }
    static void Assert(bool value, string name) { if (!value) throw new Exception(name); }
    static InfoBoard Board(uint id, float x)
    {
        var board = new InfoBoard(id, "board-" + id, new Vector3(x, 2f, 3f));
        UnityEngine.Object.SceneObjects.Add(board);
        board.Initialize();
        return board;
    }
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "liveinfoboards-harmony-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            configuration = Path.Combine(root, "InfoBoards.json");
            File.WriteAllText(configuration, "{\"boards\":{}}");
            MelonEnvironment.UserDataDirectory = root;
            ServerHandler.Current = new ServerHandler { SaveUtility = new TestSaveUtility { SaveFolder = new TestSaveFolder { Root = root } } };
            InfoBoard dormant = Board(1, 1f);
            dormant.gameObject.activeInHierarchy = false;
            var core = new Core(); core.OnInitializeMelon(); Tick(core, 0f);
            Assert(UnityEngine.Object.SceneScans == 1 && Definitions().Count == 0, "one inactive-aware startup seed");
            dormant.gameObject.activeInHierarchy = true; dormant.enabled = false;
            Tick(core, .5f); Tick(core, 3.5f);
            Assert(Definitions().ContainsKey("1.0,2.0,3.0"), "startup inactive board reactivation");
            InfoBoard late = Board(2, 4f); late.Initialize();
            Tick(core, 4f); Tick(core, 7f);
            Assert(Definitions().ContainsKey("4.0,2.0,3.0"), "actual Initialize Harmony postfix tracks late board");
            late.Entity.Destroy(true);
            Assert(Definitions().ContainsKey("4.0,2.0,3.0"), "unload preserves configuration");
            InfoBoard transient = Board(3, 7f); Tick(core, 7.5f);
            transient.DestroyUnityObject();
            Board(3, 7f); Tick(core, 10f);
            Assert(!Definitions().ContainsKey("7.0,2.0,3.0"), "actual OnDestroy Harmony prefix resets early placement");
            Tick(core, 13f);
            Assert(Definitions().ContainsKey("7.0,2.0,3.0"), "replacement settles");
            dormant.Entity.Destroy(false);
            Assert(!Definitions().ContainsKey("1.0,2.0,3.0"), "deletion removes assigned board");
            Tick(core, 60f);
            Assert(UnityEngine.Object.SceneScans == 1, "no recurring global discovery");
            Console.WriteLine("PASS: real Harmony lifecycle integration (startup seed, late initialization, duplicate initialization, reactivation, unload, early destruction/ID reuse, deletion, no recurring scans).");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
