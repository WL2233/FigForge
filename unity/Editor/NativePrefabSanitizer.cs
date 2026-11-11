using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FigForge
{
    /// <summary>
    /// Converts the generated hierarchy to a portable Unity/TMP-only prefab.
    /// FigForge components are useful while constructing the hierarchy, but none are
    /// allowed to cross the prefab-save boundary.
    /// </summary>
    internal static class NativePrefabSanitizer
    {
        internal static void Convert(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            ReplaceCustomGraphics(root);
            RemoveFigForgeBehaviours(root);
            EnsureNativeOnly(root);
        }

        static void ReplaceCustomGraphics(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !IsFigForgeComponent(component.GetType()) || !(component is Graphic graphic))
                    continue;

                var go = component.gameObject;
                var color = graphic.color;
                var raycastTarget = graphic.raycastTarget;
                var maskable = graphic is MaskableGraphic maskableGraphic && maskableGraphic.maskable;

                UnityEngine.Object.DestroyImmediate(component);
                var image = go.GetComponent<Graphic>() as Image ?? go.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = raycastTarget;
                image.maskable = maskable;
                EditorUtility.SetDirty(image);
            }
        }

        static void RemoveFigForgeBehaviours(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && IsFigForgeComponent(component.GetType()))
                    UnityEngine.Object.DestroyImmediate(component);
            }
        }

        static void EnsureNativeOnly(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    throw new InvalidOperationException($"Generated object '{root.name}' contains a missing component.");
                if (IsFigForgeComponent(component.GetType()))
                    throw new InvalidOperationException(
                        $"Generated object '{component.name}' still contains FigForge component '{component.GetType().FullName}'.");
            }
        }

        static bool IsFigForgeComponent(Type type)
        {
            for (var current = type; current != null && current != typeof(Component); current = current.BaseType)
            {
                var ns = current.Namespace;
                if (!string.IsNullOrEmpty(ns)
                    && (ns == "FigForge" || ns.StartsWith("FigForge.", StringComparison.Ordinal)))
                    return true;
            }
            return false;
        }
    }
}
