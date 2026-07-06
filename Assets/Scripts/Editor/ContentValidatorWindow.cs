using System.Text;
using UnityEditor;
using UnityEngine;
using VoidRunner.Content;
using VoidRunner.Modding;

namespace VoidRunner.EditorTools
{
    /// <summary>
    /// Editor tool (menu: <b>VoidRunner ▸ Validate Content Packs</b>) that runs the same discovery
    /// and validation pipeline the game uses, and prints a report. Modders can point this at their
    /// pack and get precise errors/warnings before ever entering play mode.
    /// </summary>
    public sealed class ContentValidatorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _report = "Press \"Validate\" to scan StreamingAssets/ContentPacks.";

        [MenuItem("VoidRunner/Validate Content Packs")]
        public static void Open()
        {
            var w = GetWindow<ContentValidatorWindow>("VoidRunner Content");
            w.minSize = new Vector2(480, 320);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Content Pack Validator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scans Assets/StreamingAssets/ContentPacks using the exact loader the game uses.",
                MessageType.Info);

            if (GUILayout.Button("Validate", GUILayout.Height(30)))
            {
                _report = RunValidation();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static string RunValidation()
        {
            string root = System.IO.Path.Combine(Application.streamingAssetsPath, "ContentPacks");
            var result = PackDiscovery.DiscoverAndLoad(root, out var order);

            var sb = new StringBuilder();
            sb.AppendLine($"Scanned: {root}");
            sb.AppendLine($"Packs loaded (in order): {order.Count}");
            foreach (var p in order)
                sb.AppendLine($"  • {p.Manifest.id} v{p.Manifest.version} by {p.Manifest.author} ({p.Manifest.files.Count} file(s))");
            sb.AppendLine();
            sb.AppendLine($"Totals: {result.Registry.EnemyCount} enemies, {result.Registry.WeaponCount} weapons, " +
                          $"{result.Registry.RoomCount} rooms, {result.Registry.WaveCount} waves.");
            sb.AppendLine();

            if (result.Ok)
            {
                sb.AppendLine("RESULT: OK ✔");
                sb.AppendLine($"Content fingerprint: {ContentFingerprint.Compute(result.Registry)}");
            }
            else
            {
                sb.AppendLine("RESULT: ERRORS ✘");
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Errors:");
                foreach (var e in result.Errors) sb.AppendLine("  ✘ " + e);
            }
            if (result.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in result.Warnings) sb.AppendLine("  ⚠ " + w);
            }

            return sb.ToString();
        }
    }
}
