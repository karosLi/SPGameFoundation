using UnityEngine;

namespace SPF.Presentation
{
    public static class RenderObjects
    {
        /// <summary>
        /// Destroys a runtime-created asset (mesh, material, texture) in play mode and in edit mode (editor
        /// tools, EditMode tests), where <see cref="Object.Destroy(Object)"/> is not allowed.
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
