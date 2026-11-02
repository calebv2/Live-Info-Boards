using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> SceneObjects = new List<Object>();
        public static int SceneScans;
        public bool Destroyed;
        public static T[] FindObjectsOfType<T>() where T : Object { return FindObjectsOfType<T>(false); }
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object
        {
            SceneScans++;
            var found = new List<T>();
            foreach (Object value in SceneObjects)
            {
                T typed = value as T;
                var board = typed as Alta.Map.InfoBoard;
                if (typed != null && (includeInactive || board == null || board.gameObject.activeInHierarchy)) found.Add(typed);
            }
            return found.ToArray();
        }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public static class Application { public static bool isBatchMode = true; }
    public static class Time { public static float unscaledTime; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject { public bool activeInHierarchy = true; }
}
namespace Alta.Map
{
    public sealed class TestEntity
    {
        public uint Identifier;
        public InfoBoard Board;
        public event Action<TestEntity, bool> DestroyedByScene;
        public void Destroy(bool isUnload)
        {
            if (DestroyedByScene != null) DestroyedByScene(this, isUnload);
            if (!ReferenceEquals(Board, null)) Board.DestroyUnityObject();
        }
    }
    public sealed class InfoBoardSaveData { public string identifier; }
    public sealed class InfoBoard : UnityEngine.Object
    {
        private InfoBoardSaveData data;
        public TestEntity Entity;
        public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
        public readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public static int Refreshes;
        public bool enabled = true;
        public InfoBoard(uint id, string channel, UnityEngine.Vector3 position)
        {
            Entity = new TestEntity { Identifier = id, Board = this };
            data = new InfoBoardSaveData { identifier = channel };
            transform.position = position;
        }
        public string Channel { get { return data.identifier; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Initialize()
        {
#if !REAL_HARMONY
            HarmonyLib.Harmony.InvokePostfix(typeof(InfoBoard), "Initialize", this);
#endif
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void OnDestroy()
        {
#if !REAL_HARMONY
            HarmonyLib.Harmony.InvokePrefix(typeof(InfoBoard), "OnDestroy", this);
#endif
            Destroyed = true;
            UnityEngine.Object.SceneObjects.Remove(this);
        }
        public void DestroyUnityObject() { OnDestroy(); }
        public static void ForceRefresh(string channel) { Refreshes++; }
    }
}
namespace Alta.Utilities
{
    public sealed class TextFileFormat { public string Text; }
    public interface IAltaFile
    {
        object Content { get; set; }
        bool IsWriting { get; }
        bool IsReading { get; }
        string FullName { get; }
        void WriteAsync();
        void QueueUnload();
        void Delete(bool immediate);
    }
    public sealed class TestFile : IAltaFile
    {
        public object Content { get; set; }
        public bool IsWriting { get; set; }
        public bool IsReading { get; set; }
        public string FullName { get; set; }
        public int Writes;
        public int Unloads;
        public void WriteAsync()
        {
            Writes++;
            File.WriteAllText(FullName, ((TextFileFormat)Content).Text);
        }
        public void QueueUnload() { Unloads++; }
        public void Delete(bool immediate) { File.Delete(FullName); }
    }
    public sealed class TestSaveFolder
    {
        public string Root;
        public readonly Dictionary<string, TestFile> Files = new Dictionary<string, TestFile>();
        public TestSaveFolder GetSubfolder(string name) { return this; }
        public IAltaFile GetFile(string channel)
        {
            TestFile file;
            if (!Files.TryGetValue(channel, out file))
            {
                file = new TestFile { FullName = Path.Combine(Root, channel + ".txt") };
                Files[channel] = file;
            }
            return file;
        }
    }
}
namespace Alta.Networking.Servers
{
    public sealed class TestSaveUtility { public Alta.Utilities.TestSaveFolder SaveFolder; }
    public sealed class ServerHandler
    {
        public static ServerHandler Current;
        public TestSaveUtility SaveUtility;
    }
}
namespace MelonLoader
{
    public sealed class TestLogger
    {
        public readonly List<string> Warnings = new List<string>();
        public void Msg(string message) { }
        public void Warning(string message) { Warnings.Add(message); }
        public void Error(string message) { throw new InvalidOperationException(message); }
    }
    public class MelonMod
    {
        public readonly TestLogger LoggerInstance = new TestLogger();
        public readonly HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("LiveInfoBoards.tests");
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
    }
}
namespace MelonLoader.Utils { public static class MelonEnvironment { public static string UserDataDirectory; } }

#if !REAL_HARMONY
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        internal readonly MethodInfo Method;
        public HarmonyMethod(Type type, string name)
        {
            Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (Method == null) throw new MissingMethodException(type.FullName, name);
        }
    }
    public sealed class Harmony
    {
        public Harmony(string id) { }
        private static readonly Dictionary<string, HarmonyMethod> Prefixes = new Dictionary<string, HarmonyMethod>();
        private static readonly Dictionary<string, HarmonyMethod> Postfixes = new Dictionary<string, HarmonyMethod>();
        public static void Reset() { Prefixes.Clear(); Postfixes.Clear(); }
        public void Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (original == null) throw new ArgumentNullException("original");
            string key = original.DeclaringType.FullName + "." + original.Name;
            if (prefix != null) Prefixes[key] = prefix;
            if (postfix != null) Postfixes[key] = postfix;
        }
        public static void InvokePrefix(Type type, string name, object instance) { Invoke(Prefixes, type, name, instance); }
        public static void InvokePostfix(Type type, string name, object instance) { Invoke(Postfixes, type, name, instance); }
        private static void Invoke(Dictionary<string, HarmonyMethod> hooks, Type type, string name, object instance)
        {
            HarmonyMethod hook;
            if (hooks.TryGetValue(type.FullName + "." + name, out hook)) hook.Method.Invoke(null, new[] { instance });
        }
    }
}
#endif
