/// <summary>
/// Provides the FullScreenView class for rendering borderless full-screen game views.
///
/// Renders a camera to a borderless popup editor window, enabling multi-monitor VR
/// display setups within the Unity editor.
/// </summary>
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Renders a borderless full-screen game view in an editor window.
    /// </summary>
    public class FullScreenView : EditorWindow
    {
        /// <summary>The list of all active full-screen views.</summary>
        public static readonly List<FullScreenView> Views = new List<FullScreenView>();

        /// <summary>The entity ID of the camera to render.</summary>
        public EntityId cameraEntityId;

        /// <summary>The camera component for rendering.</summary>
        private Camera _camera;

        /// <summary>Determines whether the view is currently rendering.</summary>
        private bool _rendering = false;

        /// <summary>Adds this view to the views list when created.</summary>
        private void Awake()
        {
            Views.Add(this);
        }

        /// <summary>Registers the quit and play-mode handlers when enabled.</summary>
        /// <remarks>
        /// The play-mode subscription closes the view when Play Mode ends. Without it the borderless
        /// window outlives the scene restore that Unity performs on exit, which clears the camera's
        /// programmatically assigned <c>targetTexture</c> and leaves the OnGUI render path drawing
        /// against a null texture (visible as a stale afterimage plus a console error).
        /// </remarks>
        private void OnEnable()
        {
            EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
            EditorApplication.wantsToQuit += OnEditorWantsToQuit;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Unregisters the quit and play-mode handlers when disabled.</summary>
        private void OnDisable()
        {
            EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>Closes the view when Play Mode ends so the post-restore null targetTexture cannot fire.</summary>
        /// <param name="state">The current Play Mode transition.</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Close();
            }
        }

        /// <summary>Handles GUI events and renders the camera view.</summary>
        private void OnGUI()
        {
            Event currentEvent = Event.current;
            if (currentEvent.isMouse && currentEvent.button == 0 && !EditorApplication.isPlaying)
            {
                Close();
            }
            else if (currentEvent.type == EventType.Repaint)
            {
                if (_camera == null)
                {
                    _camera = (Camera)EditorUtility.EntityIdToObject(cameraEntityId);
                    if (_camera != null)
                    {
                        _camera.enabled = false;
                        int renderWidth = (int)position.width;
                        int renderHeight = (int)position.height;
                        _camera.targetTexture = new RenderTexture(
                            renderWidth,
                            renderHeight,
                            24,
                            RenderTextureFormat.ARGB32
                        );
                        _rendering = true;
                    }
                }
                if (_rendering && _camera != null && _camera.targetTexture != null)
                {
                    _camera.Render();
                    GUI.DrawTexture(
                        new Rect(0, 0, position.width, position.height),
                        _camera.targetTexture,
                        ScaleMode.ScaleToFit,
                        alphaBlend: false
                    );
                }
            }
        }

        /// <summary>Triggers repaint each frame when rendering.</summary>
        private void Update()
        {
            if ((_camera != null) && _rendering)
            {
                Repaint();
            }
        }

        /// <summary>Cleans up camera resources when destroyed.</summary>
        private void OnDestroy()
        {
            _rendering = false;
            if (_camera != null)
            {
                if (_camera.targetTexture != null)
                {
                    _camera.targetTexture.Release();
                    _camera.targetTexture = null;
                }
                _camera.enabled = true;
            }
            Views.Remove(this);
        }

        /// <summary>Closes this view when the editor is quitting.</summary>
        /// <returns>Always returns true to allow the editor to quit.</returns>
        private bool OnEditorWantsToQuit()
        {
            Close();
            return true;
        }
    }
}
#endif
