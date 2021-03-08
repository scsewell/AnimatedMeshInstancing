using System.Linq;
using System.IO;

using UnityEditor;
using UnityEditor.Animations;

using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    /// <summary>
    /// A window used to bake animation data for instanced meshes.
    /// </summary>
    class InstancedAnimationWindow : EditorWindow
    {
        static readonly string k_AssetsPath = "Assets/";

        static class Contents
        {
            public static readonly GUIContent animator = new GUIContent("Animator", "The animator to bake.");
            public static readonly GUIContent directory = new GUIContent("Output Directory", "The directory to save the baked data in.");
            public static readonly GUILayoutOption directoryMinWidth = GUILayout.MinWidth(0f);
            public static readonly GUIContent directorySelector = new GUIContent("\u2299", "Select a directory.");
            public static readonly GUILayoutOption directorySelectorWidth = GUILayout.Width(22f);
        }

        [SerializeField]
        Animator m_animator = null;
        [SerializeField]
        string m_path = k_AssetsPath;

        [MenuItem("Framework/Animation/Instanced Animation Baker")]
        static void ShowWindow()
        {
            var window = GetWindow(typeof(InstancedAnimationWindow), false, "Instanced Animation Baker", true) as InstancedAnimationWindow;
            window.minSize = new Vector2(300, 350);
            window.maxSize = new Vector2(600, 2000);
            window.Show();
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

            m_animator = EditorGUILayout.ObjectField(Contents.animator, m_animator, typeof(Animator), true) as Animator;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            // allow picking the output directory
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(Contents.directory);

                m_path = GUILayout.TextField(m_path, Contents.directoryMinWidth);

                if (GUILayout.Button(Contents.directorySelector, EditorStyles.miniButton, Contents.directorySelectorWidth))
                {
                    var path = EditorUtility.SaveFolderPanel("Choose Output Directory", m_path, string.Empty);

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var index = path.IndexOf(k_AssetsPath);

                        if (index >= 0)
                        {
                            m_path = path.Substring(index);
                        }
                        else
                        {
                            m_path = k_AssetsPath;
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            // bake button
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledGroupScope(!CanBake(false)))
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Bake", GUILayout.Width(100)))
                {
                    Bake();
                }
            }

            // diplay messages explaining any issues
            CanBake(true);
        }

        void Bake()
        {
            var controller = m_animator.runtimeAnimatorController as AnimatorController;

            var config = new BakeConfig
            {
                animator = m_animator,
                animations = controller.animationClips,
                renderers = m_animator.GetComponentsInChildren<SkinnedMeshRenderer>(true),
            };

            var baker = new Baker(config);

            if (baker.Bake())
            {
                return;
            }

            baker.SaveBake(m_path);
        }

        bool CanBake(bool drawMessages)
        {
            var canBake = true;

            // we should never bake while playing
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (drawMessages)
                {
                    EditorGUILayout.HelpBox("Can't bake animations while in play mode.", MessageType.Warning);
                }
                canBake = false;
            }

            // validate the animator
            if (m_animator == null)
            {
                if (drawMessages)
                {
                    EditorGUILayout.HelpBox("An animator is required.", MessageType.Info);
                }
                canBake = false;
            }
            else
            {
                var renderers = m_animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                if (renderers.Length == 0)
                {
                    if (drawMessages)
                    {
                        EditorGUILayout.HelpBox("The selected animator has no skinned meshes!", MessageType.Warning);
                    }
                    canBake = false;
                }

                var controller = m_animator.runtimeAnimatorController as AnimatorController;

                if (controller == null)
                {
                    if (drawMessages)
                    {
                        EditorGUILayout.HelpBox("The selected animator must have an animator controller assigned!", MessageType.Warning);
                    }
                    canBake = false;
                }
                else
                {
                    var animations = controller.animationClips;

                    if (animations.Length == 0)
                    {
                        if (drawMessages)
                        {
                            EditorGUILayout.HelpBox("The assigned animator controller has no animations!", MessageType.Warning);
                        }
                        canBake = false;
                    }
                }
            }

            return canBake;
        }
    }
}
