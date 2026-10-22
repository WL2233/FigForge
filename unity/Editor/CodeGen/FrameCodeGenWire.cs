// =============================================================================
// FigForge — post-compile frame wiring. The importer places a plain FigForgeFrame
// on each page and records the generated subclass name (frame.generatedType). Once
// that subclass compiles, this hook swaps the base component for the subclass and
// fills its typed [SerializeField] refs from the frame's element registry
// (FigForgeScreen) via the generated __WireFrame override.
//
// Runs on every script reload but is cheap + idempotent: a frame is upgraded once,
// the first reload after its generated type exists. Pending prefab paths survive reloads.
// =============================================================================

using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace FigForge
{
    internal static class FrameCodeGenWire
    {
        static bool _pending;

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            RequestUpgrade();
        }

        // The importer calls this after (re)building screens. When the generated code is
        // UNCHANGED, no compile follows the import — [DidReloadScripts] never fires — and
        // a rebuilt page would sit on the base FigForgeFrame forever (UIFrames.X casts fail,
        // resolving null). The type already exists in that case, so upgrading right away
        // works; when a compile IS pending the reload hook covers it. Idempotent.
        internal static void RequestUpgrade()
        {
            if (_pending) return;
            _pending = true;
            // Defer a tick so freshly compiled types + the scene are fully settled
            // before we mutate components.
            EditorApplication.delayCall += () => { _pending = false; UpgradePendingFrames(); };
        }

        const string PendingPrefabsKey = "FigForge.PendingPrefabWiring";

        internal static void QueuePrefab(string path)
        {
            var paths = new System.Collections.Generic.HashSet<string>(
                SessionState.GetString(PendingPrefabsKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            paths.Add(path);
            SessionState.SetString(PendingPrefabsKey, string.Join("\n", paths));
            RequestUpgrade();
        }

        static void UpgradePendingFrames()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RequestUpgrade();
                return;
            }
            var pending = SessionState.GetString(PendingPrefabsKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var remaining = new System.Collections.Generic.List<string>();
            foreach (var path in pending)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    bool ready = true;
                    foreach (var group in root.GetComponentsInChildren<FigForgeFrameElement>(true))
                        if (!string.IsNullOrEmpty(group.generatedType) && ResolveType(group.generatedType) == null) ready = false;
                    foreach (var frame in root.GetComponentsInChildren<FigForgeFrame>(true))
                        if (!string.IsNullOrEmpty(frame.generatedType) && ResolveType(frame.generatedType) == null) ready = false;
                    if (!ready) { remaining.Add(path); continue; }

                    foreach (var group in root.GetComponentsInChildren<FigForgeFrameElement>(true))
                        if (group.GetType() == typeof(FigForgeFrameElement) && !string.IsNullOrEmpty(group.generatedType))
                            if (UpgradeGroup(group, ResolveType(group.generatedType)) == null)
                                throw new InvalidOperationException("Could not upgrade prefab group: " + path);
                    foreach (var group in root.GetComponentsInChildren<FigForgeFrameElement>(true))
                        group.__WireGroup(group.GetComponentInParent<FigForgeScreen>(true));
                    foreach (var frame in root.GetComponentsInChildren<FigForgeFrame>(true))
                    {
                        if (frame.GetType() == typeof(FigForgeFrame) && !string.IsNullOrEmpty(frame.generatedType))
                        {
                            if (!UpgradeFrame(frame, ResolveType(frame.generatedType), out _, out _))
                                throw new InvalidOperationException("Could not upgrade prefab frame: " + path);
                        }
                        else frame.__WireFrame(frame.GetComponent<FigForgeScreen>());
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new System.IO.IOException("Could not save wired prefab: " + path);
                }
                catch (Exception ex) { remaining.Add(path); Debug.LogException(ex); }
                finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
            }
            SessionState.SetString(PendingPrefabsKey, string.Join("\n", remaining));
        }

        static FigForgeFrameElement UpgradeGroup(FigForgeFrameElement baseEl, Type t)
        {
            var go = baseEl.gameObject;
            string typeKey = baseEl.FigmaTypeKey;
            string genType = baseEl.generatedType;
            // FigForgeFrameElement is [DisallowMultipleComponent], so the base must go before the
            // subclass can be added. If the swap fails (AddComponent throws / yields an unexpected
            // type), re-add the base so the GameObject is never left WITHOUT an element component.
            UnityEngine.Object.DestroyImmediate(baseEl);
            FigForgeFrameElement comp = null;
            try { comp = go.AddComponent(t) as FigForgeFrameElement; }
            catch (Exception e) { Debug.LogException(e); }
            if (comp == null)
            {
                var restored = go.AddComponent<FigForgeFrameElement>();
                if (restored != null) { restored.ConfigureType(typeKey); restored.generatedType = genType; }
                return null;
            }
            comp.ConfigureType(typeKey);
            comp.generatedType = genType;
            return comp;
        }

        static bool UpgradeFrame(FigForgeFrame baseFrame, Type t, out UIFrameManager manager, out FigForgeFrame upgraded)
        {
            manager = null;
            upgraded = null;
            var go = baseFrame.gameObject;
            bool isShell = baseFrame.isShell;
            bool usesShell = baseFrame.usesShell;
            bool persistent = baseFrame.persistent;
            string shellKey = baseFrame.shellKey;
            string genType = baseFrame.generatedType;
            var reg = go.GetComponent<FigForgeScreen>();
            var mgr = baseFrame.GetComponentInParent<UIFrameManager>();
            int idx = mgr != null ? mgr.screens.IndexOf(baseFrame) : -1;
            bool wasInitial = mgr != null && mgr.initialScreen == baseFrame;

            // FigForgeFrame is [DisallowMultipleComponent], so the base must go before the
            // subclass can be added. If the swap fails (AddComponent throws / yields an unexpected
            // type), re-add the base so the page is never left WITHOUT a FigForgeFrame component
            // (which would orphan it from the manager and break UIFrames.X navigation).
            UnityEngine.Object.DestroyImmediate(baseFrame);
            FigForgeFrame comp = null;
            try { comp = go.AddComponent(t) as FigForgeFrame; }
            catch (Exception e) { Debug.LogException(e); }
            if (comp == null)
            {
                var restored = go.AddComponent<FigForgeFrame>();
                if (restored != null)
                {
                    restored.isShell = isShell;
                    restored.usesShell = usesShell;
                    restored.persistent = persistent;
                    restored.shellKey = shellKey;
                    restored.generatedType = genType;
                    if (mgr != null)
                    {
                        if (idx >= 0 && idx < mgr.screens.Count) mgr.screens[idx] = restored;
                        else if (!mgr.screens.Contains(restored)) mgr.Register(restored);
                        if (wasInitial) mgr.initialScreen = restored;
                        EditorUtility.SetDirty(mgr);
                    }
                }
                return false;
            }

            comp.isShell = isShell;
            comp.usesShell = usesShell;
            comp.persistent = persistent;
            comp.shellKey = shellKey;
            comp.generatedType = genType;
            comp.__WireFrame(reg);

            if (mgr != null)
            {
                if (idx >= 0 && idx < mgr.screens.Count) mgr.screens[idx] = comp;
                else if (!mgr.screens.Contains(comp)) mgr.Register(comp);
                if (wasInitial) mgr.initialScreen = comp;
                EditorUtility.SetDirty(mgr);
            }
            EditorUtility.SetDirty(comp);
            manager = mgr;
            upgraded = comp;
            return true;
        }

        static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
