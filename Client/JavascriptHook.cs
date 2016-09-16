﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using GTA;
using GTA.Math;
using GTA.Native;
using GTANetwork.GUI;
using GTANetworkShared;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using NativeUI;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vector3 = GTANetworkShared.Vector3;

namespace GTANetwork
{
    public class ClientsideScriptWrapper
    {
        public ClientsideScriptWrapper(V8ScriptEngine en, string rs, string filename)
        {
            Engine = en;
            ResourceParent = rs;
            Filename = filename;
        }

        public V8ScriptEngine Engine { get; set; }
        public string ResourceParent { get; set; }
        public string Filename { get; set; }
    }

    public static class AudioThread
    {
        public static void StartAudio(string path, bool looped)
        {
            try
            {
                JavascriptHook.AudioDevice?.Stop();
                JavascriptHook.AudioDevice?.Dispose();
                JavascriptHook.AudioReader?.Dispose();

                if (path.EndsWith(".wav"))
                {
                    JavascriptHook.AudioReader = new WaveFileReader(path);
                }
                else
                {
                    JavascriptHook.AudioReader = new Mp3FileReader(path);
                }

                if (looped)
                {
                    JavascriptHook.AudioReader = new LoopStream(JavascriptHook.AudioReader);
                }

                JavascriptHook.AudioDevice = new WaveOutEvent();
                JavascriptHook.AudioDevice.Init(JavascriptHook.AudioReader);
                JavascriptHook.AudioDevice.Play();
            }
            catch (Exception ex)
            {
                LogManager.LogException(ex, "STARTAUDIO");
            }

            /*
            // BUG: very buggy, for some reason
            var t = new Thread((ThreadStart) delegate
            {
                try
                {
                    JavascriptHook.AudioDevice?.Stop();
                    JavascriptHook.AudioDevice?.Dispose();
                    JavascriptHook.AudioReader?.Dispose();

                    JavascriptHook.AudioReader = new Mp3FileReader(path);
                    JavascriptHook.AudioDevice = new WaveOutEvent();
                    JavascriptHook.AudioDevice.Init(JavascriptHook.AudioReader);
                    JavascriptHook.AudioDevice.Play();
                }
                catch (Exception ex)
                {
                    LogManager.LogException(ex, "STARTAUDIO");
                }
            });
            t.IsBackground = true;
            t.Start();
            */
        }

        public static void DisposeAudio()
        {
            var t = new Thread((ThreadStart)delegate
            {
                try
                {
                    JavascriptHook.AudioDevice?.Stop();
                    JavascriptHook.AudioDevice?.Dispose();
                    JavascriptHook.AudioReader?.Dispose();
                    JavascriptHook.AudioDevice = null;
                    JavascriptHook.AudioReader = null;
                }
                catch (Exception ex)
                {
                    LogManager.LogException(ex, "DISPOSEAUDIO");
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    public class JavascriptHook : Script
    {
        public JavascriptHook()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            ScriptEngines = new List<ClientsideScriptWrapper>();
            ThreadJumper = new List<Action>();
            TextElements = new List<UIResText>();
        }

        public static PointF MousePosition { get; set; }
        public static bool MouseClick { get; set; }

        public static List<UIResText> TextElements { get; set; }

        public static List<ClientsideScriptWrapper> ScriptEngines;

        public static List<Action> ThreadJumper;

        public static WaveOutEvent AudioDevice { get; set; }
        public static WaveStream AudioReader { get; set; }

        public static void InvokeServerEvent(string eventName, string resource, object[] arguments)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines) ScriptEngines.ForEach(en =>
                {
                    if (en.ResourceParent != resource) return;
                    en.Engine.Script.API.invokeServerEvent(eventName, arguments);
                });
            });
        }

        public static void InvokeMessageEvent(string msg)
        {
            if (msg == null) return;
            ThreadJumper.Add(() =>
            {
                if (msg.StartsWith("/"))
                {
                    lock (ScriptEngines)
                    {
                        ScriptEngines.ForEach(en => en.Engine.Script.API.invokeChatCommand(msg));
                    }
                }
                else
                {
                    lock (ScriptEngines)
                    {
                        ScriptEngines.ForEach(en => en.Engine.Script.API.invokeChatMessage(msg));
                    }
                }
            });
        }

