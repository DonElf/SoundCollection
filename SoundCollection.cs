using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace SC {
    [CreateAssetMenu(menuName = "Scriptable/Sound Collection")]
    public class SoundCollection : ScriptableObject {
        public List<AudioClip> clips = new();

        public void PlayRandomSound(AudioSource source, float minVolume = 0.9f, float maxVolume = 1f, float minPitch = 0.9f, float maxPitch = 1.1f) {
            // Null check. Make sure you provide a source...
            if (source == null)
                return;

            // Play a random clip at a random pitch and volume.
            source.pitch = Random.Range(minPitch, maxPitch);
            source.PlayOneShot(clips[Random.Range(0, clips.Count)], Random.Range(minVolume, maxVolume));
        }
        public void PlayRandomSound(AudioSource source, AudioMixerGroup mixer, float minVolume = 0.9f, float maxVolume = 1f, float minPitch = 0.9f, float maxPitch = 1.1f) {
            // Null check. Make sure you provide a mixer...
            if (mixer == null)
                return;

            // Set the mixer group
            source.outputAudioMixerGroup = mixer;

            // Play the sound
            PlayRandomSound(source, minVolume, maxVolume, minPitch, maxPitch);
        }

        // Wrapped inside an if, so the private variables can't be used in a build.
#if UNITY_EDITOR
        // Each of the menu options.
        [MenuItem("Assets/Sound collection/Add to sound collections")]
        private static void AddClipsNormal() =>
            AddClips(true, false);

        [MenuItem("Assets/Sound collection/Add to sound collections (including duplicates)")]
        private static void AddClipsDuplicates() =>
            AddClips(false, false);

        [MenuItem("Assets/Sound collection/Add to sound collections (recursive)")]
        private static void AddClipsRecursive() =>
            AddClips(true, true);

        // And, the actual function that they all use.
        private static void AddClips(bool skipDuplicates, bool recursive) {
            // Get clips
            AudioClip[] selectedClips = Selection.GetFiltered<AudioClip>(recursive ? SelectionMode.DeepAssets : SelectionMode.Assets);

            // Get sound collections
            SoundCollection[] selectedGroups = Selection.GetFiltered<SoundCollection>(SelectionMode.Assets);

            // Add the sounds to each group
            foreach (SoundCollection group in selectedGroups) {
                // Record previous state for undo
                Undo.RecordObject(group, "Add clips");

                // If we're ignoring duplicates, iterate and check each clip. Otherwise, just add the range.
                if (skipDuplicates) {
                    foreach (AudioClip clip in selectedClips) {
                        if (!group.clips.Contains(clip))
                            group.clips.Add(clip);
                    }
                } else {
                    group.clips.AddRange(selectedClips);
                }

                // Mark dirty so it saves
                EditorUtility.SetDirty(group);
            }
        }

        // Validation functions, to grey out the option when it's not applicable.
        private static CheapHash32<Object> lastSelectionHash = 0; // Hash the last selection for fast comparison.
        private static bool lastResult = false; // The last result.
        private static bool lastRecursiveResult = false; // The last recursive result.
        private static readonly Dictionary<string, bool> directoryCache = new(); // Cache results for performance

        // Clear the cache when a file is imported or deleted.
        internal static void ClearDirectoryCache() {
            directoryCache.Clear();
        }

        // Also see SoundCollectionAssetPostprocessor 
        [InitializeOnLoadMethod]
        private static void Initialize() {
            AssetDatabase.importPackageCompleted += _ => ClearDirectoryCache();
            AssetDatabase.importPackageFailed += (_, __) => ClearDirectoryCache();
        }

        [MenuItem("Assets/Sound collection/Add to sound collections", true)]
        [MenuItem("Assets/Sound collection/Add to sound collections (including duplicates)", true)]
        private static bool AddClipsNormalValidation() =>
            AddClipsValidation(false);

        [MenuItem("Assets/Sound collection/Add to sound collections (recursive)", true)]
        private static bool AddClipsRecursiveValidation() =>
            AddClipsValidation(true);

        // Check if we need to revalidate
        private static bool AddClipsValidation(bool recursive) {
            // If the hashes are the same, don't recalculate.
            CheapHash32<Object> hash = new(Selection.objects);
            if (hash == lastSelectionHash)
                return recursive ? lastRecursiveResult : lastResult;

            // Set the new hash
            lastSelectionHash = hash;

            // Revalidate
            Revalidate();

            // And return.
            return recursive ? lastRecursiveResult : lastResult;
        }

        // The function for revalidation.
        private static void Revalidate() {
            // Null check
            var selection = Selection.objects;
            if (selection == null || selection.Length == 0) {
                lastResult = false;
                lastRecursiveResult = false;
                return;
            }

            // Variables to check for when we loop through.
            bool hasClip = false;
            bool hasClipRecursive = false;
            bool hasCollection = false;

            // Loop through each selected object
            foreach (var obj in selection) {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (obj is AudioClip) {
                    hasClip = true;
                    hasClipRecursive = true;
                } else if (obj is SoundCollection) {
                    hasCollection = true;
                } else if (!hasClipRecursive && Directory.Exists(path)) { 
                    // If it's a directory, recursively check for AudioClips inside. Cache results.
                    if (!directoryCache.TryGetValue(path, out bool containsAudio)) {
                        containsAudio = AssetDatabase.FindAssets("t:AudioClip", new[] { path }).Length > 0;
                        directoryCache[path] = containsAudio;
                    }

                    hasClipRecursive = containsAudio;
                }

                // return early if possible.
                if (hasClip && hasCollection) {
                    lastResult = true;
                    lastRecursiveResult = true;
                    return;
                }
            }

            // hasClip or hasCollection is false, so just check recursive.
            lastResult = false;
            lastRecursiveResult = hasCollection && hasClipRecursive;
        }
#endif
    }

#if UNITY_EDITOR
    public class SoundCollectionAssetPostprocessor : AssetPostprocessor {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths) {
            
            // Clear cache if any audio files or directories were affected
            if (deletedAssets.Length > 0 || movedAssets.Length > 0 || movedFromAssetPaths.Length > 0) {
                SoundCollection.ClearDirectoryCache();
            }
        }
    }

    // A fast, probably not-very-collision-resistant 32-bit (int) hashing method.
    // https://codeforces.com/blog/entry/85900 Neat.
    public class CheapHash32<T> {
        public int hash { get; private set; }

        // Empty default cosntructor.
        public CheapHash32() {
            hash = 0;
        }

        // Int constructor
        public CheapHash32(int h) {
            hash = h;
        }

        public CheapHash32(IEnumerable<T> input) {
            foreach (var obj in input) {
                if (obj == null)
                    continue;

                // Use GetInstanceID for unity objects, and GetHashCode for others.
                if (obj is UnityEngine.Object uobj)
                    hash ^= uobj.GetInstanceID();
                else
                    hash ^= obj.GetHashCode();
            }
        }

        public CheapHash32(T input) {
            // If its just one object, just get the instance ID or Hash Code. Kinda redundant.
            if (input is UnityEngine.Object uobj)
                hash ^= uobj.GetInstanceID();
            else
                hash ^= input.GetHashCode();
        }

        // I don't think I want this to be implicit...
        // public static implicit operator CheapHash32<T>(T input) { return new(input); }

        // Implicit int conversions.
        public static implicit operator CheapHash32<T>(int input) { return new(input); }
        public static implicit operator int(CheapHash32<T> hash32) { return hash32.hash; }
    }
#endif
}