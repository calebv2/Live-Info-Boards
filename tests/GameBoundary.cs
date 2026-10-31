using System;
using System.Collections.Generic;
using System.IO;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> SceneObjects = new List<Object>();
        public static int SceneScans;
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            SceneScans++;
            var found = new List<T>();
            foreach (Object value in SceneObjects)
            {
                T typed = value as T;
                if (typed != null) found.Add(typed);
            }
            return found.ToArray();
        }
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
        public event Action<TestEntity, bool> DestroyedByScene;
        public void Destroy(bool isUnload)
        {
            if (DestroyedByScene != null) DestroyedByScene(this, isUnload);
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
        public InfoBoard(uint id, string channel, UnityEngine.Vector3 position)
        {
            Entity = new TestEntity { Identifier = id };
            data = new InfoBoardSaveData { identifier = channel };
            transform.position = position;
        }
        public string Channel { get { return data.identifier; } }
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
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
    }
}
namespace MelonLoader.Utils { public static class MelonEnvironment { public static string UserDataDirectory; } }