        public static void InvokeCustomEvent(Action<dynamic> func)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines)
                {
                    ScriptEngines.ForEach(en => func(en.Engine.Script.API));
                }
            });
        }

        public static void InvokeStreamInEvent(LocalHandle handle, int type)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines)
                {
                    ScriptEngines.ForEach(en => en.Engine.Script.API.invokeEntityStreamIn(handle, type));
                }
            });
        }

        public static void InvokeStreamOutEvent(LocalHandle handle, int type)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines)
                {
                    ScriptEngines.ForEach(en => en.Engine.Script.API.invokeEntityStreamOut(handle, type));
                }
            });
        }

        public static void InvokeDataChangeEvent(LocalHandle handle, string key, object oldValue)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines)
                {
                    ScriptEngines.ForEach(en => en.Engine.Script.API.invokeEntityDataChange(handle, key, oldValue));
                }
            });
        }

        public static void InvokeCustomDataReceived(string resource, string data)
        {
            ThreadJumper.Add(() =>
            {
                lock (ScriptEngines)
                {
                    foreach (var res in ScriptEngines.Where(en => en.ResourceParent == resource))
                        res.Engine.Script.API.invokeCustomDataReceived(data);
                }
            });
        }

        public void OnTick(object sender, EventArgs e)
        {
            var tmpList = new List<Action>(ThreadJumper);
            ThreadJumper.Clear();

            foreach (var a in tmpList)
            { 
                try
                {
                    a.Invoke();
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
            }

            lock (ScriptEngines)
            {
                foreach (var engine in ScriptEngines)
                {
                    try
                    {
                        engine.Engine.Script.API.invokeUpdate();
                    }  
                    catch (Exception ex)
                    {
                        LogException(ex);
                    }
                }
            }

        }

        public void OnKeyDown(object sender, KeyEventArgs e)
        {
            lock (ScriptEngines)
            {
                foreach (var engine in ScriptEngines)
                {
                    try
                    {
                        engine.Engine.Script.API.invokeKeyDown(sender, e);
                    }
                    catch (ScriptEngineException ex)
                    {
                        LogException(ex);
                    }
                }
            }
        }

        public void OnKeyUp(object sender, KeyEventArgs e)
        {
            lock (ScriptEngines)
            {
                foreach (var engine in ScriptEngines)
                {
                    try
                    {
                        engine.Engine.Script.API.invokeKeyUp(sender, e);
                    }
                    catch (ScriptEngineException ex)
                    {
                        LogException(ex);
                    }
                }
            }
        }

        public static void StartScripts(ScriptCollection sc)
        {
            var localSc = new List<ClientsideScript>(sc.ClientsideScripts);

            ThreadJumper.Add(() =>
            {
                List<ClientsideScriptWrapper> scripts = localSc.Select(StartScript).ToList();

                foreach (var compiledResources in scripts)
                {
                    dynamic obj = new ExpandoObject();
                    var asDict = obj as IDictionary<string, object>;

                    foreach (var resourceEngine in
                            scripts.Where(
                                s => s.ResourceParent == compiledResources.ResourceParent &&
                                s != compiledResources))
                    {
                        asDict.Add(resourceEngine.Filename, resourceEngine.Engine.Script);
                    }
                    
                    compiledResources.Engine.AddHostObject("resource", obj);
                    compiledResources.Engine.Script.API.invokeResourceStart();
                }
            });
        }

        public static ClientsideScriptWrapper StartScript(ClientsideScript script)
        {
            ClientsideScriptWrapper csWrapper = null;
            
            var scriptEngine = new V8ScriptEngine();
            scriptEngine.AddHostObject("host", new HostFunctions());
            scriptEngine.AddHostObject("API", new ScriptContext(scriptEngine));
            scriptEngine.AddHostType("Enumerable", typeof(Enumerable));
            scriptEngine.AddHostType("List", typeof(List<>));
            scriptEngine.AddHostType("Dictionary", typeof(Dictionary<,>));
            scriptEngine.AddHostType("String", typeof(string));
            scriptEngine.AddHostType("Int32", typeof(int));
            scriptEngine.AddHostType("Bool", typeof(bool));
            scriptEngine.AddHostType("KeyEventArgs", typeof(KeyEventArgs));
            scriptEngine.AddHostType("Keys", typeof(Keys));
            scriptEngine.AddHostType("Point", typeof(Point));
            scriptEngine.AddHostType("PointF", typeof(PointF));
            scriptEngine.AddHostType("Size", typeof(Size));
            scriptEngine.AddHostType("Vector3", typeof(Vector3));
            scriptEngine.AddHostType("menuControl", typeof(UIMenu.MenuControls));
                

            try
            {
                scriptEngine.Execute(script.Script);
                scriptEngine.Script.API.ParentResourceName = script.ResourceParent;
            }
            catch (ScriptEngineException ex)
            {
                LogException(ex);
            }
            finally
            {
                csWrapper = new ClientsideScriptWrapper(scriptEngine, script.ResourceParent, script.Filename);
                lock (ScriptEngines) ScriptEngines.Add(csWrapper);
            }
    
            return csWrapper;
        }

        public static void StopAllScripts()
        {
            for (int i = ScriptEngines.Count - 1; i >= 0; i--)
            {
                ScriptEngines[i].Engine.Script.API.isDisposing = true;
            }

            foreach (var engine in ScriptEngines)
            {
                engine.Engine.Script.API.invokeResourceStop();
                engine.Engine.Dispose();
            }

            ScriptEngines.Clear();
            AudioDevice?.Stop();
            AudioDevice?.Dispose();
            AudioReader?.Dispose();
            AudioDevice = null;
            AudioReader = null;

            lock (CEFManager.Browsers)
            {
                foreach (var browser in CEFManager.Browsers)
                {
                    browser.Dispose();
                }

                CEFManager.Browsers.Clear();
            }
        }

        public static void StopScript(string resourceName)
        {
            lock (ScriptEngines)
                    for (int i = ScriptEngines.Count - 1; i >= 0; i--)
                {
                    if (ScriptEngines[i].ResourceParent != resourceName) continue;
                    ScriptEngines[i].Engine.Script.API.isDisposing = true;
                }

            ThreadJumper.Add(delegate
            {
                lock (ScriptEngines)
                    for (int i = ScriptEngines.Count - 1; i >= 0; i--)
                    {
                        if (ScriptEngines[i].ResourceParent != resourceName) continue;
                        ScriptEngines[i].Engine.Script.API.invokeResourceStop();
                        ScriptEngines[i].Engine.Dispose();
                        ScriptEngines.RemoveAt(i);
                    }
            });
        }

        private static void LogException(Exception ex)
        {
            Func<string, int, string[]> splitter = (string input, int everyN) =>
            {
                var list = new List<string>();
                for (int i = 0; i < input.Length; i += everyN)
                {
                    list.Add(input.Substring(i, Math.Min(everyN, input.Length - i)));
                }
                return list.ToArray();
            };

            Util.SafeNotify("~r~~h~Clientside Javascript Error~h~~w~");
            
            foreach (var s in splitter(ex.Message, 99))
            {
                Util.SafeNotify(s);
            }

            LogManager.LogException(ex, "CLIENTSIDE SCRIPT ERROR");
        }
    }

    public class ScriptContext
    {
        public ScriptContext(V8ScriptEngine engine)
        {
            Engine = engine;
        }
        internal bool isDisposing;
        internal string ParentResourceName;
        internal V8ScriptEngine Engine;


        internal bool isPathSafe(string path)
        {
            if (ParentResourceName == null) throw new NullReferenceException("Illegal call to isPathSafe inside constructor!");

            var absPath = System.IO.Path.GetFullPath(FileTransferId._DOWNLOADFOLDER_ + ParentResourceName + Path.DirectorySeparatorChar + path);
            var resourcePath = System.IO.Path.GetFullPath(FileTransferId._DOWNLOADFOLDER_ + ParentResourceName);

            return absPath.StartsWith(resourcePath);
        }

        internal void checkPathSafe(string path)
        {
            if (!isPathSafe(path))
            {
                throw new AccessViolationException("Illegal path to file!");
            }
        }

        public enum ReturnType
        {
            Int = 0,
            UInt = 1,
            Long = 2,
            ULong = 3,
            String = 4,
            Vector3 = 5,
            Vector2 = 6,
            Float = 7,
            Bool = 8,
            Handle = 9,
        }
        
        public void showCursor(bool show)
        {
            CefController.ShowCursor = show;
        }

        public bool isCursorShown()
        {
            return CefController.ShowCursor;
        }

        public JavascriptChat registerChatOverride()
        {
            var c = new JavascriptChat();
            Main.Chat = c;
            c.OnComplete += Main.ChatOnComplete;
            return c;
        }

        public GlobalCamera createCamera(Vector3 position, Vector3 rotation)
        {
            return Main.CameraManager.Create(position, rotation);
        }

        public void setActiveCamera(GlobalCamera camera)
        {
            Main.CameraManager.SetActive(camera);
        }

        public void setGameplayCameraActive()
        {
            Main.CameraManager.SetActive(null);
        }

        public GlobalCamera getActiveCamera()
        {
            return Main.CameraManager.GetActive();
        }

        public void setCameraShake(GlobalCamera cam, string shakeType, float amplitute)
        {
            cam.Shake = shakeType;
            cam.ShakeAmp = amplitute;

            if (cam.CamObj != null && cam.Active)
            {
                Function.Call(Hash.SHAKE_CAM, cam.CamObj.Handle, cam.Shake, cam.ShakeAmp);
            }
        }

        public void stopCameraShake(GlobalCamera cam)
        {
            cam.Shake = null;
            cam.ShakeAmp = 0;

            if (cam.CamObj != null && cam.Active)
            {
                Function.Call(Hash.STOP_CAM_SHAKING, cam.CamObj.Handle, true);
            }
        }

        public bool isCameraShaking(GlobalCamera cam)
        {
            return !string.IsNullOrEmpty(cam.Shake);
        }

        public void setCameraPosition(GlobalCamera cam, Vector3 pos)
        {
            cam.Position = pos;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.Position = pos.ToVector();
            }
        }

        public Vector3 getCameraPosition(GlobalCamera cam)
        {
            return cam.Position;
        }

        public void setCameraRotation(GlobalCamera cam, Vector3 rotation)
        {
            cam.Rotation = rotation;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.Rotation = rotation.ToVector();
            }
        }

        public Vector3 getCameraRotation(GlobalCamera cam)
        {
            return cam.Rotation;
        }

        public void setCameraFov(GlobalCamera cam, float fov)
        {
            cam.Fov = fov;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.FieldOfView = fov;
            }
        }

        public float getCameraFov(GlobalCamera cam)
        {
            return cam.Fov;
        }

        public void pointCameraAtPosition(GlobalCamera cam, Vector3 pos)
        {
            cam.EntityPointing = 0;
            cam.VectorPointing = pos;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.PointAt(pos.ToVector());
            }
        }

        public void pointCameraAtEntity(GlobalCamera cam, LocalHandle ent, Vector3 offset)
        {
            cam.VectorPointing = null;
            cam.EntityPointing = ent.Value;
            cam.PointOffset = offset;
            cam.BonePointing = 0;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.PointAt(new Prop(ent.Value), offset.ToVector());
            }
        }

        public void pointCameraAtEntityBone(GlobalCamera cam, LocalHandle ent, int bone, Vector3 offset)
        {
            cam.VectorPointing = null;
            cam.EntityPointing = ent.Value;
            cam.BonePointing = bone;
            cam.PointOffset = offset;


            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.PointAt(new Ped(ent.Value), bone, offset.ToVector());
            }
        }

        public void stopCameraPointing(GlobalCamera cam)
        {
            cam.VectorPointing = null;
            cam.EntityPointing = 0;
            cam.BonePointing = 0;
            cam.PointOffset = null;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.StopPointing();
            }
        }

        public void attachCameraToEntity(GlobalCamera cam, LocalHandle ent, Vector3 offset)
        {
            cam.EntityAttached = ent.Value;
            cam.BoneAttached = 0;
            cam.AttachOffset = offset;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.AttachTo(new Prop(ent.Value), offset.ToVector());
            }
        }

        public void attachCameraToEntityBone(GlobalCamera cam, LocalHandle ent, int bone, Vector3 offset)
        {
            cam.EntityAttached = ent.Value;
            cam.BoneAttached = bone;
            cam.AttachOffset = offset;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.AttachTo(new Ped(ent.Value), bone, offset.ToVector());
            }
        }

        public void detachCamera(GlobalCamera cam)
        {
            cam.EntityAttached = 0;
            cam.BoneAttached = 0;
            cam.AttachOffset = null;

            if (cam.CamObj != null && cam.Active)
            {
                cam.CamObj.Detach();
            }
        }

        public void interpolateCameras(GlobalCamera from, GlobalCamera to, double duration, bool easepos, bool easerot)
        {
            if (!from.Active) Main.CameraManager.SetActive(from);

            Main.CameraManager.SetActiveWithInterp(to, (int)duration, easepos, easerot);
        }

        public PointF getCursorPosition()
        {
            return CefController._lastMousePoint;
        }

        public PointF getCursorPositionMantainRatio()
        {
            var res = getScreenResolutionMantainRatio();

            var mouseX = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)GTA.Control.CursorX) * res.Width;
            var mouseY = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)GTA.Control.CursorY) * res.Height;

            return new PointF(mouseX, mouseY);
        }

        public PointF worldToScreen(Vector3 pos)
        {
            var p = Main.WorldToScreen(pos.ToVector());
            var res = getScreenResolution();

            return new PointF(p.X * res.Width, p.Y * res.Height);
        }

        public PointF worldToScreenMantainRatio(Vector3 pos)
        {
            var p = Main.WorldToScreen(pos.ToVector());
            var res = getScreenResolutionMantainRatio();

            return new PointF(p.X*res.Width, p.Y*res.Height);
        }

        public string getCurrentResourceName()
        {
            return ParentResourceName;
        }

        public Vector3 screenToWorld(PointF pos)
        {
            var res = getScreenResolution();
            var norm = new Vector2(pos.X / res.Width, pos.Y / res.Height);
            var norm2 = new Vector2((norm.X - 0.5f) * 2f, (norm.Y - 0.5f) * 2f);

            var p = Main.RaycastEverything(norm2);

            return p.ToLVector();
        }

        public Vector3 screenToWorldMantainRatio(PointF pos)
        {
            var res = getScreenResolutionMantainRatio();
            var norm = new Vector2(pos.X / res.Width, pos.Y / res.Height);
            var norm2 = new Vector2((norm.X - 0.5f) * 2f, (norm.Y - 0.5f) * 2f);

            var p = Main.RaycastEverything(norm2);

            return p.ToLVector();
        }

        public Vector3 screenToWorld(PointF pos, Vector3 camPos, Vector3 camRot) // TODO: replace this with a camera object
        {
            var res = getScreenResolution();
            var norm = new Vector2(pos.X / res.Width, pos.Y / res.Height);
            var norm2 = new Vector2((norm.X - 0.5f) * 2f, (norm.Y - 0.5f) * 2f);

            var p = Main.RaycastEverything(norm2, camPos.ToVector(), camRot.ToVector());

            return p.ToLVector();
        }

        public Vector3 screenToWorldMantainRatio(PointF pos, Vector3 camPos, Vector3 camrot) // TODO: replace this with a camera object
        {
            var res = getScreenResolutionMantainRatio();
            var norm = new Vector2(pos.X / res.Width, pos.Y / res.Height);
            var norm2 = new Vector2((norm.X - 0.5f) * 2f, (norm.Y - 0.5f) * 2f);

            var p = Main.RaycastEverything(norm2, camPos.ToVector(), camrot.ToVector());

            return p.ToLVector();
        }

        public class Raycast
        {
            internal Raycast(RaycastResult res)
            {
                wrapper = res;
            }

            private RaycastResult wrapper;

            public bool didHitAnything
            {
                get { return wrapper.DitHit; }
            }

            public bool didHitEntity
            {
                get { return wrapper.DitHitEntity; }
            }

            public LocalHandle hitEntity
            {
                get { return new LocalHandle(wrapper.HitEntity?.Handle ?? 0); }
            }

            public Vector3 hitCoords
            {
                get { return wrapper.HitPosition.ToLVector(); }
            }
        }

        public Raycast createRaycast(Vector3 start, Vector3 end, int flag, LocalHandle? ignoreEntity)
        {
            if (ignoreEntity != null)
                return new Raycast(World.Raycast(start.ToVector(), end.ToVector(), (IntersectOptions) flag, new Prop(ignoreEntity.Value.Value)));
            else
                return new Raycast(World.Raycast(start.ToVector(), end.ToVector(), (IntersectOptions)flag));
        }

        public Vector3 getGameplayCamPos()
        {
            return GameplayCamera.Position.ToLVector();
        }

        public Vector3 getGameplayCamRot()
        {
            return GameplayCamera.Rotation.ToLVector();
        }

        public Vector3 getGameplayCamDir()
        {
            return GameplayCamera.Direction.ToLVector();
        }

        public void setCanOpenChat(bool show)
        {
            Main.CanOpenChatbox = show;
        }

        public bool getCanOpenChat()
        {
            return Main.CanOpenChatbox;
        }

        public Browser createCefBrowser(double width, double height, string page, bool local = true)
        {
            var newBrowser = new Browser(Engine, new Size((int)width, (int)height), local, page);
            CEFManager.Browsers.Add(newBrowser);
            return newBrowser;
        }

        public void destroyCefBrowser(Browser browser)
        {
            CEFManager.Browsers.Remove(browser);
            try
            {
                browser.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.LogException(ex, "DESTROYCEFBROWSER");
            }
        }

        public bool isCefBrowserInitialized(Browser browser)
        {
            return browser.IsInitialized();
        }
        
        public void waitUntilCefBrowserInitalization(Browser browser)
        {
            while (!browser.IsInitialized())
            {
                sleep(0);
            }
        }
        
        public void setCefBrowserSize(Browser browser, double width, double height)
        {
            browser.Size = new Size((int) width, (int) height);
        }

        public Size getCefBrowserSize(Browser browser)
        {
            return browser.Size;
        }

        public void setCefBrowserHeadless(Browser browser, bool headless)
        {
            browser.Headless = headless;
        }

        public bool getCefBrowserHeadless(Browser browser)
        {
            return browser.Headless;
        }

        public void setCefBrowserPosition(Browser browser, double xPos, double yPos)
        {
            browser.Position = new Point((int) xPos, (int) yPos);
        }

        public Point getCefBrowserPosition(Browser browser)
        {
            return browser.Position;
        }

        public void pinCefBrowser(Browser browser, double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
        {
            browser.Pinned = new PointF[4];
            browser.Pinned[0] = new PointF((float) x1, (float) y1);
            browser.Pinned[1] = new PointF((float) x2, (float) y2);
            browser.Pinned[2] = new PointF((float) x3, (float) y3);
            browser.Pinned[3] = new PointF((float) x4, (float) y4);
        }

        public void clearCefPinning(Browser browser)
        {
            browser.Pinned = null;
        }

        public void verifyIntegrityOfCache()
        {
            if (!DownloadManager.CheckFileIntegrity())
            {
                Main.Client.Disconnect("Quit");
                DownloadManager.FileIntegrity.Clear();
                return;
            }
        }

        public void loadPageCefBrowser(Browser browser, string uri)
        {
            if (browser == null) return;

            if (browser._localMode)
            {
                checkPathSafe(uri);

                if (!DownloadManager.CheckFileIntegrity())
                {
                    Main.Client.Disconnect("Quit");
                    DownloadManager.FileIntegrity.Clear();
                    return;
                }

                string fullUri = "http://" + ParentResourceName + "/" + uri.TrimStart('/');

                browser.GoToPage(fullUri);
            }
            else
            {
                // TODO: Check whitelist domains

                browser.GoToPage(uri);
            }
        }

        public bool isCefBrowserLoading(Browser browser)
        {
            return browser.IsLoading();
        }

        private bool parseHash(string hash, out Hash result)
        {
            if (hash.StartsWith("0x"))
            {
                result = (Hash) ulong.Parse(hash.Substring(2), NumberStyles.HexNumber);
                return true;
            }

            if (!Hash.TryParse(hash, out result))
                return false;
            return true;
        }
        
        public void callNative(string hash, params object[] args)
        {
            Hash ourHash;
            if (!parseHash(hash, out ourHash))
                return;
            Function.Call(ourHash, args.Select(o =>
            {
                if (o is LocalHandle)
                    return new InputArgument(((LocalHandle) o).Value);
                else if (o is fArg)
                    return new InputArgument(((fArg) o).Value);
                return new InputArgument(o);
            }).ToArray());
        }

        public object returnNative(string hash, int returnType, params object[] args)
        {
            Hash ourHash;
            if (!parseHash(hash, out ourHash))
                return null;
            var fArgs = args.Select(o =>
            {
                if (o is LocalHandle)
                    return new InputArgument(((LocalHandle) o).Value);
                else if (o is fArg)
                    return new InputArgument(((fArg)o).Value);
                return new InputArgument(o);
            }).ToArray();
            switch ((ReturnType)returnType)
            {
                case ReturnType.Int:
                    return Function.Call<int>(ourHash, fArgs);
                case ReturnType.UInt:
                    return Function.Call<uint>(ourHash, fArgs);
                case ReturnType.Long:
                    return Function.Call<long>(ourHash, fArgs);
                case ReturnType.ULong:
                    return Function.Call<ulong>(ourHash, fArgs);
                case ReturnType.String:
                    return Function.Call<string>(ourHash, fArgs);
                case ReturnType.Vector3:
                    return Function.Call<GTA.Math.Vector3>(ourHash, fArgs).ToLVector();
                case ReturnType.Vector2:
                    return Function.Call<Vector2>(ourHash, fArgs);
                case ReturnType.Float:
                    return Function.Call<float>(ourHash, fArgs);
                case ReturnType.Bool:
                    return Function.Call<bool>(ourHash, fArgs);
                case ReturnType.Handle:
                    return new LocalHandle(Function.Call<int>(ourHash, fArgs));
                default:
                    return null;
            }
        }

        public void setUiColor(int r, int g, int b)
        {
            Main.UIColor = Color.FromArgb(r, g, b);
        }

        public int getHashKey(string input)
        {
            return Game.GenerateHash(input);
        }

        public bool setEntityData(LocalHandle entity, string key, object data)
        {
            return Main.SetEntityProperty(entity, key, data);
        }

        public void resetEntityData(LocalHandle entity, string key)
        {
            Main.ResetEntityProperty(entity, key);
        }

        public bool hasEntityData(LocalHandle entity, string key)
        {
            return Main.HasEntityProperty(entity, key);
        }

        public object getEntityData(LocalHandle entity, string key)
        {
            return Main.GetEntityProperty(entity, key);
        }

        public bool setWorldData(string key, object data)
        {
            return Main.SetWorldData(key, data);
        }

        public void resetWorldData(string key)
        {
            Main.ResetWorldData(key);
        }

        public bool hasWorldData(string key)
        {
            return Main.HasWorldData(key);
        }

        public object getWorldData(string key)
        {
            return Main.GetWorldData(key);
        }

        public int getGamePlayer()
        {
            return Game.Player.Handle;
        }

        public LocalHandle getLocalPlayer()
        {
            return new LocalHandle(Game.Player.Character.Handle);
        }

        public Vector3 getEntityPosition(LocalHandle entity)
        {
            if (entity.Properties<IStreamedItem>().StreamedIn)
                return new Prop(entity.Value).Position.ToLVector();
            else return entity.Properties<IStreamedItem>().Position;
        }

        public Vector3 getEntityRotation(LocalHandle entity)
        {
            if (entity.Properties<IStreamedItem>().StreamedIn)
                return new Prop(entity.Value).Rotation.ToLVector();
            else return ((EntityProperties)entity.Properties<IStreamedItem>()).Rotation;
        }

        public Vector3 getEntityVelocity(LocalHandle entity)
        {
            return new Prop(entity.Value).Velocity.ToLVector();
        }

        public float getVehicleHealth(LocalHandle entity)
        {
            if (entity.Properties<RemoteVehicle>().StreamedIn)
                return new Vehicle(entity.Value).EngineHealth;
            else return entity.Properties<RemoteVehicle>().Health;
        }

        public float getVehicleRPM(LocalHandle entity)
        {
            return new Vehicle(entity.Value).CurrentRPM;
        }

        public bool isPlayerInAnyVehicle(LocalHandle player)
        {
            return new Ped(player.Value).IsInVehicle();
        }

        public bool isPlayerOnFire(LocalHandle player)
        {
            return new Ped(player.Value).IsOnFire;
        }

        public bool isPlayerParachuting(LocalHandle player)
        {
            return new Ped(player.Value).ParachuteState == ParachuteState.Gliding;
        }

        public bool isPlayerInFreefall(LocalHandle player)
        {
            return Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, player.Value) == 0 &&
                new Ped(player.Value).IsInAir;
        }

        public bool isPlayerAiming(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return new Ped(player.Value).IsAiming;
            }
            else
            {
                return handleToSyncPed(player).IsAiming;
            }
        }

        public bool isPlayerShooting(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return new Ped(player.Value).IsShooting;
            }
            else
            {
                return handleToSyncPed(player).IsShooting;
            }
        }

        public bool isPlayerReloading(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return new Ped(player.Value).IsReloading;
            }
            else
            {
                return handleToSyncPed(player).IsReloading;
            }
        }

        public bool isPlayerInCover(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return new Ped(player.Value).IsInCover();
            }
            else
            {
                return handleToSyncPed(player).IsInCover;
            }
        }

        public bool isPlayerOnLadder(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return Subtask.IsSubtaskActive(player.Value, ESubtask.USING_LADDER);
            }
            else
            {
                return handleToSyncPed(player).IsOnLadder;
            }
        }

        public Vector3 getPlayerAimingPoint(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return Main.RaycastEverything(new Vector2(0, 0)).ToLVector();
            }
            else
            {
                return handleToSyncPed(player).AimCoords.ToLVector();
            }
        }

        public bool isPlayerDead(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
            {
                return Game.Player.Character.IsDead;
            }
            else
            {
                return handleToSyncPed(player).IsPlayerDead;
            }
        }

        public bool doesEntityExist(LocalHandle entity)
        {
            return !entity.IsNull;
        }

        public void setEntityInvincible(LocalHandle entity, bool invincible)
        {
            entity.Properties<EntityProperties>().IsInvincible = invincible;

            new Prop(entity.Value).IsInvincible = invincible;
        }

        public bool getEntityInvincible(LocalHandle entity)
        {
            if (!entity.IsNull)
            {
                return new Prop(entity.Value).IsInvincible;
            }

            return entity.Properties<EntityProperties>().IsInvincible;
        }

        public void createParticleEffectOnPosition(string ptfxLibrary, string ptfxName, Vector3 position,
            Vector3 rotation, float scale)
        {
            Util.LoadPtfxAsset(ptfxLibrary);
            Function.Call(Hash._SET_PTFX_ASSET_NEXT_CALL, ptfxLibrary);
            Function.Call((Hash) 0x25129531F77B9ED3, ptfxName, position.X, position.Y, position.Z, rotation.X,
                rotation.Y, rotation.Z,
                scale, 0, 0, 0);
        }

        public void createParticleEffectOnEntity(string ptfxLibrary, string ptfxName, LocalHandle entity, Vector3 offset,
            Vector3 rotation, float scale, int boneIndex = -1)
        {
            Util.LoadPtfxAsset(ptfxLibrary);
            Function.Call(Hash._SET_PTFX_ASSET_NEXT_CALL, ptfxLibrary);

            if (boneIndex <= 0)
            {
                Function.Call((Hash) 0x0D53A3B8DA0809D2, ptfxName, entity.Value, offset.X, offset.Y, offset.Z,
                    rotation.X, rotation.Y, rotation.Z,
                    scale, 0, 0, 0);
            }
            else
            {
                Function.Call((Hash)0x0E7E72961BA18619, ptfxName, entity.Value, offset.X, offset.Y, offset.Z,
                    rotation.X, rotation.Y, rotation.Z,
                    boneIndex, scale, 0, 0, 0);
            }
        }

        public void createExplosion(int explosionType, Vector3 position, float damageScale)
        {
            Function.Call((Hash) 0xE3AD2BDBAEE269AC, position.X, position.Y, position.Z, explosionType, damageScale,
                true, false);
        }

        public void createOwnedExplosion(LocalHandle owner, int explosionType, Vector3 position, float damageScale)
        {
            Function.Call((Hash)0x172AA1B624FA1013, owner.Value, position.X, position.Y, position.Z, explosionType, damageScale, true, false, 1f);
        }

        public void setVehicleLivery(LocalHandle vehicle, int livery)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.Livery = livery;
                if (veh.StreamedIn) new Vehicle(vehicle.Value).Mods.Livery = livery;
            }
        }

        public int getVehicleLivery(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return new Vehicle(vehicle.Value).Mods.Livery;

                return veh.Livery;
            }
            return 0;
        }

        public LocalHandle getVehicleTrailer(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh == null || veh.Trailer == 0) return new LocalHandle(0);

            return new LocalHandle(veh.Trailer, HandleType.NetHandle);
        }

        public LocalHandle getVehicleTraileredBy(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh == null || veh.TraileredBy == 0) return new LocalHandle(0);

            return new LocalHandle(veh.TraileredBy, HandleType.NetHandle);
        }

        public bool getVehicleSirenState(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return new Vehicle(vehicle.Value).SirenActive;

                return veh.Siren;
            }
            return false;
        }

        public bool isVehicleTyrePopped(LocalHandle vehicle, int tyre)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return Util.BuildTyreArray(new Vehicle(vehicle.Value))[tyre];

                return (veh.Tires & (1 << tyre)) != 0;
            }

            return false;
        }

        public void popVehicleTyre(LocalHandle vehicle, int tyre, bool pop)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn)
                {
                    if (pop)
                        new Vehicle(vehicle.Value).Wheels[tyre].Burst();
                    else new Vehicle(vehicle.Value).Wheels[tyre].Fix();
                }

                if (pop)
                {
                    veh.Tires |= (byte)(1 << tyre);
                }
                else
                {
                    veh.Tires &= (byte)~(1 << tyre);
                }
            }
        }

        public bool isVehicleDoorBroken(LocalHandle vehicle, int door)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return new Vehicle(vehicle.Value).Doors[(VehicleDoorIndex)door].IsBroken;

                return (veh.Doors & (1 << door)) != 0;
            }

            return false;
        }

        public void breakVehicleTyre(LocalHandle vehicle, int door, bool breakDoor)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn)
                {
                    if (breakDoor)
                        new Vehicle(vehicle.Value).Doors[(VehicleDoorIndex)door].Break(true);
                }

                if (breakDoor)
                {
                    veh.Doors |= (byte)(1 << door);
                }
                else
                {
                    veh.Doors &= (byte)~(1 << door);
                }
            }
        }

        public bool isVehicleWindowBroken(LocalHandle vehicle, int window)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return !new Vehicle(vehicle.Value).Windows[(VehicleWindowIndex)window].IsIntact;

                if (veh.DamageModel == null) return false;
                return (veh.DamageModel.BrokenWindows & (1 << window)) != 0;
            }

            return false;
        }

        public void breakVehicleWindow(LocalHandle vehicle, int window, bool breakWindow)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn)
                {
                    if (breakWindow)
                        new Vehicle(vehicle.Value).Windows[(VehicleWindowIndex)window].Smash();
                    else new Vehicle(vehicle.Value).Windows[(VehicleWindowIndex)window].Repair();
                }

                if (breakWindow)
                {
                    veh.Tires |= (byte)(1 << window);
                }
                else
                {
                    veh.Tires &= (byte)~(1 << window);
                }
            }
        }

        public void setVehicleExtra(LocalHandle vehicle, int slot, bool enabled)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) new Vehicle(vehicle.Value).ToggleExtra(slot, enabled);

                if (enabled)
                    veh.VehicleComponents |= (short)(1 << slot);
                else
                    veh.VehicleComponents &= (short)~(1 << slot);
            }
        }

        public bool getVehicleExtra(LocalHandle vehicle, int slot)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return new Vehicle(vehicle.Value).IsExtraOn(slot);
                return (veh.VehicleComponents & (1 << slot)) != 0;
            }

            return false;
        }

        public void setVehicleNumberPlate(LocalHandle vehicle, string plate)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) new Vehicle(veh.LocalHandle).Mods.LicensePlate = plate;
                veh.NumberPlate = plate;
            }
        }

        public string getVehicleNumberPlate(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return new Vehicle(veh.LocalHandle).Mods.LicensePlate;
                return veh.NumberPlate;
            }

            return null;
        }

        public void setVehicleEngineStatus(LocalHandle vehicle, bool turnedOn)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (!turnedOn)
                {
                    veh.Flag = (byte)PacketOptimization.SetBit(veh.Flag, EntityFlag.EngineOff);
                }
                else
                {
                    veh.Flag = (byte)PacketOptimization.ResetBit(veh.Flag, EntityFlag.EngineOff);
                }

                new Vehicle(vehicle.Value).IsEngineRunning = turnedOn;
                new Vehicle(vehicle.Value).IsDriveable = !turnedOn;
            }
        }

        public bool getEngineStatus(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                return PacketOptimization.CheckBit(veh.Flag, EntityFlag.EngineOff);
            }

            return false;
        }

        public void setVehicleSpecialLightStatus(LocalHandle vehicle, bool status)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn)
                {
                    new Vehicle(vehicle.Value).TaxiLightOn = status;
                    new Vehicle(vehicle.Value).SearchLightOn = status;
                }

                if (status)
                    veh.Flag = (byte) PacketOptimization.SetBit(veh.Flag, EntityFlag.SpecialLight);
                else
                    veh.Flag = (byte) PacketOptimization.ResetBit(veh.Flag, EntityFlag.SpecialLight);
            }
        }

        public bool getVehicleSpecialLightStatus(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                return PacketOptimization.CheckBit(veh.Flag, EntityFlag.SpecialLight);
            }

            return false;
        }

        public void setEntityCollissionless(LocalHandle entity, bool status)
        {
            if (status)
                entity.Properties<EntityProperties>().Flag =
                    (byte) PacketOptimization.SetBit(entity.Properties<EntityProperties>().Flag, EntityFlag.Collisionless);
            else
                entity.Properties<EntityProperties>().Flag =
                    (byte) PacketOptimization.ResetBit(entity.Properties<EntityProperties>().Flag, EntityFlag.Collisionless);

            if (entity.Properties<IStreamedItem>().StreamedIn)
            {
                new Prop(entity.Value).IsCollisionEnabled = !status;
            }
        }

        public bool getEntityCollisionless(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<EntityProperties>();

            if (veh != null)
            {
                return PacketOptimization.CheckBit(veh.Flag, EntityFlag.Collisionless);
            }

            return false;
        }

        public void setVehicleMod(LocalHandle vehicle, int slot, int modType)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh.Mods == null) veh.Mods = new Dictionary<byte, int>();

            veh.Mods[(byte) slot] = modType;

            if (slot >= 60)
            {
                Util.SetNonStandardVehicleMod(new Vehicle(vehicle.Value), slot, modType);
            }
            else
            {
                if (slot >= 17 && slot <= 22)
                    new Vehicle(vehicle.Value).Mods[(VehicleToggleModType)slot].IsInstalled = modType != 0;
                else
                    new Vehicle(vehicle.Value).SetMod(slot, modType, false);
            }
        }

        public int getVehicleMod(LocalHandle vehicle, int slot)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                return veh.Mods?[(byte) slot] ?? -1;
            }

            return 0;
        }

        public void removeVehicleMod(LocalHandle vehicle, int slot)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.Mods?.Remove((byte) slot);

                if (veh.StreamedIn)
                    Function.Call((Hash)0x92D619E420858204, vehicle.Value, slot);
            }
        }

        public void setVehicleBulletproofTyres(LocalHandle vehicle, bool bulletproof)
        {
            setVehicleMod(vehicle, 61, bulletproof ? 0x01 : 0x00);
        }

        public bool getVehicleBulletproofTyres(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 61) != 0;
        }

        public void setVehicleNumberPlateStyle(LocalHandle vehicle, int style)
        {
            setVehicleMod(vehicle, 62, style);
        }

        public int getVehicleNumberPlateStyle(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 62);
        }

        public void setVehiclePearlescentColor(LocalHandle vehicle, int color)
        {
            setVehicleMod(vehicle, 63, color);
        }

        public int getVehiclePearlescentColor(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 63);
        }

        public void setVehicleWheelColor(LocalHandle vehicle, int color)
        {
            setVehicleMod(vehicle, 64, color);
        }

        public int getVehicleWheelColor(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 64);
        }

        public void setVehicleWheelType(LocalHandle vehicle, int type)
        {
            setVehicleMod(vehicle, 65, type);
        }

        public int getVehicleWheelType(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 65);
        }

        
        public void setVehicleModColor1(LocalHandle vehicle, int r, int g, int b)
        {
            setVehicleMod(vehicle, 66, Color.FromArgb(r,g,b).ToArgb());
        }
        /*
        public void getVehicleModColor1(LocalHandle vehicle, out byte red, out byte green, out byte blue)
        {
            var val = getVehicleMod(vehicle, 66);
            byte a;
            Extensions.ToArgb(val, out a, out red, out green, out blue);
        }

        */

        public void setVehicleModColor2(LocalHandle vehicle, int r, int g, int b)
        {
            setVehicleMod(vehicle, 67, Color.FromArgb(r,g,b).ToArgb());
        }
        /*
        public void getVehicleModColor2(NetHandle vehicle, out byte red, out byte green, out byte blue)
        {
            var val = getVehicleMod(vehicle, 67);
            byte a;
            Extensions.ToArgb(val, out a, out red, out green, out blue);
        }
        */
        public void setVehicleTyreSmokeColor(LocalHandle vehicle, int r, int g, int b)
        {
            setVehicleMod(vehicle, 68, Color.FromArgb(r,g,b).ToArgb());
        }
        /*
        public void getVehicleTyreSmokeColor(NetHandle vehicle, out byte red, out byte green, out byte blue)
        {
            var val = getVehicleMod(vehicle, 68);
            byte a;
            Extensions.ToArgb(val, out a, out red, out green, out blue);
        }
        */
        
        public void setVehicleWindowTint(LocalHandle vehicle, int type)
        {
            setVehicleMod(vehicle, 69, type);
        }

        public int getVehicleWindowTint(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 69);
        }

        public void setVehicleEnginePowerMultiplier(LocalHandle vehicle, float mult)
        {
            setVehicleMod(vehicle, 70, BitConverter.ToInt32(BitConverter.GetBytes(mult), 0));
        }

        public float getVehicleEnginePowerMultiplier(LocalHandle vehicle)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(getVehicleMod(vehicle, 70)), 0);
        }

        public void setVehicleEngineTorqueMultiplier(LocalHandle vehicle, float mult)
        {
            setVehicleMod(vehicle, 71, BitConverter.ToInt32(BitConverter.GetBytes(mult), 0));
        }

        public float getVehicleEngineTorqueMultiplier(LocalHandle vehicle)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(getVehicleMod(vehicle, 71)), 0);
        }

        public void setVehicleNeonState(LocalHandle vehicle, int slot, bool turnedOn)
        {
            var currentState = getVehicleMod(vehicle, 72);

            if (turnedOn)
                setVehicleMod(vehicle, 72, currentState | 1 << slot);
            else
                setVehicleMod(vehicle, 72, currentState & ~(1 << slot));
        }

        public bool getVehicleNeonState(LocalHandle vehicle, int slot)
        {
            return (getVehicleMod(vehicle, 72) & (1 << slot)) != 0;
        }

        public void setVehicleNeonColor(LocalHandle vehicle, int r, int g, int b)
        {
            setVehicleMod(vehicle, 73, Color.FromArgb(r,g,b).ToArgb());
        }
        /*
        public void getVehicleNeonColor(NetHandle vehicle, out byte red, out byte green, out byte blue)
        {
            var val = getVehicleMod(vehicle, 73);
            byte a;
            Extensions.ToArgb(val, out a, out red, out green, out blue);
        }
        */
        public void setVehicleDashboardColor(LocalHandle vehicle, int type)
        {
            setVehicleMod(vehicle, 74, type);
        }

        public int getVehicleDashboardColor(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 74);
        }

        public void setVehicleTrimColor(LocalHandle vehicle, int type)
        {
            setVehicleMod(vehicle, 75, type);
        }

        public int getVehicleTrimColor(LocalHandle vehicle)
        {
            return getVehicleMod(vehicle, 75);
        }

        public string getVehicleDisplayName(int model)
        {
            return Function.Call<string>(Hash._GET_LABEL_TEXT,
                Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, (int) model));
        }

        public float getVehicleMaxSpeed(int model)
        {
            return Function.Call<float>((Hash)0xF417C2502FFFED43, (int)model);
        }

        public float getVehicleMaxBraking(int model)
        {
            return Function.Call<float>((Hash)0xDC53FD41B4ED944C, (int)model);
        }

        public float getVehicleMaxTraction(int model)
        {
            return Function.Call<float>((Hash)0x539DE94D44FDFD0D, (int)model);
        }

        public float getVehicleMaxAcceleration(int model)
        {
            return Function.Call<float>((Hash)0x8C044C5C84505B6A, (int)model);
        }

        public float getVehicleMaxOccupants(int model)
        {
            return Function.Call<int>(Hash._GET_VEHICLE_MODEL_MAX_NUMBER_OF_PASSENGERS, (int)model);
        }

        public int getVehicleClass(int model)
        {
            return Function.Call<int>(Hash.GET_VEHICLE_CLASS_FROM_NAME, (int)model);
        }

        public void detonatePlayerStickies()
        {
            Function.Call((Hash)0xFC4BD125DE7611E4, Game.Player.Character, (int)WeaponHash.StickyBomb, true);
        }

        public void setPlayerSkin(int model)
        {
            Util.SetPlayerSkin((PedHash)model);
            setPlayerDefaultClothes();
        }

        public void setPlayerDefaultClothes()
        {
            Function.Call((Hash)0x45EEE61580806D63, Game.Player.Character);
        }

        public void setPlayerTeam(int team)
        {
            Main.LocalTeam = team;
        }

        public void playPlayerScenario(string name)
        {
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, Game.Player.Character, name, 0, 0);
        }

        public void playPlayerAnimation(string animDict, string animName, int flag, int duration = -1)
        {
            Util.LoadDict(animDict);
            Game.Player.Character.Task.PlayAnimation(animDict, animName, -8f, 8f, duration, (AnimationFlags) flag, 8f);
        }

        public void stopPlayerAnimation()
        {
            Game.Player.Character.Task.ClearAll();
        }

        public void setVehiclePrimaryColor(LocalHandle vehicle, int color)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.PrimaryColor = color;

                if (veh.StreamedIn) new Vehicle(vehicle.Value).Mods.PrimaryColor = (VehicleColor) color;
            }
        }

        public void setVehicleSecondaryColor(LocalHandle vehicle, int color)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.SecondaryColor = color;

                if (veh.StreamedIn) new Vehicle(vehicle.Value).Mods.SecondaryColor = (VehicleColor)color;
            }
        }

        public void setVehicleCustomPrimaryColor(LocalHandle vehicle, int r, int g, int b)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.PrimaryColor = Color.FromArgb(0x01, r, g, b).ToArgb();

                if (veh.StreamedIn)
                    new Vehicle(vehicle.Value).Mods.CustomPrimaryColor = Color.FromArgb(0x01, r, g, b);
            }
        }

        public void setVehicleCustomSecondaryColor(LocalHandle vehicle, int r, int g, int b)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                veh.SecondaryColor = Color.FromArgb(0x01, r,g,b).ToArgb();

                if (veh.StreamedIn)
                    new Vehicle(vehicle.Value).Mods.CustomSecondaryColor = Color.FromArgb(0x01, r, g, b);
            }
        }

        public Color getVehicleCustomPrimaryColor(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                byte r, g, b, a;
                Util.ToArgb(veh.PrimaryColor, out a, out r, out g, out b);

                return Color.FromArgb(r, g, b);
            }

            return Color.White;
        }

        public Color getVehicleCustomSecondaryColor(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                byte r, g, b, a;
                Util.ToArgb(veh.SecondaryColor, out a, out r, out g, out b);

                return Color.FromArgb(r, g, b);
            }

            return Color.White;
        }

        public int getVehiclePrimaryColor(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn) return (int)new Vehicle(vehicle.Value).Mods.PrimaryColor;

                return veh.PrimaryColor;
            }

            return 0;
        }

        public int getVehicleSecondaryColor(LocalHandle vehicle)
        {
            var veh = vehicle.Properties<RemoteVehicle>();

            if (veh != null)
            {
                if (veh.StreamedIn)
                    return (int)new Vehicle(vehicle.Value).Mods.SecondaryColor;

                return veh.SecondaryColor;
            }

            return 0;
        }

        public void setPlayerClothes(LocalHandle player, int slot, int drawable, int texture)
        {
            var pl = player.Properties<RemotePlayer>();

            Function.Call((Hash)0x262B14F48D29DE80, player.Value, slot, drawable, texture, 2);

            if (pl.Textures == null) pl.Textures = new Dictionary<byte, byte>();
            if (pl.Props == null) pl.Props = new Dictionary<byte, byte>();

            pl.Textures[(byte) slot] = (byte)texture;
            pl.Textures[(byte) slot] = (byte) drawable;
        }

        public void setPlayerAccessory(LocalHandle player, int slot, int drawable, int texture)
        {
            var pl = player.Properties<RemotePlayer>();

            Function.Call((Hash)0x93376B65A266EB5F, player.Value, slot, drawable, texture, true);

            if (pl.Accessories == null) pl.Accessories = new Dictionary<byte, Tuple<byte, byte>>();

            pl.Accessories[(byte)slot] = new Tuple<byte, byte>((byte) drawable, (byte) texture);
        }

        public void clearPlayerAccessory(LocalHandle player, int slot)
        {
            var pl = player.Properties<RemotePlayer>();

            Function.Call((Hash)0x0943E5B8E078E76E, player.Value, slot);

            if (pl.Accessories == null) return;

            pl.Accessories.Remove((byte) slot);
        }

        public VehicleHash vehicleNameToModel(string modelName)
        {
            return (from object value in Enum.GetValues(typeof(VehicleHash)) where modelName.ToLower() == ((VehicleHash)value).ToString().ToLower() select ((VehicleHash)value)).FirstOrDefault();
        }

        public PedHash pedNameToModel(string modelName)
        {
            return (from object value in Enum.GetValues(typeof(PedHash)) where modelName.ToLower() == ((PedHash)value).ToString().ToLower() select ((PedHash)value)).FirstOrDefault();
        }
        
        public WeaponHash weaponNameToModel(string modelName)
        {
            return (from object value in Enum.GetValues(typeof(WeaponHash)) where modelName.ToLower() == ((WeaponHash)value).ToString().ToLower() select ((WeaponHash)value)).FirstOrDefault();
        }

        public void clearPlayerTasks()
        {
            Game.Player.Character.Task.ClearAllImmediately();
        }

        public void setEntityPositionFrozen(LocalHandle entity, bool frozen)
        {
            new Prop(entity.Value).IsPositionFrozen = frozen;
        }

        public void setEntityVelocity(LocalHandle entity, Vector3 velocity)
        {
            new Prop(entity.Value).Velocity = velocity.ToVector();
        }

        public int getPlayerVehicleSeat(LocalHandle player)
        {
            return Util.GetPedSeat(new Ped(player.Value));
        }

        public void setPlayerWeaponTint(int weapon, int tint)
        {
            Function.Call((Hash)0x50969B9B89ED5738, Game.Player.Character, weapon, tint);
            ((RemotePlayer) Main.NetEntityHandler.NetToStreamedItem(Game.Player.Character.Handle, useGameHandle: true))
                .WeaponTints[weapon] = (byte) tint;
        }

        public int getPlayerWeaponTint(int weapon)
        {
            return ((RemotePlayer)Main.NetEntityHandler.NetToStreamedItem(Game.Player.Character.Handle, useGameHandle: true))
                .WeaponTints[weapon];
        }

        public void givePlayerWeaponComponent(int weapon, int component)
        {
            Game.Player.Character.Weapons[(WeaponHash)weapon].SetComponent((WeaponComponent) component, true);
        }

        public void removePlayerWeaponComponent(int weapon, int component)
        {
            Game.Player.Character.Weapons[(WeaponHash)weapon].SetComponent((WeaponComponent)component, false);
        }

        public bool hasPlayerWeaponComponent(int weapon, int component)
        {
            return Game.Player.Character.Weapons[(WeaponHash) weapon].IsComponentActive((WeaponComponent) component);
        }

        public WeaponComponent[] getAllWeaponComponents(WeaponHash weapon)
        {
            switch (weapon)
            {
                default:
                    return new WeaponComponent[0];
                case WeaponHash.Pistol:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.PistolClip01,
                        WeaponComponent.PistolClip02,
                        WeaponComponent.AtPiFlsh,
                        WeaponComponent.AtPiSupp02,
                        WeaponComponent.PistolVarmodLuxe,
                    };
                case WeaponHash.CombatPistol:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.CombatPistolClip01,
                        WeaponComponent.CombatPistolClip02,
                        WeaponComponent.AtPiFlsh,
                        WeaponComponent.AtPiSupp,
                        WeaponComponent.CombatPistolVarmodLowrider,
                    };
                case WeaponHash.APPistol:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.APPistolClip01,
                        WeaponComponent.APPistolClip02,
                        WeaponComponent.AtPiFlsh,
                        WeaponComponent.AtPiSupp,
                        WeaponComponent.APPistolVarmodLuxe,
                    };
                case WeaponHash.MicroSMG:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.MicroSMGClip01,
                        WeaponComponent.MicroSMGClip02,
                        WeaponComponent.AtPiFlsh,
                        WeaponComponent.AtScopeMacro,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.MicroSMGVarmodLuxe,
                    };
                case WeaponHash.SMG:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SMGClip01,
                        WeaponComponent.SMGClip02,
                        WeaponComponent.SMGClip03,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtPiSupp,
                        WeaponComponent.AtScopeMacro02,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.SMGVarmodLuxe,
                    };
                case WeaponHash.AssaultRifle:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AssaultRifleClip01,
                        WeaponComponent.AssaultRifleClip02,
                        WeaponComponent.AssaultRifleClip03,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeMacro,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.AssaultRifleVarmodLuxe,
                    };
                case WeaponHash.CarbineRifle:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.CarbineRifleClip01,
                        WeaponComponent.CarbineRifleClip02,
                        WeaponComponent.CarbineRifleClip03,
                        WeaponComponent.AtRailCover01,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeMedium,
                        WeaponComponent.AtArSupp,
                        WeaponComponent.CarbineRifleVarmodLuxe,
                    };
                case WeaponHash.AdvancedRifle:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AdvancedRifleClip01,
                        WeaponComponent.AdvancedRifleClip02,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeSmall,
                        WeaponComponent.AtArSupp,
                        WeaponComponent.AdvancedRifleVarmodLuxe,
                    };
                case WeaponHash.MG:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.MGClip01,
                        WeaponComponent.MGClip02,
                        WeaponComponent.AtScopeSmall02,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.MGVarmodLowrider,
                    };
                case WeaponHash.CombatMG:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.CombatMGClip01,
                        WeaponComponent.CombatMGClip02,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtScopeMedium,
                        WeaponComponent.CombatMGVarmodLowrider,
                    };
                case WeaponHash.PumpShotgun:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AtSrSupp,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.PumpShotgunVarmodLowrider,
                    };
                case WeaponHash.AssaultShotgun:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AssaultShotgunClip01,
                        WeaponComponent.AssaultShotgunClip02,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtArSupp,
                    };
                case WeaponHash.SniperRifle:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SniperRifleClip01,
                        WeaponComponent.AtScopeLarge,
                        WeaponComponent.AtScopeMax,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.SniperRifleVarmodLuxe,
                    };
                case WeaponHash.HeavySniper:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.HeavySniperClip01,
                        WeaponComponent.AtScopeLarge,
                        WeaponComponent.AtScopeMax,
                    };
                case WeaponHash.GrenadeLauncher:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeSmall,
                    };
                case WeaponHash.Minigun:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.MinigunClip01,
                    };
                case WeaponHash.AssaultSMG:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AssaultSMGClip01,
                        WeaponComponent.AssaultSMGClip02,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeMacro,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.AssaultSMGVarmodLowrider,
                    };
                case WeaponHash.BullpupShotgun:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtArSupp02,
                    };
                case WeaponHash.Pistol50:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.Pistol50Clip01,
                        WeaponComponent.Pistol50Clip02,
                        WeaponComponent.AtPiFlsh,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.Pistol50VarmodLuxe,
                    };
                case WeaponHash.CombatPDW:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.CombatPDWClip01,
                        WeaponComponent.CombatPDWClip02,
                        WeaponComponent.CombatPDWClip03,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeSmall,
                        WeaponComponent.AtArAfGrip,
                    };
                case WeaponHash.SawnOffShotgun:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SawnoffShotgunVarmodLuxe,
                    };
                case WeaponHash.BullpupRifle:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.BullpupRifleClip01,
                        WeaponComponent.BullpupRifleClip02,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeSmall,
                        WeaponComponent.AtArSupp,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.BullpupRifleVarmodLow,
                    };
                case WeaponHash.SNSPistol:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SNSPistolClip01,
                        WeaponComponent.SNSPistolClip02,
                        WeaponComponent.SNSPistolVarmodLowrider,
                    };
                case WeaponHash.SpecialCarbine:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SpecialCarbineClip01,
                        WeaponComponent.SpecialCarbineClip02,
                        WeaponComponent.SpecialCarbineClip03,
                        WeaponComponent.AtArFlsh,
                        WeaponComponent.AtScopeMedium,
                        WeaponComponent.AtArSupp02,
                        WeaponComponent.AtArAfGrip,
                        WeaponComponent.SpecialCarbineVarmodLowrider,
                    };
                case WeaponHash.KnuckleDuster:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.KnuckleVarmodPimp,
                        WeaponComponent.KnuckleVarmodBallas,
                        WeaponComponent.KnuckleVarmodDollar,
                        WeaponComponent.KnuckleVarmodDiamond,
                        WeaponComponent.KnuckleVarmodHate,
                        WeaponComponent.KnuckleVarmodLove,
                        WeaponComponent.KnuckleVarmodPlayer,
                        WeaponComponent.KnuckleVarmodKing,
                        WeaponComponent.KnuckleVarmodVagos,
                    };
                case WeaponHash.MachinePistol:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.MachinePistolClip01,
                        WeaponComponent.MachinePistolClip02,
                        WeaponComponent.MachinePistolClip03,
                        WeaponComponent.AtPiSupp,
                    };
                case WeaponHash.SwitchBlade:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.SwitchbladeVarmodVar1,
                        WeaponComponent.SwitchbladeVarmodVar2,
                    };
                case WeaponHash.Revolver:
                    return new WeaponComponent[]
                    {
                        WeaponComponent.RevolverClip01,
                        WeaponComponent.RevolverVarmodBoss,
                        WeaponComponent.RevolverVarmodGoon,
                    };
            }
        }

        public int getPlayerCurrentWeapon()
        {
            return (int)Game.Player.Character.Weapons.Current.Hash;
        }

        public void disconnect(string reason)
        {
            Main.Client.Disconnect(reason);
        }

        public void setEntityPosition(LocalHandle ent, Vector3 pos)
        {
            new Prop(ent.Value).Position = pos.ToVector();
            ent.Properties<EntityProperties>().Position = pos;
        }

        public void setEntityRotation(LocalHandle ent, Vector3 rot)
        {
            new Prop(ent.Value).Rotation = rot.ToVector();
            ent.Properties<EntityProperties>().Rotation = rot;
        }

        public void setPlayerIntoVehicle(LocalHandle vehicle, int seat)
        {
            Game.Player.Character.SetIntoVehicle(new Vehicle(vehicle.Value), (VehicleSeat)seat);
        }

        public void setPlayerHealth(int health)
        {
            Game.Player.Character.Health = health;
        }

        public int getPlayerHealth(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
                return Game.Player.Character.Health;
            else return handleToSyncPed(player).PedHealth;
        }

        public void setTextLabelText(LocalHandle label, string text)
        {
            label.Properties<RemoteTextLabel>().Text = text;
        }

        public void setTextLabelColor(LocalHandle textLabel, int alpha, int r, int g, int b)
        {
            var p = textLabel.Properties<RemoteTextLabel>();

            p.Alpha = (byte)alpha;
            p.Red = (byte)r;
            p.Green = (byte)g;
            p.Blue = (byte)b;
        }

        public Color getTextLabelColor(LocalHandle textLabel)
        {
            var p = textLabel.Properties<RemoteTextLabel>();

            return Color.FromArgb(p.Alpha, p.Red, p.Green, p.Blue);
        }

        public void setTextLabelSeethrough(LocalHandle handle, bool seethrough)
        {
            handle.Properties<RemoteTextLabel>().EntitySeethrough = seethrough;
        }

        public bool getTextLabelSeethrough(LocalHandle handle)
        {
            return handle.Properties<RemoteTextLabel>().EntitySeethrough;
        }

        public Vector3 getOffsetInWorldCoords(LocalHandle entity, Vector3 offset)
        {
            return new Prop(entity.Value).GetOffsetInWorldCoords(offset.ToVector()).ToLVector();
        }

        public Vector3 getOffsetFromWorldCoords(LocalHandle entity, Vector3 pos)
        {
            return new Prop(entity.Value).GetOffsetFromWorldCoords(pos.ToVector()).ToLVector();
        }

        public void drawLine(Vector3 start, Vector3 end, int a, int r, int g, int b)
        {
            Function.Call(Hash.DRAW_LINE, start.X, start.Y, start.Z, end.X, end.Y, end.Z, r,g,b,a);
        }

        public void playSoundFrontEnd(string audioLib, string audioName)
        {
            Function.Call((Hash)0x2F844A8B08D76685, audioLib, true);
            Function.Call((Hash)0x67C540AA08E4A6F5, -1, audioName, audioLib);
        }

        public void showShard(string text)
        {
            NativeUI.BigMessageThread.MessageInstance.ShowMissionPassedMessage(text);
        }

        public void showShard(string text, int timeout)
        {
            NativeUI.BigMessageThread.MessageInstance.ShowMissionPassedMessage(text, timeout);
        }

        public XmlGroup loadConfig(string config)
        {
            if (!config.EndsWith(".xml")) return null;
            var path = getResourceFilePath(config);
            checkPathSafe(path);

            var xml = new XmlGroup();
            xml.Load(path);
            return xml;
        }

        public dynamic fromJson(string json)
        {
            return JObject.Parse(json);
        }

        public string toJson(object data)
        {
            return JsonConvert.SerializeObject(data);
        }

        public SizeF getScreenResolutionMantainRatio()
        {
            return UIMenu.GetScreenResolutionMantainRatio();
        }

        public void sendChatMessage(string sender, string text)
        {
            Main.Chat.AddMessage(sender, text);
        }

        public void sendChatMessage(string text)
        {
            Main.Chat.AddMessage(null, text);
        }

        public Size getScreenResolution()
        {
            return GTA.UI.Screen.Resolution;
        }

        public void sendNotification(string text)
        {
            Util.SafeNotify(text);
        }

        public void displaySubtitle(string text)
        {
            GTA.UI.Screen.ShowSubtitle(text);
        }

        public void displaySubtitle(string text, double duration)
        {
            GTA.UI.Screen.ShowSubtitle(text, (int) duration);
        }

        public string formatTime(double ms, string format)
        {
            return TimeSpan.FromMilliseconds(ms).ToString(format);
        }

        public void setPlayerInvincible(bool invinc)
        {
            var remotePlayer = Main.NetEntityHandler.EntityToStreamedItem(Game.Player.Character.Handle) as RemotePlayer;
            if (remotePlayer != null)
            {
                remotePlayer.IsInvincible = invinc;
            }

            Game.Player.IsInvincible = invinc;
        }

        public void setPlayerWantedLevel(int wantedLevel)
        {
            Function.Call(Hash.SET_FAKE_WANTED_LEVEL, wantedLevel);
        }

        public int getPlayerWantedLevel()
        {
            return Function.Call<int>((Hash)0x4C9296CBCD1B971E);
        }

        public bool getPlayerInvincible()
        {
            return Game.Player.IsInvincible;
        }
        
        public void setPlayerArmor(int armor)
        {
            Game.Player.Character.Armor = armor;
        }

        public int getPlayerArmor(LocalHandle player)
        {
            if (player.Value == Game.Player.Character.Handle)
                return Game.Player.Character.Health;
            else return handleToSyncPed(player).PedHealth;
        }

        public LocalHandle[] getStreamedPlayers()
        {
            return Main.NetEntityHandler.ClientMap.Where(item => item is SyncPed).Cast<SyncPed>().Select(op => new LocalHandle(op.Character?.Handle ?? 0)).ToArray();
        }

        public LocalHandle[] getStreamedVehicles()
        {
            return Main.NetEntityHandler.ClientMap.Where(item => item is RemoteVehicle && item.StreamedIn).Cast<RemoteVehicle>().Select(op => new LocalHandle(op.LocalHandle)).ToArray();
        }

        public LocalHandle[] getStreamedObjects()
        {
            return Main.NetEntityHandler.ClientMap.Where(item => item is RemoteProp && item.StreamedIn).Cast<RemoteProp>().Select(op => new LocalHandle(op.LocalHandle)).ToArray();
        }

        public LocalHandle[] getStreamedPickups()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemotePickup && item.StreamedIn)
                    .Cast<RemotePickup>().Select(op => new LocalHandle(op.LocalHandle)).ToArray();
        }

        public LocalHandle[] getStreamedPeds()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemotePed && item.StreamedIn)
                    .Cast<RemotePed>().Select(op => new LocalHandle(op.LocalHandle)).ToArray();
        }

        public LocalHandle[] getStreamedMarkers()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemoteMarker && item.StreamedIn)
                    .Cast<RemoteMarker>().Select(op => new LocalHandle(op.RemoteHandle, op.LocalOnly ? HandleType.LocalHandle : HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getStreamedTextLabels()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemoteTextLabel && item.StreamedIn)
                    .Cast<RemoteTextLabel>().Select(op => new LocalHandle(op.RemoteHandle, op.LocalOnly ? HandleType.LocalHandle : HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllVehicles()
        {
            return Main.NetEntityHandler.ClientMap.Where(item => item is RemoteVehicle)
                .Cast<RemoteVehicle>().Select(op => new LocalHandle(op.RemoteHandle, HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllObjects()
        {
            return Main.NetEntityHandler.ClientMap.Where(item => item is RemoteProp)
                .Cast<RemoteProp>().Select(op => new LocalHandle(op.RemoteHandle, HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllPickups()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemotePickup)
                    .Cast<RemotePickup>().Select(op => new LocalHandle(op.RemoteHandle, HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllPeds()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemotePed)
                    .Cast<RemotePed>().Select(op => new LocalHandle(op.RemoteHandle, HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllMarkers()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemoteMarker)
                    .Cast<RemoteMarker>().Select(op => new LocalHandle(op.RemoteHandle, op.LocalOnly ? HandleType.LocalHandle : HandleType.NetHandle)).ToArray();
        }

        public LocalHandle[] getAllTextLabels()
        {
            return Main.NetEntityHandler.ClientMap.Where(item =>
                item is RemoteTextLabel)
                    .Cast<RemoteTextLabel>().Select(op => new LocalHandle(op.RemoteHandle, op.LocalOnly ? HandleType.LocalHandle : HandleType.NetHandle)).ToArray();
        }

        public LocalHandle getPlayerVehicle(LocalHandle player)
        {
            return new LocalHandle(new Ped(player.Value).CurrentVehicle?.Handle ?? 0);
        }

        public void explodeVehicle(LocalHandle vehicle)
        {
            new Vehicle(vehicle.Value).Explode();
        }

        public LocalHandle getPlayerByName(string name)
        {
            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed) op).Name == name) as SyncPed;
            if (opp != null && opp.Character != null)
                return new LocalHandle(opp.Character.Handle);
            return new LocalHandle(0);
        }

        public string getPlayerName(LocalHandle player)
        {
            if (player == getLocalPlayer())
            {
                return
                    ((RemotePlayer)
                        Main.NetEntityHandler.ClientMap.First(
                            op => op is RemotePlayer && ((RemotePlayer) op).LocalHandle == -2)).Name;
            }

            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == player.Value) as SyncPed;
            if (opp != null)
                return opp.Name;
            return null;
        }

        public void forceSendAimData(bool force)
        {
            SyncCollector.ForceAimData = force;
        }

        public bool isAimDataForced()
        {
            return SyncCollector.ForceAimData;
        }

        public Vector3 getPlayerAimCoords(LocalHandle player)
        {
            if (player == getLocalPlayer()) return Main.RaycastEverything(new Vector2(0, 0)).ToLVector();

            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == player.Value) as SyncPed;
            if (opp != null)
                return opp.AimCoords.ToLVector();
            return new Vector3();
        }

        private SyncPed handleToSyncPed(LocalHandle handle)
        {
            return Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == handle.Value) as SyncPed;
        }

        public int getPlayerPing(LocalHandle player)
        {
            if (player == getLocalPlayer()) return (int)(Main.Latency*1000f);

            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == player.Value) as SyncPed;
            if (opp != null)
                return (int)(opp.Latency * 1000f);
            return 0;
        }

        public LocalHandle createVehicle(int model, Vector3 pos, float heading)
        {
            var car = Main.NetEntityHandler.CreateLocalVehicle(model, pos, heading);
            return new LocalHandle(car, HandleType.LocalHandle);
        }

        public LocalHandle createPed(int model, Vector3 pos, float heading)
        {
            var ped = Main.NetEntityHandler.CreateLocalPed(model, pos, heading);
            return new LocalHandle(ped, HandleType.LocalHandle);
        }

        public LocalHandle createBlip(Vector3 pos)
        {
            var blip = Main.NetEntityHandler.CreateLocalBlip(pos);
            return new LocalHandle(blip, HandleType.LocalHandle);
        }

        public void setBlipPosition(LocalHandle blip, Vector3 pos)
        {
            if (blip.Properties<RemoteBlip>().StreamedIn)
                new Blip(blip.Value).Position = pos.ToVector();
            blip.Properties<RemoteBlip>().Position = pos;
        }

        public Vector3 getBlipPosition(LocalHandle blip)
        {
            if (new Blip(blip.Value).Exists())
            {
                return new Blip(blip.Value).Position.ToLVector();
            }
            else
            {
                return blip.Properties<RemoteBlip>().Position;
            }
        }

        public Vector3 getWaypointPosition()
        {
            return World.WaypointPosition.ToLVector();
        }

        public bool isWaypointSet()
        {
            return Game.IsWaypointActive;
        }

        public void setBlipColor(LocalHandle blip, int color)
        {
            var ourBlip = new Blip(blip.Value);

            if (blip.Properties<RemoteBlip>().StreamedIn)
                new Blip(blip.Value).Color = (BlipColor) color;
            blip.Properties<RemoteBlip>().Color = color;
        }

        public int getBlipColor(LocalHandle blip)
        {
            if (new Blip(blip.Value).Exists())
            {
                return (int)new Blip(blip.Value).Color;
            }

            return blip.Properties<RemoteBlip>().Color;
        }

        public void setBlipSprite(LocalHandle blip, int sprite)
        {
            if (blip.Properties<RemoteBlip>().StreamedIn)
                new Blip(blip.Value).Sprite = (BlipSprite) sprite;
            blip.Properties<RemoteBlip>().Sprite = sprite;
        }

        public int getBlipSprite(LocalHandle blip)
        {
            if (new Blip(blip.Value).Exists())
            {
                return (int)new Blip(blip.Value).Sprite;
            }

            return blip.Properties<RemoteBlip>().Sprite;
        }

        public void setBlipName(LocalHandle blip, string name)
        {
            blip.Properties<RemoteBlip>().Name = name;
        }

        public string getBlipName(LocalHandle blip)
        {
            return blip.Properties<RemoteBlip>().Name;
        }

        public void setBlipTransparency(LocalHandle blip, int alpha)
        {
            blip.Properties<RemoteBlip>().Alpha = (byte)alpha;
            Function.Call((Hash)0x45FF974EEE1C8734, blip.Value, alpha);
        }

        public int getBlipTransparency(LocalHandle blip)
        {
            return blip.Properties<RemoteBlip>().Alpha;
        }

        public void setBlipShortRange(LocalHandle blip, bool shortRange)
        {
            if (blip.Properties<RemoteBlip>().StreamedIn)
                new Blip(blip.Value).IsShortRange = shortRange;
            blip.Properties<RemoteBlip>().IsShortRange = shortRange;
        }

        public bool getBlipShortRange(LocalHandle blip)
        {
            return blip.Properties<RemoteBlip>().IsShortRange;
        }
        
        public void setBlipScale(LocalHandle blip, double scale)
        {
            setBlipScale(blip, (float) scale);
        }

        public void setBlipScale(LocalHandle blip, float scale)
        {
            if (blip.Properties<RemoteBlip>().StreamedIn)
                new Blip(blip.Value).Scale = scale;
            blip.Properties<RemoteBlip>().Scale = scale;
        }

        public float getBlipScale(LocalHandle blip)
        {
            return blip.Properties<RemoteBlip>().Scale;
        }

        public void setChatVisible(bool display)
        {
            Main.ScriptChatVisible = display;
        }

        public bool getChatVisible()
        {
            return Main.ScriptChatVisible;
        }

        public float getAveragePacketSize()
        {
            float outp = 0;

            if (Main._averagePacketSize.Count > 0)
                outp = (float)Main._averagePacketSize.Average();

            return outp;
        }

        public float getBytesSentPerSecond()
        {
            return Main._bytesSentPerSecond;
        }

        public float getBytesReceivedPerSecond()
        {
            return Main._bytesReceivedPerSecond;
        }

        public void requestControlOfPlayer(LocalHandle player)
        {
            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == player.Value) as SyncPed;
            if (opp != null)
            {
                opp.IsBeingControlledByScript = true;
            }
        }

        public void stopControlOfPlayer(LocalHandle player)
        {
            var opp = Main.NetEntityHandler.ClientMap.FirstOrDefault(op => op is SyncPed && ((SyncPed)op).Character.Handle == player.Value) as SyncPed;
            if (opp != null)
            {
                opp.IsBeingControlledByScript = false;
            }
        }

        public void setHudVisible(bool visible)
        {
            Function.Call(Hash.DISPLAY_RADAR, visible);
            Function.Call(Hash.DISPLAY_HUD, visible);
        }

        public bool isSpectating()
        {
            return Main.IsSpectating;
        }

        public bool getHudVisible()
        {
            return !Function.Call<bool>(Hash.IS_HUD_HIDDEN);
        }

        public LocalHandle createMarker(int markerType, Vector3 pos, Vector3 dir, Vector3 rot, Vector3 scale, int r, int g, int b, int alpha)
        {
            return new LocalHandle(Main.NetEntityHandler.CreateLocalMarker(markerType, pos.ToVector(), dir.ToVector(), rot.ToVector(), scale.ToVector(), alpha, r, g, b), HandleType.LocalHandle);
        }

        public void setMarkerType(LocalHandle marker, int type)
        {
            marker.Properties<RemoteMarker>().MarkerType = type;
        }

        public int getMarkerType(LocalHandle marker)
        {
            return marker.Properties<RemoteMarker>().MarkerType;
        }

        public void setMarkerColor(LocalHandle marker, int alpha, int r, int g, int b)
        {
            var p = marker.Properties<RemoteMarker>();

            p.Alpha = (byte) alpha;
            p.Red = (byte)r;
            p.Green = (byte)g;
            p.Blue = (byte)b;
        }

        public Color getMarkerColor(LocalHandle marker)
        {
            var p = marker.Properties<RemoteMarker>();

            return Color.FromArgb(p.Alpha, p.Red, p.Green, p.Blue);
        }

        public void setMarkerPosition(LocalHandle marker, Vector3 pos)
        {
            var delta = new Delta_MarkerProperties();
            delta.Position = pos;
            
            Main.NetEntityHandler.UpdateMarker(marker.Value, delta, true);
        }

        public Vector3 getMarkerPosition(LocalHandle marker)
        {
            return marker.Properties<RemoteMarker>().Position;
        }

        public void setMarkerRotation(LocalHandle marker, Vector3 rotation)
        {
            marker.Properties<RemoteMarker>().Rotation = rotation;
        }

        public Vector3 getMarkerRotation(LocalHandle marker)
        {
            return marker.Properties<RemoteMarker>().Rotation;
        }

        public void setMarkerScale(LocalHandle marker, Vector3 scale)
        {
            var delta = new Delta_MarkerProperties();
            delta.Scale = scale;

            Main.NetEntityHandler.UpdateMarker(marker.Value, delta, true);
        }

        public Vector3 getMarkerScale(LocalHandle marker)
        {
            return marker.Properties<RemoteMarker>().Scale;
        }

        public void setMarkerDirection(LocalHandle marker, Vector3 dir)
        {
            var delta = new Delta_MarkerProperties();
            delta.Direction = dir;

            Main.NetEntityHandler.UpdateMarker(marker.Value, delta, true);
        }

        public Vector3 getMarkerDirection(LocalHandle marker)
        {
            return marker.Properties<RemoteMarker>().Direction;
        }

        public void deleteEntity(LocalHandle handle)
        {
            var item = handle.Properties<IStreamedItem>();

            Main.NetEntityHandler.StreamOut(item);
            Main.NetEntityHandler.Remove(item);
        }

        public void attachEntity(LocalHandle ent1, LocalHandle ent2, string bone, Vector3 positionOffset, Vector3 rotationOffset)
        {
            if (ent1.Properties<IStreamedItem>().AttachedTo != null)
            {
                Main.NetEntityHandler.DetachEntity(ent1.Properties<IStreamedItem>(), false);
            }

            Main.NetEntityHandler.AttachEntityToEntity(ent1.Properties<IStreamedItem>(), ent2.Properties<IStreamedItem>(), new Attachment()
            {
                Bone = bone,
                PositionOffset = positionOffset,
                RotationOffset = rotationOffset,
                NetHandle = ent2.Properties<IStreamedItem>().RemoteHandle,
            });
        }

        public void detachEntity(LocalHandle ent)
        {
            Main.NetEntityHandler.DetachEntity(ent.Properties<IStreamedItem>(), false);
        }

        public bool isEntityAttachedToAnything(LocalHandle ent)
        {
            return ent.Properties<IStreamedItem>() != null;
        }

        public bool isEntityAttachedToEntity(LocalHandle from, LocalHandle to)
        {
            return (from.Properties<IStreamedItem>().AttachedTo?.NetHandle ?? 0) == to.Properties<IStreamedItem>().RemoteHandle;
        }

        public LocalHandle createTextLabel(string text, Vector3 pos, float range, float size)
        {
            return new LocalHandle(Main.NetEntityHandler.CreateLocalLabel(text, pos.ToVector(), range, size, 0), HandleType.LocalHandle);
        }

        public string getResourceFilePath(string fileName)
        {
            if (!isPathSafe(fileName)) throw new AccessViolationException("Illegal path to file!");
            return FileTransferId._DOWNLOADFOLDER_ + ParentResourceName + "\\" + fileName;
        }

        public Vector3 lerpVector(Vector3 start, Vector3 end, double currentTime, double duration)
        {
            return GTA.Math.Vector3.Lerp(start.ToVector(), end.ToVector(), (float) (currentTime/duration)).ToLVector();
        }

        public double lerpFloat(double start, double end, double currentTime, double duration)
        {
            return Util.LinearFloatLerp((float) start, (float) end, (int) currentTime, (int) duration);
        }

        public bool isInRangeOf(Vector3 entity, Vector3 destination, double range)
        {
            return (entity - destination).LengthSquared() < (range*range);
        }
        
        public void dxDrawTexture(string path, Point pos, Size size, int id = 60)
        {
            if (!Main.UIVisible || Main.MainMenu.Visible) return;
            if (!isPathSafe(path)) throw new Exception("Illegal path for texture!");
            path = getResourceFilePath(path);
            Util.DxDrawTexture(id, path, pos.X, pos.Y, size.Width, size.Height, 0f, 255, 255, 255, 255);
        }

        public void drawGameTexture(string dict, string txtName, double x, double y, double width, double height, double heading,
            int r, int g, int b, int alpha)
        {
            if (!Main.UIVisible || Main.MainMenu.Visible) return;
            if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict))
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, true);

            int screenw = GTA.UI.Screen.Resolution.Width;
            int screenh = GTA.UI.Screen.Resolution.Height;
            const float hh = 1080f;
            float ratio = (float)screenw / screenh;
            var ww = hh * ratio;


            float w = (float)(width / ww);
            float h = (float)(height / hh);
            float xx = (float)(x / ww) + w * 0.5f;
            float yy = (float)(y / hh) + h * 0.5f;

            Function.Call(Hash.DRAW_SPRITE, dict, txtName, xx, yy, w, h, heading, r, g, b, alpha);
        }

        public void drawRectangle(double xPos, double yPos, double wSize, double hSize, int r, int g, int b, int alpha)
        {
            if (!Main.UIVisible || Main.MainMenu.Visible) return;
            int screenw = GTA.UI.Screen.Resolution.Width;
            int screenh = GTA.UI.Screen.Resolution.Height;
            const float height = 1080f;
            float ratio = (float)screenw / screenh;
            var width = height*ratio;

            float w = (float)wSize / width;
            float h = (float)hSize / height;
            float x = (((float)xPos) / width) + w * 0.5f;
            float y = (((float)yPos) / height) + h * 0.5f;

            Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, alpha);
        }

        public void drawText(string caption, double xPos, double yPos, double scale, int r, int g, int b, int alpha, int font,
            int justify, bool shadow, bool outline, int wordWrap)
        {
            if (!Main.UIVisible || Main.MainMenu.Visible) return;
            int screenw = GTA.UI.Screen.Resolution.Width;
            int screenh = GTA.UI.Screen.Resolution.Height;
            const float height = 1080f;
            float ratio = (float)screenw / screenh;
            var width = height * ratio;

            float x = (float)(xPos) / width;
            float y = (float)(yPos) / height;

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, 1.0f, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, alpha);
            if (shadow)
                Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline)
                Function.Call(Hash.SET_TEXT_OUTLINE);
            switch (justify)
            {
                case 1:
                    Function.Call(Hash.SET_TEXT_CENTRE, true);
                    break;
                case 2:
                    Function.Call(Hash.SET_TEXT_RIGHT_JUSTIFY, true);
                    Function.Call(Hash.SET_TEXT_WRAP, 0, x);
                    break;
            }

            if (wordWrap != 0)
            {
                float xsize = (float)(xPos + wordWrap) / width;
                Function.Call(Hash.SET_TEXT_WRAP, x, xsize);
            }

            //Function.Call(Hash._SET_TEXT_ENTRY, "CELL_EMAIL_BCON");
            Function.Call(Hash._SET_TEXT_ENTRY, new InputArgument(Main.StringCache.GetCached("CELL_EMAIL_BCON")));
            //NativeUI.UIResText.AddLongString(caption);

            const int maxStringLength = 99;

            for (int i = 0; i < caption.Length; i += maxStringLength)
            {
                Function.Call((Hash) 0x6C188BE134E074AA,
                    new InputArgument(
                        Main.StringCache.GetCached(caption.Substring(i,
                            System.Math.Min(maxStringLength, caption.Length - i)))));
                //Function.Call((Hash)0x6C188BE134E074AA, caption.Substring(i, System.Math.Min(maxStringLength, caption.Length - i)));
            }

            Function.Call(Hash._DRAW_TEXT, x, y);
        }

        public UIResText addTextElement(string caption, double x, double y, double scale, int r, int g, int b, int a, int font, int alignment)
        {
            var txt = new UIResText(caption, new Point((int) x, (int) y), (float) scale, Color.FromArgb(a, r, g, b),
                (GTA.UI.Font) font, (UIResText.Alignment) alignment);
            JavascriptHook.TextElements.Add(txt);
            
            return txt;
        }

        public int getGameTime()
        {
            return Game.GameTime;
        }

        public int getGlobalTime()
        {
            return Environment.TickCount;
        }

        public double angleBetween(Vector3 from, Vector3 to)
        {
            return Math.Abs((Math.Atan2(to.Y, to.X) - Math.Atan2(from.Y, from.X)) * (180.0 / Math.PI));
        }

        public bool isPed(LocalHandle ent)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_PED, ent.Value);
        }

        public bool isVehicle(LocalHandle ent)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, ent.Value);
        }

        public bool isProp(LocalHandle ent)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_AN_OBJECT, ent.Value);
        }

        public float toFloat(double d)
        {
            return (float) d;
        }

        public struct fArg
        {
            public float Value;

            public fArg(double f)
            {
                Value = (float) f;
            }
        }

        public fArg f(double value)
        {
            return new fArg(value);
        }

        public void sleep(int ms)
        {
            var start = DateTime.Now;
            do
            {
                if (isDisposing) throw new Exception("resource is terminating");
                Script.Wait(0);
            } while (DateTime.Now.Subtract(start).TotalMilliseconds < ms);
        }

        public void startAudio(string path, bool looped)
        {
            //if (path.StartsWith("http")) return;
            path = getResourceFilePath(path);
            AudioThread.StartAudio(path, looped);
        }

        public void pauseAudio()
        {
            JavascriptHook.AudioDevice?.Pause();
        }

        public void resumeAudio()
        {
            JavascriptHook.AudioDevice?.Play();
        }

        public void setAudioTime(double seconds)
        {
            if (JavascriptHook.AudioReader != null)
                JavascriptHook.AudioReader.CurrentTime = TimeSpan.FromSeconds(seconds);
        }

        public double getAudioTime()
        {
            if (JavascriptHook.AudioReader != null) return JavascriptHook.AudioReader.CurrentTime.TotalSeconds;
            return 0;
        }

        public bool isAudioPlaying()
        {
            if (JavascriptHook.AudioDevice != null) return JavascriptHook.AudioDevice.PlaybackState == PlaybackState.Playing;
            return false;
        }

        public void setGameVolume(double vol)
        {
            if (JavascriptHook.AudioDevice != null) JavascriptHook.AudioDevice.Volume = (float)vol;
        }

        public bool isAudioInitialized()
        {
            return JavascriptHook.AudioDevice != null;
        }

        public void stopAudio()
        {
            AudioThread.DisposeAudio();
        }

        public void triggerServerEvent(string eventName, params object[] arguments)
        {
            Main.TriggerServerEvent(eventName, ParentResourceName, arguments);
        }

        public string toString(object obj)
        {
            return obj.ToString();
        }

        public delegate void ServerEventTrigger(string eventName, object[] arguments);
        public delegate void ChatEvent(string msg);
        public delegate void StreamEvent(LocalHandle item, int entityType);
        public delegate void DataChangedEvent(LocalHandle entity, string key, object oldValue);
        public delegate void CustomDataReceived(string data);
        public delegate void EmptyEvent();
        public delegate void EntityEvent(LocalHandle entity);
        public delegate void PlayerKilledEvent(LocalHandle killer, int weapon);
        public delegate void IntChangeEvent(int oldValue);
        public delegate void BoolChangeEvent(bool oldValue);

        public event EmptyEvent onResourceStart;
        public event EmptyEvent onResourceStop;
        public event EmptyEvent onUpdate;
        public event KeyEventHandler onKeyDown;
        public event KeyEventHandler onKeyUp;
        public event ServerEventTrigger onServerEventTrigger;
        public event ChatEvent onChatMessage;
        public event ChatEvent onChatCommand;
        public event StreamEvent onEntityStreamIn;
        public event StreamEvent onEntityStreamOut;
        public event DataChangedEvent onEntityDataChange;
        public event CustomDataReceived onCustomDataReceived;
        public event PlayerKilledEvent onPlayerDeath;
        public event EmptyEvent onPlayerRespawn;
        public event EntityEvent onPlayerPickup;
        public event EntityEvent onPlayerEnterVehicle;
        public event EntityEvent onPlayerExitVehicle;
        public event IntChangeEvent onVehicleHealthChange;
        public event IntChangeEvent onVehicleDoorBreak;
        public event IntChangeEvent onVehicleWindowSmash;
        public event IntChangeEvent onPlayerHealthChange;
        public event IntChangeEvent onPlayerArmorChange;
        public event IntChangeEvent onPlayerWeaponSwitch;
        public event IntChangeEvent onPlayerModelChange;
        public event EmptyEvent onVehicleSirenToggle;
        public event EmptyEvent onPlayerDetonateStickies;
        public event IntChangeEvent onVehicleTyreBurst;

        internal void invokePlayerKilled(LocalHandle item, int weapon)
        {
            onPlayerDeath?.Invoke(item, weapon);
        }

        internal void invokeEntityStreamIn(LocalHandle item, int type)
        {
            onEntityStreamIn?.Invoke(item, type);
        }

        internal void invokeCustomDataReceived(string data)
        {
            onCustomDataReceived?.Invoke(data);
        }

        internal void invokeEntityDataChange(LocalHandle item, string key, object oldValue)
        {
            onEntityDataChange?.Invoke(item, key, oldValue);
        }

        internal void invokeEntityStreamOut(LocalHandle item, int type)
        {
            onEntityStreamOut?.Invoke(item, type);
        }

        internal void invokeChatMessage(string msg)
        {
            onChatMessage?.Invoke(msg);
        }

        internal void invokeChatCommand(string msg)
        {
            onChatCommand?.Invoke(msg);
        }

        internal void invokeServerEvent(string eventName, object[] arsg)
        {
            onServerEventTrigger?.Invoke(eventName, arsg);
        }

        internal void invokeResourceStart()
        {
            onResourceStart?.Invoke();
        }

        internal void invokeUpdate()
        {
            onUpdate?.Invoke();
        }

        internal void invokeResourceStop()
        {
            onResourceStop?.Invoke();
        }

        internal void invokeKeyDown(object sender, KeyEventArgs e)
        {
            onKeyDown?.Invoke(sender, e);
        }

        internal void invokeKeyUp(object sender, KeyEventArgs e)
        {
            onKeyUp?.Invoke(sender, e);
        }

        #region Menus

        public UIMenu createMenu(string banner, string subtitle, double x, double y, int anchor)
        {
            var offset = convertAnchorPos((float)x, (float)y, (Anchor)anchor, 431f, 107f + 38 + 38f * 10);
            return new UIMenu(banner, subtitle, new Point((int)(offset.X), (int)(offset.Y))) { ScaleWithSafezone = false };
        }

        public UIMenu createMenu(string subtitle, double x, double y, int anchor)
        {
            var offset = convertAnchorPos((float)x, (float)y - 107, (Anchor)anchor, 431f, 107f + 38 + 38f * 10);
            var newM = new UIMenu("", subtitle, new Point((int)(offset.X), (int)(offset.Y)));
            newM.ScaleWithSafezone = false;
            newM.SetBannerType(new UIResRectangle());
            newM.ControlDisablingEnabled = false;
            return newM;
        }

        public UIMenuItem createMenuItem(string label, string description)
        {
            return new UIMenuItem(label, description);
        }

        public UIMenuColoredItem createColoredItem(string label, string description, string hexColor, string hexHighlightColor)
        {
            return new UIMenuColoredItem(label, description, ColorTranslator.FromHtml(hexColor), ColorTranslator.FromHtml(hexHighlightColor));
        }

        public UIMenuCheckboxItem createCheckboxItem(string label, string description, bool isChecked)
        {
            return new UIMenuCheckboxItem(label, isChecked, description);
        }

        public UIMenuListItem createListItem(string label, string description, List<string> items, int index)
        {
            return new UIMenuListItem(label, items.Select(s => (dynamic)s).ToList(), index, description);
        }

        public MenuPool getMenuPool()
        {
            return new MenuPool();
        }

        public void drawMenu(UIMenu menu)
        {
            if (!Main.UIVisible || Main.MainMenu.Visible) return;
            
            if (!Main.Chat.IsFocused)
            {
                menu.ProcessControl();
                menu.ProcessMouse();
            }

            menu.Draw();

            if (menu.Visible)
            {
                Game.DisableAllControlsThisFrame(0);
            }
        }

        public void setMenuBannerSprite(UIMenu menu, string spritedict, string spritename)
        {
            menu.SetBannerType(new Sprite(spritedict, spritename, new Point(), new Size()));
        }

        public void setMenuBannerTexture(UIMenu menu, string path)
        {
            var realpath = getResourceFilePath(path);
            menu.SetBannerType(realpath);
        }

        public void setMenuBannerRectangle(UIMenu menu, int alpha, int red, int green, int blue)
        {
            menu.SetBannerType(new UIResRectangle(new Point(), new Size(), Color.FromArgb(alpha, red, green, blue)));
        }

        public string getUserInput(string defaultText, int maxlen)
        {
            return Game.GetUserInput(defaultText, maxlen);
        }

        public bool isControlJustPressed(int control)
        {
            return Game.IsControlJustPressed(0, (GTA.Control) control);
        }

        public bool isControlPressed(int control)
        {
            return Game.IsControlPressed(0, (GTA.Control)control);
        }

        public bool isDisabledControlJustReleased(int control)
        {
            return Game.IsDisabledControlJustReleased(0, (GTA.Control)control);
        }

        public bool isDisabledControlJustPressed(int control)
        {
            return Game.IsDisabledControlJustPressed(0, (GTA.Control)control);
        }

        public bool isDisabledControlPressed(int control)
        {
            return Game.IsControlPressed(0, (GTA.Control)control);
        }

        public bool isControlJustReleased(int control)
        {
            return Game.IsControlJustReleased(0, (GTA.Control)control);
        }

        public void disableControlThisFrame(int control)
        {
            Game.DisableControlThisFrame(0, (GTA.Control)control);
        }

        public void enableControlThisFrame(int control)
        {
            Game.EnableControlThisFrame(0, (GTA.Control)control);
        }

        public bool isChatOpen()
        {
            return Main.Chat.IsFocused;
        }

        public void loadAnimationDict(string dict)
        {
            Util.LoadDict(dict);
        }

        public void loadModel(int model)
        {
            Util.LoadModel(new Model(model));
        }

        public Scaleform requestScaleform(string scaleformName)
        {
            var sc = new Scaleform(scaleformName);
            return sc;
        }

        public void renderScaleform(Scaleform sc, double x, double y, double w, double h)
        {
            sc.Render2DScreenSpace(new PointF((float) x, (float) y), new PointF((float) w, (float) h));
        }

        public void setEntityTransparency(LocalHandle entity, int alpha)
        {
            if (alpha == 255)
            {
                Function.Call(Hash.RESET_ENTITY_ALPHA, entity.Value);
                return;
            }

            new Prop(entity.Value).Opacity = alpha;
        }

        public int getEntityType(LocalHandle entity)
        {
            return (int) entity.Properties<IStreamedItem>().EntityType;
        }

        public int getEntityTransparency(LocalHandle entity)
        {
            return new Prop(entity.Value).Opacity;
        }

        public void setEntityDimension(LocalHandle entity, int dimension)
        {
            entity.Properties<IStreamedItem>().Dimension = dimension;
        }

        public int getEntityDimension(LocalHandle entity)
        {
            return entity.Properties<IStreamedItem>().Dimension;
        }

        public int getEntityModel(LocalHandle entity)
        {
            return new Prop(entity.Value).Model.Hash;
        }

        public void givePlayerWeapon(int weapon, int ammo, bool equipNow, bool ammoLoaded)
        {
            CrossReference.EntryPoint.WeaponInventoryManager.Allow((WeaponHash) weapon);
            Game.Player.Character.Weapons.Give((WeaponHash) weapon, ammo, equipNow, ammoLoaded);
        }

        public void removeAllPlayerWeapons()
        {
            Game.Player.Character.Weapons.RemoveAll();
            CrossReference.EntryPoint.WeaponInventoryManager.Clear();
        }

        public bool doesPlayerHaveWeapon(int weapon)
        {
            return Game.Player.Character.Weapons.HasWeapon((WeaponHash) weapon);
        }

        public void removePlayerWeapon(int weapon)
        {
            CrossReference.EntryPoint.WeaponInventoryManager.Deny((WeaponHash) weapon);
        }

        public void setWeather(string weather)
        {
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, weather);
        }

        public void resetWeather()
        {
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, Main.Weather);
        }

        public void setTime(double hours, double minutes)
        {
            World.CurrentDayTime = new TimeSpan((int) hours, (int) minutes, 00);
        }

        public void resetTime()
        {
            if (Main.Time != null)
            {
                World.CurrentDayTime = Main.Time.Value;
            }
        }

        internal PointF convertAnchorPos(float x, float y, Anchor anchor, float xOffset, float yOffset)
        {
            var res = UIMenu.GetScreenResolutionMantainRatio();

            switch (anchor)
            {
                case Anchor.TopLeft:
                    return new PointF(x, y);
                case Anchor.TopCenter:
                    return new PointF(res.Width / 2 + x, 0 + y);
                case Anchor.TopRight:
                    return new PointF(res.Width - x - xOffset, y);
                case Anchor.MiddleLeft:
                    return new PointF(x, res.Height / 2 + y - yOffset / 2);
                case Anchor.MiddleCenter:
                    return new PointF(res.Width / 2 + x, res.Height / 2 + y - yOffset / 2);
                case Anchor.MiddleRight:
                    return new PointF(res.Width - x - xOffset, res.Height / 2 + y - yOffset/2);
                case Anchor.BottomLeft:
                    return new PointF(x, res.Height - y - yOffset);
                case Anchor.BottomCenter:
                    return new PointF(res.Width / 2 + x, res.Height - y - yOffset);
                case Anchor.BottomRight:
                    return new PointF(res.Width - x, res.Height - y - yOffset);
                default:
                    return PointF.Empty;
            }
        }

        internal enum Anchor
        {
            TopLeft = 0,
            TopCenter = 1,
            TopRight = 2,
            MiddleLeft = 3,
            MiddleCenter = 4,
            MiddleRight = 6,
            BottomLeft = 7,
            BottomCenter = 8,
            BottomRight = 9,
        }

        #endregion
    }

}